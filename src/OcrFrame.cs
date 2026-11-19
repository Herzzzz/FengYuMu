using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Web.Script.Serialization;

namespace MapleOverlay
{
    // Engine-neutral geometry. Paddle word bounds come from its CTC alignment,
    // never from uniformly dividing a line or guessing numeric values.
    internal sealed class OcrWord
    {
        internal string Text;
        internal RectangleF BoundingRect;
    }
    internal sealed class OcrLine
    {
        internal string Text;
        internal readonly List<OcrWord> Words = new List<OcrWord>();
    }
    internal sealed class OcrResult
    {
        internal string Text;
        internal readonly List<OcrLine> Lines = new List<OcrLine>();

        internal static OcrResult ParsePaddle(string json, Size frame)
        {
            if(String.IsNullOrEmpty(json)||json.Length>1024*1024||frame.Width<=0||frame.Height<=0)
                throw new FormatException("OCR output size");
            JavaScriptSerializer serializer=new JavaScriptSerializer {MaxJsonLength=1024*1024,RecursionLimit=24};
            Dictionary<string,object> root=serializer.DeserializeObject(json) as Dictionary<string,object>;
            return FromPayload(root,frame);
        }
        internal static OcrResult FromPayload(Dictionary<string,object> root,Size frame)
        {
            if(frame.Width<=0||frame.Height<=0)throw new FormatException("OCR frame");
            if(root==null||!root.ContainsKey("ok")||!(root["ok"] is bool)||!(bool)root["ok"])
                throw new InvalidOperationException("本地 Paddle 识别失败，请检查运行组件和模型。");
            object raw;
            if(!root.TryGetValue("lines",out raw)||!(raw is object[])||((object[])raw).Length>512)
                throw new FormatException("OCR lines");
            OcrResult result=new OcrResult();StringBuilder text=new StringBuilder();
            foreach(object value in (object[])raw)
            {
                Dictionary<string,object> line=value as Dictionary<string,object>;
                if(line==null)throw new FormatException("OCR line");
                string source=GetText(line,"text");
                double confidence=StrongOcrClient.NormalizeConfidence(Number(line,"confidence"));
                if(confidence<StrongOcrClient.MinimumConfidence)continue;
                RectangleF span=Box(line,frame);
                object words;
                if(!line.TryGetValue("words",out words)||!(words is object[])||((object[])words).Length>4096)
                    throw new FormatException("OCR word alignment");
                OcrLine item=new OcrLine {Text=source};StringBuilder reconstructed=new StringBuilder();
                foreach(object wordValue in (object[])words)
                {
                    Dictionary<string,object> word=wordValue as Dictionary<string,object>;
                    if(word==null)throw new FormatException("OCR word");
                    string token=GetText(word,"text");RectangleF bounds=Box(word,frame);
                    RectangleF tolerance=span;tolerance.Inflate(2,2);
                    if(!tolerance.Contains(bounds))throw new FormatException("OCR word outside line");
                    item.Words.Add(new OcrWord {Text=token,BoundingRect=bounds});
                    if(reconstructed.Length>0)reconstructed.Append(' ');reconstructed.Append(token);
                }
                // Missing alignment cannot authorize an erase region for a partial match.
                if(item.Words.Count==0)continue;
                if(Compact(reconstructed.ToString())!=Compact(source))throw new FormatException("OCR word/text mismatch");
                result.Lines.Add(item);if(text.Length>0)text.AppendLine();text.Append(source);
            }
            result.Text=text.ToString();return result;
        }
        private static string Compact(string s)
        {StringBuilder b=new StringBuilder();foreach(char c in s)if(!Char.IsWhiteSpace(c))b.Append(c);return b.ToString();}
        private static string GetText(Dictionary<string,object> data,string key)
        {object raw;if(!data.TryGetValue(key,out raw)||!(raw is string)||String.IsNullOrWhiteSpace((string)raw)||((string)raw).Length>4096)throw new FormatException("OCR text");return (string)raw;}
        private static double Number(Dictionary<string,object> data,string key)
        {object raw;if(!data.TryGetValue(key,out raw)||raw is bool||raw is string||raw==null)throw new FormatException("OCR number");double n=Convert.ToDouble(raw,System.Globalization.CultureInfo.InvariantCulture);if(Double.IsNaN(n)||Double.IsInfinity(n))throw new FormatException("OCR finite number");return n;}
        private static RectangleF Box(Dictionary<string,object> d,Size frame)
        {double x=Number(d,"x"),y=Number(d,"y"),w=Number(d,"width"),h=Number(d,"height");if(x<0||y<0||w<=0||h<=0||x+w>frame.Width+0.01||y+h>frame.Height+0.01)throw new FormatException("OCR bounds");return new RectangleF((float)x,(float)y,(float)w,(float)h);}
    }
}
