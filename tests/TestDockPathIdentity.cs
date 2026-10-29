using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

internal static class TestDockPathIdentity {
 static int checks;
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static void Junction(string path,string target){
  string command="New-Item -ItemType Junction -Path '"+path.Replace("'","''")+"' -Target '"+target.Replace("'","''")+"' -ErrorAction Stop | Out-Null";
  using(var process=Process.Start(new ProcessStartInfo("pwsh.exe","-NoProfile -NonInteractive -Command \""+command+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){process.WaitForExit();if(process.ExitCode!=0)throw new Exception("Fixture junction creation failed");}
 }
 static void Shortcut(string path,string target){
  object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")),link=null;
  try{link=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});link.GetType().InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{target});link.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);}
  finally{if(link!=null)Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);}
 }
 [STAThread]static void Main(string[] args){
  if(args.Length!=1||Directory.Exists(args[0]))throw new ArgumentException("Pass a new disposable fixture directory.");
  string root=Path.GetFullPath(args[0]),first=Path.Combine(root,"installed"),second=Path.Combine(root,"unrelated"),alias=Path.Combine(root,"migrated");
  Directory.CreateDirectory(first);Directory.CreateDirectory(second);
  string executable=Path.Combine(first,"fixture.exe"),other=Path.Combine(second,"fixture.exe"),redirected=Path.Combine(alias,"fixture.exe"),shortcut=Path.Combine(root,"fixture.lnk");
  File.WriteAllText(executable,"first");File.WriteAllText(other,"second");Junction(alias,first);
  try{
   var pin=new DockPin{path=redirected,arguments="--unchanged"};var window=new DockWindow{path=executable};
   Check(DockBackend.Matches(new DockPin{path=executable},window),"the original executable identity still matches");
   Check(DockBackend.Matches(pin,window),"a real migrated directory junction matches the physical running executable");
   Check(!DockBackend.Matches(pin,new DockWindow{path=other}),"an unrelated executable with the same basename stays separate");
   Check(pin.path==redirected&&pin.arguments=="--unchanged","matching preserves the exact saved launch target and arguments");
   Shortcut(shortcut,redirected);
   Check(DockBackend.Matches(new DockPin{path=shortcut},window),"a shell shortcut through a migrated directory resolves to the running executable");
   string pinned=DockBackend.Pin(root,redirected,"--profile one","App",Path.Combine(root,"dock-icons","run-0.png"));
   Check(DockBackend.Pin(root,executable,"--profile one","App","")==pinned&&DockBackend.Pin(root,shortcut,"--profile one","App","")!=pinned,"physical aliases reuse a pin while shortcut arguments remain distinct");
   Check(DockBackend.Pin(root,executable,"--profile two","App","")!=pinned&&DockBackend.Pin(root,other,"--profile one","Other","")!=pinned,"different launch arguments and unrelated executables remain separate pins");
   Check(DockBackend.ReadPins(root).Find(p=>"p"+p.id==pinned).icon=="","pinning a running app never persists its reusable icon slot");
   Check(DockBackend.PersistentIcon(root+Path.DirectorySeparatorChar,Path.Combine(root,"dock-icons","run-4.png"))=="","legacy running-slot icons are ignored with the real trailing-root form");
   string custom=Path.Combine(root,"custom","run-0.png");Check(DockBackend.PersistentIcon(root,custom)==custom&&DockBackend.PersistentIcon(root,Path.Combine(root,"dock-icons","custom.png"))==Path.Combine(root,"dock-icons","custom.png"),"custom icons are preserved outside the running cache namespace");
   string missing=Path.Combine(root,"missing.exe");
   Check(DockBackend.Matches(new DockPin{path=missing},new DockWindow{path=missing}),"unknown identical paths retain the previous exact-match behavior");
   Check(!DockBackend.Matches(new DockPin{path=missing},window),"an unresolved different target never matches by basename");
   Check(DockBackend.Matches(new DockPin{path="shell:AppsFolder\\Example.Package!App"},new DockWindow{appId="example.package!app"}),"packaged applications keep their application model identity");
   Directory.Delete(alias);Junction(alias,second);Thread.Sleep(2100);
   Check(!DockBackend.Matches(pin,window)&&DockBackend.Matches(pin,new DockWindow{path=other}),"the bounded cache follows a replaced directory junction");
   Console.WriteLine(checks+" Dock physical path checks passed.");
  }finally{if(Directory.Exists(alias))Directory.Delete(alias);}
 }
}
