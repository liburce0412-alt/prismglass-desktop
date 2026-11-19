using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal sealed class StartupArtwork : FrameworkElement {
 static T Freeze<T>(T value) where T:Freezable {value.Freeze();return value;}
 static Color Ink(string hex){return (Color)ColorConverter.ConvertFromString(hex);}
 static SolidColorBrush Solid(string color){return Freeze(new SolidColorBrush(Ink(color)));}
 static LinearGradientBrush Ramp(Point from,Point to,params string[] colors){
  var brush=new LinearGradientBrush{StartPoint=from,EndPoint=to};
  for(int i=0;i<colors.Length;i++)brush.GradientStops.Add(new GradientStop(Ink(colors[i]),(double)i/(colors.Length-1)));
  return Freeze(brush);
 }
 static RadialGradientBrush Pool(params string[] colors){
  var brush=new RadialGradientBrush();
  for(int i=0;i<colors.Length;i++)brush.GradientStops.Add(new GradientStop(Ink(colors[i]),(double)i/(colors.Length-1)));
  return Freeze(brush);
 }
 static readonly Brush backdrop=Ramp(new Point(0,0),new Point(1,1),"#050a16","#0b1327","#080714");
 static readonly Brush lowerShade=Ramp(new Point(0,0),new Point(0,1),"#00060915","#00060915","#81060915","#ed060915");
 static readonly Brush cyan=Pool("#b82487d3","#54125291","#000c265a");
 static readonly Brush violet=Pool("#973859c9","#53512d92","#001b1142");
 static readonly Brush floor=Pool("#f0e6fbff","#bb6abaff","#3c5945be","#004232b0");
 static readonly Brush bloom=Pool("#a384d9ff","#2e5898f1","#005759ee");
 static readonly Brush lens=Pool("#09215a97","#17275091","#0d4c84d5","#455eaee9","#006175e6");
 static readonly Brush lensRim=Ramp(new Point(0,.1),new Point(1,.8),"#003d91d0","#934489c6","#497d60aa","#005b5d9d");
 static readonly Brush horizon=Ramp(new Point(0,.5),new Point(1,.5),"#005061ce","#813977c3","#cefff0df","#303b5087","#ceb9a4fd","#735a4395","#005a83b4");
 static readonly Brush sweep=Ramp(new Point(0,.5),new Point(1,.5),"#00ffffff","#0e4ae4ff","#b4fff5f1","#2f706bff","#00ffffff");
 static readonly Brush track=Solid("#465b7594");
 static readonly Brush fill=Ramp(new Point(0,.5),new Point(1,.5),"#14b4ec","#60dcff","#d9f8ff");
 static readonly Brush text=Solid("#d6e2f5");
 static readonly Brush titleInk=Solid("#f5f8ff");
 static readonly Typeface titleFace=new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"),FontStyles.Normal,FontWeights.Light,FontStretches.Normal);
 static readonly Typeface statusFace=new Typeface(new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"),FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
 static readonly Geometry[] faces={
  Freeze(Geometry.Parse("M126.8,32 Q123,32 119,36 L36,119 Q30,125 35,132 L78,174 Q78,169 84,164 L120,144 Q126,140 126.7,132 Z")),
  Freeze(Geometry.Parse("M128.6,32 Q132,32 136,37 L218,118 Q226,125 219,133 L199,154 Q197,149 193,146 L136,108 Q129,104 129,96 Z")),
  Freeze(Geometry.Parse("M81,177 Q80,170 87,165 L157,131 Q162,128 168,132 L193,149 Q199,153 194,159 L134,219 Q127,226 120,218 Z"))
 };
 static readonly Point[] separation={new Point(-30,0),new Point(29,-6),new Point(6,13)};
 static readonly Point[] macroOffset={new Point(3,4),new Point(16,38),new Point(0,0)};
 static readonly double[] twist={-22,24,-15};
 static readonly Rect plate=new Rect(0,0,256,256);
 static readonly BitmapSource glass=ReadGlass();
 static readonly DrawingGroup waves=MakeWaves();
 static readonly DrawingGroup ring=MakeLens();

 static BitmapSource ReadGlass(){
  using(Stream source=typeof(StartupArtwork).Assembly.GetManifestResourceStream("PrismGlass.Startup.png")){
   if(source==null)throw new FileNotFoundException("缺少启动棱镜图像。");
   return Freeze(BitmapDecoder.Create(source,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0]);
  }
 }
 static FormattedText Formatted(string value,Typeface face,double size,Brush brush){
  return new FormattedText(value,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,face,size,brush,1);
 }
 readonly FormattedText name=Formatted("PrismGlass",titleFace,44,titleInk);
 FormattedText status,percentage;
 double seconds,progress=-1;
 string label;
 bool quiet;
 internal StartupArtwork(){SetProgress(0,"正在开启桌面");}
 protected override AutomationPeer OnCreateAutomationPeer(){return new FrameworkElementAutomationPeer(this);}
 internal void SetFrame(double time,bool reduced){
  seconds=Math.Max(0,time);quiet=reduced;InvalidateVisual();
 }
 internal void SetProgress(double fraction,string message){
  fraction=Double.IsNaN(fraction)?0:Math.Max(0,Math.Min(1,fraction));
  if(fraction==progress&&message==label)return;
  progress=fraction;label=message??String.Empty;
  int displayed=progress==1?100:Math.Min(99,(int)Math.Round(progress*100));
  string percent=displayed.ToString(CultureInfo.InvariantCulture)+"%";
  status=Formatted(label,statusFace,16,text);
  percentage=Formatted(percent,statusFace,16,text);
  AutomationProperties.SetName(this,label+" "+percent);
  InvalidateVisual();
 }
 static double Step(double from,double to,double t){
  double p=Math.Max(0,Math.Min(1,(t-from)/(to-from)));return p*p*(3-2*p);
 }
 static Geometry Bow(double edge,double flare){
  var path=new StreamGeometry();
  using(var g=path.Open()){
   var center=new Point(0,139);
   g.BeginFigure(center,true,true);
   g.BezierTo(new Point(-218,134),new Point(-574,107),new Point(-830,edge),true,false);
   g.LineTo(new Point(-830,edge+flare),true,false);
   g.BezierTo(new Point(-570,107+flare),new Point(-218,134+flare*.13),center,true,false);
  }
  return Freeze(path);
 }
 static DrawingGroup MakeWaves(){
  var art=new DrawingGroup();
  var spread=new[]{Bow(-302,-94),Bow(-302,-45),Bow(-302,-18),Bow(-302,-6)};
  var edge=Bow(-302,2.5);var echo=Bow(-66,1.5);
  using(var g=art.Open())for(int i=0;i<2;i++){
   string lit=i==0?"#d08eeaff":"#d8e1bdff", tint=i==0?"#9215adee":"#945970fb", transparent=i==0?"#004e87ed":"#007b4eee";
   var gradient=Ramp(new Point(0,0),new Point(1,.64),transparent,tint,lit,transparent);
   g.PushTransform(new ScaleTransform(i==0?1:-1,1));
   g.PushOpacity(.23);foreach(var layer in spread)g.DrawGeometry(gradient,null,layer);g.Pop();
   g.DrawGeometry(gradient,null,edge);
   g.PushOpacity(.38);g.DrawGeometry(gradient,null,echo);g.Pop();
   g.PushTransform(new ScaleTransform(1,-.42,0,139));
   g.PushOpacity(.18);g.DrawGeometry(gradient,null,spread[1]);g.DrawGeometry(gradient,null,edge);g.Pop();
   g.Pop();g.Pop();
  }
  return Freeze(art);
 }
 static DrawingGroup MakeLens(){
  var art=new DrawingGroup();
  var outline=Freeze(new Pen(lensRim,1.05));
  using(var g=art.Open()){
   g.DrawEllipse(lens,outline,new Point(0,4),251,246);
   g.DrawEllipse(null,outline,new Point(0,7),204,200);
   g.DrawEllipse(horizon,null,new Point(0,0),534,2.3);
   g.DrawEllipse(bloom,null,new Point(-203,0),60,22);
   g.DrawEllipse(bloom,null,new Point(203,0),60,22);
  }
  return Freeze(art);
 }
 protected override void OnRender(DrawingContext dc){
  var screen=new Rect(RenderSize);dc.DrawRectangle(backdrop,null,screen);
  if(screen.Width<1||screen.Height<1)return;
  dc.PushClip(new RectangleGeometry(screen));
  double s=Math.Min(screen.Width/920,screen.Height/520), center=screen.Height*.365;
  double t=quiet?4:progress==1?Math.Max(seconds,2.65):seconds;
  double pull=Step(0,1.22,t), assembled=Step(.86,2.34,t);
  double drift=quiet?0:Math.Sin(Math.Max(0,t-1.2)*.64);
  dc.PushTransform(new TranslateTransform(screen.Width*.5,center));
  dc.PushTransform(new ScaleTransform(s,s));
  dc.DrawEllipse(cyan,null,new Point(-233,-14),523,277);
  dc.DrawEllipse(violet,null,new Point(251,-39),473,264);
  double focalY=139+30*pull*(1-assembled);
  dc.PushTransform(new TranslateTransform(0,focalY-139));
  dc.PushOpacity((.22+.78*pull)*(1-Step(1.48,2.55,t)));dc.DrawDrawing(waves);dc.Pop();dc.Pop();
  double lensOpacity=Step(1.39,2.65,t);
  dc.PushOpacity(lensOpacity*.85);dc.DrawDrawing(ring);dc.Pop();
  var focus=new Point(6*(1-assembled),focalY);
  dc.DrawEllipse(bloom,null,focus,229,35);
  dc.DrawEllipse(floor,null,focus,198,8);
  dc.DrawEllipse(floor,null,focus,49,3);
  dc.PushTransform(new TranslateTransform(140*(1-pull),-90*(1-pull)));
  dc.PushTransform(new RotateTransform(29*(1-pull)+1.1*drift));
  double camera=1.35+4.95*(1-pull)+.28*(1-assembled);
  dc.PushTransform(new ScaleTransform(camera,camera));
  for(int i=0;i<faces.Length;i++){
   double join=(1-assembled)*(.32+.68*pull);
   dc.PushTransform(new TranslateTransform(-128+separation[i].X*join+macroOffset[i].X*(1-pull),
    -128+separation[i].Y*join+macroOffset[i].Y*(1-pull)));
   dc.PushTransform(new RotateTransform(twist[i]*join,128,128));
   dc.PushClip(faces[i]);dc.DrawImage(glass,plate);
   double light=Step(.12,.48,t)*(1-Step(1.22,2.02,t));
   if(light>0){
    dc.PushTransform(new RotateTransform(-28,128,128));
    dc.PushOpacity(light*.55);
    dc.DrawRectangle(sweep,null,new Rect(-180+Step(.12,1.84,t)*520,-100,122,460));
    dc.Pop();dc.Pop();
   }
   dc.Pop();dc.Pop();dc.Pop();
  }
  dc.Pop();dc.Pop();dc.Pop();
  dc.Pop();dc.Pop();

  dc.DrawRectangle(lowerShade,null,screen);
  double barY=Math.Min(screen.Height*.891,center+265*s);
  dc.PushTransform(new TranslateTransform(screen.Width*.5,barY));
  dc.PushTransform(new ScaleTransform(s,s));
  double title=Step(.74,1.51,t);
  dc.PushOpacity(title);
  dc.DrawText(name,new Point(-name.Width*.5,-100+9*(1-title)));
  dc.Pop();
  double half=Math.Min(screen.Width*.205/s,212);
  dc.DrawRoundedRectangle(track,null,new Rect(-half,-2,2*half,4),2,2);
  if(progress>0){
   double width=half*2*progress,tip=width-half;
   dc.DrawEllipse(bloom,null,new Point(tip,0),15,10);
   dc.DrawRoundedRectangle(fill,null,new Rect(-half,-2,width,4),2,2);
  }
  dc.DrawText(status,new Point(-half-status.Width-19,-status.Height*.5));
  dc.DrawText(percentage,new Point(half+19,-percentage.Height*.5));
  dc.Pop();dc.Pop();dc.Pop();
 }
}
