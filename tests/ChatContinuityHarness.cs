using System;
using System.Collections.Generic;
using MapleOverlay;
public static class ChatContinuityHarness
{
    private static int count;
    private static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
    private static List<string> Rows(params string[] rows){return new List<string>(rows);}
    public static int Run()
    {
        count=0;var tracker=new ChatFrameContinuity();bool advance;
        Func<string,string,bool> same=delegate(string a,string b){return a==b;};
        var result=tracker.Resolve(Rows(),Rows("first"),Rows("first"),same,out advance);
        Check(advance&&result.Count==1,"first nonempty frame advances");
        result=tracker.Resolve(Rows("first"),Rows("second"),Rows(),same,out advance);
        Check(!advance&&result.Count==0,"unanchored single-row replacement waits for evidence");
        result=tracker.Resolve(Rows("first"),Rows("second"),Rows(),same,out advance);
        Check(advance&&result.Count==1&&result[0]=="second","stable same-size replacement is delivered");
        result=tracker.Resolve(Rows("second"),Rows("second"),Rows(),same,out advance);
        Check(advance&&result.Count==0,"static frame is not delivered again");
        result=tracker.Resolve(Rows("second"),Rows(),Rows(),same,out advance);
        Check(!advance&&result.Count==0,"empty OCR frame cannot erase last stable anchor");
        result=tracker.Resolve(Rows("old-a","old-b"),Rows("new-a","new-b"),Rows(),same,out advance);
        Check(!advance,"full viewport replacement waits");
        result=tracker.Resolve(Rows("old-a","old-b"),Rows("new-b","new-c"),Rows(),same,out advance);
        Check(advance&&result.Count==2&&result[1]=="new-c","scrolling new viewport confirms through shared new row");
        result=tracker.Resolve(Rows("new-b","new-c"),Rows("new-c","new-d"),Rows("new-d"),same,out advance);
        Check(advance&&result.Count==1&&result[0]=="new-d","anchored append preserves original diff");
        tracker.Resolve(Rows("old"),Rows("pending"),Rows(),same,out advance);tracker.Reset();
        result=tracker.Resolve(Rows("old"),Rows("pending"),Rows(),same,out advance);
        Check(!advance&&result.Count==0,"stop or region change clears pending evidence");
        return count;
    }
}
