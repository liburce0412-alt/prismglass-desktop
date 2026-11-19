using System;using System.Drawing;using System.Windows.Forms;
internal sealed class DockLabel:IDisposable {
 readonly DockPlate plate=new DockPlate();readonly IntPtr source;readonly Action<object> send;readonly Func<Rectangle> screen;readonly Timer timer=new Timer{Interval=8};
 Rectangle bounds;string caption="",measuredCaption="";bool shown,reduced;float opacity,radius;DateTime frameTime;Size measured,measureLimit;float measureScale;
 public DockLabel(IntPtr source,Action<object> send,Func<Rectangle> screen){this.source=source;this.send=send;this.screen=screen;timer.Tick+=(s,e)=>Animate();}
 internal static bool TryAnchor(string value,string id,out double fraction){fraction=0;var parts=value.Split('|');return parts.Length==2&&parts[0]==id&&double.TryParse(parts[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out fraction)&&!double.IsNaN(fraction)&&fraction>=0&&fraction<=1;}
 internal static Size Measure(string text,float scale,Size limit){
  int maxWidth=Math.Max(1,Math.Min((int)Math.Round(440*scale),limit.Width-16)),maxHeight=Math.Max(1,limit.Height-16),padding=(int)Math.Ceiling(32*scale);
  using(var bitmap=new Bitmap(1,1))using(var graphics=Graphics.FromImage(bitmap))using(var font=new Font("Microsoft YaHei UI",13*scale,GraphicsUnit.Pixel))using(var format=new StringFormat(StringFormat.GenericTypographic)){
   format.Trimming=StringTrimming.None;var size=graphics.MeasureString(text,font,Math.Max(1,maxWidth-padding),format);
   // Browser glyph advances can slightly exceed GDI+ measurements at fractional DPI.
   return new Size(Math.Min(maxWidth,Math.Max((int)Math.Ceiling(36*scale),(int)Math.Ceiling(size.Width)+padding+(int)Math.Ceiling(2*scale))),Math.Min(maxHeight,Math.Max((int)Math.Ceiling(36*scale),(int)Math.Ceiling(size.Height*1.08+16*scale))));
  }
 }
 public void Display(string name,Rectangle dock,double fraction,bool reduceMotion){
  bool motionChanged=reduced!=reduceMotion;reduced=reduceMotion;var area=Screen.FromRectangle(dock).WorkingArea;float scale=(float)MonitorDock.PixelScale(area);
  if(name!=measuredCaption||measureScale!=scale||measureLimit!=area.Size){measured=Measure(name,scale,area.Size);measuredCaption=name;measureScale=scale;measureLimit=area.Size;}
  int w=measured.Width,h=measured.Height;radius=Math.Min(18*scale,h/2f);int top=dock.Top-h-10;if(top<area.Top+8)top=Math.Min(area.Bottom-h-8,dock.Bottom+10);
  var next=new Rectangle(Math.Max(area.Left+8,Math.Min(area.Right-w-8,dock.Left+(int)(dock.Width*fraction)-w/2)),Math.Max(area.Top+8,top),w,h);
  if(next==bounds&&caption==name&&shown&&!motionChanged)return;
  if(next!=bounds||caption!=name||!shown){bounds=next;caption=name;var origin=screen();send(new{type="dock-label",text=name,x=bounds.X-origin.X,y=bounds.Y-origin.Y,w=bounds.Width,h=bounds.Height,r=radius,fontSize=13*scale,padding=16*scale});}
  shown=true;if(!timer.Enabled)frameTime=DateTime.UtcNow;timer.Start();if(reduced)Animate();
 }
 void Animate(){var now=DateTime.UtcNow;double dt=Math.Min(.1,Math.Max(.001,(now-frameTime).TotalSeconds));frameTime=now;if(reduced)opacity=shown?1:0;else opacity+=(float)(((shown?1:0)-opacity)*(1-Math.Exp(-22*dt)));if(Math.Abs(opacity-(shown?1:0))<.01f){opacity=shown?1:0;timer.Stop();}
  if(opacity==0){plate.Hide();send(new{type="dock-label",text=""});return;}
  plate.Sync(source,source,bounds,screen(),true,(byte)Math.Round(opacity*255));
  var points=new float[100][];float r=radius;for(int corner=0;corner<4;corner++)for(int i=0;i<25;i++){double a=(180+corner*90+i*90.0/24)*Math.PI/180;float x=corner==0||corner==3?r:bounds.Width-r,y=corner<2?r:bounds.Height-r;points[corner*25+i]=new[]{26+x+(float)Math.Cos(a)*r,26+y+(float)Math.Sin(a)*r};}plate.Outline(points);
 }
 public void Hide(){if(!shown)return;shown=false;if(!timer.Enabled)frameTime=DateTime.UtcNow;timer.Start();if(reduced)Animate();}
 public void Dispose(){timer.Stop();timer.Dispose();plate.Dispose();send(new{type="dock-label",text=""});}
}
