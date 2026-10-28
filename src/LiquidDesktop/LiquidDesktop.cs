using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class GlassCard {public string name;public int x,y,w,h,r,insetY;}
internal static class DesktopStartupReadiness {
 public static bool DisplayReady(bool enabled,bool painted,bool left,bool leftReady,bool top,bool topReady,bool right,bool rightReady,bool dock,bool dockReady){return !enabled||(painted&&(!left||leftReady)&&(!top||topReady)&&(!right||rightReady)&&(!dock||dockReady));}
}
internal sealed class LiquidDesktop : Form {
 readonly string root=AppDomain.CurrentDomain.BaseDirectory;readonly uint ownPid=(uint)Process.GetCurrentProcess().Id;
 readonly WebView2 view=new WebView2();
 readonly WebViewPixelScale pixels;
 readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 DateTime probe=DateTime.MinValue,beat=DateTime.MinValue,wallpaperTime=DateTime.MinValue;
 bool ready,painted,closing,busy,lastDown; string layoutKey="",theme="Sky"; bool paused;
 List<DesktopWindowState> desktopWindows=new List<DesktopWindowState>();
 DateTime windowProbe=DateTime.MinValue;bool screenCovered,manualMotionPause;string componentVisibility="";
 readonly CursorTrail cursorTrail;
 readonly TaskbarVisibility taskbarVisibility=new TaskbarVisibility();
 readonly DockLyrics lyrics;
 readonly LiquidDesktop coordinator;readonly bool primaryHost;readonly string instance;MonitorRecord monitor;
 readonly DesktopPreferences preferences;readonly SharedDesktopData sharedData;readonly MonitorDock monitorDock;
 readonly Dictionary<string,LiquidDesktop> displays=new Dictionary<string,LiquidDesktop>(StringComparer.OrdinalIgnoreCase);
 readonly HashSet<string> failedDisplays=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 CompanionHost companion;AppLibrary library;WorkspaceWindow workspace;RuntimePolicy runtime;HotkeyBinding hotkeys;NotifyIcon tray;long preferenceRevision;string hotkeyError="",monitorError="",runtimeKey="",motionKey="";bool reconciling,tearingDown,draggingDock,legacyManagerOpen;DateTime settingsRead=DateTime.MinValue,displaysRead=DateTime.MinValue;string[] pauseReasons=new string[0];
 bool systemAnimations=true,welcomePending;
 [DllImport("user32.dll")]static extern bool SystemParametersInfo(uint action,uint parameter,[MarshalAs(UnmanagedType.Bool)]out bool value,uint flags);
 Rectangle screen; List<object> cards=new List<object>(); Point lastPointer=new Point(-999,-999);

