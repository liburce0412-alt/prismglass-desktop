using System;
using System.IO;
class TestWallpaperRefresh {
 static void Reject(Action action,string label){try{action();}catch{Console.WriteLine("PASS "+label);return;}throw new Exception(label);}
 static int Main(string[] args){string fixture=Path.Combine(Path.GetTempPath(),"PrismGlass-wallpaper-"+Guid.NewGuid().ToString("N"));try{
  string root=Path.Combine(fixture,"LiquidDesktop"),script=Path.Combine(fixture,"RefreshGlass.ps1"),sample=Path.Combine(fixture,"Glass-Wallpaper-Sample.png");Directory.CreateDirectory(root);
  Reject(()=>WallpaperRefresh.Run(root).GetAwaiter().GetResult(),"missing script reports failure");
  File.WriteAllText(script,"[Console]::Error.WriteLine('fixture failure'); exit 7");
  Reject(()=>WallpaperRefresh.Run(root).GetAwaiter().GetResult(),"hidden PowerShell failure is propagated");
  File.WriteAllText(script,"exit 0");Reject(()=>WallpaperRefresh.Run(root).GetAwaiter().GetResult(),"missing sample is not reported as success");
  string skin=Path.Combine(fixture,"skins");Directory.CreateDirectory(skin);File.WriteAllText(Path.Combine(root,"skin-path.txt"),skin);
  if(args.Length>0)File.Copy(args[0],script,true);
  else File.WriteAllText(script,"[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'Glass-Wallpaper-Sample.png'),(New-Object byte[] 128))");
  WallpaperRefresh.Run(root+Path.DirectorySeparatorChar).GetAwaiter().GetResult();
  if(!File.Exists(sample)||new FileInfo(sample).Length<100)throw new Exception("Sampling failed");
  Console.WriteLine(args.Length>0?"PASS production sampling via Windows PowerShell 5.1 creates an image in an isolated install":"PASS successful Windows PowerShell 5.1 sampling process publishes its output");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(Directory.Exists(fixture))Directory.Delete(fixture,true);}}
}
