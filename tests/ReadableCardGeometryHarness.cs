using System;
using System.Drawing;
using MapleOverlay;
public static class ReadableCardGeometryHarness
{
    private static int count;
    private static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
    public static int Run()
    {
        count=0;
        RectangleF client=new RectangleF(10,20,1200,800),anchor=new RectangleF(100,100,10,10);
        for(int i=0;i<10000;i++)
        {
            float width=10.0f+i/1000.0f,height=17.0f+i/10000.0f;
            SizeF measured=new SizeF(width,height);
            RectangleF card=SceneTranslationPolicy.CardBounds(anchor,measured,measured,client,560);
            if(card.Width-16 < width || card.Height-12 < height)
                throw new Exception("Allocated content is smaller than measured text: measured="+width+"x"+height+" content="+(card.Width-16)+"x"+(card.Height-12));
        }
        Check(true,"fractional glyph extents never lose space through padding arithmetic");
        var prose=SceneTranslationPolicy.CardBounds(anchor,new SizeF(3000,20),new SizeF(540.125f,317.125f),client,560);
        Check(prose.Height-12>=317.125f && prose.Width<=560,"wrapped prose retains measured height and width ceiling");
        var edge=SceneTranslationPolicy.CardBounds(new RectangleF(1200,790,5,5),new SizeF(100.125f,22.75f),new SizeF(100.125f,22.75f),client,560);
        Check(client.Contains(edge),"readable card remains inside game client near screen edge");
        Check(SceneTranslationPolicy.CardBounds(anchor,new SizeF(3000,20),new SizeF(540,900),client,560).IsEmpty,"genuinely oversized text still fails rather than clipping");
        Check(SceneTranslationPolicy.CardBounds(anchor,new SizeF(100,20),new SizeF(100,20),client,Single.NaN).IsEmpty,"invalid width budget is rejected");
        Check(!SceneTranslationPolicy.CanMergeSemanticBlocks("task-name","task-body",anchor,anchor),"task name and prose remain separate");
        Check(!SceneTranslationPolicy.CanMergeSemanticBlocks("interface","task-objective",anchor,anchor),"task header and objective remain separate");
        Check(!SceneTranslationPolicy.CanMergeSemanticBlocks("task-objective","task-objective",anchor,new RectangleF(100,130,10,10)),"separate objective rows cannot merge even with identical text");
        Check(SceneTranslationPolicy.CanMergeSemanticBlocks("task-objective","task-objective",anchor,anchor),"duplicate readings of one objective can converge");
        Check(SceneTranslationPolicy.CanMergeSemanticBlocks("task-body","task-body",anchor,new RectangleF(100,130,10,10)),"wrapped prose can retain one body block");
        Check(SceneTranslationPolicy.CanMergeSemanticBlocks("detail-body","task-body",anchor,anchor),"generic and task-specific readings of the same body can converge");
        Check(!SceneTranslationPolicy.CanMergeSemanticBlocks("detail-body","task-body",anchor,new RectangleF(100,130,10,10)),"distinct body regions remain independent");
        return count;
    }
}
