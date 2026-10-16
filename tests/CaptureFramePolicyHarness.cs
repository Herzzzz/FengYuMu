using System;
using System.Drawing;
using MapleOverlay;
public static class CaptureFramePolicyHarness
{
    private static int count;
    private static void Check(bool result, string name) { if (!result) throw new Exception(name); count++; Console.WriteLine("PASS: " + name); }
    public static int Run()
    {
        count = 0;
        Check(CaptureFramePolicy.CounterTicks(15000001, 10000000) == 15000001, "QPC converts exactly at ten MHz");
        Check(CaptureFramePolicy.CounterTicks(1500000001, 1000000000) == 15000000, "high-frequency QPC preserves 100ns scale");
        Check(!CaptureFramePolicy.IsFresh(99,100), "queued pre-request frame is rejected");
        Check(CaptureFramePolicy.IsFresh(100,100) && CaptureFramePolicy.IsFresh(101,100), "only current request and later frames qualify");
        Check(!CaptureFramePolicy.IsFresh(1,0), "missing request timestamp fails closed");
        Rectangle crop;
        var client = new Rectangle(108,138,800,600);
        Check(CaptureFramePolicy.TryClientCrop(new Size(816,646),client,new Rectangle(100,100,816,646),Rectangle.Empty,out crop) && crop == new Rectangle(8,38,800,600), "measured visible window frame maps to client pixels");
        Check(CaptureFramePolicy.TryClientCrop(new Size(820,650),client,new Rectangle(100,100,816,646),new Rectangle(98,98,820,650),out crop) && crop == new Rectangle(10,40,800,600), "outer rectangle is used only when exact frame size matches");
        Check(!CaptureFramePolicy.TryClientCrop(new Size(810,640),client,new Rectangle(100,100,816,646),new Rectangle(98,98,820,650),out crop), "no guessed crop on DPI or size mismatch");
        Check(CaptureFramePolicy.TryClientCrop(new Size(800,600),new Rectangle(-800,50,800,600),new Rectangle(-800,50,800,600),Rectangle.Empty,out crop) && crop == new Rectangle(0,0,800,600), "negative monitor origin maps correctly");
        Check(!CaptureFramePolicy.TryClientCrop(new Size(800,600),new Rectangle(1,1,800,600),new Rectangle(0,0,800,600),Rectangle.Empty,out crop), "client outside captured target is rejected");
        Check(!CaptureFramePolicy.TryClientCrop(new Size(0,600),client,Rectangle.Empty,Rectangle.Empty,out crop), "empty frame rejected");
        Check(!CaptureFramePolicy.TryClientCrop(new Size(Int32.MaxValue,Int32.MaxValue),client,Rectangle.Empty,Rectangle.Empty,out crop), "overflowing frame bounds rejected");
        bool rejected = false; try { CaptureFramePolicy.CounterTicks(1,0); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "invalid clock frequency rejected");
        return count;
    }
}
