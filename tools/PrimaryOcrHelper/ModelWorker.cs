using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Sdcb.SimdPaddleOCR;

namespace FengYuMu.StrongOcr
{
    internal static class ModelWorker
    {
        internal static Dictionary<string,object> Recognize(StrongOcrEngine engine,byte[] pixels,int width,int height,int stride)
        {
            PaddleOcrResult result=engine.Recognize(pixels,width,height,stride);
            List<object> lines=new List<object>();
            foreach(PaddleOcrLine line in result.Lines)
            {
                if(lines.Count>=512)throw new InvalidOperationException("line-limit");
                float x=Math.Max(0,Math.Min(Math.Min(line.Box.X1,line.Box.X2),Math.Min(line.Box.X3,line.Box.X4)));
                float y=Math.Max(0,Math.Min(Math.Min(line.Box.Y1,line.Box.Y2),Math.Min(line.Box.Y3,line.Box.Y4)));
                float right=Math.Min(width,Math.Max(Math.Max(line.Box.X1,line.Box.X2),Math.Max(line.Box.X3,line.Box.X4)));
                float bottom=Math.Min(height,Math.Max(Math.Max(line.Box.Y1,line.Box.Y2),Math.Max(line.Box.Y3,line.Box.Y4)));
                if(right<=x||bottom<=y||String.IsNullOrWhiteSpace(line.Text))continue;
                List<object> words=new List<object>();
                MatchCollection tokens=Regex.Matches(line.Text,@"\S+");
                PaddleOcrCharacterBox[] chars=line.EstimateCharacterBoxes();
                int cursor=0;int[] indices=new int[chars.Length];
                for(int i=0;i<chars.Length;i++)
                {
                    string text=chars[i].Text;
                    if(String.IsNullOrEmpty(text)){indices[i]=-1;continue;}
                    int at=line.Text.IndexOf(text,cursor,StringComparison.Ordinal);
                    indices[i]=at;if(at>=0)cursor=at+text.Length;
                }
                foreach(Match token in tokens)
                {
                    float left=Single.MaxValue,top=Single.MaxValue,r=Single.MinValue,b=Single.MinValue;
                    for(int i=0;i<chars.Length;i++)
                    {
                        if(indices[i]<token.Index||indices[i]>=token.Index+token.Length||String.IsNullOrWhiteSpace(chars[i].Text))continue;
                        PaddleOcrCharacterBox c=chars[i];
                        left=Math.Min(left,Math.Min(Math.Min(c.X1,c.X2),Math.Min(c.X3,c.X4)));
                        top=Math.Min(top,Math.Min(Math.Min(c.Y1,c.Y2),Math.Min(c.Y3,c.Y4)));
                        r=Math.Max(r,Math.Max(Math.Max(c.X1,c.X2),Math.Max(c.X3,c.X4)));
                        b=Math.Max(b,Math.Max(Math.Max(c.Y1,c.Y2),Math.Max(c.Y3,c.Y4)));
                    }
                    if(left==Single.MaxValue)continue;
                    left=Math.Max(x,left);top=Math.Max(y,top);r=Math.Min(right,r);b=Math.Min(bottom,b);
                    if(r>left&&b>top)words.Add(Box(token.Value,left,top,r-left,b-top));
                }
                if(words.Count!=tokens.Count)continue;
                Dictionary<string,object> entry=Box(line.Text,x,y,right-x,bottom-y);
                entry["confidence"]=line.RecognitionScore;entry["words"]=words;
                lines.Add(entry);
            }
            return new Dictionary<string,object>{{"ok",true},{"lines",lines}};
        }
        private static Dictionary<string,object> Box(string text,float x,float y,float w,float h)
        {return new Dictionary<string,object>{{"text",text},{"x",x},{"y",y},{"width",w},{"height",h}};}
    }
}
