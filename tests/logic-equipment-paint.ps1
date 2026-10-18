param([string]$OutputSource)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$generated=Join-Path $env:TEMP ('equipment-paint-' + [Guid]::NewGuid().ToString('N') + '.cs')
& (Join-Path $PSScriptRoot 'logic-simple-flow-r11.ps1') -OutputSource $generated -GenerateOnly
$source=[IO.File]::ReadAllText($generated)
Remove-Item -LiteralPath $generated -Force

$source += @'
namespace MapleOverlay {
 partial class OverlayForm {
  static int equipmentPaintChecks;
  static void EqCheck(bool ok,string name){if(!ok)throw new Exception(name);equipmentPaintChecks++;Console.WriteLine("PASS: "+name);}
  static EquipmentPanelPlan EqPanel(RectangleF panel,float sourceHeight){
   var p=new EquipmentPanelPlan{PanelBounds=panel,RecognitionReason="synthetic-equipment-row"};
   p.Typography=EquipmentPanelTypography.ForRows(
    new RectangleF[]{new RectangleF(panel.X,panel.Y,panel.Width,sourceHeight)},panel);
   return p;
  }
  static OverlayLabel EqLabelIndex(string text,int index,RectangleF key,RectangleF value,
      RectangleF row,EquipmentPanelPlan panel,bool measured){
   var fields=EquipmentFieldGrammar.Parse(text,true);
   if(fields.Count<=index)throw new Exception("fixture did not parse: "+text);
   var plan=EquipmentPanelLayout.BuildField(fields[index],text,key,value,row,panel.PanelBounds,
    panel.Typography,measured);
   if(plan==null)throw new Exception("fixture did not plan: "+text);
   panel.Add(plan);
   return new OverlayLabel{Bounds=plan.KeySourceBounds,SourceBounds=plan.KeySourceBounds,
    SourceLineBounds=new List<RectangleF>{plan.KeySourceBounds},
    KeepClearBounds=plan.ValueGuardBounds,PanelBounds=plan.PanelBounds,
    Text=plan.Translation,SemanticRole="item-field",MatchSource=plan.MatchSource,
    Confidence=LabelConfidence.High,EquipmentField=plan,EquipmentPanel=panel};
  }
  static OverlayLabel EqLabel(string text,RectangleF key,RectangleF value,RectangleF row,
      EquipmentPanelPlan panel){
   return EqLabelIndex(text,0,key,value,row,panel,true);
  }
  static OverlayForm EqForm(Rectangle frame){
   var p=new OverlayForm();p.Bounds=Rectangle.Empty;p.gameBounds=p.captureBounds=frame;
   p.ClientRectangle=frame;p.recognitionGeneration=1;p.visibleTranslation=true;
   p.visibleRequestWatch=Stopwatch.StartNew();return p;
  }
  static Bitmap EqFrame(int w,int h,List<OverlayLabel> labels,List<int[]> values,List<int> colors){
   int[] p=Enumerable.Repeat(unchecked((int)0xff202838),w*h).ToArray();
   for(int y=30;y<h-30;y++)for(int x=50;x<w-50;x++)p[y*w+x]=unchecked((int)0xff2d3f58);
   foreach(var label in labels){
    var r=Rectangle.Ceiling(label.SourceBounds);
    for(int y=r.Top+3;y<r.Bottom-2;y+=3)
     for(int x=r.Left+3;x<r.Right-2;x+=4)p[y*w+x]=unchecked((int)0xfff2f2f2);
   }
   for(int i=0;i<values.Count;i++){
    int[] v=values[i];
    for(int y=v[1];y<v[3];y++)for(int x=v[0];x<v[2];x++)p[y*w+x]=colors[i];
   }
   var b=new Bitmap(w,h);b.Set(p);return b;
  }
  static int EqMarker(int w,int[] v){return ((v[1]+v[3])/2)*w+(v[0]+v[2])/2;}
  public static int RunEquipmentPaint(){
   equipmentPaintChecks=0;
   string[] rows={
    "Weapon Def.: +16","Magic Def.: +18","INT: +1","LUK: +2",
    "Remaining Enhancements: 7","Weapon Attack: +36 (40 -4)",
    "Critical Rate: +5%","Evasion: +4","Speed: +3","Attack Speed: Fast",
    "Upgrade Slots: 2","Magic Attack: -5"};
   int w=900,h=rows.Length*34+90;
   var panel=EqPanel(new RectangleF(50,25,800,h-50),18);
   var labels=new List<OverlayLabel>();var values=new List<int[]>();var colors=new List<int>();
   int valueColor=unchecked((int)0xffff8c00);
   for(int i=0;i<rows.Length;i++){
    string text=rows[i];int y=45+i*34;int nameChars=text.IndexOf(':');
    if(nameChars<2)nameChars=text.Length-2;
    float keyWidth=Math.Max(46,(float)nameChars*8.0f);
    RectangleF key=new RectangleF(90,y,keyWidth,18);
    RectangleF value=new RectangleF(90+keyWidth+12,y,Math.Max(28,text.Length*2.0f),18);
    labels.Add(EqLabel(text,key,value,new RectangleF(80,y-2,700,22),panel));
    values.Add(new int[]{(int)value.Left,(int)value.Top,(int)value.Right,(int)value.Bottom});
    colors.Add(valueColor);valueColor=unchecked((int)(0xff000000|((valueColor+0x001f2a3d)&0x00ffffff)));
   }
   EqCheck(labels.Count==rows.Length,"all generic equipment rows produce a prepared label");
   using(var frame=EqFrame(w,h,labels,values,colors)){
    var form=EqForm(new Rectangle(0,0,w,h));
    var ok=form.PrepareSimpleFusionAsync(frame,Rectangle.Empty,labels).GetAwaiter().GetResult();
    EqCheck(ok,"equipment composition commits");
    EqCheck(form.simpleReady.Count==labels.Count,"all equipment fields that fit are painted");
    int[] layer=form.simpleLayer.Pixels();
    int preservedPixels=0,totalValuePixels=0;
    for(int i=0;i<values.Count;i++){
     int[] v=values[i];
     for(int y=v[1];y<v[3];y++)for(int x=v[0];x<v[2];x++){
      totalValuePixels++;
      if(layer[y*w+x]==0)preservedPixels++;
     }
    }
    EqCheck(totalValuePixels>=48&&preservedPixels==totalValuePixels,
      "every pixel of every value region (sign, correction and colour) stays original");
    int painted=0;
    foreach(var label in labels)if(label.EquipmentField.Status=="painted"&&label.EquipmentField.PreparedFontSize>=11.0f)painted++;
    EqCheck(painted==labels.Count,"every equipment label records a painted stage and readable font size");
    var sizes=new HashSet<float>();
    foreach(var label in labels)sizes.Add(label.EquipmentField.PreparedFontSize);
    EqCheck(sizes.Count==1,"one shared field font size is used across the panel");
    EqCheck(panel.Typography.Family=="Microsoft YaHei"&&panel.Typography.Unit==GraphicsUnit.Pixel,
      "equipment typography keeps the shared FONT family/pixel unit");
    string[] expected={"+16","+18","+1","+2","7","+36 (40 -4)","+5%","+4","+3","Fast","2","-5"};
    int exact=0,guarded=0;
    for(int i=0;i<labels.Count;i++){
     var field=labels[i].EquipmentField;
     if(field.OriginalValue==expected[i])exact++;
     if(field.ValuePixelsPreserved&&!field.ValueGuardBounds.IsEmpty)guarded++;
    }
    EqCheck(exact==labels.Count,"every prepared field retains the exact observed value string");
    EqCheck(guarded==labels.Count,"every value has a measured pixel guard");
    EqCheck(panel.Painted==labels.Count&&panel.Preserved==0,
      "the shared panel plan accounts for painted and preserved fields");
    EqCheck(form.Captures==0&&form.OcrCalls==0,"equipment composition adds no capture or OCR round");
    EqCheck(Program.Logs.Exists(x=>x.Contains("equipment-paint")&&x.Contains("prepared="+labels.Count)&&
      x.Contains("painted="+labels.Count)&&x.Contains("preserved=0")),"equipment paint evidence distinguishes painted and preserved");
    form.labels=labels;using(var destination=new Bitmap(w,h))using(var g=Graphics.FromImage(destination)){
     EqCheck(form.PaintSimpleFusion(g)&&form.lastPaintedTranslationCount==labels.Count,
       "one paint pass publishes every prepared equipment field");
     bool allPainted=true;foreach(var label in labels)if(!label.PaintedLastPass)allPainted=false;
     EqCheck(allPainted,"paint counters identify the actual equipment labels");
    }
    form.ClearSimpleFusion();
   }
   // EQUIP-R1: an estimated merged-word boundary must not paint. The composition
   // guard preserves the whole numeric region even if a caller supplied a label.
   {
    var estimatedPanel=EqPanel(new RectangleF(50,25,760,120),18);
    var valid=EqLabel("INT: +1",new RectangleF(90,50,90,18),new RectangleF(200,50,30,18),
      new RectangleF(80,48,300,22),estimatedPanel);
    var estimated=EqLabelIndex("WeaponDef-5",0,new RectangleF(90,90,100,18),
      new RectangleF(200,90,12,4),new RectangleF(80,88,300,22),estimatedPanel,false);
    var estimatedValues=new List<int[]>{new int[]{200,90,212,94}};
    using(var frame=EqFrame(860,220,new List<OverlayLabel>{valid,estimated},estimatedValues,
      new List<int>{unchecked((int)0xff44ccff)})){
     var form=EqForm(new Rectangle(0,0,860,220));
     var ok=form.PrepareSimpleFusionAsync(frame,Rectangle.Empty,
       new List<OverlayLabel>{valid,estimated}).GetAwaiter().GetResult();
     EqCheck(ok&&form.simpleReady.Contains(valid)&&!form.simpleReady.Contains(estimated),
       "EQUIP-R1 estimated boundary label is not painted");
     EqCheck(estimated.FusionFailureReason=="equipment-field-boundary-estimated",
       "EQUIP-R1 estimated boundary records its preserve reason");
     int[] layer=form.simpleLayer.Pixels();
     int preservedPixels=0;
     for(int y=90;y<94;y++)for(int x=200;x<212;x++)if(layer[y*860+x]==0)preservedPixels++;
     EqCheck(preservedPixels==48,"EQUIP-R1 all 48 numeric marker pixels stay original");
     form.ClearSimpleFusion();
    }
   }
   // EQUIP-R1a: an independent sign word is inside the value guard. The whole
   // signed value region, including the sign, must stay original.
   {
    var signPanel=EqPanel(new RectangleF(50,25,500,120),18);
    var signed=EqLabel("WDEF : \u2212 5",new RectangleF(90,50,60,18),
      new RectangleF(155,50,20,18),new RectangleF(80,48,260,22),signPanel);
    using(var frame=EqFrame(580,180,new List<OverlayLabel>{signed},
      new List<int[]>{new int[]{155,50,175,68}},new List<int>{unchecked((int)0xffffbb22)})){
     var form=EqForm(new Rectangle(0,0,580,180));
     var ok=form.PrepareSimpleFusionAsync(frame,Rectangle.Empty,
       new List<OverlayLabel>{signed}).GetAwaiter().GetResult();
     EqCheck(ok&&form.simpleReady.Contains(signed),"EQUIP-R1a independent sign field paints from the measured range");
     int[] layer=form.simpleLayer.Pixels();
     int preserved=0;
     for(int y=50;y<68;y++)for(int x=155;x<175;x++)if(layer[y*580+x]==0)preserved++;
     EqCheck(preserved==18*20&&signed.EquipmentField.OriginalValue=="\u2212 5",
       "EQUIP-R1a the complete signed value region stays original");
     EqCheck(signed.KeepClearBounds.Left<=155&&signed.KeepClearBounds.Right>=175,
       "EQUIP-R1a composition guard covers the independent sign");
     form.ClearSimpleFusion();
    }
   }
   // EQUIP-R3: adjacent no-space fields with measured word boxes both paint; the
   // STR value guard must stop before INT and must not trigger a live-value conflict.
   {
    var contiguousPanel=EqPanel(new RectangleF(50,25,600,120),18);
    var strength=EqLabelIndex("STR+5INT+3",0,new RectangleF(90,50,30,18),
      new RectangleF(125,50,20,18),new RectangleF(80,48,220,22),contiguousPanel,true);
    var intelligence=EqLabelIndex("STR+5INT+3",1,new RectangleF(150,50,30,18),
      new RectangleF(185,50,20,18),new RectangleF(80,48,220,22),contiguousPanel,true);
    using(var frame=EqFrame(680,180,new List<OverlayLabel>{strength,intelligence},
      new List<int[]>{new int[]{125,50,145,68},new int[]{185,50,205,68}},
      new List<int>{unchecked((int)0xffffaa33),unchecked((int)0xff33aaff)})){
     var form=EqForm(new Rectangle(0,0,680,180));
     var ok=form.PrepareSimpleFusionAsync(frame,Rectangle.Empty,
       new List<OverlayLabel>{strength,intelligence}).GetAwaiter().GetResult();
     EqCheck(ok&&form.simpleReady.Contains(strength)&&form.simpleReady.Contains(intelligence),
       "EQUIP-R3 contiguous STR+5INT+3 paints both fields");
     EqCheck(strength.FusionFailureReason==""&&intelligence.FusionFailureReason=="",
       "EQUIP-R3 no source-or-live-value-conflict for contiguous fields");
     int[] layer=form.simpleLayer.Pixels();
     int preserved=0;
     for(int y=50;y<68;y++)for(int x=125;x<145;x++)if(layer[y*680+x]==0)preserved++;
     for(int y=50;y<68;y++)for(int x=185;x<205;x++)if(layer[y*680+x]==0)preserved++;
     EqCheck(preserved==2*18*20,"EQUIP-R3 both complete value regions stay original");
     form.ClearSimpleFusion();
    }
   }
   // EQUIP-R3a: INT follows the OCR digit O in STR:+1OINT:+3. Both fields must
   // paint and the STR guard must not touch the INT key.
   {
    var ocrDigitPanel=EqPanel(new RectangleF(50,25,600,120),18);
    var strength=EqLabelIndex("STR:+1OINT:+3",0,new RectangleF(90,50,30,18),
      new RectangleF(125,50,22,18),new RectangleF(80,48,220,22),ocrDigitPanel,true);
    var intelligence=EqLabelIndex("STR:+1OINT:+3",1,new RectangleF(150,50,30,18),
      new RectangleF(185,50,22,18),new RectangleF(80,48,220,22),ocrDigitPanel,true);
    using(var frame=EqFrame(680,180,new List<OverlayLabel>{strength,intelligence},
      new List<int[]>{new int[]{125,50,147,68},new int[]{185,50,207,68}},
      new List<int>{unchecked((int)0xffff8833),unchecked((int)0xff3388ff)})){
     var form=EqForm(new Rectangle(0,0,680,180));
     var ok=form.PrepareSimpleFusionAsync(frame,Rectangle.Empty,
       new List<OverlayLabel>{strength,intelligence}).GetAwaiter().GetResult();
     EqCheck(ok&&form.simpleReady.Contains(strength)&&form.simpleReady.Contains(intelligence),
       "EQUIP-R3a STR:+1OINT:+3 paints both fields");
     EqCheck(strength.EquipmentField.OriginalValue=="+1O"&&strength.FusionFailureReason==""&&
       intelligence.FusionFailureReason=="","EQUIP-R3a STR keeps +1O and INT is not blocked");
     int[] layer=form.simpleLayer.Pixels();
     int preserved=0;
     for(int y=50;y<68;y++)for(int x=125;x<147;x++)if(layer[y*680+x]==0)preserved++;
     for(int y=50;y<68;y++)for(int x=185;x<207;x++)if(layer[y*680+x]==0)preserved++;
     EqCheck(preserved==2*18*22,"EQUIP-R3a both complete value regions stay original");
     form.ClearSimpleFusion();
    }
   }
   // EQUIP-R2: the panel decides one size; a wide key cannot shrink independently.
   {
    var sharedPanel=EqPanel(new RectangleF(50,25,800,180),22);
    EqCheck(sharedPanel.Typography.FieldSize==16,"EQUIP-R2 fixture shares a 16px panel size");
    var narrow=EqLabel("INT: +1",new RectangleF(90,50,90,22),new RectangleF(230,50,30,22),
      new RectangleF(80,48,260,26),sharedPanel);
    var wide=EqLabel("Weapon Def.: +16",new RectangleF(90,90,150,22),new RectangleF(260,90,40,22),
      new RectangleF(80,88,300,26),sharedPanel);
    using(var frame=EqFrame(900,260,new List<OverlayLabel>{narrow,wide},
      new List<int[]>{new int[]{230,50,260,72},new int[]{260,90,300,112}},
      new List<int>{unchecked((int)0xffff8844),unchecked((int)0xff44aaff)})){
     var form=EqForm(new Rectangle(0,0,900,260));
     var ok=form.PrepareSimpleFusionAsync(frame,Rectangle.Empty,
       new List<OverlayLabel>{narrow,wide}).GetAwaiter().GetResult();
     EqCheck(ok&&form.simpleReady.Contains(narrow)&&form.simpleReady.Contains(wide),
       "EQUIP-R2 narrow and wide keys both paint at the shared size");
     EqCheck(narrow.EquipmentField.PreparedFontSize==16&&wide.EquipmentField.PreparedFontSize==16,
       "EQUIP-R2 narrow 智力 and wide 物理防御力 both use 16px, not 16/14");
     EqCheck(Program.Logs.Exists(x=>x.Contains("equipment-paint")&&x.Contains("panelSize=16")&&
       x.Contains("prepared=2")&&x.Contains("painted=2")),
       "EQUIP-R2 paint evidence records the single 16px panel decision");
     form.ClearSimpleFusion();
    }
    var mixedPanel=EqPanel(new RectangleF(50,25,800,180),22);
    var fits=EqLabel("INT: +1",new RectangleF(90,50,80,22),new RectangleF(180,50,25,22),
      new RectangleF(80,48,260,26),mixedPanel);
    var tooWide=EqLabel("Weapon Def.: +16",new RectangleF(90,90,24,22),new RectangleF(120,90,30,22),
      new RectangleF(80,88,260,26),mixedPanel);
    using(var frame=EqFrame(900,260,new List<OverlayLabel>{fits,tooWide},
      new List<int[]>{new int[]{180,50,205,72},new int[]{120,90,150,112}},
      new List<int>{unchecked((int)0xff77dd88),unchecked((int)0xffdd77cc)})){
     var form=EqForm(new Rectangle(0,0,900,260));
     var ok=form.PrepareSimpleFusionAsync(frame,Rectangle.Empty,
       new List<OverlayLabel>{fits,tooWide}).GetAwaiter().GetResult();
     EqCheck(ok&&form.simpleReady.Contains(fits)&&!form.simpleReady.Contains(tooWide),
       "EQUIP-R2 wide key that cannot fit is preserved while the narrow key paints");
     EqCheck(tooWide.FusionFailureReason=="equipment-field-does-not-fit"&&
       tooWide.EquipmentField.PanelFontSize==16&&tooWide.EquipmentField.PreparedFontSize==0,
       "EQUIP-R2 preserved wide key keeps the 16px panel decision and records no shrink");
     form.ClearSimpleFusion();
    }
   }
   // A key that cannot fit at the shared floor must preserve the original and report a reason.
   {
    var preservationPanel=EqPanel(new RectangleF(50,25,700,100),18);
    var label=EqLabel("Weapon Def.: +16",new RectangleF(90,50,24,18),new RectangleF(140,50,40,18),
     new RectangleF(80,48,300,22),preservationPanel);
    label.Text="这是一个无法在二十四个像素内完整显示的装备属性名称";
    label.EquipmentField.Translation=label.Text;
    using(var frame=EqFrame(800,180,new List<OverlayLabel>{label},
      new List<int[]>{new int[]{140,50,180,68}},new List<int>{unchecked((int)0xffff3300)})){
     var form=EqForm(new Rectangle(0,0,800,180));
     var ok=form.PrepareSimpleFusionAsync(frame,Rectangle.Empty,new List<OverlayLabel>{label}).GetAwaiter().GetResult();
     EqCheck(ok&&!form.simpleReady.Contains(label),"unfitting equipment key preserves the original");
     EqCheck(label.FusionFailureReason=="equipment-field-does-not-fit",
       "unfitting equipment key records equipment-field-does-not-fit");
     EqCheck(label.EquipmentField.Status=="preserved"&&label.EquipmentField.PreservedReason=="equipment-field-does-not-fit",
       "preservedReason is attached to the equipment field plan");
     int[] layer=form.simpleLayer==null?null:form.simpleLayer.Pixels();
     EqCheck(layer==null||layer[59*800+160]==0,"unfitting key leaves its value pixels untouched");
     EqCheck(Program.Logs.Exists(x=>x.Contains("equipment-paint")&&x.Contains("preserved=1")&&
       x.Contains("preservedReason=equipment-field-does-not-fit")),
       "equipment paint evidence records the preserved reason");
     form.ClearSimpleFusion();
    }
   }
   // A value rectangle without geometry is still protected by the rest of the row.
   {
    var partialPanel=EqPanel(new RectangleF(50,25,700,100),18);
    var fields=EquipmentFieldGrammar.Parse("Weapon Def.: +16",true);
    var plan=EquipmentPanelLayout.BuildField(fields[0],"Weapon Def.: +16",
      new RectangleF(90,50,90,18),RectangleF.Empty,new RectangleF(80,48,300,22),
      partialPanel.PanelBounds,partialPanel.Typography);
    EqCheck(plan.PartialValue&&plan.ValueMode=="value-bounds-unmeasured"&&!plan.ValueGuardBounds.IsEmpty,
      "partial OCR without a value rectangle protects the rest of the row");
    EqCheck(plan.OriginalValue=="+16","partial OCR still retains the observed value text");
   }
   // A modified terminal numeric value must still be emitted verbatim in the plan.
   {
    var exact=EquipmentFieldGrammar.Parse("Weapon Attack: +36 (40 -4)",true);
    EqCheck(exact.Count==1&&exact[0].RawValue=="+36 (40 -4)","parenthesized correction is preserved byte-for-byte");
   }
   var seenLogs=new HashSet<string>();
   foreach(var log in Program.Logs)
    if(log.Contains("equipment-paint")&&seenLogs.Add(log))Console.WriteLine("EVIDENCE: "+log);
   Console.WriteLine("Equipment field paint: "+equipmentPaintChecks+
    " checks passed; generated-source substitute Font/Graphics harness; real game pixels unverified.");
   return equipmentPaintChecks;
  }
 }
 public static class EquipmentPaintRunner{public static int Run(){return OverlayForm.RunEquipmentPaint();}}
}
'@
if($OutputSource){[IO.File]::WriteAllText($OutputSource,$source);return}
Add-Type -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition $source
$count=[MapleOverlay.EquipmentPaintRunner]::Run()
if($count -lt 20){throw "unexpected check count: $count"}
