using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Sdcb.SimdPaddleOCR;

namespace FengYuMu.StrongOcr
{
    // Standalone strong-OCR helper. It never touches the game process: it only reads one cropped
    // PNG that the caller already produced and writes UTF-8 JSON Lines on stdout. Diagnostics go to
    // stderr; the process never shows a dialog.
    internal static class Program
    {
        private const int ExitOk = 0;
        private const int ExitArguments = 2;
        private const int ExitImage = 3;
        private const int ExitModel = 4;
        private const int ExitRecognition = 5;

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); }
            catch { }
            try
            {
                if (args.Length == 1 && args[0] == "--selftest-json")
                {
                    SelfTestJson();
                    return ExitOk;
                }
                if (args.Length != 1)
                {
                    Console.Error.WriteLine("usage: StrongOcrHelper <cropped.png>");
                    return ExitArguments;
                }
                if (!File.Exists(args[0]))
                {
                    Console.Error.WriteLine("image not found: " + args[0]);
                    return ExitImage;
                }
                return Recognize(args[0]);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("fatal: " + error.GetType().Name + ": " + error.Message);
                return ExitRecognition;
            }
        }

        private static string Serialize(object value)
        {
            return new JavaScriptSerializer().Serialize(value);
        }

        // Escaping proof: quotes, backslashes, CJK, tab and newline must all survive as JSON.
        private static void SelfTestJson()
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["text"] = "quote \" backslash \\ tab \t newline \n 中文 引号 \"结束\"";
            payload["confidence"] = 0.5;
            payload["x"] = 1;
            payload["y"] = 2;
            payload["width"] = 3;
            payload["height"] = 4;
            Console.WriteLine(Serialize(payload));
            Dictionary<string, object> summary = new Dictionary<string, object>();
            summary["summary"] = true;
            summary["ok"] = true;
            summary["lines"] = 1;
            Console.WriteLine(Serialize(summary));
        }

        private static int Recognize(string imagePath)
        {
            Stopwatch watch = Stopwatch.StartNew();
            StrongOcrEngine engine;
            try { engine = new StrongOcrEngine(); }
            catch (Exception error)
            {
                Console.Error.WriteLine("model bundle failed: " + error.Message);
                return ExitModel;
            }
            try
            {
                using (engine)
                {
                    long loadMs = watch.ElapsedMilliseconds;
                    using (Bitmap bitmap = new Bitmap(imagePath))
                    {
                        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        byte[] pixels = new byte[data.Stride * bitmap.Height];
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                        bitmap.UnlockBits(data);
                        long start = watch.ElapsedMilliseconds;
                        PaddleOcrResult result = engine.Recognize(pixels, bitmap.Width, bitmap.Height, data.Stride);
                        long runMs = watch.ElapsedMilliseconds - start;
                        int emitted = 0;
                        foreach (PaddleOcrLine line in result.Lines)
                        {
                            float x1 = Math.Min(Math.Min(line.Box.X1, line.Box.X2), Math.Min(line.Box.X3, line.Box.X4));
                            float y1 = Math.Min(Math.Min(line.Box.Y1, line.Box.Y2), Math.Min(line.Box.Y3, line.Box.Y4));
                            float x2 = Math.Max(Math.Max(line.Box.X1, line.Box.X2), Math.Max(line.Box.X3, line.Box.X4));
                            float y2 = Math.Max(Math.Max(line.Box.Y1, line.Box.Y2), Math.Max(line.Box.Y3, line.Box.Y4));
                            Dictionary<string, object> item = new Dictionary<string, object>();
                            item["text"] = line.Text;
                            item["confidence"] = line.RecognitionScore;
                            item["x"] = (int)Math.Round(x1);
                            item["y"] = (int)Math.Round(y1);
                            item["width"] = (int)Math.Round(x2 - x1);
                            item["height"] = (int)Math.Round(y2 - y1);
                            item["box"] = new object[] {
                                new object[] { line.Box.X1, line.Box.Y1 },
                                new object[] { line.Box.X2, line.Box.Y2 },
                                new object[] { line.Box.X3, line.Box.Y3 },
                                new object[] { line.Box.X4, line.Box.Y4 } };
                            Console.WriteLine(Serialize(item));
                            emitted++;
                        }
                        Dictionary<string, object> summary = new Dictionary<string, object>();
                        summary["summary"] = true;
                        summary["ok"] = true;
                        summary["lines"] = emitted;
                        summary["load_ms"] = loadMs;
                        summary["run_ms"] = runMs;
                        summary["image_width"] = bitmap.Width;
                        summary["image_height"] = bitmap.Height;
                        summary["peak_working_set_mb"] = Math.Round(
                            Process.GetCurrentProcess().PeakWorkingSet64 / 1024.0 / 1024.0, 1);
                        Console.WriteLine(Serialize(summary));
                        return ExitOk;
                    }
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("recognition failed: " + error.GetType().Name + ": " + error.Message);
                return ExitRecognition;
            }
        }
    }
}
