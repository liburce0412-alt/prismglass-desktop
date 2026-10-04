using System.Drawing;
internal static class DesktopVisibilityPolicy {
 public static bool Covered(Rectangle window,Rectangle screen,Rectangle work,bool application,bool maximized){
  if(!application||window.Width<=0||window.Height<=0)return false;
  return Covers(window,screen)||(maximized&&Covers(window,work));
 }
 static bool Covers(Rectangle window,Rectangle area){return window.Left<=area.Left+2&&window.Top<=area.Top+2&&window.Right>=area.Right-2&&window.Bottom>=area.Bottom-2;}
}
