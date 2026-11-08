using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

internal static class GlassWindow {
 [StructLayout(LayoutKind.Sequential)]struct Margins {public int left,right,top,bottom;}
 [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(IntPtr window,ref Margins margins);
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 public static OpticalBackdrop Configure(Form window,WebView2 view=null){
  var optical=view==null?null:LiveGlassBackdrop.Attach(()=>window,view);
  window.BackColor=Color.Black;if(view!=null)view.DefaultBackgroundColor=Color.Transparent;
  Action apply=()=>{var margins=new Margins{left=-1,right=-1,top=-1,bottom=-1};if(DwmExtendFrameIntoClientArea(window.Handle,ref margins)!=0)throw new InvalidOperationException("透明玻璃窗口合成不可用。");int border=-2;DwmSetWindowAttribute(window.Handle,34,ref border,4);};
  window.HandleCreated+=(s,e)=>apply();if(window.IsHandleCreated)apply();return optical;
 }
 public static Region Clip(GraphicsPath shape,Rectangle bounds){
  var region=new Region(shape);
  // Shader coverage spans 0.8 physical pixels around its contour. Keep that fringe,
  // without the old two-pixel apron of unshaded wallpaper around every native window.
  if(shape.PointCount>0)using(var edge=(GraphicsPath)shape.Clone())using(var pen=new Pen(Color.Black,2)){edge.Widen(pen);region.Union(edge);}
  region.Intersect(bounds);return region;
 }
 public static void Round(GraphicsPath path,float x,float y,float width,float height,float radius){
  // Match glassSize = halfSize - 2 in the shared shader; inputs are physical pixels.
  x+=2;y+=2;width-=4;height-=4;if(width<=0||height<=0)return;
  float d=Math.Min(radius*2,Math.Min(width,height));path.StartFigure();
  if(d<=0){path.AddRectangle(new RectangleF(x,y,width,height));return;}
  path.AddArc(x,y,d,d,180,90);path.AddArc(x+width-d,y,d,d,270,90);path.AddArc(x+width-d,y+height-d,d,d,0,90);path.AddArc(x,y+height-d,d,d,90,90);path.CloseFigure();
 }
}
