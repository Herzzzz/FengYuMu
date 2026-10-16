using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal static class QuestObjectiveSurface
    {
        // This only locates raw counter pixels. OCR confusions are not corrected
        // or displayed; names are translated and these pixels stay transparent.
        internal static Match Counter(string text)
        {return Regex.Match(text??"",@"(?<count>[0-9IlOo]+(?:\s*/\s*|\s+)[0-9IlOo]+)\s*$");}

        internal static List<RectangleF> Find(int[] pixels,int width,int height,RectangleF owner,float scale)
        {
            List<RectangleF> result=new List<RectangleF>();
            if(pixels==null||(long)width*height!=pixels.Length||scale<=0||!OverlayPaintSafety.IsFiniteRect(owner))return result;
            Rectangle bounds=Rectangle.Intersect(Rectangle.Ceiling(owner),new Rectangle(0,0,width,height));
            const int step=3;int columns=(bounds.Width+step-1)/step,rows=(bounds.Height+step-1)/step;
            if(columns<=0||rows<=0)return result;
            bool[] mask=new bool[columns*rows],seen=new bool[columns*rows];int[] queue=new int[mask.Length];
            for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)
                mask[y*columns+x]=Grey(pixels[Math.Min(bounds.Bottom-1,bounds.Top+y*step+1)*width+Math.Min(bounds.Right-1,bounds.Left+x*step+1)]);
            for(int start=0;start<mask.Length;start++) {
                if(!mask[start]||seen[start])continue;
                int head=0,tail=0,minX=columns,maxX=0,minY=rows,maxY=0;queue[tail++]=start;seen[start]=true;
                while(head<tail) {
                    int at=queue[head++],x=at%columns,y=at/columns;
                    minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                    for(int direction=0;direction<4;direction++) {
                        int nx=x+(direction==0?-1:direction==1?1:0),ny=y+(direction==2?-1:direction==3?1:0);
                        if(nx<0||nx>=columns||ny<0||ny>=rows)continue;int next=ny*columns+nx;
                        if(mask[next]&&!seen[next]){seen[next]=true;queue[tail++]=next;}
                    }
                }
                int w=(maxX-minX+1)*step,h=(maxY-minY+1)*step;
                if(w<140*scale||h<28*scale||tail*step*step<(long)w*h*.55)continue;
                result.Add(RectangleF.Intersect(owner,new RectangleF(bounds.Left+minX*step,bounds.Top+minY*step,w,h)));
            }
            return result;
        }
        private static bool Grey(int color)
        {
            int r=(color>>16)&255,g=(color>>8)&255,b=color&255;
            return r>=205&&r<=249&&g>=200&&g<=247&&b>=188&&b<=242&&
                Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<=35&&r>=g-5&&g>=b-3;
        }
    }

    internal sealed partial class TranslationStore
    {
        private readonly List<TranslationEntry> objectiveNames=new List<TranslationEntry>();
        private readonly Dictionary<string,TranslationEntry> objectiveExact=new Dictionary<string,TranslationEntry>(StringComparer.Ordinal);
        private readonly HashSet<string> objectiveAmbiguous=new HashSet<string>(StringComparer.Ordinal);
        private void BuildObjectiveNameIndex(List<TranslationEntry> entries)
        {
            objectiveNames.Clear();objectiveExact.Clear();objectiveAmbiguous.Clear();
            foreach(TranslationEntry entry in entries)
                if(entry.Category.StartsWith("怀旧服-道具",StringComparison.Ordinal)||entry.Category.StartsWith("怀旧服-怪物#",StringComparison.Ordinal)||
                    entry.Category.StartsWith("怀旧服-装备#",StringComparison.Ordinal)) {
                    objectiveNames.Add(entry);string key=CompactName(entry.English);TranslationEntry previous;
                    if(objectiveExact.TryGetValue(key,out previous)){if(previous.Chinese!=entry.Chinese)objectiveAmbiguous.Add(key);}
                    else objectiveExact.Add(key,entry);
                }
        }
        internal TranslationEntry ResolveObjectiveName(string text)
        {
            string key=CompactName(text);TranslationEntry best=null;float bestScore=0,second=0;
            if(objectiveAmbiguous.Contains(key))return null;
            if(objectiveExact.TryGetValue(key,out best))return best;
            best=null;
            foreach(KeyValuePair<string,TranslationEntry> pair in objectiveExact) {
                string candidate=pair.Key;TranslationEntry entry=pair.Value;
                if(candidate.Length==0||key.Length==0||candidate[0]!=key[0]||Math.Abs(candidate.Length-key.Length)>3||objectiveAmbiguous.Contains(candidate))continue;
                float score=candidate==key?1:
                    (Math.Min(candidate.Length,key.Length)>=8?TextSimilarity(key,candidate):0);
                if(score<.90f)continue;
                if(best!=null&&best.Chinese==entry.Chinese)continue;
                if(score>bestScore){second=bestScore;bestScore=score;best=entry;}else second=Math.Max(second,score);
            }
            return bestScore-second>=.08f?best:null;
        }
        private static string CompactName(string text)
        {return System.Text.RegularExpressions.Regex.Replace(Normalize(text),@"[^\p{L}\p{N}]","");}
    }
}
