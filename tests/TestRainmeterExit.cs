using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// A separate, hidden fixture process owns these Rainmeter-shaped windows.
// No installed Rainmeter process, settings, desktop or wallpaper is touched.
internal static class TestRainmeterExit {
 [StructLayout(LayoutKind.Sequential)]struct CopyData {public IntPtr id;public int bytes;public IntPtr text;}
 delegate IntPtr WindowProc(IntPtr window,uint message,IntPtr sender,IntPtr data);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct WindowClass {public uint style;public WindowProc callback;public int classExtra,windowExtra;public IntPtr instance,icon,cursor,background;public string menu,className;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern ushort RegisterClass(ref WindowClass value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateWindowEx(uint extended,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr DefWindowProc(IntPtr window,uint message,IntPtr sender,IntPtr data);
 [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr sender,IntPtr data);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 static string mode,log;static IntPtr skin;static WindowProc callback;
 static IntPtr Receive(IntPtr window,uint message,IntPtr sender,IntPtr data){
  if(message==0x004a){
   var value=(CopyData)Marshal.PtrToStructure(data,typeof(CopyData));string command=Marshal.PtrToStringUni(value.text);
   if(value.id!=new IntPtr(1)||value.bytes!=(command.Length+1)*2)throw new Exception("Wrong command protocol");
   File.AppendAllText(log,command+Environment.NewLine);
   if(command.StartsWith("!DeactivateConfig",StringComparison.Ordinal)){if(skin!=IntPtr.Zero){DestroyWindow(skin);skin=IntPtr.Zero;}}
   else if(command=="!Quit"){
    if(mode=="stall")Thread.Sleep(500);
    else if(mode=="destroy")PostMessage(window,0x8002,IntPtr.Zero,IntPtr.Zero);
    else if(mode!="ignore")PostMessage(window,0x8001,IntPtr.Zero,IntPtr.Zero);
   }
   return IntPtr.Zero;
  }
  if(message==0x8002){DestroyWindow(window);return IntPtr.Zero;}
  if(message==0x0010||message==0x8001){DestroyWindow(window);Application.ExitThread();return IntPtr.Zero;}
  return DefWindowProc(window,message,sender,data);
 }
 static IntPtr Window(string type,string title){var definition=new WindowClass{callback=callback,className=type};if(RegisterClass(ref definition)==0)throw new Exception("Fixture class registration failed");var window=CreateWindowEx(0,type,title,0,-16000,-16000,1,1,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);if(window==IntPtr.Zero)throw new Exception("Fixture window creation failed");return window;}
 static int Fixture(string nextMode,string target){
  mode=nextMode;log=target;callback=Receive;
  IntPtr control=mode=="skin-only"?IntPtr.Zero:Window("DummyRainWClass","Rainmeter control window");
  if(mode=="foreign"||mode=="prism"||mode=="skin-only")skin=Window("RainmeterMeterWindow",Path.Combine(Path.GetDirectoryName(log),mode=="prism"?"PrismGlass\\Dock\\Main.ini":"UserClock\\Clock.ini"));
  File.WriteAllText(log+".ready",(control==IntPtr.Zero?skin:control).ToInt64().ToString());Application.Run();GC.KeepAlive(callback);return 0;
 }
 sealed class Host : IDisposable {
  public readonly Process Process;public readonly string Log;public readonly IntPtr Handle;
  public Host(string kind,string directory){
   Log=Path.Combine(directory,Guid.NewGuid().ToString("N")+".txt");
   Process=System.Diagnostics.Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--fixture "+kind+" \""+Log+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
   var wait=Stopwatch.StartNew();while(!File.Exists(Log+".ready")&&wait.ElapsedMilliseconds<5000&&!Process.HasExited)Thread.Sleep(20);
   if(!File.Exists(Log+".ready"))throw new Exception("Fixture did not become ready");Handle=new IntPtr(Int64.Parse(File.ReadAllText(Log+".ready")));
  }
  public string Commands {get{return File.Exists(Log)?File.ReadAllText(Log):"";}}
  public void Dispose(){if(!Process.HasExited){PostMessage(Handle,0x0010,IntPtr.Zero,IntPtr.Zero);if(!Process.WaitForExit(1500)){Process.Kill();if(!Process.WaitForExit(1500))throw new Exception("Owned fixture cleanup failed");}}Process.Dispose();}
 }
 static int passed;static void Check(bool condition,string label){if(!condition)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
 static Exception Reject(Action action,string label){Exception error=null;try{action();}catch(Exception failure){error=failure;}Check(error!=null,label);return error;}
 static void Reset(){typeof(RainmeterCommands).GetField("retryAt",BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,DateTime.MinValue);}
 [STAThread] static int Main(string[] args){
  if(args.Length==3&&args[0]=="--fixture")return Fixture(args[1],args[2]);
  string executable=Assembly.GetExecutingAssembly().Location,root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"rainmeter-exit-"+Guid.NewGuid().ToString("N")));
  try{
   Directory.CreateDirectory(root);
   Reject(()=>RainmeterCommands.QuitIfIdle(executable,0,3000),"zero command deadline is rejected");
   Reject(()=>RainmeterCommands.QuitIfIdle(executable,250,0),"zero exit deadline is rejected");
   Reject(()=>RainmeterCommands.QuitIfIdle(executable,250,3000),"missing control endpoint cannot be reported as a confirmed process exit");Reset();
   using(var host=new Host("normal",root)){
    Reject(()=>RainmeterCommands.QuitIfIdle(executable+".other",250,3000),"a different executable path cannot receive quit");Reset();
    Check(!host.Process.HasExited&&host.Commands.Length==0,"path mismatch leaves the isolated host untouched");
    Check(RainmeterCommands.QuitIfIdle(executable,250,3000).Length==0&&host.Process.HasExited&&host.Commands.Trim()=="!Quit","idle configured host receives one native quit and its process exit is confirmed");
   }
   using(var host=new Host("prism",root)){
    RainmeterCommands.Send(executable,"!DeactivateConfig \"PrismGlass\\Dock\"",250);
    Check(RainmeterCommands.QuitIfIdle(executable,250,3000).Length==0&&host.Process.HasExited,"a PrismGlass-only host exits after its own skin window closes");
   }
   using(var host=new Host("foreign",root)){
    var remaining=RainmeterCommands.QuitIfIdle(executable,250,3000);
    Check(remaining.Length==1&&remaining[0].EndsWith("UserClock\\Clock.ini")&&!host.Process.HasExited&&host.Commands.Length==0,"even hidden user skin windows preserve their exact host without sending quit");
    Check(!IsWindowVisible(host.Handle),"preservation test never shows a desktop window");
   }
   using(var other=new Host("skin-only",root))using(var host=new Host("normal",root)){
    Check(RainmeterCommands.QuitIfIdle(executable,250,3000).Length==0&&host.Process.HasExited&&!other.Process.HasExited,"a skin in another process is preserved without blocking the selected host exit");
   }
   using(var host=new Host("ignore",root)){
    var elapsed=Stopwatch.StartNew();var failure=Reject(()=>RainmeterCommands.QuitIfIdle(executable,50,120),"delivery alone does not count as process exit");
    Check(failure is TimeoutException&&elapsed.ElapsedMilliseconds<700&&!host.Process.HasExited,"exit deadline returns while the non-exiting fixture remains intact");
    Reject(()=>RainmeterCommands.QuitIfIdle(executable,50,120),"exit failure cooldown prevents another quit");
    Check(host.Commands.Trim()=="!Quit","exit timeout does not accumulate native quit requests");
   }Reset();
   using(var host=new Host("destroy",root)){
    Reject(()=>RainmeterCommands.QuitIfIdle(executable,50,120),"destroying the control HWND is insufficient while the original process remains alive");
    Check(!host.Process.HasExited,"exit verification uses the held process handle rather than window disappearance");
   }Reset();
   using(var host=new Host("stall",root)){
    var elapsed=Stopwatch.StartNew();Reject(()=>RainmeterCommands.QuitIfIdle(executable,50,120),"an unresponsive control window stops the quit attempt");
    Check(elapsed.ElapsedMilliseconds<350&&!host.Process.HasExited,"native delivery timeout is bounded and never force terminates its target");
   }
   Console.WriteLine("Rainmeter exit: "+passed+" checks passed; only isolated hidden fixture processes were used.");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
  finally{string boundary=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!root.StartsWith(boundary,StringComparison.OrdinalIgnoreCase))throw new IOException("Fixture escaped its output directory");if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
