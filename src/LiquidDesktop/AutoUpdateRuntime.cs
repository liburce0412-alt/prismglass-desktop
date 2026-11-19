using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal static class AutoUpdateRuntime {
 internal const string UpdateMutex="Local\\PrismGlassApplyUpdate";

 internal static bool IsApplying(){
  Mutex gate;
  if(!Mutex.TryOpenExisting(UpdateMutex,out gate))return false;
  using(gate){
   try{if(!gate.WaitOne(0))return true;}catch(AbandonedMutexException){}
   gate.ReleaseMutex();return false;
  }
 }

 internal static AutoUpdates Create(string root){
  string installation=Path.GetFullPath(Path.Combine(root,".."));
  string script=Path.Combine(installation,"ApplyUpdate.ps1");
  bool managed=false;
  try{
   var record=DesktopSettingsFiles.Map(DesktopSettingsFiles.Read(Path.Combine(installation,"prismglass-install.json")));
   object product,path;
   managed=record.TryGetValue("product",out product)&&Equals(product,"PrismGlass.Desktop")&&record.TryGetValue("root",out path)&&String.Equals(path as string,installation.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)&&File.Exists(script)&&File.Exists(Path.Combine(installation,"update-inventory.json"));
  }catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}catch(UnauthorizedAccessException){}
  if(!managed)return new AutoUpdates(root,Application.ProductVersion,null,null);
  return new AutoUpdates(root,Application.ProductVersion,()=>CanApply(root),staged=>{
   if(!File.Exists(staged)||IsApplying())return false;
   string shell=Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe");
   using(var worker=Process.Start(new ProcessStartInfo(shell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\""){
    UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=installation
   }))return worker!=null;
  });
 }

 static bool CanApply(string root){
  if(IsApplying())return false;
  var state=CompanionProcessState.Read(root);
  return state.pid==0||state.state=="running"&&!state.active;
 }
}
