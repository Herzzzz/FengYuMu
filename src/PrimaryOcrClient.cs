using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MapleOverlay
{
    internal sealed class PrimaryOcrClient : IDisposable
    {
        // New primary engine: Windows OCR previously had no interruptible per-call
        // bound. The optional StrongOcrClient's original 2500ms limit is unchanged.
        internal const int InitializationTimeoutMilliseconds=15000;
        internal const int RecognitionTimeoutMilliseconds=60000;
        private readonly string folder,tempRoot;
        private CaptureProcessSession session;
        private readonly object state=new object();
        private bool disposed;
        private string tier;
        private bool windowsSelected=true;
        internal bool UseWindows {get{lock(state)return windowsSelected;}}
        internal string EngineLabel {get{lock(state)return windowsSelected?"Windows":tier;}}
        internal void SetEngine(string value)
        {
            if(value!="Windows"){SetTier(value);lock(state)windowsSelected=false;return;}
            lock(state){if(disposed)throw new ObjectDisposedException("PrimaryOcrClient");if(session!=null)session.Dispose();session=null;windowsSelected=true;}
        }
        internal string Tier {get {lock(state)return tier;}}
        internal PrimaryOcrClient(string root)
        {
            folder=Path.Combine(root,"PrimaryOcr");tempRoot=Path.Combine(root,"运行临时","PrimaryOcr");
            foreach(string name in new string[]{"Medium","Small","Tiny"})if(HasTier(name)){tier=name;break;}
            if(tier==null)tier="Small";
        }
        internal bool HasTier(string value)
        {return value=="Small"||value=="Medium"||value=="Tiny" ? File.Exists(Path.Combine(folder,"Sdcb.SimdPaddleOCR.Models.ChineseV6"+value+".dll")):false;}
        internal void SetTier(string value)
        {
            if(!HasTier(value))throw new InvalidOperationException("本包未安装该模型。");
            lock(state){if(disposed)throw new ObjectDisposedException("PrimaryOcrClient");if(tier==value)return;if(session!=null)session.Dispose();session=null;tier=value;}
        }
        internal static bool Validate(string directory)
        {
            try
            {
                string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                string manifest=Path.Combine(root,"model-manifest.tsv");if(!File.Exists(manifest))return false;
                HashSet<string> names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(string line in File.ReadAllLines(manifest))
                {
                    if(line.Length==0||line.StartsWith("#",StringComparison.Ordinal))continue;
                    string[] p=line.Split('\t');if(p.Length!=2||p[1].Length!=64)return false;
                    string name=p[0].Replace('\\','/');
                    string full=Path.GetFullPath(Path.Combine(root,name.Replace('/',Path.DirectorySeparatorChar)));
                    if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!names.Add(name)||!File.Exists(full)||
                        (File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0||UpdateFileTransaction.Hash(full)!=p[1])return false;
                }
                foreach(string name in new string[]{"PrimaryOcrHelper.exe","Sdcb.SimdPaddleOCR.dll","Sdcb.SimdPaddleOCR.ModelProvider.dll"})if(!names.Contains(name))return false;
                bool model=false;foreach(string t in new string[]{"Small","Medium","Tiny"})model|=names.Contains("Sdcb.SimdPaddleOCR.Models.ChineseV6"+t+".dll");
                foreach(string full in Directory.GetFiles(root,"*",SearchOption.AllDirectories))if(full!=manifest&&!names.Contains(full.Substring(root.Length).Replace(Path.DirectorySeparatorChar,'/')))return false;
                return model;
            }catch{return false;}
        }
        private async Task<CaptureProcessSession> GetSessionAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();lock(state){if(disposed)throw new ObjectDisposedException("PrimaryOcrClient");if(session!=null)return session;}
            if(!await Task.Run(delegate{return Validate(folder);},token))
                throw new InvalidOperationException("Paddle 模型组件缺失或校验失败，请完整解压运行包。");
            lock(state)
            {
                if(disposed)throw new ObjectDisposedException("PrimaryOcrClient");
                if(session==null)session=new CaptureProcessSession(new ProcessStartInfo{
                    FileName=Path.Combine(folder,"PrimaryOcrHelper.exe"),Arguments="--server "+tier,
                    WorkingDirectory=folder,UseShellExecute=false,CreateNoWindow=true,
                    RedirectStandardInput=true,RedirectStandardOutput=true,
                    StandardOutputEncoding=new System.Text.UTF8Encoding(false)},InitializationTimeoutMilliseconds,RecognitionTimeoutMilliseconds);
                return session;
            }
        }
        // Framework Process.StandardInput can use a legacy console code page.
        // Keep requests ASCII so Chinese installation paths survive UTF-8 helpers.
        internal static string AsciiJson(string json)
        {
            System.Text.StringBuilder result=new System.Text.StringBuilder(json.Length);
            foreach(char value in json)
                if(value>127)result.Append("\\u").Append(((int)value).ToString("x4",System.Globalization.CultureInfo.InvariantCulture));
                else result.Append(value);
            return result.ToString();
        }
        internal async Task WarmAsync()
        {CaptureProcessSession active=await GetSessionAsync(CancellationToken.None);await active.SendAsync(null,CancellationToken.None);}
        internal async Task<OcrResult> RecognizeAsync(Bitmap bitmap,CancellationToken token)
        {
            if(bitmap==null||bitmap.Width<1||bitmap.Height<1||(long)bitmap.Width*bitmap.Height>16000000)throw new ArgumentException("图片尺寸无效或超过 1600 万像素。");
            token.ThrowIfCancellationRequested();CaptureProcessSession active=await GetSessionAsync(token);
            Directory.CreateDirectory(tempRoot);string path=Path.Combine(tempRoot,Guid.NewGuid().ToString("N")+".png");
            try
            {
                bitmap.Save(path,ImageFormat.Png);token.ThrowIfCancellationRequested();
                string request=AsciiJson(new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"path",path}}));
                string json=await active.SendAsync(request,token);token.ThrowIfCancellationRequested();
                OcrResult result=OcrResult.ParsePaddle(json,bitmap.Size);
                Program.LogRuntimeEvent("primary-ocr","engine=paddle-v6 tier="+Tier+" lines="+result.Lines.Count);
                return result;
            }
            catch(TimeoutException){throw new TimeoutException("Paddle 识别超时；可取消并在图片测试中切换较小模型。");}
            catch(IOException){throw new InvalidOperationException("Paddle 模型进程未能完成识别。请确认使用 64 位 Windows、已安装 .NET Framework 4.8，并完整解压全部组件。");}
            finally{try{File.Delete(path);}catch{}}
        }
        public void Dispose(){lock(state){if(disposed)return;disposed=true;if(session!=null)session.Dispose();session=null;}}
    }
}
