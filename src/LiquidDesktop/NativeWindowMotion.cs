using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Media = System.Windows.Media;
using Media3D = System.Windows.Media.Media3D;
using Wpf = System.Windows;

// Dock-owned transitions: never inject into applications or alter global animation settings.
internal sealed class NativeWindowMotion : IDisposable {
 readonly Func<bool> allowed;readonly Func<DockEntry,DockWindow,Rectangle> target;
 readonly Dictionary<long,Flight> flights=new Dictionary<long,Flight>();readonly List<Launch> launches=new List<Launch>();
 readonly Timer timer=new Timer{Interval=30};readonly Stopwatch clock=Stopwatch.StartNew();bool disposed,rendering;double launchPoll;
 static readonly System.Threading.SemaphoreSlim captures=new System.Threading.SemaphoreSlim(2,2);
 sealed class Flight {public DockWindow window;public Bitmap image;public Rectangle bounds,icon;public GenieSurface surface;public double position,goal,stamp;public bool preparing,first,cancelled,minimizedByUs,restoring,hiddenForSnapshot;}
 sealed class Launch {public DockEntry entry;public HashSet<long> before;public double expires,seen;public long candidate;public Rectangle bounds;}
 [StructLayout(LayoutKind.Sequential)]internal struct RECT {public int l,t,r,b;public Rectangle Rectangle{get{return Rectangle.FromLTRB(l,t,r,b);}}}
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
 [DllImport("user32.dll")]internal static extern bool IsIconic(IntPtr window);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out RECT value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmFlush();
 const string SnapshotOwner="PrismGlass.Motion.SnapshotOwner";
 delegate bool EnumWindow(IntPtr window,IntPtr state);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,IntPtr state);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetProp(IntPtr window,string name);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool SetProp(IntPtr window,string name,IntPtr value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr RemoveProp(IntPtr window,string name);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
 [DllImport("user32.dll")]static extern bool GetLayeredWindowAttributes(IntPtr window,out uint color,out byte alpha,out uint flags);
 [DllImport("user32.dll",SetLastError=true)]static extern bool SetLayeredWindowAttributes(IntPtr window,uint color,byte alpha,uint flags);
 internal int ActiveCount {get{return flights.Values.Count(f=>f.preparing||f.surface!=null);}}
 internal bool Animating {get{return flights.Values.Any(f=>!f.preparing&&f.surface!=null);}}
 internal IEnumerable<GenieSurface> Surfaces {get{return flights.Values.Where(f=>f.surface!=null).Select(f=>f.surface);}}
 internal string Error {get;private set;}
 public NativeWindowMotion(Func<bool> enabled,Func<DockEntry,DockWindow,Rectangle> icon){allowed=enabled;target=icon;RecoverSnapshots();timer.Tick+=(s,e)=>{if(!rendering)Tick();};}
 static void RecoverSnapshots(){
  EnumWindows((window,state)=>{long marker=GetProp(window,SnapshotOwner).ToInt64();if(marker<=0||marker>int.MaxValue)return true;
   try{using(var process=Process.GetProcessById((int)marker))if(!process.HasExited&&process.MainModule.FileName==Application.ExecutablePath)return true;}catch(ArgumentException){}catch(System.ComponentModel.Win32Exception){return true;}catch(InvalidOperationException){}
   RestoreOpacity(window);return true;
  },IntPtr.Zero);
 }
 static bool HideForSnapshot(IntPtr window){
  if(GetProp(window,SnapshotOwner)!=IntPtr.Zero)return false;long style=GetWindowLongPtr(window,-20).ToInt64();bool layered=(style&0x80000)!=0;uint color=0,flags=2;byte alpha=255;
  if(layered&&!GetLayeredWindowAttributes(window,out color,out alpha,out flags))return false;
  string[] names={".Layered",".Color",".Alpha",".Flags"};long[] values={layered?2:1,color,alpha+1,flags};
  for(int i=0;i<names.Length;i++)if(!SetProp(window,SnapshotOwner+names[i],new IntPtr(values[i]))){foreach(var name in names)RemoveProp(window,SnapshotOwner+name);return false;}
  if(!SetProp(window,SnapshotOwner,new IntPtr(Process.GetCurrentProcess().Id))){foreach(var name in names)RemoveProp(window,SnapshotOwner+name);return false;}
  if(!layered)SetWindowLongPtr(window,-20,new IntPtr(style|0x80000));
  if(SetLayeredWindowAttributes(window,0,0,2))return true;RestoreOpacity(window);return false;
 }
 static void RestoreOpacity(IntPtr window){
  long layered=GetProp(window,SnapshotOwner+".Layered").ToInt64(),alpha=GetProp(window,SnapshotOwner+".Alpha").ToInt64(),flags=GetProp(window,SnapshotOwner+".Flags").ToInt64();
  if(layered<1||layered>2||alpha<1||alpha>256||flags<1||flags>3)return;
  if(layered==2){if(!SetLayeredWindowAttributes(window,(uint)GetProp(window,SnapshotOwner+".Color").ToInt64(),(byte)(alpha-1),(uint)flags))return;}
  else SetWindowLongPtr(window,-20,new IntPtr(GetWindowLongPtr(window,-20).ToInt64()&~0x80000L));
  foreach(var name in new[]{".Layered",".Color",".Alpha",".Flags",""})RemoveProp(window,SnapshotOwner+name);
 }
 void RenderTick(object sender,EventArgs args){Tick();}
 void Rendering(bool value){if(rendering==value)return;rendering=value;if(value)Media.CompositionTarget.Rendering+=RenderTick;else Media.CompositionTarget.Rendering-=RenderTick;}
 internal static bool SetState(DockWindow window,bool minimized){
  if(!DockBackend.Valid(window))return false;var handle=new IntPtr(window.handle);int previous;
  // This DWM attribute is set-only on some Windows builds; a failed read must not skip suppression.
  if(DwmGetWindowAttribute(handle,3,out previous,4)!=0)previous=0;
  int disabled=1;DwmSetWindowAttribute(handle,3,ref disabled,4);
  try{return DockBackend.SetMinimized(window,minimized);}finally{if(DockBackend.Valid(window))DwmSetWindowAttribute(handle,3,ref previous,4);}
 }
 public bool Interact(DockEntry entry,DockClickAction action,DockWindow window){
  if(disposed||!allowed())return false;
  if(action==DockClickAction.Launch){launches.Add(new Launch{entry=entry,before=new HashSet<long>(entry.windows.Select(w=>w.handle)),expires=clock.Elapsed.TotalSeconds+10});if(launches.Count>8)launches.RemoveAt(0);timer.Start();return false;}
  Flight running;if(window!=null&&flights.TryGetValue(window.handle,out running)&&(running.preparing||running.surface!=null)){running.goal=1-running.goal;timer.Start();return true;}
  if(action==DockClickAction.Minimize)return Begin(window,target(entry,window),true,false);
  return false;
 }
 public bool Restore(DockWindow window){
  if(disposed||!allowed()||!DockBackend.Valid(window)||!DockBackend.CanManage(window)||!IsIconic(new IntPtr(window.handle)))return false;
  var icon=target(null,window);if(icon.IsEmpty)return false;Flight flight;
  if(!flights.TryGetValue(window.handle,out flight)||flight.image==null||flight.window.pid!=window.pid){
   var handle=new IntPtr(window.handle);if(!HideForSnapshot(handle))return false;
   flight=new Flight{window=window,icon=icon,goal=0,restoring=true,hiddenForSnapshot=true,preparing=true};flights[window.handle]=flight;
   try{RECT bounds;if(!SetState(window,false)||!GetWindowRect(new IntPtr(window.handle),out bounds)||bounds.Rectangle.Width<1||bounds.Rectangle.Height<1||(long)bounds.Rectangle.Width*bounds.Rectangle.Height>16777216)throw new InvalidOperationException("窗口预览暂不可用。");flight.bounds=bounds.Rectangle;timer.Start();Prepare(flight);return true;}
   catch{SetState(window,false);Release(flight);flights.Remove(window.handle);return false;}
  }
  flight.icon=icon;
  flight.goal=0;if(flight.surface==null){flight.surface=new GenieSurface(flight.image,flight.bounds,flight.icon);flight.position=1;flight.preparing=true;ShowRestore(flight);}timer.Start();return true;
 }
 async void ShowRestore(Flight flight){
  try{var surface=flight.surface;surface.Render(flight.position);surface.Show();if(await Task.WhenAny(surface.Ready,Task.Delay(500))!=surface.Ready||!await surface.Ready)throw new InvalidOperationException("窗口动画未及时就绪。");if(!Current(flight)||!allowed())return;flight.stamp=clock.Elapsed.TotalSeconds;Rendering(true);}
  catch{if(Current(flight)){SetState(flight.window,false);Release(flight);flights.Remove(flight.window.handle);}}
  finally{flight.preparing=false;}
 }
 internal bool Begin(DockWindow window,Rectangle icon,bool minimize,bool first){
  if(disposed||!allowed()||!DockBackend.Valid(window)||!DockBackend.CanManage(window)||icon.Width<=0||icon.Height<=0||IsIconic(new IntPtr(window.handle)))return false;
  RECT rect;if(!GetWindowRect(new IntPtr(window.handle),out rect)||rect.Rectangle.Width<1||rect.Rectangle.Height<1||(long)rect.Rectangle.Width*rect.Rectangle.Height>16777216)return false;
  Flight old;if(flights.TryGetValue(window.handle,out old)){old.cancelled=true;Release(old);}
  var flight=new Flight{window=window,bounds=rect.Rectangle,icon=icon,goal=minimize?1:0,first=first,preparing=true};flights[window.handle]=flight;timer.Start();Prepare(flight);return true;
 }
 internal static async Task<Bitmap> Snapshot(DockWindow window,Rectangle bounds){
  if(bounds.Width<1||bounds.Height<1||(long)bounds.Width*bounds.Height>16777216||!DockBackend.Valid(window)||!captures.Wait(0))return null;
  var source=new LiveGlassBackdrop.Source{handle=new IntPtr(window.handle),pid=window.pid,bounds=bounds,visible=bounds};
  var capture=Task.Run(()=>{try{return LiveGlassBackdrop.CaptureWindow(source);}finally{captures.Release();}});
  if(await Task.WhenAny(capture,Task.Delay(280))==capture){
   var image=await capture;if(image==null)return null;
   // Both animations and stage cards must reject a blank capture from an unsupported app.
   for(int y=1;y<8;y++)for(int x=1;x<8;x++){var color=image.GetPixel(x*image.Width/8,y*image.Height/8);if(color.R+color.G+color.B>36)return image;}
   image.Dispose();return null;
  }
  var cleanup=capture.ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion&&t.Result!=null)t.Result.Dispose();else if(t.IsFaulted){var ignored=t.Exception;}});return null;
 }
 async void Prepare(Flight flight){
  Bitmap captured=null;
  try{
   // A restored Chromium window needs its first compositor frame before PrintWindow can supply pixels.
   if(flight.hiddenForSnapshot){await Task.Delay(32);if(!Current(flight)||!allowed())return;DwmFlush();}
   var source=new LiveGlassBackdrop.Source{handle=new IntPtr(flight.window.handle),pid=flight.window.pid,bounds=flight.bounds,visible=flight.bounds};
   captured=await Snapshot(flight.window,flight.bounds);if(!Current(flight)||!DockBackend.Valid(flight.window)||!allowed()||captured==null)return;
   if(flight.goal==0&&!flight.first&&!flight.restoring)return;
   RECT visible;if(DwmGetWindowAttribute(source.handle,9,out visible,16)==0&&flight.bounds.Contains(visible.Rectangle)&&visible.Rectangle.Width>0&&visible.Rectangle.Height>0){var crop=visible.Rectangle;crop.Offset(-flight.bounds.X,-flight.bounds.Y);var trimmed=captured.Clone(crop,PixelFormat.Format32bppArgb);captured.Dispose();captured=trimmed;flight.bounds=visible.Rectangle;}
   flight.image=captured;captured=null;flight.position=flight.first||flight.restoring?1:0;
   flight.surface=new GenieSurface(flight.image,flight.bounds,flight.icon);flight.surface.Render(flight.position);flight.surface.Show();
   var surface=flight.surface;if(await Task.WhenAny(surface.Ready,Task.Delay(500))!=surface.Ready||!await surface.Ready)throw new InvalidOperationException("窗口动画未及时就绪。");
   if(!Current(flight)||!allowed()||!DockBackend.Valid(flight.window))return;
   if(!SetState(flight.window,true)){Release(flight);return;}flight.minimizedByUs=true;Reveal(flight);flight.stamp=clock.Elapsed.TotalSeconds;
   Rendering(true);
  }catch(Exception ex){Error="窗口动效暂不可用："+ex.Message;Release(flight);}
  finally{
   if(captured!=null)captured.Dispose();flight.preparing=false;
   if(Current(flight)&&flight.surface==null){if(DockBackend.Valid(flight.window)&&allowed()){if(flight.goal==1&&!SetState(flight.window,true))Error="窗口未能最小化，请使用应用的最小化按钮。";else if(flight.goal==0&&flight.minimizedByUs&&!SetState(flight.window,false))Error="窗口未能还原，请从 Windows 任务栏打开。";}Release(flight);flights.Remove(flight.window.handle);}
  }
 }
 void Tick(){
  if(disposed)return;
  try{
   double now=clock.Elapsed.TotalSeconds;
   if(!allowed()){Cancel();return;}
   if(launches.Count>0&&now-launchPoll>=.08){launchPoll=now;var windows=DockBackend.ReadWindows();foreach(var launch in launches.ToArray()){
    var window=windows.FirstOrDefault(w=>!launch.before.Contains(w.handle)&&DockBackend.Matches(new DockPin{path=launch.entry.path,arguments=launch.entry.arguments},w));
    if(window!=null){RECT rect;if(GetWindowRect(new IntPtr(window.handle),out rect)){if(launch.candidate!=window.handle||launch.bounds!=rect.Rectangle){launch.candidate=window.handle;launch.bounds=rect.Rectangle;launch.seen=now;}else if(now-launch.seen>=.10){launches.Remove(launch);Begin(window,target(launch.entry,window),false,true);}}}if(now>=launch.expires)launches.Remove(launch);
   }}
   foreach(var flight in flights.Values.ToArray()){
    if(!DockBackend.Valid(flight.window)){Release(flight);flights.Remove(flight.window.handle);continue;}
    if(flight.preparing||flight.surface==null)continue;
    if(!IsIconic(new IntPtr(flight.window.handle))){Release(flight);flights.Remove(flight.window.handle);continue;}
    double step=Math.Max(0,now-flight.stamp)/(flight.first?.23:.36);flight.stamp=now;
    flight.position=flight.goal==1?Math.Min(1,flight.position+step):Math.Max(0,flight.position-step);flight.surface.Render(flight.position);
    if(flight.position==flight.goal){
     if(flight.goal==0){SetState(flight.window,false);try{DockBackend.ActivateImmediate(flight.window);}catch(InvalidOperationException){}Release(flight);flights.Remove(flight.window.handle);}
     else{flight.surface.Dispose();flight.surface=null;}
    }
   }
   // Minimized snapshots are bounded and only retained for the reverse Dock transition.
   foreach(var flight in flights.Values.Where(f=>!f.preparing&&f.surface==null).Skip(6).ToArray()){Release(flight);flights.Remove(flight.window.handle);}
   if(!Surfaces.Any())Rendering(false);if(ActiveCount==0&&launches.Count==0)timer.Stop();
  }catch{Cancel();}
 }
 bool Current(Flight flight){Flight current;return !disposed&&!flight.cancelled&&flights.TryGetValue(flight.window.handle,out current)&&current==flight;}
 public void Cancel(){Rendering(false);launches.Clear();foreach(var flight in flights.Values.ToArray()){flight.cancelled=true;if(flight.minimizedByUs&&flight.surface!=null&&DockBackend.Valid(flight.window))SetState(flight.window,false);Release(flight);}flights.Clear();timer.Stop();}
 static void Reveal(Flight flight){if(!flight.hiddenForSnapshot)return;var handle=new IntPtr(flight.window.handle);if(DockBackend.Valid(flight.window)&&GetProp(handle,SnapshotOwner)==new IntPtr(Process.GetCurrentProcess().Id))RestoreOpacity(handle);flight.hiddenForSnapshot=false;}
 static void Release(Flight flight){Reveal(flight);if(flight.surface!=null){flight.surface.Dispose();flight.surface=null;}if(flight.image!=null){flight.image.Dispose();flight.image=null;}}
 public void Dispose(){if(disposed)return;disposed=true;Cancel();timer.Dispose();}

 internal sealed class GenieSurface:Wpf.Window,IDisposable {
  const int Rows=64;
  readonly Rectangle source,canvas;Rectangle icon;
  internal double StageAngle=32,StageDepth=400;
  readonly System.Windows.Controls.Viewport3D viewport;
  readonly Media3D.MeshGeometry3D mesh=new Media3D.MeshGeometry3D();
  Media3D.PerspectiveCamera stageCamera;bool stageMesh;
  readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>();
  public Task<bool> Ready {get{return ready.Task;}}
  [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr item);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
  [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int w,int h,uint flags);
  internal static System.Windows.Media.Imaging.BitmapSource Texture(Bitmap bitmap){
   var handle=bitmap.GetHbitmap();try{var source=System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(handle,IntPtr.Zero,Wpf.Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());var texture=new System.Windows.Media.Imaging.FormatConvertedBitmap(source,Media.PixelFormats.Bgr32,null,0);texture.Freeze();return texture;}finally{DeleteObject(handle);}
  }
  public GenieSurface(Bitmap bitmap,Rectangle from,Rectangle to):this(Texture(bitmap),from,to){}
  internal GenieSurface(System.Windows.Media.Imaging.BitmapSource texture,Rectangle from,Rectangle to){
   source=from;icon=to;canvas=Rectangle.Inflate(Rectangle.Union(from,to),4,4);
   AllowsTransparency=true;WindowStyle=Wpf.WindowStyle.None;ResizeMode=Wpf.ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;Background=Media.Brushes.Transparent;
   Width=canvas.Width;Height=canvas.Height;Left=canvas.Left;Top=canvas.Top;
   viewport=new System.Windows.Controls.Viewport3D{Width=canvas.Width,Height=canvas.Height,IsHitTestVisible=false,ClipToBounds=false};
   viewport.Camera=new Media3D.OrthographicCamera(new Media3D.Point3D(canvas.Width/2.0,-canvas.Height/2.0,100),new Media3D.Vector3D(0,0,-1),new Media3D.Vector3D(0,1,0),canvas.Width){NearPlaneDistance=1,FarPlaneDistance=200};
   var brush=new Media.ImageBrush(texture){Stretch=Media.Stretch.Fill};brush.Freeze();
   var material=new Media3D.DiffuseMaterial(brush);material.Freeze();
   MeshRows(Rows);
   viewport.Children.Add(new Media3D.ModelVisual3D{Content=new Media3D.GeometryModel3D(mesh,material)});viewport.Children.Add(new Media3D.ModelVisual3D{Content=new Media3D.AmbientLight(Media.Colors.White)});
   Content=new System.Windows.Controls.Viewbox{Stretch=Media.Stretch.Fill,Child=viewport};
   // Per-pixel alpha also lets pointer input pass through overlays across processes.
   SourceInitialized+=(s,e)=>{var window=new System.Windows.Interop.WindowInteropHelper(this).Handle;SetWindowLongPtr(window,-20,new IntPtr(GetWindowLongPtr(window,-20).ToInt64()|0x080000A0L));SetWindowPos(window,new IntPtr(-1),canvas.X,canvas.Y,canvas.Width,canvas.Height,0x10);};
   ContentRendered+=(s,e)=>ready.TrySetResult(true);Closed+=(s,e)=>ready.TrySetResult(false);
  }
  void MeshRows(int rows){var uv=new Media.PointCollection((rows+1)*2);var indices=new Media.Int32Collection(rows*6);for(int row=0;row<=rows;row++){uv.Add(new Wpf.Point(0,row/(double)rows));uv.Add(new Wpf.Point(1,row/(double)rows));if(row<rows){int n=row*2;indices.Add(n);indices.Add(n+2);indices.Add(n+1);indices.Add(n+1);indices.Add(n+2);indices.Add(n+3);}}uv.Freeze();indices.Freeze();mesh.TextureCoordinates=uv;mesh.TriangleIndices=indices;}
  // Lamp curve adapted from Potassiumuncher's MIT engine in Windhawk's macos-minimize-animation.
  internal static PointF Vertex(float x,float y,double progress,Rectangle from,Rectangle to){
   if(to.Top+to.Height/2.0<from.Top+from.Height/2.0){var point=Vertex(x,1-y,progress,new Rectangle(from.X,-from.Bottom,from.Width,from.Height),new Rectangle(to.X,-to.Bottom,to.Width,to.Height));return new PointF(point.X,-point.Y);}
   double value=Math.Max(0,Math.Min(1,progress)),p=value*value*(3-2*value),neck=Math.Min(1,p/.3),travel=Math.Max(0,(p-.3)/.7);
   double gap=to.Top-from.Bottom,fullHeight=to.Top-from.Top-gap*(1-neck),height=fullHeight*(1-travel),dy=y*height;
   double width=to.Width+(from.Width-to.Width)*((1-travel)*(1-y)+(1-neck)*y);
   double offsetX=(to.Left-from.Left)*(dy/(fullHeight+.1)*neck+travel);
   double wave=Math.Sin((height-dy)/Math.Max(1,fullHeight)*2*Math.PI+Math.PI)*(from.Left+from.Width*x-to.Left-to.Width*x)/7*neck;
   return new PointF((float)(from.Left+x*width+offsetX+wave),(float)(to.Top-height-gap*(1-neck)+dy));
  }
  internal void Render(double progress){
   if(stageMesh){MeshRows(Rows);stageMesh=false;}
   var points=new Media3D.Point3DCollection((Rows+1)*2);
   for(int row=0;row<=Rows;row++)for(int col=0;col<2;col++){var p=Vertex(col,row/(float)Rows,progress,source,icon);points.Add(new Media3D.Point3D(p.X-canvas.X,canvas.Y-p.Y,0));}
   points.Freeze();mesh.Positions=points;
  }
  internal void RenderStage(double progress){
   // A Stage card is one rigid plane; only the Dock lamp deformation needs strips.
   if(!stageMesh){MeshRows(1);stageMesh=true;}
   double p=Math.Max(0,Math.Min(1,progress)),angle=p*Math.PI*StageAngle/180,sin=Math.Sin(angle),cos=Math.Cos(angle);
   double width=source.Width+(icon.Width-source.Width)*p,height=source.Height+(icon.Height-source.Height)*p;
   double cx=source.Left+source.Width/2.0+(icon.Left+icon.Width/2.0-source.Left-source.Width/2.0)*p-canvas.X;
   double cy=canvas.Y-source.Top-source.Height/2.0-(icon.Top+icon.Height/2.0-source.Top-source.Height/2.0)*p;
   double distance=canvas.Width/2.0/Math.Tan(Math.PI/12);
   if(stageCamera==null){stageCamera=new Media3D.PerspectiveCamera(new Media3D.Point3D(canvas.Width/2.0,-canvas.Height/2.0,distance),new Media3D.Vector3D(0,0,-1),new Media3D.Vector3D(0,1,0),30){NearPlaneDistance=1,FarPlaneDistance=distance+canvas.Width*2};stageCamera.Freeze();viewport.Camera=stageCamera;}
   var points=new Media3D.Point3DCollection(4);double depth=distance+(Math.Max(1,StageDepth)-distance)*p;
   for(int row=0;row<=1;row++)for(int col=0;col<2;col++){double x=(col-.5)*width,z=-x*sin;points.Add(new Media3D.Point3D(cx+x*cos+(canvas.Width/2.0-cx)*z/depth,cy+(.5-row)*height+(-canvas.Height/2.0-cy)*z/depth,z*distance/depth));}
   points.Freeze();mesh.Positions=points;
  }
  internal void MoveCard(Rectangle bounds){icon=bounds;RenderStage(1);}
  public void Dispose(){Close();}
 }
}
