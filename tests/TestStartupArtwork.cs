using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class TestStartupArtwork {
 static byte[] Frame(Grid scene,StartupArtwork art,int w,int h,double scale,double t,bool reduced,Brush background){
  scene.Background=background;art.SetFrame(t,reduced);
  var size=new Size(w/scale,h/scale);scene.Measure(size);scene.Arrange(new Rect(size));scene.UpdateLayout();
  var image=new RenderTargetBitmap(w,h,96*scale,96*scale,PixelFormats.Pbgra32);image.Render(scene);
  var bytes=new byte[w*h*4];image.CopyPixels(bytes,w*4,0);return bytes;
 }
 static void Equal(byte[] a,byte[] b,string reason){for(int i=0;i<a.Length;i++)if(a[i]!=b[i])throw new Exception(reason+" at byte "+i);}
 [STAThread]static int Main(string[] args){
  var scene=new Grid();var art=new StartupArtwork();scene.Children.Add(art);
  foreach(double dpi in new[]{1,1.25,1.5,2})foreach(bool reduced in new[]{false,true}){
   byte[] first=null;bool animated=false;foreach(double t in new[]{0,.25,1.5,4.95}){
    var a=Frame(scene,art,640,360,dpi,t,reduced,Brushes.Magenta);var b=Frame(scene,art,640,360,dpi,t,reduced,Brushes.Lime);
    Equal(a,b,"backdrop leak");if(first==null)first=a;else if(reduced)Equal(first,a,"reduced motion changes");else animated|=!a.SequenceEqual(first);
   }
   if(!reduced&&!animated)throw new Exception("normal motion produced only a still frame");
  }
  art.SetProgress(0,"准备桌面");
  var empty=Frame(scene,art,640,360,1,2.5,true,Brushes.Magenta);
  art.SetProgress(.58,"加载桌面组件");
  var partial=Frame(scene,art,640,360,1,2.5,true,Brushes.Magenta);
  if(AutomationProperties.GetName(art)!="加载桌面组件 58%")throw new Exception("incorrect accessible stage and percentage");
  art.SetProgress(1,"桌面已就绪");
  var full=Frame(scene,art,640,360,1,2.5,true,Brushes.Magenta);
  int filled=(315*640+300)*4, pending=(315*640+432)*4;
  if(empty[filled]>=partial[filled]||empty[pending]!=partial[pending]||partial[pending]>=full[pending])
   throw new Exception("progress does not show empty, partial and complete stages");
  Equal(full,Frame(scene,art,640,360,1,0,true,Brushes.Lime),"completed reduced-motion frame changed");
  scene.Children.Remove(art);var restored=Frame(scene,art,640,360,1,0,false,Brushes.Magenta);
  for(int i=0;i<restored.Length;i+=4)if(restored[i]!=255||restored[i+1]!=0||restored[i+2]!=255||restored[i+3]!=255)throw new Exception("backdrop not restored");
  scene.Children.Add(art);
  var start=new ProcessStartInfo(args[0],"-v error -y -f rawvideo -pixel_format bgra -video_size 960x540 -framerate 30 -i pipe:0 -c:v libx264 -crf 18 -pix_fmt yuv420p -movflags +faststart \""+args[1]+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true};
  using(var encoder=Process.Start(start)){
   using(var raw=encoder.StandardInput.BaseStream)for(int n=0;n<150;n++){
    if(n==0)art.SetProgress(0,"正在开启桌面");
    if(n==8)art.SetProgress(.12,"正在准备 Windows 桌面");
    if(n==18)art.SetProgress(.33,"正在启动桌面服务");
    if(n==36)art.SetProgress(.52,"正在加载桌面组件");
    if(n==74)art.SetProgress(.73,"正在同步桌面面板");
    if(n==108)art.SetProgress(.91,"正在交接桌面");
    if(n==128)art.SetProgress(1,"桌面已就绪");
    var bytes=Frame(scene,art,960,540,1,n/30.0,false,Brushes.Black);raw.Write(bytes,0,bytes.Length);
   }
   encoder.WaitForExit();if(encoder.ExitCode!=0)throw new Exception("Preview encode failed");
  }
  Console.WriteLine("PASS 32 paired renders at 100%, 125%, 150%, 200% DPI; opaque in every tested frame; reduced motion stable across time; 0%, 58%, 100% bar coverage; underlying pixels restored; 150 production-rendered frames with staged preview data.");return 0;
 }
}
