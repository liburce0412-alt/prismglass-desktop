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
 public static void Configure(Form window,WebView2 view=null){
  window.BackColor=Color.Black;if(view!=null)view.DefaultBackgroundColor=Color.Transparent;
  Action apply=()=>{var margins=new Margins{left=-1,right=-1,top=-1,bottom=-1};if(DwmExtendFrameIntoClientArea(window.Handle,ref margins)!=0)throw new InvalidOperationException("透明玻璃窗口合成不可用。");int border=-2;DwmSetWindowAttribute(window.Handle,34,ref border,4);};
  window.HandleCreated+=(s,e)=>apply();if(window.IsHandleCreated)apply();
 }
 public static Region Clip(GraphicsPath shape,Rectangle bounds){
  var region=new Region(shape);
  // The native region owns hit testing; leave the shader's antialiased fringe intact.
  if(shape.PointCount>0)using(var edge=(GraphicsPath)shape.Clone())using(var pen=new Pen(Color.Black,4)){edge.Widen(pen);region.Union(edge);}
  region.Intersect(bounds);return region;
 }
}
