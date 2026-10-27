using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class DockPin {public string id,name,path,arguments,icon;public bool exists;}
internal sealed class DockWindow {public long handle;public uint pid;public string title,path,appId;}
internal sealed class DockEntry {public string id,name,icon,path,arguments;public bool pinned,active,attention,system;public List<DockWindow> windows=new List<DockWindow>();}
internal enum DockClickAction { Launch,Activate,Minimize,ChooseWindow }
internal sealed class DockForegroundHistory {
 internal long current;DockWindow application,previous;bool dock;DateTime observed,entered;
 internal void Observe(long handle,DockWindow window,bool isDock,DateTime now){
  if(handle!=current){previous=isDock&&!dock&&now>=observed&&(now-observed).TotalMilliseconds<=500?application:null;application=isDock?null:window;current=handle;dock=isDock;entered=now;}
  observed=now;
 }
 internal DockWindow PreviousFor(long handle,DateTime now){return dock&&handle==current&&now>=observed&&(now-observed).TotalMilliseconds<=500&&(now-entered).TotalMilliseconds<=1500?previous:null;}
 internal void Invalidate(){current=0;application=null;previous=null;dock=false;}
}
internal sealed class DockBackend {
 const string OverflowId="r2147483647";
 const string WorkspaceId="workspace";
 readonly string root;DockPin[] pins;DateTime pinsStamp;readonly JavaScriptSerializer json=new JavaScriptSerializer();
 readonly Dictionary<string,int> order=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
 readonly Dictionary<string,string> icons=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 string previous="";
 readonly HashSet<long> attention=new HashSet<long>();public void Attention(long h){attention.Add(h);}
 DockDrag drag;DateTime requestPoll;string externalToken="";
 static DockPreview applicationPicker;
 static readonly DockForegroundHistory foregroundHistory=new DockForegroundHistory();
 sealed class DockDrag {public string id,instance,token;public DockPin external;public Point start,release;public bool moved,released;public DateTime releasedAt;public IDisposable hold;public List<DockEntry> entries;public string feedback="",feedbackInstance="";}
 internal sealed class ExternalDragRequest {public string token;public long expires;public DockPin entry;public bool ended,cancelled;}
 internal sealed class DockLayout {public float cell {get;set;}public float dpi {get;set;}}
 delegate bool EnumProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc fn,IntPtr p);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h,uint cmd);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int size);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder text,int size);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h,int cmd);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
 [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h,uint flags,StringBuilder path,ref uint size);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern int GetApplicationUserModelId(IntPtr process,ref uint length,StringBuilder id);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attr,out int value,int size);
 [StructLayout(LayoutKind.Sequential)] struct SIZE {public int x,y;}
 [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IImageFactory {[PreserveSig]int GetImage(SIZE size,int flags,out IntPtr bitmap);}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)] static extern void SHCreateItemFromParsingName(string path,IntPtr context,ref Guid guid,[MarshalAs(UnmanagedType.Interface)]out IImageFactory item);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
 [StructLayout(LayoutKind.Sequential)] struct BITMAPINFO {public uint size;public int width,height;public ushort planes,bits;public uint compression,imageSize;public int x,y;public uint used,important;}
 [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr h);
 [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr h);
 [DllImport("gdi32.dll")]static extern int GetDIBits(IntPtr dc,IntPtr bitmap,uint start,uint rows,IntPtr data,ref BITMAPINFO info,uint usage);
 static void SaveBitmap(IntPtr h,string path){using(var source=Image.FromHbitmap(h)){int w=source.Width,ht=source.Height;using(var output=new Bitmap(w,ht,PixelFormat.Format32bppArgb)){var data=output.LockBits(new Rectangle(0,0,w,ht),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);var info=new BITMAPINFO{size=40,width=w,height=-ht,planes=1,bits=32};var dc=CreateCompatibleDC(IntPtr.Zero);try{if(GetDIBits(dc,h,0,(uint)ht,data.Scan0,ref info,0)==0)throw new Exception("icon pixels");}finally{DeleteDC(dc);output.UnlockBits(data);}output.Save(path,ImageFormat.Png);}}}
 static string ProcessPath(uint pid){var h=OpenProcess(0x1000,false,pid);if(h==IntPtr.Zero)return "";try{var b=new StringBuilder(32768);uint n=32768;return QueryFullProcessImageName(h,0,b,ref n)?b.ToString():"";}finally{CloseHandle(h);}}
 public DockBackend(string folder){root=folder;Directory.CreateDirectory(Path.Combine(root,"dock-icons"));pins=ReadPins(root).ToArray();
  foreach(var p in pins)p.icon=IconFor(p.path,p.icon,"pin-"+p.id);
 }
 string IconFor(string path,string custom,string key){
  string saved;string cacheKey=path+"|"+custom;if(icons.TryGetValue(cacheKey,out saved))return saved;
  saved=Path.Combine(root,"dock-icons",key+".png");
  try{if(!String.IsNullOrEmpty(custom)&&File.Exists(custom)){using(var input=Image.FromFile(custom))input.Save(saved,ImageFormat.Png);}
   else {IImageFactory factory=null;IntPtr bitmap=IntPtr.Zero;try{var iid=typeof(IImageFactory).GUID;SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out factory);if(factory.GetImage(new SIZE{x=128,y=128},5,out bitmap)!=0)throw new Exception("icon");SaveBitmap(bitmap,saved);}finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(factory!=null)Marshal.ReleaseComObject(factory);}}
  }catch{try{using(var ico=Icon.ExtractAssociatedIcon(path))using(var img=ico.ToBitmap())img.Save(saved,ImageFormat.Png);}catch{using(var img=SystemIcons.Application.ToBitmap())img.Save(saved,ImageFormat.Png);}}
  icons[cacheKey]=saved;return saved;
 }
 static readonly Dictionary<string,string[]> shortcutTargets=new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
 static readonly Dictionary<string,string> physicalPaths=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);static DateTime physicalPathsExpiry;
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file,StringBuilder path,uint size,uint flags);
 static string PhysicalPath(string path){
  if(String.IsNullOrEmpty(path))return path;
  lock(physicalPaths){
   var now=DateTime.UtcNow;if(now>=physicalPathsExpiry||physicalPaths.Count>=256){physicalPaths.Clear();physicalPathsExpiry=now.AddSeconds(2);}
   string resolved;if(physicalPaths.TryGetValue(path,out resolved))return resolved;resolved=path;
   // Resolve migrated application junctions without changing the saved launch target.
   using(var file=CreateFile(path,0,7,IntPtr.Zero,3,0,IntPtr.Zero)){if(!file.IsInvalid){var final=new StringBuilder(32768);uint length=GetFinalPathNameByHandle(file,final,(uint)final.Capacity,0);if(length>0&&length<final.Capacity)resolved=final.ToString();}}
   physicalPaths[path]=resolved;return resolved;
  }
 }
 static string[] LaunchIdentity(string path,string arguments){
  if(!path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase))return new[]{path,arguments??""};
  string key=path+"|"+File.GetLastWriteTimeUtc(path).Ticks;string[] cached;lock(shortcutTargets){if(shortcutTargets.TryGetValue(key,out cached))return cached;}
  object shell=null,shortcut=null;var result=new[]{path,arguments??""};
  try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});string target=shortcut.GetType().InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null) as string;if(!String.IsNullOrWhiteSpace(target))result=new[]{target,(shortcut.GetType().InvokeMember("Arguments",BindingFlags.GetProperty,null,shortcut,null) as string)??""};}catch(COMException){}catch(TargetInvocationException){}catch(ArgumentException){}finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
  lock(shortcutTargets){if(shortcutTargets.Count>256)shortcutTargets.Clear();shortcutTargets[key]=result;}return result;
 }
 static string ApplicationId(uint pid){var h=OpenProcess(0x1000,false,pid);if(h==IntPtr.Zero)return "";try{uint length=0;GetApplicationUserModelId(h,ref length,null);if(length==0||length>1024)return "";var value=new StringBuilder((int)length);return GetApplicationUserModelId(h,ref length,value)==0?value.ToString():"";}finally{CloseHandle(h);}}
 internal static bool Matches(DockPin pin,DockWindow window){if(pin.path.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase))return String.Equals(pin.path.Substring(17),window.appId,StringComparison.OrdinalIgnoreCase);string target=LaunchIdentity(pin.path,pin.arguments)[0];return String.Equals(target,window.path,StringComparison.OrdinalIgnoreCase)||String.Equals(PhysicalPath(target),PhysicalPath(window.path),StringComparison.OrdinalIgnoreCase);}
 static List<DockWindow> ReadWindows(){
  var all=new List<DockWindow>();var paths=new Dictionary<uint,string>();var appIds=new Dictionary<uint,string>();
  EnumWindows((h,p)=>{
   if(!IsWindowVisible(h))return true;long style=GetWindowLongPtr(h,-20).ToInt64();if((style&0x80)!=0||(style&0x08000000)!=0)return true;
   if(GetWindow(h,4)!=IntPtr.Zero&&(style&0x40000)==0)return true;
   int cloaked;if(DwmGetWindowAttribute(h,14,out cloaked,4)==0&&cloaked!=0)return true;
   var cl=new StringBuilder(256);GetClassName(h,cl,256);if(new[]{"Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd","RainmeterMeterWindow"}.Contains(cl.ToString()))return true;
   var title=new StringBuilder(1024);GetWindowText(h,title,1024);if(title.Length==0)return true;
   uint pid;GetWindowThreadProcessId(h,out pid);string path;if(!paths.TryGetValue(pid,out path)){path=ProcessPath(pid);paths[pid]=path;}
   if(String.IsNullOrEmpty(path)||new[]{"liquiddesktop.exe","rainmeter.exe","nexus.exe"}.Contains(Path.GetFileName(path).ToLowerInvariant()))return true;
   string appId;if(!appIds.TryGetValue(pid,out appId)){appId=ApplicationId(pid);appIds[pid]=appId;}all.Add(new DockWindow{handle=h.ToInt64(),pid=pid,title=title.ToString(),path=path,appId=appId});return true;
  },IntPtr.Zero);
  return all;
 }
 public void Refresh(){
  var stamp=File.GetLastWriteTimeUtc(Path.Combine(root,"pins.json"));if(stamp!=pinsStamp){try{var updated=json.Deserialize<DockPin[]>(File.ReadAllText(Path.Combine(root,"pins.json")));foreach(var pin in updated)pin.icon=IconFor(pin.path,pin.icon,"pin-"+pin.id);pins=updated;pinsStamp=stamp;}catch{}}

  if(drag!=null)return; // Keep hit targets stable until the drag transaction ends.
  var foreground=GetForegroundWindow();var all=ReadWindows();
  var entries=new List<DockEntry>{WorkspaceEntry(WorkspaceIcon())};var used=new HashSet<long>();
  foreach(var pin in pins){var e=new DockEntry{id="p"+pin.id,name=pin.name,icon=pin.icon,path=pin.path,arguments=pin.arguments,pinned=true};
   e.windows=all.Where(w=>!used.Contains(w.handle)&&Matches(pin,w)).ToList();foreach(var w in e.windows)used.Add(w.handle);e.active=e.windows.Any(w=>w.handle==foreground.ToInt64());entries.Add(e);
  }
  foreach(var group in all.Where(w=>!used.Contains(w.handle)).GroupBy(w=>w.path,StringComparer.OrdinalIgnoreCase)){
   if(!order.ContainsKey(group.Key))order[group.Key]=order.Count;var sequence=order[group.Key];
   var name=Path.GetFileNameWithoutExtension(group.Key);if(name.Equals("explorer",StringComparison.OrdinalIgnoreCase))name="文件资源管理器";
   entries.Add(new DockEntry{id="r"+sequence,name=name,path=group.Key,arguments="",pinned=false,icon=IconFor(group.Key,"","run-"+sequence),windows=group.ToList(),active=group.Any(w=>w.handle==foreground.ToInt64())});
  }
  entries=entries.Where(e=>e.pinned).Concat(entries.Where(e=>!e.pinned).OrderBy(e=>Int32.Parse(e.id.Substring(1)))).ToList();
  attention.RemoveWhere(h=>!all.Any(w=>w.handle==h));foreach(var e in entries){if(e.active)foreach(var w in e.windows)attention.Remove(w.handle);e.attention=e.windows.Any(w=>attention.Contains(w.handle));}
  var displayed=DisplayEntries(entries,OverflowIcon());
  var overflow=displayed.FirstOrDefault(e=>e.id==OverflowId);if(overflow!=null)entries.Add(overflow);
  string data=json.Serialize(entries);if(data==previous)return;previous=data;
  AtomicWrite(Path.Combine(root,"dock-state.json"),data);
  var lines=new StringBuilder();foreach(var e in displayed)lines.AppendLine(e.id+"\t"+B64(e.name)+"\t"+B64(e.icon)+"\t"+e.windows.Count+"\t"+(e.active?"1":"0")+"\t"+(e.pinned?"1":"0")+"\t"+(e.attention?"1":"0"));
  AtomicWrite(Path.Combine(root,"dock-state.txt"),lines.ToString());
 }
 internal static List<DockEntry> DisplayEntries(List<DockEntry> entries,string icon){
  if(entries.Count<=32)return new List<DockEntry>(entries);
  var visible=entries.Take(31).ToList();var remainder=entries.Skip(31).ToList();
  visible.Add(new DockEntry{id=OverflowId,name="更多应用 · "+remainder.Count,icon=icon,path="",arguments="",windows=remainder.SelectMany(e=>e.windows).ToList(),active=remainder.Any(e=>e.active),attention=remainder.Any(e=>e.attention)});return visible;
 }
 internal static DockEntry WorkspaceEntry(string icon){return new DockEntry{id=WorkspaceId,name="应用中心",icon=icon,path="",arguments="",pinned=true,system=true};}
 string WorkspaceIcon(){var path=Path.Combine(root,"dock-icons","workspace.png");if(!File.Exists(path)){using(var bitmap=new Bitmap(128,128))using(var g=Graphics.FromImage(bitmap))using(var brush=new SolidBrush(Color.FromArgb(232,243,255))){g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;for(int y=0;y<3;y++)for(int x=0;x<3;x++)g.FillEllipse(brush,17+x*34,17+y*34,25,25);bitmap.Save(path,ImageFormat.Png);}}return path;}
 string OverflowIcon(){var path=Path.Combine(root,"dock-icons","more-apps.png");if(!File.Exists(path)){using(var bitmap=new Bitmap(64,64))using(var g=Graphics.FromImage(bitmap))using(var brush=new SolidBrush(Color.FromArgb(224,235,248)))using(var ink=new SolidBrush(Color.FromArgb(39,59,83))){g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.FillEllipse(brush,2,2,60,60);for(int i=0;i<3;i++)g.FillEllipse(ink,14+i*14,28,8,8);bitmap.Save(path,ImageFormat.Png);}}return path;}
 static void MoreMenu(string root){
  var entries=new JavaScriptSerializer().Deserialize<List<DockEntry>>(File.ReadAllText(Path.Combine(root,"dock-state.json"))).Where(e=>e.id!=OverflowId).Skip(31).ToList();
  using(Hold(root))using(var owner=new Form())using(var menu=new ContextMenuStrip()){
   owner.FormBorderStyle=FormBorderStyle.None;owner.ShowInTaskbar=false;owner.StartPosition=FormStartPosition.Manual;owner.Bounds=new Rectangle(Cursor.Position,new Size(1,1));owner.Opacity=0;
   foreach(var entry in entries){var id=entry.id;menu.Items.Add(entry.name,null,(s,e)=>Click(root,id,false));}
   menu.Closed+=(s,e)=>owner.Close();owner.Shown+=(s,e)=>menu.Show(Cursor.Position);Application.Run(owner);
  }
 }
 static string B64(string s){return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));}
 static bool Valid(DockWindow w){if(w==null||w.handle==0||w.pid==0||String.IsNullOrEmpty(w.path))return false;uint pid;GetWindowThreadProcessId(new IntPtr(w.handle),out pid);return pid==w.pid&&String.Equals(ProcessPath(pid),w.path,StringComparison.OrdinalIgnoreCase);}
 public static void Activate(DockWindow w){if(!Valid(w))return;var h=new IntPtr(w.handle);if(IsIconic(h))ShowWindowAsync(h,9);SetForegroundWindow(h);}
 internal static DockClickAction ResolveClick(int windows,bool foreground,bool minimized,bool forceNew){
  if(forceNew||windows==0)return DockClickAction.Launch;
  if(foreground&&!minimized)return DockClickAction.Minimize;
  if(windows>1)return DockClickAction.ChooseWindow;
  return DockClickAction.Activate;
 }
 static readonly string installedSkinRoot=ReadInstalledSkinRoot();
 static string ReadInstalledSkinRoot(){var configured=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"skin-path.txt");return Path.GetFullPath(File.Exists(configured)?File.ReadAllText(configured).Trim():Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Rainmeter","Skins","PrismGlass"));}
 internal static bool IsPrismDock(string className,string title,string skinDirectory=null){
  if(className!="RainmeterMeterWindow"||String.IsNullOrEmpty(title))return false;
  var prefix=(skinDirectory??installedSkinRoot).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(!title.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!title.EndsWith("\\Main.ini",StringComparison.OrdinalIgnoreCase))return false;
  string name=title.Substring(prefix.Length,title.Length-prefix.Length-9);
  return name.Equals("Dock",StringComparison.OrdinalIgnoreCase)||(name.StartsWith("Dock-monitor-",StringComparison.OrdinalIgnoreCase)&&name.Length>13&&name.Substring(13).All(Char.IsDigit));
 }
 public static void ObserveForeground(){
  var foreground=GetForegroundWindow();long handle=foreground.ToInt64();var now=DateTime.UtcNow;
  if(handle==foregroundHistory.current){foregroundHistory.Observe(handle,null,false,now);return;}
  var cl=new StringBuilder(256);GetClassName(foreground,cl,256);var title=new StringBuilder(1024);GetWindowText(foreground,title,1024);
  uint pid;GetWindowThreadProcessId(foreground,out pid);bool dock=IsPrismDock(cl.ToString(),title.ToString())&&String.Equals(Path.GetFileName(ProcessPath(pid)),"Rainmeter.exe",StringComparison.OrdinalIgnoreCase);
  var owner=GetAncestor(foreground,3);if(owner==IntPtr.Zero)owner=foreground;GetWindowThreadProcessId(owner,out pid);
  var window=handle==0||dock?null:new DockWindow{handle=owner.ToInt64(),pid=pid,path=ProcessPath(pid)};
  foregroundHistory.Observe(handle,window,dock,now);
 }
 public static void ObserveActivation(IntPtr window){
  // Shell messages can arrive late; sample the current window instead of erasing a newer observation.
  ObserveForeground();
 }
 static bool Foreground(DockWindow window,bool dockClick){
  var foreground=GetForegroundWindow();if(foreground.ToInt64()==window.handle||GetAncestor(foreground,3).ToInt64()==window.handle)return true;
  if(!dockClick)return false;ObserveForeground();var previous=foregroundHistory.PreviousFor(foreground.ToInt64(),DateTime.UtcNow);
  return previous!=null&&previous.handle==window.handle&&previous.pid==window.pid&&String.Equals(previous.path,window.path,StringComparison.OrdinalIgnoreCase)&&Valid(previous);
 }
 static List<DockWindow> WindowsFor(string path,string arguments){var target=new DockPin{path=path,arguments=arguments??""};return ReadWindows().Where(w=>Matches(target,w)).ToList();}
 static void Interact(string folder,DockEntry entry,bool forceNew){
  var windows=entry.windows.Where(Valid).ToList();
  var active=windows.FirstOrDefault(w=>Foreground(w,folder!=null)&&!IsIconic(new IntPtr(w.handle)));
  var action=ResolveClick(windows.Count,active!=null,false,forceNew);
  if(folder!=null)try{AtomicWrite(Path.Combine(folder,"dock-click.json"),new JavaScriptSerializer().Serialize(new{at=DateTime.UtcNow.ToString("o"),id=entry.id,action=action.ToString(),windows=windows.Count,foreground=GetForegroundWindow().ToInt64(),target=active==null?0:active.handle}));}catch(IOException){}catch(UnauthorizedAccessException){}
  if(folder!=null)foregroundHistory.Invalidate(); // A Dock activation belongs to one click only.
  if(action==DockClickAction.Launch){Launch(entry.path,entry.arguments);return;}
  if(action==DockClickAction.Minimize){if(Valid(active))ShowWindowAsync(new IntPtr(active.handle),6);return;}
  if(action==DockClickAction.Activate){Activate(windows[0]);return;}
  entry.windows=windows;var area=Screen.FromPoint(Cursor.Position).WorkingArea;
  var anchor=new Rectangle(area.Left,area.Bottom-24,area.Width,24);
  if(folder==null){
   if(applicationPicker!=null&&!applicationPicker.IsDisposed)applicationPicker.Close();
   var picker=new DockPreview(entry,anchor,false,true);applicationPicker=picker;
   picker.FormClosed+=(s,e)=>{if(applicationPicker==picker)applicationPicker=null;picker.Dispose();};
   try{picker.Show();}catch{if(applicationPicker==picker)applicationPicker=null;picker.Dispose();throw;}return;
  }
  using(Hold(folder))using(var picker=new DockPreview(entry,anchor,false,true))picker.ShowDialog();
 }
 public static void OpenApplication(string path,string arguments,string name){
  if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("应用入口路径无效。");
  Interact(null,new DockEntry{path=path,arguments=arguments??"",name=name,pinned=true,windows=WindowsFor(path,arguments)},false);
 }
 public DockEntry Entry(string id){if(previous.Length==0)return null;var e=json.Deserialize<List<DockEntry>>(previous).FirstOrDefault(x=>x.id==id);if(e!=null)e.windows=e.windows.Where(Valid).ToList();return e;}
 public static void Click(string root,string id,bool forceNew){
  if(id==WorkspaceId){AtomicWrite(Path.Combine(root,"workspace.request"),"apps");return;}
  if(id==OverflowId){MoreMenu(root);return;}
  try{var entries=new JavaScriptSerializer().Deserialize<List<DockEntry>>(File.ReadAllText(Path.Combine(root,"dock-state.json")));var entry=entries.FirstOrDefault(e=>e.id==id);if(entry==null)return;
   entry.windows=WindowsFor(entry.path,entry.arguments);
   Interact(root,entry,forceNew);
  }catch(Exception ex){File.WriteAllText(Path.Combine(root,"dock-error.txt"),ex.Message);}
 }

 public static void Menu(string root,string id){
  if(id==WorkspaceId){using(Hold(root))using(var owner=new Form())using(var menu=new ContextMenuStrip()){owner.FormBorderStyle=FormBorderStyle.None;owner.ShowInTaskbar=false;owner.StartPosition=FormStartPosition.Manual;owner.Bounds=new Rectangle(Cursor.Position,new Size(1,1));owner.Opacity=0;menu.Items.Add("应用中心",null,(s,e)=>Click(root,WorkspaceId,false));menu.Items.Add("PrismGlass 设置",null,(s,e)=>AtomicWrite(Path.Combine(root,"workspace.request"),"settings"));menu.Items.Add("Windows 设置",null,(s,e)=>Process.Start(new ProcessStartInfo("ms-settings:"){UseShellExecute=true}));menu.Closed+=(s,e)=>owner.Close();owner.Shown+=(s,e)=>menu.Show(Cursor.Position);Application.Run(owner);}return;}
  if(id==OverflowId){MoreMenu(root);return;}
  try{
   var serializer=new JavaScriptSerializer();var entry=serializer.Deserialize<List<DockEntry>>(File.ReadAllText(Path.Combine(root,"dock-state.json"))).FirstOrDefault(e=>e.id==id);if(entry==null)return;
   using(Hold(root))using(var owner=new Form())using(var menu=new ContextMenuStrip()){
    owner.FormBorderStyle=FormBorderStyle.None;owner.ShowInTaskbar=false;owner.StartPosition=FormStartPosition.Manual;owner.Bounds=new Rectangle(Cursor.Position,new Size(1,1));owner.Opacity=0;
    owner.ContextMenuStrip=menu;menu.Font=new Font("Microsoft YaHei UI",10);menu.Items.Add(entry.name).Enabled=false;
    menu.Items.Add("打开 / 切换窗口",null,(s,e)=>Click(root,id,false));
    menu.Items.Add("新建窗口",null,(s,e)=>Click(root,id,true));
    menu.Items.Add("打开文件位置",null,(s,e)=>Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+entry.path+"\""){UseShellExecute=true}));
    menu.Items.Add(new ToolStripSeparator());
    Action<int> change=direction=>{try{if(direction==0){if(entry.pinned)Unpin(root,id);else Pin(root,entry.path,entry.arguments,entry.name,entry.icon);}else MoveBy(root,id,direction);}catch(Exception ex){MessageBox.Show(ex.Message,"Dock 设置未保存",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};
    menu.Items.Add(entry.pinned?"从 Dock 取消固定":"固定到 Dock",null,(s,e)=>change(0));
    if(entry.pinned){menu.Items.Add("向左移动",null,(s,e)=>change(-1));menu.Items.Add("向右移动",null,(s,e)=>change(1));}
    menu.Closed+=(s,e)=>owner.Close();owner.Shown+=(s,e)=>menu.Show(Cursor.Position);Application.Run(owner);
   }
  }catch(Exception ex){File.WriteAllText(Path.Combine(root,"dock-error.txt"),ex.ToString());}
 }
 internal static List<DockPin> ReadPins(string folder){
  var file=Path.Combine(folder,"pins.json");if(!File.Exists(file))return new List<DockPin>();
  var result=new JavaScriptSerializer().Deserialize<List<DockPin>>(File.ReadAllText(file));
  if(result==null||result.Any(p=>p==null||String.IsNullOrEmpty(p.id)||String.IsNullOrEmpty(p.path))||result.Select(p=>p.id).Distinct(StringComparer.Ordinal).Count()!=result.Count)throw new InvalidDataException("Dock 固定项配置损坏，原文件已保留。");
  return result;
 }
 static string LockName(string folder){uint value=2166136261;foreach(char c in Path.GetFullPath(folder).ToUpperInvariant())value=unchecked((value^c)*16777619);return "Local\\PrismGlassDock-"+value.ToString("x8");}
 static void AtomicWrite(string path,string value){
  var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temp,value,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 static void WithLock(string folder,Action action){using(var mutex=new Mutex(false,LockName(folder))){bool held=false;try{try{held=mutex.WaitOne(5000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("Dock 正在保存，请稍后重试。");action();}finally{if(held)mutex.ReleaseMutex();}}}
 static void ChangePins(string folder,Action<List<DockPin>> change){
  WithLock(folder,()=>{var list=ReadPins(folder);var serializer=new JavaScriptSerializer();var before=serializer.Serialize(list);change(list);var after=serializer.Serialize(list);if(before!=after)AtomicWrite(Path.Combine(folder,"pins.json"),after);});
 }
 static bool SameTarget(DockPin pin,string path,string arguments){var a=LaunchIdentity(pin.path,pin.arguments);var b=LaunchIdentity(path,arguments);return String.Equals(a[0],b[0],StringComparison.OrdinalIgnoreCase)&&String.Equals(a[1],b[1],StringComparison.Ordinal);}
 static void Position(List<DockPin> pins,DockPin pin,string beforeId){if(beforeId=="p"+pin.id)return;pins.Remove(pin);int index=String.IsNullOrEmpty(beforeId)?-1:pins.FindIndex(p=>"p"+p.id==beforeId);pins.Insert(index<0?pins.Count:index,pin);}
 public static string Pin(string folder,string path,string arguments,string name,string icon){return Pin(folder,path,arguments,name,icon,null);}
 public static string Pin(string folder,string path,string arguments,string name,string icon,string beforeId){
  if(String.IsNullOrWhiteSpace(path)||path.IndexOfAny(new[]{'\r','\n','\0','"'})>=0)throw new ArgumentException("应用入口路径无效。");
  if(!path.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase))path=Path.GetFullPath(path);
  string result=null;ChangePins(folder,list=>{var pin=list.FirstOrDefault(p=>SameTarget(p,path,arguments));if(pin==null){pin=new DockPin{id=Guid.NewGuid().ToString("N"),path=path,arguments=arguments??"",name=String.IsNullOrWhiteSpace(name)?Path.GetFileNameWithoutExtension(path):name,icon=icon??"",exists=File.Exists(path)||Directory.Exists(path)||path.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase)};list.Add(pin);}if(beforeId!=null)Position(list,pin,beforeId);result="p"+pin.id;});return result;
 }
 public static void Unpin(string folder,string id){ChangePins(folder,list=>list.RemoveAll(p=>"p"+p.id==id));}
 public static void Move(string folder,string id,string beforeId){ChangePins(folder,list=>{var pin=list.FirstOrDefault(p=>"p"+p.id==id);if(pin!=null)Position(list,pin,beforeId);});}
 static void MoveBy(string folder,string id,int delta){ChangePins(folder,list=>{int index=list.FindIndex(p=>"p"+p.id==id);if(index<0)return;var pin=list[index];list.RemoveAt(index);list.Insert(Math.Max(0,Math.Min(list.Count,index+delta)),pin);});}
 static void Launch(string path,string arguments){
  if(path.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase)&&path.IndexOfAny(new[]{'"','\r','\n','\0'})<0){Process.Start(new ProcessStartInfo("explorer.exe","\""+path+"\""){UseShellExecute=true});return;}
  if(!File.Exists(path)&&!Directory.Exists(path))throw new FileNotFoundException("此入口已失效，请重新定位应用。",path);
  Process.Start(new ProcessStartInfo(path,arguments??""){UseShellExecute=true,WorkingDirectory=Directory.Exists(path)?path:Path.GetDirectoryName(path)});
 }
 sealed class DockHold:IDisposable {readonly string file;public DockHold(string folder){file=Path.Combine(folder,"dock-hold-"+Process.GetCurrentProcess().Id+"-"+Guid.NewGuid().ToString("N")+".txt");File.WriteAllText(file,"");}public void Dispose(){try{File.Delete(file);}catch(IOException){}}}
 public static IDisposable Hold(string folder){return new DockHold(folder);}
 static string interactionRoot="";static DateTime interactionRead;static bool interactionState;
 public static bool Interaction(string folder){
  var now=DateTime.UtcNow;if(interactionRoot==folder&&(now-interactionRead).TotalMilliseconds<80)return interactionState;interactionRoot=folder;interactionRead=now;interactionState=false;
  try{foreach(var file in Directory.EnumerateFiles(folder,"dock-hold-*.txt")){int pid;var parts=Path.GetFileName(file).Split('-');if(parts.Length<4||!Int32.TryParse(parts[2],out pid))continue;try{using(var process=Process.GetProcessById(pid)){if(!process.HasExited){interactionState=true;break;}}}catch(ArgumentException){try{File.Delete(file);}catch(IOException){}}}}catch(IOException){}
  return interactionState;
 }
 static long UnixTime(){return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;}
 public static void BeginExternalDrag(string folder,string path,string arguments,string name,string icon){
  var request=new ExternalDragRequest{token=Guid.NewGuid().ToString("N"),expires=UnixTime()+60,entry=new DockPin{path=path,arguments=arguments,name=name,icon=icon}};
  WithLock(folder,()=>AtomicWrite(Path.Combine(folder,"dock-external-drag.json"),new JavaScriptSerializer().Serialize(request)));
 }
 public static void EndExternalDrag(string folder,bool cancelled){WithLock(folder,()=>{var file=Path.Combine(folder,"dock-external-drag.json");if(!File.Exists(file))return;var request=new JavaScriptSerializer().Deserialize<ExternalDragRequest>(File.ReadAllText(file));if(request==null)return;request.ended=true;request.cancelled=cancelled;AtomicWrite(file,new JavaScriptSerializer().Serialize(request));});}
 ExternalDragRequest ExternalRequest(){try{var file=Path.Combine(root,"dock-external-drag.json");return File.Exists(file)?json.Deserialize<ExternalDragRequest>(File.ReadAllText(file)):null;}catch(IOException){return null;}catch(ArgumentException){return null;}}
 static string SafeInstance(string instance){if(String.IsNullOrEmpty(instance)||instance.Length>80||instance.Any(c=>!Char.IsLetterOrDigit(c)&&c!='-'&&c!='_'))throw new ArgumentException("Invalid Dock instance");return instance;}
 internal static string BeforeAt(IList<DockEntry> entries,string source,float localX,float cell,float dpi,out int slot){
  slot=0;for(int i=0;i<entries.Count;i++){var entry=entries[i];if(!entry.pinned)break;if(entry.system){slot=i+1;continue;}if(entry.id==source)continue;if(localX<(16+(i+.5f)*cell)*dpi){slot=i;return entry.id;}slot=i+1;}return "";
 }
 string DragBefore(Rectangle bounds,string instance,Point point,out int slot){
  float dpi=1,cell=(bounds.Width-52f)/Math.Max(1,drag.entries.Count);
  try{var file=Path.Combine(root,instance=="primary"?"dock-layout.json":"dock-layout-"+instance+".json");var geometry=json.Deserialize<DockLayout>(File.ReadAllText(file));if(geometry!=null&&geometry.cell>0&&geometry.dpi>0){dpi=geometry.dpi;cell=geometry.cell;}}catch(IOException){}catch(ArgumentException){}
  return BeforeAt(drag.entries,drag.id,point.X-bounds.Left,cell,dpi,out slot);
 }
 void Feedback(string instance,int slot,int remove){
  string data=(UnixTime()+2)+"|"+slot+"|"+remove+"|"+(drag.external!=null?"1":"0");
  if(drag.feedbackInstance==instance&&data==drag.feedback)return;
  try{
   if(drag.feedbackInstance!=instance&&drag.feedbackInstance.Length>0){File.WriteAllText(Path.Combine(root,"dock-drag-feedback-"+drag.feedbackInstance+".txt"),"");drag.feedback="";}
   AtomicWrite(Path.Combine(root,"dock-drag-feedback-"+instance+".txt"),data);
   drag.feedbackInstance=instance;drag.feedback=data;
  }catch(IOException){/* A reader may hold this transient feedback file; the next tick retries without marking it sent. */}
 }
 void EndDrag(){
  var current=drag;drag=null;if(current==null)return;
  try{if(current.feedbackInstance.Length>0)File.WriteAllText(Path.Combine(root,"dock-drag-feedback-"+current.feedbackInstance+".txt"),"");}catch(IOException){/* Dock.lua expires the existing short feedback lease if a reader prevents clearing it. */}finally{if(current.hold!=null)current.hold.Dispose();}
  pinsStamp=DateTime.MinValue;Refresh();
 }
 public void CancelDrag(){EndDrag();}
 public bool TickDrag(IDictionary<string,Rectangle> docks){
  try{return TickDrag(docks,Cursor.Position,(GetAsyncKeyState(1)&0x8000)!=0,(GetAsyncKeyState(27)&0x8000)!=0,DateTime.UtcNow);}catch(Exception ex){try{File.WriteAllText(Path.Combine(root,"dock-error.txt"),ex.ToString());EndDrag();}catch(IOException){}MessageBox.Show(ex.Message,"Dock 操作未保存",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}
 }
 internal bool TickDrag(IDictionary<string,Rectangle> docks,Point point,bool down,bool escape,DateTime now){
  if(drag==null){
   if((now-requestPoll).TotalMilliseconds<40)return false;requestPoll=now;
   var external=ExternalRequest();
   if(external!=null&&external.token!=externalToken&&external.expires>=UnixTime()){
    externalToken=external.token;if(!external.cancelled&&external.entry!=null&&docks.Count>0)drag=new DockDrag{id="",instance=docks.Keys.First(),token=external.token,external=external.entry,start=point,moved=true,entries=DisplayEntries(json.Deserialize<List<DockEntry>>(previous),"")};
   }
   if(drag==null)foreach(var dock in docks){
    var file=Path.Combine(root,"dock-drag-request-"+SafeInstance(dock.Key)+".txt");if(!File.Exists(file))continue;var data=File.ReadAllText(file).Split('|');File.Delete(file);long time;if(data.Length!=2||!Int64.TryParse(data[0],out time)||Math.Abs(UnixTime()-time)>3)continue;
    var entry=Entry(data[1]);if(entry==null)continue;drag=new DockDrag{id=entry.id,instance=dock.Key,start=point,entries=DisplayEntries(json.Deserialize<List<DockEntry>>(previous),"")};break;
   }
   if(drag==null)return false;drag.hold=Hold(root);
  }
  if(escape){EndDrag();return false;}
  if(drag.external!=null){var external=ExternalRequest();if(external==null||external.token!=drag.token||external.cancelled||external.expires<UnixTime()){EndDrag();return false;}}
  if(!drag.moved&&(Math.Abs(point.X-drag.start.X)>=SystemInformation.DragSize.Width||Math.Abs(point.Y-drag.start.Y)>=SystemInformation.DragSize.Height))drag.moved=true;
  if(drag.id==WorkspaceId&&drag.moved){EndDrag();return false;}
  if(!down&&!drag.released){drag.released=true;drag.release=point;drag.releasedAt=now;}
  if(drag.released)point=drag.release;
  var target=docks.FirstOrDefault(d=>d.Value.Contains(point));bool inside=target.Key!=null;
  if(!inside&&(!docks.ContainsKey(drag.instance)||docks[drag.instance].IsEmpty)){EndDrag();return false;}
  int slot=0;string before=inside?DragBefore(target.Value,target.Key,point,out slot):"";
  if(drag.moved)Feedback(inside?target.Key:drag.instance,slot,inside?0:drag.external==null&&drag.entries.Any(e=>e.id==drag.id&&e.pinned)?1:2);
  if(!drag.released)return true;
  if(drag.external!=null){var external=ExternalRequest();if(!external.ended){if((now-drag.releasedAt).TotalSeconds<1)return true;EndDrag();return false;}}
  var current=drag;
  if(!current.moved){EndDrag();if(inside)Click(root,current.id,false);return false;}
  if(current.external!=null){if(inside)Pin(root,current.external.path,current.external.arguments,current.external.name,current.external.icon,before);}
  else {var entry=current.entries.FirstOrDefault(e=>e.id==current.id);if(entry!=null&&entry.id!=OverflowId){if(inside){if(entry.pinned)Move(root,entry.id,before);else Pin(root,entry.path,entry.arguments,entry.name,entry.icon,before);}else if(entry.pinned)Unpin(root,entry.id);}}
  EndDrag();return false;
 }
}
