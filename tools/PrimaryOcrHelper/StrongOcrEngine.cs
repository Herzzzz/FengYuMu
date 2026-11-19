using System;
using System.IO;
using System.Reflection;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.ModelProvider;

namespace FengYuMu.StrongOcr
{
    // Shared by the Windows PNG helper and the cloud screenshot regression runner.
    // Both feed the same BGRA pixels into the same models and recognition options.
    internal sealed class StrongOcrEngine : IDisposable
    {
        private readonly PaddleOcrModelSet models;
        private readonly PaddleOcrAll ocr;

        internal StrongOcrEngine(string tier)
        {
            PaddleOcrModelBundle bundle = FindBundle(tier);
            using (Stream det = bundle.Detection.OpenRead())
            using (Stream cls = bundle.Classification == null ? null : bundle.Classification.OpenRead())
            using (Stream rec = bundle.Recognition.OpenRead())
            using (Stream dict = bundle.Dictionary == null ? null : bundle.Dictionary.OpenRead())
                models = PaddleOcrModelSet.Load(det, cls, rec, dict);
            try { ocr = new PaddleOcrAll(models, new PaddleOcrOptions { UseDirectionClassification = false, DetIntraOpThreads=2, LineWorkerCount=2 }); }
            catch { models.Dispose(); throw; }
        }

        internal PaddleOcrResult Recognize(byte[] pixels, int width, int height, int stride)
        {
            if (pixels == null || width < 1 || height < 1 || stride < (long)width * 4 ||
                (long)stride * height > pixels.LongLength)
                throw new ArgumentException("Invalid BGRA image dimensions");
            return ocr.Run(pixels, width, height, true, stride, ImagePixelFormat.Bgra32);
        }

        private static PaddleOcrModelBundle FindBundle(string tier)
        {
            Type type = Type.GetType("Sdcb.SimdPaddleOCR.Models.ChineseV6"+tier+".ChineseV6"+tier+"Models, " +
                "Sdcb.SimdPaddleOCR.Models.ChineseV6"+tier);
            if (type == null) throw new InvalidOperationException("Small model assembly not found");
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
                if (property.PropertyType == typeof(PaddleOcrModelBundle))
                    return (PaddleOcrModelBundle)property.GetValue(null, null);
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (method.ReturnType == typeof(PaddleOcrModelBundle) && method.GetParameters().Length == 0)
                    return (PaddleOcrModelBundle)method.Invoke(null, null);
            throw new InvalidOperationException("no static PaddleOcrModelBundle on " + type.FullName);
        }

        public void Dispose()
        {
            ocr.Dispose();
            models.Dispose();
        }
    }
}
