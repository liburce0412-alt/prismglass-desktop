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
 IntPtr thumbnail;
 public void Outline(float[][] points){if(points==null||points.Length<3)return;using(var path=new GraphicsPath()){var polygon=new PointF[points.Length];for(int i=0;i<points.Length;i++)polygon[i]=new PointF(points[i][0],points[i][1]);path.AddPolygon(polygon);var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}}
 protected override bool ShowWithoutActivation {get{return true;}}
 protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
 protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}base.WndProc(ref m);}
 public DockPlate(){Text="Liquid Dock Glass";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Enabled=false;}
 public void Sync(IntPtr source,IntPtr dock,Rectangle bounds,Rectangle screen){
  if(dock==IntPtr.Zero){Hide();return;}
  bounds=Rectangle.Inflate(bounds,26,26);
  if(Bounds!=bounds){Bounds=bounds;using(var path=new GraphicsPath()){int d=48;path.AddArc(26,26,d,d,180,90);path.AddArc(Width-d-26,26,d,d,270,90);path.AddArc(Width-d-26,Height-d-26,d,d,0,90);path.AddArc(26,Height-d-26,d,d,90,90);path.CloseFigure();var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}}
  if(thumbnail==IntPtr.Zero&&DwmRegisterThumbnail(Handle,source,out thumbnail)!=0)throw new InvalidOperationException("Dock glass thumbnail unavailable");
  var props=new PROPS{flags=31,destination=new RECT(0,0,Width,Height),source=new RECT(bounds.X-screen.X,bounds.Y-screen.Y,bounds.Right-screen.X,bounds.Bottom-screen.Y),opacity=255,visible=true,client=true};
  if(DwmUpdateThumbnailProperties(thumbnail,ref props)!=0)throw new InvalidOperationException("Dock glass update failed");
  SetWindowPos(Handle,dock,bounds.X,bounds.Y,Width,Height,0x50);
 }
 protected override void Dispose(bool disposing){if(thumbnail!=IntPtr.Zero){DwmUnregisterThumbnail(thumbnail);thumbnail=IntPtr.Zero;}base.Dispose(disposing);}
}
