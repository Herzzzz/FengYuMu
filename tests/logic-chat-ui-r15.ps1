$ErrorActionPreference='Stop'
$r=Split-Path -Parent $PSScriptRoot;$work='/workspace/work/release-v4.0'
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll');Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$simple=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $r 'src/SimpleForms.cs'))).GetRoot()
$main=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $r 'src/MapleOverlay.cs'))).GetRoot()
$chat=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $r 'src/OfflineChat.cs'))).GetRoot()
function GetNode($tree,[string]$name){@($tree.DescendantNodes()|Where-Object{($_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -or $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.EnumDeclarationSyntax]) -and $_.Identifier.ValueText -eq $name})[0]}
$source='using System;using System.Drawing;using System.Collections.Generic;using System.Text;using System.Text.RegularExpressions; namespace ChatUiReplay {'
foreach($name in @('ChatVisualKind','ChatSourceCategory','ChatVisualStyle','ChatVisualStylePolicy','ChatTranslationHistory','ChatTranslationRecord')){$source+=(GetNode $simple $name).ToFullString()}
foreach($name in @('ChatCaptureLine','ChatCaptureFrame')){$source+=(GetNode $main $name).ToFullString()}
$source+='}'+([IO.File]::ReadAllText((Join-Path $r 'src/ChatColorEvidence.cs')) -replace '(?m)^using .*;\r?\n','').Replace('namespace MapleOverlay','namespace ChatUiReplay')
$selector=GetNode $chat 'ChatRegionSelectorForm'
$hit=@($selector.Members|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'HitTest'})[0].ToFullString()
$start=@((GetNode $simple 'MainPanelForm').Members|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'BuildStartPage'})[0]
$center=@($start.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax] -and $_.Identifier.ValueText -eq 'centerStart'})[0].Initializer.Value.Block.ToFullString()
$source+='namespace ChatUiReplay { class Widget{public Size Size;public Point Location;public Size ClientSize;public int Width{get{return Size.Width;}set{Size=new Size(value,Size.Height);}}} class UiProbe {const int LeftEdge=1,RightEdge=2,TopEdge=4,BottomEdge=8,MoveArea=16,NewArea=32;Rectangle selection=new Rectangle(50,80,860,127);Widget startPage=new Widget(),start=new Widget(),update=new Widget{Size=new Size(250,44)},picture=new Widget{Size=new Size(360,42)};'+$hit+' void CenterStart()'+$center+@'
 public static bool InteriorMoves(){var p=new UiProbe();for(int y=95;y<190;y+=3)for(int x=65;x<895;x+=5)if(p.HitTest(new Point(x,y))!=MoveArea)return false;return true;}
 public static bool Centers(){var p=new UiProbe();foreach(int width in new int[]{446,650,900,1200}){p.startPage.ClientSize=new Size(width,720);p.CenterStart();foreach(var card in new[]{p.start,p.update,p.picture})if(Math.Abs(card.Location.X+card.Width/2-width/2)>1)return false;}return true;}
 }
 public static class ChatUiRunner {
  static int n;static void Check(bool value,string message){if(!value)throw new Exception(message);n++;Console.WriteLine("PASS: "+message);}
  static int Pixel(Color c){return c.ToArgb();}
  static int[] Pixels(Color background,Color ink,bool band){int[] pixels=new int[340*70];for(int i=0;i<pixels.Length;i++)pixels[i]=Pixel(background);
   if(band)for(int y=18;y<40;y++)for(int x=0;x<340;x++)pixels[y*340+x]=Pixel(Color.FromArgb(205,120,175));
   for(int left=28;left<310;left+=14){for(int y=23;y<32;y++)for(int x=left;x<left+7;x++)pixels[y*340+x]=Pixel(Color.Black);for(int y=25;y<30;y++)for(int x=left+2;x<left+5;x++)pixels[y*340+x]=Pixel(ink);}return pixels;}
  public static int Run(){n=0;var channels=new[]{ChatSourceCategory.Normal,ChatSourceCategory.Buddy,ChatSourceCategory.Party,ChatSourceCategory.Guild,ChatSourceCategory.Alliance,ChatSourceCategory.Whisper};var colors=new[]{Color.FromArgb(255,255,255),Color.FromArgb(255,204,102),Color.FromArgb(255,153,255),Color.FromArgb(153,153,255),Color.FromArgb(153,255,153),Color.FromArgb(0,255,0)};
   for(int i=0;i<channels.Length;i++){Color foreground,background;bool band;ChatColorEvidence.Sample(Pixels(Color.FromArgb(85,145,200),colors[i],false),340,70,new Rectangle(20,20,300,16),null,out foreground,out background,out band);var style=ChatVisualStylePolicy.FromSample("Alice: hello",foreground,background,band);
    Check(!band&&style.Category==channels[i]&&style.Kind==ChatVisualKind.Chat,"source glyph color determines "+channels[i]+" even over a uniformly colored game scene");}
   Color fg,bg;bool broadcast;ChatColorEvidence.Sample(Pixels(Color.FromArgb(25,28,35),Color.White,true),340,70,new Rectangle(20,20,300,16),null,out fg,out bg,out broadcast);
   Check(broadcast&&ChatVisualStylePolicy.FromSample("Alice: selling",fg,bg,broadcast).Category==ChatSourceCategory.Megaphone,"a real colored band requires a measured boundary and still classifies as broadcast");
   Check(ChatVisualStylePolicy.FromSample("[Party] Alice: hello",colors[2],bg,true).Category==ChatSourceCategory.Party,"an explicit personal channel is not promoted to megaphone by background color");
   Check(ChatVisualStylePolicy.FromSample("Alice: this system uses a megaphone",Color.White,Color.Empty,false).Category==ChatSourceCategory.Normal,"ordinary message words do not manufacture system or megaphone channels");
   var party=ChatVisualStylePolicy.FromSample("Alice: hello",colors[2],Color.FromArgb(160,80,150),false);var frame=new ChatCaptureFrame();frame.Lines.Add(new ChatCaptureLine{Text="Alice: hello",Style=party});
   Check(frame.FindStyle("Alice: hello").Category==ChatSourceCategory.Party&&!frame.FindStyle("Alice: hello").HasBackground,"parsed-line association retains the source category without a later colored-background upgrade");
   var history=new ChatTranslationHistory(24000);foreach(var category in channels){var style=ChatVisualStyle.Default;style.Category=category;history.Add(new ChatTranslationRecord{Original="Alice: source",Translation="译文",Style=style});}
   Check(channels.Length==history.Select(null).Count&&history.Select(ChatSourceCategory.Normal).Count==1,"every source channel remains visible in All and exactly its own translated-history filter");
   foreach(var category in channels)Check(history.Select(category).Count==1,"translation keeps the original "+category+" category rather than reclassifying Chinese text");
   history.Add(new ChatTranslationRecord{Translation="English outbound",Style=ChatVisualStyle.Manual});
   Check(history.Select(ChatSourceCategory.Normal).Count==1&&history.Select(null).Count==7,"a manual outgoing translation is not falsely labelled as received Normal chat");
   Check(UiProbe.InteriorMoves(),"all sampled interior selector points move the rectangle without hunting painted pixels");
   Check(UiProbe.Centers(),"all home cards keep the same center across narrow and wide window sizes");
   Console.WriteLine("Chat/UI replay: "+n+" checks passed; pixel and layout/selection code controlled, physical Windows interaction unverified.");return n;
  }
 }
}
'@
[IO.File]::WriteAllText((Join-Path $work 'ChatUi.generated.cs'),$source)
Add-Type -TypeDefinition $source
[ChatUiReplay.ChatUiRunner]::Run()
$offline=[IO.File]::ReadAllText((Join-Path $r 'src/OfflineChat.cs'))
$interval=[int][regex]::Match($offline,'timer.Interval\s*=\s*(\d+)').Groups[1].Value
if([Math]::Abs(140.0/$interval-1.2) -gt .01){throw 'requested 20% polling increase missing'}
Write-Output 'PASS: configured chat detection frequency is approximately 20% higher (140ms to117ms), with existing no-overlap capture guard'
if($offline.Contains('new TextureBrush(tile)')){throw 'selector still has color-key holes'}
Write-Output 'PASS: selector body no longer uses the color-key screen-door brush; host opacity supplies translucency'
