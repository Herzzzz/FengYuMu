
public static class PrimaryOcrProtocolHarness {
 public static bool ValidateInstalled(string path){return MapleOverlay.PrimaryOcrClient.Validate(path);}
 static int count;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
 static Dictionary<string,object> B(string text,double x=10,double y=10,double w=70,double h=14){return new Dictionary<string,object>{{"text",text},{"x",x},{"y",y},{"width",w},{"height",h}};}
 static Dictionary<string,object> Line(){var d=B("LUK +2");d["confidence"]=18.0;d["words"]=new object[]{B("LUK",10,10,30,14),B("+2",50,10,20,14)};return d;}
 static Dictionary<string,object> Payload(Dictionary<string,object> line){return new Dictionary<string,object>{{"ok",true},{"lines",new object[]{line}}};}
 static MapleOverlay.OcrResult Decode(Dictionary<string,object> line){return MapleOverlay.OcrResult.FromPayload(Payload(line),new Size(120,80));}
 static void Reject(Action action,string name){bool failed=false;try{action();}catch(FormatException){failed=true;}catch(InvalidOperationException){failed=true;}Check(failed,name);}
 public static int Run(){
  Check(MapleOverlay.PrimaryOcrClient.AsciiJson("\"路径😀\"")=="\"\\u8def\\u5f84\\ud83d\\ude00\"","Unicode path is ASCII JSON with preserved surrogate pairs");
  Check(MapleOverlay.PrimaryOcrClient.AsciiJson("{\"path\":\"C:\\\\Temp\"}")=="{\"path\":\"C:\\\\Temp\"}","ASCII JSON escapes remain unchanged");
  var result=Decode(Line());Check(result.Text=="LUK +2"&&result.Lines[0].Words[1].Text=="+2","numeric source survives payload");
  Check(result.Lines[0].Words[1].BoundingRect.X==50,"CTC word position preserved");
  var d=Line();d["confidence"]=0.41;Check(Decode(d).Lines.Count==0,"low score ignored");
  d=Line();d["confidence"]=0.42;Check(Decode(d).Lines.Count==1,"threshold inclusive");
  d=Line();d["confidence"]=-1;Check(Decode(d).Lines.Count==0,"negative raw logit ignored");
  d=Line();d["confidence"]=Double.NaN;Reject(()=>Decode(d),"NaN score refused");
  d=Line();d["x"]=Double.PositiveInfinity;Reject(()=>Decode(d),"infinite geometry refused");
  d=Line();d["x"]=-1;Reject(()=>Decode(d),"negative geometry refused");
  d=Line();d["width"]=999;Reject(()=>Decode(d),"outside image refused");
  d=Line();d["height"]=0;Reject(()=>Decode(d),"empty box refused");
  d=Line();d["words"]=new object[]{B("LUK +20")};Reject(()=>Decode(d),"invented numeric word refused");
  d=Line();d["words"]=new object[]{B("LUK"),B("+2",90,10,20,14)};Reject(()=>Decode(d),"word outside line refused");
  d=Line();d["words"]=new object[0];Check(Decode(d).Lines.Count==0,"missing alignment never authorizes erase");
  d=Line();d.Remove("words");Reject(()=>Decode(d),"missing words refused");
  d=Line();d["words"]=new object[]{null};Reject(()=>Decode(d),"null word refused");
  d=Line();d["confidence"]="0.9";Reject(()=>Decode(d),"string score refused");
  var root=Payload(Line());root["lines"]=new object[513];Reject(()=>MapleOverlay.OcrResult.FromPayload(root,new Size(120,80)),"line limit enforced");
  root=Payload(Line());root["ok"]=false;Reject(()=>MapleOverlay.OcrResult.FromPayload(root,new Size(120,80)),"worker failure refused");
  Reject(()=>MapleOverlay.OcrResult.FromPayload(null,new Size(120,80)),"null payload refused");
  var path=Path.Combine(Path.GetTempPath(),"fengyumu-ocr-protocol-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);
  try{
   string[] names={"PrimaryOcrHelper.exe","Sdcb.SimdPaddleOCR.dll","Sdcb.SimdPaddleOCR.ModelProvider.dll","Sdcb.SimdPaddleOCR.Models.ChineseV6Small.dll"};
   foreach(var name in names)File.WriteAllText(Path.Combine(path,name),"fixture "+name);
   Action manifest=()=>{var s=new StringBuilder();foreach(var name in names)s.AppendLine(name+"\t"+MapleOverlay.UpdateFileTransaction.Hash(Path.Combine(path,name)));File.WriteAllText(Path.Combine(path,"model-manifest.tsv"),s.ToString());};
   Check(!MapleOverlay.PrimaryOcrClient.Validate(path),"missing manifest refused");manifest();Check(MapleOverlay.PrimaryOcrClient.Validate(path),"complete component accepted");
   File.AppendAllText(Path.Combine(path,names[0]),"changed");Check(!MapleOverlay.PrimaryOcrClient.Validate(path),"changed helper refused");manifest();
   File.WriteAllText(Path.Combine(path,"unexpected.dll"),"extra");Check(!MapleOverlay.PrimaryOcrClient.Validate(path),"unlisted file refused");File.Delete(Path.Combine(path,"unexpected.dll"));
   File.AppendAllText(Path.Combine(path,"model-manifest.tsv"),names[0]+"\t"+new string('0',64)+"\n");Check(!MapleOverlay.PrimaryOcrClient.Validate(path),"duplicate manifest entry refused");manifest();
   File.AppendAllText(Path.Combine(path,"model-manifest.tsv"),"../escape.dll\t"+new string('0',64)+"\n");Check(!MapleOverlay.PrimaryOcrClient.Validate(path),"manifest escape refused");manifest();
   Directory.CreateDirectory(Path.Combine(path,"licenses"));string license=Path.Combine(path,"licenses","NOTICE.txt");File.WriteAllText(license,"fixture notice");
   string entry="licenses/NOTICE.txt\t"+MapleOverlay.UpdateFileTransaction.Hash(license)+"\n";File.AppendAllText(Path.Combine(path,"model-manifest.tsv"),entry);Check(MapleOverlay.PrimaryOcrClient.Validate(path),"portable nested manifest path accepted");
   manifest();File.AppendAllText(Path.Combine(path,"model-manifest.tsv"),entry.Replace('/','\\'));Check(MapleOverlay.PrimaryOcrClient.Validate(path),"Windows nested manifest path accepted");
   manifest();File.Delete(license);Directory.Delete(Path.Combine(path,"licenses"));File.AppendAllText(Path.Combine(path,"model-manifest.tsv"),"..\\escape.dll\t"+new string('0',64)+"\n");Check(!MapleOverlay.PrimaryOcrClient.Validate(path),"Windows separator escape refused");manifest();
   File.Delete(Path.Combine(path,names[3]));Check(!MapleOverlay.PrimaryOcrClient.Validate(path),"missing model refused");
  }finally{Directory.Delete(path,true);}
  return count;
 }
}
