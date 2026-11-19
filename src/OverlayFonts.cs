using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MapleOverlay
{
    // The client draws its English UI with the embedded Noto Sans KR Medium and its
    // Chinese with Noto Sans SC Medium (Data/UI/Font/Font_000.wz, LanguageFont.img).
    // A subset of the SC face is embedded in this assembly, so overlay text can use the
    // same typeface as the game without installing anything or reading game files.
    internal static class OverlayFonts
    {
        private const string ResourceName = "MapleOverlay.NotoSansSC.ttf";
        private static PrivateFontCollection collection;
        private static string bodyFamily = "SimSun";

        internal static string BodyFamily
        {
            get { EnsureLoaded(); return bodyFamily; }
        }

        internal static void EnsureLoaded()
        {
            if (collection != null) return;
            collection = new PrivateFontCollection();
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream(ResourceName))
                {
                    if (stream == null) return;
                    byte[] bytes = new byte[stream.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int step = stream.Read(bytes, read, bytes.Length - read);
                        if (step <= 0) break;
                        read += step;
                    }
                    if (read <= 0) return;
                    // AddMemoryFont keeps using the buffer for the collection's lifetime,
                    // so it is deliberately never freed.
                    IntPtr memory = Marshal.AllocCoTaskMem(read);
                    Marshal.Copy(bytes, 0, memory, read);
                    collection.AddMemoryFont(memory, read);
                    if (collection.Families.Length > 0)
                        bodyFamily = collection.Families[0].Name;
                    // Compare against the system UI face; it is hinted for Windows and can
                    // look sharper than an unhinted web font at 12-13 px.
                    if (Environment.GetEnvironmentVariable("FYM_OVERLAY_FACE") == "yahei")
                        bodyFamily = "Microsoft YaHei";
                }
            }
            catch (Exception)
            {
                bodyFamily = "SimSun";
            }
        }
    }
}
