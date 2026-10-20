param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$tree=[Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText((Join-Path $RepositoryRoot 'src/OfflineChat.cs'))).GetRoot()
$names=@('ChatIdentity','SameChatLine','IsSameTranslationSource','SplitChatIdentityParts','EditDistanceWithin','NormalizeCommonChatOcr','SameChatNumericEvidence','GetNewChatLines','GetNewChatLineIndices','ProtectPlayerNames','RestorePlayerNames','ParseChatLines','AddParsedLine','IsCompleteChatNotice','RepairChatSpeakerMarker','TryParseChatPresence','IsStructuredUiNoise','IsLikelyWrappedChatTail','IsWrappedPseudoSpeaker','IsWrappedContinuationWord','NormalizeLinkedItemName','ProtectLinkedItemNames')
$parts=@($tree.DescendantNodes()|Where-Object {$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $names -contains $_.Identifier.ValueText}|ForEach-Object {$_.ToFullString()})
$protocol=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'src/OnlineAiProtocol.cs')) -replace '(?m)^using .*;\r?\n',''
$continuity=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'src/ChatFrameContinuity.cs')) -replace '(?m)^using .*;\r?\n',''
$boundaries=[IO.File]::ReadAllText((Join-Path $RepositoryRoot 'src/ChatBoundaryPolicy.cs')) -replace '(?m)^using .*;\r?\n',''
$code='using System;using System.Collections.Generic;using System.Text;using System.Text.RegularExpressions;using System.Security.Cryptography;'+$protocol+$continuity+$boundaries+'namespace MapleOverlay {public class ChatCloseoutProbe{private HashSet<string> protectedPlayerNames=new HashSet<string>();'+($parts -join "`n")+@'
static int checks;
static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
static string Key(string source,string endpoint="https://provider.invalid/v1/chat/completions",string model="model-A",string target="简体中文",string glossary=""){return OnlineAiProtocol.BuildChatCacheKey(endpoint,model,target,source,glossary);}
public static int Run(){checks=0;
 string warning="You are lacking the MP Potion that your pet is supposed to use.";
 string player="Alice: selling gloves and scrolls, please whisper me if you would like to buy any";
 var notices=ParseChatLines(player+"\n"+warning+"\n"+warning+"\n"+warning+"\nBob: hello");
 Check(notices.Count==2&&notices[0]==player&&notices[1]=="Bob: hello","repeated pet notices cannot become wrapped player-chat tails");
 Check(ParseChatLines(warning).Count==0,"standalone MP pet notice is excluded");
 Check(ParseChatLines(warning.Replace("MP","HP")).Count==0,"standalone HP pet notice is excluded");
 notices=ParseChatLines(player+"\n  You are lacking the mp potion that your pet is supposed to use  ");
 Check(notices.Count==1&&notices[0]==player,"case and whitespace variants cannot attach to player chat");
 notices=ParseChatLines("Alice: "+warning);
 Check(notices.Count==1&&notices[0]=="Alice: "+warning,"player quoting the pet warning remains player chat");
 Check(ParseChatLines("Alice: B> MP Potion please").Count==1,"player potion trade remains player chat");
 var items=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){
  {NormalizeLinkedItemName("White Label Ring"),"白色名片戒指"},
  {NormalizeLinkedItemName("Mokbi Throwing-Stars"),"黑色利刃"}};
 Dictionary<string,string> links;
 string linked=ProtectLinkedItemNames("[White Label Ring] S>100K",items,out links);
 Check(linked=="__FYM_TERM_0__ S>100K"&&links["__FYM_TERM_0__"]=="[白色名片戒指]","whole known item link locked to dictionary translation with price outside");
 linked=ProtectLinkedItemNames("【Mokbi Throwing Stars】 S>150K",items,out links);
 Check(RestorePlayerNames(linked,links)=="【黑色利刃】 S>150K","hyphen/spacing variant and full-width brackets retain item identity");
 linked=ProtectLinkedItemNames("[Unknown Gold $& Ring] S>10K",items,out links);
 Check(RestorePlayerNames(linked,links)=="[Unknown Gold $& Ring] S>10K","unknown link preserved verbatim including literal replacement characters");
 linked=ProtectLinkedItemNames("[White Label Ring Plus] S>100K",items,out links);
 Check(links["__FYM_TERM_0__"]=="[White Label Ring Plus]","substring is not an exact item name");
 linked=ProtectLinkedItemNames("[White Label Ring] and [Mokbi Throwing Stars]",items,out links);
 Check(links.Count==2&&RestorePlayerNames(linked,links)=="[白色名片戒指] and [黑色利刃]","multiple item links restore independently");
 linked=ProtectLinkedItemNames("__FYM_TERM_0__ [Unknown Ring]",items,out links);
 Check(links.ContainsKey("__FYM_TERM_1__"),"generated link token cannot collide with source text");
 Check(ProtectLinkedItemNames("B> MP Potion please",items,out links)=="B> MP Potion please"&&links.Count==0,"ordinary unlinked chat is unchanged");
 var changes=new[]{
  new[]{"S> gloves 10k","S> gloves 20k"},new[]{"S> gloves 1.5m","S> gloves 15m"},
  new[]{"S> scroll 60%","S> scroll 60"},new[]{"S> gloves +2","S> gloves -2"},
  new[]{"R> PQ 3/4","R> PQ 2/4"},new[]{"S> gloves 10k","S> gloves 10m"},
  new[]{"S> item 1,000 mesos","S> item 100 mesos"},new[]{"HP 10 MP 20","HP 20 MP 10"},
  new[]{"item +36 (40 -4)","item +36 (40 +4)"},new[]{"wait 10sec","wait 10min"}
 };
 foreach(var pair in changes){
  string a="Alice: "+pair[0],b="Alice: "+pair[1];
  Check(!SameChatLine(a,b),"changed numeric evidence is not an old row: "+pair[0]);
  Check(!IsSameTranslationSource(a,b),"changed numeric evidence cannot be realigned as the same source: "+pair[1]);
  Check(Key(pair[0])!=Key(pair[1]),"changed value/unit/symbol cannot share cached output");
  var indices=GetNewChatLineIndices(new List<string>{a},new List<string>{a,b});
  Check(indices.Count==1&&indices[0]==1,"retained row plus updated message delivers only appended index");
 }
 Check(SameChatLine("Alice: selling gloves 10k","Alice: seliing gloves 10k"),"ordinary letter OCR tolerance remains with stable numbers");
 Check(SameChatLine("Alice: S> gloves 10k","Alice: S> gloves 10k"),"stationary frame remains identical");
 Check(SameChatNumericEvidence("value −2","value -2"),"Unicode and ASCII minus represent the same numeric evidence");
 Check(SameChatNumericEvidence("chance 60 ％","chance 60%"),"percent glyph/spacing variants remain identical");
 Check(Key("Hello")==Key("Hello"),"same request produces a stable cache identity");
 Check(Key("Hello")!=Key("Hello",model:"model-B"),"model switch isolates cached translations");
 Check(Key("Hello")!=Key("Hello",endpoint:"https://other.invalid/v1/chat/completions"),"endpoint switch isolates cached translations");
 Check(Key("Hello")!=Key("Hello",target:"英语"),"target language isolates cached translations");
 Check(Key("Hello")!=Key("Hello",glossary:"new glossary"),"glossary change isolates cached translations");
 Check(Key("Bob")!=Key("__FYM_PLAYER_0__"),"changed name protection layout cannot reuse incompatible tokens");
 Check(Key("a|b",glossary:"c")!=Key("a",glossary:"b|c"),"embedded field separators cannot collide");
 Check(Key("Hello",model:"Model-A")!=Key("Hello",model:"model-A"),"case-sensitive model IDs remain distinct");
 var p=new ChatCloseoutProbe();p.protectedPlayerNames.Add("Ann");p.protectedPlayerNames.Add("Anna");
 Dictionary<string,string> tokens;
 string text=p.ProtectPlayerNames("I cannot go, Ann and Anna are here.",out tokens);
 Check(text.StartsWith("I cannot go,"),"nickname inside ordinary word remains readable");
 Check(tokens.Count==2,"two standalone names receive two protected tokens");
 Check(RestorePlayerNames(text,tokens)=="I cannot go, Ann and Anna are here.","names restore without changing body words");
 text=p.ProtectPlayerNames("announced Ann1 xAnn _Ann",out tokens);
 Check(tokens.Count==0&&text=="announced Ann1 xAnn _Ann","longer usernames and words are not partial nickname matches");
 text=p.ProtectPlayerNames("(Ann), Ann!",out tokens);
 Check(tokens.Count==1&&RestorePlayerNames(text,tokens)=="(Ann), Ann!","punctuation bounded repeated nickname remains protected");
 var c=new ChatFrameContinuity();bool advance;
 var before=new List<string>{"Alice: S> gloves 10k"};var after=new List<string>{"Alice: S> gloves 20k"};
 var fresh=GetNewChatLineIndices(before,after);
 var first=c.ResolveIndices(before,after,fresh,SameChatLine,out advance);
 Check(first.Count==0&&!advance,"unanchored changed viewport still requires confirmation");
 var second=c.ResolveIndices(before,after,fresh,SameChatLine,out advance);
 Check(second.Count==1&&second[0]==0&&advance,"confirmed replacement viewport delivers the changed price");
 return checks;
}}
}
'@
Add-Type -TypeDefinition $code -IgnoreWarnings -WarningAction SilentlyContinue
$count=[MapleOverlay.ChatCloseoutProbe]::Run()
if($count -ne 72){throw "Unexpected check count $count"}
# Inspect production wiring in addition to executing the extracted methods.
$process=@($tree.DescendantNodes()|Where-Object {$_ -is [Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax] -and $_.Identifier.ValueText -eq 'ProcessPendingChatAsync'})[0].ToString()
if(-not $process.Contains('BuildChatCacheKey(online.Endpoint') -or -not $process.Contains('RequestChatTranslationAsync(online, protectedMessage')){throw 'Live cache/request do not use the same settings snapshot'}
Write-Output 'Chat closeout: 73/73 passed (72 executed method checks + production snapshot wiring); no live provider/OCR/Windows UI claim.'
