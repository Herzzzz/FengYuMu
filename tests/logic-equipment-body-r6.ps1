param([switch]$Before,[string]$BeforeRepository='/workspace/FengYuMu-option2-completeness-r5')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$sourceRoot=if($Before){$BeforeRepository}else{$root}
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$tree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $sourceRoot 'src/MapleOverlay.cs'))).GetRoot()
$form=@($tree.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq 'OverlayForm'})[0]
$method=@($form.Members|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'AddEquipmentPanelLabels'})[0].ToFullString()
$prefix=@'
using System;using System.Collections.Generic;
namespace EquipmentBodyProbe {
 class OcrLine{public string Text;}
 class OcrPanelInfo{public string Text="known item";public List<OcrLine> Lines=new List<OcrLine>();}
 class OverlayLabel{public string Text="",SemanticRole="";}
 class TranslationEntry{public string Chinese="已观察的物品说明";}
 class MatchResult{public TranslationEntry Entry=new TranslationEntry();}
 class TranslationStore{
  public bool Known=true,Matches=true,Accepted=true;
  public string DetectItemId(string text){return Known?"4003000":"";}
  public TranslationEntry GetItemOverview(string id){return id.Length>0?new TranslationEntry():null;}
  public List<MatchResult> FindItemTextMatches(string text,string id){return Matches&&id.Length>0?new List<MatchResult>{new MatchResult()}:new List<MatchResult>();}
  public static string Normalize(string text){return text.ToLowerInvariant();}
 }
 class Probe{
  TranslationStore translations=new TranslationStore();
  List<List<OcrLine>> BuildVisualRows(List<OcrLine> source){var r=new List<List<OcrLine>>();foreach(var line in source)r.Add(new List<OcrLine>{line});return r;}
  static string CombineVisualRow(List<OcrLine> row){return String.Join(" ",row.ConvertAll(x=>x.Text));}
  bool TryTranslateStructuredLine(string text,bool equipment,bool skill,out string translated){translated="属性";return text.StartsWith("STAT");}
  static List<OcrLine> FindBestPanelProseBlock(List<OcrLine> source){return source.FindAll(x=>x.Text.StartsWith("DESCRIPTION"));}
  static List<OcrLine> SelectBestDetailLines(List<OcrLine> lines,TranslationEntry entry){return lines;}
  void AddDetailBlockLabel(List<OverlayLabel> output,List<OcrLine> rows,string text,float scale){output.Add(new OverlayLabel{Text=text});}
  int ScoreStrongLongTextCandidate(string source,string normalized,MatchResult match,float confidence){return translations.Accepted?100:Int32.MinValue;}
'@
$suffix=@'
  static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
  static OcrPanelInfo Panel(int fields,bool prose=true){var p=new OcrPanelInfo();for(int i=0;i<fields;i++)p.Lines.Add(new OcrLine{Text="STAT "+i});if(prose)p.Lines.Add(new OcrLine{Text="DESCRIPTION observed in this exact panel"});return p;}
  public static int Run(bool before){count=0;var p=new Probe();var output=new List<OverlayLabel>();
   p.AddEquipmentPanelLabels(output,Panel(1),1);
   Check(output.Count==2&&output[1].Text=="已观察的物品说明","one translated equipment attribute cannot suppress its observed description");
   if(before)return count;
   output.Clear();p.AddEquipmentPanelLabels(output,Panel(4),1);Check(output.Count==5&&output[4].SemanticRole=="item-body","four fields and body keep independent semantic roles");
   output.Clear();p.AddEquipmentPanelLabels(output,Panel(1,false),1);Check(output.Count==1,"name and fields alone cannot publish an unseen overview");
   output.Clear();p.translations.Known=false;p.AddEquipmentPanelLabels(output,Panel(0),1);Check(output.Count==0,"unknown item cannot borrow a different item's overview");p.translations.Known=true;
   output.Clear();p.translations.Matches=false;p.AddEquipmentPanelLabels(output,Panel(1),1);Check(output.Count==1,"unmatched observed prose is preserved");p.translations.Matches=true;
   output.Clear();p.AddEquipmentPanelLabels(output,Panel(0),1);Check(output.Count==1&&output[0].SemanticRole=="item-body","description-only item still has its own path");
   output.Clear();p.translations.Accepted=false;p.AddEquipmentPanelLabels(output,Panel(1),1);Check(output.Count==1,"insufficient match coverage cannot publish a known item's body");
   return count;
  }
 }
 public static class Runner{public static int Run(bool before){return Probe.Run(before);}}
}
'@
Add-Type -TypeDefinition ($prefix+$method+$suffix)
$count=[EquipmentBodyProbe.Runner]::Run([bool]$Before)
if(-not $Before -and $count -ne 7){throw "Unexpected count: $count"}
Write-Output "Equipment description routing: $count passed; exact production builder, controlled OCR/glossary boundaries; real CW pixels unverified."
