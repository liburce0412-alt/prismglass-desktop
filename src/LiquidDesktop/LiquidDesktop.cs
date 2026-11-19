using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
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
 public static bool PublishDue(bool initialReadyPublished,bool heartbeatDue,TimeSpan sinceLastPublish){return heartbeatDue||(!initialReadyPublished&&sinceLastPublish.TotalMilliseconds>=250);}
 public static bool CanHandoff(bool componentsReady,bool widgetsReady,bool stageConfigured){return componentsReady&&widgetsReady&&stageConfigured;}
}
internal sealed class LiquidDesktop : Form {
 readonly string root=AppDomain.CurrentDomain.BaseDirectory;readonly uint ownPid=(uint)Process.GetCurrentProcess().Id;
 readonly WebView2 view=new WebView2();
 readonly WebViewPixelScale pixels;
 readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 DateTime probe=DateTime.MinValue,beat=DateTime.MinValue,readinessProbe=DateTime.MinValue,wallpaperSourceTime=DateTime.MinValue;string wallpaperTime;
 bool ready,painted,closing,busy,componentsReady,initialReadyPublished,stageConfigured; string layoutKey="",theme="Sky"; bool paused;
 List<DesktopWindowState> desktopWindows=new List<DesktopWindowState>();
 DateTime windowProbe=DateTime.MinValue;bool screenCovered,stageFullscreen,stageEdgeEligible,manualMotionPause;string componentVisibility="";
 readonly CursorTrail cursorTrail;
 readonly TaskbarVisibility taskbarVisibility=new TaskbarVisibility();
 readonly DockLyrics lyrics;
 readonly DesktopSession session;readonly LiquidDesktop coordinator;readonly bool primaryHost;readonly string instance;MonitorRecord monitor;
 readonly DesktopPreferences preferences;readonly SharedDesktopData sharedData;readonly MonitorDock monitorDock;readonly DesktopWidgets desktopWidgets;bool widgetsWereDragging;
 readonly Dictionary<string,LiquidDesktop> displays=new Dictionary<string,LiquidDesktop>(StringComparer.OrdinalIgnoreCase);
 readonly HashSet<string> failedDisplays=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 AppLibrary library;WorkspaceWindow workspace;RuntimePolicy runtime;HotkeyBinding hotkeys;NotifyIcon tray;long preferenceRevision;string hotkeyError="",monitorError="",runtimeKey="",motionKey="";string pendingShelfContent="";bool reconciling,tearingDown,draggingDock,legacyManagerOpen;DateTime settingsRead=DateTime.MinValue,displaysRead=DateTime.MinValue,requestsRead=DateTime.MinValue;string[] pauseReasons=new string[0];
 bool systemAnimations=true,welcomePending;
 NativeWindowMotion windowMotion;StageManager stages;
 [DllImport("user32.dll")]static extern bool SystemParametersInfo(uint action,uint parameter,[MarshalAs(UnmanagedType.Bool)]out bool value,uint flags);
 Rectangle screen; List<object> cards=new List<object>();
 readonly DesktopPointerDelivery pointerDelivery;DesktopRawMouseInput rawPointer;