 IntPtr lastSkin,dockWindow; Rectangle dockRectangle; bool dockShown=true; readonly DockVisibilityPolicy dockPolicy=new DockVisibilityPolicy();string dockDecision=""; int ticks; DockBackend backend; readonly DockPlate dockPlate=new DockPlate();
 TopIsland topIsland;FileShelf fileShelf;bool islandReady,shelfReady,legacyTopHidden;readonly ComponentRecovery componentRecovery=new ComponentRecovery();
 DockPreview preview;SideDock sideDock;string hoverId="";DateTime hoverSince=DateTime.MinValue,previewLeave=DateTime.MinValue;uint shellMessage;
 Region widgetRegion=new Region(Rectangle.Empty);float[][] dockOutline;
 void UpdateVisibleRegion(){
  var region=widgetRegion.Clone();
  if(dockShown&&dockWindow!=IntPtr.Zero){
   using(var path=new GraphicsPath()){
    if(dockOutline!=null&&dockOutline.Length>=3){var points=new PointF[dockOutline.Length];for(int i=0;i<points.Length;i++)points[i]=new PointF(dockOutline[i][0]+dockRectangle.X-screen.X-26,dockOutline[i][1]+dockRectangle.Y-screen.Y-26);path.AddPolygon(points);}
    else {float x=dockRectangle.X-screen.X+2,y=dockRectangle.Y-screen.Y+2,w=dockRectangle.Width-4,h=dockRectangle.Height-4,d=48;path.AddArc(x,y,d,d,180,90);path.AddArc(x+w-d,y,d,d,270,90);path.AddArc(x+w-d,y+h-d,d,d,0,90);path.AddArc(x,y+h-d,d,d,90,90);path.CloseFigure();}
    region.Union(path);
   }
  }
  var old=Region;Region=region;if(old!=null)old.Dispose();
 }
 [DllImport("user32.dll")]static extern bool RegisterShellHookWindow(IntPtr h);
 [DllImport("user32.dll")]static extern bool DeregisterShellHookWindow(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string name);
 [StructLayout(LayoutKind.Sequential)] struct RECT {public int L,T,R,B;}
 [StructLayout(LayoutKind.Sequential)] struct POINT {public int X,Y;}
 delegate bool EnumProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb,IntPtr p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder text,int n);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
 [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h,uint command);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags);
 [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] static extern short GetAsyncKeyState(int k);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string c,string t);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string c,string t);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int command);
 void Taskbar(bool visible){
  if(!primaryHost)return;
  if(!visible&&!File.Exists(Path.Combine(root,"desktop-ready.flag")))return;
  if(!visible&&SystemTray.KeepVisible){SystemTray.Show();return;}
  taskbarVisibility.Set(visible,screen);
 }
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out RECT bounds,int size);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out int value,int size);
 void DockVisibility(Point cursor){
  if(dockWindow==IntPtr.Zero)return;
  var at=new Point(cursor.X+screen.X,cursor.Y+screen.Y);var now=DateTime.UtcNow;
  bool edge=screen.Contains(at)&&at.X>=dockRectangle.Left&&at.X<=dockRectangle.Right&&at.Y>=screen.Bottom-3;
  bool hovering=(preview!=null&&!preview.IsDisposed&&preview.Bounds.Contains(at))||(dockShown&&dockRectangle.Contains(at))||coordinator.WorkspaceOpen||coordinator.draggingDock||DockBackend.Interaction(root);
  var front=GetForegroundWindow();uint pid;GetWindowThreadProcessId(front,out pid);var cl=new StringBuilder(256);GetClassName(front,cl,256);RECT rect=new RECT();
  bool application=front!=IntPtr.Zero&&pid!=ownPid&&front!=dockWindow&&IsWindowVisible(front)&&!IsIconic(front)&&cl.ToString()!="Progman"&&cl.ToString()!="WorkerW"&&cl.ToString()!="RainmeterMeterWindow";
  bool hasBounds=application&&(DwmGetWindowAttribute(front,9,out rect,Marshal.SizeOf(typeof(RECT)))==0||GetWindowRect(front,out rect));
  var visualDock=new Rectangle(dockRectangle.X,dockRectangle.Y+16,dockRectangle.Width,Math.Max(0,dockRectangle.Height-16));
  bool overlap=DesktopVisibilityPolicy.OverlapsDock(desktopWindows,visualDock);
  bool show=(!preferences.Current.dockAutoHide||dockPolicy.Update(now,overlap,edge,hovering))&&!SystemTray.KeepVisible;
  string decision=show+"|"+overlap+"|"+pid+"|"+cl;
  if(decision!=dockDecision){dockDecision=decision;File.WriteAllText(Diagnostic("dock-visibility.json"),json.Serialize(new{shown=show,covered=overlap,foregroundPid=pid,foregroundClass=cl.ToString(),ignoredOwnedWindow=!application,window=new{left=rect.L,top=rect.T,right=rect.R,bottom=rect.B},atEdge=edge,updated=now.ToString("o")}));}
  bool nativeShown=IsWindowVisible(dockWindow);
  if((show!=dockShown||show!=nativeShown)&&(show==nativeShown||monitorDock.SetVisible(show))){dockShown=show;UpdateVisibleRegion();dockPlate.Sync(Handle,show?dockWindow:IntPtr.Zero,dockRectangle,screen);}
 }
 void PreviewTick(Point cursor){
  if(preview!=null&&!preview.IsDisposed&&preview.KeyboardMode)return;
  var at=new Point(cursor.X+screen.X,cursor.Y+screen.Y);var now=DateTime.UtcNow;
  if(preview!=null&&!preview.IsDisposed&&preview.Bounds.Contains(at)){previewLeave=now.AddMilliseconds(400);return;}
  string id=dockShown&&dockRectangle.Contains(at)?Read(instance=="primary"?"dock-hover.txt":"dock-hover-"+instance+".txt",""):"";
  if(id!=hoverId){hoverId=id;hoverSince=now;if(id.Length>0&&preview!=null&&preview.EntryId!=id){preview.Dispose();preview=null;}}
  if(id.Length>0){previewLeave=now.AddMilliseconds(400);if((now-hoverSince).TotalMilliseconds>=400&&(preview==null||preview.IsDisposed||preview.EntryId!=id)){if(preview!=null)preview.Dispose();var entry=backend.Entry(id);if(entry!=null){preview=new DockPreview(entry,dockRectangle,theme=="Astro");preview.Show();}}}
  else if(now>previewLeave&&preview!=null){preview.Dispose();preview=null;}
 }
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;p.ExStyle&=~0x40000;return p;}}
 protected override void WndProc(ref Message m){if(primaryHost){if(runtime!=null)runtime.WindowMessage(ref m);if(hotkeys!=null&&hotkeys.Matches(m)){OpenWorkspace("apps");return;}if(m.Msg==0x7E||m.Msg==0x2E0){displaysRead=DateTime.MinValue;probe=DateTime.MinValue;}if(shellMessage!=0&&(uint)m.Msg==shellMessage&&backend!=null){long code=m.WParam.ToInt64();if(code==0x8006)backend.Attention(m.LParam.ToInt64());if(code==4||code==0x8004)DockBackend.ObserveActivation(m.LParam);}}if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}base.WndProc(ref m);}
 public LiquidDesktop():this(null,null){}
 LiquidDesktop(LiquidDesktop owner,MonitorRecord display){
  pixels=new WebViewPixelScale(view,()=>{layoutKey="";probe=DateTime.MinValue;});
  primaryHost=owner==null;coordinator=owner??this;monitor=display??MonitorCatalog.Read().Find(item=>item.Primary);instance=primaryHost?"primary":monitor.Instance;
  preferences=primaryHost?new DesktopPreferences(root):owner.preferences;sharedData=primaryHost?new SharedDesktopData(root):owner.sharedData;cursorTrail=primaryHost?new CursorTrail():null;monitorDock=new MonitorDock(root,instance);
  welcomePending=primaryHost&&preferences.IsFirstRun&&!preferences.Current.welcomeShown;
  lyrics=primaryHost?new DockLyrics(root):owner.lyrics;
  if(primaryHost){lyrics.VisualChanged+=Broadcast;backend=new DockBackend(root);backend.Refresh();library=new AppLibrary(root);Task.Run(()=>library.Refresh());preferences.Changed+=PreferencesChanged;}
  else backend=owner.backend;
  Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)??SystemIcons.Application;
  Text="PrismGlass Desktop";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
  screen=monitor.Screen.Bounds;Bounds=screen;Region=new Region(Rectangle.Empty);Enabled=false;
  view.Dock=DockStyle.Fill;Controls.Add(view);
  Shown+=async(s,e)=>{try{
   if(primaryHost){shellMessage=RegisterWindowMessage("SHELLHOOK");RegisterShellHookWindow(Handle);runtime=new RuntimePolicy(Handle);runtime.Changed+=RuntimeChanged;SystemEvents.DisplaySettingsChanged+=DisplayChanged;hotkeys=new HotkeyBinding(Handle);preferences.PrepareHotkey=hotkeys.Prepare;try{hotkeys.Prepare(preferences.Current.hotkey);hotkeys.Commit();}catch(Exception ex){hotkeyError=ex.Message;}workspace=new WorkspaceWindow(root,library,preferences);workspace.SharedData=sharedData;workspace.SettingsChanged+=PreferencesChanged;companion=new CompanionHost(this,root,library,preferences,sharedData,lyrics);workspace.CompanionRequested+=()=>companion.Show(workspace);SetupTray();}
   var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"runtime-data"));
   await view.EnsureCoreWebView2Async(env);SyncPixelScale();
   view.CoreWebView2.Settings.AreDevToolsEnabled=false;view.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;view.CoreWebView2.Settings.IsStatusBarEnabled=false;
   view.CoreWebView2.SetVirtualHostNameToFolderMapping("desktop.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
   view.CoreWebView2.ProcessFailed+=(ss,a)=>{File.WriteAllText(Diagnostic("error.txt"),"WebView2 process failed: "+a.ProcessFailedKind);Close();};
   view.CoreWebView2.NavigationStarting+=(ss,a)=>{Uri u;if(!Uri.TryCreate(a.Uri,UriKind.Absolute,out u)||u.Host!="desktop.local")a.Cancel=true;};
   view.CoreWebView2.NewWindowRequested+=(ss,a)=>a.Handled=true;
   view.CoreWebView2.WebMessageReceived+=async(ss,a)=>{
    if(!a.Source.StartsWith("https://desktop.local/",StringComparison.Ordinal))return;
    var data=a.TryGetWebMessageAsString();
    if(data=="ready"){ready=true;probe=DateTime.MinValue;Tick();}
    else if(data=="painted"){painted=true;probe=DateTime.MinValue;EnsureComponents();ApplyPreferences();}
    else if(data.StartsWith("dock-icons:")){int sequence,alpha;if(MonitorDock.TryIconMessage(data.Substring(11),out sequence,out alpha)){bool applied=await Task.Run(()=>monitorDock.SetIconOpacity(alpha));if(applied&&!closing)Send(new{type="dock-icons-ack",sequence=sequence,alpha=alpha});}}
    else if(data.StartsWith("dock-shape:")){dockOutline=json.Deserialize<float[][]>(data.Substring(11));dockPlate.Outline(dockOutline);UpdateVisibleRegion();}
    else if(data.StartsWith("widget-shapes:")){var outlines=json.Deserialize<float[][][]>(data.Substring(14));widgetRegion.MakeEmpty();foreach(var outline in outlines){if(outline.Length<3)continue;using(var path=new GraphicsPath()){var points=new PointF[outline.Length];for(int i=0;i<points.Length;i++)points[i]=new PointF(outline[i][0],outline[i][1]);path.AddPolygon(points);widgetRegion.Union(path);}}UpdateVisibleRegion();}
    else if(data.StartsWith("error:")){File.WriteAllText(Diagnostic("error.txt"),data);Close();}
    else if(data.StartsWith("stats:"))File.WriteAllText(Diagnostic("render-stats.json"),data.Substring(6));
   };
   view.Source=new Uri("https://desktop.local/index.html");
   timer.Interval=16;timer.Tick+=(ss,a)=>Tick();timer.Start();
  }catch(Exception ex){File.WriteAllText(Diagnostic("error.txt"),ex.ToString());Close();}};
  FormClosed+=(s,e)=>{closing=true;timer.Stop();timer.Dispose();if(primaryHost){if(companion!=null)companion.Dispose();SystemEvents.DisplaySettingsChanged-=DisplayChanged;preferences.Changed-=PreferencesChanged;preferences.PrepareHotkey=null;SystemTray.Cancel();DeregisterShellHookWindow(Handle);backend.CancelDrag();if(tray!=null){tray.Visible=false;tray.ContextMenuStrip.Dispose();tray.Dispose();}if(runtime!=null)runtime.Dispose();if(hotkeys!=null)hotkeys.Dispose();if(workspace!=null)workspace.Dispose();foreach(var host in new List<LiquidDesktop>(displays.Values))host.Close();displays.Clear();lyrics.Dispose();library.Dispose();sharedData.Dispose();cursorTrail.Dispose();LegacyTopbar(false);RestoreIcons();Taskbar(true);}tearingDown=true;if(sideDock!=null)sideDock.Dispose();if(topIsland!=null)topIsland.Dispose();if(fileShelf!=null)fileShelf.Dispose();if(preview!=null)preview.Dispose();monitorDock.Dispose();dockPlate.Dispose();view.Dispose();try{File.Delete(Diagnostic("heartbeat.txt"));File.Delete(Path.Combine(root,"dock-motion-"+instance+".txt"));}catch{}};
 }
 string Read(string file,string fallback){try{return File.ReadAllText(Path.Combine(root,file)).Trim();}catch{return fallback;}}
 string Diagnostic(string name){return Path.Combine(root,instance=="primary"?name:Path.GetFileNameWithoutExtension(name)+"-"+instance+Path.GetExtension(name));}
 bool WorkspaceOpen {get{return workspace!=null&&!workspace.IsDisposed&&workspace.IsOpen;}}
 IEnumerable<LiquidDesktop> Hosts(){yield return this;foreach(var host in displays.Values)yield return host;}
 void Broadcast(object value){foreach(var host in Hosts())host.Send(value);}
 void RuntimeChanged(){if(closing||!IsHandleCreated)return;try{BeginInvoke(new Action(()=>{foreach(var host in Hosts())host.probe=DateTime.MinValue;}));}catch(InvalidOperationException){}}
 void DisplayChanged(object sender,EventArgs args){if(closing||!IsHandleCreated)return;try{BeginInvoke(new Action(()=>{displaysRead=DateTime.MinValue;probe=DateTime.MinValue;}));}catch(InvalidOperationException){}}
 void PreferencesChanged(){if(!primaryHost){coordinator.PreferencesChanged();return;}if(hotkeys!=null){try{hotkeys.Configure(preferences.Current.hotkey);hotkeyError="";}catch(Exception ex){hotkeyError=ex.Message;}}failedDisplays.Clear();foreach(var host in Hosts()){host.probe=DateTime.MinValue;host.ApplyPreferences();}displaysRead=DateTime.MinValue;}
 void ApplyPreferences(){if(closing)return;var snapshot=preferences.Snapshot();Send(new{type="preferences",snapshot=snapshot});if(sideDock!=null&&!sideDock.IsDisposed)sideDock.ApplyPreferences(snapshot);if(topIsland!=null&&!topIsland.IsDisposed)topIsland.ApplyPreferences(snapshot);if(fileShelf!=null&&!fileShelf.IsDisposed)fileShelf.ApplyPreferences(snapshot);preferenceRevision=preferences.Revision;}
 void OpenWorkspace(string page){if(!primaryHost){coordinator.OpenWorkspace(page);return;}if(workspace==null||workspace.IsDisposed){workspace=new WorkspaceWindow(root,library,preferences);workspace.SharedData=sharedData;workspace.SettingsChanged+=PreferencesChanged;workspace.CompanionRequested+=()=>companion.Show(workspace);runtimeKey="";}var targetScreen=Screen.FromPoint(Cursor.Position);Rectangle targetDock=Rectangle.Empty;foreach(var host in Hosts()){if(host.monitor.Screen.DeviceName==targetScreen.DeviceName)targetDock=host.dockRectangle;if(host.sideDock!=null&&!host.sideDock.IsDisposed)host.sideDock.DismissTransient();if(host.topIsland!=null&&!host.topIsland.IsDisposed)host.topIsland.DismissTransient();if(host.fileShelf!=null&&!host.fileShelf.IsDisposed)host.fileShelf.DismissTransient();if(host.preview!=null){host.preview.Dispose();host.preview=null;}}workspace.OpenPage(page,targetScreen,targetDock);}
 void ExitDesktop(){
  try{var script=Path.GetFullPath(Path.Combine(root,"..","DesktopMode.ps1"));Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \""+script+"\" -Mode Game -ShowErrors"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});}
  catch(Exception ex){tray.ShowBalloonTip(3000,"PrismGlass 未能退出",ex.Message,ToolTipIcon.Warning);}
 }
 void SetupTray(){if(!primaryHost||tray!=null)return;var menu=new ContextMenuStrip();menu.Items.Add("应用中心",null,(s,e)=>OpenWorkspace("apps"));menu.Items.Add("PrismGlass 设置",null,(s,e)=>OpenWorkspace("settings"));menu.Items.Add("iPad 伴随屏",null,(s,e)=>companion.Show());menu.Items.Add("暂停 / 恢复动效",null,(s,e)=>{try{preferences.Apply(new Dictionary<string,object>{{"manualPaused",!preferences.Current.manualPaused}},preferences.Revision);}catch(Exception ex){tray.ShowBalloonTip(3000,"PrismGlass",ex.Message,ToolTipIcon.Warning);}});menu.Items.Add("退出 PrismGlass",null,(s,e)=>ExitDesktop());menu.Items.Add("关于 PrismGlass",null,(s,e)=>MessageBox.Show(this,"PrismGlass Desktop "+Application.ProductVersion+"\n免费开源 · MIT\nhttps://github.com/liburce0412-alt/prismglass-desktop","PrismGlass",MessageBoxButtons.OK,MessageBoxIcon.Information));tray=new NotifyIcon{Icon=Icon,Text="PrismGlass",Visible=true,ContextMenuStrip=menu};tray.DoubleClick+=(s,e)=>OpenWorkspace("apps");}
 void ReconcileDisplays(){
  if(!primaryHost||reconciling||closing)return;reconciling=true;
  try{var catalog=MonitorCatalog.Read();var primary=catalog.Find(item=>item.Primary);if(primary!=null)monitor=primary;var wanted=new Dictionary<string,MonitorRecord>(StringComparer.OrdinalIgnoreCase);foreach(var item in catalog)if(!item.Primary&&preferences.ForMonitor(item.Id,false).enabled&&!failedDisplays.Contains(item.Id))wanted[item.Id]=item;
   foreach(var pair in new List<KeyValuePair<string,LiquidDesktop>>(displays)){MonitorRecord connected;if(!wanted.TryGetValue(pair.Key,out connected)||connected.Instance!=pair.Value.instance||pair.Value.IsDisposed){displays.Remove(pair.Key);if(!pair.Value.IsDisposed)pair.Value.Close();}else{pair.Value.monitor=connected;pair.Value.probe=DateTime.MinValue;}}
   foreach(var pair in wanted)if(!displays.ContainsKey(pair.Key)){var host=new LiquidDesktop(this,pair.Value);displays.Add(pair.Key,host);host.Show();}
   monitorError="";
  }catch(Exception ex){monitorError=ex.Message;}finally{reconciling=false;}
 }
 void PublishRuntime(){
  if(!primaryHost)return;var startup=new List<object>();bool allReady=painted;
  foreach(var item in MonitorCatalog.Read()){
   LiquidDesktop host=null;if(item.Primary)host=this;else displays.TryGetValue(item.Id,out host);bool available=host!=null&&!host.IsDisposed&&!host.closing;
   var options=preferences.ForMonitor(item.Id,item.Primary);bool rendererReady=available&&host.painted,leftReady=available&&host.sideDock!=null&&!host.sideDock.IsDisposed&&host.sideDock.IsReady,topReady=available&&host.islandReady,rightReady=available&&host.shelfReady,dockReady=available&&host.dockWindow!=IntPtr.Zero;
   bool displayReady=DesktopStartupReadiness.DisplayReady(options.enabled,rendererReady,options.left,leftReady,options.top,topReady,options.right,rightReady,options.dock,dockReady);allReady&=displayReady;
   startup.Add(new{id=item.Id,enabled=options.enabled,ready=displayReady,rendererReady=rendererReady,leftReady=leftReady,topReady=topReady,rightReady=rightReady,dockReady=dockReady});
  }
  DesktopSettingsFiles.Write(Path.Combine(root,"readiness.json"),json.Serialize(new{version=1,pid=ownPid,ready=allReady,primaryEnabled=preferences.ForMonitor(monitor.Id,true).enabled,monitors=startup}));
  if(workspace==null||workspace.IsDisposed)return;var monitors=new List<object>();foreach(var host in Hosts())monitors.Add(new{id=host.monitor.Id,name=host.monitor.Name,reasons=host.pauseReasons});var snapshot=new{monitors=monitors,hotkeyError=hotkeyError,monitorError=monitorError};string key=json.Serialize(snapshot);if(key!=runtimeKey){runtimeKey=key;workspace.PublishRuntime(snapshot,RuntimePolicy.Reasons(preferences.Current,runtime==null?new RuntimeSignals():runtime.Signals,false,false,systemAnimations).Length>0);}
 }
 void Send(object value){if(view.CoreWebView2!=null&&!closing)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(value));}
 void Tick(){
  if(!ready||closing||busy)return;busy=true;
  try{
   if(primaryHost&&File.Exists(Path.Combine(root,"stop.request"))){try{File.Delete(Path.Combine(root,"stop.request"));}catch(IOException){return;}Close();return;}
   var now=DateTime.UtcNow;
   if((now-windowProbe).TotalMilliseconds>=150){windowProbe=now;if(primaryHost)RefreshWindowCoverage();UpdateComponentVisibility();}
   if((now-probe).TotalSeconds>=1){probe=now;Probe();}
   POINT p;GetCursorPos(out p);var cursor=new Point(p.X-screen.X,p.Y-screen.Y);
   if(primaryHost){cursorTrail.Update(new Point(p.X,p.Y),RuntimePolicy.Reasons(preferences.Current,runtime==null?new RuntimeSignals():runtime.Signals,false,false,systemAnimations).Length==0);
   if((now-displaysRead).TotalSeconds>=5){displaysRead=now;ReconcileDisplays();}
   if(welcomePending&&painted&&File.Exists(Path.Combine(root,"desktop-ready.flag"))){welcomePending=false;OpenWorkspace("welcome");preferences.Apply(new Dictionary<string,object>{{"welcomeShown",true}},preferences.Revision);}
   var workspaceRequest=Path.Combine(root,"workspace.request");if(File.Exists(workspaceRequest)){string page=Read("workspace.request","");File.Delete(workspaceRequest);if(page=="apps"||page=="settings"||page=="weather"||page=="welcome")OpenWorkspace(page);}
   var companionRequest=Path.Combine(root,"companion.request");if(File.Exists(companionRequest)){File.Delete(companionRequest);companion.Show();}
   var shelfRequest=Path.Combine(root,"shelf.request");if(File.Exists(shelfRequest)){string requested=Read("shelf.request","");File.Delete(shelfRequest);if(requested=="manage"){if(workspace!=null&&!workspace.IsDisposed)workspace.Hide();var target=this;foreach(var host in Hosts())if(host.monitor.Screen.Bounds.Contains(new Point(p.X,p.Y))){target=host;break;}target.legacyManagerOpen=true;target.EnsureComponents();if(target.fileShelf!=null&&target.shelfReady)target.fileShelf.OpenLegacyManager();}}
   var panelRequest=Path.Combine(root,"panel.request");
   if(File.Exists(panelRequest)){string mode=Read("panel.request","");File.Delete(panelRequest);if(sideDock!=null&&!sideDock.IsDisposed&&(mode=="battery"||mode=="network"||mode=="both"||mode=="calendar"))sideDock.Open(mode,new Point(p.X,p.Y));}
   var widgetRequest=Path.Combine(root,"widget.request");if(File.Exists(widgetRequest)){string name=Read("widget.request","");File.Delete(widgetRequest);if(sideDock!=null&&!sideDock.IsDisposed)sideDock.Open(name.ToLowerInvariant(),new Point(p.X,p.Y));}
   var trayRequest=Path.Combine(root,"tray.request");if(File.Exists(trayRequest)){File.Delete(trayRequest);if(topIsland!=null&&!topIsland.IsDisposed)topIsland.OpenTray();}
   DockBackend.ObserveForeground();var dockAreas=new Dictionary<string,Rectangle>();foreach(var host in Hosts())if(host.dockWindow!=IntPtr.Zero)dockAreas[host.instance]=host.dockRectangle;draggingDock=backend.TickDrag(dockAreas);
   }
   if(painted)DockVisibility(cursor);
   if(painted&&primaryHost){bool near=WorkspaceOpen||draggingDock||DockBackend.Interaction(root),anyVisible=false,musicOpen=false;foreach(var host in Hosts()){near|=Rectangle.Inflate(host.dockRectangle,24,32).Contains(new Point(p.X,p.Y))||(host.preview!=null&&!host.preview.IsDisposed);anyVisible|=host.dockShown&&host.dockWindow!=IntPtr.Zero&&!host.screenCovered;musicOpen|=host.sideDock!=null&&!host.sideDock.IsDisposed&&host.sideDock.MusicOpen;}lyrics.DesktopMediaActive=musicOpen;lyrics.Tick(now,anyVisible,near,RuntimePolicy.Reasons(preferences.Current,runtime==null?new RuntimeSignals():runtime.Signals,false,false,systemAnimations).Length>0);}
   if(painted&&ticks%5==0)PreviewTick(cursor);
   bool down=(GetAsyncKeyState(1)&0x8000)!=0;
   if(cursor!=lastPointer||down!=lastDown){lastPointer=cursor;lastDown=down;Send(new {type="pointer",x=cursor.X,y=cursor.Y,down=down});}
   if(painted&&(now-beat).TotalSeconds>=2){beat=now;File.WriteAllText(Diagnostic("heartbeat.txt"),((long)(now-new DateTime(1970,1,1)).TotalSeconds)+" "+theme);if(primaryHost)PublishRuntime();}
   if(++ticks%150==0)Send(new {type="stats"});
  }catch(Exception ex){File.WriteAllText(Diagnostic("error.txt"),ex.ToString());}finally{busy=false;}
 }
 void LegacyTopbar(bool hidden){
  if(!primaryHost)return;
  if(hidden==legacyTopHidden)return;legacyTopHidden=hidden;layoutKey="";
  try{Process.Start(new ProcessStartInfo(RuntimePaths.Rainmeter,(hidden?"!DeactivateConfig \"PrismGlass\\Topbar\"":"!ActivateConfig \"PrismGlass\\Topbar\" \"Main.ini\"")){UseShellExecute=false,CreateNoWindow=true});}catch{}
 }
 void RestoreIcons(){
  try{DesktopIcons.SetVisible(true);}
  catch(Exception ex){
   // Explorer may already be shutting down; still release any remaining icon HWND.
   EnumWindows((h,p)=>{var v=FindWindowEx(h,IntPtr.Zero,"SHELLDLL_DefView",null);if(v!=IntPtr.Zero){var icons=FindWindowEx(v,IntPtr.Zero,"SysListView32",null);ShowWindow(v,5);if(icons!=IntPtr.Zero)ShowWindow(icons,5);}return true;},IntPtr.Zero);
   try{File.WriteAllText(Path.Combine(root,"icon-restore-error.txt"),ex.ToString());}catch{}
  }
 }
 void ComponentFailed(int component){
  if(closing||tearingDown)return;
  componentRecovery.Failed(component,DateTime.UtcNow);coordinator.Taskbar(true);
  if(component==1){islandReady=false;LegacyTopbar(false);}if(component==2)shelfReady=false;
  if(componentRecovery.Exhausted(component)){
   if(!primaryHost){coordinator.failedDisplays.Add(monitor.Id);coordinator.monitorError=monitor.Name+" 的组件反复启动失败，已释放此屏实例；修改显示器设置后可重试。";Close();return;}
   try{File.WriteAllText(Path.Combine(root,"component-recovery.txt"),"Desktop component failed three times; restoring ordinary desktop.");
    var script=Path.GetFullPath(Path.Combine(root,"..","DesktopMode.ps1"));
    Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+script+"\" -Mode Recovery"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
   }catch{}finally{Close();}
  }
 }
 void EnsureComponents(){
  if(closing)return;var now=DateTime.UtcNow;var options=preferences.ForMonitor(monitor.Id,monitor.Primary);bool left=options.enabled&&options.left,top=options.enabled&&options.top,right=options.enabled&&options.right||legacyManagerOpen;
  tearingDown=true;try{if(!left&&sideDock!=null){sideDock.Dispose();sideDock=null;}if(!top&&topIsland!=null){topIsland.Dispose();topIsland=null;islandReady=false;}if(!right&&fileShelf!=null){fileShelf.Dispose();fileShelf=null;shelfReady=false;}}finally{tearingDown=false;}
  if(!top)LegacyTopbar(true);
  if(left&&(sideDock==null||sideDock.IsDisposed)&&componentRecovery.CanStart(0,now)){
   sideDock=new SideDock(root,false,sharedData,instance);sideDock.FormClosed+=(s,e)=>ComponentFailed(0);sideDock.Sync(theme,monitor.Screen.WorkingArea,IntPtr.Zero,paused);sideDock.ApplyPreferences(preferences.Snapshot());sideDock.Show();
  }
  if(top&&(topIsland==null||topIsland.IsDisposed)&&componentRecovery.CanStart(1,now)){
   islandReady=false;topIsland=new TopIsland(root,false,sharedData,instance);topIsland.Ready+=()=>{islandReady=true;LegacyTopbar(true);topIsland.ApplyPreferences(preferences.Snapshot());};topIsland.FormClosed+=(s,e)=>ComponentFailed(1);topIsland.Sync(theme,monitor.Screen.WorkingArea,IntPtr.Zero,paused);topIsland.Show();
  }
  if(right&&(fileShelf==null||fileShelf.IsDisposed)&&componentRecovery.CanStart(2,now)){
   shelfReady=false;fileShelf=new FileShelf(root,false,sharedData,instance);fileShelf.Ready+=()=>{shelfReady=true;fileShelf.ApplyPreferences(preferences.Snapshot());if(legacyManagerOpen)fileShelf.OpenLegacyManager();};fileShelf.CursorSettingsChanged+=(settings,size)=>coordinator.cursorTrail.Configure(settings,size);fileShelf.FormClosed+=(s,e)=>ComponentFailed(2);fileShelf.Sync(theme,monitor.Screen.WorkingArea,IntPtr.Zero,paused);fileShelf.Show();
  }
 }
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr h,int index);
 internal static void PlaceComponent(Form component,IntPtr anchor){
  if(GetForegroundWindow()==component.Handle)return;
  // The Dock is topmost. Inserting another component beside it promotes that
  // component too, even though its Form.TopMost property remains false.
  if((GetWindowLong(component.Handle,-20)&8)!=0)SetWindowPos(component.Handle,new IntPtr(-2),0,0,0,0,0x13);
  if(anchor==IntPtr.Zero||(GetWindowLong(anchor,-20)&8)!=0)return;
  var previous=GetWindow(anchor,3);
  if(previous!=IntPtr.Zero&&previous!=component.Handle&&(GetWindowLong(previous,-20)&8)==0)SetWindowPos(component.Handle,previous,0,0,0,0,0x13);
 }
 void UpdateComponentVisibility(){
  var options=preferences.ForMonitor(monitor.Id,monitor.Primary);var enabled=new[]{options.left,options.top,options.right};var signals=coordinator.runtime==null?new RuntimeSignals():coordinator.runtime.Signals;
  var components=new Form[]{sideDock,topIsland,fileShelf};
  var bounds=new[]{sideDock==null?Rectangle.Empty:sideDock.OcclusionBounds,topIsland==null?Rectangle.Empty:topIsland.OcclusionBounds,fileShelf==null?Rectangle.Empty:fileShelf.OcclusionBounds};
  var hidden=new bool[3];
  for(int i=0;i<components.Length;i++){
   var component=components[i];if(component==null||component.IsDisposed)continue;
   bool forceManager=i==2&&legacyManagerOpen;hidden[i]=signals.locked||signals.displayOff||!forceManager&&(!options.enabled||!enabled[i]||screenCovered||DesktopVisibilityPolicy.OverlapsComponent(desktopWindows,bounds[i]));
   bool initialized=i==0?sideDock.IsReady:i==1?islandReady:shelfReady;
   if(hidden[i]&&initialized){if(component.Visible)component.Hide();}
   else if(!hidden[i]&&!component.Visible)component.Show();
  }
  string key=json.Serialize(new{hidden=hidden,screenCovered=screenCovered,bounds=bounds});
  if(key!=componentVisibility){componentVisibility=key;File.WriteAllText(Diagnostic("component-visibility.json"),json.Serialize(new{leftHidden=hidden[0],topHidden=hidden[1],rightHidden=hidden[2],screenCovered=screenCovered,bounds=bounds,updated=DateTime.UtcNow.ToString("o")}));}
 }
 void RefreshWindowCoverage(){
  var windows=new List<DesktopWindowState>();
  EnumWindows((h,p)=>{
   uint pid;GetWindowThreadProcessId(h,out pid);if(pid==ownPid||!IsWindowVisible(h)||IsIconic(h))return true;
   if(!DesktopVisibilityPolicy.IsApplicationSurface(GetWindowLong(h,-20)))return true;
   int cloaked;if(DwmGetWindowAttribute(h,14,out cloaked,4)==0&&cloaked!=0)return true;
   var cl=new StringBuilder(256);GetClassName(h,cl,256);string name=cl.ToString();
   if(name=="Progman"||name=="WorkerW"||name=="RainmeterMeterWindow"||name=="Shell_TrayWnd"||name=="Shell_SecondaryTrayWnd")return true;
   RECT r;if(DwmGetWindowAttribute(h,9,out r,Marshal.SizeOf(typeof(RECT)))!=0&&!GetWindowRect(h,out r))return true;
   windows.Add(new DesktopWindowState{Bounds=Rectangle.FromLTRB(r.L,r.T,r.R,r.B),Application=true,Visible=true,Minimized=false,Cloaked=false,Maximized=IsZoomed(h)});return true;
  },IntPtr.Zero);
  foreach(var host in Hosts()){host.desktopWindows=windows;host.screenCovered=DesktopVisibilityPolicy.AnyCovered(windows,host.screen,host.monitor.Screen.WorkingArea);}
 }
 void SyncPixelScale(){pixels.Sync();}
 void Probe(){
  SyncPixelScale();
  SystemParametersInfo(0x1042,0,out systemAnimations,0);
  if(primaryHost){preferences.Reload();if(runtime!=null)runtime.ReadPower();if(preferenceRevision!=preferences.Revision){PreferencesChanged();}backend.Refresh();}
  if(legacyManagerOpen&&shelfReady&&fileShelf!=null&&!fileShelf.LegacyManagerOpen)legacyManagerOpen=false;
  if(painted)EnsureComponents();if(closing)return;IntPtr dockHandle=IntPtr.Zero;Rectangle dockBounds=Rectangle.Empty;
  var current=monitor.Screen.Bounds;if(current!=screen||Bounds!=current){screen=current;Bounds=screen;layoutKey="";}
  string nextTheme=preferences.Current.theme;var options=preferences.ForMonitor(monitor.Id,monitor.Primary);
  try{monitorDock.Configure(monitor,options,preferences.Current.dockIconSize);}catch(Exception ex){coordinator.monitorError=ex.Message;}
  bool manual=preferences.Current.manualPaused,covered=screenCovered;manualMotionPause=manual;
  if(primaryHost){string cursorDirectory=Path.GetFullPath(Path.Combine(root,".."));cursorTrail.Configure(CursorPreferences.Load(cursorDirectory),CursorPreferences.ReadScale(cursorDirectory));}
  pauseReasons=RuntimePolicy.Reasons(preferences.Current,coordinator.runtime==null?new RuntimeSignals():coordinator.runtime.Signals,covered&&!coordinator.WorkspaceOpen&&!coordinator.draggingDock,!options.enabled,systemAnimations);
  bool nextPaused=pauseReasons.Length>0;timer.Interval=coordinator.WorkspaceOpen||coordinator.draggingDock?16:nextPaused?150:16;
  string motion=nextPaused?"true":"false";if(motion!=motionKey){motionKey=motion;DesktopSettingsFiles.Write(Path.Combine(root,"dock-motion-"+instance+".txt"),motion);}
  var regions=new List<Rectangle>();var nextCards=new List<GlassCard>();lastSkin=IntPtr.Zero;IntPtr firstSkin=IntPtr.Zero;
  EnumWindows((h,p)=>{var cl=new StringBuilder(256);GetClassName(h,cl,256);if(cl.ToString()!="RainmeterMeterWindow")return true;
   var title=new StringBuilder(1024);GetWindowText(h,title,1024);if(!title.ToString().Contains("\\PrismGlass\\"))return true;
   RECT r;if(!GetWindowRect(h,out r))return true;var rect=new Rectangle(r.L-screen.Left,r.T-screen.Top,r.R-r.L,r.B-r.T);
   if(rect.Width<10||rect.Height<10||!screen.IntersectsWith(new Rectangle(r.L,r.T,rect.Width,rect.Height)))return true;
   string name=Path.GetFileName(Path.GetDirectoryName(title.ToString()));if(name.StartsWith("Dock",StringComparison.Ordinal)&&name!=monitorDock.SkinName)return true;if(!primaryHost&&name!=monitorDock.SkinName)return true;if(name==monitorDock.SkinName)name="Dock";if(name=="Dock"&&(!options.enabled||!options.dock))return true;if(name=="Topbar"&&(islandReady||!options.enabled||!options.top))return true;
   if(name!="Dock"&&!IsWindowVisible(h))return true;
   if(name=="Dock"){dockHandle=h;dockBounds=new Rectangle(r.L,r.T,rect.Width,rect.Height);}
   int insetY=name=="Dock"?16:0;
   nextCards.Add(new GlassCard{name=name,x=rect.X,y=rect.Y+insetY,w=rect.Width,h=rect.Height-insetY,r=Math.Min(24,(rect.Height-insetY)/2),insetY=insetY});
   if(name!="Dock"){regions.Add(Rectangle.Inflate(rect,26,26));if(firstSkin==IntPtr.Zero)firstSkin=h;}lastSkin=h;return true;
  },IntPtr.Zero);
  if(firstSkin==IntPtr.Zero)firstSkin=dockHandle;
  string key=json.Serialize(nextCards);
  bool changed=key!=layoutKey||theme!=nextTheme||paused!=nextPaused;
  cards=new List<object>();foreach(var card in nextCards)cards.Add(card);theme=nextTheme;paused=nextPaused;
  if(key!=layoutKey){layoutKey=key;widgetRegion.MakeEmpty();foreach(var rect in regions)widgetRegion.Union(rect);dockOutline=null;}
  if(painted&&lastSkin!=IntPtr.Zero)SetWindowPos(Handle,lastSkin,0,0,0,0,0x53);
  var area=monitor.Screen.WorkingArea;
  if(sideDock!=null&&!sideDock.IsDisposed){sideDock.Sync(theme,area,firstSkin,paused||!sideDock.Visible);if(sideDock.IsReady)componentRecovery.Healthy(0,DateTime.UtcNow);}
  if(topIsland!=null&&!topIsland.IsDisposed){topIsland.Sync(theme,area,firstSkin,paused||!topIsland.Visible);if(islandReady)componentRecovery.Healthy(1,DateTime.UtcNow);}
  if(fileShelf!=null&&!fileShelf.IsDisposed){fileShelf.Sync(theme,area,firstSkin,paused||!fileShelf.Visible);if(shelfReady)componentRecovery.Healthy(2,DateTime.UtcNow);}
  UpdateComponentVisibility();
  if(islandReady&&shelfReady)File.WriteAllText(Diagnostic("extensions-heartbeat.txt"),DateTime.UtcNow.ToString("o"));
  dockWindow=dockHandle;dockRectangle=dockBounds;
  UpdateVisibleRegion();
  if(painted){dockPlate.Sync(Handle,dockShown?dockHandle:IntPtr.Zero,dockBounds,screen);Taskbar(!options.enabled||!options.dock||dockHandle==IntPtr.Zero||(options.left&&(sideDock==null||!sideDock.IsReady))||(options.top&&!islandReady)||(options.right&&!shelfReady));}
  if(changed)Send(new {type="layout",width=screen.Width,height=screen.Height,cards=cards,theme=theme,paused=paused});
  var source=Path.Combine(root,"..","Glass-Wallpaper-Sample.png");var stamp=File.GetLastWriteTimeUtc(source);
  if(stamp!=wallpaperTime){wallpaperTime=stamp;if(primaryHost)File.Copy(source,Path.Combine(root,"wallpaper.png"),true);Send(new {type="wallpaper",version=stamp.Ticks.ToString()});}
  File.WriteAllText(Diagnostic("layout.json"),json.Serialize(new {width=screen.Width,height=screen.Height,cards=cards,theme=theme,paused=paused,behind=lastSkin.ToInt64()}));
 }
 [STAThread] static void Main(string[] args){
  try{if(!SetProcessDpiAwarenessContext(new IntPtr(-4)))SetProcessDPIAware();}catch(EntryPointNotFoundException){SetProcessDPIAware();}string root=AppDomain.CurrentDomain.BaseDirectory;
  if(args.Length==0){try{Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),LoginStartup.Arguments(root)+" -ShowErrors"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});}catch(Exception ex){MessageBox.Show("PrismGlass 未能启动："+ex.Message,"PrismGlass");}return;}
  if(args.Length==1&&(args[0]=="--apps"||args[0]=="--settings")){File.WriteAllText(Path.Combine(root,"workspace.request"),args[0]=="--apps"?"apps":"settings");return;}
  if(args.Length==1&&args[0]=="--tray"){File.WriteAllText(Path.Combine(root,"tray.request"),"open");return;}
  if(args.Length==1&&args[0]=="--companion"){File.WriteAllText(Path.Combine(root,"companion.request"),"open");return;}
  if(args.Length==2&&args[0]=="--widget"&&Array.IndexOf(new[]{"Weather","Music","Focus","Folders","System","Tools","Life"},args[1])>=0){File.WriteAllText(Path.Combine(root,"widget.request"),args[1]);return;}
  if(args.Length==2&&args[0]=="--panel"&&(args[1]=="battery"||args[1]=="network"||args[1]=="both"||args[1]=="calendar")){File.WriteAllText(Path.Combine(root,"panel.request"),args[1]);return;}
  if(args.Length>0){string flag=args[0];if(flag=="--side-preview"){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SideDock(root,true));return;}if(flag=="--dock-menu"&&args.Length==2){Application.EnableVisualStyles();DockBackend.Menu(root,args[1]);return;}if((flag=="--dock"||flag=="--dock-new")&&args.Length==2){Application.EnableVisualStyles();DockBackend.Click(root,args[1],flag=="--dock-new");return;}if(flag=="--toggle-theme"){var p=Path.Combine(root,"theme.txt");File.WriteAllText(p,File.Exists(p)&&File.ReadAllText(p).Trim()=="Astro"?"Sky":"Astro");return;}
   if(flag=="--toggle-motion"){var p=Path.Combine(root,"paused.txt");File.WriteAllText(p,File.Exists(p)&&File.ReadAllText(p).Trim()=="true"?"false":"true");return;}
   if(flag=="--quit"){File.WriteAllText(Path.Combine(root,"stop.request"),"stop");return;}}
  bool created;using(var gate=new Mutex(true,"Local\\PrismLiquidDesktop",out created)){if(!created)return;File.Delete(Path.Combine(root,"stop.request"));
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new LiquidDesktop());}
 }
}
