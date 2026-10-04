using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class GlassCard {public string name;public int x,y,w,h,r,insetY;}
internal sealed class LiquidDesktop : Form {
 readonly string root=AppDomain.CurrentDomain.BaseDirectory;readonly uint ownPid=(uint)Process.GetCurrentProcess().Id;
 readonly WebView2 view=new WebView2();
 readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 DateTime probe=DateTime.MinValue,beat=DateTime.MinValue,wallpaperTime=DateTime.MinValue;
 bool ready,painted,closing,busy,lastDown; string layoutKey="",theme="Sky"; bool paused;
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
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags);
 [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] static extern short GetAsyncKeyState(int k);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string c,string t);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string c,string t);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int command);
 [StructLayout(LayoutKind.Sequential)] struct APPBAR {public int cb;public IntPtr h;public uint message,edge;public RECT rect;public IntPtr param;}
 [DllImport("shell32.dll")]static extern UIntPtr SHAppBarMessage(uint message,ref APPBAR data);
 [DllImport("user32.dll")]static extern bool SystemParametersInfo(uint action,uint param,ref RECT rect,uint flags);
 void Taskbar(bool visible){
  if(!visible&&!File.Exists(Path.Combine(root,"desktop-ready.flag")))return;
  var h=FindWindow("Shell_TrayWnd",null);var data=new APPBAR{cb=Marshal.SizeOf(typeof(APPBAR)),h=h};int desired=visible?2:3;
  if(h!=IntPtr.Zero&&(int)SHAppBarMessage(4,ref data).ToUInt64()!=desired){data.param=new IntPtr(desired);SHAppBarMessage(10,ref data);}
  if(h!=IntPtr.Zero&&IsWindowVisible(h)!=visible)ShowWindow(h,visible?5:0);
  h=IntPtr.Zero;while((h=FindWindowEx(IntPtr.Zero,h,"Shell_SecondaryTrayWnd",null))!=IntPtr.Zero)if(!IsWindowVisible(h))ShowWindow(h,5);
  if(!visible){var area=new RECT();if(SystemParametersInfo(48,0,ref area,0)&&area.L==screen.Left&&area.T==screen.Top&&area.R==screen.Right&&area.B<screen.Bottom&&screen.Bottom-area.B<=128){area.B=screen.Bottom;SystemParametersInfo(47,0,ref area,2);}}
 }
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out RECT bounds,int size);
 void DockVisibility(Point cursor){
  if(dockWindow==IntPtr.Zero)return;
  var at=new Point(cursor.X+screen.X,cursor.Y+screen.Y);var now=DateTime.UtcNow;
  bool edge=at.X>=dockRectangle.Left&&at.X<=dockRectangle.Right&&at.Y>=screen.Bottom-3;
  bool hovering=(preview!=null&&!preview.IsDisposed&&preview.Bounds.Contains(at))||(dockShown&&dockRectangle.Contains(at));
  var front=GetForegroundWindow();uint pid;GetWindowThreadProcessId(front,out pid);var cl=new StringBuilder(256);GetClassName(front,cl,256);RECT rect=new RECT();
  bool application=front!=IntPtr.Zero&&pid!=ownPid&&front!=dockWindow&&IsWindowVisible(front)&&!IsIconic(front)&&cl.ToString()!="Progman"&&cl.ToString()!="WorkerW"&&cl.ToString()!="RainmeterMeterWindow";
  bool hasBounds=application&&(DwmGetWindowAttribute(front,9,out rect,Marshal.SizeOf(typeof(RECT)))==0||GetWindowRect(front,out rect));
  var visualDock=new Rectangle(dockRectangle.X,dockRectangle.Y+16,dockRectangle.Width,Math.Max(0,dockRectangle.Height-16));
  var intersection=hasBounds?Rectangle.Intersect(visualDock,Rectangle.FromLTRB(rect.L,rect.T,rect.R,rect.B)):Rectangle.Empty;
  bool overlap=intersection.Width>8&&intersection.Height>8;
  bool show=dockPolicy.Update(now,overlap,edge,hovering);
  string decision=show+"|"+overlap+"|"+pid+"|"+cl;
  if(decision!=dockDecision){dockDecision=decision;File.WriteAllText(Path.Combine(root,"dock-visibility.json"),json.Serialize(new{shown=show,covered=overlap,foregroundPid=pid,foregroundClass=cl.ToString(),ignoredOwnedWindow=!application,window=new{left=rect.L,top=rect.T,right=rect.R,bottom=rect.B},atEdge=edge,updated=now.ToString("o")}));}
  if(show!=dockShown){dockShown=show;UpdateVisibleRegion();Process.Start(new ProcessStartInfo(RuntimePaths.Rainmeter,(show?"!Show":"!Hide")+" \"PrismGlass\\Dock\""){UseShellExecute=false,CreateNoWindow=true});dockPlate.Sync(Handle,show?dockWindow:IntPtr.Zero,dockRectangle,screen);}
 }
 void PreviewTick(Point cursor){
  var at=new Point(cursor.X+screen.X,cursor.Y+screen.Y);var now=DateTime.UtcNow;
  if(preview!=null&&!preview.IsDisposed&&preview.Bounds.Contains(at)){previewLeave=now.AddMilliseconds(400);return;}
  string id=dockShown&&dockRectangle.Contains(at)?Read("dock-hover.txt",""):"";
  if(id!=hoverId){hoverId=id;hoverSince=now;}
  if(id.Length>0){previewLeave=now.AddMilliseconds(400);if((now-hoverSince).TotalMilliseconds>=400&&(preview==null||preview.IsDisposed||preview.EntryId!=id)){if(preview!=null)preview.Dispose();var entry=backend.Entry(id);if(entry!=null){preview=new DockPreview(entry,dockRectangle,theme=="Astro");preview.Show();}}}
  else if(now>previewLeave&&preview!=null){preview.Dispose();preview=null;}
 }
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override CreateParams CreateParams {get {var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;p.ExStyle&=~0x40000;return p;}}
 protected override void WndProc(ref Message m){if(shellMessage!=0&&(uint)m.Msg==shellMessage&&m.WParam.ToInt64()==0x8006&&backend!=null)backend.Attention(m.LParam.ToInt64());if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}base.WndProc(ref m);}
 public LiquidDesktop(){
  backend=new DockBackend(root); backend.Refresh();
  Text="Liquid Desktop · 玻璃渲染层";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;
  screen=Screen.PrimaryScreen.Bounds;Bounds=screen;Region=new Region(Rectangle.Empty);Enabled=false;
  view.Dock=DockStyle.Fill;Controls.Add(view);
  Shown+=async(s,e)=>{try{
   shellMessage=RegisterWindowMessage("SHELLHOOK");RegisterShellHookWindow(Handle);
   var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"runtime-data"));
   await view.EnsureCoreWebView2Async(env);
   view.CoreWebView2.Settings.AreDevToolsEnabled=false;view.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;view.CoreWebView2.Settings.IsStatusBarEnabled=false;
   view.CoreWebView2.SetVirtualHostNameToFolderMapping("desktop.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
   view.CoreWebView2.ProcessFailed+=(ss,a)=>{File.WriteAllText(Path.Combine(root,"error.txt"),"WebView2 process failed: "+a.ProcessFailedKind);Close();};
   view.CoreWebView2.NavigationStarting+=(ss,a)=>{Uri u;if(!Uri.TryCreate(a.Uri,UriKind.Absolute,out u)||u.Host!="desktop.local")a.Cancel=true;};
   view.CoreWebView2.NewWindowRequested+=(ss,a)=>a.Handled=true;
   view.CoreWebView2.WebMessageReceived+=(ss,a)=>{
    if(!a.Source.StartsWith("https://desktop.local/",StringComparison.Ordinal))return;
    var data=a.TryGetWebMessageAsString();
    if(data=="ready"){ready=true;probe=DateTime.MinValue;Tick();}
    else if(data=="painted"){painted=true;probe=DateTime.MinValue;EnsureComponents();}
    else if(data.StartsWith("dock-shape:")){dockOutline=json.Deserialize<float[][]>(data.Substring(11));dockPlate.Outline(dockOutline);UpdateVisibleRegion();}
    else if(data.StartsWith("widget-shapes:")){var outlines=json.Deserialize<float[][][]>(data.Substring(14));widgetRegion.MakeEmpty();foreach(var outline in outlines){if(outline.Length<3)continue;using(var path=new GraphicsPath()){var points=new PointF[outline.Length];for(int i=0;i<points.Length;i++)points[i]=new PointF(outline[i][0],outline[i][1]);path.AddPolygon(points);widgetRegion.Union(path);}}UpdateVisibleRegion();}
    else if(data.StartsWith("error:")){File.WriteAllText(Path.Combine(root,"error.txt"),data);Close();}
    else if(data.StartsWith("stats:"))File.WriteAllText(Path.Combine(root,"render-stats.json"),data.Substring(6));
   };
   view.Source=new Uri("https://desktop.local/index.html");
   timer.Interval=16;timer.Tick+=(ss,a)=>Tick();timer.Start();
  }catch(Exception ex){File.WriteAllText(Path.Combine(root,"error.txt"),ex.ToString());Close();}};
  FormClosed+=(s,e)=>{closing=true;DeregisterShellHookWindow(Handle);timer.Stop();timer.Dispose();if(sideDock!=null)sideDock.Dispose();if(topIsland!=null)topIsland.Dispose();if(fileShelf!=null)fileShelf.Dispose();LegacyTopbar(false);RestoreIcons();if(preview!=null)preview.Dispose();dockPlate.Dispose();view.Dispose();Taskbar(true);try{File.Delete(Path.Combine(root,"heartbeat.txt"));}catch{}};
 }
 string Read(string file,string fallback){try{return File.ReadAllText(Path.Combine(root,file)).Trim();}catch{return fallback;}}
 void Send(object value){if(view.CoreWebView2!=null&&!closing)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(value));}
 void Tick(){
  if(!ready||closing||busy)return;busy=true;
  try{
   if(File.Exists(Path.Combine(root,"stop.request"))){try{File.Delete(Path.Combine(root,"stop.request"));}catch(IOException){return;}Close();return;}
   var now=DateTime.UtcNow;
   if((now-probe).TotalSeconds>=1){probe=now;Probe();}
   POINT p;GetCursorPos(out p);var cursor=new Point(p.X-screen.X,p.Y-screen.Y);
   var panelRequest=Path.Combine(root,"panel.request");
   if(File.Exists(panelRequest)){string mode=Read("panel.request","");File.Delete(panelRequest);if(sideDock!=null&&!sideDock.IsDisposed&&(mode=="battery"||mode=="network"||mode=="both"||mode=="calendar"))sideDock.Open(mode,new Point(p.X,p.Y));}
   var widgetRequest=Path.Combine(root,"widget.request");if(File.Exists(widgetRequest)){string name=Read("widget.request","");File.Delete(widgetRequest);if(sideDock!=null&&!sideDock.IsDisposed)sideDock.Open(name.ToLowerInvariant(),new Point(p.X,p.Y));}
   if(painted)DockVisibility(cursor);
   if(painted&&ticks%5==0)PreviewTick(cursor);
   bool down=(GetAsyncKeyState(1)&0x8000)!=0;
   if(cursor!=lastPointer||down!=lastDown){lastPointer=cursor;lastDown=down;Send(new {type="pointer",x=cursor.X,y=cursor.Y,down=down});}
   if(painted&&(now-beat).TotalSeconds>=2){beat=now;File.WriteAllText(Path.Combine(root,"heartbeat.txt"),((long)(now-new DateTime(1970,1,1)).TotalSeconds)+" "+theme);}
   if(++ticks%150==0)Send(new {type="stats"});
  }catch(Exception ex){File.WriteAllText(Path.Combine(root,"error.txt"),ex.ToString());}finally{busy=false;}
 }
 void LegacyTopbar(bool hidden){
  if(hidden==legacyTopHidden)return;legacyTopHidden=hidden;layoutKey="";
  try{Process.Start(new ProcessStartInfo(RuntimePaths.Rainmeter,(hidden?"!DeactivateConfig \"PrismGlass\\Topbar\"":"!ActivateConfig \"PrismGlass\\Topbar\" \"Main.ini\"")){UseShellExecute=false,CreateNoWindow=true});}catch{}
 }
 void RestoreIcons(){EnumWindows((h,p)=>{var v=FindWindowEx(h,IntPtr.Zero,"SHELLDLL_DefView",null);if(v!=IntPtr.Zero){var icons=FindWindowEx(v,IntPtr.Zero,"SysListView32",null);ShowWindow(v,5);if(icons!=IntPtr.Zero)ShowWindow(icons,5);}return true;},IntPtr.Zero);}
 void ComponentFailed(int component){
  if(closing)return;
  componentRecovery.Failed(component,DateTime.UtcNow);Taskbar(true);
  if(component==1){islandReady=false;LegacyTopbar(false);}if(component==2)shelfReady=false;
  if(componentRecovery.Exhausted(component)){
   try{File.WriteAllText(Path.Combine(root,"component-recovery.txt"),"Desktop component failed three times; restoring ordinary desktop.");
    var script=Path.GetFullPath(Path.Combine(root,"..","DesktopMode.ps1"));
    Process.Start(new ProcessStartInfo("pwsh.exe","-NoProfile -NonInteractive -WindowStyle Hidden -File \""+script+"\" -Mode Recovery"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
   }catch{}finally{Close();}
  }
 }
 void EnsureComponents(){
  if(closing)return;var now=DateTime.UtcNow;
  if((sideDock==null||sideDock.IsDisposed)&&componentRecovery.CanStart(0,now)){
   sideDock=new SideDock(root,false);sideDock.FormClosed+=(s,e)=>ComponentFailed(0);sideDock.Show();
  }
  if((topIsland==null||topIsland.IsDisposed)&&componentRecovery.CanStart(1,now)){
   islandReady=false;topIsland=new TopIsland(root,false);topIsland.Ready+=()=>{islandReady=true;LegacyTopbar(true);};topIsland.FormClosed+=(s,e)=>ComponentFailed(1);topIsland.Show();
  }
  if((fileShelf==null||fileShelf.IsDisposed)&&componentRecovery.CanStart(2,now)){
   shelfReady=false;fileShelf=new FileShelf(root,false);fileShelf.Ready+=()=>shelfReady=true;fileShelf.FormClosed+=(s,e)=>ComponentFailed(2);fileShelf.Show();
  }
 }
 void Probe(){
  if(painted)EnsureComponents();if(closing)return;backend.Refresh(); IntPtr dockHandle=IntPtr.Zero;Rectangle dockBounds=Rectangle.Empty;
  var current=Screen.PrimaryScreen.Bounds;if(current!=screen){screen=current;Bounds=screen;layoutKey="";}
  string nextTheme=Read("theme.txt","Sky")=="Astro"?"Astro":"Sky";
  bool manual=Read("paused.txt","false")=="true",covered=false;
  var foreground=GetForegroundWindow();RECT fr=new RECT();uint foregroundPid;GetWindowThreadProcessId(foreground,out foregroundPid);var fc=new StringBuilder(256);GetClassName(foreground,fc,256);
  bool application=foreground!=IntPtr.Zero&&foregroundPid!=ownPid&&IsWindowVisible(foreground)&&!IsIconic(foreground)&&fc.ToString()!="Progman"&&fc.ToString()!="WorkerW"&&fc.ToString()!="RainmeterMeterWindow";
  if(application&&(DwmGetWindowAttribute(foreground,9,out fr,Marshal.SizeOf(typeof(RECT)))==0||GetWindowRect(foreground,out fr)))
   covered=DesktopVisibilityPolicy.Covered(Rectangle.FromLTRB(fr.L,fr.T,fr.R,fr.B),screen,Screen.PrimaryScreen.WorkingArea,true,IsZoomed(foreground));
  bool nextPaused=manual||covered;timer.Interval=nextPaused?50:16;
  var regions=new List<Rectangle>();var nextCards=new List<GlassCard>();lastSkin=IntPtr.Zero;IntPtr firstSkin=IntPtr.Zero;
  EnumWindows((h,p)=>{var cl=new StringBuilder(256);GetClassName(h,cl,256);if(cl.ToString()!="RainmeterMeterWindow")return true;
   var title=new StringBuilder(1024);GetWindowText(h,title,1024);if(!title.ToString().Contains("\\PrismGlass\\"))return true;
   RECT r;if(!GetWindowRect(h,out r))return true;var rect=new Rectangle(r.L-screen.Left,r.T-screen.Top,r.R-r.L,r.B-r.T);
   if(rect.Width<10||rect.Height<10||!screen.IntersectsWith(new Rectangle(r.L,r.T,rect.Width,rect.Height)))return true;
   string name=Path.GetFileName(Path.GetDirectoryName(title.ToString()));if(name=="Topbar"&&islandReady)return true;
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
  if(sideDock!=null&&!sideDock.IsDisposed){sideDock.Sync(theme,screen,firstSkin,paused);if(sideDock.IsReady)componentRecovery.Healthy(0,DateTime.UtcNow);}
  if(topIsland!=null&&!topIsland.IsDisposed){topIsland.Sync(theme,screen,firstSkin,paused);if(islandReady)componentRecovery.Healthy(1,DateTime.UtcNow);}
  if(fileShelf!=null&&!fileShelf.IsDisposed){fileShelf.Sync(theme,screen,firstSkin,paused);if(shelfReady)componentRecovery.Healthy(2,DateTime.UtcNow);}
  if(!covered||(sideDock!=null&&sideDock.IsReady&&islandReady&&shelfReady))foreach(Form component in new Form[]{sideDock,topIsland,fileShelf}){if(component==null||component.IsDisposed)continue;if(covered){if(component.Visible)component.Hide();}else if(!component.Visible)component.Show();}
  if(islandReady&&shelfReady)File.WriteAllText(Path.Combine(root,"extensions-heartbeat.txt"),DateTime.UtcNow.ToString("o"));
  dockWindow=dockHandle;dockRectangle=dockBounds;
  UpdateVisibleRegion();
  if(painted){dockPlate.Sync(Handle,dockShown?dockHandle:IntPtr.Zero,dockBounds,screen);Taskbar(dockHandle==IntPtr.Zero||sideDock==null||!sideDock.IsReady||!islandReady||!shelfReady);}
  if(changed)Send(new {type="layout",width=screen.Width,height=screen.Height,cards=cards,theme=theme,paused=paused});
  var source=Path.Combine(root,"..","Glass-Wallpaper-Sample.png");var stamp=File.GetLastWriteTimeUtc(source);
  if(stamp!=wallpaperTime){wallpaperTime=stamp;File.Copy(source,Path.Combine(root,"wallpaper.png"),true);Send(new {type="wallpaper",version=stamp.Ticks.ToString()});}
  File.WriteAllText(Path.Combine(root,"layout.json"),json.Serialize(new {width=screen.Width,height=screen.Height,cards=cards,theme=theme,paused=paused,behind=lastSkin.ToInt64()}));
 }
 [STAThread] static void Main(string[] args){
  SetProcessDPIAware();string root=AppDomain.CurrentDomain.BaseDirectory;
  if(args.Length==2&&args[0]=="--widget"&&Array.IndexOf(new[]{"Weather","Music","Focus","Folders","System","Tools","Life"},args[1])>=0){File.WriteAllText(Path.Combine(root,"widget.request"),args[1]);return;}
  if(args.Length==2&&args[0]=="--panel"&&(args[1]=="battery"||args[1]=="network"||args[1]=="both"||args[1]=="calendar")){File.WriteAllText(Path.Combine(root,"panel.request"),args[1]);return;}
  if(args.Length>0){string flag=args[0];if(flag=="--side-preview"){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SideDock(root,true));return;}if(flag=="--dock-menu"&&args.Length==2){Application.EnableVisualStyles();DockBackend.Menu(root,args[1]);return;}if((flag=="--dock"||flag=="--dock-new")&&args.Length==2){Application.EnableVisualStyles();DockBackend.Click(root,args[1],flag=="--dock-new");return;}if(flag=="--toggle-theme"){var p=Path.Combine(root,"theme.txt");File.WriteAllText(p,File.Exists(p)&&File.ReadAllText(p).Trim()=="Astro"?"Sky":"Astro");return;}
   if(flag=="--toggle-motion"){var p=Path.Combine(root,"paused.txt");File.WriteAllText(p,File.Exists(p)&&File.ReadAllText(p).Trim()=="true"?"false":"true");return;}
   if(flag=="--quit"){File.WriteAllText(Path.Combine(root,"stop.request"),"stop");return;}}
  bool created;using(var gate=new Mutex(true,"Local\\PrismLiquidDesktop",out created)){if(!created)return;File.Delete(Path.Combine(root,"stop.request"));
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new LiquidDesktop());}
 }
}
