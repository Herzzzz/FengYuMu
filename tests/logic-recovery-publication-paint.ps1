param([string]$SourceRoot,[ValidateSet('None','Publication','Paint')][string]$Before='None',
 [string]$BaselineRoot='/workspace/work/r9-recovery/baseline-r7')
$ErrorActionPreference='Stop'
$root=if($SourceRoot){$SourceRoot}else{Split-Path -Parent $PSScriptRoot}
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
function Parse([string]$file){[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $root $file))).GetRoot()}
function Get-ProbeClass($tree,[string]$name){@($tree.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq $name})[0]}
function Method($type,[string]$name){@($type.Members|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq $name})[0]}
$main=Parse 'src/MapleOverlay.cs';$form=Get-ProbeClass $main 'OverlayForm'
$show=@($form.Members|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'ShowTranslationAsync' -and $_.ParameterList.Parameters.Count -eq 2})[0]
if($Before -eq 'Publication'){
 $baseline=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $BaselineRoot 'src/MapleOverlay.cs'))).GetRoot()
 $show=@((Get-ProbeClass $baseline 'OverlayForm').Members|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'ShowTranslationAsync' -and $_.ParameterList.Parameters.Count -eq 2})[0]
}
# Extract the actual consecutive final publication statements from either version.
$startPattern=if($Before -eq 'Publication'){'foreach (OverlayLabel candidate in next)'}else{'List<OverlayLabel> finalLabels'}
$block=@($show.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax] -and @($_.Statements|Where-Object{$_.ToString().StartsWith($startPattern)}).Count -eq 1})[0]
if($null -eq $block){throw 'Actual final publication block not found'}
$active=$false;$final=''
foreach($statement in $block.Statements){
 if($statement.ToString().StartsWith($startPattern)){$active=$true}
 if($active){$final+=$statement.ToFullString()}
 if($active -and (($Before -ne 'Publication' -and $statement.ToString() -eq 'labels.AddRange(finalLabels);') -or
  ($Before -eq 'Publication' -and $statement.ToString().Contains('RecognitionWasCancelled')))){break}
}
if($final -notmatch 'await RememberDisplayedSceneAsync' -or $final -notmatch 'RecognitionWasCancelled'){throw 'Actual final sequence missing await/cancel'}
$final=$final -replace 'return;','return false;'
$enums=@($main.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.EnumDeclarationSyntax] -and $_.Identifier.ValueText -in @('LabelConfidence','LabelRenderMode')}|ForEach-Object{$_.ToFullString()})
$fusion=Get-ProbeClass (Parse 'src/FusionOverlay.cs') 'OverlayForm'
$asset=Get-ProbeClass (Parse 'src/FusionOverlay.cs') 'FusionAsset'
$paintMethod=Method $fusion 'TryPaintFusionLabel'
if($Before -eq 'Paint'){
 $baseline=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $BaselineRoot 'src/FusionOverlay.cs'))).GetRoot()
 $paintMethod=Method (Get-ProbeClass $baseline 'OverlayForm') 'TryPaintFusionLabel'
}

