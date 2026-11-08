using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

internal static class WallpaperRefresh {
 static readonly SemaphoreSlim refreshing=new SemaphoreSlim(1,1);
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
  string sample=Path.GetFullPath(Path.Combine(root,"..","Glass-Wallpaper-Sample.png")),temporary=sample+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{image.Save(temporary,ImageFormat.Png);if(File.Exists(sample))File.Replace(temporary,sample,null);else File.Move(temporary,sample);SyncSample(root);}
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 static void DisposeLate(Task<Bitmap> pending){pending.ContinueWith(task=>{if(task.Status==TaskStatus.RanToCompletion){if(task.Result!=null)task.Result.Dispose();}else{var ignored=task.Exception;}},TaskScheduler.Default);}
 public static async Task<string> Run(string root,Func<CancellationToken,Task<Bitmap>> captureDynamic=null){
  if(!refreshing.Wait(0))throw new InvalidOperationException("正在更新玻璃壁纸，请稍候。");
  try{
   // The callback returns null only when no live wallpaper is present. A known
   // player's capture failure must propagate instead of silently saving a still.
   if(captureDynamic!=null){
    using(var cancellation=new CancellationTokenSource(6000)){
     // Native capture setup can itself block before its first await. Keep it off
     // the UI thread and bound the button even if a source stops responding.
     var pending=Task.Run(()=>captureDynamic(cancellation.Token),cancellation.Token);
     if(await Task.WhenAny(pending,Task.Delay(6000)).ConfigureAwait(false)!=pending){cancellation.Cancel();DisposeLate(pending);throw new TimeoutException("动态壁纸取样超时，请重试。");}
     Bitmap image;try{image=await pending.ConfigureAwait(false);}catch(OperationCanceledException){throw new TimeoutException("动态壁纸取样超时，请重试。");}
     if(image!=null){using(image)await Task.Run(()=>SaveSample(root,image)).ConfigureAwait(false);return "动态壁纸当前画面已取样";}
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
