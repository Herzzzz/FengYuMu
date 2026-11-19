$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$source='using System;using System.Collections.Generic;using System.Drawing;'
foreach($f in @('QuestDetailGeometry.cs','QuestTextScope.cs')){$source+=[IO.File]::ReadAllText((Join-Path $repo ('src/'+$f))) -replace '(?m)^using .*;\r?\n',''}
$source+=@'
namespace MapleOverlay {
 public static class QuestScopeProbe{
  static int count;
  static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;Console.WriteLine("PASS: "+message);}
  static List<PanelTextAnchor> Anchors(){return new List<PanelTextAnchor>{new PanelTextAnchor{Key="quest summary",Bounds=new RectangleF(420,130,120,14)}};}
  static readonly Size Frame=new Size(1200,800);
  static List<RectangleF> Panels(){return new List<RectangleF>{new RectangleF(120,70,720,520)};}
  public static int Run(){
   var anchors=Anchors();var panels=Panels();
   Check(QuestDetailGeometry.Measure(anchors,1,Frame).IsEmpty,"legacy geometry cannot establish context when OCR misses tabs and footer");
   var area=QuestTextScope.Measure(anchors,panels,1,Frame);
   Check(!area.IsEmpty&&area.Contains(new RectangleF(420,170,220,14)),"observed panel and summary establish a selected detail scope in the same OCR pass");
   Check(!area.Contains(new RectangleF(180,170,160,14)),"left quest list cannot choose the active task");
   Check(!area.Contains(new RectangleF(900,170,160,14)),"adjacent helper or HUD stays outside the measured window");
   Check(!area.Contains(new RectangleF(420,100,200,14)),"chain heading above summary cannot override selected subtask");
   Check(QuestTextScope.Measure(anchors,new List<RectangleF>(),1,Frame).IsEmpty,"summary alone cannot invent a panel boundary");
   Check(QuestTextScope.Measure(anchors,new List<RectangleF>{new RectangleF(0,0,1200,800)},1,Frame).IsEmpty,"whole-frame region cannot masquerade as a measured quest panel");
   var duplicate=Anchors();duplicate.Add(new PanelTextAnchor{Key="quest summary",Bounds=new RectangleF(900,130,120,14)});
   Check(QuestTextScope.Measure(duplicate,panels,1,Frame).IsEmpty,"two distinct summary anchors are ambiguous");
   duplicate=Anchors();duplicate.Add(duplicate[0]);Check(!QuestTextScope.Measure(duplicate,panels,1,Frame).IsEmpty,"identical repeated heading evidence remains one anchor");
   anchors[0].Key="questsummary";Check(!QuestTextScope.Measure(anchors,panels,1,Frame).IsEmpty,"OCR joined summary words retain the same context");
   anchors[0].Key="quest";Check(QuestTextScope.Measure(anchors,panels,1,Frame).IsEmpty,"generic quest caption cannot authorize right-pane prose");
   anchors=Anchors();anchors.Add(new PanelTextAnchor{Key="forfeit",Bounds=new RectangleF(650,520,100,18)});area=QuestTextScope.Measure(anchors,panels,1,Frame);
   Check(area.Bottom==518&&!area.IntersectsWith(anchors[1].Bounds),"observed footer bounds the prose and stays outside it");
   panels.Add(new RectangleF(390,115,410,440));area=QuestTextScope.Measure(Anchors(),panels,1,Frame);
   Check(area.Right==798,"smallest observed owning panel wins");
   Check(QuestTextScope.Measure(Anchors(),Panels(),float.NaN,Frame).IsEmpty,"nonfinite OCR scale is refused");
   Check(QuestTextScope.Measure(Anchors(),Panels(),0,Frame).IsEmpty,"zero OCR scale is refused");
   Check(QuestTextScope.Measure(Anchors(),new List<RectangleF>{new RectangleF(float.NaN,70,720,520)},1,Frame).IsEmpty,"nonfinite measured panel is refused");
   var moved=Anchors();moved[0].Bounds=new RectangleF(900,130,120,14);Check(QuestTextScope.Measure(moved,Panels(),1,Frame).IsEmpty,"unrelated foreground panel cannot lend its boundary");
   Console.WriteLine("Quest text scope: "+count+" checks passed; observed-geometry fixtures, not user OCR/pixels.");return count;
  }
 }
}
'@
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
[MapleOverlay.QuestScopeProbe]::Run()
