using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

// Capture individual windows behind each surface, without excluding PrismGlass from screenshots or screen sharing.
internal static class LiveGlassBackdrop {
 static volatile bool enabled,suspended;
 static readonly List<OpticalBackdrop> nativeSurfaces=new List<OpticalBackdrop>();
 internal sealed class Source {public IntPtr handle;public uint pid;public Rectangle bounds,visible;}
 [StructLayout(LayoutKind.Sequential)]struct RECT {public int left,top,right,bottom;public Rectangle Rectangle {get{return Rectangle.FromLTRB(left,top,right,bottom);}}}
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
 delegate bool EnumWindow(IntPtr window,IntPtr parameter);
 [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr window,EnumWindow callback,IntPtr parameter);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,IntPtr parameter);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 [DllImport("user32.dll")]static extern bool GetLayeredWindowAttributes(IntPtr window,out uint color,out byte alpha,out uint flags);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT bounds);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")]static extern bool GetWindowDisplayAffinity(IntPtr window,out uint affinity);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder name,int length);
 [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int length);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out RECT value,int length);
 internal static bool Enabled {get{return enabled&&!suspended;}}
 public static void Configure(string root,bool active,bool paused){
  if(enabled==active&&suspended==paused)return;
  enabled=active;suspended=paused;
  foreach(var surface in nativeSurfaces.ToArray())surface.Refresh();
 }
 internal static List<Source> Sources(IntPtr owner,Rectangle area){
  var result=new List<Source>();uint own=(uint)Process.GetCurrentProcess().Id;var visited=new HashSet<IntPtr>();
  for(var window=GetWindow(owner,2);window!=IntPtr.Zero&&visited.Count<512&&visited.Add(window);window=GetWindow(window,2)){
   uint pid;GetWindowThreadProcessId(window,out pid);int cloaked;RECT bounds,visible;
   if(pid==own||!IsWindowVisible(window)||IsIconic(window)||!GetWindowRect(window,out bounds)||(DwmGetWindowAttribute(window,14,out cloaked,4)==0&&cloaked!=0))continue;
   uint color,alphaFlags;byte alpha;if((GetWindowLongPtr(window,-20).ToInt64()&0x80000)!=0&&GetLayeredWindowAttributes(window,out color,out alpha,out alphaFlags)&&(alphaFlags&2)!=0&&alpha==0)continue;
   var name=new StringBuilder(128);GetClassName(window,name,name.Capacity);string type=name.ToString();
   bool desktop=type=="Progman"||type=="WorkerW";
   if(desktop&&!HasLiveWallpaper(window,own,area))continue;
   if(type=="RainmeterMeterWindow"||type=="Shell_TrayWnd"||type=="Shell_SecondaryTrayWnd")continue;
   var rect=bounds.Rectangle;if(rect.Width<1||rect.Height<1||(long)rect.Width*rect.Height>16777216||!rect.IntersectsWith(area))continue;
   uint affinity;if(GetWindowDisplayAffinity(window,out affinity)&&affinity!=0)break;
   if(DwmGetWindowAttribute(window,9,out visible,16)!=0)visible=bounds;
   try{using(var process=Process.GetProcessById((int)pid))if(process.ProcessName=="LiquidDesktop")continue;}catch(ArgumentException){continue;}
   result.Add(new Source{handle=window,pid=pid,bounds=rect,visible=visible.Rectangle});if(rect.Contains(area)||result.Count==12)break;
  }return result;
 }
 static bool HasLiveWallpaper(IntPtr shell,uint own,Rectangle area){
  bool present,blocked;return InspectWallpaper(shell,own,area,out present,out blocked);
 }
 static bool InspectWallpaper(IntPtr shell,uint own,Rectangle area,out bool present,out bool blocked){
  bool found=false,excluded=false;int count=0;var names=new Dictionary<uint,string>();
  EnumChildWindows(shell,(child,unused)=>{
   if(++count>256){excluded=true;return false;}
   uint pid;GetWindowThreadProcessId(child,out pid);uint affinity;
   if(pid==own||(GetWindowDisplayAffinity(child,out affinity)&&affinity!=0))excluded=true;
   string process;if(!names.TryGetValue(pid,out process)){
    try{using(var item=Process.GetProcessById((int)pid))process=item.ProcessName;}catch(ArgumentException){process="";}catch(System.ComponentModel.Win32Exception){process="";}catch(InvalidOperationException){process="";}
    names[pid]=process;
   }
   if(process=="LiquidDesktop"||process=="Rainmeter")excluded=true;
   RECT rect;if(IsWindowVisible(child)&&GetWindowRect(child,out rect)&&rect.Rectangle.IntersectsWith(area)&&(process.StartsWith("Lively.Player.",StringComparison.OrdinalIgnoreCase)||process=="mpv"&&LivelyMpv(pid)))found=true;
   return true;
  },IntPtr.Zero);
  // Lively's child HWND has a zero-sized WGC item; capture its shell root only
  // when it contains a known player and no PrismGlass surfaces or protected child.
  present=found;blocked=found&&excluded||count>256;return found&&!excluded;
 }
 internal static bool MatchesMpv(string player,string lively){
  if(String.IsNullOrEmpty(player)||String.IsNullOrEmpty(lively))return false;
  try{return Path.GetFileName(lively).Equals("Lively.exe",StringComparison.OrdinalIgnoreCase)&&Path.GetFullPath(player).Equals(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(lively)),"plugins","mpv","mpv.exe"),StringComparison.OrdinalIgnoreCase);}catch(ArgumentException){return false;}
 }
 static bool LivelyMpv(uint pid){
  try{using(var player=Process.GetProcessById((int)pid)){
   string path=player.MainModule.FileName;bool matches=false;
   foreach(var lively in Process.GetProcessesByName("Lively"))using(lively){try{matches|=MatchesMpv(path,lively.MainModule.FileName);}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}}
   return matches;
  }}catch(System.ComponentModel.Win32Exception){return false;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}
 }
 internal static Source WallpaperSource(Rectangle area){
  Source result=null;bool blocked=false;int count=0;uint own=(uint)Process.GetCurrentProcess().Id;
  EnumWindows((window,unused)=>{
   if(++count>512){blocked=true;return false;}if(!IsWindowVisible(window)||IsIconic(window))return true;
   var name=new StringBuilder(128);GetClassName(window,name,name.Capacity);if(name.ToString()!="Progman"&&name.ToString()!="WorkerW")return true;
   RECT bounds,visible;if(!GetWindowRect(window,out bounds)||!bounds.Rectangle.IntersectsWith(area))return true;
   bool present,unsafeRoot;bool allowed=InspectWallpaper(window,own,area,out present,out unsafeRoot);
   if(!present){if(unsafeRoot)blocked=true;return true;}uint pid,affinity;GetWindowThreadProcessId(window,out pid);
   if(!allowed||pid==0||(GetWindowDisplayAffinity(window,out affinity)&&affinity!=0)||(long)bounds.Rectangle.Width*bounds.Rectangle.Height>16777216){blocked=true;return true;}
   if(DwmGetWindowAttribute(window,9,out visible,16)!=0)visible=bounds;
   result=new Source{handle=window,pid=pid,bounds=bounds.Rectangle,visible=visible.Rectangle};return false;
  },IntPtr.Zero);
  if(result==null&&blocked)throw new InvalidOperationException("动态壁纸所在桌面不允许安全取样，请保持壁纸运行后重试。");return result;
 }
 internal static Bitmap CaptureWindow(Source source){
  uint pid,affinity;RECT current;GetWindowThreadProcessId(source.handle,out pid);
  if(pid!=source.pid||!IsWindowVisible(source.handle)||IsIconic(source.handle)||!GetWindowRect(source.handle,out current)||current.Rectangle!=source.bounds||(GetWindowDisplayAffinity(source.handle,out affinity)&&affinity!=0))return null;
  var image=new Bitmap(source.bounds.Width,source.bounds.Height,PixelFormat.Format32bppRgb);
  try{using(var graphics=Graphics.FromImage(image)){var dc=graphics.GetHdc();bool captured;try{
    // shortcut: PrintWindow depends on the source app; use per-window WGC if GPU-only apps need broader coverage.
    captured=PrintWindow(source.handle,dc,2);
   }finally{graphics.ReleaseHdc(dc);}if(!captured){image.Dispose();return null;}}
   GetWindowThreadProcessId(source.handle,out pid);if(pid!=source.pid||!GetWindowRect(source.handle,out current)||current.Rectangle!=source.bounds){image.Dispose();return null;}return image;
  }catch{image.Dispose();throw;}
 }
 internal static string Error {get{foreach(var surface in nativeSurfaces)if(surface.Error.Length>0)return surface.Error;return "";}}
 public static void Attach(Form owner,WebView2 view){if(view!=null)Attach(()=>owner,view);}
 public static OpticalBackdrop Attach(Func<Control> owner,WebView2 view){var surface=new OpticalBackdrop(owner,view);nativeSurfaces.Add(surface);view.Disposed+=(s,e)=>nativeSurfaces.Remove(surface);return surface;}
}
