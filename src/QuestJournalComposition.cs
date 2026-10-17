using System;
using System.Collections.Generic;
using System.Drawing;

namespace MapleOverlay
{
    internal sealed partial class OverlayForm
    {
        private sealed class JournalPageView
        {
            internal Rectangle Area;
            internal RectangleF Content, Footer, Title;
            internal List<string> Pages;
            internal List<List<string>> PageLines;
            internal List<string> TitleLines;
            internal float FontSize, TitleSize;
            internal GraphicsUnit FontUnit;
            internal string FontFamily;
            internal Color Background;
            internal int Page;
            internal JournalLayoutPlan Layout;
        }
        private JournalPageView journalPageView;

        private bool TryPrepareJournalDraw(OverlayLabel label,Rectangle area,Rectangle extent,
            IList<Rectangle> values,Graphics measure,out SimpleDraw draw,out string reason)
        {
            draw=null;reason="invalid-journal-region";
            if(label.Journal==null || !extent.Contains(area) || area.Width<100 || area.Height<45 ||
                (long)area.Width*area.Height>800000 || label.PanelBounds!=label.SourceBounds)return false;
            foreach(Rectangle value in values)if(value.IntersectsWith(area)){reason="journal-live-value-conflict";return false;}
            string title=label.Journal.Title;
            string body=label.Journal.Body;
            if(body==null)body=label.Text;
            if(title==null)title="";
            JournalLayoutPlan plan=label.Journal.Layout;
            bool reused=plan!=null&&plan.Region==area;
            if(!reused)
                plan=QuestJournalLayout.Build(measure,area,label.Journal.FontHeight,title,body);
            if(plan==null){reason="journal-text-cannot-fit";return false;}
            Font font=plan.Type.CreateBodyFont();
            Font titleFont=plan.Type.CreateTitleFont(plan.TitleSize);
            int[] fill=new int[area.Width*area.Height];
            Color paper=label.Journal.Background.IsEmpty?Color.FromArgb(250,248,241):label.Journal.Background;
            int color=paper.ToArgb();
            for(int i=0;i<fill.Length;i++)fill[i]=color;
            draw=new SimpleDraw {Label=label,Content=plan.Body,Rows=new List<Rectangle>{area},Font=font,
                JournalTitleFont=titleFont,JournalLayout=plan,
                Surface=new ReconstructedTextSurface {Region=area,Pixels=fill,Ink=Color.FromArgb(45,45,45).ToArgb(),SourceHeight=plan.Type.BodySize},
                JournalPages=plan.Pages,JournalFooter=plan.Footer};
            Program.LogRuntimeEvent("quest-journal-layout","region="+area.Width+"x"+area.Height+" "+
                plan.Describe()+" maxLine="+plan.MaxLineWidth.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+
                " reused="+reused);
            reason="stage-owned-journal";return true;
        }

        // Draws pre-wrapped lines at the exact measured width. The overload that draws
        // at a point (rather than a layout rectangle) is deliberate: GDI+ cannot re-wrap
        // or clip the final glyph of a measured line, which was the right-edge cut.
        private static void DrawJournalPage(Graphics graphics,JournalLayoutPlan plan,int page,
            Font bodyFont,Font titleFont)
        {
            if(graphics==null || plan==null || plan.PageLines==null || page<0 || page>=plan.PageLines.Count)return;
            using(StringFormat format=QuestJournalLayout.TextFormat())
            using(SolidBrush ink=new SolidBrush(Color.FromArgb(45,45,45))) {
                graphics.SetClip(plan.Region);
                if(plan.TitleLines!=null && titleFont!=null) {
                    float titleTop=plan.Title.Top;
                    foreach(string titleLine in plan.TitleLines) {
                        if(titleLine.Length>0)graphics.DrawString(titleLine,titleFont,ink,
                            new PointF(plan.Title.Left,titleTop),format);
                        titleTop+=plan.TitleLineHeight;
                    }
                }
                float top=plan.Body.Top;
                foreach(string line in plan.PageLines[page]) {
                    if(line.Length>0)graphics.DrawString(line,bodyFont,ink,
                        new PointF(plan.Body.Left,top),format);
                    top+=plan.Type.LineHeight;
                }
                if(plan.PageCount>1 && !plan.Footer.IsEmpty)
                    using(Font small=new Font(plan.Type.Family,Math.Max(11,plan.Type.BodySize-3),
                        FontStyle.Regular,GraphicsUnit.Pixel)) {
                        graphics.DrawString("第 "+(page+1)+" / "+plan.PageCount+" 页 · Alt+PgUp / PgDn",
                            small,ink,new PointF(plan.Footer.Left,plan.Footer.Top+2),format);
                    }
                graphics.ResetClip();
            }
        }

        private bool ChangeQuestJournalPage(int direction)
        {
            JournalPageView view=journalPageView;
            if(view==null || simpleLayer==null || simpleLayerGeneration!=recognitionGeneration ||
                !visibleTranslation || view.Pages.Count<2 || view.Layout==null)return false;
            int next=Math.Max(0,Math.Min(view.Pages.Count-1,view.Page+direction));
            if(next==view.Page)return true;
            int[] pixels=ReadSimplePixels(simpleLayer);
            for(int y=view.Area.Top;y<view.Area.Bottom;y++)
            for(int x=view.Area.Left;x<view.Area.Right;x++)pixels[y*simpleLayer.Width+x]=view.Background.ToArgb();
            WriteSimplePixels(simpleLayer,pixels);
            using(Graphics graphics=Graphics.FromImage(simpleLayer))
            using(Font bodyFont=new Font(view.FontFamily,view.FontSize,FontStyle.Regular,view.FontUnit))
            using(Font titleFont=new Font(view.FontFamily,view.TitleSize,FontStyle.Bold,view.FontUnit))
                DrawJournalPage(graphics,view.Layout,next,bodyFont,titleFont);
            view.Page=next;Invalidate();
            Program.LogRuntimeEvent("quest-journal-page","page="+(next+1)+" pages="+view.Pages.Count);
            return true;
        }
    }
}
