using System;using System.Drawing;using System.IO;using System.Threading.Tasks;using System.Windows.Forms;
internal static class TestMonitorWallpaper {
 static int checks;static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Reject(Action action,string name){bool rejected=false;try{action();}catch(IOException){rejected=true;}Check(rejected,name);}
 static void Picture(string path,Color color){using(var bitmap=new Bitmap(40,80)){using(var graphics=Graphics.FromImage(bitmap))graphics.Clear(color);bitmap.Save(path);}}
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [STAThread]static int Main(string[] args){string fixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"monitor-wallpaper-"+Guid.NewGuid().ToString("N")),root=Path.Combine(fixture,"app");Directory.CreateDirectory(root);try{
  SetProcessDpiAwarenessContext(new IntPtr(-4));string red=Path.Combine(fixture,"red.png"),blue=Path.Combine(fixture,"blue.png");Picture(red,Color.Red);Picture(blue,Color.Blue);Picture(Path.Combine(root,"wallpaper.png"),Color.Gray);
  var left=new Rectangle(-320,-40,320,200);var right=new Rectangle(0,0,160,120);var desktop=Rectangle.Union(left,right);
  Check(MonitorWallpaper.For(root,right).File=="wallpaper.png","legacy sample remains a usable fallback until a monitor sample is ready");
  using(var image=MonitorWallpaper.Static(new MonitorWallpaper.State{Source=red,Position=4},left,desktop)){MonitorWallpaper.Save(root,left,image);Check(image.Width==320&&image.Height==200&&image.GetPixel(160,100).R==255,"negative-origin monitor keeps its own red wallpaper at physical dimensions");}
  using(var image=MonitorWallpaper.Static(new MonitorWallpaper.State{Source=blue,Position=4},right,desktop)){MonitorWallpaper.Save(root,right,image);Check(image.Width==160&&image.Height==120&&image.GetPixel(80,60).B==255,"different-size second monitor samples its independent blue wallpaper");}
  DateTime same=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc);File.SetLastWriteTimeUtc(Path.Combine(root,MonitorWallpaper.Name(left)),same);File.SetLastWriteTimeUtc(Path.Combine(root,MonitorWallpaper.Name(right)),same);
  Check(MonitorWallpaper.For(root,left).File!=MonitorWallpaper.For(root,right).File&&MonitorWallpaper.For(root,left).Version!=MonitorWallpaper.For(root,right).Version,"matching timestamps cannot cause one monitor's cache to be reused on another");
  using(var image=MonitorWallpaper.Static(new MonitorWallpaper.State{Source=blue,Position=3,Background=0x0000ff00},right,desktop))Check(image.GetPixel(2,60).G==255&&image.GetPixel(80,60).B==255,"fit mode preserves the configured background bars");
  var span=Path.Combine(fixture,"span.png");using(var image=new Bitmap(480,240)){using(var graphics=Graphics.FromImage(image)){graphics.Clear(Color.Blue);graphics.FillRectangle(Brushes.Red,0,0,320,240);}image.Save(span);}
  using(var image=MonitorWallpaper.Static(new MonitorWallpaper.State{Source=span,Position=5},left,desktop))Check(image.GetPixel(20,20).R>250,"spanned wallpaper crops the negative-coordinate left screen from the full desktop");
  using(var image=MonitorWallpaper.Static(new MonitorWallpaper.State{Source=span,Position=5},right,desktop))Check(image.GetPixel(80,60).B>250,"spanned wallpaper samples the right screen's portion rather than fitting the whole image again");
  var placement=MonitorWallpaper.Placement(new Size(40,80),right,desktop,0);Check(placement==new RectangleF(60,20,40,80),"center mode retains the image's native dimensions");
  using(var image=MonitorWallpaper.Static(new MonitorWallpaper.State{Source=blue,Position=1},left,desktop))Check(image.GetPixel(0,0).B==255&&image.GetPixel(319,199).B==255,"tile mode covers a negative-origin monitor without black edges");
  string tile=Path.Combine(fixture,"tile.png");using(var image=new Bitmap(2,2)){image.SetPixel(0,0,Color.Red);image.SetPixel(1,0,Color.Green);image.SetPixel(0,1,Color.Blue);image.SetPixel(1,1,Color.White);image.Save(tile);}
  var offset=new Rectangle(-5,-3,8,8);using(var image=MonitorWallpaper.Static(new MonitorWallpaper.State{Source=tile,Position=1},offset,offset))Check(image.GetPixel(0,0).ToArgb()==Color.White.ToArgb()&&image.GetPixel(1,0).ToArgb()==Color.Blue.ToArgb(),"tiled wallpaper is anchored at the primary origin, not the negative virtual-screen corner");
  string saved=Path.Combine(root,MonitorWallpaper.Name(right));byte[] previous=File.ReadAllBytes(saved);
  Reject(()=>{using(var image=MonitorWallpaper.Static(new MonitorWallpaper.State{Source=Path.Combine(fixture,"missing.png"),Position=4},right,desktop))MonitorWallpaper.Save(root,right,image);},"a missing wallpaper source cannot replace the valid monitor sample");
  using(var wrong=new Bitmap(1,1))Reject(()=>MonitorWallpaper.Save(root,right,wrong),"a mismatched capture cannot overwrite the monitor's valid image");
  Check(Convert.ToBase64String(previous)==Convert.ToBase64String(File.ReadAllBytes(saved))&&Directory.GetFiles(root,"*.tmp").Length==0,"failure leaves the previous image and no partial cache file");
  var missingMonitor=new Rectangle(-99999,-99999,24,16);int dynamicCalls=0;
  var update=typeof(WallpaperRefresh).GetMethod("UpdateMonitors",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
  Func<Rectangle,System.Threading.CancellationToken,Task<Bitmap>> dynamicCapture=(area,token)=>{dynamicCalls++;Check(area==missingMonitor,"dynamic capture receives the requested monitor when the static API cannot find it");var image=new Bitmap(area.Width,area.Height);using(var graphics=Graphics.FromImage(image))graphics.Clear(Color.Gold);return Task.FromResult(image);};
  string player="one";
  Action<bool,Func<Rectangle,System.Threading.CancellationToken,Task<Bitmap>>> refresh=(force,capture)=>((Task)update.Invoke(null,new object[]{root,new[]{missingMonitor},desktop,Rectangle.Empty,capture,force,(Func<Rectangle,string>)(area=>player)})).GetAwaiter().GetResult();
  refresh(true,dynamicCapture);string dynamicPath=Path.Combine(root,MonitorWallpaper.For(root,missingMonitor).File);
  using(var image=new Bitmap(dynamicPath))Check(image.GetPixel(10,10).ToArgb()==Color.Gold.ToArgb(),"working dynamic capture is published despite the static wallpaper API failure");
  DateTime dynamicStamp=File.GetLastWriteTimeUtc(dynamicPath);refresh(false,dynamicCapture);Check(dynamicCalls==1&&File.GetLastWriteTimeUtc(dynamicPath)==dynamicStamp,"unchanged monitor sources skip capture and PNG recompression");
  player="two";refresh(false,dynamicCapture);Check(dynamicCalls==2,"player replacement invalidates the static wallpaper key");dynamicStamp=File.GetLastWriteTimeUtc(dynamicPath);
  Reject(()=>refresh(true,(area,token)=>Task.FromResult<Bitmap>(null)),"failure of both sources is reported without publishing the primary fallback as a monitor success");
  Check(File.GetLastWriteTimeUtc(dynamicPath)==dynamicStamp,"failed refresh retains the valid image previously sampled for this monitor");
  string settings=Path.Combine(root,"desktop-settings.json");File.WriteAllText(settings,"preserve-user-settings");
  Picture(Path.Combine(root,MonitorWallpaper.Name(left)),Color.Magenta);Picture(Path.Combine(root,MonitorWallpaper.Name(right)),Color.Magenta);Picture(Path.Combine(root,"wallpaper.png"),Color.Magenta);
  WallpaperRefresh.PrepareStartup(root,new[]{left,right},desktop,right,area=>new MonitorWallpaper.State{Source=area==left?red:blue,Position=4});
  using(var image=new Bitmap(Path.Combine(root,MonitorWallpaper.Name(left))))Check(image.Size==left.Size&&image.GetPixel(10,10).R==255&&image.GetPixel(10,10).B==0,"startup replaces a polluted negative-origin monitor cache from its own wallpaper source");
  using(var image=new Bitmap(Path.Combine(root,MonitorWallpaper.Name(right))))using(var legacy=new Bitmap(Path.Combine(root,"wallpaper.png")))Check(image.Size==right.Size&&image.GetPixel(10,10).B==255&&image.GetPixel(10,10).R==0&&legacy.GetPixel(10,10).B==255&&legacy.GetPixel(10,10).R==0,"first-paint monitor and legacy textures both discard the polluted snapshot");
  string compatibilitySample=Path.Combine(fixture,"Glass-Wallpaper-Sample.png");
  Check(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root,MonitorWallpaper.Name(right))))==Convert.ToBase64String(File.ReadAllBytes(compatibilitySample)),"startup copies the exact primary monitor PNG to the compatibility sample");
  Check(File.ReadAllText(settings)=="preserve-user-settings"&&WallpaperRefresh.MonitorError==null,"startup sampling preserves user settings and reports successful preparation");
  var reconnected=new Rectangle(9000,0,48,32);Picture(Path.Combine(root,MonitorWallpaper.Name(reconnected)),Color.Magenta);
  Check(MonitorWallpaper.For(root,reconnected).File=="wallpaper.png","a screen reconnected after startup cannot reuse an unverified old icon-contaminated cache");
  using(var clean=MonitorWallpaper.Static(new MonitorWallpaper.State{Source=blue,Position=4},reconnected,desktop))MonitorWallpaper.Save(root,reconnected,clean);
  Check(MonitorWallpaper.For(root,reconnected).File==MonitorWallpaper.Name(reconnected),"a freshly published sample enables that monitor's own texture");
  refresh(false,dynamicCapture);Check(dynamicCalls==3,"clean startup preview does not suppress the next dynamic sample with an old cache key");
  WallpaperRefresh.PrepareStartup(root,new[]{right},right,right,area=>{throw new IOException("unavailable source");});
  using(var image=new Bitmap(Path.Combine(root,MonitorWallpaper.Name(right))))Check(image.GetPixel(10,10).ToArgb()==Color.FromArgb(42,56,91).ToArgb()&&!String.IsNullOrEmpty(WallpaperRefresh.MonitorError),"missing startup source uses a clean neutral image and a diagnostic instead of the polluted cache");
  using(var held=new FileStream(Path.Combine(root,MonitorWallpaper.Name(right)),FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>WallpaperRefresh.PrepareStartup(root,new[]{right},right,right,area=>new MonitorWallpaper.State{Source=blue,Position=4}),"unwritable startup cache aborts preparation rather than allowing an untrusted first paint");
  string compatibilityBefore=Convert.ToBase64String(File.ReadAllBytes(compatibilitySample));
  using(var held=new FileStream(compatibilitySample,FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>WallpaperRefresh.PrepareStartup(root,new[]{right},right,right,area=>new MonitorWallpaper.State{Source=blue,Position=4}),"unwritable compatibility output aborts startup preparation");
  Check(Convert.ToBase64String(File.ReadAllBytes(compatibilitySample))==compatibilityBefore&&Directory.GetFiles(root,"*.tmp").Length==0&&Directory.GetFiles(fixture,"*.tmp").Length==0&&File.ReadAllText(settings)=="preserve-user-settings","failed startup preparation leaves valid compatibility bytes without partial files or changed settings");
  if(Array.IndexOf(args,"--current")>=0){
   foreach(var screen in Screen.AllScreens){var state=MonitorWallpaper.Read(screen.Bounds);using(var image=MonitorWallpaper.Static(state,screen.Bounds,SystemInformation.VirtualScreen))Check(image.Size==screen.Bounds.Size,"current Windows monitor source reads without changing wallpaper: "+screen.Bounds);}
   string answer=WallpaperRefresh.RunMonitors(root,(area,token)=>{var image=new Bitmap(area.Width,area.Height);using(var graphics=Graphics.FromImage(image))graphics.Clear(Color.Coral);return Task.FromResult(image);}).GetAwaiter().GetResult();
   Check(answer=="各显示器的壁纸已分别取样","multi-monitor refresh reports success only after the monitor images are published");
   foreach(var screen in Screen.AllScreens)using(var image=new Bitmap(Path.Combine(root,MonitorWallpaper.For(root,screen.Bounds).File)))Check(image.GetPixel(image.Width/2,image.Height/2).ToArgb()==Color.Coral.ToArgb(),"dynamic callback receives and fills the actual target monitor: "+screen.Bounds);
  }
  MonitorWallpaper.BeginStartup(root);
  var owners=new System.Collections.Generic.Dictionary<Rectangle,string>{{right,"red-monitor"}};
  MonitorWallpaper.Bind(owners);string redFile=MonitorWallpaper.Name(right);
  using(var img=MonitorWallpaper.Static(new MonitorWallpaper.State{Source=red},right,desktop))MonitorWallpaper.Save(root,right,img);
  owners[right]="blue-monitor";MonitorWallpaper.Bind(owners);
  Check(MonitorWallpaper.Name(right)!=redFile&&MonitorWallpaper.For(root,right).File==MonitorWallpaper.Neutral,"replacement monitor cannot read a different panel's image before capture succeeds");
  owners[right]="red-monitor";MonitorWallpaper.Bind(owners);
  Check(MonitorWallpaper.For(root,right).File==redFile,"returning panel can reuse its own clean image");
  string layout=Path.Combine(fixture,"WallpaperLayout.json"),first="[{\"LivelyScreen\":{\"DeviceName\":\"left\"},\"LivelyInfoPath\":\"clip-a\"},{\"LivelyScreen\":{\"DeviceName\":\"right\"},\"LivelyInfoPath\":\"clip-b\"}]";
  File.WriteAllText(layout,first);string before=WallpaperRefresh.LivelySelection(layout,"left");
  File.WriteAllText(layout,first.Replace("clip-a","clip-c"));File.SetLastWriteTimeUtc(layout,same);
  Check(before=="clip-a"&&WallpaperRefresh.LivelySelection(layout,"left")=="clip-c"&&WallpaperRefresh.LivelySelection(layout,"right")=="clip-b","same-player clip changes invalidate only the assigned monitor independently of file timestamps");
  Console.WriteLine("Completed "+checks+" monitor wallpaper checks.");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}finally{Directory.Delete(fixture,true);}}
}
