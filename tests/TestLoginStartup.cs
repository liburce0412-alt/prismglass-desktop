using System;
using System.IO;

class TestLoginStartup {
 static int checks;
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS: "+label);}
 static void Reject(Action action,string label){bool failed=false;try{action();}catch{failed=true;}Check(failed,label);}
 [STAThread]static int Main(){string fixture=Path.Combine(Path.GetTempPath(),"PrismGlass-startup-test-"+Guid.NewGuid().ToString("N"));try{
  string root=Path.Combine(fixture,"安装 空格🙂","LiquidDesktop"),folder=Path.Combine(fixture,"启动目录🙂"),file=Path.Combine(folder,"PrismGlass.lnk");Directory.CreateDirectory(root);Directory.CreateDirectory(folder);
  File.WriteAllText(Path.Combine(root,"LiquidDesktop.exe"),"inert fixture");File.WriteAllText(Path.Combine(Path.GetDirectoryName(root),"DesktopMode.ps1"),"throw 'test must never launch'");
  Check(!LoginStartup.ReadFrom(root,folder,false).enabled,"No startup item means disabled by default");
  Check(!File.Exists(file),"Reading does not create a startup item");
  Check(!LoginStartup.ReadFrom(root,folder,true).registered,"A stale Windows approval record cannot register a missing item");
  Check(LoginStartup.Arguments(root)==LoginStartup.Arguments(root+Path.DirectorySeparatorChar),"Runtime base directory trailing separator uses the same parent script");
  LoginStartup.SetIn(root+Path.DirectorySeparatorChar,folder,true);var state=LoginStartup.ReadFrom(root,folder,false);
  Check(state.enabled&&state.registered&&!state.systemDisabled,"Enabling writes a valid Unicode shortcut and reads it back");
  Check(LoginStartup.Arguments(root).Contains("-ExecutionPolicy Bypass -File \""),"Startup uses scoped policy and a quoted script path");
  Check(LoginStartup.Arguments(root).EndsWith("-Mode Beauty"),"Login invokes the complete desktop startup lifecycle");
  LoginStartup.SetIn(root,folder,true);Check(LoginStartup.ReadFrom(root,folder,false).enabled,"Enabling twice preserves one valid item");
  state=LoginStartup.ReadFrom(root,folder,true);Check(!state.enabled&&state.registered&&state.systemDisabled,"Windows-disabled registration is not reported as enabled");
  Check(LoginStartup.Disabled(new byte[]{3,0,0,0}),"Disabled Windows approval is recognized");
  Check(!LoginStartup.Disabled(new byte[]{2,0,0,0})&&!LoginStartup.Disabled(null),"Enabled or missing Windows approval stays enabled");
  string otherRoot=Path.Combine(fixture,"other","LiquidDesktop");Directory.CreateDirectory(otherRoot);
  Check(LoginStartup.ReadFrom(otherRoot,folder,false).error.Length>0,"A different installation is reported without mutation");
  Reject(()=>LoginStartup.SetIn(otherRoot,folder,true),"Cannot overwrite another installation startup item");
  Reject(()=>LoginStartup.SetIn(otherRoot,folder,false),"Cannot delete another installation startup item");
  Check(LoginStartup.ReadFrom(root,folder,false).enabled,"Other installation rejection retains the original item");
  LoginStartup.SetIn(root,folder,false);Check(!File.Exists(file)&&!LoginStartup.ReadFrom(root,folder,false).enabled,"Disabling removes only this startup link");
  LoginStartup.SetIn(root,folder,false);Check(!File.Exists(file),"Disabling twice is harmless");
  File.Delete(Path.Combine(root,"LiquidDesktop.exe"));Reject(()=>LoginStartup.SetIn(root,folder,true),"Missing installation files prevent registration");
  Check(!File.Exists(file),"Failed registration leaves startup disabled");
  File.WriteAllText(file,"not a shortcut");Reject(()=>LoginStartup.SetIn(root,folder,false),"Malformed existing startup file is preserved");Check(File.ReadAllText(file)=="not a shortcut","Malformed startup file remains unchanged");
  Check(Directory.GetFiles(folder,"*.tmp").Length==0,"Temporary shortcut files are cleaned up");
  Console.WriteLine(checks+" login startup checks passed.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}finally{if(Directory.Exists(fixture))Directory.Delete(fixture,true);}}
}
