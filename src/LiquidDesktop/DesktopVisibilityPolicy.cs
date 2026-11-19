using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
internal sealed class DesktopWindowState {
 public Rectangle Bounds;public bool Application,Maximized,Visible,Minimized,Cloaked,Foreground;public int Style;
 public bool CanCover {get{return Application&&Visible&&!Minimized&&!Cloaked;}}
}
internal static class DesktopVisibilityPolicy {
 public static void KeepInFront(Form component){StartupWindowOrder.KeepInFront(component);}
 public static bool ComponentHidden(bool enabled,bool locked,bool displayOff,bool autoHide,bool covered){return !enabled||locked||displayOff||autoHide&&covered;}
 public static void SetComponentVisible(Form component,bool hidden,bool initialized){if(hidden&&initialized){if(component.Visible)component.Hide();}else if(!hidden){KeepInFront(component);if(!component.Visible)component.Show();}}
 public static bool IsApplicationSurface(int extendedStyle,string className=""){return className!="tooltips_class32"&&(extendedStyle & (0x08000000|0x00000020))==0;}
 public static bool Covered(Rectangle window,Rectangle screen,Rectangle work,bool application,bool maximized){
  if(!application||window.Width<=0||window.Height<=0)return false;
  return Covers(window,screen)||(maximized&&Covers(window,work));
 }
 public static bool AnyCovered(IEnumerable<DesktopWindowState> windows,Rectangle screen,Rectangle work){
  foreach(var window in windows)if(Covered(window.Bounds,screen,work,window.CanCover,window.Maximized))return true;
  return false;
 }
 public static bool StageCovered(IEnumerable<DesktopWindowState> windows,Rectangle screen,Rectangle work){
  // EnumWindows supplies Z order; each display has its own foremost application.
  foreach(var window in windows){if(!window.CanCover)continue;var overlap=Rectangle.Intersect(window.Bounds,screen);if(overlap.Width<=8||overlap.Height<=8)continue;return Covered(window.Bounds,screen,work,true,window.Maximized);}
  return false;
 }
 public static bool StageEdgeEligible(IEnumerable<DesktopWindowState> windows,Rectangle screen,Rectangle work){
  foreach(var window in windows){if(!window.CanCover)continue;var overlap=Rectangle.Intersect(window.Bounds,screen);if(overlap.Width<=8||overlap.Height<=8)continue;
   return window.Maximized&&(window.Style&0x00c00000)==0x00c00000&&Covers(window.Bounds,work);}
  return false;
 }
 public static bool OverlapsDock(IEnumerable<DesktopWindowState> windows,Rectangle dock){
  return OverlapsComponent(windows,dock);
 }
 public static bool OverlapsComponent(IEnumerable<DesktopWindowState> windows,Rectangle component){
  if(component.Width<=0||component.Height<=0)return false;
  foreach(var window in windows){if(!window.CanCover)continue;var overlap=Rectangle.Intersect(window.Bounds,component);if(overlap.Width>8&&overlap.Height>8)return true;}
  return false;
 }
 static bool Covers(Rectangle window,Rectangle area){return window.Left<=area.Left+2&&window.Top<=area.Top+2&&window.Right>=area.Right-2&&window.Bottom>=area.Bottom-2;}
}

internal static class StartupWindowOrder {
 const int Destroy=0x82;
 static readonly IntPtr Topmost=new IntPtr(-1),NotTopmost=new IntPtr(-2);
 static readonly string launcher=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"PrismGlass.exe");
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr w,out int pid);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr w);

 [DllImport("user32.dll")]static extern int GetWindowLongW(IntPtr w,int index);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr w,IntPtr after,int x,int y,int width,int height,uint flags);
 [ThreadStatic]static Dictionary<IntPtr,Action> pending;
 [ThreadStatic]static Timer restore;
 static IntPtr LowestMask(){
  IntPtr lowest=IntPtr.Zero,w=IntPtr.Zero;
  while((w=FindWindowEx(IntPtr.Zero,w,null,"PrismGlass · 正在开启桌面"))!=IntPtr.Zero){
   if(!IsWindowVisible(w))continue;
   int pid;GetWindowThreadProcessId(w,out pid);
   try{using(var p=Process.GetProcessById(pid)){if(String.Equals(p.MainModule.FileName,launcher,StringComparison.OrdinalIgnoreCase))lowest=w;}}
   catch(ArgumentException){}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
  }
  return lowest;
 }
 internal static bool MaskVisible(){return LowestMask()!=IntPtr.Zero;}
 internal static void RestoreTopmost(IntPtr w,Action property=null){
  if(pending==null)pending=new Dictionary<IntPtr,Action>();
  if(property!=null||!pending.ContainsKey(w))pending[w]=property;
  if(restore!=null)return;
  restore=new Timer{Interval=40};restore.Tick+=(s,e)=>{
   if(pending.Count>0&&LowestMask()!=IntPtr.Zero)return;
   restore.Stop();restore.Dispose();restore=null;
   var windows=pending;pending=null;
   foreach(var entry in windows){if(entry.Value!=null)entry.Value();else if((GetWindowLongW(entry.Key,-20)&8)==0)SetWindowPos(entry.Key,Topmost,0,0,0,0,0x13);}
  };restore.Start();
 }
 internal static void KeepInFront(Form form){
  if(form.TopMost&&form.Visible)return;
  if(!MaskVisible()){if(!form.TopMost)form.TopMost=true;return;}
  if(form.TopMost)form.TopMost=false;
  if(form.IsHandleCreated)RestoreTopmost(form.Handle,()=>{if(!form.IsDisposed)form.TopMost=true;});
  else {form.HandleCreated-=DeferFirstShow;form.HandleCreated+=DeferFirstShow;}
 }
 static void DeferFirstShow(object sender,EventArgs e){var f=(Form)sender;f.HandleCreated-=DeferFirstShow;RestoreTopmost(f.Handle,()=>{if(!f.IsDisposed)f.TopMost=true;});}
 internal static IntPtr InsertAfter(IntPtr window,IntPtr requested){
  if(!MaskVisible())return requested;
  if(requested==Topmost)RestoreTopmost(window);
  return NotTopmost;
 }
 internal static void Constrain(IntPtr window,int message,IntPtr position){
  if(message==Destroy&&pending!=null)pending.Remove(window);
 }
 internal static IntPtr Hook(IntPtr window,int message,IntPtr w,IntPtr l,ref bool handled){Constrain(window,message,l);return IntPtr.Zero;}
}