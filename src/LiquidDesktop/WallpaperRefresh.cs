using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class WallpaperRefresh {
 static readonly SemaphoreSlim refreshing=new SemaphoreSlim(1,1);
 static readonly Dictionary<string,string> monitorKeys=new Dictionary<string,string>();static DateTime nextMonitorRead;
 internal static string MonitorError {get;private set;}
 static Rectangle[] MonitorAreas(){var screens=Screen.AllScreens;var areas=new Rectangle[screens.Length];for(int i=0;i<screens.Length;i++)areas[i]=screens[i].Bounds;return areas;}
 internal static bool CanPrepareStartup {get{return refreshing.CurrentCount>0;}}
 internal static void PrepareStartup(string root){BindDisplays(MonitorCatalog.Read());PrepareStartup(root,MonitorAreas(),SystemInformation.VirtualScreen,Screen.PrimaryScreen.Bounds,MonitorWallpaper.Read);}
 internal static void BindDisplays(List<MonitorRecord> displays){var owners=new Dictionary<Rectangle,string>();foreach(var display in displays)owners[display.Screen.Bounds]=display.Id;MonitorWallpaper.Bind(owners);}
 internal static void PrepareStartup(string root,Rectangle[] areas,Rectangle desktop,Rectangle primary,Func<Rectangle,MonitorWallpaper.State> read){
  if(!refreshing.Wait(0))throw new InvalidOperationException("壁纸取样仍在运行，无法准备启动画面。");
  try{
   MonitorWallpaper.BeginStartup(root);monitorKeys.Clear();nextMonitorRead=DateTime.MinValue;var errors=new List<string>();
   using(var neutral=MonitorWallpaper.Static(new MonitorWallpaper.State{Background=0x5b382a},new Rectangle(0,0,1,1),Rectangle.Empty))MonitorWallpaper.Save(root,new Rectangle(0,0,1,1),neutral,MonitorWallpaper.Neutral);
   foreach(var area in areas){
    Bitmap image;
    try{image=MonitorWallpaper.Static(read(area),area,desktop);}
    catch(Exception error){errors.Add(area+"："+error.Message);image=MonitorWallpaper.Static(new MonitorWallpaper.State{Background=0x5b382a},area,desktop);}
    // Old snapshots may contain Explorer icons; first paint must not reuse them.
    using(image){string source=MonitorWallpaper.Save(root,area,image);if(area==primary)SaveSample(root,temporary=>File.Copy(source,temporary));}
   }
   MonitorError=errors.Count==0?null:"启动暂用干净底色，等待壁纸重新取样。"+String.Join("\n",errors);
  }finally{refreshing.Release();}
 }
 internal sealed class LivelyAssignment {public LivelyDisplay LivelyScreen {get;set;}public string LivelyInfoPath {get;set;}}
 internal sealed class LivelyDisplay {public string DeviceName {get;set;}public bool isStale {get;set;}}
 internal static string LivelySelection(string path,string device){
  if(!File.Exists(path))return "";
  string json;using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
   long length=file.Length;if(length>262144)throw new IOException("Lively 壁纸布局文件超过读取上限。");
   using(var reader=new StreamReader(file)){char[] text=new char[(int)length+1];int count=reader.ReadBlock(text,0,text.Length);if(count==text.Length)throw new IOException("Lively 正在更新壁纸布局。");json=new string(text,0,count);}
  }
  var rows=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=262144}.Deserialize<LivelyAssignment[]>(json);
  if(rows==null)throw new IOException("Lively 壁纸布局暂未就绪。");
  var items=new List<string>();foreach(var row in rows){
   if(row==null||row.LivelyScreen==null||row.LivelyScreen.isStale)continue;
   if(String.Equals(row.LivelyScreen.DeviceName,device,StringComparison.OrdinalIgnoreCase))return row.LivelyInfoPath??"";
   items.Add(row.LivelyInfoPath??"");
  }
  // A single assignment may span every monitor.
  return items.Count==1?items[0]:"";
 }
 internal static string LivelySelection(Rectangle bounds){
  return LivelySelection(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lively Wallpaper","WallpaperLayout.json"),Screen.FromRectangle(bounds).DeviceName);
 }
 internal static void ScheduleMonitors(string root,Func<Rectangle,CancellationToken,Task<Bitmap>> captureDynamic,Func<Rectangle,string> player){
  if(DateTime.UtcNow<nextMonitorRead)return;nextMonitorRead=DateTime.UtcNow.AddSeconds(2);
  if(!refreshing.Wait(0))return;var areas=MonitorAreas();var desktop=SystemInformation.VirtualScreen;var primary=Screen.PrimaryScreen.Bounds;
  Task.Run(async()=>{try{await UpdateMonitors(root,areas,desktop,primary,captureDynamic,false,player).ConfigureAwait(false);MonitorError=null;}catch(Exception error){MonitorError=error.Message;}finally{refreshing.Release();}});
 }
 internal static async Task<string> RunMonitors(string root,Func<Rectangle,CancellationToken,Task<Bitmap>> captureDynamic){
  if(!refreshing.Wait(0))throw new InvalidOperationException("正在更新玻璃壁纸，请稍候。");
  var areas=MonitorAreas();var desktop=SystemInformation.VirtualScreen;var primary=Screen.PrimaryScreen.Bounds;
  try{await Task.Run(()=>UpdateMonitors(root,areas,desktop,primary,captureDynamic,true)).ConfigureAwait(false);MonitorError=null;return "各显示器的壁纸已分别取样";}catch(Exception error){MonitorError=error.Message;throw;}finally{refreshing.Release();}
 }
 static async Task UpdateMonitors(string root,Rectangle[] areas,Rectangle desktop,Rectangle primary,Func<Rectangle,CancellationToken,Task<Bitmap>> captureDynamic,bool force,Func<Rectangle,string> player=null){
  var errors=new List<string>();root=Path.GetFullPath(root);
  foreach(var area in areas)try{
   string name=MonitorWallpaper.Name(area);
   MonitorWallpaper.State state=null;Exception staticError=null;try{state=MonitorWallpaper.Read(area);}catch(Exception error){staticError=error;}
   string path=Path.Combine(root,name),key=state==null?"dynamic|"+desktop:state.Key(desktop),previous;
   if(player!=null)key+="|player:"+player(area);
   if(!force&&File.Exists(path)&&monitorKeys.TryGetValue(path,out previous)&&previous==key)continue;
   using(var image=await ReadDynamic(captureDynamic==null?null:(Func<CancellationToken,Task<Bitmap>>)(token=>captureDynamic(area,token))).ConfigureAwait(false)){
    if(name!=MonitorWallpaper.Name(area))continue;
    if(image!=null){MonitorWallpaper.Save(root,area,image,name);if(area==primary)SaveSample(root,image);}
    else{if(staticError!=null)throw new IOException("无法读取此显示器的静态壁纸。",staticError);using(var still=MonitorWallpaper.Static(state,area,desktop)){MonitorWallpaper.Save(root,area,still,name);if(area==primary)SaveSample(root,still);}}
   }
   monitorKeys[path]=key;
  }catch(Exception error){errors.Add(area+"："+error.Message);}
  if(errors.Count>0)throw new IOException(String.Join("\n",errors));
 }
 public static void SyncSample(string root){
  root=Path.GetFullPath(root);string source=Path.GetFullPath(Path.Combine(root,"..","Glass-Wallpaper-Sample.png")),target=Path.Combine(root,"wallpaper.png");
  var sample=new FileInfo(source);if(!sample.Exists)throw new FileNotFoundException("没有生成壁纸采样，请重试。",source);
  var current=new FileInfo(target);if(current.Exists&&current.Length==sample.Length&&current.LastWriteTimeUtc==sample.LastWriteTimeUtc)return;
  string temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{
   // File.Copy preserves the source timestamp. Readers see either complete version,
   // and every host can use wallpaper.png's timestamp as the texture version.
   File.Copy(source,temporary);
   if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);
  }finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 static void SaveSample(string root,Bitmap image){
  if(image.Width<1||image.Height<1||(long)image.Width*image.Height>16777216)throw new IOException("动态壁纸画面尺寸无效。");
  SaveSample(root,temporary=>image.Save(temporary,ImageFormat.Png));
 }
 static void SaveSample(string root,Action<string> write){
  string sample=Path.GetFullPath(Path.Combine(root,"..","Glass-Wallpaper-Sample.png")),temporary=sample+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{write(temporary);if(File.Exists(sample))File.Replace(temporary,sample,null);else File.Move(temporary,sample);SyncSample(root);}
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 static void DisposeLate(Task<Bitmap> pending){pending.ContinueWith(task=>{if(task.Status==TaskStatus.RanToCompletion){if(task.Result!=null)task.Result.Dispose();}else{var ignored=task.Exception;}},TaskScheduler.Default);}
 static async Task<Bitmap> ReadDynamic(Func<CancellationToken,Task<Bitmap>> captureDynamic){
  if(captureDynamic==null)return null;
  using(var cancellation=new CancellationTokenSource(6000)){
   var pending=Task.Run(()=>captureDynamic(cancellation.Token),cancellation.Token);
   if(await Task.WhenAny(pending,Task.Delay(6000)).ConfigureAwait(false)!=pending){cancellation.Cancel();DisposeLate(pending);throw new TimeoutException("动态壁纸取样超时，请重试。");}
   try{return await pending.ConfigureAwait(false);}catch(OperationCanceledException){throw new TimeoutException("动态壁纸取样超时，请重试。");}
  }
 }
 public static async Task<string> Run(string root,Func<CancellationToken,Task<Bitmap>> captureDynamic=null){
  if(!refreshing.Wait(0))throw new InvalidOperationException("正在更新玻璃壁纸，请稍候。");
  try{
   // The callback returns null only when no live wallpaper is present. A known
   // player's capture failure must propagate instead of silently saving a still.
   if(captureDynamic!=null){
    using(var image=await ReadDynamic(captureDynamic).ConfigureAwait(false)){
     if(image!=null){await Task.Run(()=>SaveSample(root,image)).ConfigureAwait(false);return "动态壁纸当前画面已取样";}
    }
   }
   await Task.Run(()=>RunStatic(root)).ConfigureAwait(false);return "静态壁纸已取样";
  }finally{refreshing.Release();}
 }
 static async Task RunStatic(string root){
  string directory=Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)),script=Path.Combine(directory,"RefreshGlass.ps1");
  if(!File.Exists(script))throw new FileNotFoundException("缺少壁纸更新脚本，请修复安装。");
  string samplePath=Path.Combine(directory,"Glass-Wallpaper-Sample.png");var before=new FileInfo(samplePath);DateTime previous=before.Exists?before.LastWriteTimeUtc:DateTime.MinValue;
  var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true,RedirectStandardOutput=true};
  using(var process=Process.Start(start)){
   var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
   bool completed=await Task.Run(()=>process.WaitForExit(30000));
   if(!completed){try{process.Kill();}catch{}throw new TimeoutException("更新超时，请稍后重试。");}
   await output;string reason=await error;
   if(process.ExitCode!=0)throw new IOException("壁纸采样失败，请检查当前静态壁纸文件。"+(String.IsNullOrWhiteSpace(reason)?"":"\n"+reason));
   var sample=new FileInfo(samplePath);
   if(!sample.Exists||sample.Length==0||sample.LastWriteTimeUtc==previous)throw new IOException("没有生成新的壁纸采样，请重试。");
   SyncSample(root);
  }
 }
}
