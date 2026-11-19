using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Graphics.Imaging;

// DWM retains minimized content. Capture that relationship through one offscreen
// tool window, then use the frozen pixels in the existing perspective card.
internal sealed class StageThumbnailCapture:IDisposable {
 [StructLayout(LayoutKind.Sequential)]struct RECT {internal int l,t,r,b;internal RECT(Rectangle value){l=value.Left;t=value.Top;r=value.Right;b=value.Bottom;}}
 [StructLayout(LayoutKind.Sequential)]struct SIZE {internal int x,y;}
 [StructLayout(LayoutKind.Sequential)]struct PROPS {internal uint flags;internal RECT dest,source;internal byte opacity;[MarshalAs(UnmanagedType.Bool)]internal bool visible;[MarshalAs(UnmanagedType.Bool)]internal bool client;}
 [DllImport("dwmapi.dll")]static extern int DwmRegisterThumbnail(IntPtr target,IntPtr source,out IntPtr thumbnail);
 [DllImport("dwmapi.dll")]static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail,out SIZE size);
 [DllImport("dwmapi.dll")]static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,ref PROPS props);
 [DllImport("dwmapi.dll")]static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
 [DllImport("dwmapi.dll")]static extern int DwmFlush();
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr source);
 [DllImport("user32.dll")]static extern bool GetWindowDisplayAffinity(IntPtr source,out uint affinity);
 sealed class Host:Form {
  internal Host(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;BackColor=Color.Magenta;}
  protected override bool ShowWithoutActivation{get{return true;}}
  protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
 }
 Host host;IntPtr thumbnail;bool disposed;
 internal bool Busy {get;private set;}
 internal bool TimedOut {get;private set;}
 internal static bool Protected(DockWindow window){uint affinity;return GetWindowDisplayAffinity(new IntPtr(window.handle),out affinity)&&affinity!=0;}
 internal static bool Allowed(DockWindow window){return DockBackend.Valid(window)&&IsWindowVisible(new IntPtr(window.handle))&&!Protected(window);}
 internal async Task<Bitmap> Snapshot(DockWindow window,Func<bool> wanted){
  if(disposed||Busy||!wanted()||!Allowed(window))return null;Busy=true;TimedOut=false;WindowCapture capture=null;bool deferred=false;
  try{
   if(host==null)host=new Host();var area=SystemInformation.VirtualScreen;host.Location=new Point(area.Right+160,area.Top+40);
   if(DwmRegisterThumbnail(host.Handle,new IntPtr(window.handle),out thumbnail)!=0)return null;
   SIZE size;if(DwmQueryThumbnailSourceSize(thumbnail,out size)!=0||size.x<1||size.y<1||(long)size.x*size.y>16777216)return null;
   var crop=DockPreview.SourceCrop(new IntPtr(window.handle),new Size(size.x,size.y));if(crop.Width<1||crop.Height<1)return null;
   double scale=Math.Min(1,1600.0/Math.Max(crop.Width,crop.Height));host.ClientSize=new Size(Math.Max(1,(int)(crop.Width*scale)),Math.Max(1,(int)(crop.Height*scale)));host.Show();
   var props=new PROPS{flags=31,dest=new RECT(host.ClientRectangle),source=new RECT(crop),opacity=255,visible=true,client=false};if(DwmUpdateThumbnailProperties(thumbnail,ref props)!=0)return null;
   // A new capture session after this compositor boundary cannot retain queued
   // frames from the previous relationship when the host is reused.
   var flush=Task.Run(()=>DwmFlush());if(await Task.WhenAny(flush,Task.Delay(250))!=flush||await flush!=0||!wanted()||!Allowed(window))return null;
   capture=new WindowCapture(host.Handle,(uint)Process.GetCurrentProcess().Id,false);var clock=Stopwatch.StartNew();
   while(clock.ElapsedMilliseconds<600&&wanted()&&Allowed(window)){
    var pending=capture.Next();int remaining=Math.Max(1,600-(int)clock.ElapsedMilliseconds);
    if(await Task.WhenAny(pending,Task.Delay(remaining))!=pending){TimedOut=true;deferred=true;ReleaseLate(pending,capture);return null;}
    using(var pixels=await pending){if(pixels!=null){
     if(!wanted()||!Allowed(window))return null;var image=new Bitmap(pixels.PixelWidth,pixels.PixelHeight,PixelFormat.Format32bppArgb);
     try{var data=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.WriteOnly,image.PixelFormat);try{WindowCapture.Copy(pixels,data.Scan0,data.Stride);}finally{image.UnlockBits(data);}
      bool content=false;for(int y=1;y<8&&!content;y++)for(int x=1;x<8;x++){var p=image.GetPixel(image.Width*x/8,image.Height*y/8);if(p.A>0&&(p.R<245||p.G>10||p.B<245)){content=true;break;}}
      if(content&&wanted()&&Allowed(window)){var result=image;image=null;return result;}
     }finally{if(image!=null)image.Dispose();}
    }}await Task.Delay(20);
   }return null;
  }catch(Exception){return null;}finally{if(!deferred)Release(capture);}
 }
 async void ReleaseLate(Task<SoftwareBitmap> pending,WindowCapture capture){try{using(var late=await pending){}}catch(Exception){}finally{Release(capture);}}
 void Release(WindowCapture capture){try{if(capture!=null)capture.Dispose();}finally{if(thumbnail!=IntPtr.Zero){DwmUnregisterThumbnail(thumbnail);thumbnail=IntPtr.Zero;}if(host!=null){host.Hide();if(disposed){host.Dispose();host=null;}}Busy=false;}}
 internal void Cancel(){if(host!=null)host.Hide();}
 public void Dispose(){disposed=true;Cancel();if(!Busy&&host!=null){host.Dispose();host=null;}}
}
