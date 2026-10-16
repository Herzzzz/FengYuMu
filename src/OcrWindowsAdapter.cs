using System.Drawing;
namespace MapleOverlay { internal static class OcrWindowsAdapter {
        internal static OcrResult FromWindows(Windows.Media.Ocr.OcrResult raw)
        {
            OcrResult result=new OcrResult {Text=raw.Text};
            foreach(Windows.Media.Ocr.OcrLine line in raw.Lines)
            {
                OcrLine item=new OcrLine {Text=line.Text};
                foreach(Windows.Media.Ocr.OcrWord word in line.Words)
                    item.Words.Add(new OcrWord {Text=word.Text,BoundingRect=new RectangleF(
                        (float)word.BoundingRect.X,(float)word.BoundingRect.Y,
                        (float)word.BoundingRect.Width,(float)word.BoundingRect.Height)});
                result.Lines.Add(item);
            }
            return result;
        }


}}
