using System;
using System.Collections.Generic;
using System.Drawing;
using MapleOverlay;
public static class R6PanelGeometryHarness
{
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
 static PanelTextAnchor A(string key,float x,float y,float w,float h){return new PanelTextAnchor{Key=key,Bounds=new RectangleF(x,y,w,h)};}
 static List<PanelTextAnchor> Quest(float scale){var a=new List<PanelTextAnchor>{A("available",100,100,90,20),A("in progress",210,100,100,20),A("completed",330,100,100,20),A("quest summary",510,280,160,24),A("forfeit",900,800,80,24)};foreach(var p in a)p.Bounds=new RectangleF(p.Bounds.X*scale,p.Bounds.Y*scale,p.Bounds.Width*scale,p.Bounds.Height*scale);return a;}
 public static int Run(){count=0;
  Check(PanelRecognitionPolicy.CaptionKind("world map")==VerifiedPanelKind.WorldMap,"world map is a dedicated caption");
  Check(PanelRecognitionPolicy.CaptionKind("character info")==VerifiedPanelKind.Information,"character info is separate from statistics");
  Check(PanelRecognitionPolicy.CaptionKind("game settings")==VerifiedPanelKind.Settings,"settings caption is recognized without changing generic vocabulary filters");
  Check(PanelRecognitionPolicy.CaptionKind("my options are open")==VerifiedPanelKind.None,"ordinary prose is not a window caption");
  var caption=new RectangleF(120,55,100,16);var actual=new RectangleF(100,40,300,350);
  Check(PanelRecognitionPolicy.MeasuredScope(caption,1,1200,900,VerifiedPanelKind.Information,null).IsEmpty,"caption alone cannot invent panel extent");
  Check(PanelRecognitionPolicy.MeasuredScope(caption,1,1200,900,VerifiedPanelKind.Information,new List<RectangleF>{actual})==actual,"measured frame bounds are retained byte for byte");
  Check(PanelRecognitionPolicy.MeasuredScope(caption,1,1200,900,VerifiedPanelKind.Information,new List<RectangleF>{new RectangleF(0,0,1000,850),actual})==actual,"smallest containing measured panel prevents a broad neighboring scope");
  Check(PanelRecognitionPolicy.Owns(actual,new RectangleF(150,100,80,16)),"an entirely contained row belongs to this panel");
  Check(!PanelRecognitionPolicy.Owns(actual,new RectangleF(380,100,60,16)),"row crossing a panel edge is rejected even when its center is nearby");
  Check(!PanelRecognitionPolicy.Owns(actual,new RectangleF(450,100,80,16)),"neighboring application/panel text cannot borrow scope");
  Check(PanelRecognitionPolicy.MeasuredScope(new RectangleF(float.NaN,0,10,10),1,1200,900,VerifiedPanelKind.Information,new List<RectangleF>{actual}).IsEmpty,"invalid caption coordinates fail closed");
  Check(PanelRecognitionPolicy.MeasuredScope(caption,0,1200,900,VerifiedPanelKind.Information,new List<RectangleF>{actual}).IsEmpty,"invalid OCR scale fails closed");
  var measured=QuestDetailGeometry.Measure(Quest(1),1,new Size(2000,1080));
  Check(!measured.IsEmpty&&measured.Left==442&&measured.Right==992,"tabs summary and footer jointly establish the selected detail pane");
  Check(measured.Contains(new RectangleF(520,160,230,20)),"selected blue-header title remains in scope");
  Check(measured.Contains(new RectangleF(520,470,400,180)),"visible task prose remains in scope");
  Check(!measured.Contains(new RectangleF(140,180,230,20)),"left quest list cannot select another task's body");
  Check(!measured.Contains(new RectangleF(1700,160,200,20)),"side tracker cannot lend task identity");
  Check(!measured.Contains(new RectangleF(520,900,400,40)),"chat below the journal is excluded");
  var twice=QuestDetailGeometry.Measure(Quest(2),2,new Size(4000,2160));
  Check(twice==new RectangleF(measured.X*2,measured.Y*2,measured.Width*2,measured.Height*2),"DPI/OCR scaling preserves the measured semantic boundary");
  var missing=Quest(1);missing.RemoveAt(4);Check(QuestDetailGeometry.Measure(missing,1,new Size(2000,1080)).IsEmpty,"missing footer does not manufacture a trusted detail crop");
  missing=Quest(1);missing.RemoveAt(3);Check(QuestDetailGeometry.Measure(missing,1,new Size(2000,1080)).IsEmpty,"missing summary remains unverified");
  missing=Quest(1);missing.RemoveRange(1,2);Check(QuestDetailGeometry.Measure(missing,1,new Size(2000,1080)).IsEmpty,"one tab is insufficient isolation evidence");
  var mixed=Quest(1);mixed.Add(A("quest summary",1200,280,160,24));Check(QuestDetailGeometry.Measure(mixed,1,new Size(2000,1080)).IsEmpty,"mixed detail panes fail closed");
  mixed=Quest(1);mixed[1].Bounds=new RectangleF(210,500,100,20);Check(QuestDetailGeometry.Measure(mixed,1,new Size(2000,1080)).IsEmpty,"vertically unrelated tab words cannot establish a journal");
  Check(QuestDetailGeometry.Measure(Quest(1),float.PositiveInfinity,new Size(2000,1080)).IsEmpty,"nonfinite geometry scale is refused");
  var cw = new List<PanelTextAnchor> {
    A("available", 0, 70, 92, 26),
    A("in progress", 102, 71, 100, 26),
    A("completed", 218, 70, 95, 25),
    A("questsummary", 382, 207, 171, 36),
    A("forfeit", 742, 616, 69, 21)
};

var cwPane = QuestDetailGeometry.Measure(
    cw, 1, new Size(820, 656));

Check(
    !cwPane.IsEmpty &&
    cwPane.Contains(new RectangleF(380, 343, 368, 243)),
    "joined CW summary establishes body scope");

Check(
    !cwPane.Contains(new RectangleF(20, 150, 270, 20)) &&
    !cwPane.Contains(new RectangleF(606, 616, 116, 20)),
    "list and footer excluded from body scope");

cw[3].Key = "questsummaryx";

Check(
    QuestDetailGeometry.Measure(
        cw, 1, new Size(820, 656)).IsEmpty,
    "near-miss summary rejected");

Check(
    !QuestDetailGeometry.IsSummaryKey("my questsummary") &&
    !QuestDetailGeometry.IsSummaryKey(null),
    "summary matching remains exact");

cw[3].Key = "questsummary";

var linked = QuestDetailGeometry.LinkedWindow(
    cw, 1, new Size(820, 656), new Point(150, 300));

Check(
    !linked.IsEmpty &&
    linked.Contains(new Rectangle(379, 343, 369, 243)),
    "list cursor links verified detail pane");

Check(
    QuestDetailGeometry.LinkedWindow(
        cw, 1, new Size(820, 656), new Point(500, 500))
        == linked,
    "detail cursor selects same linked window");

Check(
    QuestDetailGeometry.LinkedWindow(
        cw, 1, new Size(820, 656), new Point(100, 650))
        .IsEmpty,
    "cursor below quest cannot claim scope");

cw.RemoveAt(4);

Check(
    QuestDetailGeometry.LinkedWindow(
        cw, 1, new Size(820, 656), new Point(150, 300))
        .IsEmpty,
    "linked task still requires footer");
return count;
 }
}
