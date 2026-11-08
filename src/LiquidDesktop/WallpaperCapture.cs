using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// Explicit refresh-button snapshot only. Continuous glass never writes frames.
internal static class WallpaperCapture {
 internal static async Task<Bitmap> CaptureDynamic(CancellationToken cancellation){
  cancellation.ThrowIfCancellationRequested();
  var area=Screen.PrimaryScreen.Bounds;var source=LiveGlassBackdrop.WallpaperSource(area);
  if(source==null)return null;
  if(!await WindowCapture.Borderless().ConfigureAwait(false))throw new InvalidOperationException("Windows 未允许动态壁纸捕获，请允许本机玻璃取样后重试。");
  cancellation.ThrowIfCancellationRequested();
  using(var capture=SharedWindowCapture.Acquire(source.handle,source.pid)){
   while(true){
    cancellation.ThrowIfCancellationRequested();
    if(!String.IsNullOrEmpty(capture.Error))throw new IOException("动态壁纸取样失败："+capture.Error);
    if(!capture.Valid)throw new IOException("动态壁纸窗口已关闭或改变，请重试。");
    using(var frame=capture.Latest(-1)){
     if(frame!=null){
      cancellation.ThrowIfCancellationRequested();
      var current=LiveGlassBackdrop.WallpaperSource(area);
      if(current==null||current.handle!=source.handle||current.pid!=source.pid||current.visible!=source.visible)throw new IOException("动态壁纸来源在取样时发生变化，请重试。");
      return ToMonitor(frame.Pixels,frame.Width,frame.Height,source.visible,area);
     }
    }
    await Task.Delay(50,cancellation).ConfigureAwait(false);
   }
  }
 }
 internal static Bitmap ToMonitor(byte[] pixels,int width,int height,Rectangle visible,Rectangle area){
  if(width<1||height<1||(long)width*height>16777216||pixels==null||pixels.LongLength<(long)width*height*4)throw new IOException("动态壁纸返回了不完整的画面。");
  if(area.Width<1||area.Height<1||(long)area.Width*area.Height>16777216||!visible.Contains(area))throw new IOException("动态壁纸画面没有完整覆盖主显示器。");
  using(var frame=new Bitmap(width,height,PixelFormat.Format32bppPArgb)){
   var bits=frame.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
   try{int row=checked(width*4);for(int y=0;y<height;y++)Marshal.Copy(pixels,y*row,IntPtr.Add(bits.Scan0,y*bits.Stride),row);}
   finally{frame.UnlockBits(bits);}
   var output=new Bitmap(area.Width,area.Height,PixelFormat.Format32bppRgb);
   try{
    using(var graphics=Graphics.FromImage(output))using(var attributes=new ImageAttributes()){
     graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;attributes.SetWrapMode(WrapMode.TileFlipXY);
     float sx=(float)width/visible.Width,sy=(float)height/visible.Height;
     graphics.DrawImage(frame,new Rectangle(0,0,area.Width,area.Height),(area.Left-visible.Left)*sx,(area.Top-visible.Top)*sy,area.Width*sx,area.Height*sy,GraphicsUnit.Pixel,attributes);
    }
    return output;
   }catch{output.Dispose();throw;}
  }
 }
}
