$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
function Method([string]$file,[string]$name,[int]$parameterCount=-1){
 $tree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $root ('src/'+$file)))).GetRoot()
 $methods=@($tree.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq $name})
 if($parameterCount -ge 0){$methods=@($methods|Where-Object{$_.ParameterList.Parameters.Count -eq $parameterCount})}
 if($methods.Count -eq 0){throw ("method not found: "+$file+" "+$name+" params="+$parameterCount)}
 return $methods[0].ToFullString()
}
$prefix=@'
using System;using System.Collections.Generic;using System.Globalization;using System.Text;using System.Text.RegularExpressions;
namespace MapleOverlay {
 public enum GraphicsUnit{Pixel}
 public enum FontStyle{Regular,Bold}
 public class Font{public float Size;public Font(string family,float size,FontStyle style,GraphicsUnit unit){Size=size;}}
 public struct Rectangle{
  public int Left,Top,Width,Height;
  public Rectangle(int left,int top,int width,int height){Left=left;Top=top;Width=width;Height=height;}
  public static Rectangle Empty{get{return new Rectangle(0,0,0,0);}}
 }
 public struct RectangleF{
  public float X,Y,Width,Height;
  public RectangleF(float x,float y,float width,float height){X=x;Y=y;Width=width;Height=height;}
  public float Left{get{return X;}}public float Top{get{return Y;}}
  public float Right{get{return X+Width;}}public float Bottom{get{return Y+Height;}}
  public bool IsEmpty{get{return Width<=0||Height<=0;}}
  public static RectangleF Empty{get{return new RectangleF(0,0,0,0);}}
  public static RectangleF FromLTRB(float left,float top,float right,float bottom){return new RectangleF(left,top,right-left,bottom-top);}
  public static RectangleF Union(RectangleF a,RectangleF b){
   if(a.IsEmpty)return b;if(b.IsEmpty)return a;
   return FromLTRB(Math.Min(a.Left,b.Left),Math.Min(a.Top,b.Top),Math.Max(a.Right,b.Right),Math.Max(a.Bottom,b.Bottom));}
  public static RectangleF Inflate(RectangleF value,float x,float y){
   return FromLTRB(value.Left-x,value.Top-y,value.Right+x,value.Bottom+y);}
  public static RectangleF Intersect(RectangleF a,RectangleF b){
   float left=Math.Max(a.Left,b.Left),top=Math.Max(a.Top,b.Top);
   float right=Math.Min(a.Right,b.Right),bottom=Math.Min(a.Bottom,b.Bottom);
   return right<=left||bottom<=top?Empty:FromLTRB(left,top,right,bottom);}
  public void Inflate(float x,float y){X-=x;Y-=y;Width+=x*2;Height+=y*2;}
  public bool IntersectsWith(RectangleF other){return other.Left<Right&&Left<other.Right&&other.Top<Bottom&&Top<other.Bottom;}
  public bool Contains(RectangleF other){return other.Left>=Left&&other.Right<=Right&&other.Top>=Top&&other.Bottom<=Bottom;}
  public bool Equals(RectangleF other){return X==other.X&&Y==other.Y&&Width==other.Width&&Height==other.Height;}
  public override bool Equals(object other){return other is RectangleF&&Equals((RectangleF)other);}
  public override int GetHashCode(){return X.GetHashCode()^Y.GetHashCode()^Width.GetHashCode()^Height.GetHashCode();}
 }
 class OcrWord{public string Text;public RectangleF BoundingRect;}
 class OcrLine{public string Text;public List<OcrWord> Words=new List<OcrWord>();}
 class OcrPanelInfo{public string Text="";public List<OcrLine> Lines=new List<OcrLine>();public bool IsEquipmentDetail;public string EquipmentReason="";}
 enum LabelConfidence{Low,Medium,High}
 class OverlayLabel{public RectangleF Bounds,SourceBounds,KeepClearBounds,PanelBounds;public List<RectangleF> SourceLineBounds;public bool HasSourceBounds=true;public string Text="",SemanticRole="",MatchSource="";public LabelConfidence Confidence=LabelConfidence.High;public EquipmentFieldPlan EquipmentField;public EquipmentPanelPlan EquipmentPanel;}
 static class TranslationStore{public static string Normalize(string value){
  if(String.IsNullOrEmpty(value))return "";var b=new StringBuilder();bool space=false;
  foreach(char raw in value.Trim().ToLowerInvariant()){char c=raw;bool keep=Char.IsLetterOrDigit(c)||c=='\''||c=='+'||c=='%'||c=='#';
   if(!keep){if(!space&&b.Length>0)b.Append(' ');space=true;}else{b.Append(c);space=false;}}
  return b.ToString().Trim();}}
 static class Program{public static List<string> Logs=new List<string>();public static void LogRuntimeEvent(string name,string detail){Logs.Add(name+" "+detail);}}
 partial class OverlayForm{
  public Rectangle Bounds=Rectangle.Empty,captureBounds=new Rectangle(0,0,1200,900);
  string EquipmentLabelText;float EquipmentLabelStart;int EquipmentLabelCount;int EquipmentNameCount;int EquipmentKeyStart;
'@
$body=$prefix
foreach($method in @(
 @{File='StructuredPanelOverlay.cs';Name='PreciseTextSpan';Params=5},
 @{File='StructuredPanelOverlay.cs';Name='OverlaySpan'},
 @{File='StructuredPanelOverlay.cs';Name='AddStructuredFieldLabels'},
 @{File='MapleOverlay.cs';Name='BuildVisualRows'},
 @{File='MapleOverlay.cs';Name='CombineVisualRow'},
 @{File='MapleOverlay.cs';Name='GetCombinedLineBounds'},
 @{File='MapleOverlay.cs';Name='GetOcrLineBounds'},
 @{File='MapleOverlay.cs';Name='ToOverlayX'},
 @{File='MapleOverlay.cs';Name='ToOverlayY'})){
 if($method.ContainsKey('Params')){$body+="`n"+(Method $method.File $method.Name $method.Params)}else{$body+="`n"+(Method $method.File $method.Name)}
}
$body+="`n}`n}`n"
$body+=([IO.File]::ReadAllText((Join-Path $root 'src/EquipmentFieldGrammar.cs')) -replace '(?m)^using .*;\r?\n','')
$body+=([IO.File]::ReadAllText((Join-Path $root 'src/EquipmentPanelLayout.cs')) -replace '(?m)^using .*;\r?\n','')
$suffix=@'
 namespace MapleOverlay {
 partial class OverlayForm {
  static int count;
  static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
  static OcrLine Line(string text,float x,float y){
   var line=new OcrLine{Text=text};float cx=x;
   foreach(string token in text.Split(new char[]{' '},StringSplitOptions.RemoveEmptyEntries)){
    float width=Math.Max(5,token.Length*7.0f);
    line.Words.Add(new OcrWord{Text=token,BoundingRect=new RectangleF(cx,y,width,16)});
    cx+=width+4;
   }
   return line;
  }
  static OcrLine Adjacent(string text,string[] words,float x,float y){
   var line=new OcrLine{Text=text};float cx=x;
   foreach(string token in words){
    float width=Math.Max(5,token.Length*7.0f);
    line.Words.Add(new OcrWord{Text=token,BoundingRect=new RectangleF(cx,y,width,16)});
    cx+=width;
   }
   return line;
  }
  static Dictionary<OcrLine,OcrPanelInfo> Panels(List<OcrLine> lines,bool equipment,string reason){
   var panel=new OcrPanelInfo{IsEquipmentDetail=equipment,EquipmentReason=reason};
   panel.Lines.AddRange(lines);var builder=new StringBuilder();
   foreach(var line in lines){if(builder.Length>0)builder.Append(' ');builder.Append(line.Text);}
   panel.Text=builder.ToString();
   var map=new Dictionary<OcrLine,OcrPanelInfo>();foreach(var line in lines)map[line]=panel;return map;
  }
  List<OverlayLabel> Build(List<OcrLine> lines,Dictionary<OcrLine,OcrPanelInfo> panels,out List<OverlayLabel> output){
   output=new List<OverlayLabel>();AddStructuredFieldLabels(output,lines,panels,1.0f);return output;
  }
  public static int Run(){
   count=0;
   var lines=new List<OcrLine>{
    Line("REQ LEV : 10  REQ STR : 0  REQ INT : 50",100,100),
    Line("Weapon Def.: +16  Magic Def.: +18",100,124),
    Line("INT: +1  LUK: +2  Remaining Enhancements: 7",100,148),
    Line("Weapon Attack: +36 (40 -4)",100,172),
    Line("Critical Rate: +5%",100,196),
    Line("Evasion: +4",100,220),
    Line("Attack Speed: Fast",100,244),
    Line("Upgrade Slots: 2",100,268)
   };
   var form=new OverlayForm();var panels=Panels(lines,true,"grammar-structure");
   List<OverlayLabel> output;
   form.Build(lines,panels,out output);
   Check(output.Count==13,"all parsed equipment columns are planned");
   Check(Program.Logs.Exists(x=>x.Contains("equipment-coverage")&&x.Contains("recognizedPanels=1")&&x.Contains("prepared=13")),"coverage evidence distinguishes recognized and prepared fields");
   Check(Program.Logs.Exists(x=>x.Contains("equipment-panel")&&x.Contains("reason=grammar-structure")),"panel evidence records the recognition reason");
   Check(output.TrueForAll(x=>x.SemanticRole=="item-field"&&x.EquipmentField!=null),"every planned label carries an equipment field plan");
   Check(output.TrueForAll(x=>x.EquipmentField.Status=="prepared"),"every planned field is marked prepared");
   var typography=output[0].EquipmentField.Typography;
   Check(output.TrueForAll(x=>Object.ReferenceEquals(x.EquipmentField.Typography,typography)),"all fields share one panel typography");
   Check(output.TrueForAll(x=>x.EquipmentPanel!=null&&Object.ReferenceEquals(x.EquipmentPanel,output[0].EquipmentPanel)),"all fields share one panel plan");
   Check(output.TrueForAll(x=>x.Confidence==LabelConfidence.High&&x.HasSourceBounds),"planned fields keep high confidence and source geometry");
   Check(output.TrueForAll(x=>!x.KeepClearBounds.IsEmpty),"every numeric/modifier value has a protection guard");
   Check(output.TrueForAll(x=>x.KeepClearBounds.Left>x.SourceBounds.Right),"guards begin after the key replacement region");
   Check(output.TrueForAll(x=>x.PanelBounds.Contains(x.SourceBounds)),"every field stays inside its equipment panel boundary");
   string values="";foreach(var label in output)values+=label.EquipmentField.OriginalValue+"|";
   Check(values.Contains("+16|")&&values.Contains("+18|")&&values.Contains("+1|")&&values.Contains("+2|")&&values.Contains("7|"),"defence, INT/LUK and upgrade values survive planning");
   Check(values.Contains("+36 (40 -4)|")&&values.Contains("+5%|")&&values.Contains("+4|")&&values.Contains("Fast|")&&values.Contains("2|"),"attack, critical, evasion, attack speed and slot values survive planning");
   Check(output.Exists(x=>x.EquipmentField.ValueMode=="value-pixels-preserved"),"numeric values are pixel-protected");
   Check(output.Exists(x=>x.EquipmentField.MatchSource=="equipment-field:avoidability"),"Evasion is planned as the generic avoidability field");
   Check(output.FindAll(x=>x.EquipmentField.PartialValue).Count==0,"complete fixture has no partial values");
   Check(output.FindAll(x=>x.EquipmentField.MergedValueBaked).Count==0,"complete fixture has no text-baked merged values");

   // A partial OCR tooltip without a recognized panel still uses the row fallback.
   var fallbackLines=new List<OcrLine>{Line("Weapon Def.: +1O  Magic Def.: -2",100,300),Line("Evasion +4",100,324)};
   var fallbackPanels=Panels(fallbackLines,false,"");
   form.Build(fallbackLines,fallbackPanels,out output);
   Check(output.Count==3,"unrecognized equipment rows still plan every parsed field");
   Check(Program.Logs.Exists(x=>x.Contains("equipment-coverage")&&x.Contains("recognizedRows=")),"fallback evidence records recognized rows");
   Check(output.TrueForAll(x=>x.EquipmentField.OriginalValue.Length>0),"fallback rows retain their observed value strings");
   Check(output.Exists(x=>x.EquipmentField.OriginalValue=="+1O"),"fallback preserves OCR-confusable digits");

   // A key with its value missing must still be planned, and the rest of the row protected.
   var missingLines=new List<OcrLine>{Line("Weapon Def.",100,400)};
   var missingPanels=Panels(missingLines,true,"grammar-structure");
   form.Build(missingLines,missingPanels,out output);
   Check(output.Count==1&&!output[0].EquipmentField.HasValue,"a key-only partial row still yields one field");
   Check(output[0].EquipmentField.ValueMode=="no-value"&&output[0].KeepClearBounds.IsEmpty,"a missing value does not invent a guard or number");

   // Multiple equipment types use the same generic path.
   foreach(string kind in new string[]{"Hat","Overall","Shoes","Gloves","One-Handed Sword"}){
    var kindLines=new List<OcrLine>{Line("Type: "+kind,100,450),Line("Weapon Def.: +12  Magic Def.: +14",100,474)};
    var kindPanels=Panels(kindLines,true,"grammar-structure");
    form.Build(kindLines,kindPanels,out output);
    Check(output.Count==2,"equipment type "+kind+" uses the same field path");
   }
   // EQUIP-R1: one OCR word contains both key and value. The proportional split is
   // estimated, so the field must be preserved and never claimed as pixel-protected.
   var mergedLines=new List<OcrLine>{Line("WeaponDef-5",100,520)};
   var mergedPanels=Panels(mergedLines,true,"grammar-structure");
   form.Build(mergedLines,mergedPanels,out output);
   Check(output.Count==0,"R1 estimated merged-word boundary preserves the original row");
   Check(Program.Logs.Exists(x=>x.Contains("equipment-coverage")&&x.Contains("estimatedValues=1")&&
     x.Contains("preservedReason=weapon-defense:estimated-value-region")),
     "R1 estimated boundary is not reported as value-pixels-preserved");

   // EQUIP-R3: adjacent no-space fields with measured word boxes. The STR value must
   // stop before INT, and both key/value pairs must be prepared without conflict.
   var contiguousLines=new List<OcrLine>{Adjacent("STR+5INT+3",new string[]{"STR","+5","INT","+3"},100,550)};
   var contiguousPanels=Panels(contiguousLines,true,"grammar-structure");
   form.Build(contiguousLines,contiguousPanels,out output);
   Check(output.Count==2,"R3 contiguous STR+5INT+3 parses and prepares two fields");
   Check(output.TrueForAll(x=>x.EquipmentField.BoundaryMode=="measured"&&
     x.EquipmentField.ValuePixelsPreserved),"R3 contiguous fields have measured value boundaries");
   Check(output.Exists(x=>x.EquipmentField.Key=="strength"&&x.EquipmentField.OriginalValue=="+5")&&
     output.Exists(x=>x.EquipmentField.Key=="intelligence"&&x.EquipmentField.OriginalValue=="+3"),
     "R3 STR does not consume the first character of INT");
   Check(output.TrueForAll(x=>x.KeepClearBounds.Left>x.SourceBounds.Right),
     "R3 STR value guard does not intrude into the INT key");

   var seen=new HashSet<string>();
   foreach(var log in Program.Logs)
    if((log.StartsWith("equipment-coverage")||log.StartsWith("equipment-panel"))&&seen.Add(log))
     Console.WriteLine("EVIDENCE: "+log);
   Console.WriteLine("Equipment panel plan: "+count+" checks passed; exact production structured builder with controlled OCR geometry; real CW pixels unverified.");
   return count;
  }
 }
 public static class EquipmentPanelRunner{public static int Run(){return OverlayForm.Run();}}
}
'@
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition ($body+$suffix)
$count=[MapleOverlay.EquipmentPanelRunner]::Run()
if($count -lt 20){throw "unexpected check count: $count"}
