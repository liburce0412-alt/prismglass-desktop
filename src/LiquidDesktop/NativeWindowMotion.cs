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
 readonly Dictionary<long,Flight> flights=new Dictionary<long,Flight>();
 readonly Timer timer=new Timer{Interval=30};readonly Stopwatch clock=Stopwatch.StartNew();bool disposed,rendering;
 static readonly System.Threading.SemaphoreSlim captures=new System.Threading.SemaphoreSlim(2,2);
 sealed class Flight {public DockWindow window;public System.Windows.Media.Imaging.BitmapSource texture;public Rectangle bounds,icon;public GenieSurface surface;public double position,velocity,goal,stamp;public bool preparing,cancelled,minimizedByUs,restoring,hiddenForSnapshot;}
 internal static bool AdvanceMotion(ref double position,ref double velocity,double goal,double dt){
  const double omega=22;
  double displacement=position-goal,momentum=velocity+omega*displacement,decay=Math.Exp(-omega*dt);
  position=goal+(displacement+momentum*dt)*decay;
  velocity=(velocity-omega*momentum*dt)*decay;
  if(Math.Abs(position-goal)>=.001||Math.Abs(velocity)>=.02)return false;
  position=goal;velocity=0;return true;
 }
 [StructLayout(LayoutKind.Sequential)]internal struct RECT {public int l,t,r,b;public Rectangle Rectangle{get{return Rectangle.FromLTRB(l,t,r,b);}}}
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
 [DllImport("user32.dll")]internal static extern bool IsIconic(IntPtr window);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out RECT value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 const string SnapshotOwner="PrismGlass.Motion.SnapshotOwner";
 static readonly Dictionary<IntPtr,IntPtr> pendingReveal=new Dictionary<IntPtr,IntPtr>();static System.Windows.Forms.Timer revealRetry;
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
 internal static bool HideForSnapshot(IntPtr window){
  if(GetProp(window,SnapshotOwner)!=IntPtr.Zero)return false;long style=GetWindowLongPtr(window,-20).ToInt64();bool layered=(style&0x80000)!=0;uint color=0,flags=2;byte alpha=255;
  int cloaked;if(DwmGetWindowAttribute(window,14,out cloaked,4)==0&&cloaked!=0)return false;
  if(layered&&!GetLayeredWindowAttributes(window,out color,out alpha,out flags))return false;
  string[] names={".Layered",".Color",".Alpha",".Flags"};long[] values={layered?2:1,color,alpha+1,flags};
  for(int i=0;i<names.Length;i++)if(!SetProp(window,SnapshotOwner+names[i],new IntPtr(values[i]))){foreach(var name in names)RemoveProp(window,SnapshotOwner+name);return false;}
  if(!SetProp(window,SnapshotOwner,new IntPtr(Process.GetCurrentProcess().Id))){foreach(var name in names)RemoveProp(window,SnapshotOwner+name);return false;}
  if(!layered)SetWindowLongPtr(window,-20,new IntPtr(style|0x80000));
  if(SetLayeredWindowAttributes(window,0,0,2))return true;RestoreOpacity(window);return false;
 }
 internal static void RestoreOpacity(IntPtr window){
  long layered=GetProp(window,SnapshotOwner+".Layered").ToInt64(),alpha=GetProp(window,SnapshotOwner+".Alpha").ToInt64(),flags=GetProp(window,SnapshotOwner+".Flags").ToInt64();
  if(layered<1||layered>2||alpha<1||alpha>256||flags<1||flags>3)return;
  if(layered==2){if(!SetLayeredWindowAttributes(window,(uint)GetProp(window,SnapshotOwner+".Color").ToInt64(),(byte)(alpha-1),(uint)flags)){RetryReveal(window);return;}}
  else SetWindowLongPtr(window,-20,new IntPtr(GetWindowLongPtr(window,-20).ToInt64()&~0x80000L));
  foreach(var name in new[]{".Layered",".Color",".Alpha",".Flags",""})RemoveProp(window,SnapshotOwner+name);
  pendingReveal.Remove(window);
 }
 static void RetryReveal(IntPtr window){
  pendingReveal[window]=GetProp(window,SnapshotOwner);if(revealRetry==null){revealRetry=new System.Windows.Forms.Timer{Interval=100};revealRetry.Tick+=(s,e)=>{foreach(var item in pendingReveal.ToArray()){if(GetProp(item.Key,SnapshotOwner)!=item.Value)pendingReveal.Remove(item.Key);else RestoreOpacity(item.Key);}if(pendingReveal.Count==0)revealRetry.Stop();};}revealRetry.Start();
 }
 void RenderTick(object sender,EventArgs args){Tick();}
 void Rendering(bool value){if(rendering==value)return;rendering=value;if(value)Media.CompositionTarget.Rendering+=RenderTick;else Media.CompositionTarget.Rendering-=RenderTick;}
 sealed class TransitionState {public DockWindow window;public int previous,count;}
 static readonly Dictionary<long,TransitionState> transitions=new Dictionary<long,TransitionState>();
 static TransitionState Suppress(DockWindow window){
  TransitionState state;if(transitions.TryGetValue(window.handle,out state)&&state.window.pid==window.pid){state.count++;return state;}
  int previous;if(DwmGetWindowAttribute(new IntPtr(window.handle),3,out previous,4)!=0)previous=0;
  state=new TransitionState{window=window,previous=previous,count=1};transitions[window.handle]=state;int disabled=1;DwmSetWindowAttribute(new IntPtr(window.handle),3,ref disabled,4);return state;
 }
 static void Unsuppress(TransitionState state){
  if(--state.count!=0)return;TransitionState current;if(transitions.TryGetValue(state.window.handle,out current)&&current==state){transitions.Remove(state.window.handle);if(DockBackend.Valid(state.window))DwmSetWindowAttribute(new IntPtr(state.window.handle),3,ref state.previous,4);}
 }
 internal static bool SetState(DockWindow window,bool minimized){
  if(!DockBackend.Valid(window))return false;var state=Suppress(window);
  try{return DockBackend.SetMinimized(window,minimized);}finally{Unsuppress(state);}
 }
 internal static async Task<bool> SetStateAsync(DockWindow window,bool minimized,Func<bool> current=null){
  if(!DockBackend.Valid(window)||current!=null&&!current())return false;
  if(IsIconic(new IntPtr(window.handle))==minimized)return DockBackend.CanManage(window);
  var state=Suppress(window);
  try{return await DockBackend.SetMinimizedAsync(window,minimized,current);}finally{Unsuppress(state);}
 }
 async Task<bool> Minimize(Flight flight){
  flight.minimizedByUs=true;bool result=await SetStateAsync(flight.window,true,()=>Current(flight));
  if(!Current(flight)){if(!flights.ContainsKey(flight.window.handle)&&DockBackend.Valid(flight.window))await SetStateAsync(flight.window,false,()=>!flights.ContainsKey(flight.window.handle));return false;}return result;
 }
 internal static async Task Compose(){
  var ready=new TaskCompletionSource<bool>();int frames=0;EventHandler rendered=(s,e)=>{if(++frames==2)ready.TrySetResult(true);};
  Media.CompositionTarget.Rendering+=rendered;try{await Task.WhenAny(ready.Task,Task.Delay(80));}finally{Media.CompositionTarget.Rendering-=rendered;}
 }
 public bool Interact(DockEntry entry,DockClickAction action,DockWindow window){
  if(disposed||!allowed())return false;
  // A newly opened application owns its first frame; never minimize it to replay its launch.
  if(action==DockClickAction.Launch)return false;
  Flight running;if(window!=null&&flights.TryGetValue(window.handle,out running)&&(running.preparing||running.surface!=null)){running.goal=1-running.goal;timer.Start();return true;}
  if(action==DockClickAction.Minimize)return Begin(window,target(entry,window),true);
  return false;
 }
 public bool Restore(DockWindow window){
  if(disposed||!allowed()||!DockBackend.Valid(window)||!DockBackend.CanManage(window)||!IsIconic(new IntPtr(window.handle)))return false;
  var icon=target(null,window);if(icon.IsEmpty)return false;Error=null;Flight flight;
  // Without a retained image, let Windows restore immediately instead of hiding the app for capture.
  if(!flights.TryGetValue(window.handle,out flight)||flight.texture==null||flight.window.pid!=window.pid)return false;
  try{
   if(flight.surface==null)flight.surface=new GenieSurface(flight.texture,flight.bounds,icon);
   if(!HideForSnapshot(new IntPtr(window.handle))){Release(flight);flights.Remove(window.handle);return false;}
   flight.hiddenForSnapshot=true;flight.restoring=true;flight.icon=icon;flight.goal=0;flight.position=1;flight.preparing=true;ShowRestore(flight);timer.Start();return true;
  }catch(Exception ex){Error="窗口动效暂不可用："+ex.Message;Release(flight);flights.Remove(window.handle);return false;}
 }
 async void ShowRestore(Flight flight){
  try{if(!await SetStateAsync(flight.window,false,()=>Current(flight)))throw new InvalidOperationException("窗口暂时无法还原。");if(!Current(flight))return;var surface=flight.surface;surface.Render(flight.position);surface.Show();if(await Task.WhenAny(surface.Ready,Task.Delay(500))!=surface.Ready||!await surface.Ready)throw new InvalidOperationException("窗口动画未及时就绪。");if(!Current(flight)||!allowed())return;flight.stamp=clock.Elapsed.TotalSeconds;Rendering(true);}
  catch(Exception ex){if(Current(flight)){Error=ex.Message;Release(flight);CompleteFallback(flight);}}
  finally{if(flight.surface!=null)flight.preparing=false;}
 }
 internal bool Begin(DockWindow window,Rectangle icon,bool minimize){
  if(disposed||!allowed()||!DockBackend.Valid(window)||!DockBackend.CanManage(window)||icon.Width<=0||icon.Height<=0||IsIconic(new IntPtr(window.handle)))return false;
  Error=null;RECT rect;if(!GetWindowRect(new IntPtr(window.handle),out rect)||rect.Rectangle.Width<1||rect.Rectangle.Height<1||(long)rect.Rectangle.Width*rect.Rectangle.Height>16777216)return false;
  Flight old;if(flights.TryGetValue(window.handle,out old)){old.cancelled=true;Release(old);}
  var flight=new Flight{window=window,bounds=rect.Rectangle,icon=icon,goal=minimize?1:0,preparing=true};flights[window.handle]=flight;timer.Start();Prepare(flight);return true;
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
   var source=new LiveGlassBackdrop.Source{handle=new IntPtr(flight.window.handle),pid=flight.window.pid,bounds=flight.bounds,visible=flight.bounds};
   captured=await Snapshot(flight.window,flight.bounds);if(!Current(flight)||!DockBackend.Valid(flight.window)||!allowed())return;if(captured==null)return;
   if(flight.goal==0&&!flight.restoring)return;
   Rectangle crop=Rectangle.Empty;RECT visible;if(DwmGetWindowAttribute(source.handle,9,out visible,16)==0&&flight.bounds.Contains(visible.Rectangle)&&visible.Rectangle.Width>0&&visible.Rectangle.Height>0){crop=visible.Rectangle;crop.Offset(-flight.bounds.X,-flight.bounds.Y);flight.bounds=visible.Rectangle;}
   var image=captured;captured=null;
   var texture=await Task.Run(()=>{using(image){if(crop.IsEmpty)return GenieSurface.Texture(image);using(var trimmed=image.Clone(crop,PixelFormat.Format32bppArgb))return GenieSurface.Texture(trimmed);}});
   if(!Current(flight)||!DockBackend.Valid(flight.window)||!allowed())return;
   flight.texture=texture;flight.position=flight.restoring?1:0;
   flight.surface=new GenieSurface(flight.texture,flight.bounds,flight.icon);flight.surface.Render(flight.position);flight.surface.Show();
   var surface=flight.surface;if(await Task.WhenAny(surface.Ready,Task.Delay(500))!=surface.Ready||!await surface.Ready)throw new InvalidOperationException("窗口动画未及时就绪。");
   if(!Current(flight)||!allowed()||!DockBackend.Valid(flight.window))return;
   if(!flight.restoring){await Compose();if(!Current(flight)||!allowed())return;if(!await Minimize(flight)){Release(flight);return;}Reveal(flight);}flight.stamp=clock.Elapsed.TotalSeconds;
   Rendering(true);
  }catch(Exception ex){if(Current(flight)){Error="窗口动效暂不可用："+ex.Message;Release(flight);}}
  finally{
   if(captured!=null)captured.Dispose();flight.preparing=false;
   if(Current(flight)&&flight.surface==null)CompleteFallback(flight);
  }
 }
 async void CompleteFallback(Flight flight){
  flight.preparing=true;
  try{
   while(Current(flight)&&allowed()){
    double goal=flight.goal;if(goal==0&&!flight.minimizedByUs&&!flight.restoring)break;
    if(goal==1)flight.minimizedByUs=true;
    bool applied=await SetStateAsync(flight.window,goal==1,()=>Current(flight)&&flight.goal==goal);
    if(!Current(flight))return;if(flight.goal!=goal)continue;
    if(!applied)Error="窗口未接受此次切换，请从任务栏重试。";break;
   }
  }catch(Exception ex){if(Current(flight))Error=ex.Message;}
  finally{if(Current(flight)){Release(flight);flights.Remove(flight.window.handle);}flight.preparing=false;}
 }
 void Tick(){
  if(disposed)return;
  try{
   double now=clock.Elapsed.TotalSeconds;
   if(!allowed()){Cancel();return;}
   foreach(var flight in flights.Values.ToArray()){
    if(!DockBackend.Valid(flight.window)){Release(flight);flights.Remove(flight.window.handle);continue;}
    if(flight.preparing||flight.surface==null)continue;
    if(!IsIconic(new IntPtr(flight.window.handle))&&!flight.hiddenForSnapshot){Release(flight);flights.Remove(flight.window.handle);continue;}
    double elapsed=Math.Max(0,now-flight.stamp);flight.stamp=now;
    bool finished=AdvanceMotion(ref flight.position,ref flight.velocity,flight.goal,elapsed);flight.surface.Render(flight.position);
    if(finished){
     Finish(flight);
    }
   }
   // Minimized snapshots are bounded and only retained for the reverse Dock transition.
   foreach(var flight in flights.Values.Where(f=>!f.preparing&&f.surface==null).Skip(6).ToArray()){Release(flight);flights.Remove(flight.window.handle);}
   if(!Surfaces.Any())Rendering(false);if(ActiveCount==0)timer.Stop();
  }catch(Exception ex){Error="窗口动效暂不可用："+ex.Message;Cancel();}
 }
 async void Finish(Flight flight){
  flight.preparing=true;double goal=flight.goal;
  try{
   if(goal==0&&!flight.hiddenForSnapshot)flight.hiddenForSnapshot=HideForSnapshot(new IntPtr(flight.window.handle));
   bool changed=await (goal==1?Minimize(flight):SetStateAsync(flight.window,false,()=>Current(flight)&&flight.goal==goal));if(!Current(flight)||flight.goal!=goal)return;if(!changed)throw new InvalidOperationException("窗口未接受此次切换。");
   if(goal==0){Reveal(flight);await Compose();if(!Current(flight)||flight.goal!=goal)return;try{await DockBackend.ActivateAsync(flight.window);}catch(InvalidOperationException ex){Error=ex.Message;}if(!Current(flight)||flight.goal!=goal)return;Release(flight);flights.Remove(flight.window.handle);}
   else{flight.minimizedByUs=true;Reveal(flight);flight.surface.Dispose();flight.surface=null;}
  }catch(Exception ex){if(Current(flight)){Error=ex.Message;Release(flight);flights.Remove(flight.window.handle);}}
  finally{if(Current(flight)&&flight.surface!=null&&flight.goal!=goal){if(!IsIconic(new IntPtr(flight.window.handle))&&!flight.hiddenForSnapshot)flight.hiddenForSnapshot=HideForSnapshot(new IntPtr(flight.window.handle));flight.stamp=clock.Elapsed.TotalSeconds;}flight.preparing=false;}
 }
 bool Current(Flight flight){Flight current;return !disposed&&!flight.cancelled&&flights.TryGetValue(flight.window.handle,out current)&&current==flight;}
 async void RestoreCancelled(DockWindow window){
  try{if(!await DockBackend.SetMinimizedAsync(window,false,()=>!flights.ContainsKey(window.handle))&&!flights.ContainsKey(window.handle))Error="窗口未能还原，请从 Windows 任务栏打开。";}catch(Exception ex){Error=ex.Message;}
 }
 public void Cancel(){
  Rendering(false);var cancelled=flights.Values.ToArray();flights.Clear();timer.Stop();
  foreach(var flight in cancelled){flight.cancelled=true;bool restore=flight.minimizedByUs&&(flight.surface!=null||flight.preparing);Release(flight);if(restore&&DockBackend.Valid(flight.window))RestoreCancelled(flight.window);}
 }
 static void Reveal(Flight flight){if(!flight.hiddenForSnapshot)return;var handle=new IntPtr(flight.window.handle);if(DockBackend.Valid(flight.window)&&GetProp(handle,SnapshotOwner)==new IntPtr(Process.GetCurrentProcess().Id))RestoreOpacity(handle);flight.hiddenForSnapshot=false;}
 static void Release(Flight flight){Reveal(flight);if(flight.surface!=null){flight.surface.Dispose();flight.surface=null;}flight.texture=null;}
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
   AllowsTransparency=true;WindowStyle=Wpf.WindowStyle.None;ResizeMode=Wpf.ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=!StartupWindowOrder.MaskVisible();Background=Media.Brushes.Transparent;
   Width=canvas.Width;Height=canvas.Height;Left=canvas.Left;Top=canvas.Top;
   viewport=new System.Windows.Controls.Viewport3D{Width=canvas.Width,Height=canvas.Height,IsHitTestVisible=false,ClipToBounds=false};
   viewport.Camera=new Media3D.OrthographicCamera(new Media3D.Point3D(canvas.Width/2.0,-canvas.Height/2.0,100),new Media3D.Vector3D(0,0,-1),new Media3D.Vector3D(0,1,0),canvas.Width){NearPlaneDistance=1,FarPlaneDistance=200};
   var brush=new Media.ImageBrush(texture){Stretch=Media.Stretch.Fill};brush.Freeze();
   var material=new Media3D.DiffuseMaterial(brush);material.Freeze();
   MeshRows(Rows);
   viewport.Children.Add(new Media3D.ModelVisual3D{Content=new Media3D.GeometryModel3D(mesh,material)});viewport.Children.Add(new Media3D.ModelVisual3D{Content=new Media3D.AmbientLight(Media.Colors.White)});
   Content=new System.Windows.Controls.Viewbox{Stretch=Media.Stretch.Fill,Child=viewport};
   // Per-pixel alpha also lets pointer input pass through overlays across processes.
   SourceInitialized+=(s,e)=>{var window=new System.Windows.Interop.WindowInteropHelper(this).Handle;System.Windows.Interop.HwndSource.FromHwnd(window).AddHook(StartupWindowOrder.Hook);if(!Topmost)StartupWindowOrder.RestoreTopmost(window,()=>Topmost=true);SetWindowLongPtr(window,-20,new IntPtr(GetWindowLongPtr(window,-20).ToInt64()|0x080000A0L));SetWindowPos(window,StartupWindowOrder.InsertAfter(window,new IntPtr(-1)),canvas.X,canvas.Y,canvas.Width,canvas.Height,0x10);};
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
