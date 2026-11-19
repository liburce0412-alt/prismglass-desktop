using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Threading.Tasks;
using Media = System.Windows.Media;
using Texture = System.Windows.Media.Imaging.BitmapSource;

internal sealed partial class StageManager:IDisposable {
 [StructLayout(LayoutKind.Sequential)]internal struct POINT {public int x,y;}
 [StructLayout(LayoutKind.Sequential)]internal struct RECT {public int l,t,r,b;}
 [StructLayout(LayoutKind.Sequential)]internal struct Placement {public int length,flags,show;public POINT min,max;public RECT normal;}
 internal sealed class Member {public DockWindow window;public Placement original,layout,geometryIntent;public int recoveryVersion,visibilityIntent;internal Placement standalone;internal readonly PlacementLane placements=new PlacementLane();internal string placementFailure="";internal string thumbnailStatus="正在读取预览…";internal int thumbnailAttempts,thumbnailBytes;internal DateTime thumbnailRetry,thumbnailCaptured;internal bool thumbnailProtected,thumbnailMinimized;}
 internal sealed class Group {public string id=Guid.NewGuid().ToString("N"),monitor;public List<Member> members=new List<Member>();public bool layoutPending=true,restoreStandalone;public string layoutNotice="";public string Name{get{return members.Count==0?"窗口组":members[0].window.title;}}}
 internal struct CardPose {public Rectangle bounds;public double angle,depth;}
 [DllImport("user32.dll")]static extern bool GetWindowPlacement(IntPtr window,ref Placement placement);
 [DllImport("user32.dll")]static extern bool IsHungAppWindow(IntPtr window);
 [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPlacement(IntPtr window,ref Placement placement);
 [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr window,int command);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out RECT value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 [StructLayout(LayoutKind.Sequential)]struct MinMax {public POINT reserved,maxSize,maxPosition,minTrack,maxTrack;}
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 [DllImport("user32.dll")]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wParam,ref MinMax limits,uint flags,uint timeout,out IntPtr result);
 [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
 readonly string journal;readonly Action cancelMotion;readonly Func<List<DockWindow>> readWindows;
 readonly List<Member> unresolved=new List<Member>();readonly HashSet<Member> restoring=new HashSet<Member>();bool wasSelected;
 readonly List<Group> groups=new List<Group>();readonly Dictionary<string,Group> active=new Dictionary<string,Group>();readonly Dictionary<string,StageStrip> strips=new Dictionary<string,StageStrip>();
 Dictionary<string,Rectangle> areas=new Dictionary<string,Rectangle>();HashSet<string> fullscreen=new HashSet<string>();bool enabled,switching,disposed,suspended;DateTime scan;string saved="";
 const int ThumbnailByteBudget=96*1024*1024;
 readonly Dictionary<long,Texture> pictures=new Dictionary<long,Texture>();readonly List<long> pictureOrder=new List<long>();readonly Dictionary<long,Task> capturing=new Dictionary<long,Task>();
 readonly Dictionary<long,uint> pictureOwners=new Dictionary<long,uint>();readonly StageThumbnailCapture thumbnails=new StageThumbnailCapture();
 readonly Dictionary<long,uint> unmanaged=new Dictionary<long,uint>();
 readonly List<StageFlight> flights=new List<StageFlight>();readonly Stopwatch motionClock=Stopwatch.StartNew();int epoch;bool motionEnabled,flightsReady;Group queued,destination;bool queuedFocus,destinationFocus;
 Group warmed;DateTime warmedAt;int pendingStates,retiring,membershipEpoch,displayEpoch;bool stoppedRestored,recovering,stopping;
 string groupLayout="auto",singleSize="fit";int maxWidth=85,maxHeight=85;
 internal IEnumerable<StageStrip> Strips {get{return strips.Values;}}
 internal bool Busy {get{return recovering||stopping||switching||capturing.Count>0||pendingStates>0||retiring>0;}}
 internal bool Animating {get{return flightsReady;}}
 internal int SkippedWindows {get{return unmanaged.Count;}}
 sealed class StageFlight {
  public Member member;public bool incoming,hiddenForSnapshot;public NativeWindowMotion.GenieSurface surface;public double position,velocity,goal,started,updated;
  public void Retarget(double target,double now){goal=target;incoming=goal==0;started=updated=now;}
  public bool Advance(double now){
   double dt=Math.Max(0,now-updated);updated=now;
   return NativeWindowMotion.AdvanceMotion(ref position,ref velocity,goal,dt);
  }
 }
 string operationalError="",recoveryWarning="";internal string Error {get{return operationalError.Length>0?operationalError:recoveryWarning;}private set{operationalError=value??"";}}
 internal IList<Group> Groups {get{return groups.AsReadOnly();}}
 internal string Hover(Point pointer,out Rectangle bounds,out string caption){bounds=Rectangle.Empty;caption="";if(!enabled||Busy)return "";foreach(var strip in strips.Values){string id=strip.Hover(pointer,out bounds,out caption);if(id.Length>0)return id;}return "";}
 public StageManager(string root,Action cancel):this(root,cancel,DockBackend.ReadWindows){}
 internal StageManager(string root,Action cancel,Func<List<DockWindow>> windows){journal=Path.Combine(root,"stage-recovery.json");cancelMotion=cancel;readWindows=windows;Error="";Recover();}
 internal static Placement Read(DockWindow window){var p=new Placement{length=Marshal.SizeOf(typeof(Placement))};if(!DockBackend.Valid(window)||!GetWindowPlacement(new IntPtr(window.handle),ref p))throw new InvalidOperationException("无法读取窗口布局。");return p;}
 static bool Restore(Member member,bool original,bool visibilityOnly=false,bool allowPostedShow=true){
  if(!DockBackend.Valid(member.window)||!DockBackend.CanManage(member.window))return true;
  var h=new IntPtr(member.window.handle);if(visibilityOnly){bool minimized=original&&member.original.show==2;return NativeWindowMotion.IsIconic(h)==minimized||ShowWindowAsync(h,minimized?7:4);}
  var p=original?member.original:member.layout;
  if(!original&&p.show==2)p.show=(p.flags&2)!=0?3:1;
  // Finishing a flight often finds the incoming HWND already in this placement.
  // Reapplying it forces a second native resize/redraw at the animation boundary.
  var current=new Placement{length=Marshal.SizeOf(typeof(Placement))};if(GetWindowPlacement(h,ref current)&&current.show==p.show&&(p.show!=2||((current.flags^p.flags)&2)==0)&&current.normal.Equals(p.normal))return true;
  var beforePlacement=current;if(IsHungAppWindow(h))return false; // Keep the journal until the target can accept synchronous placement.
  int before;if(DwmGetWindowAttribute(h,3,out before,4)!=0)before=0;int disable=1;DwmSetWindowAttribute(h,3,ref disable,4);
  try{
   p.length=Marshal.SizeOf(typeof(Placement));if(!SetWindowPlacement(h,ref p)){member.placementFailure="Win32 "+Marshal.GetLastWin32Error();return false;}
   for(int i=0;i<16;i++){
    if(!DockBackend.Valid(member.window))return true;
    if(GetWindowPlacement(h,ref current)&&current.show==p.show&&(p.show!=2||((current.flags^p.flags)&2)==0)&&current.normal.Equals(p.normal))return true;
    // Some applications restore their saved normal bounds after handling the first show.
    if(i==4&&beforePlacement.show!=p.show&&current.show==p.show&&current.normal.Equals(beforePlacement.normal)&&!current.normal.Equals(p.normal)){
     if(!SetWindowPlacement(h,ref p))return false;
    }
    if(allowPostedShow&&i==7&&current.show!=p.show)ShowWindowAsync(h,p.show==2?7:p.show==1?9:p.show);
    System.Threading.Thread.Sleep(10);
   }
   if(!allowPostedShow&&current.show==p.show&&beforePlacement.show!=p.show&&!current.normal.Equals(beforePlacement.normal))member.geometryIntent=current;
   member.placementFailure=String.Format("期望 {0} [{1},{2},{3},{4}]，读回 {5} [{6},{7},{8},{9}]",p.show,p.normal.l,p.normal.t,p.normal.r,p.normal.b,current.show,current.normal.l,current.normal.t,current.normal.r,current.normal.b);return false;
  }finally{if(DockBackend.Valid(member.window))DwmSetWindowAttribute(h,3,ref before,4);}
 }
 internal sealed class PlacementLane {
  sealed class Request {internal Func<bool> apply;internal readonly TaskCompletionSource<bool> done=new TaskCompletionSource<bool>();}
  readonly object gate=new object();Request pending;bool running;
  internal bool Busy {get{lock(gate)return running;}}
  internal Task<bool> Submit(Func<bool> apply){
   var request=new Request{apply=apply};Request replaced;
   lock(gate){
    replaced=pending;
    pending=request;if(!running){running=true;Task.Run((Action)Run);}
   }
   if(replaced!=null)replaced.done.TrySetResult(false);return request.done.Task;
  }
  void Run(){
   while(true){
    Request request;lock(gate){request=pending;pending=null;if(request==null){running=false;return;}}
    try{request.done.TrySetResult(request.apply());}catch(Exception ex){request.done.TrySetException(ex);}
   }
  }
 }
 async Task<bool> WatchPlacement(Task<bool> placement){
  pendingStates++;try{return await placement;}catch(Exception ex){Error=ex.Message;return false;}finally{pendingStates--;ClearStoppedRecovery();}
 }
 async Task<bool> Place(Member member,bool original,bool visibilityOnly=false,Placement? overridePlacement=null){
  if(!original&&DockBackend.Valid(member.window)){
   var before=Read(member.window);var target=member.layout;int show=target.show==2?((target.flags&2)!=0?3:1):target.show;
   if(!before.normal.Equals(target.normal))member.geometryIntent=target;
   if(before.show!=show)member.visibilityIntent=show;
   Save();
  }
  var snapshot=new Member{window=member.window,layout=member.layout,original=overridePlacement??member.original};
  var applied=WatchPlacement(member.placements.Submit(()=>Restore(snapshot,original,visibilityOnly)));
  if(!original&&await Task.WhenAny(applied,Task.Delay(450))!=applied)return false;
  return await applied;
 }
 void Save(){
  var all=groups.SelectMany(g=>g.members).Concat(unresolved).ToList();
  if(all.Count==0){if(File.Exists(journal))File.Delete(journal);saved="";return;}
  string value=new JavaScriptSerializer().Serialize(all);if(value==saved)return;DesktopSettingsFiles.Write(journal,value);saved=value;
 }
 static Placement RecoveryTarget(Member member,Placement current){
  var original=member.original;
  var expected=member.recoveryVersion==0?member.layout:member.geometryIntent;
  if(expected.length==0||expected.normal.Equals(original.normal)||!current.normal.Equals(expected.normal))original.normal=current.normal;
  int expectedShow=member.visibilityIntent;
  if(member.recoveryVersion==0)expectedShow=current.show==2?2:member.layout.show;
  if(expectedShow==0||current.show!=expectedShow){original.show=current.show;original.flags=current.flags;}
  return original;
 }
 async Task RecoverMember(Member member){
  if(!restoring.Add(member))return;try{
   bool ok=await member.placements.Submit(()=>{
    if(!DockBackend.Valid(member.window)||!DockBackend.CanManage(member.window))return true;
    var target=RecoveryTarget(member,Read(member.window));return Restore(new Member{window=member.window,original=target},true);
   });
   if(ok){unresolved.Remove(member);scan=DateTime.MinValue;}else recoveryWarning="部分窗口未接受恢复；已保留其恢复记录，其余窗口可继续使用。";
   if(unresolved.Count==0)recoveryWarning="";Save();
  }catch(Exception ex){recoveryWarning="窗口恢复记录已保留："+ex.Message;}finally{restoring.Remove(member);}
 }
 async void RetryUnresolved(){await Task.WhenAll(unresolved.ToArray().Select(RecoverMember));}
 async void Recover(){
  recovering=true;
  try{if(!File.Exists(journal))return;var members=new JavaScriptSerializer().Deserialize<List<Member>>(DesktopSettingsFiles.Read(journal));if(members==null||members.Count>512)throw new IOException("恢复记录无效。");
   unresolved.AddRange(members.Where(m=>m!=null&&m.window!=null));await Task.WhenAll(unresolved.ToArray().Select(RecoverMember));if(members.Count==0)Save();}
  catch(Exception ex){Error="台前调度恢复失败："+ex.Message;}finally{recovering=false;}
 }
 string edgeOpen,edgeCandidate;DateTime edgeSince,edgeKeep;
 internal void EdgeReveal(string monitor,Rectangle area,Point pointer){
  var now=DateTime.UtcNow;
  if(monitor!=edgeCandidate){edgeOpen=null;edgeSince=DateTime.MinValue;edgeCandidate=monitor;}
  if(monitor==null)return;
  bool edge=pointer.X>=area.Left&&pointer.X<area.Left+4&&pointer.Y>=area.Top+area.Height/4&&pointer.Y<area.Bottom-area.Height/4;
  StageStrip strip;bool over=strips.TryGetValue(monitor,out strip)&&strip.OccupiedBounds.Contains(pointer);
  if(edge){if(edgeSince==DateTime.MinValue)edgeSince=now;if((now-edgeSince).TotalMilliseconds>=180)edgeOpen=monitor;}else edgeSince=DateTime.MinValue;
  if(edgeOpen==monitor){if(edge||over||switching)edgeKeep=now.AddMilliseconds(550);else if(now>edgeKeep)edgeOpen=null;}
 }
 bool MonitorPaused(string monitor){return suspended||fullscreen.Contains(monitor)&&monitor!=edgeOpen;}
 internal void DisplaysChanged(){
  if(disposed)return;displayEpoch++;CancelTransition();cancelMotion();warmed=null;scan=DateTime.MinValue;
  // Windows has already adapted placements to the new display topology. Keep those
  // coordinates; only a later explicit activation applies the chosen group layout.
  foreach(var group in groups){foreach(var member in group.members)if(DockBackend.Valid(member.window))try{member.layout=Read(member.window);if(member.placements.Busy)CorrectPlacement(member,member.layout);}catch(InvalidOperationException){}group.layoutPending=true;group.layoutNotice="";}
  foreach(var strip in strips.Values)strip.SetPaused(true);
 }
 async void CorrectPlacement(Member member,Placement desired){
  try{if(!await Place(member,true,false,desired))Error="窗口暂未接受显示布局；恢复记录已保留。";}catch(Exception ex){Error=ex.Message;}
 }
 public void Configure(bool selected,bool paused,bool motion,IDictionary<string,Rectangle> monitors,IEnumerable<string> fullscreenMonitors=null,string layout="auto",string single="fit",int width=85,int height=85){
  if(disposed)return;var hidden=new HashSet<string>(fullscreenMonitors??Enumerable.Empty<string>());var resumed=fullscreen.Where(m=>!hidden.Contains(m)).ToArray();
  bool changed=areas.Count>0&&(areas.Count!=monitors.Count||areas.Any(p=>!monitors.ContainsKey(p.Key)||monitors[p.Key]!=p.Value));if(changed&&(switching||pendingStates>0||capturing.Count>0||areas.Keys.Any(m=>!monitors.ContainsKey(m))))DisplaysChanged();
  if(groupLayout!=layout||singleSize!=single||maxWidth!=width||maxHeight!=height){groupLayout=layout;singleSize=single;maxWidth=width;maxHeight=height;foreach(var group in groups)group.layoutPending=true;}
  foreach(var group in groups){Rectangle before,after;if(areas.TryGetValue(group.monitor,out before)&&monitors.TryGetValue(group.monitor,out after)&&before!=after&&!after.Contains(before))group.layoutPending=true;}
  if(paused&&!suspended||hidden.Any(m=>!fullscreen.Contains(m))&&switching){CancelTransition();foreach(var group in groups)foreach(var member in group.members.Where(m=>m.placements.Busy)){Group current;var desired=member.layout;if(!active.TryGetValue(group.monitor,out current)||current!=group)desired.show=2;else if(desired.show==2)desired.show=(desired.flags&2)!=0?3:1;CorrectPlacement(member,desired);}}suspended=paused;fullscreen=hidden;motionEnabled=motion;areas=new Dictionary<string,Rectangle>(monitors);
  if(selected&&!wasSelected&&!recovering&&!stopping&&unresolved.Count>0)RetryUnresolved();wasSelected=selected;
  if(!selected){if(enabled)Stop();if(!File.Exists(journal))Error="";return;}
  if(paused){foreach(var strip in strips.Values)strip.SetPaused(true);return;}
  if(!enabled){if(operationalError.Length>0)return;enabled=true;membershipEpoch++;stoppedRestored=false;scan=DateTime.MinValue;}
  try{
   // Membership discovery must not choose a temporary active group while a
   // merge/switch is preparing surfaces or posting native visibility changes.
   if((changed||!switching&&pendingStates==0&&retiring==0)&&DateTime.UtcNow-scan>TimeSpan.FromMilliseconds(800)){
    scan=DateTime.UtcNow;Refresh();
   }
   foreach(var pair in strips){pair.Value.Motion=motion;pair.Value.SetPaused(MonitorPaused(pair.Key));if(areas.ContainsKey(pair.Key))pair.Value.RefreshGroups(groups.Where(g=>g.monitor==pair.Key).ToList(),areas[pair.Key]);}
   if(!paused&&!switching&&pendingStates==0){var foreground=GetForegroundWindow().ToInt64();var group=groups.FirstOrDefault(g=>g.members.Any(m=>m.window.handle==foreground));Group current;if(group!=null&&group.monitor!=edgeOpen&&(!active.TryGetValue(group.monitor,out current)||current!=group))Switch(group,false);}
   foreach(var monitor in resumed)if(areas.ContainsKey(monitor)&&active.ContainsKey(monitor))RetireBackground(monitor,epoch);
   if(!Busy&&!suspended&&!thumbnails.Busy)FillMissingThumbnails();
  }catch(Exception ex){Error=ex.Message;Stop();}
 }
 void ReconcileMonitorMembership(){
  foreach(var group in groups.ToArray()){
   var moved=group.members.Where(m=>!NativeWindowMotion.IsIconic(new IntPtr(m.window.handle)))
    .GroupBy(m=>Screen.FromHandle(new IntPtr(m.window.handle)).DeviceName)
    .Where(part=>part.Key!=group.monitor&&areas.ContainsKey(part.Key)).ToArray();
   foreach(var part in moved){
    foreach(var member in part){member.layout=Read(member.window);member.standalone=member.layout;}
    if(part.Count()==group.members.Count){
     Group current;if(active.TryGetValue(group.monitor,out current)&&current==group)active.Remove(group.monitor);
     group.monitor=part.Key;
    }else{
     var destination=new Group{monitor=part.Key,members=part.ToList()};
     foreach(var member in destination.members)group.members.Remove(member);
     groups.Add(destination);
    }
   }
  }
 }
 void Refresh(){
  foreach(var group in groups.ToArray()){
   if(!areas.ContainsKey(group.monitor)){if(group.members.Any(m=>m.placements.Busy))continue;foreach(var m in group.members)if(!Restore(m,true,true))throw new IOException("显示器停用时未能恢复窗口。");groups.Remove(group);active.Remove(group.monitor);continue;}
   group.members.RemoveAll(m=>!DockBackend.Valid(m.window));if(group.members.Count==0){groups.Remove(group);Group current;if(active.TryGetValue(group.monitor,out current)&&current==group)active.Remove(group.monitor);}
  }
  if(!switching&&pendingStates==0&&retiring==0&&(Control.MouseButtons&MouseButtons.Left)==0)ReconcileMonitorMembership();
  var known=new HashSet<long>(groups.SelectMany(g=>g.members).Select(m=>m.window.handle));
  var windows=readWindows();foreach(var handle in unmanaged.Keys.Where(h=>!windows.Any(w=>w.handle==h&&w.pid==unmanaged[h])).ToArray())unmanaged.Remove(handle);
  foreach(var w in windows){
   string monitor=Screen.FromHandle(new IntPtr(w.handle)).DeviceName;
   if(known.Contains(w.handle)||unresolved.Any(m=>m.window.handle==w.handle&&m.window.pid==w.pid)||!areas.ContainsKey(monitor)||w.pid==(uint)Process.GetCurrentProcess().Id||!DockBackend.CanManage(w)||unmanaged.ContainsKey(w.handle))continue;
   // Recording controls and other non-minimizable tool windows must not stop all groups.
   if((GetWindowLongPtr(new IntPtr(w.handle),-16).ToInt64()&0x20000)==0){unmanaged[w.handle]=w.pid;continue;}
   var placement=Read(w);var group=new Group{monitor=monitor};group.members.Add(new Member{window=w,original=placement,layout=placement,recoveryVersion=1});groups.Add(group);
  }
  Save();
  var members=groups.SelectMany(g=>g.members).ToList();foreach(var handle in pictures.Keys.Where(h=>!members.Any(m=>m.window.handle==h&&pictureOwners.ContainsKey(h)&&pictureOwners[h]==m.window.pid)).ToArray())ForgetPicture(handle);
  foreach(var member in members){bool blocked=StageThumbnailCapture.Protected(member.window),minimized=NativeWindowMotion.IsIconic(new IntPtr(member.window.handle));if(blocked){ForgetPicture(member.window.handle);member.thumbnailStatus="此窗口不提供预览";member.thumbnailAttempts=3;}else if(member.thumbnailProtected||member.thumbnailMinimized&&!minimized){member.thumbnailAttempts=0;member.thumbnailRetry=DateTime.MinValue;member.thumbnailStatus="正在读取预览…";}member.thumbnailProtected=blocked;member.thumbnailMinimized=minimized;}
  foreach(var monitor in areas.Keys){if(!active.ContainsKey(monitor)){var current=groups.FirstOrDefault(g=>g.monitor==monitor&&g.members.Any(m=>m.window.handle==GetForegroundWindow().ToInt64()))??groups.FirstOrDefault(g=>g.monitor==monitor);if(current!=null){active[monitor]=current;RetireBackground(monitor,epoch);}}
   StageStrip strip;if(!strips.TryGetValue(monitor,out strip)){strip=new StageStrip(this,monitor);strips.Add(monitor,strip);}strip.SetPaused(MonitorPaused(monitor));strip.RefreshGroups(groups.Where(g=>g.monitor==monitor).ToList(),areas[monitor]);
  }
  foreach(var monitor in strips.Keys.Where(k=>!areas.ContainsKey(k)).ToArray()){strips[monitor].Dispose();strips.Remove(monitor);}
 }
 async Task Capture(Member member,int version){
  Task pending;if(capturing.TryGetValue(member.window.handle,out pending)){await pending;return;}
  var task=CaptureCore(member,version);capturing[member.window.handle]=task;
  try{await task;}finally{if(capturing.TryGetValue(member.window.handle,out pending)&&pending==task)capturing.Remove(member.window.handle);}
 }
 Texture Picture(Member member){uint pid;Texture image;return pictureOwners.TryGetValue(member.window.handle,out pid)&&pid==member.window.pid&&pictures.TryGetValue(member.window.handle,out image)?image:null;}
 void ForgetPicture(long handle){pictures.Remove(handle);pictureOwners.Remove(handle);pictureOrder.Remove(handle);}
 IEnumerable<Member> VisibleThumbnails(){
  int remaining=ThumbnailByteBudget,count=0;
  foreach(var group in groups.Where(g=>!MonitorPaused(g.monitor)))foreach(var member in group.members.Take(3)){
   if(member.thumbnailProtected||member.thumbnailBytes>remaining||Card(group,member).bounds.IsEmpty)continue;
   remaining-=member.thumbnailBytes;yield return member;if(++count==16)yield break;
  }
 }
 async void FillMissingThumbnails(){
  var member=VisibleThumbnails().FirstOrDefault(m=>Picture(m)==null&&m.thumbnailAttempts<3&&DateTime.UtcNow>=m.thumbnailRetry);if(member==null)return;
  var group=groups.First(g=>g.members.Contains(member));int version=epoch;
  try{await Capture(member,version);if(version==epoch&&enabled)RefreshStrip(group.monitor);}catch(Exception){/* Missing content keeps its explicit retry/unavailable state. */}
 }
 async Task CaptureCore(Member member,int version){
  Func<bool> wanted=()=>version==epoch&&enabled&&DockBackend.Valid(member.window);if(!wanted())return;
  if(StageThumbnailCapture.Protected(member.window)){ForgetPicture(member.window.handle);member.thumbnailProtected=true;member.thumbnailStatus="此窗口不提供预览";member.thumbnailAttempts=3;return;}
  bool missing=Picture(member)==null,minimized=NativeWindowMotion.IsIconic(new IntPtr(member.window.handle));if(minimized&&!missing)return;
  if(missing){member.thumbnailAttempts++;member.thumbnailStatus="正在读取预览…";}
  RECT rect=new RECT();Bitmap captured=null;bool retained=false;
  if(!minimized&&GetWindowRect(new IntPtr(member.window.handle),out rect))captured=await NativeWindowMotion.Snapshot(member.window,Rectangle.FromLTRB(rect.l,rect.t,rect.r,rect.b));
  if(captured==null&&missing&&wanted()){
   if(thumbnails.Busy){if(thumbnails.TimedOut){member.thumbnailAttempts=3;member.thumbnailStatus="预览暂不可用 · 点击打开";}else{member.thumbnailAttempts--;member.thumbnailRetry=DateTime.UtcNow.AddMilliseconds(250);}return;}
   captured=await thumbnails.Snapshot(member.window,wanted);retained=true;
  }
  using(var bitmap=captured){
   if(bitmap==null){if(missing){if(thumbnails.TimedOut)member.thumbnailAttempts=3;member.thumbnailStatus=member.thumbnailAttempts>=3?"预览暂不可用 · 点击打开":"正在读取预览…";member.thumbnailRetry=DateTime.UtcNow.AddMilliseconds(500*member.thumbnailAttempts);}return;}
   if(!wanted()||!StageThumbnailCapture.Allowed(member.window))return;
   RECT visible;var crop=new Rectangle(0,0,bitmap.Width,bitmap.Height);
   if(!retained&&DwmGetWindowAttribute(new IntPtr(member.window.handle),9,out visible,16)==0&&visible.l>=rect.l&&visible.t>=rect.t&&visible.r<=rect.r&&visible.b<=rect.b&&visible.r>visible.l&&visible.b>visible.t)crop=new Rectangle(visible.l-rect.l,visible.t-rect.t,visible.r-visible.l,visible.b-visible.t);
   double scale=Math.Min(1,1600.0/Math.Max(crop.Width,crop.Height));var texture=await Task.Run(()=>{using(var thumbnail=new Bitmap(Math.Max(1,(int)(crop.Width*scale)),Math.Max(1,(int)(crop.Height*scale)))){using(var graphics=Graphics.FromImage(thumbnail))graphics.DrawImage(bitmap,new Rectangle(Point.Empty,thumbnail.Size),crop,GraphicsUnit.Pixel);return NativeWindowMotion.GenieSurface.Texture(thumbnail);}});
   if(!wanted()||!StageThumbnailCapture.Allowed(member.window))return;pictures[member.window.handle]=texture;pictureOwners[member.window.handle]=member.window.pid;member.thumbnailStatus="";member.thumbnailAttempts=0;member.thumbnailCaptured=DateTime.UtcNow;member.thumbnailBytes=texture.PixelWidth*texture.PixelHeight*4;
   pictureOrder.Remove(member.window.handle);pictureOrder.Add(member.window.handle);while(pictureOrder.Count>16||pictures.Values.Sum(t=>t.PixelWidth*t.PixelHeight*4)>ThumbnailByteBudget){
    var visibleHandles=new HashSet<long>(VisibleThumbnails().Select(m=>m.window.handle));long old=pictureOrder.FirstOrDefault(h=>h!=member.window.handle&&!visibleHandles.Contains(h));ForgetPicture(old==0?pictureOrder[0]:old);
   }
  }
 }
 void ClearStoppedRecovery(){if(enabled||pendingStates>0||unresolved.Count>0||!stoppedRestored)return;try{if(File.Exists(journal))File.Delete(journal);stoppedRestored=false;}catch(IOException){}}
 async Task<bool> Minimize(Member member,Func<bool> wanted){
  if(!wanted())return false;if(DockBackend.Valid(member.window)&&!NativeWindowMotion.IsIconic(new IntPtr(member.window.handle))){member.visibilityIntent=2;Save();}int membership=membershipEpoch,display=displayEpoch;pendingStates++;
  try{
   bool result=false;Exception failure=null;try{result=await NativeWindowMotion.SetStateAsync(member.window,true,wanted)&&wanted();}catch(Exception ex){failure=ex;}
   // An already posted native command can finish after its caller was cancelled.
   // Correct only the same enrollment; a later Stage session owns its own layout.
   if(!wanted()&&membership==membershipEpoch){
    var owner=groups.FirstOrDefault(g=>g.members.Contains(member));Group current;
    bool changed=display!=displayEpoch;if(!enabled||owner!=null&&(MonitorPaused(owner.monitor)||active.TryGetValue(owner.monitor,out current)&&current==owner)||changed&&owner==null){
     bool original=!enabled||owner==null,restored=await Place(member,original,changed);
     // A display transition owns visibility only. Never submit a normal rectangle:
     // it can still describe the old display while the application's queue drains.
     if(changed&&restored){bool minimized=original&&member.original.show==2;for(int i=0;i<24&&membership==membershipEpoch&&DockBackend.Valid(member.window)&&NativeWindowMotion.IsIconic(new IntPtr(member.window.handle))!=minimized;i++)await Task.Delay(10);restored=membership!=membershipEpoch||!DockBackend.Valid(member.window)||NativeWindowMotion.IsIconic(new IntPtr(member.window.handle))==minimized;}
     if(!restored){stoppedRestored=false;Error="部分窗口未能恢复；恢复记录已保留。";}
    }
   }
   if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();return result&&wanted();
  }finally{pendingStates--;ClearStoppedRecovery();}
 }
 bool RetireWanted(Group group,Member member,string monitor,int version){Group current;return version==epoch&&enabled&&!switching&&!MonitorPaused(monitor)&&areas.ContainsKey(monitor)&&groups.Contains(group)&&group.members.Contains(member)&&active.TryGetValue(monitor,out current)&&current!=group;}
 async void RetireBackground(string monitor,int version){
  if(MonitorPaused(monitor)||monitor==edgeOpen||switching)return;
  // Include the gaps between awaited captures/native commands in Busy. Otherwise
  // the caller can start another transition while this retirement still owns work.
  retiring++;
  try{foreach(var group in groups.Where(g=>g.monitor==monitor).ToArray())foreach(var member in group.members.ToArray()){
   if(version!=epoch||!enabled||switching||MonitorPaused(monitor))return;
   if(!groups.Contains(group)||!group.members.Contains(member))continue;
   await Capture(member,version);if(version!=epoch||!enabled||switching||MonitorPaused(monitor))return;
   if(!groups.Contains(group)||!group.members.Contains(member))continue;
   Func<bool> wanted=()=>RetireWanted(group,member,monitor,version);if(wanted()&&!await Minimize(member,wanted)){
    if(!wanted())return;
    if(DockBackend.Valid(member.window)){
     bool restored=await Place(member,true);if(!wanted())return;
     if(restored)unmanaged[member.window.handle]=member.window.pid;
     else{unresolved.Add(member);recoveryWarning="部分窗口未接受后台切换；已保留其恢复记录，其余窗口可继续使用。";}
    }
    group.members.Remove(member);ForgetPicture(member.window.handle);if(group.members.Count==0)groups.Remove(group);Save();
   }
  }RefreshStrip(monitor);}catch(Exception ex){if(version==epoch){Error=ex.Message;Stop();}}finally{retiring--;}
 }
 CardPose Card(Group group,Member member){StageStrip strip;return strips.TryGetValue(group.monitor,out strip)?strip.Pose(group,member):new CardPose();}
 internal static Rectangle PlacementBounds(Placement placement,Screen screen,bool toolWindow=false){var rect=Rectangle.FromLTRB(placement.normal.l,placement.normal.t,placement.normal.r,placement.normal.b);if(!toolWindow)rect.Offset(screen.WorkingArea.Left-screen.Bounds.Left,screen.WorkingArea.Top-screen.Bounds.Top);return rect;}
 internal static Rectangle[] LayoutCells(Rectangle area,int count,string layout){
  if(count<1||area.Width<1||area.Height<1)return new Rectangle[0];int columns=layout=="columns"?count:(int)Math.Ceiling(Math.Sqrt(count)),rows=(count+columns-1)/columns,gap=12;var result=new Rectangle[count];
  int width=(area.Width-gap*(columns-1))/columns,height=(area.Height-gap*(rows-1))/rows;if(width<1||height<1)return new Rectangle[0];
  for(int i=0;i<count;i++)result[i]=new Rectangle(area.Left+i%columns*(width+gap),area.Top+i/columns*(height+gap),width,height);return result;
 }
 internal static Rectangle AvailableArea(Rectangle area,IEnumerable<Rectangle> obstacles){
  foreach(var obstacle in obstacles){if(obstacle.IsEmpty)continue;var occupied=Rectangle.Intersect(area,Rectangle.Inflate(obstacle,12,12));if(occupied.Width<=0||occupied.Height<=0)continue;area=new[]{Rectangle.FromLTRB(area.Left,area.Top,occupied.Left,area.Bottom),Rectangle.FromLTRB(occupied.Right,area.Top,area.Right,area.Bottom),Rectangle.FromLTRB(area.Left,area.Top,area.Right,occupied.Top),Rectangle.FromLTRB(area.Left,occupied.Bottom,area.Right,area.Bottom)}.OrderByDescending(r=>(long)r.Width*r.Height).First();}return area;
 }
 bool PreserveLayout(Group group,string reason){group.layoutPending=false;group.layoutNotice=reason;Error="";return true;}
 int layoutRequest;
 async Task<bool> PrepareLayout(Group group){
  int request=++layoutRequest,version=epoch;
  if(!group.layoutPending)return true;
  if(group.members.Any(m=>!DockBackend.Valid(m.window)||!DockBackend.CanManage(m.window))){Error="窗口已关闭或没有调整权限。";return false;}
  if(group.restoreStandalone&&group.members.Count==1){var member=group.members[0];if(member.standalone.length>0)member.layout=member.standalone;group.restoreStandalone=false;}
  if(group.members.Count==1&&singleSize=="remember"||group.members.Count>1&&groupLayout=="preserve")return PreserveLayout(group,"");
  Rectangle area;if(!areas.TryGetValue(group.monitor,out area))return false;
  StageStrip strip;if(strips.TryGetValue(group.monitor,out strip))area=AvailableArea(area,new[]{strip.OccupiedBounds});
  var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==group.monitor);if(screen==null)return false;
  var cells=LayoutCells(area,group.members.Count,groupLayout);if(cells.Length!=group.members.Count)return PreserveLayout(group,"可用空间不足，已保留窗口原位置。");
  var placements=new List<Placement>();
  for(int i=0;i<group.members.Count;i++){
   var member=group.members[i];
   var handle=new IntPtr(member.window.handle);var p=member.layout;bool tool=(GetWindowLongPtr(handle,-20).ToInt64()&0x80)!=0;var old=PlacementBounds(p,screen,tool);long style=GetWindowLongPtr(handle,-16).ToInt64();
   bool maximized=p.show==3||p.show==2&&(p.flags&2)!=0,fullscreen=(style&0x00c40000)==0&&old.Left<=screen.Bounds.Left+2&&old.Top<=screen.Bounds.Top+2&&old.Right>=screen.Bounds.Right-2&&old.Bottom>=screen.Bounds.Bottom-2;
   if(maximized||fullscreen)return PreserveLayout(group,group.members.Count==1?"":"组内有最大化或全屏窗口，已保留各窗口状态。");
   bool resizable=(style&0x40000)!=0;var limits=new MinMax{minTrack=new POINT{x=GetSystemMetrics(34),y=GetSystemMetrics(35)},maxTrack=new POINT{x=Int32.MaxValue,y=Int32.MaxValue}};IntPtr response;
   if(resizable){
    pendingStates++;bool answered;
    try{answered=await Task.Run(()=>DockBackend.Valid(member.window)&&SendMessageTimeout(handle,0x24,IntPtr.Zero,ref limits,2,80,out response)!=IntPtr.Zero);}finally{pendingStates--;ClearStoppedRecovery();}
    if(version!=epoch||request!=layoutRequest||!enabled||MonitorPaused(group.monitor))return false;
    if(!answered)return PreserveLayout(group,"窗口暂未响应尺寸查询，已保留窗口原位置。");
   }
   var cell=cells[i];int width,height;
   if(group.members.Count==1){width=Math.Min(old.Width,area.Width*maxWidth/100);height=Math.Min(old.Height,area.Height*maxHeight/100);}else{width=cell.Width;height=cell.Height;}
   if(resizable){width=Math.Max(limits.minTrack.x,Math.Min(width,limits.maxTrack.x));height=Math.Max(limits.minTrack.y,Math.Min(height,limits.maxTrack.y));}else{width=old.Width;height=old.Height;}
   if(width<1||height<1||width>cell.Width||height>cell.Height)return PreserveLayout(group,"窗口最小尺寸超过可用区域，已保留窗口原位置。");
   int x=group.members.Count==1?Math.Max(area.Left,Math.Min(old.X,area.Right-width)):cell.Left+(cell.Width-width)/2;
   int y=group.members.Count==1?Math.Max(area.Top,Math.Min(old.Y,area.Bottom-height)):cell.Top+(cell.Height-height)/2;
   if(!tool){x-=screen.WorkingArea.Left-screen.Bounds.Left;y-=screen.WorkingArea.Top-screen.Bounds.Top;}
   p.normal=new RECT{l=x,t=y,r=x+width,b=y+height};placements.Add(p);
  }
  for(int i=0;i<placements.Count;i++)group.members[i].layout=placements[i];return PreserveLayout(group,"");
 }
 internal async void Warm(Group next){
  foreach(var member in next.members)if(Picture(member)==null&&!member.thumbnailProtected){member.thumbnailAttempts=0;member.thumbnailRetry=DateTime.MinValue;}
  Group current;if(!enabled||MonitorPaused(next.monitor)||switching||!active.TryGetValue(next.monitor,out current)||current==next)return;
  int version=epoch;try{await Task.WhenAll(current.members.Take(6).Select(m=>Capture(m,version)));if(version==epoch){warmed=current;warmedAt=DateTime.UtcNow;}}catch(Exception){/* A click still uses its retained texture or the normal window operation. */}
 }
 void Fly(Group group,bool incoming,IDictionary<Member,Rectangle> origins=null){
  if(group==null||!motionEnabled)return;
  foreach(var member in group.members.Take(6)){if(StageThumbnailCapture.Protected(member.window)){ForgetPicture(member.window.handle);continue;}Texture texture=Picture(member);RECT rect;var card=Card(group,member);Rectangle origin;if(incoming&&origins!=null&&origins.TryGetValue(member,out origin))card=new CardPose{bounds=origin,angle=0,depth=400};if(card.bounds.IsEmpty||texture==null||!DockBackend.Valid(member.window))continue;
   var p=member.layout;var display=Screen.FromHandle(new IntPtr(member.window.handle));var bounds=p.show==3||p.show==2&&(p.flags&2)!=0?display.WorkingArea:PlacementBounds(p,display,(GetWindowLongPtr(new IntPtr(member.window.handle),-20).ToInt64()&0x80)!=0);
   if(!incoming&&GetWindowRect(new IntPtr(member.window.handle),out rect))bounds=Rectangle.FromLTRB(rect.l,rect.t,rect.r,rect.b);
   if(bounds.Width<1||bounds.Height<1)continue;
   var surface=new NativeWindowMotion.GenieSurface(texture,bounds,card.bounds){StageAngle=card.angle,StageDepth=card.depth};surface.RenderStage(incoming?1:0);surface.Show();flights.Add(new StageFlight{member=member,incoming=incoming,surface=surface,position=incoming?1:0,goal=incoming?0:1});
  }
 }
 async Task Animate(int version){
  if(flights.Count==0)return;var finished=new TaskCompletionSource<bool>();EventHandler frame=null;foreach(var flight in flights)flight.started=flight.updated=motionClock.Elapsed.TotalSeconds;flightsReady=true;
  frame=(s,e)=>{try{if(version!=epoch){finished.TrySetResult(false);return;}bool done=true;double now=motionClock.Elapsed.TotalSeconds;foreach(var flight in flights){done&=flight.Advance(now);flight.surface.RenderStage(flight.position);}if(done)finished.TrySetResult(true);}catch(Exception ex){finished.TrySetException(ex);}};
  Media.CompositionTarget.Rendering+=frame;try{do{await Task.WhenAny(finished.Task,Task.Delay(900));}while(!finished.Task.IsCompleted&&flights.Any(f=>motionClock.Elapsed.TotalSeconds-f.started<.9));if(finished.Task.IsCompleted)await finished.Task;}finally{Media.CompositionTarget.Rendering-=frame;}
 }
 // Desktop controls may interrupt an animation without changing real windows.
 void CancelTransition(){epoch++;queued=null;flightsReady=false;foreach(var flight in flights){if(flight.hiddenForSnapshot&&DockBackend.Valid(flight.member.window))NativeWindowMotion.RestoreOpacity(new IntPtr(flight.member.window.handle));flight.surface.Dispose();}flights.Clear();switching=false;}
  async Task<bool> Settle(){
  bool interrupted=switching;CancelTransition();int version=epoch;
  if(interrupted){var placed=await Task.WhenAll(active.Values.SelectMany(g=>g.members).Select(m=>Place(m,false)));if(version!=epoch||!enabled)return false;if(placed.Any(ok=>!ok)){Error="窗口未接受恢复布局。";return false;}}
  return true;
 }
 bool Retarget(Group group,bool focus){
  if(group.layoutPending||destination==null||group.monitor!=destination.monitor||!flights.Any(f=>group.members.Contains(f.member)))return false;
  destination=group;destinationFocus=focus;active[group.monitor]=group;queued=null;double now=motionClock.Elapsed.TotalSeconds;foreach(var flight in flights)flight.Retarget(group.members.Contains(flight.member)?0:1,now);return true;
 }
 async Task<bool> SwitchMember(Member member,bool minimize,int version){
  try{
   if(!DockBackend.Valid(member.window)||!DockBackend.CanManage(member.window))return false;
   var before=Read(member.window);var target=minimize?before:member.layout;
   if(minimize){if(target.show==3)target.flags|=2;target.show=2;}
   else if(target.show==2)target.show=(target.flags&2)!=0?3:1;
   target.flags&=~4;
   if(!before.normal.Equals(target.normal))member.geometryIntent=target;
   if(before.show!=target.show)member.visibilityIntent=target.show;
   Save();
   // Recovery runs on the same lane, with no unconfirmed posted show behind it.
   var snapshot=new Member{window=member.window,original=target};
   var applied=member.placements.Submit(()=>version==System.Threading.Volatile.Read(ref epoch)&&Restore(snapshot,true,false,false));
   if(await Task.WhenAny(applied,Task.Delay(450))!=applied)return false;
   bool confirmed=await applied;
   if(!confirmed&&snapshot.geometryIntent.length>0)member.geometryIntent=snapshot.geometryIntent;
   member.placementFailure=snapshot.placementFailure;
   return confirmed;
  }catch(InvalidOperationException ex){member.placementFailure=ex.Message;return false;}
 }
 async Task<bool> SwitchMembers(IEnumerable<Member> selected,bool minimize,int version){
  var members=selected.ToArray();var confirmed=await Task.WhenAll(members.Select(member=>SwitchMember(member,minimize,version)));
  if(version!=epoch||!enabled)return false;
  for(int i=0;i<members.Length;i++){
   if(confirmed[i])continue;
   var member=members[i];var owner=groups.First(g=>g.members.Contains(member));owner.members.Remove(member);
   foreach(var flight in flights.Where(f=>f.member==member).ToArray()){
    if(flight.hiddenForSnapshot&&DockBackend.Valid(member.window))NativeWindowMotion.RestoreOpacity(new IntPtr(member.window.handle));
    flight.surface.Dispose();flights.Remove(flight);
   }
   ForgetPicture(member.window.handle);
   if(DockBackend.Valid(member.window)){
    unresolved.Add(member);recoveryWarning="部分窗口未接受切组操作；已保留其恢复记录，其余窗口可继续使用。";
    if(member.placementFailure.Length>0)recoveryWarning+=" "+member.placementFailure;
   }
   Group current;
   if(owner.members.Count==0){groups.Remove(owner);if(active.TryGetValue(owner.monitor,out current)&&current==owner)active.Remove(owner.monitor);}
   Save();RefreshStrip(owner.monitor);
  }
  return true;
 }
 internal async void Switch(Group group,bool focus,IDictionary<Member,Rectangle> origins=null,bool animate=true){
  if(!enabled||MonitorPaused(group.monitor)||!groups.Contains(group))return;StageStrip visibleStrip;if(strips.TryGetValue(group.monitor,out visibleStrip))visibleStrip.Reveal(group,true);
  if(!animate&&switching&&!await Settle())return;bool animateTransition=animate&&motionEnabled;
  if(switching){
   // Splitting invalidates the old endpoint; apply that pending layout before returning.
   if(flightsReady&&Retarget(group,focus))return;
   queued=group;queuedFocus=focus;return;
  }
  Group previous;if(active.TryGetValue(group.monitor,out previous)&&previous==group){if(focus&&group.members.Count>0){int focusVersion=epoch;bool arrange=group.layoutPending;if(arrange)foreach(var member in group.members)if(DockBackend.Valid(member.window)&&!NativeWindowMotion.IsIconic(new IntPtr(member.window.handle)))member.layout=Read(member.window);if(!await PrepareLayout(group))return;var results=await Task.WhenAll(group.members.Where(m=>arrange||NativeWindowMotion.IsIconic(new IntPtr(m.window.handle))).Select(m=>Place(m,false)));Group selected;if(focusVersion!=epoch||!enabled||switching||MonitorPaused(group.monitor)||!active.TryGetValue(group.monitor,out selected)||selected!=group)return;if(results.Any(ok=>!ok)){Error="窗口布局暂时无法恢复。";return;}Focus(group.members[0].window);}return;}
  if(!await PrepareLayout(group))return;
  switching=true;destination=group;destinationFocus=focus;int version=epoch;cancelMotion();Group fallback=null;
  try{
   if(previous!=null){var members=previous.members.Where(m=>DockBackend.Valid(m.window)).ToArray();foreach(var member in members)member.layout=Read(member.window);if(animateTransition&&(warmed!=previous||(DateTime.UtcNow-warmedAt).TotalMilliseconds>500))await Task.WhenAll(members.Take(6).Where(m=>Picture(m)==null||(DateTime.UtcNow-m.thumbnailCaptured).TotalMilliseconds>2000).Select(m=>Capture(m,version)));if(version!=epoch||!enabled)return;}
   if(animateTransition){
   RefreshStrip(group.monitor);Fly(previous,false);Fly(group,true,origins);
   var ready=Task.WhenAll(flights.Select(f=>f.surface.Ready));await Task.WhenAny(ready,Task.Delay(500));if(version!=epoch||!enabled)return;
   // One slow/closed surface must not discard the other windows' rendered frames.
   // Failed members still use the normal native restore path below.
   foreach(var flight in flights.Where(f=>!f.surface.Ready.IsCompleted||f.surface.Ready.IsCanceled||f.surface.Ready.IsFaulted||!f.surface.Ready.Result).ToArray()){flight.surface.Dispose();flights.Remove(flight);}
   if(flights.Count>0){await NativeWindowMotion.Compose();}if(version!=epoch||!enabled)return;
   }else RefreshStrip(group.monitor);
   if(previous!=null&&!await SwitchMembers(previous.members,true,version))return;
   active[group.monitor]=group;
   var placements=new List<Member>();foreach(var member in group.members){var incoming=flights.FirstOrDefault(f=>f.incoming&&f.member==member);if(incoming!=null)incoming.hiddenForSnapshot=NativeWindowMotion.HideForSnapshot(new IntPtr(member.window.handle));if(incoming==null||incoming.hiddenForSnapshot)placements.Add(member);}
   if(!await SwitchMembers(placements,false,version))return;
   if(group.members.Count==0){fallback=previous;return;}
   // A click during capture/surface preparation belongs to this same flight when
   // it selects either endpoint; do not play a discarded transition first.
   if(queued!=null)Retarget(queued,queuedFocus);
   if(animateTransition){await Animate(version);}if(version!=epoch||!enabled)return;flightsReady=false;
   // While settling real windows, new clicks queue instead of retargeting. Include
   // members without a texture or beyond the six animated surfaces in each group.
   var retiring=(previous==null?Enumerable.Empty<Member>():previous.members).Concat(group.members).Distinct().Where(m=>!destination.members.Contains(m)).ToArray();
   if(!await SwitchMembers(retiring,true,version)||!await SwitchMembers(destination.members,false,version))return;
   if(destination.members.Count==0){fallback=destination==group?previous:group;return;}
   foreach(var flight in flights)if(flight.hiddenForSnapshot&&DockBackend.Valid(flight.member.window)){NativeWindowMotion.RestoreOpacity(new IntPtr(flight.member.window.handle));flight.hiddenForSnapshot=false;}
   if(destinationFocus){var first=destination.members.FirstOrDefault(m=>DockBackend.Valid(m.window));if(first!=null)try{await DockBackend.ActivateAsync(first.window);}catch(InvalidOperationException ex){Error=ex.Message;}}
   if(flights.Count>0){await NativeWindowMotion.Compose();}if(version!=epoch||!enabled)return;
   foreach(var flight in flights)flight.surface.Dispose();flights.Clear();foreach(var bar in strips.Values)bar.Invalidate();
  }catch(Exception ex){if(version==epoch){Error=ex.Message;Stop();}}
  finally{if(version==epoch){
   foreach(var flight in flights){if(flight.hiddenForSnapshot&&DockBackend.Valid(flight.member.window))NativeWindowMotion.RestoreOpacity(new IntPtr(flight.member.window.handle));flight.surface.Dispose();}flights.Clear();
   switching=false;flightsReady=false;var next=queued;bool nextFocus=queuedFocus;queued=null;
   if(next==null||!groups.Contains(next)){next=groups.Contains(fallback)?fallback:destination.members.Count==0?groups.FirstOrDefault(g=>g.monitor==group.monitor):null;nextFocus=focus;}
   if(next!=null)Switch(next,nextFocus);
  }}
 }
 async void Focus(DockWindow window){int version=epoch;pendingStates++;try{await DockBackend.ActivateAsync(window);if(version==epoch&&enabled)Error="";}catch(InvalidOperationException ex){if(version==epoch&&enabled)Error=ex.Message;}finally{pendingStates--;ClearStoppedRecovery();}}
 public bool Activating(DockWindow window,bool animate=true){if(!enabled||suspended)return false;var group=groups.FirstOrDefault(g=>g.members.Any(m=>m.window.handle==window.handle&&m.window.pid==window.pid));if(group==null||MonitorPaused(group.monitor))return false;StageStrip strip;if(strips.TryGetValue(group.monitor,out strip)){strip.Keyboard(true);strip.Keyboard(false);}Group current;if(!switching&&active.TryGetValue(group.monitor,out current)&&current==group&&!group.layoutPending)return false;Switch(group,true,null,animate);return true;}
 internal async void Merge(Group source,Group destination){
  if(!enabled||source==destination||source.monitor!=destination.monitor||!groups.Contains(source)||!groups.Contains(destination))return;if(!await Settle())return;cancelMotion();
  try{
   Group current;active.TryGetValue(source.monitor,out current);if(current!=null)foreach(var m in current.members)if(DockBackend.Valid(m.window))m.layout=Read(m.window);
   var origins=new Dictionary<Member,Rectangle>();if(current==source||current==destination)foreach(var m in current.members){RECT rect;if(GetWindowRect(new IntPtr(m.window.handle),out rect)&&!NativeWindowMotion.IsIconic(new IntPtr(m.window.handle)))origins[m]=Rectangle.FromLTRB(rect.l,rect.t,rect.r,rect.b);}
   foreach(var group in new[]{source,destination})if(group.members.Count==1&&!group.restoreStandalone)group.members[0].standalone=group.members[0].layout;
   var proposed=new Group{monitor=source.monitor,members=destination.members.Concat(source.members).ToList()};if(!await PrepareLayout(proposed))return;
   destination.members.AddRange(source.members);destination.layoutPending=false;destination.restoreStandalone=false;destination.layoutNotice=proposed.layoutNotice;groups.Remove(source);if(current==source||current==destination)active.Remove(source.monitor);Save();RefreshStrip(destination.monitor);Switch(destination,true,origins);
  }catch(Exception ex){Error=ex.Message;Stop();}
 }
 void RefreshStrip(string monitor){StageStrip strip;Rectangle area;if(strips.TryGetValue(monitor,out strip)&&areas.TryGetValue(monitor,out area))strip.RefreshGroups(groups.Where(g=>g.monitor==monitor).ToList(),area);}
 internal async void Split(Group source,Member member){
  if(!enabled||source.members.Count<2||!groups.Contains(source)||!source.members.Contains(member))return;if(!await Settle())return;cancelMotion();
  try{Group current;var origins=new Dictionary<Member,Rectangle>();if(active.TryGetValue(source.monitor,out current)&&current==source){foreach(var m in source.members)if(DockBackend.Valid(m.window))m.layout=Read(m.window);RECT rect;if(GetWindowRect(new IntPtr(member.window.handle),out rect)&&!NativeWindowMotion.IsIconic(new IntPtr(member.window.handle)))origins[member]=Rectangle.FromLTRB(rect.l,rect.t,rect.r,rect.b);}
   source.members.Remove(member);source.layoutPending=true;source.restoreStandalone=source.members.Count==1;source.layoutNotice="";var group=new Group{monitor=source.monitor,restoreStandalone=true};group.members.Add(member);groups.Add(group);Save();RefreshStrip(source.monitor);Switch(group,true,origins);
  }catch(Exception ex){Error=ex.Message;Stop();}
 }
 async void Stop(){
  CancelTransition();cancelMotion();thumbnails.Cancel();
  var members=groups.SelectMany(g=>g.members).ToArray();
  foreach(var bar in strips.Values)bar.Dispose();strips.Clear();groups.Clear();active.Clear();pictures.Clear();pictureOwners.Clear();pictureOrder.Clear();enabled=false;saved="";
  if(members.Length==0)return;unresolved.AddRange(members);stopping=true;
  try{await Task.WhenAll(members.Select(RecoverMember));stoppedRestored=unresolved.Count==0;ClearStoppedRecovery();}
  catch(Exception ex){stoppedRestored=false;Error=ex.Message;}finally{stopping=false;}
 }
 internal async Task DrainClose(){Dispose();var start=Stopwatch.StartNew();while((pendingStates>0||stopping||recovering||unresolved.Any(m=>m.placements.Busy))&&start.ElapsedMilliseconds<1200)await Task.Delay(10);}
 public void Dispose(){if(disposed)return;disposed=true;Stop();thumbnails.Dispose();}

}
