using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Reuse the installed data-source skins; removing a card keeps its data source alive.
internal sealed class DesktopWidgets : IDisposable {
 internal static readonly string[] Names={"Weather","System","Clock","Calendar","Battery","Folders","Music","Focus","Tools","BreathingOrb","DigitalClock","Moon","Photo","Photo2","Photo3","Photo4","Photo5","Photo6","Hourglass","SunPath","YearDots","Tide","Orrery","Bonsai"};
 readonly string skinRoot;readonly Dictionary<string,DateTime> retries=new Dictionary<string,DateTime>();
 readonly HashSet<IntPtr> styled=new HashSet<IntPtr>();readonly HashSet<string> shown=new HashSet<string>();
 readonly Dictionary<IntPtr,bool> ambientPaused=new Dictionary<IntPtr,bool>();
 readonly Dictionary<IntPtr,Point> pendingMoves=new Dictionary<IntPtr,Point>();DateTime moveDeadline;
 readonly WidgetPositions positions;string dragged="";Rectangle dragOrigin;
 bool dragging,catalogRefreshed,layoutLocked,ignoreDragUntilRelease;
 internal void LockLayout(bool value){if(layoutLocked==value)return;layoutLocked=value;dragging=false;dragged="";styled.Clear();}
 internal void DisplaysChanged(){if(Dispatch!=null)Dispatch.Reset();dragging=false;dragged="";ignoreDragUntilRelease=GetAsyncKeyState(1)<0;pendingMoves.Clear();styled.Clear();ambientPaused.Clear();}
 [StructLayout(LayoutKind.Sequential)]struct Rect {public int left,top,right,bottom;}
 [StructLayout(LayoutKind.Sequential)]struct MonitorInfo {public int size;public Rect monitor,work;public uint flags;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string title);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect bounds);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(Point point,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
 [DllImport("user32.dll")]static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 internal RainmeterDispatch Dispatch;
 bool Deliver(string slot,IntPtr window,string command){
  Func<bool> send=()=>MonitorDock.SendSkinCommand(window,command);
  return Dispatch==null?send():Dispatch.Send(slot,window+command,send);
 }
 internal DesktopWidgets(string root){positions=new WidgetPositions(root);var path=Path.Combine(root,"skin-path.txt");skinRoot=Path.GetFullPath(File.Exists(path)?File.ReadAllText(path).Trim():Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Rainmeter","Skins","PrismGlass"));}
 IntPtr Window(string name){return FindWindow("RainmeterMeterWindow",Path.Combine(skinRoot,name,"Main.ini"));}
 static Rectangle Bounds(IntPtr window){Rect r;return window!=IntPtr.Zero&&GetWindowRect(window,out r)?Rectangle.FromLTRB(r.left,r.top,r.right,r.bottom):Rectangle.Empty;}
 static bool VisibleByPolicy(string name,Rectangle bounds,ICollection<string> autoHide,Func<Rectangle,bool> covered,Func<Rectangle,bool> screenEnabled){
  return bounds.IsEmpty||(screenEnabled==null||screenEnabled(bounds))&&!(covered!=null&&autoHide!=null&&autoHide.Contains(name)&&covered(bounds));
 }
 internal bool StartupReady(ICollection<string> selected,bool enabled,ICollection<string> autoHide,Func<Rectangle,bool> covered,Func<Rectangle,bool> screenEnabled){
  if(!enabled||selected.Count==0)return true;
  foreach(var name in Names.Where(selected.Contains)){
   var window=Window(name);var bounds=Bounds(window);
   if(!VisibleByPolicy(name,bounds,autoHide,covered,screenEnabled))continue;
   if(!MonitorDock.SkinCatalogReady||bounds.Width<1||bounds.Height<1||!shown.Contains(name)||!styled.Contains(window)||!IsWindowVisible(window))return false;
  }
  return true;
 }
 internal bool Owns(string path){foreach(var name in shown)if(String.Equals(Path.Combine(skinRoot,name,"Main.ini"),path,StringComparison.OrdinalIgnoreCase))return true;return false;}
 static Dictionary<string,Rectangle> Screens(){return MonitorCatalog.Read().ToDictionary(m=>m.Id,m=>m.Screen.WorkingArea,StringComparer.OrdinalIgnoreCase);}
 internal static Point MapPosition(Point at,Rectangle source,Rectangle target){return new Point(target.Left+(int)Math.Round((at.X-source.Left)*target.Width/(double)source.Width,MidpointRounding.AwayFromZero),target.Top+(int)Math.Round((at.Y-source.Top)*target.Height/(double)source.Height,MidpointRounding.AwayFromZero));}
 static bool Positioned(Point actual,Point expected){return Math.Abs(actual.X-expected.X)<=1&&Math.Abs(actual.Y-expected.Y)<=1;}
 static Point RainmeterPosition(IntPtr window,Rectangle bounds,out Point expected){
  var monitor=MonitorFromPoint(new Point(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2),2);var physical=new MonitorInfo{size=Marshal.SizeOf(typeof(MonitorInfo))};var logical=physical;
  if(!GetMonitorInfo(monitor,ref physical))throw new IOException("无法读取小组件目标屏幕。");
  var previous=SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(window));if(previous==IntPtr.Zero)throw new IOException("无法读取小组件的缩放比例。");
  try{if(!GetMonitorInfo(monitor,ref logical))throw new IOException("无法读取小组件目标屏幕的缩放。");}finally{SetThreadDpiAwarenessContext(previous);}
  var from=Rectangle.FromLTRB(physical.monitor.left,physical.monitor.top,physical.monitor.right,physical.monitor.bottom);var to=Rectangle.FromLTRB(logical.monitor.left,logical.monitor.top,logical.monitor.right,logical.monitor.bottom);
  if(from.Width<1||from.Height<1||to.Width<1||to.Height<1)throw new IOException("小组件目标屏幕大小无效。");var at=MapPosition(bounds.Location,from,to);expected=MapPosition(at,to,from);return at;
 }
 internal bool Dragging {get{
  // Keep the glass geometry polling until the other UI thread applies an automatic move.
  if(pendingMoves.Count>0){if(DateTime.UtcNow>=moveDeadline)pendingMoves.Clear();else foreach(var window in pendingMoves.Keys.ToArray()){Rect r;if(!GetWindowRect(window,out r)||Positioned(new Point(r.left,r.top),pendingMoves[window]))pendingMoves.Remove(window);}if(pendingMoves.Count>0)return true;}
  if(ignoreDragUntilRelease){if(GetAsyncKeyState(1)>=0)ignoreDragUntilRelease=false;return dragging=false;}
  if(layoutLocked){dragged="";return dragging=false;}
  if(GetAsyncKeyState(1)>=0){if(dragging&&dragged.Length>0){Rect r;if(GetWindowRect(Window(dragged),out r)){var bounds=Rectangle.FromLTRB(r.left,r.top,r.right,r.bottom);if(bounds!=dragOrigin)positions.Remember(dragged,bounds,Screens());}}dragged="";return dragging=false;}
  if(dragging)return true;var at=Cursor.Position;var hit=WindowFromPoint(at);foreach(var name in shown){Rect r;var window=Window(name);if(window==hit&&GetWindowRect(window,out r)&&at.X>=r.left&&at.X<r.right&&at.Y>=r.top&&at.Y<r.bottom){dragged=name;dragOrigin=Rectangle.FromLTRB(r.left,r.top,r.right,r.bottom);return dragging=true;}}return false;
 }}
 internal static bool Animated(string name){return name=="Clock"||name=="BreathingOrb"||name=="Hourglass"||name=="SunPath"||name=="YearDots"||name=="Tide"||name=="Orrery"||name=="Bonsai";}
 internal static string Tooltip(string name){
  switch(name){case "BreathingOrb":return "点击切换配色 · 拖动可移动";case "DigitalClock":return "本机时间 · 拖动可移动";case "Moon":return "本地月相估算 · 北向朝上";case "Hourglass":return "选择 5 / 15 / 25 / 50 分钟 · 开始 / 暂停 · 重置";case "SunPath":return "真实天气与日光 · 点击天气详情";case "YearDots":return "当前月份 · 打开倒数日或日历";case "Tide":return "一分钟呼吸休息 · 开始 / 暂停 / 重置";case "Orrery":return "电脑专注计时 · 开始 / 暂停 / 重置";case "Bonsai":return "当前待办与完成进度 · 打开待办";default:return name.StartsWith("Photo",StringComparison.Ordinal)?"双击打开照片设置 · 拖动可移动":"";}
 }
 internal string Hover(Point point,out Rectangle bounds,out string caption){
  bounds=Rectangle.Empty;caption="";if(GetAsyncKeyState(1)<0||dragging)return "";var hit=WindowFromPoint(point);
  foreach(var name in shown){string text=Tooltip(name);var window=Window(name);Rect r;if(text.Length==0||window!=hit||!IsWindowVisible(window)||!GetWindowRect(window,out r))continue;bounds=Rectangle.FromLTRB(r.left,r.top,r.right,r.bottom);if(!bounds.Contains(point))continue;caption=layoutLocked?text.Replace(" · 拖动可移动",""):text;return name;}return "";
 }
 internal static Rectangle Place(Rectangle card,Rectangle area,ICollection<Rectangle> rails){
  if(card.Width>area.Width||card.Height>area.Height)return Rectangle.Empty;
  var xs=new List<int>{card.X};var ys=new List<int>{card.Y};
  foreach(var rail in rails){xs.Add(rail.Left-card.Width-8);xs.Add(rail.Right+8);ys.Add(rail.Top-card.Height-8);ys.Add(rail.Bottom+8);}
  Rectangle best=Rectangle.Empty;double distance=Double.MaxValue;
  foreach(int x in xs)foreach(int y in ys){var candidate=new Rectangle(Math.Max(area.Left,Math.Min(x,area.Right-card.Width)),Math.Max(area.Top,Math.Min(y,area.Bottom-card.Height)),card.Width,card.Height);bool overlaps=false;foreach(var rail in rails)if(candidate.IntersectsWith(Rectangle.Inflate(rail,8,8))){overlaps=true;break;}if(overlaps)continue;double dx=(double)candidate.X-card.X,dy=(double)candidate.Y-card.Y,d=dx*dx+dy*dy;if(d<distance){best=candidate;distance=d;}}
  return best;
 }
 internal void Arrange(ICollection<Rectangle> rails){
  if(dragging)return;var screens=Screens();var obstacles=new List<Rectangle>(rails);
  foreach(var name in Names.Where(shown.Contains)){var window=Window(name);Rect r;if(!GetWindowRect(window,out r))continue;var bounds=Rectangle.FromLTRB(r.left,r.top,r.right,r.bottom);bool temporary;var next=positions.Restore(name,bounds,screens,out temporary);next=Place(next,screens[WidgetPositions.Nearest(next,screens)],obstacles);string config="\"PrismGlass\\"+name+"\"";
   if(next.IsEmpty){Deliver(name+"-position",window,"!Hide "+config);throw new IOException("屏幕没有足够空间放置桌面卡片，请先隐藏侧栏或移到更大的显示器。");}
   // Bangs update Rainmeter's redraw coordinates; native positioning alone is later overwritten.
   if(next!=bounds){Point expected;var at=RainmeterPosition(window,next,out expected);next.Location=expected;if(!Positioned(bounds.Location,expected)){if(!Deliver(name+"-position",window,"!Move "+at.X+" "+at.Y+" "+config))throw new IOException("桌面卡片未能避开侧栏，请重试。");pendingMoves[window]=expected;moveDeadline=DateTime.UtcNow.AddMilliseconds(250);}else next.Location=bounds.Location;}
   obstacles.Add(next);
  }
 }
 bool QueueMissing(string name,bool preload=false){
  DateTime retry;if(retries.TryGetValue(name,out retry)&&DateTime.UtcNow<retry)return false;
  if(!File.Exists(Path.Combine(skinRoot,name,"Main.ini"))){
   if(preload)return false;
   throw new IOException("桌面小组件 "+name+" 的皮肤尚未安装，请重新安装 PrismGlass。");
  }
  // Newly installed directories may not be in the running Rainmeter catalog yet.
  if(retries.ContainsKey(name)&&!catalogRefreshed){MonitorDock.RefreshSkinCatalog(Dispatch);catalogRefreshed=true;styled.Clear();ambientPaused.Clear();return true;}

  string command="!ActivateConfig \"PrismGlass\\"+name+"\" \"Main.ini\"";
  Func<bool> activate=()=>{RainmeterCommands.Send(RuntimePaths.Rainmeter,command,50);return true;};
  try{if(Dispatch==null)activate();else Dispatch.Send(name+"-activate",command,activate);}
  catch(RainmeterDispatch.Pending){throw;}
  catch{retries[name]=DateTime.UtcNow.AddSeconds(3);throw;}
  retries[name]=DateTime.UtcNow.AddSeconds(3);
  return false;
 }
 internal void Preload(ICollection<string> selected){
  if(Dispatch==null||!MonitorDock.SkinCatalogReady)return;
  // The selected skins initialize hidden. Sync still decides whether and where to show them.
  foreach(var name in Names.Where(selected.Contains)){
   if(Window(name)!=IntPtr.Zero)continue;
   try{if(QueueMissing(name,true))return;}catch(RainmeterDispatch.Pending){}
  }
 }
 internal bool Sync(ICollection<string> selected,bool enabled,ICollection<string> autoHide=null,Func<Rectangle,bool> covered=null,Func<Rectangle,bool> screenEnabled=null,Func<Rectangle,bool> motionPaused=null){
  if(!MonitorDock.SkinCatalogReady)return false;
  bool changed=false;
  foreach(var name in Names)try{
   var window=Window(name);var area=Bounds(window);string config="\"PrismGlass\\"+name+"\"";
   bool visible=enabled&&selected.Contains(name)&&VisibleByPolicy(name,area,autoHide,covered,screenEnabled);
   if(Dispatch!=null&&(!visible||window!=IntPtr.Zero))Dispatch.Cancel(name+"-activate");
   if(window==IntPtr.Zero){
    changed|=shown.Remove(name);
    if(visible&&QueueMissing(name))return changed;
    continue;
   }
   bool nativeVisible=IsWindowVisible(window);
   if(!visible)styled.Remove(window);
   else if(!nativeVisible&&shown.Remove(name)){styled.Remove(window);changed=true;}
   bool restyle=visible&&!styled.Contains(window);
   if(restyle){
    string command="[!ZPos -2 "+config+"][!Draggable "+(layoutLocked?"0":"1")+" "+config+"][!ClickThrough 0 "+config+"][!KeepOnScreen 1 "+config+"][!SetVariable Ink 245,248,255 "+config+"][!SetVariable Muted 218,230,245 "+config+"][!UpdateMeter * "+config+"][!Redraw "+config+"]";
    if(Tooltip(name).Length>0)command="[!SetOption "+(name=="BreathingOrb"?"OrbInput":"Input")+" ToolTipHidden 1 "+config+"]"+command;
    if(!Deliver(name+"-style",window,command))throw new IOException("桌面小组件未响应，请稍后重试。");styled.Add(window);ambientPaused.Remove(window);
   }
   if(Animated(name)){
    bool stop=!visible||(motionPaused!=null&&motionPaused(area)),previous;
    if(!ambientPaused.TryGetValue(window,out previous)||previous!=stop){
     if(!Deliver(name+"-motion",window,"[!SetVariable AmbientPaused "+(stop?"1":"0")+" "+config+"][!CommandMeasure Ambient \"MotionPolicy()\" "+config+"]"))throw new IOException("装饰小组件未能同步动效设置。");ambientPaused[window]=stop;
    }
   }
   if(nativeVisible==visible){if(Dispatch!=null)Dispatch.Cancel(name+"-visible");}
   else if(!Deliver(name+"-visible",window,(visible?"!Show ":"!Hide ")+config))throw new IOException("桌面小组件显示状态未能更新。");
   if(visible){if(IsWindowVisible(window))changed|=shown.Add(name);}
   else changed|=shown.Remove(name);
  }catch(RainmeterDispatch.Pending){}
  return changed;
 }
 public void Dispose(){if(Dispatch!=null)Dispatch.Reset();foreach(var name in shown){var window=Window(name);string config="\"PrismGlass\\"+name+"\"";if(Animated(name))MonitorDock.SendSkinCommand(window,"[!SetVariable AmbientPaused 1 "+config+"][!CommandMeasure Ambient \"MotionPolicy()\" "+config+"]");MonitorDock.SendSkinCommand(window,"!Hide "+config);}shown.Clear();pendingMoves.Clear();styled.Clear();ambientPaused.Clear();}
}
