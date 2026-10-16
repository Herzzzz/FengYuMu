using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using MapleOverlay;

public static class SceneFeedbackSafetyHarness
{
    private static int count;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS: " + name); }
    private static Dictionary<string, object> Obj(params object[] entries)
    {
        var value = new Dictionary<string, object>();
        for (int i = 0; i < entries.Length; i += 2) value[(string)entries[i]] = entries[i+1];
        return value;
    }
    private static Dictionary<string, object> Chat(object content, string reason)
    { return Obj("choices", new object[] { Obj("message", Obj("content", content), "finish_reason", reason) }); }
    private static void Reject(Dictionary<string, object> root, string expected)
    {
        try { OnlineAiResponse.Extract(root); throw new Exception("Response accepted"); }
        catch (OnlineAiResponseException ex) { Check(ex.Message.Contains(expected) && !ex.Message.Contains("sk-test-secret"), expected); }
    }
    public static int Run()
    {
        count = 0;
        Check(SceneTranslationPolicy.PulseOpacity(0)==0 && SceneTranslationPolicy.PulseOpacity(320)==0,"pulse starts and ends transparent");
        Check(Math.Abs(SceneTranslationPolicy.PulseOpacity(160)-0.055)<0.000001,"whole-client pulse remains subtle");
        Check(SceneTranslationPolicy.PulseOpacity(-10)==0 && SceneTranslationPolicy.PulseOpacity(1000)==0,"pulse never loops or leaks beyond duration");
        Check(SceneTranslationPolicy.PulseOpacity(Double.NaN)==0,"invalid pulse time stays transparent");
        int[] original = new int[288], current = new int[288];
        for (int i=0; i<288; i++) original[i]=current[i]=0xEEEEEE;
        Check(!SceneTranslationPolicy.PatchChanged(original,current), "unchanged source retains overlay");
        for (int i=0; i<288; i++) current[i]=0xECECEC;
        Check(!SceneTranslationPolicy.PatchChanged(original,current), "small compositor variation does not retire text");
        for (int i=0; i<288; i++) current[i]=0x203040;
        Check(SceneTranslationPolicy.PatchChanged(original,current), "closed light panel on world pixels retires text");
        for (int i=0; i<288; i++) current[i]=i<72 ? 0x202020:original[i];
        Check(SceneTranslationPolicy.PatchChanged(original,current), "substantial local source replacement retires text");
        for (int i=0; i<288; i++) current[i]=i<10 ? 0x202020:original[i];
        Check(!SceneTranslationPolicy.PatchChanged(original,current), "small live counter changes are tolerated");
        Check(SceneTranslationPolicy.PatchChanged(original,new int[16]), "different evidence sizes fail closed");
        Check(SceneTranslationPolicy.PatchChanged(new int[1],new int[1]), "inadequate evidence fails closed");
        RectangleF client = new RectangleF(4,4,1192,792), anchor = new RectangleF(100,100,30,12);
        RectangleF title = SceneTranslationPolicy.CardBounds(anchor,new SizeF(160,20),new SizeF(160,20),client,560);
        Check(title.Width>=176 && title.Height>=32,"title card grows for readable translation");
        RectangleF prose = SceneTranslationPolicy.CardBounds(anchor,new SizeF(3000,20),new SizeF(530,320),client,560);
        Check(prose.Width==560 && prose.Height>=332,"prose height follows wrapped translation rather than source rows");
        RectangleF edge = SceneTranslationPolicy.CardBounds(new RectangleF(1180,780,10,10),new SizeF(180,20),new SizeF(180,20),client,560);
        Check(client.Contains(edge),"edge card remains inside locked client");
        Check(SceneTranslationPolicy.CardBounds(anchor,new SizeF(3000,20),new SizeF(530,1000),client,560).IsEmpty,"oversize prose explicitly refuses clipped layout");
        Check(SceneTranslationPolicy.CardBounds(anchor,new SizeF(Single.NaN,20),new SizeF(20,20),client,560).IsEmpty,"non-finite measure rejected");
        Check(SceneTranslationPolicy.CardBounds(anchor,new SizeF(10,10),new SizeF(10,10),new RectangleF(0,0,20,20),560).IsEmpty,"invalid tiny client rejected");
        var occupied = new List<RectangleF>{title};
        RectangleF placed=SceneTranslationPolicy.PlaceCard(title,occupied,client);
        Check(!placed.IsEmpty && client.Contains(placed) && !title.IntersectsWith(placed),"adjacent translation cards do not overlap");
        Check(SceneTranslationPolicy.PlaceCard(title,new List<RectangleF>{client},client).IsEmpty,"full viewport reports no placement");
        Check(SceneTranslationPolicy.PlaceCard(title,new List<RectangleF>(),client)==title,"free card preserves original anchor");
        Check(OnlineAiResponse.Extract(Chat("Hello there!","stop"))=="Hello there!","standard chat completion parsed");
        var arrayList=Obj("choices",new ArrayList{Obj("message",Obj("content","你好"))});
        Check(OnlineAiResponse.Extract(arrayList)=="你好","JavaScriptSerializer ArrayList response parsed");
        Check(OnlineAiResponse.Extract(Chat(new object[]{Obj("type","text","text","Hello"),Obj("type","text","text"," world")},"stop"))=="Hello world","structured chat text joined");
        var responses=Obj("output",new object[]{Obj("type","reasoning","summary",new object[]{Obj("type","summary_text","text","sk-test-secret")}),Obj("type","message","content",new object[]{Obj("type","output_text","text","Hello"),Obj("type","output_text","text"," again")})});
        Check(OnlineAiResponse.Extract(responses)=="Hello again","responses text joined without reasoning summary");
        Check(OnlineAiResponse.Extract(Obj("output_text","完成"))=="完成","top-level output text parsed");
        Reject(Chat("","length"),"输出额度耗尽");
        Reject(Obj("status","incomplete","incomplete_details",Obj("reason","sk-test-secret")),"响应未完成");
        Reject(Obj("choices",new object[]{Obj("message",Obj("reasoning_content","sk-test-secret"))}),"没有最终译文");
        Reject(null,"有效 JSON 对象");
        Check(OnlineAiProtocol.SafeDiagnosticToken("sk-test-secret","sk-test-secret")=="redacted","key in provider metadata redacted");
        Check(OnlineAiProtocol.SafeDiagnosticToken("request-sk-test-secret-id","sk-test-secret")=="redacted","embedded key redacted");
        Check(OnlineAiProtocol.SafeDiagnosticToken("model_not_found","sk-test-secret")=="model_not_found","safe provider error code preserved");
        return count;
    }
}
