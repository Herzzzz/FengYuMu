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
            internal RectangleF Content, Footer;
            internal List<string> Pages;
            internal float FontSize;
            internal Color Background;
            internal int Page;
        }
        private JournalPageView journalPageView;

        private bool TryPrepareJournalDraw(OverlayLabel label,Rectangle area,Rectangle extent,
            IList<Rectangle> values,Graphics measure,out SimpleDraw draw,out string reason)
        {
            draw=null;reason="invalid-journal-region";
            if(label.Journal==null || !extent.Contains(area) || area.Width<100 || area.Height<45 ||
                (long)area.Width*area.Height>800000 || label.PanelBounds!=label.SourceBounds)return false;
            foreach(Rectangle value in values)if(value.IntersectsWith(area)){reason="journal-live-value-conflict";return false;}
            RectangleF content=area;content.Inflate(-5,-4);
            float size=Math.Max(14,Math.Min(20,label.Journal.FontHeight));
            Font font=new Font("Microsoft YaHei",size,FontStyle.Regular,GraphicsUnit.Pixel);
            List<string> pages=PaginateJournal(measure,label.Text,font,content.Size);
            RectangleF footer=RectangleF.Empty;
            if(pages==null){font.Dispose();reason="journal-text-cannot-paginate";return false;}
            if(pages.Count>1) {
                footer=new RectangleF(content.Left,content.Bottom-20,content.Width,20);
                content.Height-=24;
                pages=PaginateJournal(measure,label.Text,font,content.Size);
                if(pages==null){font.Dispose();reason="journal-text-cannot-paginate";return false;}
            }
            int[] fill=new int[area.Width*area.Height];int color=label.Journal.Background.ToArgb();
            for(int i=0;i<fill.Length;i++)fill[i]=color;
            draw=new SimpleDraw {Label=label,Content=content,Rows=new List<Rectangle>{area},Font=font,
                Surface=new ReconstructedTextSurface {Region=area,Pixels=fill,Ink=Color.FromArgb(45,45,45).ToArgb(),SourceHeight=size},
                JournalPages=pages,JournalFooter=footer};
            reason="stage-owned-journal";return true;
        }

        private static List<string> PaginateJournal(Graphics graphics,string text,Font font,SizeF area)
        {
            if(String.IsNullOrWhiteSpace(text) || text.Length>4096 || area.Width<30 || area.Height<font.Size*1.5f)return null;
            List<string> pages=new List<string>();
            using(StringFormat format=SimpleTextFormat())
            while(text.Length>0) {
                int fitted,lines;graphics.MeasureString(text,font,area,format,out fitted,out lines);
                if(fitted<=0)return null;
                int take=Math.Min(text.Length,fitted);
                if(take<text.Length && take>0 && Char.IsHighSurrogate(text[take-1]))take--;
                if(take<=0)return null;
                pages.Add(text.Substring(0,take));text=text.Substring(take);
                if(pages.Count>128)return null;
            }
            return pages;
        }

        private static void DrawJournalPage(Graphics graphics,List<string> pages,int page,Font font,
            RectangleF content,RectangleF footer)
        {
            using(StringFormat format=SimpleTextFormat())
            using(SolidBrush ink=new SolidBrush(Color.FromArgb(45,45,45))) {
                graphics.SetClip(content);graphics.DrawString(pages[page],font,ink,content,format);graphics.ResetClip();
                if(pages.Count>1)using(Font small=new Font("Microsoft YaHei",12,FontStyle.Regular,GraphicsUnit.Pixel)) {
                    graphics.SetClip(footer);graphics.DrawString("第 "+(page+1)+" / "+pages.Count+" 页 · Alt+PgUp / PgDn",small,ink,footer,format);graphics.ResetClip();
                }
            }
        }

        private bool ChangeQuestJournalPage(int direction)
        {
            JournalPageView view=journalPageView;
            if(view==null || simpleLayer==null || simpleLayerGeneration!=recognitionGeneration ||
                !visibleTranslation || view.Pages.Count<2)return false;
            int next=Math.Max(0,Math.Min(view.Pages.Count-1,view.Page+direction));
            if(next==view.Page)return true;
            int[] pixels=ReadSimplePixels(simpleLayer);
            for(int y=view.Area.Top;y<view.Area.Bottom;y++)
            for(int x=view.Area.Left;x<view.Area.Right;x++)pixels[y*simpleLayer.Width+x]=view.Background.ToArgb();
            WriteSimplePixels(simpleLayer,pixels);
            using(Graphics graphics=Graphics.FromImage(simpleLayer))
            using(Font font=new Font("Microsoft YaHei",view.FontSize,FontStyle.Regular,GraphicsUnit.Pixel))
                DrawJournalPage(graphics,view.Pages,next,font,view.Content,view.Footer);
            view.Page=next;Invalidate();
            Program.LogRuntimeEvent("quest-journal-page","page="+(next+1)+" pages="+view.Pages.Count);return true;
        }
    }
}
