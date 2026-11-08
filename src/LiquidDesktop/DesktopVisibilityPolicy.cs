using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
internal sealed class DesktopWindowState {
 public Rectangle Bounds;public bool Application,Maximized,Visible,Minimized,Cloaked,Foreground;public int Style;
 public bool CanCover {get{return Application&&Visible&&!Minimized&&!Cloaked;}}
}
internal static class DesktopVisibilityPolicy {
 public static void KeepInFront(Form component){if(!component.TopMost)component.TopMost=true;}
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
 public static bool AnyFullscreen(IEnumerable<DesktopWindowState> windows,Rectangle screen){
  // Work-area maximization is not fullscreen, even with an auto-hidden taskbar.
  // Inspect the foreground owner only so Alt+Tab out of fullscreen restores the strip.
  foreach(var window in windows)if(window.CanCover&&window.Foreground&&Covers(window.Bounds,screen)&&(!window.Maximized||(window.Style&0x00c40000)==0))return true;
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
