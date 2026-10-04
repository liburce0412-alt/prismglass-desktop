using System.Collections.Generic;
using System.Drawing;
internal sealed class DesktopWindowState {
 public Rectangle Bounds;public bool Application,Maximized,Visible,Minimized,Cloaked;
 public bool CanCover {get{return Application&&Visible&&!Minimized&&!Cloaked;}}
}
internal static class DesktopVisibilityPolicy {
 public static bool IsApplicationSurface(int extendedStyle){return (extendedStyle & (0x08000000|0x00000020))==0;}
 public static bool Covered(Rectangle window,Rectangle screen,Rectangle work,bool application,bool maximized){
  if(!application||window.Width<=0||window.Height<=0)return false;
  return Covers(window,screen)||(maximized&&Covers(window,work));
 }
 public static bool AnyCovered(IEnumerable<DesktopWindowState> windows,Rectangle screen,Rectangle work){
  foreach(var window in windows)if(Covered(window.Bounds,screen,work,window.CanCover,window.Maximized))return true;
  return false;
 }
 public static bool OverlapsDock(IEnumerable<DesktopWindowState> windows,Rectangle dock){
  foreach(var window in windows){if(!window.CanCover)continue;var overlap=Rectangle.Intersect(window.Bounds,dock);if(overlap.Width>8&&overlap.Height>8)return true;}
  return false;
 }
 static bool Covers(Rectangle window,Rectangle area){return window.Left<=area.Left+2&&window.Top<=area.Top+2&&window.Right>=area.Right-2&&window.Bottom>=area.Bottom-2;}
}
