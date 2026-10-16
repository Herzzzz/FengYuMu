param([switch]$Before,[ValidateSet('all','parser','publication','fields','ink','requests')][string]$Area='all',[string]$BeforeRepository='/workspace/FengYuMu-option2-r3')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$readRoot=if($Before){$BeforeRepository}else{$root}
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
function GetProbeClass($tree,[string]$name){@($tree.DescendantNodes()|Where-Object{$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax] -and $_.Identifier.ValueText -eq $name})[0]}
function Member($class,[string]$name){@($class.Members|Where-Object{($_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -or $_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax]) -and $_.Identifier.ValueText -eq $name})[0].ToFullString()}
$chatTree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $readRoot 'src/OfflineChat.cs'))).GetRoot()
$overlayTree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $readRoot 'src/MapleOverlay.cs'))).GetRoot()
$chat=GetProbeClass $chatTree 'OfflineChatForm';$overlay=GetProbeClass $overlayTree 'OverlayForm';$store=GetProbeClass $overlayTree 'TranslationStore'
$source=@'
using System;using System.Collections.Generic;using System.Text;using System.Text.RegularExpressions;using System.Threading;using System.Threading.Tasks;
namespace MapleOverlay {
 class ChatVisualStyle{public static readonly ChatVisualStyle Default=new ChatVisualStyle();}
 static class Program{public static void LogRuntimeEvent(string a,string b){}}
 class OnlineAiSettings{public static bool Ready=true;public bool IsReady{get{return Ready;}}public string Provider="test",NotReadyReason="Key missing";public static OnlineAiSettings Load(){return new OnlineAiSettings();}}
 static class OnlineAiClient{public static Func<string,Task<string>> Handler;public static Task<string> TranslateAsync(OnlineAiSettings s,string source,string target,string glossary,CancellationToken t){return Handler(source);}public static string DescribeFailure(Exception e,OnlineAiSettings s){return e.Message;}}
 class ChatTextRules{public static string ToSimplifiedChinese(string text){return text;}
'@
$source+=Member (GetProbeClass $chatTree 'ChatTextRules') 'LooksLikeInstructionLeak'
$source+="}`nclass TranslationStore {"+(Member $store 'Normalize')+"}`npublic class Probe {"
foreach($name in @('PendingChatLine','PreparedChatLine','ParseChatLines','IsCompleteChatNotice','RepairChatSpeakerMarker','TryParseChatPresence','IsStructuredUiNoise','IsLikelyWrappedChatTail','IsWrappedPseudoSpeaker','IsWrappedContinuationWord','IsPlausibleChatTranslationForTarget','RestorePlayerNames','RememberTranslation')){$source+="`n"+(Member $chat $name)}
if(-not $Before){foreach($name in @('ChatTranslationOutcome','PublishPreparedChatLine','ForgetTranslation','RequestChatTranslationAsync')){$source+="`n"+(Member $chat $name)}}
else{
 $source=$source.Replace('internal bool CacheAfterTranslation;', 'internal bool CacheAfterTranslation; internal string Failure="";')
 $source+=' class ChatTranslationOutcome {public string Text,Failure;}'
 $process=Member $chat 'ProcessPendingChatAsync'
 $a=$process.IndexOf('string translated = RestorePlayerNames(')
 $b=$process.IndexOf('prepared.SpeakerPrefix + translated, prepared.Pending.Style);',$a)+'prepared.SpeakerPrefix + translated, prepared.Pending.Style);'.Length
 $source+=' private bool PublishPreparedChatLine(PreparedChatLine prepared){int failedTranslations=0;'+$process.Substring($a,$b-$a)+'return true;}'
 $source+=' private Task<ChatTranslationOutcome> RequestChatTranslationAsync(string a,string b,string c,string d,CancellationToken t){return Task.FromResult(new ChatTranslationOutcome());}'
}
foreach($name in @('LooksLikeEquipmentStatText')){$source+="`n"+(Member $overlay $name)}
if(-not $Before){foreach($name in @('HasExplicitMagicValue','IsSkillTooltipText')){$source+="`n"+(Member $overlay $name)}}
else{$source+=' private static bool IsSkillTooltipText(string text){return false;}'}
$source+=@'
 private readonly Dictionary<string,string> translationCache=new Dictionary<string,string>();private readonly Queue<string> translationCacheOrder=new Queue<string>();
 class Publication{public string Original,Text,Label;public bool Floating;}
 private readonly List<Publication> publications=new List<Publication>();
 void AppendTranslation(string a,string b,ChatVisualStyle s){AppendTranslation(a,b,s,"中文",true);}
 void AppendTranslation(string a,string b,ChatVisualStyle s,string label,bool floating){publications.Add(new Publication{Original=a,Text=b,Label=label,Floating=floating});}
 static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
 static List<string> Parse(string text){return ParseChatLines(text);}
 static PreparedChatLine Prepared(string translated,string failure=""){return new PreparedChatLine{Source="Alice: selling hat 40k",SpeakerPrefix="Alice: ",Untranslated="selling hat 40k",Translated=translated,Failure=failure,CacheKey="hat",CacheAfterTranslation=true,NameTokens=new Dictionary<string,string>(),Pending=new PendingChatLine()};}
 public static int Run(string area){count=0;
 if(area=="all"||area=="parser"){
  var lines=Parse("Paimon: S> 5slots 40k // hat acc 60% 40k, Dagger att 60%\n230k Maxim m: B> Wand m.atk 60% scrolls 230k each Skaia 0-: B> pet equip speed scroll 60%");
  Check(lines.Count==3,"lower-case and punctuated badges keep three players separate");
  Check(lines[0].EndsWith("230k")&&!lines[0].Contains("Maxim"),"wrapped price before next speaker stays with its own player");
  Check(lines[1].StartsWith("Maxim:")&&!lines[1].Contains("Skaia"),"embedded Maxim does not consume next player");
  Check(lines[2].StartsWith("Skaia:"),"noisy O-dash-colon badge preserves name");
  lines=Parse("Skaia: B> pet equip speed scroll 60% / S> angel wings, Bunny Ears :) Jaysun ·07 S> Korean fan 55+3 ATK PMO");
  Check(lines.Count==2&&lines[1].StartsWith("Jaysun:"),"missing colon needs badge plus explicit trade command");
  lines=Parse("RawrbY Di: S> 2h Axe 60%, PM offer");Check(lines.Count==1&&lines[0].StartsWith("RawrbY:"),"existing letter badge still works");
  lines=Parse("Raea CH01: 3 words: Suck my D");Check(lines.Count==1&&lines[0].Contains("words:"),"internal colon is not a player");
  lines=Parse("Obliterate CH02: selective\ncoupons 6: 9k each");Check(lines.Count==1&&lines[0].Contains("coupons 6:"),"item continuation is not a player");
  lines=Parse("Chadson CH01: hello TugaStyle CH02: hi");Check(lines.Count==2,"flattened confirmed channels still split");
  lines=Parse("Alice: hello everyone\nAlice: hello everyone");Check(lines.Count==2,"two genuine repeated posts are not deduplicated");
  Check(ChatBoundaryPolicy.HasUnresolvedSpeaker("S> Bunny Ears :) Jaysun o Korean fan 55+3 ATK PMO"),"unproven missing-colon speaker is flagged instead of guessed");
  Check(!ChatBoundaryPolicy.HasUnresolvedSpeaker("I saw Jaysun on a Korean server"),"normal mention is not an unconfirmed player marker");
 }
 if(area=="all"||area=="fields"){
  Check(!LooksLikeEquipmentStatText("Absorbs the enemy's MP when attacking with Magic skills,"),"MP Eater magic skills is not equipment Magic 5");
  Check(!LooksLikeEquipmentStatText("Magic scrolls can be found in town"),"scroll prose cannot supply an OCR numeric prefix");
  Check(!LooksLikeEquipmentStatText("Magic boss is near the exit"),"letter-only word boss is not a complete value");
  Check(LooksLikeEquipmentStatText("Magic +15"),"actual compact magic stat remains supported");
  Check(LooksLikeEquipmentStatText("Magic s"),"single complete OCR digit S remains supported");
  Check(LooksLikeEquipmentStatText("Magic Def: 15"),"explicit Magic Def remains supported");
  Check(IsSkillTooltipText("MP Eater [Master Level 20] Absorbs MP"),"strong skill header vetoes equipment tooltip semantics");
  Check(IsSkillTooltipText("[Current Level 2] [Next Level 3]"),"paired skill level fields veto equipment semantics");
  Check(!IsSkillTooltipText("Req Level 20 Magic Def 15"),"equipment level is not a skill header");
 }
 if(area=="all"||area=="ink"){
  int white=unchecked((int)0xffffffff),black=unchecked((int)0xff000000),blue=unchecked((int)0xff305080);
  Check(FusionInkWeight.Blend(blue,white,0)==blue,"zero-coverage background is unchanged");
  Check(FusionInkWeight.Blend(blue,white,255)==white,"solid glyph remains opaque source ink");
  Check((FusionInkWeight.Blend(white,black,128)&255)==104,"dark glyph edge coverage rises from 128 to 151");
  Check((FusionInkWeight.Blend(black,white,128)&255)==151,"light glyph edge gets denser rather than darker");
  Check(FusionInkWeight.Blend(blue,white,1000)==white,"coverage clamps without channel overflow");
  Check(FusionInkWeight.Blend(blue,white,-1)==blue,"negative coverage cannot alter background");
 }
 if(area=="all"||area=="publication"){
  var p=new Probe();var prepared=Prepared(null,"服务商拒绝（HTTP 401）");
  Check(!p.PublishPreparedChatLine(prepared),"failed request cannot publish a successful Chinese translation");
  Check(p.publications[0].Label=="未翻译"&&p.publications[0].Text.Contains("401")&&!p.publications[0].Floating,"per-record failure stays visible and out of floating success output");
  Check(p.translationCache.Count==0,"failed source is never cached as success");
  Check(!p.PublishPreparedChatLine(Prepared("selling hat 40k")),"English echo cannot become Chinese success");
  p.translationCache["hat"]="bad";p.translationCacheOrder.Enqueue("hat");p.PublishPreparedChatLine(Prepared(null,"HTTP 429"));Check(p.translationCache.Count==0&&p.translationCacheOrder.Count==0,"invalid cached result and queue key are removed together");
  Check(p.PublishPreparedChatLine(Prepared("出售帽子，40k。"))&&p.translationCache["hat"]=="出售帽子，40k。","valid Chinese translation still publishes and caches");
  int before=p.publications.Count;p.PublishPreparedChatLine(Prepared("出售帽子，40k。"));p.PublishPreparedChatLine(Prepared("出售帽子，40k。"));Check(p.publications.Count==before+2,"success publisher does not erase genuine repeated posts");
  p.PublishPreparedChatLine(Prepared(null,"HTTP 403"));p.PublishPreparedChatLine(Prepared(null,"HTTP 502"));Check(p.publications[p.publications.Count-2].Text=="HTTP 403"&&p.publications[p.publications.Count-1].Text=="HTTP 502","each record retains its own concurrent failure reason");
 }
 if(area=="all"||area=="requests"){
  var p=new Probe();var first=new TaskCompletionSource<string>();var second=new TaskCompletionSource<string>();
  OnlineAiClient.Handler=delegate(string text){return text=="first"?first.Task:second.Task;};
  var a=p.RequestChatTranslationAsync("first","英语","简体中文","",CancellationToken.None);var b=p.RequestChatTranslationAsync("second","英语","简体中文","",CancellationToken.None);
  first.SetException(new Exception("HTTP 401"));second.SetResult("第二条成功。");
  Check(a.GetAwaiter().GetResult().Failure=="HTTP 401"&&b.GetAwaiter().GetResult().Text=="第二条成功。","concurrent result and failure state are isolated");
  OnlineAiClient.Handler=delegate(string text){return Task.FromResult(text);};var echo=p.RequestChatTranslationAsync("selling hat","英语","简体中文","",CancellationToken.None).GetAwaiter().GetResult();Check(echo.Text==null&&echo.Failure.Contains("完整性"),"provider English echo becomes explicit validation failure");
  OnlineAiSettings.Ready=false;var notReady=p.RequestChatTranslationAsync("hello","英语","简体中文","",CancellationToken.None).GetAwaiter().GetResult();Check(notReady.Text==null&&notReady.Failure=="Key missing","unready settings report their own reason");OnlineAiSettings.Ready=true;
  var pending=new TaskCompletionSource<string>();OnlineAiClient.Handler=delegate(string text){return pending.Task;};var cancel=new CancellationTokenSource();var work=p.RequestChatTranslationAsync("hello","英语","简体中文","",cancel.Token);cancel.Cancel();pending.SetResult("你好。");bool cancelled=false;try{work.GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"late response after stop cannot return success");
 }
 return count;
 }
}}
'@
$temporary=Join-Path $root ('.bench/chat-feedback-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $temporary|Out-Null
try{
 $generated=Join-Path $temporary 'Probe.cs';[IO.File]::WriteAllText($generated,$source)
 Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -Path @($generated,(Join-Path $root 'src/ChatBoundaryPolicy.cs'),(Join-Path $root 'src/FusionInkWeight.cs'))
 $count=[MapleOverlay.Probe]::Run($Area)
 Write-Output "Chat feedback correctness: $count passed; actual extracted parser/field/publication/request methods, controlled HTTP/UI/GDI boundaries; no provider call."
}finally{Remove-Item -LiteralPath $temporary -Recurse -Force}