$clone=Method (Get-ProbeClass (Parse 'src/ManualMemoryOverlay.cs') 'OverlayForm') 'CloneMemoryLabel'
$prefix=@'
using System;using System.Collections.Generic;using System.Drawing;using System.Diagnostics;using System.Threading.Tasks;
namespace MapleOverlay {
class Bitmap:IDisposable {public void Dispose(){}}
class Graphics {public int Draws;public void DrawImageUnscaled(Bitmap b,Point p){Draws++;}}
static class Program{public static void LogRuntimeEvent(string a,string b){}}
'@
$code=$prefix+($enums -join "`n")+(Get-ProbeClass $main 'OverlayLabel').ToFullString()+@'
static class LabelConfidencePolicy{public static bool IsTrusted(LabelConfidence c){return c==LabelConfidence.High;}}
class RecoveryPublicationProbe {
List<OverlayLabel> labels=new List<OverlayLabel>(),earlyPaintedLabels=new List<OverlayLabel>();
Dictionary<OverlayLabel,FusionAsset> fusionAssets=new Dictionary<OverlayLabel,FusionAsset>();
List<RectangleF> paintedCards=new List<RectangleF>();List<FusionAsset> paintedFusionAssets=new List<FusionAsset>();
Rectangle ClientRectangle=new Rectangle(0,0,200,200),Bounds=Rectangle.Empty,gameBounds=new Rectangle(0,0,200,200);
int recognitionGeneration=1;bool cancelled,visibleTranslation;Stopwatch visibleRequestWatch=Stopwatch.StartNew();
TaskCompletionSource<bool> gate=new TaskCompletionSource<bool>();
Task<bool> RememberDisplayedSceneAsync(Bitmap b,Rectangle r,List<OverlayLabel> s){return gate.Task;}
bool RecognitionWasCancelled(bool a,int r,int g){return cancelled||g!=recognitionGeneration;}
void HideWindowScanIndicator(){}void ShowCurrentTranslations(){}
'@+$asset.ToFullString()+$clone.ToFullString()+(Method $form 'PreserveEarlyLabels').ToFullString()+(Method $form 'PublishEarlySnapshotAsync').ToFullString()+$paintMethod.ToFullString()+@'
async Task<bool> Final(Bitmap bitmap,Rectangle screen,List<OverlayLabel> next,bool automatic,int requestId,int generation){
'@+$final+@'
return true;}
static int count;
static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
static OverlayLabel Label(string text){return new OverlayLabel{Text=text,Confidence=LabelConfidence.High,HasSourceBounds=true,SourceBounds=new RectangleF(10,10,50,20),Bounds=new RectangleF(10,10,50,20),SourceLineBounds=new List<RectangleF>{new RectangleF(10,10,50,20)}};}
static RecoveryPublicationProbe Fresh(){var p=new RecoveryPublicationProbe();var old=Label("old");p.labels.Add(old);p.fusionAssets.Add(old,new FusionAsset{Image=new Bitmap()});return p;}
void Asset(OverlayLabel l,Rectangle box,int x,int y){var mask=new bool[box.Width*box.Height];mask[y*box.Width+x]=true;fusionAssets[l]=new FusionAsset{Image=new Bitmap(),Text=l.Text,Bounds=box,Generation=recognitionGeneration,PixelMask=mask};}
public static int Run(){count=0;var p=Fresh();var old=p.labels[0];var next=new List<OverlayLabel>{Label("new")};
var task=p.Final(null,Rectangle.Empty,next,false,1,1);
Check(!task.IsCompleted&&p.labels[0]==old,"final await preserves old visible label identity");p.gate.SetResult(true);
Check(task.GetAwaiter().GetResult()&&p.labels.Count==1&&p.labels[0]==next[0],"successful preparation commits final set");
p=Fresh();old=p.labels[0];task=p.Final(null,Rectangle.Empty,next,false,1,1);p.gate.SetResult(false);
Check(!task.GetAwaiter().GetResult()&&p.labels[0]==old,"preparation failure preserves visible set");
p=Fresh();old=p.labels[0];task=p.Final(null,Rectangle.Empty,next,false,1,1);p.cancelled=true;p.gate.SetResult(true);
Check(!task.GetAwaiter().GetResult()&&p.labels[0]==old,"cancelled final cannot commit new labels");
p=Fresh();old=p.labels[0];var candidate=Label("focus");next=new List<OverlayLabel>{candidate,Label("weak")};next[1].Confidence=LabelConfidence.Low;
task=p.PublishEarlySnapshotAsync(null,Rectangle.Empty,next,false,1,1,"focus");candidate.Text="later merge";candidate.SourceLineBounds.Clear();
Check(!task.IsCompleted&&p.labels[0]==old,"early preparation does not replace visible labels while awaiting");p.gate.SetResult(true);task.GetAwaiter().GetResult();
Check(p.labels.Count==1&&p.labels[0].Text=="focus"&&p.labels[0].SourceLineBounds.Count==1,"early trusted snapshot deep copies measured line bounds");
p=Fresh();old=p.labels[0];task=p.PublishEarlySnapshotAsync(null,Rectangle.Empty,next,false,1,1,"focus");p.gate.SetResult(false);
Check(!task.GetAwaiter().GetResult()&&p.labels[0]==old,"failed early preparation preserves old set");
p=Fresh();old=p.labels[0];task=p.PublishEarlySnapshotAsync(null,Rectangle.Empty,next,false,1,1,"focus");p.cancelled=true;p.gate.SetResult(true);
Check(!task.GetAwaiter().GetResult()&&p.labels[0]==old,"cancelled early snapshot cannot become visible");
var one=Label("same");next=new List<OverlayLabel>{one};PreserveEarlyLabels(next,new List<OverlayLabel>{CloneMemoryLabel(one)},1);
Check(next.Count==1,"identical cloned early row does not duplicate final label");
p=Fresh();p.fusionAssets.Clear();var g=new Graphics();var a=Label("a");var b=Label("b");
p.Asset(a,new Rectangle(10,10,50,20),5,5);p.Asset(b,new Rectangle(10,25,50,20),5,5);
Check(p.TryPaintFusionLabel(g,a),"first actual fusion patch paints");
Check(p.TryPaintFusionLabel(g,b)&&g.Draws==2,"overlapping transparent margins do not suppress adjacent row");
p.paintedFusionAssets.Clear();p.Asset(a,new Rectangle(10,10,50,20),5,16);p.Asset(b,new Rectangle(10,25,50,20),5,1);p.TryPaintFusionLabel(g,a);
Check(!p.TryPaintFusionLabel(g,b)&&b.FusionFailureReason=="paint-pixel-overlap","actual changed-pixel overlap is refused");
p.paintedFusionAssets.Clear();p.paintedCards.Add(new RectangleF(10,10,2,2));p.Asset(a,new Rectangle(10,10,50,20),5,5);
Check(p.TryPaintFusionLabel(g,a),"dynamic value touching transparent padding is preserved");
p.paintedFusionAssets.Clear();p.paintedCards.Clear();p.paintedCards.Add(new RectangleF(15,15,1,1));
Check(!p.TryPaintFusionLabel(g,a),"changed pixel touching dynamic value is refused");p.paintedCards.Clear();
p.Asset(a,new Rectangle(-1,10,50,20),5,5);Check(!p.TryPaintFusionLabel(g,a),"patch outside client refused");
p.Asset(a,new Rectangle(10,10,50,20),5,5);p.gameBounds=new Rectangle(0,0,20,20);Check(!p.TryPaintFusionLabel(g,a),"patch outside game refused");
p.gameBounds=p.ClientRectangle;a.Text="changed";Check(!p.TryPaintFusionLabel(g,a),"stale patch text refused");a.Text="a";p.recognitionGeneration++;
Check(!p.TryPaintFusionLabel(g,a),"previous generation patch refused");return count;}
}
public static class RecoveryPublicationRunner{public static int Run(){return RecoveryPublicationProbe.Run();}}
}
'@
$mask=[IO.File]::ReadAllText((Join-Path $root 'src/FusionPatchMask.cs')) -replace '(?m)^using .*;\r?\n',''
$code+="`n"+$mask
Add-Type -TypeDefinition $code -IgnoreWarnings -WarningAction SilentlyContinue
$count=[MapleOverlay.RecoveryPublicationRunner]::Run()
if($count -ne 18){throw "Unexpected check count $count"}
Write-Output "Actual production publication/painting: $count passed; GDI calls are a controlled boundary, not native Windows rendering."
