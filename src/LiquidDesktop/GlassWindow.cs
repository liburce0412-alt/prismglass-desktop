using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

internal static class GlassWindow {
 internal static Action<Form,string> Hint;
 [StructLayout(LayoutKind.Sequential)]struct Margins {public int left,right,top,bottom;}
 [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(IntPtr window,ref Margins margins);
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 internal static string RecoveryError="";
 static bool Apply(Form window){
  var margins=new Margins{left=-1,right=-1,top=-1,bottom=-1};int result=DwmExtendFrameIntoClientArea(window.Handle,ref margins);
  if(result!=0){RecoveryError="透明窗口恢复失败：0x"+result.ToString("X8");return false;}
  int border=-2;DwmSetWindowAttribute(window.Handle,34,ref border,4);RecoveryError="";return true;
 }
 sealed class CompositionFrame:NativeWindow {
  readonly Form window;readonly WebView2 view;readonly Timer recovery=new Timer{Interval=150};int attempts;
  internal CompositionFrame(Form target,WebView2 browser){
   window=target;view=browser;
   recovery.Tick+=(s,e)=>{if(window.IsDisposed||!window.IsHandleCreated){recovery.Stop();return;}if(Apply(window)){recovery.Stop();window.Invalidate(true);if(view!=null&&view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson("{\"type\":\"display-recovery\"}");}else if(++attempts>=10)recovery.Stop();};
   window.HandleCreated+=(s,e)=>AssignHandle(window.Handle);
   window.HandleDestroyed+=(s,e)=>{recovery.Stop();ReleaseHandle();};
   window.Disposed+=(s,e)=>recovery.Dispose();
   if(window.IsHandleCreated)AssignHandle(window.Handle);
  }
  protected override void WndProc(ref Message message){
   int kind=message.Msg;StartupWindowOrder.Constrain(message.HWnd,kind,message.LParam);base.WndProc(ref message);
   // A display mode or DWM reset invalidates glass even when the HWND and size survive.
   if(kind==0x7E||kind==0x31E||kind==0x2E0){attempts=0;recovery.Stop();recovery.Start();}
  }
 }
 public static OpticalBackdrop Configure(Form window,WebView2 view=null){
  var optical=view==null?null:LiveGlassBackdrop.Attach(()=>window,view);
  window.BackColor=Color.Black;if(view!=null)view.DefaultBackgroundColor=Color.Transparent;
  if(view!=null)view.CoreWebView2InitializationCompleted+=(s,e)=>{if(!e.IsSuccess)return;view.CoreWebView2.WebMessageReceived+=(sender,message)=>{string value;try{value=message.TryGetWebMessageAsString();}catch(ArgumentException){return;}if(value!=null&&value.StartsWith("hint:",StringComparison.Ordinal)&&Hint!=null)Hint(window,value);};};
  window.Disposed+=(s,e)=>{if(Hint!=null)Hint(window,"hint:hide");};
  Action apply=()=>{if(!Apply(window))throw new InvalidOperationException("透明玻璃窗口合成不可用。");};
  window.HandleCreated+=(s,e)=>apply();if(window.IsHandleCreated)apply();new CompositionFrame(window,view);return optical;
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
