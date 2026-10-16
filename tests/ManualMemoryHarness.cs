using System;using System.Drawing;using System.Collections.Generic;using MapleOverlay;
public static class ManualMemoryHarness {
 static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
 public static int Run(){count=0;int w=200,h=120;int[] frame=new int[w*h];for(int i=0;i<frame.Length;i++)frame[i]=0xEEEEEE;
 var memory=new ManualTranslationMemory();DateTime now=new DateTime(2026,10,14,0,0,0,DateTimeKind.Utc);
 var apple=ManualTranslationMemory.Create(frame,w,h,new Rectangle(40,30,30,12),"apple",new object());
 memory.Store("game:skill:1",now,new List<ManualMemoryRecord>{apple});
 Check(memory.Match("game:skill:1",now,frame,w,h).Count==1,"unchanged source plus neighborhood can reuse apple while new OCR adds banana");
 int[] changed=(int[])frame.Clone();changed[31*w+42]=0x111111;
 Check(memory.Match("game:skill:1",now,changed,w,h).Count==0,"one changed source pixel rejects reuse even at identical position");
 changed=(int[])frame.Clone();changed[20*w+30]=0x111111;
 Check(memory.Match("game:skill:1",now,changed,w,h).Count==0,"changed neighboring panel context rejects identical source coordinates");
 changed=(int[])frame.Clone();changed[110*w+180]=0x111111;
 Check(memory.Match("game:skill:1",now,changed,w,h).Count==1,"unrelated game animation outside the verified neighborhood does not poison the block");
 Check(memory.Match("game:skill:2",now,frame,w,h).Count==0,"new skill identity clears old memory");
 Check(memory.Match("game:skill:1",now,frame,w,h).Count==0,"return to old context cannot resurrect a cleared record");
 memory.Store("same",now,new List<ManualMemoryRecord>{apple});Check(memory.Match("same",now.AddSeconds(31),frame,w,h).Count==0,"short memory expires after thirty seconds");
 memory.Store("same",now,new List<ManualMemoryRecord>{apple});Check(memory.Match("same",now.AddSeconds(-1),frame,w,h).Count==0,"clock rollback cannot extend memory lifetime");
 memory.Store("same",now,new List<ManualMemoryRecord>{apple});memory.Clear();Check(memory.Match("same",now,frame,w,h).Count==0,"cancel close and geometry invalidation can erase all records");
 Check(ManualTranslationMemory.Create(frame,w,h,new Rectangle(Int32.MaxValue,2,100,10),"bad",null)==null,"overflowing rectangles fail closed");
 Check(ManualTranslationMemory.Create(frame,w,h,new Rectangle(190,110,30,12),"bad",null)==null,"off-frame records cannot be reused");
 var tooMany=new List<ManualMemoryRecord>();for(int i=0;i<200;i++)tooMany.Add(apple);memory.Store("same",now,tooMany);
 Check(memory.Match("same",now,frame,w,h).Count==128,"cache has a fixed record capacity");
 return count;
 }
}
