using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
internal sealed class DockPlate:Form {
 [StructLayout(LayoutKind.Sequential)] struct RECT {public int L,T,R,B;public RECT(int l,int t,int r,int b){L=l;T=t;R=r;B=b;}}
 [StructLayout(LayoutKind.Sequential)] struct PROPS {public uint flags;public RECT destination,source;public byte opacity;[MarshalAs(UnmanagedType.Bool)]public bool visible;[MarshalAs(UnmanagedType.Bool)]public bool client;}
 [DllImport("dwmapi.dll")]static extern int DwmRegisterThumbnail(IntPtr dest,IntPtr source,out IntPtr thumb);
 [DllImport("dwmapi.dll")]static extern int DwmUpdateThumbnailProperties(IntPtr thumb,ref PROPS props);
 [DllImport("dwmapi.dll")]static extern int DwmUnregisterThumbnail(IntPtr thumb);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int command);
 public new void Hide(){if(IsHandleCreated)ShowWindow(Handle,0);base.Hide();}
 IntPtr thumbnail,thumbnailSource;int padding=26;
 internal void ResetComposition(){if(thumbnail!=IntPtr.Zero)DwmUnregisterThumbnail(thumbnail);thumbnail=thumbnailSource=IntPtr.Zero;}
 public void Outline(float[][] points){if(points==null||points.Length<3)return;using(var path=new GraphicsPath()){var polygon=new PointF[points.Length];for(int i=0;i<points.Length;i++)polygon[i]=new PointF(points[i][0]-26+padding,points[i][1]-26+padding);path.AddPolygon(polygon);var old=Region;Region=GlassWindow.Clip(path,ClientRectangle);if(old!=null)old.Dispose();}}
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
 protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}int kind=m.Msg;base.WndProc(ref m);if(kind==0x7E||kind==0x31E)ResetComposition();}
 public DockPlate(){Text="Liquid Dock Glass";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Enabled=false;GlassWindow.Configure(this);}
 public void Sync(IntPtr source,IntPtr dock,Rectangle bounds,Rectangle screen,bool foreground=false,byte alpha=255){
  if(dock==IntPtr.Zero){Hide();return;}
  padding=foreground?0:26;bounds=Rectangle.Inflate(bounds,padding,padding);
  if(Bounds!=bounds){Bounds=bounds;using(var path=new GraphicsPath()){int d=Math.Max(1,Math.Min(68,Math.Min(Width-padding*2,Height-padding*2)));path.AddArc(padding,padding,d,d,180,90);path.AddArc(Width-d-padding,padding,d,d,270,90);path.AddArc(Width-d-padding,Height-d-padding,d,d,0,90);path.AddArc(padding,Height-d-padding,d,d,90,90);path.CloseFigure();var old=Region;Region=GlassWindow.Clip(path,ClientRectangle);if(old!=null)old.Dispose();}}
  if(thumbnailSource!=source)ResetComposition();
  if(thumbnail==IntPtr.Zero){if(DwmRegisterThumbnail(Handle,source,out thumbnail)!=0)throw new InvalidOperationException("Dock glass thumbnail unavailable");thumbnailSource=source;}
  var props=new PROPS{flags=31,destination=new RECT(0,0,Width,Height),source=new RECT(bounds.X-screen.X,bounds.Y-screen.Y,bounds.Right-screen.X,bounds.Bottom-screen.Y),opacity=alpha,visible=true,client=true};
  if(DwmUpdateThumbnailProperties(thumbnail,ref props)!=0)throw new InvalidOperationException("Dock glass update failed");
  SetWindowPos(Handle,StartupWindowOrder.InsertAfter(Handle,foreground?new IntPtr(-1):dock),bounds.X,bounds.Y,Width,Height,0x50);
 }
 protected override void Dispose(bool disposing){ResetComposition();base.Dispose(disposing);}
}