 IntPtr lastSkin,dockWindow; Rectangle dockRectangle; bool dockShown=true; readonly DockVisibilityPolicy dockPolicy=new DockVisibilityPolicy();string dockDecision=""; int ticks; DockBackend backend; readonly DockPlate dockPlate=new DockPlate();
 readonly OpticalBackdrop opticalBackdrop;
 TopIsland topIsland;FileShelf fileShelf;bool islandReady,shelfReady,legacyTopHidden;readonly ComponentRecovery componentRecovery=new ComponentRecovery();
 DockLabel dockLabel;DockPreview preview;SideDock sideDock;string hoverId="",previewSuppressedId="";DateTime hoverSince=DateTime.MinValue,previewLeave=DateTime.MinValue;uint shellMessage;
 Region widgetRegion=new Region(Rectangle.Empty);float[][] dockOutline;
 void UpdateVisibleRegion(){
  var region=widgetRegion.Clone();
  if(dockShown&&dockWindow!=IntPtr.Zero){
   using(var path=new GraphicsPath()){
    if(dockOutline!=null&&dockOutline.Length>=3){var points=new PointF[dockOutline.Length];for(int i=0;i<points.Length;i++)points[i]=new PointF(dockOutline[i][0]+dockRectangle.X-screen.X-26,dockOutline[i][1]+dockRectangle.Y-screen.Y-26);path.AddPolygon(points);}
    else {float x=dockRectangle.X-screen.X+2,y=dockRectangle.Y-screen.Y+2,w=dockRectangle.Width-4,h=dockRectangle.Height-4,d=48;path.AddArc(x,y,d,d,180,90);path.AddArc(x+w-d,y,d,d,270,90);path.AddArc(x+w-d,y+h-d,d,d,0,90);path.AddArc(x,y+h-d,d,d,90,90);path.CloseFigure();}
    using(var clip=GlassWindow.Clip(path,ClientRectangle))region.Union(clip);
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
 [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
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
  var hit=GetAncestor(WindowFromPoint(at),2);bool pointerDown=(GetAsyncKeyState(1)&0x8000)!=0;
  bool hovering=(preview!=null&&!preview.IsDisposed&&preview.Bounds.Contains(at))||(dockShown&&dockRectangle.Contains(at)&&!pointerDown&&(hit==dockWindow||hit==dockPlate.Handle))||coordinator.draggingDock||DockBackend.Interaction(root);
  var front=GetForegroundWindow();uint pid;GetWindowThreadProcessId(front,out pid);var cl=new StringBuilder(256);GetClassName(front,cl,256);RECT rect=new RECT();
  bool application=front!=IntPtr.Zero&&pid!=ownPid&&front!=dockWindow&&IsWindowVisible(front)&&!IsIconic(front)&&cl.ToString()!="Progman"&&cl.ToString()!="WorkerW"&&cl.ToString()!="RainmeterMeterWindow";
  bool hasBounds=application&&(DwmGetWindowAttribute(front,9,out rect,Marshal.SizeOf(typeof(RECT)))==0||GetWindowRect(front,out rect));
  var visualDock=new Rectangle(dockRectangle.X,dockRectangle.Y+16,dockRectangle.Width,Math.Max(0,dockRectangle.Height-16));
  bool overlap=DesktopVisibilityPolicy.OverlapsDock(desktopWindows,visualDock);
  bool show=(!preferences.Current.dockAutoHide||dockPolicy.Update(now,overlap,edge,hovering))&&!SystemTray.KeepVisible;
  string decision=show+"|"+overlap+"|"+hovering+"|"+pid+"|"+cl;
  if(decision!=dockDecision){dockDecision=decision;File.WriteAllText(Diagnostic("dock-visibility.json"),json.Serialize(new{shown=show,covered=overlap,hovering=hovering,foregroundPid=pid,foregroundClass=cl.ToString(),ignoredOwnedWindow=!application,window=new{left=rect.L,top=rect.T,right=rect.R,bottom=rect.B},atEdge=edge,updated=now.ToString("o")}));}
  bool nativeShown=IsWindowVisible(dockWindow);
  if(show==nativeShown)monitorDock.Dispatch.Cancel("visibility");
  if((show!=dockShown||show!=nativeShown)&&(show==nativeShown||monitorDock.SetVisible(show))){dockShown=show;UpdateVisibleRegion();dockPlate.Sync(Handle,show?dockWindow:IntPtr.Zero,dockRectangle,screen);SyncDockBackdrop();}
 }
 void SyncDockBackdrop(){if(opticalBackdrop!=null)opticalBackdrop.SetOverlay(dockShown&&dockWindow!=IntPtr.Zero?dockPlate.Handle:IntPtr.Zero,new Rectangle(dockRectangle.X,dockRectangle.Y+16,dockRectangle.Width,Math.Max(0,dockRectangle.Height-16)));}
 void ClosePreviews(string id){DockPreview.CloseActive();foreach(var host in Hosts()){if(host.preview!=null){host.preview.Dispose();host.preview=null;}if(host.dockLabel!=null)host.dockLabel.Hide();host.previewSuppressedId=id??host.hoverId;}}
 bool DockPointerAt(Point at){if(!dockShown||dockWindow==IntPtr.Zero||!IsWindowVisible(dockWindow)||!dockRectangle.Contains(at))return false;var hit=GetAncestor(WindowFromPoint(at),2);return hit==dockWindow||hit==dockPlate.Handle;}
 void PreviewTick(Point cursor){
  if(DockPreview.PickerOpen){if(dockLabel!=null)dockLabel.Hide();return;}
  var at=new Point(cursor.X+screen.X,cursor.Y+screen.Y);var now=DateTime.UtcNow;
  Rectangle widgetBounds=Rectangle.Empty;string caption="";bool webHint=NativeHintHost.Hover(at,out widgetBounds,out caption)&&screen.Contains(new Point(widgetBounds.Left+widgetBounds.Width/2,widgetBounds.Top+widgetBounds.Height/2));string widget=webHint?"hint:"+caption:"";
  if(coordinator.WorkspaceOpen&&!webHint||coordinator.draggingDock||DockBackend.Interaction(root)){if(dockLabel!=null)dockLabel.Hide();if(preview!=null){preview.Dispose();preview=null;}hoverId="";hoverSince=DateTime.MinValue;return;}
  if(preview!=null&&!preview.IsDisposed&&preview.KeyboardMode){if(dockLabel!=null)dockLabel.Hide();return;}
  if(widget.Length==0&&screen.Contains(at)&&coordinator.stages!=null)widget=coordinator.stages.Hover(at,out widgetBounds,out caption);
  if(widget.Length==0&&screen.Contains(at))widget=desktopWidgets.Hover(at,out widgetBounds,out caption);
  if(widget.Length>0){
   string key="widget:"+widget;if(hoverId!=key){hoverId=key;hoverSince=now;if(dockLabel!=null)dockLabel.Hide();}
   if(preview!=null){preview.Dispose();preview=null;}
   if(webHint||(now-hoverSince).TotalMilliseconds>=250){if(dockLabel==null)dockLabel=new DockLabel(Handle,Send,()=>screen);dockLabel.Display(caption,widgetBounds,.5,paused);}return;
  }
  if(preview!=null&&!preview.IsDisposed&&preview.Bounds.Contains(at)){preview.Revive();if(dockLabel!=null)dockLabel.Hide();previewLeave=now.AddMilliseconds(400);return;}
  string id=DockPointerAt(at)?Read(instance=="primary"?"dock-hover.txt":"dock-hover-"+instance+".txt",""):"";
  if(id!=previewSuppressedId)previewSuppressedId="";
  double anchor;var labelEntry=backend.Entry(id);
  if(id.Length>0&&labelEntry!=null&&!coordinator.draggingDock&&!DockBackend.Interaction(root)&&DockLabel.TryAnchor(Read(instance=="primary"?"dock-label.txt":"dock-label-"+instance+".txt",""),id,out anchor)&&(preview==null||preview.IsDisposed)){
   if(dockLabel==null)dockLabel=new DockLabel(Handle,Send,()=>screen);dockLabel.Display(labelEntry.name,dockRectangle,anchor,paused);
  }else if(dockLabel!=null)dockLabel.Hide();
  if(id!=hoverId){hoverId=id;hoverSince=now;if(id.Length>0&&preview!=null&&preview.EntryId!=id){preview.Dismiss();preview=null;}}
  if(id.Length>0&&id!=previewSuppressedId){if(preview!=null&&!preview.IsDisposed&&preview.EntryId==id)preview.Revive();previewLeave=now.AddMilliseconds(200);if((now-hoverSince).TotalMilliseconds>=400&&(preview==null||preview.IsDisposed||preview.EntryId!=id)){if(preview!=null){preview.Dispose();preview=null;}var entry=backend.Entry(id);if(entry!=null&&!entry.system&&entry.windows.Count>0){if(dockLabel!=null)dockLabel.Hide();preview=new DockPreview(entry,dockRectangle,theme=="Astro");preview.Show();}}}
  else if(now>previewLeave&&preview!=null){preview.Dismiss();if(preview.IsDisposed)preview=null;}
 }
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;p.ExStyle&=~0x40000;return p;}}
 protected override void WndProc(ref Message m){if(primaryHost){if(runtime!=null)runtime.WindowMessage(ref m);if(rawPointer!=null)rawPointer.WindowMessage(m.Msg,m.LParam);if(hotkeys!=null&&hotkeys.Matches(m)){OpenWorkspace("apps");return;}if(m.Msg==0x7E||m.Msg==0x2E0){displaysRead=DateTime.MinValue;probe=DateTime.MinValue;}if(shellMessage!=0&&(uint)m.Msg==shellMessage&&backend!=null){long code=m.WParam.ToInt64();if(code==0x8006)backend.Attention(m.LParam.ToInt64());if(code==4||code==0x8004)DockBackend.ObserveActivation(m.LParam);}}if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}base.WndProc(ref m);}
 internal LiquidDesktop(DesktopSession session):this(session,null,null){}
 LiquidDesktop(DesktopSession session,LiquidDesktop owner,MonitorRecord display){
  this.session=session;
  pixels=new WebViewPixelScale(view,()=>{layoutKey="";probe=DateTime.MinValue;});
  primaryHost=owner==null;coordinator=owner??this;monitor=display??MonitorCatalog.Read().Find(item=>item.Primary);instance=primaryHost?"primary":monitor.Instance;
  pointerDelivery=new DesktopPointerDelivery(Send);
  preferences=session.Preferences;sharedData=session.SharedData;cursorTrail=primaryHost?new CursorTrail():null;monitorDock=new MonitorDock(root,instance){Dispatch=new RainmeterDispatch()};desktopWidgets=primaryHost?new DesktopWidgets(root){Dispatch=new RainmeterDispatch()}:owner.desktopWidgets;
  welcomePending=primaryHost&&preferences.IsFirstRun&&!preferences.Current.welcomeShown;
  lyrics=session.Lyrics;
  if(primaryHost){GlassWindow.Hint=NativeHintHost.Receive;lyrics.VisualChanged+=Broadcast;backend=new DockBackend(root);backend.Refresh();library=session.Library;preferences.Changed+=PreferencesChanged;
   windowMotion=new NativeWindowMotion(()=>preferences.Current.windowAnimations&&MotionAllowed(),WindowIcon);DockBackend.Animate=(entry,action,window)=>{if(action==DockClickAction.ChooseWindow&&DockPreview.TryPromote(entry))return true;ClosePreviews(entry.id);return windowMotion.Interact(entry,action,window);};DockBackend.AnimateRestore=windowMotion.Restore;
   stages=new StageManager(root,()=>windowMotion.Cancel());DockBackend.Activating=window=>{ClosePreviews(null);return stages.Activating(window,preferences.Current.windowAnimations);};DockPreview.MotionAllowed=MotionAllowed;
   bool stageClosing=false,stageClosed=false;
   FormClosing+=async(s,e)=>{
    if(stageClosed)return;e.Cancel=true;if(stageClosing)return;stageClosing=true;closing=true;session.Stopping();
    await Task.Yield();try{await stages.DrainClose();}finally{stageClosed=true;if(!IsDisposed)Close();}
   };
   FormClosed+=(s,e)=>{GlassWindow.Hint=null;DockBackend.Animate=null;DockBackend.AnimateRestore=null;DockBackend.Activating=null;DockPreview.MotionAllowed=null;windowMotion.Dispose();stages.Dispose();};
  }
  else backend=owner.backend;
  Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)??SystemIcons.Application;
  Text="PrismGlass Desktop";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
  screen=monitor.Screen.Bounds;Bounds=screen;Region=new Region(Rectangle.Empty);Enabled=false;
  view.Dock=DockStyle.Fill;Controls.Add(view);opticalBackdrop=GlassWindow.Configure(this,view);
  Shown+=async(s,e)=>{try{
   if(primaryHost){shellMessage=RegisterWindowMessage("SHELLHOOK");RegisterShellHookWindow(Handle);runtime=new RuntimePolicy(Handle);runtime.Changed+=RuntimeChanged;rawPointer=new DesktopRawMouseInput(Handle,RawPointerChanged);rawPointer.Enable(!runtime.Signals.locked&&!runtime.Signals.displayOff);SystemEvents.DisplaySettingsChanged+=DisplayChanged;hotkeys=new HotkeyBinding(Handle);preferences.PrepareHotkey=hotkeys.Prepare;try{hotkeys.Prepare(preferences.Current.hotkey);hotkeys.Commit();}catch(Exception ex){hotkeyError=ex.Message;}workspace=new WorkspaceWindow(root,library,preferences);workspace.SharedData=sharedData;workspace.Updates=session.Updates;workspace.SettingsChanged+=PreferencesChanged;SetupTray();}
   if(primaryHost&&preferences.Current.desktopWidgets.Count>0&&StartupWindowOrder.MaskVisible()&&!runtime.Signals.locked&&!runtime.Signals.displayOff&&MonitorCatalog.Read().Any(item=>preferences.ForMonitor(item.Id,item.Primary).enabled))desktopWidgets.Preload(preferences.Current.desktopWidgets);
   var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"runtime-data"));
   if(closing)return;await view.EnsureCoreWebView2Async(env);if(closing)return;SyncPixelScale();
   view.CoreWebView2.Settings.AreDevToolsEnabled=false;view.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;view.CoreWebView2.Settings.IsStatusBarEnabled=false;
   view.CoreWebView2.SetVirtualHostNameToFolderMapping("desktop.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
   view.CoreWebView2.ProcessFailed+=(ss,a)=>{if(WebViewRecovery.KeepAlive(root,a.ProcessFailedKind))return;File.WriteAllText(Diagnostic("error.txt"),"WebView2 process failed: "+a.ProcessFailedKind);Close();};
   view.CoreWebView2.NavigationStarting+=(ss,a)=>{Uri u;if(!Uri.TryCreate(a.Uri,UriKind.Absolute,out u)||u.Host!="desktop.local")a.Cancel=true;};
   view.CoreWebView2.NewWindowRequested+=(ss,a)=>a.Handled=true;
   view.CoreWebView2.WebMessageReceived+=async(ss,a)=>{
    if(!a.Source.StartsWith("https://desktop.local/",StringComparison.Ordinal))return;
    var data=a.TryGetWebMessageAsString();
    if(data=="ready"){pointerDelivery.Reset();ready=true;probe=DateTime.MinValue;BeginInvoke(new Action(()=>{if(!closing)Tick();}));}
    else if(data=="painted"){painted=true;probe=DateTime.MinValue;BeginInvoke(new Action(()=>{if(!closing){EnsureComponents();ApplyPreferences();}}));}
    else if(data.StartsWith("pointer-ack:",StringComparison.Ordinal)){int sequence;if(PointerActive&&Int32.TryParse(data.Substring(12),out sequence))pointerDelivery.Acknowledge(sequence);}
    else if(data.StartsWith("dock-icons:")){int sequence,alpha;if(MonitorDock.TryIconMessage(data.Substring(11),out sequence,out alpha)){bool applied=await Task.Run(()=>monitorDock.SetIconOpacity(alpha));if(applied&&!closing)Send(new{type="dock-icons-ack",sequence=sequence,alpha=alpha});}}
    else if(data.StartsWith("dock-shape:")){dockOutline=json.Deserialize<float[][]>(data.Substring(11));dockPlate.Outline(dockOutline);UpdateVisibleRegion();}
    else if(data.StartsWith("widget-shapes:")){var outlines=json.Deserialize<float[][][]>(data.Substring(14));widgetRegion.MakeEmpty();foreach(var outline in outlines){if(outline.Length<3)continue;using(var path=new GraphicsPath()){var points=new PointF[outline.Length];for(int i=0;i<points.Length;i++)points[i]=new PointF(outline[i][0],outline[i][1]);path.AddPolygon(points);using(var clip=GlassWindow.Clip(path,ClientRectangle))widgetRegion.Union(clip);}}UpdateVisibleRegion();}
    else if(data.StartsWith("error:")){File.WriteAllText(Diagnostic("error.txt"),data);Close();}
    else if(data.StartsWith("stats:"))File.WriteAllText(Diagnostic("render-stats.json"),data.Substring(6));
   };
   view.Source=new Uri("https://desktop.local/index.html");
   timer.Interval=8;timer.Tick+=(ss,a)=>Tick();timer.Start();
  }catch(Exception ex){if(!closing){File.WriteAllText(Diagnostic("error.txt"),ex.ToString());Close();}}};
  FormClosed+=(s,e)=>{if(primaryHost)DockPreview.CloseActive();if(dockLabel!=null)dockLabel.Dispose();closing=true;if(rawPointer!=null)rawPointer.Dispose();pointerDelivery.Reset();timer.Stop();timer.Dispose();if(primaryHost){SystemEvents.DisplaySettingsChanged-=DisplayChanged;preferences.Changed-=PreferencesChanged;preferences.PrepareHotkey=null;SystemTray.Cancel();DeregisterShellHookWindow(Handle);backend.CancelDrag();if(tray!=null){tray.Visible=false;tray.ContextMenuStrip.Dispose();tray.Dispose();}if(runtime!=null)runtime.Dispose();if(hotkeys!=null)hotkeys.Dispose();if(workspace!=null)workspace.Dispose();foreach(var host in new List<LiquidDesktop>(displays.Values))host.Close();displays.Clear();lyrics.VisualChanged-=Broadcast;lyrics.DesktopMediaActive=false;cursorTrail.Dispose();desktopWidgets.Dispose();HideLegacyTopbar();}tearingDown=true;if(sideDock!=null)sideDock.Dispose();if(topIsland!=null)topIsland.Dispose();if(fileShelf!=null)fileShelf.Dispose();if(preview!=null)preview.Dispose();monitorDock.Dispose();dockPlate.Dispose();view.Dispose();if(primaryHost){RestoreIcons();Taskbar(true);}try{File.Delete(Diagnostic("heartbeat.txt"));File.Delete(Path.Combine(root,"dock-motion-"+instance+".txt"));}catch{}};
 }
 string Read(string file,string fallback){try{return File.ReadAllText(Path.Combine(root,file)).Trim();}catch{return fallback;}}
 string Diagnostic(string name){return Path.Combine(root,instance=="primary"?name:Path.GetFileNameWithoutExtension(name)+"-"+instance+Path.GetExtension(name));}
 bool WorkspaceOpen {get{return workspace!=null&&!workspace.IsDisposed&&workspace.IsOpen;}}
 IEnumerable<LiquidDesktop> Hosts(){yield return this;foreach(var host in displays.Values)yield return host;}
 void Broadcast(object value){foreach(var host in Hosts())host.Send(value);}
 bool MotionAllowed(){return !closing&&RuntimePolicy.Reasons(preferences.Current,runtime==null?new RuntimeSignals():runtime.Signals,false,false,systemAnimations).Length==0;}
 Rectangle WindowIcon(DockEntry entry,DockWindow window){
  var display=window==null?Screen.FromPoint(Cursor.Position):Screen.FromHandle(new IntPtr(window.handle));
  foreach(var host in Hosts())if(host.monitor.Screen.DeviceName==display.DeviceName&&host.dockWindow!=IntPtr.Zero&&preferences.ForMonitor(host.monitor.Id,host.monitor.Primary).enabled&&preferences.ForMonitor(host.monitor.Id,host.monitor.Primary).dock){
   try{var entries=json.Deserialize<List<DockEntry>>(File.ReadAllText(Path.Combine(root,"dock-state.json")));var displayed=DockBackend.DisplayEntries(entries,"");
    int index=displayed.FindIndex(e=>entry!=null&&e.id==entry.id||window!=null&&e.windows.Exists(w=>w.handle==window.handle&&w.pid==window.pid));if(index<0)return Rectangle.Empty;
    string suffix=host.instance=="primary"?"":"-"+host.instance;var anchors=json.Deserialize<Dictionary<string,float[]>>(File.ReadAllText(Path.Combine(root,"dock-anchors"+suffix+".json")));float[] rect;
    if(!anchors.TryGetValue(displayed[index].id,out rect)||rect.Length!=4||Array.Exists(rect,v=>Single.IsNaN(v)||Single.IsInfinity(v))||rect[2]<=0||rect[3]<=0)return Rectangle.Empty;
    double dpi=MonitorDock.PixelScale(host.monitor.Screen.WorkingArea);var dockBounds=host.dockRectangle;return new Rectangle(dockBounds.X+(int)(rect[0]*dpi),dockBounds.Y+(int)(rect[1]*dpi),(int)(rect[2]*dpi),(int)(rect[3]*dpi));
   }catch(IOException){}catch(ArgumentException){}return Rectangle.Empty;
  }return Rectangle.Empty;
 }
 void RuntimeChanged(){if(closing||!IsHandleCreated)return;try{BeginInvoke(new Action(()=>{if(rawPointer!=null)rawPointer.Enable(!runtime.Signals.locked&&!runtime.Signals.displayOff);if(runtime.Signals.locked||runtime.Signals.displayOff)DockPreview.CloseActive();foreach(var host in Hosts()){host.pointerDelivery.Reset();host.probe=DateTime.MinValue;if(runtime.Signals.locked||runtime.Signals.displayOff){if(host.dockLabel!=null){host.dockLabel.Dispose();host.dockLabel=null;}if(host.preview!=null){host.preview.Dispose();host.preview=null;}host.hoverId="";host.hoverSince=DateTime.MinValue;}}}));}catch(InvalidOperationException){}}
 void RefreshDisplayBindings(List<MonitorRecord> catalog){
  WallpaperRefresh.BindDisplays(catalog);
  failedDisplays.RemoveWhere(id=>!catalog.Exists(item=>StringComparer.OrdinalIgnoreCase.Equals(item.Id,id)));
 }
 void DisplayChanged(object sender,EventArgs args){if(closing||!IsHandleCreated)return;try{var connected=MonitorCatalog.Read();BeginInvoke(new Action(()=>{if(closing)return;RefreshDisplayBindings(connected);ClosePreviews(null);backend.CancelDrag();if(stages!=null)stages.DisplaysChanged();ComponentPosition.DisplaysChanged();desktopWidgets.DisplaysChanged();foreach(var host in Hosts()){host.monitorDock.DisplaysChanged();host.dockPlate.ResetComposition();host.pointerDelivery.Reset();host.layoutKey="";host.probe=DateTime.MinValue;}displaysRead=DateTime.MinValue;}));}catch(InvalidOperationException){}}
 void PreferencesChanged(){if(!primaryHost){coordinator.PreferencesChanged();return;}if(hotkeys!=null){try{hotkeys.Configure(preferences.Current.hotkey);hotkeyError="";}catch(Exception ex){hotkeyError=ex.Message;}}failedDisplays.Clear();foreach(var host in Hosts()){host.probe=DateTime.MinValue;host.ApplyPreferences();}displaysRead=DateTime.MinValue;}
 void ApplyPreferences(){if(closing)return;var snapshot=preferences.Snapshot();ComponentPosition.LayoutLocked=preferences.Current.layoutLocked;if(primaryHost&&desktopWidgets!=null)desktopWidgets.LockLayout(preferences.Current.layoutLocked);Send(new{type="preferences",snapshot=snapshot});if(sideDock!=null&&!sideDock.IsDisposed)sideDock.ApplyPreferences(snapshot);if(topIsland!=null&&!topIsland.IsDisposed)topIsland.ApplyPreferences(snapshot);if(fileShelf!=null&&!fileShelf.IsDisposed)fileShelf.ApplyPreferences(snapshot);preferenceRevision=preferences.Revision;}
 internal void OpenWorkspace(string page){if(closing)return;if(!primaryHost){coordinator.OpenWorkspace(page);return;}if(page=="companion"){CompanionSession.Open(root);return;}ClosePreviews(null);if(workspace==null||workspace.IsDisposed){workspace=new WorkspaceWindow(root,library,preferences);workspace.SharedData=sharedData;workspace.Updates=session.Updates;workspace.SettingsChanged+=PreferencesChanged;runtimeKey="";}var targetScreen=Screen.FromPoint(Cursor.Position);Rectangle targetDock=Rectangle.Empty;foreach(var host in Hosts()){if(host.monitor.Screen.DeviceName==targetScreen.DeviceName)targetDock=host.dockRectangle;if(host.sideDock!=null&&!host.sideDock.IsDisposed)host.sideDock.DismissTransient();if(host.topIsland!=null&&!host.topIsland.IsDisposed)host.topIsland.DismissTransient();if(host.fileShelf!=null&&!host.fileShelf.IsDisposed)host.fileShelf.DismissTransient();if(host.preview!=null){host.preview.Dispose();host.preview=null;}}workspace.OpenPage(page,targetScreen,targetDock);}
 void ExitDesktop(){
  try{var script=Path.GetFullPath(Path.Combine(root,"..","DesktopMode.ps1"));Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \""+script+"\" -Mode Game -ShowErrors"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});}
  catch(Exception ex){tray.ShowBalloonTip(3000,"PrismGlass 未能退出",ex.Message,ToolTipIcon.Warning);}
 }
 void SetupTray(){if(!primaryHost||tray!=null)return;var menu=NativeGlassMaterial.CreateMenu();menu.Items.Add("应用中心",null,(s,e)=>OpenWorkspace("apps"));menu.Items.Add("PrismGlass 设置",null,(s,e)=>OpenWorkspace("settings"));menu.Items.Add("iPad 伴随屏",null,(s,e)=>OpenWorkspace("companion"));menu.Items.Add("暂停 / 恢复动效",null,(s,e)=>{try{preferences.Apply(new Dictionary<string,object>{{"manualPaused",!preferences.Current.manualPaused}},preferences.Revision);}catch(Exception ex){tray.ShowBalloonTip(3000,"PrismGlass",ex.Message,ToolTipIcon.Warning);}});menu.Items.Add("退出 PrismGlass",null,(s,e)=>ExitDesktop());menu.Items.Add("检查更新",null,(s,e)=>OpenWorkspace("updates"));menu.Items.Add("关于 PrismGlass",null,(s,e)=>MessageBox.Show(this,"PrismGlass Desktop "+Application.ProductVersion+"\n免费开源 · MIT\nhttps://github.com/liburce0412-alt/prismglass-desktop","PrismGlass",MessageBoxButtons.OK,MessageBoxIcon.Information));tray=new NotifyIcon{Icon=Icon,Text="PrismGlass",Visible=true,ContextMenuStrip=menu};tray.DoubleClick+=(s,e)=>OpenWorkspace("apps");}
 void ReconcileDisplays(){
  if(!primaryHost||reconciling||closing)return;reconciling=true;
  try{var catalog=MonitorCatalog.Read();RefreshDisplayBindings(catalog);var primary=catalog.Find(item=>item.Primary);if(primary!=null)monitor=primary;var wanted=new Dictionary<string,MonitorRecord>(StringComparer.OrdinalIgnoreCase);foreach(var item in catalog)if(!item.Primary&&preferences.ForMonitor(item.Id,false).enabled&&!failedDisplays.Contains(item.Id))wanted[item.Id]=item;
   foreach(var pair in new List<KeyValuePair<string,LiquidDesktop>>(displays)){MonitorRecord connected;if(!wanted.TryGetValue(pair.Key,out connected)||connected.Instance!=pair.Value.instance||pair.Value.IsDisposed){displays.Remove(pair.Key);if(!pair.Value.IsDisposed)pair.Value.Close();}else{pair.Value.monitor=connected;pair.Value.probe=DateTime.MinValue;}}
   foreach(var pair in wanted)if(!displays.ContainsKey(pair.Key)){var host=new LiquidDesktop(session,this,pair.Value);displays.Add(pair.Key,host);host.Show();}
   monitorError="";
  }catch(Exception ex){monitorError=ex.Message;}finally{reconciling=false;}
 }
 void PublishRuntime(){
  if(!primaryHost)return;
  var signals=runtime==null?new RuntimeSignals():runtime.Signals;
  LiveGlassBackdrop.Configure(root,preferences.Current.liveGlassBackground,RuntimePolicy.Reasons(preferences.Current,signals,false,false,systemAnimations).Length>0);
  var startup=new List<object>();bool allReady=painted,anyEnabled=false;
  foreach(var item in MonitorCatalog.Read()){
   LiquidDesktop host=null;if(item.Primary)host=this;else displays.TryGetValue(item.Id,out host);bool available=host!=null&&!host.IsDisposed&&!host.closing;
   var options=preferences.ForMonitor(item.Id,item.Primary);bool rendererReady=available&&host.painted,leftReady=available&&host.sideDock!=null&&!host.sideDock.IsDisposed&&host.sideDock.IsReady,topReady=available&&host.islandReady,rightReady=available&&host.shelfReady,dockPresent=available&&host.dockWindow!=IntPtr.Zero,dockReady=available&&host.monitorDock.Responsive(host.dockWindow);
   bool displayReady=DesktopStartupReadiness.DisplayReady(options.enabled,rendererReady,options.left,leftReady,options.top,topReady,options.right,rightReady,options.dock,dockReady);allReady&=displayReady;anyEnabled|=options.enabled;
   startup.Add(new{id=item.Id,enabled=options.enabled,ready=displayReady,rendererReady=rendererReady,leftReady=leftReady,topReady=topReady,rightReady=rightReady,dockPresent=dockPresent,dockReady=dockReady,dockLastResponseUtc=available?host.monitorDock.LastResponse.ToString("o"):null});
  }
  componentsReady=allReady;
  bool widgetsReady=desktopWidgets.StartupReady(preferences.Current.desktopWidgets,anyEnabled&&!signals.locked&&!signals.displayOff,preferences.Current.autoHideWidgets,WidgetCovered,WidgetScreenEnabled);
  var primary=preferences.ForMonitor(monitor.Id,true);bool taskbarHiddenExpected=primary.enabled&&primary.dock&&!SystemTray.KeepVisible;
  bool desktopReady=DesktopStartupReadiness.CanHandoff(allReady,widgetsReady,!preferences.Current.stageManager||stageConfigured);
  DesktopSettingsFiles.Write(Path.Combine(root,"readiness.json"),json.Serialize(new{version=1,buildId=Application.ProductVersion,pid=ownPid,window=Handle.ToInt64(),ready=desktopReady,widgetsReady=widgetsReady,stageConfigured=stageConfigured,primaryEnabled=primary.enabled,taskbarHiddenExpected=taskbarHiddenExpected,monitors=startup,monitorError=monitorError,dockCleanupError=MonitorDock.CleanupError,stageError=stages==null?"":stages.Error,windowError=windowMotion==null?"":windowMotion.Error,glassError=String.IsNullOrEmpty(LiveGlassBackdrop.Error)?(String.IsNullOrEmpty(GlassWindow.RecoveryError)?WallpaperRefresh.MonitorError:GlassWindow.RecoveryError):LiveGlassBackdrop.Error,glassCapture=SharedWindowCapture.Diagnostics,stageGroups=stages==null?0:stages.Groups.Count,stageVisible=stages!=null&&System.Linq.Enumerable.Any(stages.Strips,s=>s.IsVisible)}));
  if(desktopReady)session.Running();
  initialReadyPublished|=desktopReady;
  if(workspace==null||workspace.IsDisposed)return;var monitors=new List<object>();foreach(var host in Hosts())monitors.Add(new{id=host.monitor.Id,name=host.monitor.Name,reasons=host.pauseReasons});var snapshot=new{monitors=monitors,hotkeyError=hotkeyError,monitorError=monitorError,stageError=stages==null?"":stages.Error,windowError=windowMotion==null?"":windowMotion.Error,glassError=String.IsNullOrEmpty(LiveGlassBackdrop.Error)?(String.IsNullOrEmpty(GlassWindow.RecoveryError)?WallpaperRefresh.MonitorError:GlassWindow.RecoveryError):LiveGlassBackdrop.Error};string key=json.Serialize(snapshot);if(key!=runtimeKey){runtimeKey=key;workspace.PublishRuntime(snapshot,RuntimePolicy.Reasons(preferences.Current,runtime==null?new RuntimeSignals():runtime.Signals,false,false,systemAnimations).Length>0);}
 }
 void Send(object value){if(view.CoreWebView2!=null&&!closing)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(value));}
 bool PointerActive {get{return ready&&!closing&&!paused&&(coordinator.runtime==null||!coordinator.runtime.Signals.locked&&!coordinator.runtime.Signals.displayOff);}}
 void PublishPointer(Point cursor,bool down){if(!PointerActive){pointerDelivery.Reset();return;}pointerDelivery.Offer(cursor,down);}
 void PublishDockPointer(Point at){monitorDock.Pointer(!coordinator.WorkspaceOpen&&!coordinator.draggingDock&&DockPointerAt(at)?(at.X-dockRectangle.Left)/(double)Math.Max(1,dockRectangle.Width):-1);}
 void RawPointerChanged(){if(closing||runtime==null||runtime.Signals.locked||runtime.Signals.displayOff)return;POINT p;if(!GetCursorPos(out p))return;var at=new Point(p.X,p.Y);bool down=(GetAsyncKeyState(1)&0x8000)!=0;foreach(var host in Hosts())try{var bounds=host.screen;host.PublishPointer(new Point(at.X-bounds.X,at.Y-bounds.Y),down);host.PublishDockPointer(at);}catch(InvalidOperationException){host.pointerDelivery.Reset();rawPointer.Enable(false);return;}}
 void Tick(){
  if(!ready||closing||busy)return;busy=true;
  try{
   var now=DateTime.UtcNow;
   if((now-windowProbe).TotalMilliseconds>=150){windowProbe=now;if(primaryHost)RefreshWindowCoverage();UpdateComponentVisibility();}
   bool widgetsDragging=desktopWidgets.Dragging,widgetsMoved=widgetsDragging||widgetsWereDragging;widgetsWereDragging=widgetsDragging;
   if((now-probe).TotalSeconds>=1){probe=now;Probe();if(primaryHost)sharedData.SyncWorkWidgets();}else if(widgetsMoved)Probe(true);
   POINT p;GetCursorPos(out p);var cursor=new Point(p.X-screen.X,p.Y-screen.Y);
   if(primaryHost){cursorTrail.Update(new Point(p.X,p.Y),RuntimePolicy.Reasons(preferences.Current,runtime==null?new RuntimeSignals():runtime.Signals,false,false,systemAnimations).Length==0);
   if((now-displaysRead).TotalSeconds>=5){displaysRead=now;ReconcileDisplays();}
   if(welcomePending&&painted&&File.Exists(Path.Combine(root,"desktop-ready.flag"))){welcomePending=false;OpenWorkspace("welcome");preferences.Apply(new Dictionary<string,object>{{"welcomeShown",true}},preferences.Revision);}
   if((now-requestsRead).TotalMilliseconds>=100){requestsRead=now;
   foreach(var request in Directory.GetFiles(root,"dock-action-*.json")){try{var data=json.Deserialize<Dictionary<string,object>>(DesktopSettingsFiles.Read(request));File.Delete(request);object id,force;if(data!=null&&data.TryGetValue("id",out id)&&id is string&&data.TryGetValue("force",out force)&&force is bool)DockBackend.Click(root,(string)id,(bool)force);}catch(IOException){}catch(ArgumentException){}}
   bool movingComponent=widgetsDragging||draggingDock||System.Linq.Enumerable.Any(Hosts(),host=>(host.sideDock!=null&&host.sideDock.IsRepositioning)||(host.topIsland!=null&&host.topIsland.IsRepositioning)||(host.fileShelf!=null&&host.fileShelf.IsRepositioning));
   var signals=runtime==null?new RuntimeSignals():runtime.Signals;var pointer=Cursor.Position;var edgeHost=Hosts().FirstOrDefault(h=>h.stageEdgeEligible&&h.monitor.Screen.Bounds.Contains(pointer)&&(h.sideDock==null||!h.sideDock.PanelOpen)&&(h.fileShelf==null||!h.fileShelf.PanelOpen));
   stages.EdgeReveal(componentsReady&&preferences.Current.stageManager&&!signals.locked&&!signals.displayOff&&!WorkspaceOpen&&!movingComponent&&edgeHost!=null?edgeHost.monitor.Screen.DeviceName:null,edgeHost==null?Rectangle.Empty:edgeHost.monitor.Screen.WorkingArea,pointer);stages.Configure(preferences.Current.stageManager,!componentsReady||signals.locked||signals.displayOff||WorkspaceOpen||movingComponent,MotionAllowed(),StageAreas(),Hosts().Where(host=>host.stageFullscreen||(host.sideDock!=null&&host.sideDock.PanelOpen)||(host.fileShelf!=null&&host.fileShelf.PanelOpen)).Select(host=>host.monitor.Screen.DeviceName),preferences.Current.stageGroupLayout,preferences.Current.stageSingleSize,preferences.Current.stageMaxWidth,preferences.Current.stageMaxHeight);stageConfigured=componentsReady;
   var workspaceRequest=Path.Combine(root,"workspace.request");if(File.Exists(workspaceRequest)){string page=Read("workspace.request","");File.Delete(workspaceRequest);if(page=="apps"||page=="toggle-apps"||page=="settings"||page=="components"||page=="glass"||page=="weather"||page=="welcome"||page=="companion")OpenWorkspace(page);}
   var dockNotice=Path.Combine(root,"dock-notice.txt");if(File.Exists(dockNotice)){string notice=Read("dock-notice.txt","");File.Delete(dockNotice);if(tray!=null&&notice.Length>0)tray.ShowBalloonTip(6000,"PrismGlass",notice,ToolTipIcon.Warning);}
   var shelfRequest=Path.Combine(root,"shelf.request");if(File.Exists(shelfRequest)){string requested=Read("shelf.request","");File.Delete(shelfRequest);if(requested=="manage"||requested=="tasks"||requested=="countdown"){if(workspace!=null&&!workspace.IsDisposed)workspace.Hide();var target=this;foreach(var host in Hosts())if(host.monitor.Screen.Bounds.Contains(new Point(p.X,p.Y))){target=host;break;}target.legacyManagerOpen=requested=="manage";target.pendingShelfContent=requested=="manage"?"":requested;target.EnsureComponents();if(target.fileShelf!=null&&target.shelfReady){if(requested=="manage")target.fileShelf.OpenLegacyManager();else target.fileShelf.OpenContent(requested);}}}
   var panelRequest=Path.Combine(root,"panel.request");
   if(File.Exists(panelRequest)){string mode=Read("panel.request","");File.Delete(panelRequest);if(sideDock!=null&&!sideDock.IsDisposed&&(mode=="battery"||mode=="network"||mode=="both"||mode=="calendar"))sideDock.Open(mode,new Point(p.X,p.Y));}
   var widgetRequest=Path.Combine(root,"widget.request");if(File.Exists(widgetRequest)){string name=Read("widget.request","");File.Delete(widgetRequest);if(sideDock!=null&&!sideDock.IsDisposed)sideDock.Open(name.ToLowerInvariant(),new Point(p.X,p.Y));}
   var trayRequest=Path.Combine(root,"tray.request");if(File.Exists(trayRequest)){File.Delete(trayRequest);if(topIsland!=null&&!topIsland.IsDisposed)topIsland.OpenTray();}
   }
   DockBackend.ObserveForeground();var dockAreas=new Dictionary<string,Rectangle>();foreach(var host in Hosts())if(host.dockWindow!=IntPtr.Zero)dockAreas[host.instance]=host.dockRectangle;draggingDock=backend.TickDrag(dockAreas);
   }
   if(painted)DockVisibility(cursor);
   if(painted&&primaryHost){bool near=WorkspaceOpen||draggingDock||DockBackend.Interaction(root),anyVisible=false,musicOpen=false;foreach(var host in Hosts()){near|=Rectangle.Inflate(host.dockRectangle,24,32).Contains(new Point(p.X,p.Y))||(host.preview!=null&&!host.preview.IsDisposed);anyVisible|=host.dockShown&&host.dockWindow!=IntPtr.Zero&&!host.screenCovered;musicOpen|=host.sideDock!=null&&!host.sideDock.IsDisposed&&host.sideDock.MusicOpen;}lyrics.DesktopMediaActive=musicOpen;lyrics.Tick(now,anyVisible,near,RuntimePolicy.Reasons(preferences.Current,runtime==null?new RuntimeSignals():runtime.Signals,false,false,systemAnimations).Length>0);}
   if(painted&&ticks%5==0)PreviewTick(cursor);
   bool down=(GetAsyncKeyState(1)&0x8000)!=0;
   PublishDockPointer(new Point(p.X,p.Y));
   PublishPointer(cursor,down);
   bool heartbeatDue=painted&&(now-beat).TotalSeconds>=2;
   if(heartbeatDue){beat=now;File.WriteAllText(Diagnostic("heartbeat.txt"),((long)(now-new DateTime(1970,1,1)).TotalSeconds)+" "+theme);}
   if(primaryHost&&painted&&DesktopStartupReadiness.PublishDue(initialReadyPublished,heartbeatDue,now-readinessProbe)){
    readinessProbe=now;PublishRuntime();
   }
   if(++ticks%150==0)Send(new {type="stats"});
  }catch(Exception ex){File.WriteAllText(Diagnostic("error.txt"),ex.ToString());}finally{busy=false;}
 }
 Dictionary<string,Rectangle> StageAreas(){var areas=new Dictionary<string,Rectangle>();foreach(var host in Hosts()){if(!preferences.ForMonitor(host.monitor.Id,host.monitor.Primary).enabled)continue;var area=Rectangle.Inflate(host.monitor.Screen.WorkingArea,-8,-8);areas[host.monitor.Screen.DeviceName]=StageManager.AvailableArea(area,new[]{host.dockShown?host.dockRectangle:Rectangle.Empty,host.sideDock!=null&&host.sideDock.Visible?host.sideDock.OcclusionBounds:Rectangle.Empty,host.topIsland!=null&&host.topIsland.Visible?host.topIsland.OcclusionBounds:Rectangle.Empty,host.fileShelf!=null&&host.fileShelf.Visible?host.fileShelf.OcclusionBounds:Rectangle.Empty});}return areas;}
 bool hidingLegacyTopbar;
 async void HideLegacyTopbar(){
  if(!primaryHost)return;
  if(legacyTopHidden||hidingLegacyTopbar&&!closing)return;hidingLegacyTopbar=true;
  try{Func<bool> hide=()=>{RainmeterCommands.Send(RuntimePaths.Rainmeter,"!DeactivateConfig \"PrismGlass\\Topbar\"",50);return true;};if(closing)hide();else await Task.Run(hide);legacyTopHidden=true;layoutKey="";}catch(Exception ex){monitorError=ex.Message;}finally{hidingLegacyTopbar=false;}
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
  if(component==1){islandReady=false;HideLegacyTopbar();}if(component==2)shelfReady=false;
  if(componentRecovery.Exhausted(component)){
   if(!primaryHost){coordinator.failedDisplays.Add(monitor.Id);coordinator.monitorError=monitor.Name+" 的组件反复启动失败，已释放此屏实例；重连显示器或修改显示器设置后可重试。";Close();return;}
   try{File.WriteAllText(Path.Combine(root,"component-recovery.txt"),"Desktop component failed three times; restoring ordinary desktop.");
    var script=Path.GetFullPath(Path.Combine(root,"..","DesktopMode.ps1"));
    Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+script+"\" -Mode Recovery"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
   }catch{}finally{Close();}
  }
 }
 void EnsureComponents(){
  if(closing)return;var now=DateTime.UtcNow;var options=preferences.ForMonitor(monitor.Id,monitor.Primary);bool left=options.enabled&&options.left,top=options.enabled&&options.top,right=options.enabled&&options.right||legacyManagerOpen||pendingShelfContent.Length>0;
  tearingDown=true;try{if(!left&&sideDock!=null){sideDock.Dispose();sideDock=null;}if(!top&&topIsland!=null){topIsland.Dispose();topIsland=null;islandReady=false;}if(!right&&fileShelf!=null){fileShelf.Dispose();fileShelf=null;shelfReady=false;}}finally{tearingDown=false;}
  if(!top)HideLegacyTopbar();
  if(left&&(sideDock==null||sideDock.IsDisposed)&&componentRecovery.CanStart(0,now)){
   sideDock=new SideDock(root,false,sharedData,instance);sideDock.FormClosed+=(s,e)=>ComponentFailed(0);sideDock.Sync(theme,monitor.Screen.WorkingArea,IntPtr.Zero,paused);sideDock.ApplyPreferences(preferences.Snapshot());sideDock.Show();
  }
  if(top&&(topIsland==null||topIsland.IsDisposed)&&componentRecovery.CanStart(1,now)){
   islandReady=false;topIsland=new TopIsland(root,false,sharedData,instance);topIsland.Ready+=()=>{islandReady=true;HideLegacyTopbar();topIsland.ApplyPreferences(preferences.Snapshot());};topIsland.CompanionStopRequested+=()=>DesktopSession.Request(root,CompanionSession.ControlsRequest);topIsland.FormClosed+=(s,e)=>ComponentFailed(1);topIsland.Sync(theme,monitor.Screen.WorkingArea,IntPtr.Zero,paused);topIsland.Show();
  }
  if(right&&(fileShelf==null||fileShelf.IsDisposed)&&componentRecovery.CanStart(2,now)){
   shelfReady=false;fileShelf=new FileShelf(root,false,sharedData,instance);fileShelf.Ready+=()=>{shelfReady=true;fileShelf.ApplyPreferences(preferences.Snapshot());if(legacyManagerOpen)fileShelf.OpenLegacyManager();else if(pendingShelfContent.Length>0)fileShelf.OpenContent(pendingShelfContent);};fileShelf.CursorSettingsChanged+=(settings,size)=>coordinator.cursorTrail.Configure(settings,size);fileShelf.FormClosed+=(s,e)=>ComponentFailed(2);fileShelf.Sync(theme,monitor.Screen.WorkingArea,IntPtr.Zero,paused);fileShelf.Show();
  }
 }
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetWindowLong(IntPtr h,int index);
 internal static void PlaceComponent(Form component,IntPtr anchor){
  DesktopVisibilityPolicy.KeepInFront(component);
 }
 bool WidgetCovered(Rectangle bounds){return DesktopVisibilityPolicy.OverlapsComponent(desktopWindows,bounds);}
 bool WidgetScreenEnabled(Rectangle bounds){
  string screenName=Screen.FromRectangle(bounds).DeviceName;
  foreach(var host in Hosts())if(host.monitor.Screen.DeviceName==screenName)return preferences.ForMonitor(host.monitor.Id,host.monitor.Primary).enabled;
  return false;
 }
 void UpdateComponentVisibility(){
  var options=preferences.ForMonitor(monitor.Id,monitor.Primary);var enabled=new[]{options.left,options.top,options.right};var signals=coordinator.runtime==null?new RuntimeSignals():coordinator.runtime.Signals;
  var components=new Form[]{sideDock,topIsland,fileShelf};
  var bounds=new[]{sideDock==null?Rectangle.Empty:sideDock.OcclusionBounds,topIsland==null?Rectangle.Empty:topIsland.OcclusionBounds,fileShelf==null?Rectangle.Empty:fileShelf.OcclusionBounds};
  var hidden=new bool[3];
  var autoHide=new[]{preferences.Current.leftAutoHide,preferences.Current.topAutoHide,preferences.Current.rightAutoHide};
  var moving=new[]{sideDock!=null&&sideDock.IsRepositioning,topIsland!=null&&topIsland.IsRepositioning,fileShelf!=null&&fileShelf.IsRepositioning};
  for(int i=0;i<components.Length;i++){
   var component=components[i];if(component==null||component.IsDisposed)continue;
   bool forceManager=i==2&&(legacyManagerOpen||pendingShelfContent.Length>0);hidden[i]=DesktopVisibilityPolicy.ComponentHidden(options.enabled&&enabled[i],signals.locked,signals.displayOff,!forceManager&&!moving[i]&&autoHide[i],screenCovered||DesktopVisibilityPolicy.OverlapsComponent(desktopWindows,bounds[i]));
   bool initialized=i==0?sideDock.IsReady:i==1?islandReady:shelfReady;
   DesktopVisibilityPolicy.SetComponentVisible(component,hidden[i],initialized);
  }
  if(primaryHost)try{if(desktopWidgets.Sync(preferences.Current.desktopWidgets,!signals.locked&&!signals.displayOff,preferences.Current.autoHideWidgets,WidgetCovered,WidgetScreenEnabled,b=>{foreach(var host in Hosts())if(host.monitor.Screen.DeviceName==Screen.FromRectangle(b).DeviceName)return host.pauseReasons.Length>0||host.screenCovered;return true;}))foreach(var host in Hosts())host.Probe(true);}catch(RainmeterDispatch.Pending){}catch(Exception ex){monitorError=ex.Message;}
  string key=json.Serialize(new{hidden=hidden,screenCovered=screenCovered,bounds=bounds});
  if(key!=componentVisibility){componentVisibility=key;File.WriteAllText(Diagnostic("component-visibility.json"),json.Serialize(new{leftHidden=hidden[0],topHidden=hidden[1],rightHidden=hidden[2],screenCovered=screenCovered,bounds=bounds,updated=DateTime.UtcNow.ToString("o")}));}
 }
 [DllImport("shell32.dll")]static extern int SHQueryUserNotificationState(out int state);
 void RefreshWindowCoverage(){
  var windows=new List<DesktopWindowState>();var foreground=GetAncestor(GetForegroundWindow(),3);
  EnumWindows((h,p)=>{
   uint pid;GetWindowThreadProcessId(h,out pid);bool settings=pid==ownPid&&coordinator.workspace!=null&&!coordinator.workspace.IsDisposed&&coordinator.workspace.IsHandleCreated&&h==coordinator.workspace.Handle;
   if(pid==ownPid&&!settings||!IsWindowVisible(h)||IsIconic(h))return true;
   int cloaked;if(DwmGetWindowAttribute(h,14,out cloaked,4)==0&&cloaked!=0)return true;
   var cl=new StringBuilder(256);GetClassName(h,cl,256);string name=cl.ToString();
   if(!DesktopVisibilityPolicy.IsApplicationSurface(GetWindowLong(h,-20),name))return true;
   if(name=="Progman"||name=="WorkerW"||name=="RainmeterMeterWindow"||name=="Shell_TrayWnd"||name=="Shell_SecondaryTrayWnd")return true;
   RECT r;if(DwmGetWindowAttribute(h,9,out r,Marshal.SizeOf(typeof(RECT)))!=0&&!GetWindowRect(h,out r))return true;
   windows.Add(new DesktopWindowState{Bounds=Rectangle.FromLTRB(r.L,r.T,r.R,r.B),Application=true,Visible=true,Minimized=false,Cloaked=false,Maximized=IsZoomed(h),Style=GetWindowLong(h,-16),Foreground=!settings&&h==foreground});return true;
  },IntPtr.Zero);
  int notification;bool exclusive=SHQueryUserNotificationState(out notification)==0&&(notification==3||notification==4);
  foreach(var host in Hosts()){host.desktopWindows=windows;host.screenCovered=DesktopVisibilityPolicy.AnyCovered(windows,host.screen,host.monitor.Screen.WorkingArea);host.stageFullscreen=preferences.Current.stageHideCovered&&DesktopVisibilityPolicy.StageCovered(windows,host.screen,host.monitor.Screen.WorkingArea);host.stageEdgeEligible=!exclusive&&host.stageFullscreen&&DesktopVisibilityPolicy.StageEdgeEligible(windows,host.screen,host.monitor.Screen.WorkingArea);}
 }
 void SyncPixelScale(){pixels.Sync();}
 bool readingCompanion;
 async void PublishCompanionState(){
  if(closing||readingCompanion)return;readingCompanion=true;
  try{var state=await Task.Run(()=>CompanionProcessState.Read(root));if(!closing)foreach(var host in Hosts())if(host.topIsland!=null&&!host.topIsland.IsDisposed)host.topIsland.ApplyCompanionState(new{running=state.running,connected=state.connected,screen=state.screen,pointer=state.pointer,audio=state.audio});}finally{readingCompanion=false;}
 }
 void Probe(bool positionOnly=false){
  if(!positionOnly){SyncPixelScale();SystemParametersInfo(0x1042,0,out systemAnimations,0);}
  if(primaryHost&&!positionOnly){preferences.Reload();if(runtime!=null)runtime.ReadPower();if(preferenceRevision!=preferences.Revision){PreferencesChanged();}backend.Refresh();PublishCompanionState();}
  if(legacyManagerOpen&&shelfReady&&fileShelf!=null&&!fileShelf.LegacyManagerOpen)legacyManagerOpen=false;
  if(pendingShelfContent.Length>0&&shelfReady&&fileShelf!=null&&!fileShelf.PanelOpen)pendingShelfContent="";
  if(painted&&!positionOnly)EnsureComponents();if(closing)return;IntPtr dockHandle=IntPtr.Zero;Rectangle dockBounds=Rectangle.Empty;
  var current=monitor.Screen.Bounds;if(current!=screen||Bounds!=current){screen=current;Bounds=screen;layoutKey="";}
  string nextTheme=preferences.Current.theme;var options=preferences.ForMonitor(monitor.Id,monitor.Primary);
  if(!positionOnly)try{monitorDock.Configure(monitor,options,preferences.Current.dockIconSize,StartupWindowOrder.MaskVisible());}catch(RainmeterDispatch.Pending){}catch(Exception ex){coordinator.monitorError=ex.Message;}
  if(primaryHost&&!desktopWidgets.Dragging){var rails=new List<Rectangle>();foreach(var host in Hosts()){var selected=preferences.ForMonitor(host.monitor.Id,host.monitor.Primary);if(!selected.enabled)continue;if(selected.left&&host.sideDock!=null&&!host.sideDock.IsDisposed&&host.sideDock.OcclusionBounds.Height>0)rails.Add(host.sideDock.OcclusionBounds);if(selected.right&&host.fileShelf!=null&&!host.fileShelf.IsDisposed)rails.Add(host.fileShelf.OcclusionBounds);}try{desktopWidgets.Arrange(rails);}catch(RainmeterDispatch.Pending){}catch(Exception ex){monitorError=ex.Message;}}
  bool manual=preferences.Current.manualPaused,covered=screenCovered;manualMotionPause=manual;
  bool overlayVisible=options.dock&&dockShown||sideDock!=null&&sideDock.Visible||topIsland!=null&&topIsland.Visible||fileShelf!=null&&fileShelf.Visible;
  if(primaryHost&&!positionOnly){string cursorDirectory=Path.GetFullPath(Path.Combine(root,".."));cursorTrail.Configure(CursorPreferences.Load(cursorDirectory),CursorPreferences.ReadScale(cursorDirectory));}
  pauseReasons=RuntimePolicy.Reasons(preferences.Current,coordinator.runtime==null?new RuntimeSignals():coordinator.runtime.Signals,covered&&!coordinator.WorkspaceOpen&&!coordinator.draggingDock,!options.enabled,systemAnimations,overlayVisible);
  bool nextPaused=pauseReasons.Length>0;timer.Interval=coordinator.WorkspaceOpen||coordinator.draggingDock?8:nextPaused?150:8;
  string motion=nextPaused?"true":"false";if(motion!=motionKey){motionKey=motion;DesktopSettingsFiles.Write(Path.Combine(root,"dock-motion-"+instance+".txt"),motion);}
  var regions=new List<Rectangle>();var nextCards=new List<GlassCard>();lastSkin=IntPtr.Zero;IntPtr firstSkin=IntPtr.Zero;
  EnumWindows((h,p)=>{var cl=new StringBuilder(256);GetClassName(h,cl,256);if(cl.ToString()!="RainmeterMeterWindow")return true;
   var title=new StringBuilder(1024);GetWindowText(h,title,1024);if(!title.ToString().Contains("\\PrismGlass\\"))return true;
   RECT r;if(!GetWindowRect(h,out r))return true;var rect=new Rectangle(r.L-screen.Left,r.T-screen.Top,r.R-r.L,r.B-r.T);
   if(rect.Width<10||rect.Height<10||!screen.IntersectsWith(new Rectangle(r.L,r.T,rect.Width,rect.Height)))return true;
   string name=Path.GetFileName(Path.GetDirectoryName(title.ToString()));if(name==monitorDock.SkinName)name="Dock";else if(!desktopWidgets.Owns(title.ToString()))return true;if(!options.enabled||(name=="Dock"&&!options.dock))return true;
   if(name!="Dock"&&!IsWindowVisible(h))return true;
   if(name=="Dock"){dockHandle=h;dockBounds=new Rectangle(r.L,r.T,rect.Width,rect.Height);}
   int insetY=name=="Dock"?16:0;
   nextCards.Add(new GlassCard{name=name,x=rect.X,y=rect.Y+insetY,w=rect.Width,h=rect.Height-insetY,r=Math.Min(name=="Dock"?34:24,(rect.Height-insetY)/2),insetY=insetY});
   if(name!="Dock"){regions.Add(Rectangle.Inflate(rect,26,26));if(firstSkin==IntPtr.Zero)firstSkin=h;}lastSkin=h;return true;
  },IntPtr.Zero);
  if(firstSkin==IntPtr.Zero)firstSkin=dockHandle;
  string key=json.Serialize(nextCards);
  bool changed=key!=layoutKey||theme!=nextTheme||paused!=nextPaused;
  cards=new List<object>();foreach(var card in nextCards)cards.Add(card);theme=nextTheme;paused=nextPaused;
  if(key!=layoutKey){layoutKey=key;widgetRegion.MakeEmpty();foreach(var rect in regions)widgetRegion.Union(rect);dockOutline=null;}
  if(painted&&lastSkin!=IntPtr.Zero)SetWindowPos(Handle,StartupWindowOrder.InsertAfter(Handle,lastSkin),0,0,0,0,0x53);
  // Dragging needs fresh geometry, without repeating data reads and settings writes each frame.
  if(positionOnly){UpdateVisibleRegion();if(changed)Send(new{type="layout",width=screen.Width,height=screen.Height,cards=cards,theme=theme,paused=paused});return;}
  var area=monitor.Screen.WorkingArea;
  if(sideDock!=null&&!sideDock.IsDisposed){sideDock.Sync(theme,area,firstSkin,paused||!sideDock.Visible);if(sideDock.IsReady)componentRecovery.Healthy(0,DateTime.UtcNow);}
  if(topIsland!=null&&!topIsland.IsDisposed){topIsland.Sync(theme,area,firstSkin,paused||!topIsland.Visible);if(islandReady)componentRecovery.Healthy(1,DateTime.UtcNow);}
  if(fileShelf!=null&&!fileShelf.IsDisposed){fileShelf.Sync(theme,area,firstSkin,paused||!fileShelf.Visible);if(shelfReady)componentRecovery.Healthy(2,DateTime.UtcNow);}
  UpdateComponentVisibility();
  if(islandReady&&shelfReady)File.WriteAllText(Diagnostic("extensions-heartbeat.txt"),DateTime.UtcNow.ToString("o"));
  dockWindow=dockHandle;dockRectangle=dockBounds;
  UpdateVisibleRegion();
  if(painted){dockPlate.Sync(Handle,dockShown?dockHandle:IntPtr.Zero,dockBounds,screen);SyncDockBackdrop();Taskbar(!options.enabled||!options.dock||dockHandle==IntPtr.Zero||(options.left&&(sideDock==null||!sideDock.IsReady))||(options.top&&!islandReady)||(options.right&&!shelfReady));}
  if(changed)Send(new {type="layout",width=screen.Width,height=screen.Height,cards=cards,theme=theme,paused=paused});
  var source=Path.Combine(root,"..","Glass-Wallpaper-Sample.png");var sourceStamp=File.GetLastWriteTimeUtc(source);
  if(primaryHost&&File.Exists(source)&&sourceStamp!=wallpaperSourceTime){WallpaperRefresh.SyncSample(root);wallpaperSourceTime=sourceStamp;}
  if(primaryHost&&runtime!=null&&!runtime.Signals.locked&&!runtime.Signals.displayOff)WallpaperRefresh.ScheduleMonitors(root,WallpaperCapture.CaptureDynamic,bounds=>{var player=LiveGlassBackdrop.WallpaperSource(bounds);return player==null?"":player.player+"|"+WallpaperRefresh.LivelySelection(bounds);});
  var sample=MonitorWallpaper.For(root,screen);
  if(sample.Version!=wallpaperTime){wallpaperTime=sample.Version;Send(new {type="wallpaper",version=sample.Version,file=sample.File});}
  File.WriteAllText(Diagnostic("layout.json"),json.Serialize(new {width=screen.Width,height=screen.Height,cards=cards,theme=theme,paused=paused,behind=lastSkin.ToInt64()}));
 }
 [STAThread] static void Main(string[] args){
  AppDomain.CurrentDomain.UnhandledException+=(sender,eventArgs)=>{try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-exception.txt"),DateTime.UtcNow.ToString("o")+Environment.NewLine+eventArgs.ExceptionObject);}catch{}};
  try{if(!SetProcessDpiAwarenessContext(new IntPtr(-4)))SetProcessDPIAware();}catch(EntryPointNotFoundException){SetProcessDPIAware();}string root=AppDomain.CurrentDomain.BaseDirectory;NativeGlassMaterial.Register(root);
  if(args.Length==0){using(var maintenance=new Mutex(false,AutoUpdateRuntime.UpdateMutex)){if(!Acquire(maintenance))return;try{Process.Start(new ProcessStartInfo(Path.Combine(root,"PrismGlass.exe")){UseShellExecute=false});}catch(Exception ex){MessageBox.Show("PrismGlass 未能启动："+ex.Message,"PrismGlass");}finally{maintenance.ReleaseMutex();}}return;}
  if(args.Length==1&&(args[0]=="--apps"||args[0]=="--settings")){File.WriteAllText(Path.Combine(root,"workspace.request"),args[0]=="--apps"?"apps":"settings");return;}
  if(args.Length==1&&args[0]=="--tray"){File.WriteAllText(Path.Combine(root,"tray.request"),"open");return;}
  if(args.Length==2&&args[0]=="--widget"&&Array.IndexOf(new[]{"Weather","Music","Focus","Folders","System","Tools","Life"},args[1])>=0){File.WriteAllText(Path.Combine(root,"widget.request"),args[1]);return;}
  if(args.Length==2&&args[0]=="--panel"&&(args[1]=="battery"||args[1]=="network"||args[1]=="both"||args[1]=="calendar")){File.WriteAllText(Path.Combine(root,"panel.request"),args[1]);return;}
  if(args.Length>0){string flag=args[0];if(flag=="--side-preview"){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SideDock(root,true));return;}if(flag=="--dock-release"&&args.Length==2){DockBackend.ReleasePointer(root,args[1]);return;}if(flag=="--dock-menu"&&args.Length==2){Application.EnableVisualStyles();DockBackend.Menu(root,args[1]);return;}if((flag=="--dock"||flag=="--dock-new")&&args.Length==2){Application.EnableVisualStyles();DockBackend.Click(root,args[1],flag=="--dock-new");return;}if(flag=="--toggle-theme"){var p=Path.Combine(root,"theme.txt");File.WriteAllText(p,File.Exists(p)&&File.ReadAllText(p).Trim()=="Astro"?"Sky":"Astro");return;}
   if(flag=="--toggle-motion"){var p=Path.Combine(root,"paused.txt");File.WriteAllText(p,File.Exists(p)&&File.ReadAllText(p).Trim()=="true"?"false":"true");return;}
   if(flag=="--quit"||flag=="--maintenance-stop"){DesktopSession.Request(root,flag=="--quit"?"stop.request":"maintenance-stop.request");return;}}
  string mode=args.Length==1?args[0]:"";
  if(mode=="--stop-companion"||mode=="--update-stop-companion"){
   DesktopSession.Request(root,mode=="--stop-companion"?CompanionSession.StopRequest:CompanionSession.UpdateStopRequest);return;
  }
  bool companionOnly=mode=="--companion"||mode=="--ensure-companion"||mode=="--recover-companion";
  if(!companionOnly&&mode!="--renderer")return;
  using(var gate=new Mutex(false,companionOnly?CompanionSession.MutexName:"Local\\PrismLiquidDesktop")){
   ApplicationContext session;
   using(var maintenance=new Mutex(false,AutoUpdateRuntime.UpdateMutex)){
    if(!Acquire(maintenance))return;
    try{
     if(!Acquire(gate)){if(mode=="--companion")DesktopSession.Request(root,CompanionSession.OpenRequest);return;}
     Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
     if(companionOnly){
      File.Delete(Path.Combine(root,CompanionSession.StopRequest));
      File.Delete(Path.Combine(root,CompanionSession.UpdateStopRequest));
      File.Delete(Path.Combine(root,CompanionSession.ControlsRequest));
      session=new CompanionSession(root,mode);
     }else{
      File.Delete(Path.Combine(root,"stop.request"));File.Delete(Path.Combine(root,"maintenance-stop.request"));
      session=new DesktopSession(root);
     }
    }finally{maintenance.ReleaseMutex();}
   }
   try{
    using(session){
     if(!companionOnly&&File.Exists(Path.Combine(root,"runtime-data","companion","screen-stream.json"))){
      using(var recovery=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--recover-companion"){UseShellExecute=false,CreateNoWindow=true})){}
     }
     Application.Run(session);
    }
   }finally{gate.ReleaseMutex();}
  }
 }
 static bool Acquire(Mutex mutex){try{return mutex.WaitOne(0);}catch(AbandonedMutexException){return true;}}
}
