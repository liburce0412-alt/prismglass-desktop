using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

internal static class TestWallpaperCapture {
 [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr value);
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static void Reject(Action action,string message){try{action();}catch(IOException){Console.WriteLine("PASS "+message);return;}throw new Exception(message);}
 [STAThread]static int Main(string[] args){string fixture=null;bool keep=false;try{
  SetProcessDpiAwarenessContext(new IntPtr(-4));byte[] pixels=new byte[8*4*4];for(int y=0;y<4;y++)for(int x=0;x<8;x++){int at=(y*8+x)*4;pixels[at]=(byte)(x<4?0:220);pixels[at+2]=(byte)(x<4?210:0);pixels[at+3]=255;}
  Check(LiveGlassBackdrop.MatchesMpv(@"D:\Lively\plugins\mpv\mpv.exe",@"D:\Lively\Lively.exe"),"Lively bundled MP4 player is recognized");
  Check(!LiveGlassBackdrop.MatchesMpv(@"D:\Other\mpv.exe",@"D:\Lively\Lively.exe"),"unrelated mpv does not authorize desktop capture");
  Check(!LiveGlassBackdrop.MatchesMpv(@"D:\LivelyFake\plugins\mpv\mpv.exe",@"D:\Lively\Lively.exe"),"similar installation prefix is rejected");
  Check(SharedWindowCapture.FrameWait(0)==9&&SharedWindowCapture.FrameWait(5)==4&&SharedWindowCapture.FrameWait(40)==1,"120 Hz ceiling charges work time without a catch-up backlog");
  var desktop=new Rectangle(-400,0,800,400);
  using(var right=WallpaperCapture.ToMonitor(pixels,8,4,desktop,new Rectangle(0,0,400,400))){Check(right.Width==400&&right.Height==400,"broker thumbnail expands to actual monitor pixels");Check(right.GetPixel(200,200).B==220&&right.GetPixel(200,200).R==0,"monitor crop uses source visible bounds instead of the full desktop image");Check(right.GetPixel(399,399).B==220,"upscaled far edge does not acquire a black sampling border");}
  using(var left=WallpaperCapture.ToMonitor(pixels,8,4,desktop,new Rectangle(-400,0,400,400)))Check(left.GetPixel(200,200).R==210&&left.GetPixel(200,200).B==0,"negative desktop origin maps to the correct monitor pixels");
  Reject(()=>WallpaperCapture.ToMonitor(new byte[8],8,4,desktop,new Rectangle(0,0,400,400)),"truncated broker frame is rejected");
  Reject(()=>WallpaperCapture.ToMonitor(pixels,8,4,desktop,new Rectangle(200,0,400,400)),"incomplete monitor coverage is rejected");
  if(Array.IndexOf(args,"--live")>=0){
   int cache=Array.IndexOf(args,"--cache");keep=cache>=0;
   fixture=keep?Path.GetFullPath(args[cache+1]):Path.Combine(Path.GetTempPath(),"PrismGlass-dynamic-sample-"+Guid.NewGuid().ToString("N"));if(Directory.Exists(fixture))throw new IOException("Snapshot fixture already exists");string root=Path.Combine(fixture,"LiquidDesktop");Directory.CreateDirectory(root);
   var source=LiveGlassBackdrop.WallpaperSource(System.Windows.Forms.Screen.PrimaryScreen.Bounds);Check(source!=null,"a permitted current Lively desktop root exists");Console.WriteLine("SOURCE hwnd=0x"+source.handle.ToInt64().ToString("X")+" pid="+source.pid+" visible="+source.visible);
   File.WriteAllText(Path.Combine(fixture,"RefreshGlass.ps1"),"throw 'Live snapshot must not fall back to the static wallpaper script.'");
   string result=WallpaperRefresh.Run(root,WallpaperCapture.CaptureDynamic).GetAwaiter().GetResult();Check(result=="动态壁纸当前画面已取样","button workflow reports an actual dynamic snapshot");
   string sample=Path.Combine(fixture,"Glass-Wallpaper-Sample.png"),texture=Path.Combine(root,"wallpaper.png");
   using(var image=new Bitmap(texture)){var screen=System.Windows.Forms.Screen.PrimaryScreen.Bounds;Check(image.Width==screen.Width&&image.Height==screen.Height,"dynamic snapshot is a decodable PNG at physical primary-monitor dimensions");int nonblack=0;for(int i=0;i<64;i++){var color=image.GetPixel((i%8*2+1)*image.Width/16,(i/8*2+1)*image.Height/16);if(color.R>4||color.G>4||color.B>4)nonblack++;}Check(nonblack>0,"saved dynamic wallpaper contains rendered pixels");Console.WriteLine("SNAPSHOT "+image.Width+"x"+image.Height+" nonblackSamplePoints="+nonblack+"/64");
    int baseline=Array.IndexOf(args,"--baseline");if(baseline>=0)using(var old=new Bitmap(args[baseline+1])){long difference=0;for(int i=0;i<64;i++){var a=image.GetPixel((i%8*2+1)*image.Width/16,(i/8*2+1)*image.Height/16);var b=old.GetPixel((i%8*2+1)*old.Width/16,(i/8*2+1)*old.Height/16);difference+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);}Console.WriteLine("BASELINE meanSampleChannelDifference="+(difference/192.0).ToString("F2"));Check(difference>192,"live desktop snapshot differs from the previous static cache");}
   }
   Check(File.GetLastWriteTimeUtc(sample)==File.GetLastWriteTimeUtc(texture)&&new FileInfo(sample).Length==new FileInfo(texture).Length,"button completes only after both local wallpaper caches are published");File.Delete(Path.Combine(fixture,"RefreshGlass.ps1"));
  }
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{if(!keep&&fixture!=null&&Directory.Exists(fixture))Directory.Delete(fixture,true);}}
}
