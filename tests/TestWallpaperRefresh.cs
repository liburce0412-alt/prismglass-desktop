using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
class TestWallpaperRefresh {
 static void Reject(Action action,string label){try{action();}catch{Console.WriteLine("PASS "+label);return;}throw new Exception(label);}
 static int Main(string[] args){string fixture=Path.Combine(Path.GetTempPath(),"PrismGlass-wallpaper-"+Guid.NewGuid().ToString("N"));try{
  string root=Path.Combine(fixture,"LiquidDesktop"),script=Path.Combine(fixture,"RefreshGlass.ps1"),sample=Path.Combine(fixture,"Glass-Wallpaper-Sample.png");Directory.CreateDirectory(root);
  Reject(()=>WallpaperRefresh.Run(root).GetAwaiter().GetResult(),"missing script reports failure");
  File.WriteAllText(script,"[Console]::Error.WriteLine('fixture failure'); exit 7");
  Reject(()=>WallpaperRefresh.Run(root).GetAwaiter().GetResult(),"hidden PowerShell failure is propagated");
  File.WriteAllText(script,"exit 0");Reject(()=>WallpaperRefresh.Run(root).GetAwaiter().GetResult(),"missing sample is not reported as success");
  File.WriteAllBytes(sample,new byte[128]);File.SetLastWriteTimeUtc(sample,new DateTime(2001,1,1,0,0,0,DateTimeKind.Utc));
  Reject(()=>WallpaperRefresh.Run(root).GetAwaiter().GetResult(),"unchanged old sample is not reported as a fresh sampling");
  string texture=Path.Combine(root,"wallpaper.png");WallpaperRefresh.SyncSample(root);
  if(new FileInfo(texture).Length!=128||File.GetLastWriteTimeUtc(texture)!=File.GetLastWriteTimeUtc(sample))throw new Exception("Initial texture copy did not preserve sample version");
  File.WriteAllBytes(sample,new byte[256]);File.SetLastWriteTimeUtc(sample,new DateTime(2002,1,1,0,0,0,DateTimeKind.Utc));
  using(var held=new FileStream(texture,FileMode.Open,FileAccess.Read,FileShare.Read)){
   Reject(()=>WallpaperRefresh.SyncSample(root),"blocked atomic replacement reports failure");
   if(held.Length!=128||new FileInfo(texture).Length!=128)throw new Exception("Failed replacement damaged the previous texture");
  }
  if(Directory.GetFiles(root,"wallpaper.png.*.tmp").Length!=0)throw new Exception("Failed replacement left a temporary file");
  WallpaperRefresh.SyncSample(root);
  if(new FileInfo(texture).Length!=256||File.GetLastWriteTimeUtc(texture)!=File.GetLastWriteTimeUtc(sample))throw new Exception("Replacement did not publish the complete new texture version");
  using(var held=new FileStream(texture,FileMode.Open,FileAccess.Read,FileShare.Read))WallpaperRefresh.SyncSample(root);
  Console.WriteLine("PASS atomic replacement preserves old texture on failure, retries successfully and skips an already synchronized version");
  string skin=Path.Combine(fixture,"skins");Directory.CreateDirectory(skin);File.WriteAllText(Path.Combine(root,"skin-path.txt"),skin);
  if(args.Length>0)File.Copy(args[0],script,true);
  else File.WriteAllText(script,"[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'Glass-Wallpaper-Sample.png'),(New-Object byte[] 128))");
  WallpaperRefresh.Run(root+Path.DirectorySeparatorChar).GetAwaiter().GetResult();
  if(!File.Exists(sample)||new FileInfo(sample).Length<100)throw new Exception("Sampling failed");
  if(new FileInfo(texture).Length!=new FileInfo(sample).Length||File.GetLastWriteTimeUtc(texture)!=File.GetLastWriteTimeUtc(sample))throw new Exception("Run returned before publishing the sampled texture");
  Console.WriteLine(args.Length>0?"PASS production sampling via Windows PowerShell 5.1 creates an image in an isolated install":"PASS successful Windows PowerShell 5.1 sampling process publishes its output");
  File.WriteAllText(script,"[Console]::Error.WriteLine('static fallback must not run'); exit 7");
  var pending=new TaskCompletionSource<Bitmap>();bool cancellable=false;
  var refresh=WallpaperRefresh.Run(root,token=>{cancellable=token.CanBeCanceled;return pending.Task;});
  Reject(()=>WallpaperRefresh.Run(root).GetAwaiter().GetResult(),"concurrent button updates are rejected without starting another sampler");
  var frame=new Bitmap(32,18);using(var graphics=Graphics.FromImage(frame))graphics.Clear(Color.FromArgb(42,180,87));pending.SetResult(frame);
  string dynamicResult=refresh.GetAwaiter().GetResult();
  if(!cancellable||dynamicResult!="动态壁纸当前画面已取样")throw new Exception("Dynamic result or timeout token missing");
  using(var saved=new Bitmap(sample))using(var published=new Bitmap(texture))if(saved.Width!=32||saved.Height!=18||saved.GetPixel(15,9).ToArgb()!=Color.FromArgb(42,180,87).ToArgb()||published.GetPixel(15,9)!=saved.GetPixel(15,9))throw new Exception("Dynamic snapshot was not atomically published to both caches");
  Console.WriteLine("PASS dynamic frame takes precedence over static script and publishes PNG plus an accurate result");
  DateTime dynamicStamp=File.GetLastWriteTimeUtc(sample);
  Reject(()=>WallpaperRefresh.Run(root,token=>{throw new IOException("dynamic frame unavailable");}).GetAwaiter().GetResult(),"known dynamic capture failure is propagated instead of falling back to static wallpaper");
  Reject(()=>WallpaperRefresh.Run(root,token=>{throw new OperationCanceledException();}).GetAwaiter().GetResult(),"cancelled dynamic capture reports a timeout");
  if(File.GetLastWriteTimeUtc(sample)!=dynamicStamp||Directory.GetFiles(fixture,"*.tmp").Length!=0)throw new Exception("Failed dynamic sampling changed the cached image or leaked a temporary file");
  File.WriteAllText(script,"[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'Glass-Wallpaper-Sample.png'),(New-Object byte[] 128))");
  if(WallpaperRefresh.Run(root,token=>Task.FromResult<Bitmap>(null)).GetAwaiter().GetResult()!="静态壁纸已取样")throw new Exception("No dynamic source did not select the static sampler");
  Console.WriteLine("PASS absent dynamic source uses static sampling with a distinct result");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(Directory.Exists(fixture))Directory.Delete(fixture,true);}}
}
