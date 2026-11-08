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
 internal sealed class Member {public DockWindow window;public Placement original,layout;}
 internal sealed class Group {public string id=Guid.NewGuid().ToString("N"),monitor;public List<Member> members=new List<Member>();public string Name{get{return members.Count==0?"窗口组":members[0].window.title;}}}
 internal struct CardPose {public Rectangle bounds;public double angle,depth;}
 [DllImport("user32.dll")]static extern bool GetWindowPlacement(IntPtr window,ref Placement placement);
 [DllImport("user32.dll")]static extern bool SetWindowPlacement(IntPtr window,ref Placement placement);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out RECT value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 readonly string journal;readonly Action cancelMotion;readonly Func<List<DockWindow>> readWindows;
 readonly List<Group> groups=new List<Group>();readonly Dictionary<string,Group> active=new Dictionary<string,Group>();readonly Dictionary<string,StageStrip> strips=new Dictionary<string,StageStrip>();
 Dictionary<string,Rectangle> areas=new Dictionary<string,Rectangle>();HashSet<string> fullscreen=new HashSet<string>();bool enabled,switching,disposed,suspended;DateTime scan;string saved="";
 readonly Dictionary<long,Texture> pictures=new Dictionary<long,Texture>();readonly Queue<long> pictureOrder=new Queue<long>();readonly Dictionary<long,Task> capturing=new Dictionary<long,Task>();
 readonly List<StageFlight> flights=new List<StageFlight>();readonly Stopwatch motionClock=Stopwatch.StartNew();int epoch;bool motionEnabled,flightsReady;Group queued,destination;bool queuedFocus,destinationFocus;
 Group warmed;DateTime warmedAt;
 internal IEnumerable<StageStrip> Strips {get{return strips.Values;}}
 internal bool Busy {get{return switching||capturing.Count>0;}}
 internal bool Animating {get{return flightsReady;}}
 sealed class StageFlight {
  public Member member;public bool incoming;public NativeWindowMotion.GenieSurface surface;public double position,velocity,goal,started,updated;
  public void Retarget(double target,double now){goal=target;incoming=goal==0;started=updated=now;}
  public bool Advance(double now){
   // The same analytical critical damping used by the component panels retains
   // momentum when a click changes direction between compositor frames.
   double dt=Math.Max(0,now-updated),omega=22,y=position-goal,j=velocity+omega*y,e=Math.Exp(-omega*dt);updated=now;
   position=goal+(y+j*dt)*e;velocity=(velocity-omega*j*dt)*e;
   if(Math.Abs(position-goal)<.001&&Math.Abs(velocity)<.02){position=goal;velocity=0;return true;}return false;
  }
 }
 internal string Error {get;private set;}
 internal IList<Group> Groups {get{return groups.AsReadOnly();}}
 public StageManager(string root,Action cancel):this(root,cancel,DockBackend.ReadWindows){}
 internal StageManager(string root,Action cancel,Func<List<DockWindow>> windows){journal=Path.Combine(root,"stage-recovery.json");cancelMotion=cancel;readWindows=windows;Error="";Recover();}
 internal static Placement Read(DockWindow window){var p=new Placement{length=Marshal.SizeOf(typeof(Placement))};if(!DockBackend.Valid(window)||!GetWindowPlacement(new IntPtr(window.handle),ref p))throw new InvalidOperationException("无法读取窗口布局。");return p;}
 static bool Restore(Member member,bool original){
  if(!DockBackend.Valid(member.window)||!DockBackend.CanManage(member.window))return true;
  var p=original?member.original:member.layout;var h=new IntPtr(member.window.handle);int before;if(DwmGetWindowAttribute(h,3,out before,4)!=0)before=0;int disable=1;DwmSetWindowAttribute(h,3,ref disable,4);
  if(!original&&p.show==2)p.show=(p.flags&2)!=0?3:1;
  try{p.length=Marshal.SizeOf(typeof(Placement));return SetWindowPlacement(h,ref p);}finally{if(DockBackend.Valid(member.window))DwmSetWindowAttribute(h,3,ref before,4);}
 }
 void Save(){string value=new JavaScriptSerializer().Serialize(groups.SelectMany(g=>g.members).ToList());if(value==saved)return;DesktopSettingsFiles.Write(journal,value);saved=value;}
 void Recover(){
  try{if(!File.Exists(journal))return;var members=new JavaScriptSerializer().Deserialize<List<Member>>(DesktopSettingsFiles.Read(journal));if(members==null||members.Count>512)throw new IOException("恢复记录无效。");bool ok=true;foreach(var m in members)if(m!=null&&m.window!=null)ok&=Restore(m,true);if(ok)File.Delete(journal);else Error="部分窗口未接受恢复，请从 Windows 任务栏还原。";}
  catch(Exception ex){Error="台前调度恢复失败："+ex.Message;}
 }
 bool MonitorPaused(string monitor){return suspended||fullscreen.Contains(monitor);}
 public void Configure(bool selected,bool paused,bool motion,IDictionary<string,Rectangle> monitors,IEnumerable<string> fullscreenMonitors=null){
  if(disposed)return;var hidden=new HashSet<string>(fullscreenMonitors??Enumerable.Empty<string>());var resumed=fullscreen.Where(m=>!hidden.Contains(m)).ToArray();
  if(paused&&!suspended||hidden.Any(m=>!fullscreen.Contains(m))&&switching)CancelTransition();suspended=paused;fullscreen=hidden;motionEnabled=motion;areas=new Dictionary<string,Rectangle>(monitors);
  if(!selected){if(enabled)Stop();if(!File.Exists(journal))Error="";return;}
  if(paused){foreach(var strip in strips.Values)strip.SetPaused(true);return;}
  if(!enabled){if(Error.Length>0)return;enabled=true;scan=DateTime.MinValue;}
  try{
   if(DateTime.UtcNow-scan>TimeSpan.FromMilliseconds(800)){
    scan=DateTime.UtcNow;Refresh();
   }
   foreach(var pair in strips){pair.Value.Motion=motion;pair.Value.SetPaused(MonitorPaused(pair.Key));if(areas.ContainsKey(pair.Key))pair.Value.RefreshGroups(groups.Where(g=>g.monitor==pair.Key).ToList(),areas[pair.Key]);}
   if(!paused&&!switching){var foreground=GetForegroundWindow().ToInt64();var group=groups.FirstOrDefault(g=>g.members.Any(m=>m.window.handle==foreground));Group current;if(group!=null&&(!active.TryGetValue(group.monitor,out current)||current!=group))Switch(group,false);}
   foreach(var monitor in resumed)if(areas.ContainsKey(monitor)&&active.ContainsKey(monitor))RetireBackground(monitor,epoch);
  }catch(Exception ex){Error=ex.Message;Stop();}
 }
 void Refresh(){
  foreach(var group in groups.ToArray()){
   if(!areas.ContainsKey(group.monitor)){foreach(var m in group.members)if(!Restore(m,true))throw new IOException("显示器停用时未能恢复窗口。");groups.Remove(group);active.Remove(group.monitor);continue;}
   group.members.RemoveAll(m=>!DockBackend.Valid(m.window));if(group.members.Count==0){groups.Remove(group);Group current;if(active.TryGetValue(group.monitor,out current)&&current==group)active.Remove(group.monitor);}
  }
  var known=new HashSet<long>(groups.SelectMany(g=>g.members).Select(m=>m.window.handle));
  foreach(var w in readWindows()){
   string monitor=Screen.FromHandle(new IntPtr(w.handle)).DeviceName;
   if(known.Contains(w.handle)||!areas.ContainsKey(monitor)||w.pid==(uint)Process.GetCurrentProcess().Id||!DockBackend.CanManage(w))continue;
   var placement=Read(w);var group=new Group{monitor=monitor};group.members.Add(new Member{window=w,original=placement,layout=placement});groups.Add(group);
  }
  Save();
  var handles=new HashSet<long>(groups.SelectMany(g=>g.members).Select(m=>m.window.handle));foreach(var handle in pictures.Keys.Where(h=>!handles.Contains(h)).ToArray())pictures.Remove(handle);
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
 async Task CaptureCore(Member member,int version){
  // shortcut: Minimized windows retain their last texture; live minimized content requires a WGC capture path.
  RECT rect;if(!DockBackend.Valid(member.window)||NativeWindowMotion.IsIconic(new IntPtr(member.window.handle))||!GetWindowRect(new IntPtr(member.window.handle),out rect))return;
  using(var bitmap=await NativeWindowMotion.Snapshot(member.window,Rectangle.FromLTRB(rect.l,rect.t,rect.r,rect.b))){
   if(bitmap==null||version!=epoch||!enabled||!DockBackend.Valid(member.window))return;
   RECT visible;var crop=new Rectangle(0,0,bitmap.Width,bitmap.Height);
   if(DwmGetWindowAttribute(new IntPtr(member.window.handle),9,out visible,16)==0&&visible.l>=rect.l&&visible.t>=rect.t&&visible.r<=rect.r&&visible.b<=rect.b&&visible.r>visible.l&&visible.b>visible.t)crop=new Rectangle(visible.l-rect.l,visible.t-rect.t,visible.r-visible.l,visible.b-visible.t);
   double scale=Math.Min(1,1600.0/Math.Max(crop.Width,crop.Height));using(var thumbnail=new Bitmap(Math.Max(1,(int)(crop.Width*scale)),Math.Max(1,(int)(crop.Height*scale)))){using(var graphics=Graphics.FromImage(thumbnail))graphics.DrawImage(bitmap,new Rectangle(Point.Empty,thumbnail.Size),crop,GraphicsUnit.Pixel);pictures[member.window.handle]=NativeWindowMotion.GenieSurface.Texture(thumbnail);}
   pictureOrder.Enqueue(member.window.handle);while(pictureOrder.Count>16){long old=pictureOrder.Dequeue();if(!pictureOrder.Contains(old))pictures.Remove(old);}
  }
 }
 async void RetireBackground(string monitor,int version){
  if(MonitorPaused(monitor))return;
  try{foreach(var group in groups.Where(g=>g.monitor==monitor).ToArray())foreach(var member in group.members.ToArray()){
   await Capture(member,version);if(version!=epoch||!enabled||MonitorPaused(monitor))return;
   Group current;if(!switching&&active.TryGetValue(monitor,out current)&&current!=group&&!NativeWindowMotion.SetState(member.window,true))throw new IOException("窗口未接受台前调度，请关闭此选项后重试。");
  }foreach(var bar in strips.Values)bar.Invalidate();}catch(Exception ex){if(version==epoch){Error=ex.Message;Stop();}}
 }
 CardPose Card(Group group,Member member){StageStrip strip;return strips.TryGetValue(group.monitor,out strip)?strip.Pose(group,member):new CardPose();}
 internal async void Warm(Group next){
  Group current;if(!enabled||MonitorPaused(next.monitor)||switching||!active.TryGetValue(next.monitor,out current)||current==next)return;
  int version=epoch;try{await Task.WhenAll(current.members.Select(m=>Capture(m,version)));if(version==epoch){warmed=current;warmedAt=DateTime.UtcNow;}}catch(Exception){/* A click still uses its retained texture or the normal window operation. */}
 }
 void Fly(Group group,bool incoming){
  if(group==null||!motionEnabled)return;
  foreach(var member in group.members.Take(6)){Texture texture;RECT rect;var card=Card(group,member);if(card.bounds.IsEmpty||!pictures.TryGetValue(member.window.handle,out texture)||!DockBackend.Valid(member.window))continue;
   var p=member.layout;var bounds=p.show==3||p.show==2&&(p.flags&2)!=0?Screen.FromHandle(new IntPtr(member.window.handle)).WorkingArea:Rectangle.FromLTRB(p.normal.l,p.normal.t,p.normal.r,p.normal.b);
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
 void CancelTransition(){epoch++;queued=null;flightsReady=false;foreach(var flight in flights)flight.surface.Dispose();flights.Clear();switching=false;}
 void Settle(){CancelTransition();foreach(var group in active.Values)foreach(var member in group.members)Restore(member,false);}
 internal async void Switch(Group group,bool focus){
  if(!enabled||MonitorPaused(group.monitor)||!groups.Contains(group))return;StageStrip visibleStrip;if(strips.TryGetValue(group.monitor,out visibleStrip))visibleStrip.Reveal(group,true);
  if(switching){
   if(flightsReady&&destination!=null&&group.monitor==destination.monitor&&flights.Any(f=>group.members.Contains(f.member))){foreach(var member in destination.members.Where(m=>!group.members.Contains(m)))if(!NativeWindowMotion.SetState(member.window,true)){Error="窗口暂时无法收起。";Stop();return;}destination=group;destinationFocus=focus;active[group.monitor]=group;queued=null;double now=motionClock.Elapsed.TotalSeconds;foreach(var flight in flights)flight.Retarget(group.members.Contains(flight.member)?0:1,now);return;}
   queued=group;queuedFocus=focus;return;
  }
  Group previous;if(active.TryGetValue(group.monitor,out previous)&&previous==group){if(focus&&group.members.Count>0){foreach(var member in group.members)if(NativeWindowMotion.IsIconic(new IntPtr(member.window.handle))&&!Restore(member,false)){Error="窗口布局暂时无法恢复。";return;}Focus(group.members[0].window);}return;}
  switching=true;destination=group;destinationFocus=focus;int version=epoch;cancelMotion();
  try{
   if(previous!=null){var members=previous.members.Where(m=>DockBackend.Valid(m.window)).ToArray();foreach(var member in members)member.layout=Read(member.window);if(warmed!=previous||(DateTime.UtcNow-warmedAt).TotalMilliseconds>500)await Task.WhenAll(members.Select(m=>Capture(m,version)));if(version!=epoch||!enabled)return;}
   Fly(previous,false);Fly(group,true);
   var ready=Task.WhenAll(flights.Select(f=>f.surface.Ready));await Task.WhenAny(ready,Task.Delay(500));if(version!=epoch||!enabled)return;
   if(!ready.IsCompleted||!ready.Result.All(value=>value)){foreach(var flight in flights)flight.surface.Dispose();flights.Clear();}
   if(previous!=null)foreach(var member in previous.members)if(!NativeWindowMotion.SetState(member.window,true))throw new IOException("窗口暂时无法收起，已恢复台前调度。");
   active[group.monitor]=group;
   foreach(var member in group.members)if(!flights.Any(f=>f.incoming&&f.member==member)&&!Restore(member,false))throw new IOException("窗口布局暂时无法恢复。");
   await Animate(version);if(version!=epoch||!enabled)return;flightsReady=false;
   foreach(var member in destination.members)if(!Restore(member,false))throw new IOException("窗口布局暂时无法恢复。");
   if(destinationFocus){var first=destination.members.FirstOrDefault(m=>DockBackend.Valid(m.window));if(first!=null)Focus(first.window);}
   await Task.Delay(45);if(version!=epoch||!enabled)return;
   foreach(var flight in flights)flight.surface.Dispose();flights.Clear();foreach(var bar in strips.Values)bar.Invalidate();
  }catch(Exception ex){if(version==epoch){Error=ex.Message;Stop();}}
  finally{if(version==epoch){switching=false;flightsReady=false;var next=queued;bool nextFocus=queuedFocus;queued=null;if(next!=null)Switch(next,nextFocus);}}
 }
 void Focus(DockWindow window){try{DockBackend.ActivateImmediate(window);Error="";}catch(InvalidOperationException ex){Error=ex.Message;}}
 public bool Activating(DockWindow window){if(!enabled||suspended)return false;var group=groups.FirstOrDefault(g=>g.members.Any(m=>m.window.handle==window.handle&&m.window.pid==window.pid));if(group==null||MonitorPaused(group.monitor))return false;StageStrip strip;if(strips.TryGetValue(group.monitor,out strip)){strip.Keyboard(true);strip.Keyboard(false);}Group current;if(active.TryGetValue(group.monitor,out current)&&current==group)return false;Switch(group,true);return true;}
 internal void Merge(Group source,Group destination){if(!enabled||source==destination||source.monitor!=destination.monitor||!groups.Contains(source)||!groups.Contains(destination))return;Settle();cancelMotion();try{Group current;if(active.TryGetValue(source.monitor,out current)){foreach(var m in current.members)if(DockBackend.Valid(m.window))m.layout=Read(m.window);if(current!=source&&current!=destination)foreach(var m in current.members)if(!NativeWindowMotion.SetState(m.window,true))throw new IOException("当前窗口未能收起。");}destination.members.AddRange(source.members);groups.Remove(source);active.Remove(source.monitor);Save();Switch(destination,true);if(enabled)Refresh();}catch(Exception ex){Error=ex.Message;Stop();}}
 internal void Split(Group source,Member member){if(!enabled||source.members.Count<2||!groups.Contains(source)||!source.members.Contains(member))return;Settle();cancelMotion();try{Group current;if(active.TryGetValue(source.monitor,out current)&&current==source)member.layout=Read(member.window);source.members.Remove(member);var group=new Group{monitor=source.monitor};group.members.Add(member);groups.Add(group);Save();Switch(group,true);if(enabled)Refresh();}catch(Exception ex){Error=ex.Message;Stop();}}
 void Stop(){CancelTransition();cancelMotion();bool restored=true,managed=groups.Any(g=>g.members.Count>0);foreach(var group in groups)foreach(var member in group.members)restored&=Restore(member,true);foreach(var bar in strips.Values)bar.Dispose();strips.Clear();groups.Clear();active.Clear();pictures.Clear();enabled=false;saved="";if(restored&&managed){try{if(File.Exists(journal))File.Delete(journal);}catch(IOException){}}else if(!restored)Error="部分窗口未能恢复；可从 Windows 任务栏打开，恢复记录已保留。";}
 public void Dispose(){if(disposed)return;Stop();disposed=true;}

}
