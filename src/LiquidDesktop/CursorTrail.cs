using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// A small native alpha surface; no WebView, input hooks, or extra process.
// It only exists on screen while particles or the movement halo are visible.
internal sealed class CursorTrail : Form {
 sealed class Particle {public float x,y,vx,vy,age,life,radius;public bool violet;}
 sealed class TracePoint {public float x,y,age;}
 readonly List<Particle> particles=new List<Particle>(64);
 readonly List<TracePoint> ribbon=new List<TracePoint>(32);
 readonly Random random=new Random();
 Point previous;bool sampled;DateTime last=DateTime.MinValue;float halo;
 float scale=1;
 bool trailEnabled=true;string style="particles";Color tint=Color.FromArgb(121,195,227);
 Bitmap surface;Graphics graphics;IntPtr memoryDc,dib,previousBitmap;int surfaceWidth,surfaceHeight;
 public void SetScale(int percent){scale=Math.Max(.75f,Math.Min(3f,GetSystemMetrics(13)/32f*percent/100f));}
 public void Configure(CursorPreferences settings,int percent){
  SetScale(percent);Color color=ColorTranslator.FromHtml(settings.trailColor);
  if(trailEnabled!=settings.trailEnabled||style!=settings.trailStyle||tint!=color){Reset();trailEnabled=settings.trailEnabled;style=settings.trailStyle;tint=color;if(!trailEnabled)ReleaseSurface();}
 }
 void Reset(){particles.Clear();ribbon.Clear();halo=0;sampled=false;if(Visible)Hide();}
 [StructLayout(LayoutKind.Sequential)]struct POINT {public int x,y;public POINT(int a,int b){x=a;y=b;}}
 [StructLayout(LayoutKind.Sequential)]struct SIZE {public int w,h;public SIZE(int a,int b){w=a;h=b;}}
 [StructLayout(LayoutKind.Sequential,Pack=1)]struct BLEND {public byte op,flags,alpha,format;}
 [StructLayout(LayoutKind.Sequential)]struct BITMAPINFO {public uint size;public int width,height;public ushort planes,bits;public uint compression,imageSize;public int x,y;public uint used,important;}
 [StructLayout(LayoutKind.Sequential)]struct CURSORINFO {public int size,flags;public IntPtr handle;public POINT point;}
 [DllImport("user32.dll")]static extern bool GetCursorInfo(ref CURSORINFO info);
 [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")]static extern bool UpdateLayeredWindow(IntPtr h,IntPtr screen,ref POINT position,ref SIZE size,IntPtr source,ref POINT origin,uint key,ref BLEND blend,uint flags);
 [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr dc);
 [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
 [DllImport("gdi32.dll")]static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
 [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
 [DllImport("gdi32.dll")]static extern IntPtr CreateDIBSection(IntPtr dc,ref BITMAPINFO info,uint usage,out IntPtr bits,IntPtr section,uint offset);
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x080800A0;p.ExStyle&=~0x40000;return p;}}
 protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
 public CursorTrail(){Text="PrismGlass Cursor Trail";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;Size=new Size(1,1);}
 public void Update(Point pointer,bool enabled){
  var now=DateTime.UtcNow;float dt=last==DateTime.MinValue?1f/60:(float)Math.Min(.05,(now-last).TotalSeconds);last=now;
  var info=new CURSORINFO{size=Marshal.SizeOf(typeof(CURSORINFO))};
  if(!enabled||!trailEnabled||!GetCursorInfo(ref info)||(info.flags&1)==0){Reset();return;}
  if(!sampled){previous=pointer;sampled=true;return;}
  float dx=pointer.X-previous.X,dy=pointer.Y-previous.Y,distance=(float)Math.Sqrt(dx*dx+dy*dy);
  if(distance>320*scale){Reset();previous=pointer;distance=0;} // a warp or monitor jump must not paint a screen-sized surface
  for(int i=ribbon.Count-1;i>=0;i--){ribbon[i].age+=dt;if(ribbon[i].age>=.38f)ribbon.RemoveAt(i);}
  for(int i=particles.Count-1;i>=0;i--){var p=particles[i];p.age+=dt;if(p.age>=p.life){particles.RemoveAt(i);continue;}p.x+=p.vx*dt;p.y+=p.vy*dt;p.vx*=Math.Max(0,1-3*dt);p.vy+=9*scale*dt;}
  if(distance>1){
   if(style=="ribbon"){
    if(ribbon.Count==0)ribbon.Add(new TracePoint{x=previous.X,y=previous.Y});
    if(ribbon.Count==32)ribbon.RemoveAt(0);ribbon.Add(new TracePoint{x=pointer.X,y=pointer.Y});
   }else{
   int count=Math.Min(12,Math.Max(1,(int)(distance/(5*scale))));
   if(style=="stars")count=Math.Max(1,count/2);
   for(int i=0;i<count;i++){
    if(particles.Count==64)particles.RemoveAt(0);float fraction=(i+1f)/count;
    particles.Add(new Particle{x=previous.X+dx*fraction+(float)(random.NextDouble()-.5)*4*scale,y=previous.Y+dy*fraction+(float)(random.NextDouble()-.5)*4*scale,
     vx=(-dx/distance*10+(float)(random.NextDouble()-.5)*20)*scale,vy=(-dy/distance*10+(float)(random.NextDouble()-.5)*20)*scale,
     life=(style=="stars"?.4f:.28f)+(float)random.NextDouble()*.2f,radius=((style=="stars"?1.7f:.8f)+(float)random.NextDouble()*1.2f)*scale,violet=random.Next(3)==0});
   }
   }
   halo=1;
  }else halo=Math.Max(0,halo-dt*6);
  previous=pointer;
  if(particles.Count==0&&ribbon.Count==0&&halo<=0){if(Visible)Hide();return;}
  Render(pointer);
 }
 void Render(Point pointer){
  float padding=24*scale,left=pointer.X-padding,top=pointer.Y-padding,right=pointer.X+padding,bottom=pointer.Y+padding;
  foreach(var p in particles){left=Math.Min(left,p.x-padding);top=Math.Min(top,p.y-padding);right=Math.Max(right,p.x+padding);bottom=Math.Max(bottom,p.y+padding);}
  foreach(var p in ribbon){left=Math.Min(left,p.x-padding);top=Math.Min(top,p.y-padding);right=Math.Max(right,p.x+padding);bottom=Math.Max(bottom,p.y+padding);}
  var bounds=Rectangle.FromLTRB((int)Math.Floor(left),(int)Math.Floor(top),(int)Math.Ceiling(right),(int)Math.Ceiling(bottom));
  int width=Math.Max(64,((bounds.Width+63)/64)*64),height=Math.Max(64,((bounds.Height+63)/64)*64);
  if(width>1024||height>1024){Reset();return;}
  EnsureSurface(width,height);
  graphics.Clear(Color.Transparent);graphics.CompositingMode=CompositingMode.SourceOver;
  if(halo>0){float cx=pointer.X-bounds.Left+5*scale,cy=pointer.Y-bounds.Top+7*scale;
   for(int i=5;i>=1;i--){float radius=(5+i*2)*scale;using(var brush=new SolidBrush(Color.FromArgb((int)(halo*5),tint)))graphics.FillEllipse(brush,cx-radius,cy-radius,radius*2,radius*2);}
  }
  for(int i=1;i<ribbon.Count;i++){
   var a=ribbon[i-1];var b=ribbon[i];float fade=Math.Max(0,1-a.age/.38f),widthScale=scale*(.3f+1.7f*fade);
   using(var glow=new Pen(Color.FromArgb((int)(28*fade),tint),widthScale*3))using(var line=new Pen(Color.FromArgb((int)(185*fade),tint),widthScale)){
    glow.StartCap=glow.EndCap=line.StartCap=line.EndCap=LineCap.Round;
    graphics.DrawLine(glow,a.x-bounds.Left,a.y-bounds.Top,b.x-bounds.Left,b.y-bounds.Top);
    graphics.DrawLine(line,a.x-bounds.Left,a.y-bounds.Top,b.x-bounds.Left,b.y-bounds.Top);
   }
  }
  foreach(var p in particles){float t=1-p.age/p.life,r=p.radius*(.35f+.65f*t),x=p.x-bounds.Left,y=p.y-bounds.Top;int alpha=(int)(170*t*t);
   var color=p.violet?Color.FromArgb(alpha,(tint.R+255)/2,(tint.G+255)/2,(tint.B+255)/2):Color.FromArgb(alpha,tint);
   using(var soft=new SolidBrush(Color.FromArgb(alpha/5,color)))graphics.FillEllipse(soft,x-r*2,y-r*2,r*4,r*4);
   using(var brush=new SolidBrush(color)){
    if(style=="stars")graphics.FillPolygon(brush,new[]{new PointF(x,y-r*1.8f),new PointF(x+r*.4f,y-r*.4f),new PointF(x+r*1.8f,y),new PointF(x+r*.4f,y+r*.4f),new PointF(x,y+r*1.8f),new PointF(x-r*.4f,y+r*.4f),new PointF(x-r*1.8f,y),new PointF(x-r*.4f,y-r*.4f)});
    else graphics.FillEllipse(brush,x-r,y-r,r*2,r*2);
   }
  }
  graphics.Flush();var position=new POINT(bounds.X,bounds.Y);var size=new SIZE(surfaceWidth,surfaceHeight);var origin=new POINT(0,0);var blend=new BLEND{alpha=255,format=1};
  if(UpdateLayeredWindow(Handle,IntPtr.Zero,ref position,ref size,memoryDc,ref origin,0,ref blend,2)){
   if(!Visible)Show();SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x13);
  }
 }
 void EnsureSurface(int width,int height){
  if(surface!=null&&surfaceWidth==width&&surfaceHeight==height)return;
  ReleaseSurface();memoryDc=CreateCompatibleDC(IntPtr.Zero);
  var info=new BITMAPINFO{size=40,width=width,height=-height,planes=1,bits=32};IntPtr bits;
  dib=CreateDIBSection(memoryDc,ref info,0,out bits,IntPtr.Zero,0);
  if(memoryDc==IntPtr.Zero||dib==IntPtr.Zero||bits==IntPtr.Zero){ReleaseSurface();throw new InvalidOperationException("Cannot create cursor particle surface");}
  previousBitmap=SelectObject(memoryDc,dib);surfaceWidth=width;surfaceHeight=height;
  surface=new Bitmap(width,height,width*4,PixelFormat.Format32bppPArgb,bits);graphics=Graphics.FromImage(surface);graphics.SmoothingMode=SmoothingMode.AntiAlias;
 }
 void ReleaseSurface(){if(graphics!=null){graphics.Dispose();graphics=null;}if(surface!=null){surface.Dispose();surface=null;}if(memoryDc!=IntPtr.Zero&&previousBitmap!=IntPtr.Zero)SelectObject(memoryDc,previousBitmap);previousBitmap=IntPtr.Zero;if(dib!=IntPtr.Zero){DeleteObject(dib);dib=IntPtr.Zero;}if(memoryDc!=IntPtr.Zero){DeleteDC(memoryDc);memoryDc=IntPtr.Zero;}}
 protected override void Dispose(bool disposing){if(disposing)ReleaseSurface();base.Dispose(disposing);}
}
