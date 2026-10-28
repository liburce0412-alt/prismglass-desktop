using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

internal static class WallpaperRefresh {
 public static async Task Run(string root){
  string directory=Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)),script=Path.Combine(directory,"RefreshGlass.ps1");
  if(!File.Exists(script))throw new FileNotFoundException("缺少壁纸更新脚本，请修复安装。");
  var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true,RedirectStandardOutput=true};
  using(var process=Process.Start(start)){
   var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
   bool completed=await Task.Run(()=>process.WaitForExit(30000));
   if(!completed){try{process.Kill();}catch{}throw new TimeoutException("更新超时，请稍后重试。");}
   await output;string reason=await error;
   if(process.ExitCode!=0)throw new IOException("壁纸采样失败，请检查当前静态壁纸文件。"+(String.IsNullOrWhiteSpace(reason)?"":"\n"+reason));
   if(!File.Exists(Path.Combine(directory,"Glass-Wallpaper-Sample.png")))throw new IOException("没有生成壁纸采样，请重试。");
  }
 }
}
