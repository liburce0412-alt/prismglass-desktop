using System;using System.Drawing;using System.Windows.Forms;
internal sealed class DockLabel:IDisposable {
 readonly DockPlate plate=new DockPlate();readonly IntPtr source;readonly Action<object> send;readonly Func<Rectangle> screen;readonly Timer timer=new Timer{Interval=16};
 Rectangle bounds;string caption="";bool shown,reduced;float opacity;DateTime frameTime;
 public DockLabel(IntPtr source,Action<object> send,Func<Rectangle> screen){this.source=source;this.send=send;this.screen=screen;timer.Tick+=(s,e)=>Animate();}
 internal static bool TryAnchor(string value,string id,out double fraction){fraction=0;var parts=value.Split('|');return parts.Length==2&&parts[0]==id&&double.TryParse(parts[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out fraction)&&!double.IsNaN(fraction)&&fraction>=0&&fraction<=1;}
 public void Display(string name,Rectangle dock,double fraction,bool reduceMotion){
  reduced=reduceMotion;var area=Screen.FromRectangle(dock).WorkingArea;float scale=(float)MonitorDock.PixelScale(area);int h=(int)Math.Round(36*scale),w;
  using(var font=new Font("Microsoft YaHei UI",10))w=Math.Min((int)(300*scale),TextRenderer.MeasureText(name,font).Width+(int)(32*scale));
  var next=new Rectangle(Math.Max(area.Left+8,Math.Min(area.Right-w-8,dock.Left+(int)(dock.Width*fraction)-w/2)),Math.Max(area.Top+8,dock.Top-h-10),w,h);
  if(next!=bounds||caption!=name||!shown){bounds=next;caption=name;var origin=screen();send(new{type="dock-label",text=name,x=bounds.X-origin.X,y=bounds.Y-origin.Y,w=bounds.Width,h=bounds.Height});}
  shown=true;if(!timer.Enabled)frameTime=DateTime.UtcNow;timer.Start();if(reduced)Animate();
 }
 void Animate(){var now=DateTime.UtcNow;double dt=Math.Min(.1,Math.Max(.001,(now-frameTime).TotalSeconds));frameTime=now;if(reduced)opacity=shown?1:0;else opacity+=(float)(((shown?1:0)-opacity)*(1-Math.Exp(-22*dt)));if(Math.Abs(opacity-(shown?1:0))<.01f){opacity=shown?1:0;timer.Stop();}
  if(opacity==0){plate.Hide();send(new{type="dock-label",text=""});return;}
  plate.Sync(source,source,bounds,screen(),true,(byte)Math.Round(opacity*255));
  var points=new float[98][];float r=bounds.Height/2f;for(int i=0;i<49;i++){double a=Math.PI/2+i*Math.PI/48;points[i]=new[]{26+r+(float)Math.Cos(a)*r,26+r+(float)Math.Sin(a)*r};a=-Math.PI/2+i*Math.PI/48;points[49+i]=new[]{26+bounds.Width-r+(float)Math.Cos(a)*r,26+r+(float)Math.Sin(a)*r};}plate.Outline(points);
 }
 public void Hide(){if(!shown)return;shown=false;if(!timer.Enabled)frameTime=DateTime.UtcNow;timer.Start();if(reduced)Animate();}
 public void Dispose(){timer.Stop();timer.Dispose();plate.Dispose();send(new{type="dock-label",text=""});}
}
