using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;

namespace MapleOverlay
{
    internal sealed class CharacterFieldBand {internal RectangleF Bounds;internal int ColorKind;}
    internal static class CharacterFieldSurface
    {
        private static int Kind(int color){int r=(color>>16)&255,g=(color>>8)&255,b=color&255;
            if(r>=150&&r<=245&&g>=34&&g<=150&&b>=65&&b<=190&&r>=g+38&&b>=g+10)return 1;
            if(b>=110&&b<=230&&g>=62&&g<=190&&r>=32&&r<=160&&b>=r+20&&b>=g+8)return 2;
            if(r>=110&&g>=130&&b>=25&&b<=135&&g>=b+40&&r>=b+30&&Math.Abs(r-g)<85)return 3;return 0;}
        internal static List<CharacterFieldBand> Find(int[] pixels,int width,int height,float scale)
        {
            List<CharacterFieldBand> bands=new List<CharacterFieldBand>();const int step=2;int columns=(width+step-1)/step,rows=(height+step-1)/step;
            int[] mask=new int[columns*rows],queue=new int[columns*rows];bool[] seen=new bool[mask.Length];
            for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)mask[y*columns+x]=Kind(pixels[Math.Min(height-1,y*step)*width+Math.Min(width-1,x*step)]);
            for(int first=0;first<mask.Length;first++){if(mask[first]==0||seen[first])continue;int head=0,tail=0,minX=columns,minY=rows,maxX=0,maxY=0;queue[tail++]=first;seen[first]=true;
                while(head<tail){int at=queue[head++],x=at%columns,y=at/columns;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);
                    for(int direction=0;direction<4;direction++){int nx=x+(direction==0?-1:direction==1?1:0),ny=y+(direction==2?-1:direction==3?1:0);if(nx<0||nx>=columns||ny<0||ny>=rows)continue;int next=ny*columns+nx;if(!seen[next]&&mask[next]==mask[first]){seen[next]=true;queue[tail++]=next;}}}
                RectangleF box=new RectangleF(minX*step,minY*step,(maxX-minX+1)*step,(maxY-minY+1)*step);
                if(box.Width<40*scale||box.Width>200*scale||box.Height<13*scale||box.Height>65*scale||tail*step*step<box.Width*box.Height*.58)continue;
                bands.Add(new CharacterFieldBand{Bounds=box,ColorKind=mask[first]});}
            return bands;
        }
        internal static List<CharacterFieldBand> Column(List<CharacterFieldBand> all,int kind,RectangleF anchor,int count,float scale,bool right)
        {
            List<CharacterFieldBand> best=null;float distance=Single.MaxValue;
            foreach(CharacterFieldBand seed in all){RectangleF b=seed.Bounds;if(seed.ColorKind!=kind||b.Top<anchor.Bottom||b.Top>anchor.Bottom+200*scale||(!right&&Math.Abs(b.Left-anchor.Left)>120*scale)||(right&&b.Left<anchor.Right+10*scale))continue;
                List<CharacterFieldBand> run=all.FindAll(delegate(CharacterFieldBand value){RectangleF r=value.Bounds;return value.ColorKind==kind&&Math.Abs(r.Left-b.Left)<8*scale&&Math.Abs(r.Width-b.Width)<15*scale&&r.Top>=b.Top&&r.Top<b.Top+650*scale;});
                run.Sort(delegate(CharacterFieldBand a,CharacterFieldBand c){return a.Bounds.Top.CompareTo(c.Bounds.Top);});if(run.Count!=count)continue;
                bool regular=true;for(int i=1;i<run.Count;i++)if(run[i].Bounds.Top-run[i-1].Bounds.Bottom>18*scale)regular=false;if(!regular)continue;
                float valueDistance=Math.Abs(b.Left-anchor.Left)+Math.Abs(b.Top-anchor.Bottom);if(valueDistance<distance){best=run;distance=valueDistance;}}
            return best;
        }
    }
    internal sealed partial class TranslationStore
    {
        internal string TranslateJobName(string name)
        {
            string key=Normalize(name),found="";
            foreach(List<TranslationEntry> bucket in buckets.Values)foreach(TranslationEntry entry in bucket)
                if(entry.Category.StartsWith("怀旧服-职业#",StringComparison.Ordinal)&&entry.Normalized==key){if(found.Length>0&&found!=entry.Chinese)return "";found=entry.Chinese;}
            return found;
        }
    }
    internal sealed partial class OverlayForm
    {
        private void AddCharacterFieldSurfaces(List<OverlayLabel> output,List<OcrLine> all,Bitmap prepared,float scale)
        {
            if(prepared==null)return;List<CharacterFieldBand> bands=null;int fields=0,jobs=0;
            foreach(OcrLine header in CharacterCaptionRows(all)){string key=Regex.Replace(TranslationStore.Normalize(header.Text),@"\s+","");bool stats=key=="characterstat"||key=="characterstats",info=key=="characterinfo"||key=="characterinformation";if(!stats&&!info)continue;
                if(bands==null)bands=CharacterFieldSurface.Find(ReadSimplePixels(prepared),prepared.Width,prepared.Height,scale);
                RectangleF anchor=GetOcrLineBounds(header);List<CharacterFieldBand> main=stats?CharacterFieldSurface.Column(bands,1,anchor,8,scale,false):null;
                if(main==null)main=CharacterFieldSurface.Column(bands,1,anchor,stats?7:4,scale,info);
                string[] names=stats?(main!=null&&main.Count==8?new string[]{"名称","职业","等级","家族","生命值","魔法值","经验","人气"}:new string[]{"名称","职业","等级","生命值","魔法值","经验","人气"}):new string[]{"等级","职业","人气","家族"};
                RectangleF job=RectangleF.Empty;
                if(main!=null) {
                    for(int i=0;i<main.Count;i++){AddCharacterFieldSurface(output,main[i].Bounds,names[i],scale);fields++;}
                    job=main[1].Bounds;
                }
                // A missed/noisy cell must not disable every other measured field.
                // Independently confirmed keys use their own coloured cell, with the
                // character caption and local geometry as context. No values or
                // player names are used to guess a label.
                foreach(CharacterFieldBand band in bands) {
                    RectangleF box=band.Bounds;
                    if(box.Top<anchor.Bottom||box.Top>anchor.Bottom+750*scale||
                        box.Left<anchor.Left-20*scale||box.Left>anchor.Right+500*scale)continue;
                    if((band.ColorKind==1||band.ColorKind==3)&&Math.Abs(box.Left-anchor.Left)>120*scale&&!info)continue;
                    if(band.ColorKind==2&&box.Left<anchor.Right)continue;
                    string english="";
                    foreach(OcrLine line in all)foreach(OcrWord word in line.Words)
                        if(box.Contains(new PointF(word.BoundingRect.Left+word.BoundingRect.Width/2,
                            word.BoundingRect.Top+word.BoundingRect.Height/2)))
                            english+=(english.Length>0?" ":"")+word.Text;
                    string chinese=CharacterFieldKey(english,band.ColorKind,stats);
                    if(chinese.Length==0)continue;
                    AddCharacterFieldSurface(output,box,chinese,scale);fields++;
                    if(TranslationStore.Normalize(english)=="job")job=box;
                }
                if(!job.IsEmpty)
                foreach(OcrLine line in all)foreach(OcrWord word in line.Words){RectangleF box=word.BoundingRect;if(box.Top+box.Height/2<job.Top||box.Top+box.Height/2>job.Bottom||box.Left<job.Right||box.Left>job.Right+280*scale)continue;
                    string chinese=translations.TranslateJobName(word.Text);if(chinese.Length==0)continue;
                    RectangleF slot=RectangleF.FromLTRB(box.Left,Math.Max(job.Top+2*scale,box.Top-2*scale),box.Right,Math.Min(job.Bottom-2*scale,box.Bottom+3*scale));
                    AddCharacterFieldSurface(output,slot,chinese,scale);output[output.Count-1].SemanticRole="character-job";jobs++;}
                if(!stats)continue;
                RectangleF greenAnchor=main==null?RectangleF.Empty:new RectangleF(main[0].Bounds.Left,main[main.Count-1].Bounds.Bottom-1,main[0].Bounds.Width,1);
                List<CharacterFieldBand> attributes=main==null?null:CharacterFieldSurface.Column(bands,3,greenAnchor,4,scale,false);
                if(attributes!=null){string[] labels={"力量","敏捷","智力","运气"};for(int i=0;i<4;i++){AddCharacterFieldSurface(output,attributes[i].Bounds,labels[i],scale);fields++;}}
                List<CharacterFieldBand> detail=CharacterFieldSurface.Column(bands,2,anchor,10,scale,true);
                if(detail==null)detail=CharacterFieldSurface.Column(bands,2,anchor,9,scale,true);
                if(detail!=null){string[] labels=detail.Count==9?new string[]{"攻击力","物理防御力","魔法攻击力","魔法防御力","命中率","回避率","手技","移动速度","跳跃力"}:new string[]{"攻击力","物理防御力","魔法攻击力","魔法防御力","命中率","回避率","暴击率","暴击伤害","移动速度","跳跃力"};for(int i=0;i<detail.Count;i++){AddCharacterFieldSurface(output,detail[i].Bounds,labels[i],scale);fields++;}}
            }
            Program.LogRuntimeEvent("character-field-coverage","fields="+fields+" jobs="+jobs);
        }
        private static List<OcrLine> CharacterCaptionRows(List<OcrLine> source)
        {
            List<OcrLine> captions=new List<OcrLine>();
            // A Windows OCR line can include the caption and another column. Scope
            // the anchor to the original caption words, not that whole merged line.
            foreach(OcrLine line in source) {
                for(int i=0;i<line.Words.Count;i++) {
                    OcrWord word=line.Words[i];string key=TranslationStore.Normalize(word.Text).Replace(" ","");
                    if(key=="characterstat"||key=="characterstats"||key=="characterinfo"||key=="characterinformation") {
                        OcrLine caption=new OcrLine{Text=key};caption.Words.Add(word);captions.Add(caption);break;
                    }
                    if(key!="character"||i+1>=line.Words.Count)continue;
                    OcrWord next=line.Words[i+1];string suffix=TranslationStore.Normalize(next.Text);
                    if(suffix!="stat"&&suffix!="stats"&&suffix!="info"&&suffix!="information")continue;
                    if(Math.Abs(word.BoundingRect.Top-next.BoundingRect.Top)>Math.Max(word.BoundingRect.Height,next.BoundingRect.Height)*.5f)continue;
                    OcrLine joined=new OcrLine{Text="character"+suffix};joined.Words.Add(word);joined.Words.Add(next);captions.Add(joined);break;
                }
            }
            return captions;
        }
        private static string CharacterFieldKey(string text,int kind,bool stats)
        {
            string key=Regex.Replace(TranslationStore.Normalize(text),@"[^a-z0-9]","");
            if(kind==1) {
                switch(key) {
                    case "name":return stats?"名称":"";
                    case "job":return "职业";
                    case "level":return "等级";
                    case "hp":return stats?"生命值":"";
                    case "mp":return stats?"魔法值":"";
                    case "exp":return stats?"经验":"";
                    case "fame":return "人气";
                    case "guild":return "家族";
                }
            }
            if(!stats)return "";
            if(kind==3) {
                switch(key) {case "str":return "力量";case "dex":return "敏捷";case "int":return "智力";case "luk":return "运气";}
            }
            if(kind==2) {
                switch(key) {
                    case "attack":return "攻击力";case "weapondef":case "weapondefense":return "物理防御力";
                    case "magic":case "magicattack":return "魔法攻击力";case "magicdef":case "magicdefense":return "魔法防御力";
                    case "accuracy":return "命中率";case "evasion":case "avoidability":return "回避率";
                    case "critrate":case "criticalrate":return "暴击率";case "critdamage":case "criticaldamage":return "暴击伤害";
                    case "speed":return "移动速度";case "jump":return "跳跃力";
                }
            }
            return "";
        }
        private void AddCharacterFieldSurface(List<OverlayLabel> output,RectangleF raw,string chinese,float scale)
        {
            RectangleF box=OverlaySpan(raw,scale);box.Inflate(-1,-1);if(box.Width<8||box.Height<8)return;
            output.RemoveAll(delegate(OverlayLabel label){return box.IntersectsWith(label.HasSourceBounds?label.SourceBounds:label.Bounds)&&label.SemanticRole!="task-journal";});
            output.Add(new OverlayLabel{Text=chinese,Bounds=box,SourceBounds=box,HasSourceBounds=true,SourceLineBounds=new List<RectangleF>{box},PanelBounds=box,
                Confidence=LabelConfidence.High,SemanticRole="character-field",MatchSource="character-field-surface"});
        }
    }
}
