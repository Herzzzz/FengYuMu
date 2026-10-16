using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MapleOverlay
{
    internal sealed class PictureTranslation : IDisposable
    {
        internal Bitmap Image;
        internal string RecognizedText;
        internal int OcrLines,Matched,Painted;
        public void Dispose(){if(Image!=null){Image.Dispose();Image=null;}}
    }
    internal sealed partial class OverlayForm
    {
        private readonly bool imageTestOnly,ownsPrimaryOcr;
        private readonly PrimaryOcrClient primaryOcr;
        private Size imageTestSize;
        private bool imageBackendDisposed;
        // The hidden picture renderer uses the image's pixel extent, independent
        // of monitor size. Normal game capture continues to use the real client.
        private new Rectangle ClientRectangle
        {get{return imageTestOnly&&!imageTestSize.IsEmpty?new Rectangle(Point.Empty,imageTestSize):base.ClientRectangle;}}
        internal void ShowImageTest()
        {
            HideTranslation();
            if(ImageTestForm.Current!=null&&!ImageTestForm.Current.IsDisposed)
            {ImageTestForm.Current.Show();ImageTestForm.Current.Activate();return;}
            ImageTestForm test=new ImageTestForm(primaryOcr,translations);test.Show();test.Activate();
        }
        internal async Task<PictureTranslation> TranslatePictureAsync(Bitmap source,CancellationToken token)
        {
            if(!imageTestOnly)throw new InvalidOperationException("Picture backend only");
            if(source==null||(long)source.Width*source.Height>16000000||source.Width>8192||source.Height>8192)
                throw new ArgumentException("请选择不超过 8192 像素边长、1600 万像素的图片。");
            if(processing)throw new InvalidOperationException("上一张图片仍在结束，请稍后重试。");
            processing=true;recognitionGeneration++;ClearFusionAssets();
            imageTestSize=source.Size;requestFocusBounds=RectangleF.Empty;
            Rectangle screen=new Rectangle(Bounds.Left,Bounds.Top,source.Width,source.Height);
            captureBounds=screen;gameBounds=screen;
            captureTextRows.Clear();captureTextRowsOverflow=false;
            Stopwatch watch=Stopwatch.StartNew();
            Program.LogRuntimeEvent("picture-test-start","engine="+primaryOcr.EngineLabel);
            try
            {
                float scale;OcrResult result;List<OverlayLabel> found;
                using(Bitmap prepared=PrepareForOcr(source,out scale,false,1800.0f))
                {
                    result=await RecognizeAsync(prepared,token);token.ThrowIfCancellationRequested();
                    found=BuildLabels(result,scale,prepared,LabelBuildScope.Discovery);
                }
                MergeAdjacentLongLabels(found);MergeOverlappingSameTextLabels(found);
                MergeSameTextLabels(found);RemoveContainedLabels(found);
                token.ThrowIfCancellationRequested();
                using(token.Register(delegate { recognitionGeneration++; }))
                    if(!await PrepareSimpleFusionAsync(source,screen,found))throw new OperationCanceledException();
                token.ThrowIfCancellationRequested();
                PictureTranslation output=new PictureTranslation {Image=(Bitmap)source.Clone(),
                    RecognizedText=result.Text,OcrLines=result.Lines.Count,Matched=found.Count};
                try
                {
                    if(simpleLayer!=null)
                    {
                        using(Graphics g=Graphics.FromImage(output.Image))g.DrawImageUnscaled(simpleLayer,0,0);
                        output.Painted=simpleReady.Count;
                    }
                    token.ThrowIfCancellationRequested();
                    Program.LogRuntimeEvent("picture-test","engine="+primaryOcr.EngineLabel+
                        " lines="+output.OcrLines+" matched="+output.Matched+" painted="+output.Painted+" elapsedMs="+watch.ElapsedMilliseconds);
                    return output;
                }catch{output.Dispose();throw;}
            }
            finally{processing=false;ClearFusionAssets();}
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                if(ownsPrimaryOcr&&primaryOcr!=null)primaryOcr.Dispose();
                if(imageTestOnly&&!imageBackendDisposed)
                {
                    imageBackendDisposed=true;shuttingDown=true;
                    gamepadTimer.Dispose();continuousTranslationTimer.Dispose();mapleWindowTimer.Dispose();displayedSceneTimer.Dispose();
                    mapleWindows.Dispose();tray.Dispose();ClearFusionAssets();
                }
            }
            base.Dispose(disposing);
        }
    }
    internal sealed class ImageTestForm : Form
    {
        internal static ImageTestForm Current;
        internal static bool IsActiveTest {get{return Current!=null&&!Current.IsDisposed&&Current.Visible&&Current.ContainsFocus;}}
        internal static void ForwardF8(){if(Current!=null&&!Current.IsDisposed)Current.ToggleRecognition();}
        private readonly PictureBox picture=new PictureBox();
        private readonly Label status=new Label();
        private readonly TextBox recognized=new TextBox();
        private readonly Button run=new Button(),originalButton=new Button();
        private readonly ComboBox models=new ComboBox();
        private readonly OverlayForm backend;
        private readonly PrimaryOcrClient client;
        private readonly bool ownsClient;
        private Bitmap original;
        private PictureTranslation result;
        private CancellationTokenSource cancellation;
        private bool busy;
        private int ticket;
        internal ImageTestForm(PrimaryOcrClient shared,TranslationStore store)
        {
            Current=this;ownsClient=shared==null;client=shared??new PrimaryOcrClient(AppDomain.CurrentDomain.BaseDirectory);
            backend=new OverlayForm(true,client,store);
            Text="枫语幕 v4.0 · 本地图片 OCR 测试";ClientSize=new Size(1120,760);MinimumSize=new Size(760,540);
            Font=new Font("Microsoft YaHei UI",9);KeyPreview=true;
            FlowLayoutPanel bar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=44,Padding=new Padding(6)};
            Button open=new Button{Text="打开图片…",Width=110,Height=28};open.Click+=delegate{OpenPicture();};
            run.Text="识别并翻译（F8）";run.Width=170;run.Height=28;run.Click+=delegate{ToggleRecognition();};
            originalButton.Text="查看原图";originalButton.Width=110;originalButton.Height=28;
            originalButton.Click+=delegate{picture.Image=original;};
            models.DropDownStyle=ComboBoxStyle.DropDownList;models.Width=230;
            if(backend.WindowsOcrAvailable)models.Items.Add("Windows");
            foreach(string tier in new string[]{"Medium","Small","Tiny"})if(client.HasTier(tier))models.Items.Add(tier);
            models.SelectedItem=client.EngineLabel;
            models.SelectedIndexChanged+=delegate{
                if(busy)return;client.SetEngine((string)models.SelectedItem);ClearResult();status.Text="已切换模型，按 F8 重新识别。";
            };
            bar.Controls.AddRange(new Control[]{open,run,originalButton,new Label{Text="识别模型",AutoSize=true,Padding=new Padding(0,6,0,0)},models});
            SplitContainer split=new SplitContainer{Dock=DockStyle.Fill,Orientation=Orientation.Vertical,Size=new Size(1120,670),SplitterDistance=820};
            picture.Dock=DockStyle.Fill;picture.SizeMode=PictureBoxSizeMode.Zoom;picture.BackColor=Color.FromArgb(38,38,42);
            recognized.Dock=DockStyle.Fill;recognized.Multiline=true;recognized.ReadOnly=true;recognized.ScrollBars=ScrollBars.Both;
            split.Panel1.Controls.Add(picture);split.Panel2.Controls.Add(recognized);
            status.Dock=DockStyle.Bottom;status.Height=46;status.Text="打开保存的游戏截图即可测试，无需登录游戏。图片只在本地识别；F8 识别 → 清除 → 重新识别。";
            Controls.Add(split);Controls.Add(status);Controls.Add(bar);
            FormClosed+=delegate{ticket++;if(cancellation!=null)cancellation.Cancel();ClearResult();picture.Image=null;if(original!=null){original.Dispose();original=null;}backend.Dispose();if(ownsClient)client.Dispose();if(Current==this)Current=null;};
        }
        private void OpenPicture()
        {
            using(OpenFileDialog picker=new OpenFileDialog {Filter="图片|*.png;*.jpg;*.jpeg;*.bmp",CheckFileExists=true,Multiselect=false})
            {
                if(picker.ShowDialog(this)!=DialogResult.OK)return;
                try
                {
                    using(Image decoded=Image.FromFile(picker.FileName))
                    {
                        if(decoded.Width>8192||decoded.Height>8192||(long)decoded.Width*decoded.Height>16000000)throw new ArgumentException("图片过大，请使用单张游戏窗口截图。");
                        Bitmap next=new Bitmap(decoded.Width,decoded.Height,PixelFormat.Format32bppArgb);
                        using(Graphics g=Graphics.FromImage(next))g.DrawImageUnscaled(decoded,0,0);
                        ticket++;if(cancellation!=null)cancellation.Cancel();picture.Image=null;ClearResult();if(original!=null)original.Dispose();original=next;picture.Image=original;
                        recognized.Clear();status.Text="图片已打开。点击识别并翻译或按 F8。";
                    }
                }catch(Exception e){status.Text="打开图片失败："+e.Message;}
            }
        }
        private void ClearResult(){picture.Image=original;if(result!=null){result.Dispose();result=null;}}
        private async void ToggleRecognition()
        {
            if(busy){if(cancellation!=null)cancellation.Cancel();ticket++;status.Text="已取消；等待本次任务结束后再按 F8。";return;}
            if(result!=null){ClearResult();status.Text="译文已清除；再次按 F8 重新识别。";return;}
            if(original==null){OpenPicture();return;}
            busy=true;models.Enabled=false;int request=++ticket;
            cancellation=new CancellationTokenSource();CancellationTokenSource own=cancellation;
            status.Text="本地识别中（"+client.EngineLabel+"）；再次按 F8 可取消本次结果。";
            try
            {
                using(Bitmap copy=(Bitmap)original.Clone())
                {
                    PictureTranslation next=await backend.TranslatePictureAsync(copy,own.Token);
                    if(IsDisposed||request!=ticket||own.IsCancellationRequested){next.Dispose();return;}
                    result=next;picture.Image=result.Image;recognized.Text=result.RecognizedText;
                    status.Text="识别 "+result.OcrLines+" 行 · 匹配 "+result.Matched+" 条 · 实际融合 "+result.Painted+" 条。右侧是 OCR 原文，可区分漏识别与匹配/绘制失败。";
                }
            }
            catch(OperationCanceledException){if(!IsDisposed)status.Text="已取消；再次按 F8 可重新识别。";}
            catch(Exception e){if(!IsDisposed)status.Text="识别失败："+e.Message;}
            finally{busy=false;if(!IsDisposed)models.Enabled=true;if(Object.ReferenceEquals(cancellation,own))cancellation=null;own.Dispose();}
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {if(keyData==Keys.F8){ToggleRecognition();return true;}return base.ProcessCmdKey(ref msg,keyData);}
    }
}
