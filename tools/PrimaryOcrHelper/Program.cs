using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace FengYuMu.StrongOcr
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding=new UTF8Encoding(false);Console.InputEncoding=new UTF8Encoding(false);
            if(args.Length!=2||args[0]!="--server"||(args[1]!="Small"&&args[1]!="Medium"&&args[1]!="Tiny"))return 2;
            try
            {
                using(StrongOcrEngine engine=new StrongOcrEngine(args[1]))
                {
                    Console.WriteLine("READY");Console.Out.Flush();string request;
                    while((request=Console.ReadLine())!=null)
                    {
                        Dictionary<string,object> response;
                        try
                        {
                            if(request.Length>32768)throw new ArgumentException("request-limit");
                            Dictionary<string,object> data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(request);
                            string path=(string)data["path"];
                            using(Image decoded=Image.FromFile(path))
                            {
                                if(decoded.Width<1||decoded.Height<1||(long)decoded.Width*decoded.Height>16000000)throw new ArgumentException("image-size");
                                using(Bitmap bitmap=new Bitmap(decoded.Width,decoded.Height,PixelFormat.Format32bppArgb))
                                {
                                    using(Graphics g=Graphics.FromImage(bitmap))g.DrawImageUnscaled(decoded,0,0);
                                    int stride=checked(bitmap.Width*4);byte[] pixels=new byte[checked(stride*bitmap.Height)];
                                    BitmapData locked=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                                    try{for(int y=0;y<bitmap.Height;y++)Marshal.Copy(IntPtr.Add(locked.Scan0,y*locked.Stride),pixels,y*stride,stride);}
                                    finally{bitmap.UnlockBits(locked);}
                                    response=ModelWorker.Recognize(engine,pixels,bitmap.Width,bitmap.Height,stride);
                                }
                            }
                        }
                        catch(Exception e){response=new Dictionary<string,object>{{"ok",false},{"error",e.GetType().Name}};}
                        string output=new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(response);
                        if(output.Length>1024*1024)output="{\"ok\":false,\"error\":\"output-limit\"}";
                        Console.WriteLine(output);Console.Out.Flush();
                    }
                }
                return 0;
            }catch{return 3;}
        }
    }
}
