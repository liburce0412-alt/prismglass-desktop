using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Compile with MonitorDock.cs and MonitorCatalog.cs. This executable also acts
// as an inert Rainmeter command recorder; it never calls the installed program.
internal static class RuntimePaths {public static string Rainmeter;}
internal sealed class MonitorPreferences {public bool enabled=true,dock=true;public int dockOffset=18;}
internal static class TestMonitorDock {
 [StructLayout(LayoutKind.Sequential)]struct CopyData {public IntPtr id;public int bytes;public IntPtr text;}
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string title);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 sealed class CommandWindow : NativeWindow {
  public string command;public int bytes;public long id;
  public CommandWindow(){CreateHandle(new CreateParams{Caption="PrismGlass isolated command test",Parent=new IntPtr(-3)});}
  protected override void WndProc(ref Message message){
   if(message.Msg==0x004A){var data=(CopyData)Marshal.PtrToStructure(message.LParam,typeof(CopyData));command=Marshal.PtrToStringUni(data.text);bytes=data.bytes;id=data.id.ToInt64();if(command=="stall")Thread.Sleep(250);message.Result=new IntPtr(command=="reject"?0:1);return;}
   if(message.Msg==0x0010){DestroyHandle();Application.ExitThread();return;}base.WndProc(ref message);
  }
 }
 static void CheckCommandDelivery(){
  CommandWindow endpoint=null;using(var ready=new ManualResetEvent(false)){
   var thread=new Thread(()=>{endpoint=new CommandWindow();ready.Set();Application.Run();});thread.SetApartmentState(ApartmentState.STA);thread.Start();
   try{
    Check(ready.WaitOne(5000),"isolated command endpoint is ready without showing a window");
    string command="!Hide \"PrismGlass\\Dock-monitor-1\"";
    Check(MonitorDock.SendSkinCommand(endpoint.Handle,command)&&endpoint.command==command&&endpoint.id==1&&endpoint.bytes==(command.Length+1)*2,"visibility command uses the Rainmeter Unicode COPYDATA protocol");
    Check(MonitorDock.SendSkinCommand(endpoint.Handle,"!Show \"中文\"")&&endpoint.command=="!Show \"中文\"","Unicode command and show/hide order are preserved");
    Check(!MonitorDock.SendSkinCommand(endpoint.Handle,"reject"),"unacknowledged command can be retried");
    Check(!MonitorDock.SendSkinCommand(IntPtr.Zero,command),"absent skin does not launch a command process");
    var elapsed=Stopwatch.StartNew();bool accepted=MonitorDock.SendSkinCommand(endpoint.Handle,"stall");elapsed.Stop();
    Check(!accepted&&elapsed.ElapsedMilliseconds<200,"unresponsive skin is bounded by a short timeout");
   }finally{if(endpoint!=null)PostMessage(endpoint.Handle,0x0010,IntPtr.Zero,IntPtr.Zero);if(!thread.Join(5000))throw new Exception("Command endpoint did not stop");}
  }
 }
 static int CheckRainmeterDelivery(string executable){
  string fixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"monitor-native-"+Guid.NewGuid().ToString("N")),skins=Path.Combine(fixture,"Skins","PrismGlass"),skin=Path.Combine(skins,"Dock");Process process=null;
  try{
   Directory.CreateDirectory(skin);string config=Path.Combine(fixture,"Rainmeter.ini"),main=Path.Combine(skin,"Main.ini");
   File.WriteAllText(config,"[Rainmeter]\r\nSkinPath="+Path.Combine(fixture,"Skins")+"\\\r\nDisableVersionCheck=1\r\n[PrismGlass\\Dock]\r\nActive=1\r\nWindowX=-16000\r\nWindowY=-16000\r\nKeepOnScreen=0\r\nSavePosition=0\r\nAlwaysOnTop=-2\r\n",System.Text.Encoding.Unicode);
   File.WriteAllText(main,"[Rainmeter]\r\nUpdate=1000\r\n[Invisible]\r\nMeter=Shape\r\nShape=Rectangle 0,0,1,1 | Fill Color 0,0,0,1 | StrokeWidth 0\r\n",System.Text.Encoding.Unicode);
   process=Process.Start(new ProcessStartInfo(executable,"\""+config+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
   IntPtr window=IntPtr.Zero;var wait=Stopwatch.StartNew();while(window==IntPtr.Zero&&wait.ElapsedMilliseconds<10000&&!process.HasExited){Thread.Sleep(50);window=FindWindow("RainmeterMeterWindow",main);}
   Check(window!=IntPtr.Zero&&IsWindowVisible(window),"isolated offscreen Rainmeter skin started");
   using(var dock=new MonitorDock(fixture,"primary")){
    typeof(MonitorDock).GetField("skinRoot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dock,skins);typeof(MonitorDock).GetField("enabled",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dock,true);
    Check(dock.SetVisible(false)&&!IsWindowVisible(window),"live Rainmeter acknowledges hide and hides the exact skin");
    Check(dock.SetVisible(true)&&IsWindowVisible(window),"live Rainmeter acknowledges show and restores the exact skin");
    var elapsed=Stopwatch.StartNew();for(int i=0;i<40;i++){if(!dock.SetVisible(i%2!=0))throw new Exception("Repeated live delivery failed");}elapsed.Stop();
    Check(IsWindowVisible(window),"forty ordered visibility changes preserve the final state");Console.WriteLine("40 direct skin commands: "+elapsed.Elapsed.TotalMilliseconds.ToString("F1")+" ms total; "+(elapsed.Elapsed.TotalMilliseconds/40).ToString("F2")+" ms average.");
   }
   Console.WriteLine("Completed "+passed+" native Rainmeter delivery checks; user profile was not touched.");return 0;
  }finally{if(process!=null){if(!process.HasExited){process.Kill();process.WaitForExit(5000);}process.Dispose();}if(Directory.Exists(fixture)){Directory.Delete(fixture,true);if(Directory.Exists(fixture))throw new IOException("Native fixture cleanup failed");}}
 }
 static int passed;static string log;
 static void Check(bool value,string label){if(!value)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
 static string[] Commands(){return File.Exists(log)?File.ReadAllLines(log):new string[0];}
 static void Expire(MonitorDock dock){typeof(MonitorDock).GetField("nextAttempt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dock,DateTime.MinValue);}
 [STAThread] static int Main(string[] args){
  if(args.Length==2&&args[0]=="--native"){try{return CheckRainmeterDelivery(args[1]);}catch(Exception ex){Console.Error.WriteLine(ex.GetType().FullName+": "+ex.Message);return 1;}}
  if(args.Length>0){File.AppendAllText(Environment.GetEnvironmentVariable("PRISMGLASS_DOCK_TEST_LOG"),String.Join(" ",args)+Environment.NewLine);return 0;}
  string fixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"monitor-dock-"+Guid.NewGuid().ToString("N"));MonitorDock dock=null;
  try{
   Directory.CreateDirectory(Path.Combine(fixture,"Dock"));log=Path.Combine(fixture,"commands.txt");Environment.SetEnvironmentVariable("PRISMGLASS_DOCK_TEST_LOG",log);RuntimePaths.Rainmeter=Assembly.GetExecutingAssembly().Location;
   File.WriteAllText(Path.Combine(fixture,"Dock","Main.ini"),"[Rainmeter]\r\nUpdate=16\r\n[Variables]\r\nDockInstance=primary\r\n");
   File.WriteAllText(Path.Combine(fixture,"skin-path.txt"),fixture);
   dock=new MonitorDock(fixture,"monitor-1");Check((string)typeof(MonitorDock).GetField("skinRoot",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dock)==fixture,"custom installed skin path is used without changing Rainmeter settings");
   var monitor=new MonitorRecord{Id="test-secondary",Instance="monitor-1",Name="fixture",Screen=Screen.PrimaryScreen};var preferences=new MonitorPreferences();
   dock.Configure(monitor,preferences,48);
   Check(Commands().Length==1&&Commands()[0]=="!RefreshApp","new secondary directory refresh is sent alone");
   string generated=File.ReadAllText(Path.Combine(fixture,"Dock-monitor-1","Main.ini"));
   Check(generated.StartsWith("; PrismGlass generated monitor instance")&&generated.Contains("DockInstance=monitor-1"),"secondary skin keeps ownership marker and explicit instance");
   Check(generated.Contains("DockMonitorHeight="+(monitor.Screen.Bounds.Bottom-monitor.Screen.WorkingArea.Top)),"Dock reaches the physical bottom independently of the taskbar work-area reservation");
   dock.Configure(monitor,preferences,48);Check(Commands().Length==1,"activation waits for the delayed registry refresh");
   Expire(dock);dock.Configure(monitor,preferences,48);
   Check(Commands().Length==2&&Commands()[1].StartsWith("[!ActivateConfig")&&!Commands()[1].Contains("RefreshApp")&&Commands()[1].Contains("PrismGlass\\Dock-monitor-1"),"next probe activates the exact secondary config without another global refresh");
   dock.Configure(monitor,preferences,48);Check(Commands().Length==2,"missing window retry is throttled");
   Check(!dock.SetVisible(false)&&Commands().Length==2,"visibility waits for a live skin without spawning Rainmeter");
   Check((DateTime)typeof(MonitorDock).GetField("nextVisibilityAttempt",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dock)>DateTime.UtcNow,"missing or unresponsive skins back off instead of blocking every frame");
   int sequence,alpha;
   Check(MonitorDock.TryIconMessage("1:0",out sequence,out alpha)&&sequence==1&&alpha==0&&MonitorDock.TryIconMessage("2:255",out sequence,out alpha)&&alpha==255,"icon protocol accepts bounded integer opacity and request identity");
   Check(!MonitorDock.TryIconMessage("0:0",out sequence,out alpha)&&!MonitorDock.TryIconMessage("3:256",out sequence,out alpha)&&!MonitorDock.TryIconMessage("3:-1",out sequence,out alpha)&&!MonitorDock.TryIconMessage("3:0:!Quit",out sequence,out alpha)&&!MonitorDock.TryIconMessage("3:0.5",out sequence,out alpha),"icon protocol rejects invalid sequence, bounds and command injection");
   Check(!dock.SetIconOpacity(-1)&&!dock.SetIconOpacity(256)&&Commands().Length==2,"invalid opacity never reaches Rainmeter");
   Expire(dock);dock.Configure(monitor,preferences,48);Check(Commands().Length==3&&Commands()[2].StartsWith("[!ActivateConfig"),"missing window retries even with unchanged settings");
   preferences.enabled=false;dock.Configure(monitor,preferences,48);Check(Commands().Length==4&&Commands()[3].StartsWith("!DeactivateConfig"),"disabling the monitor deactivates its own Dock");
   dock.Configure(monitor,preferences,48);Check(Commands().Length==4,"disabled monitor does not repeatedly activate or deactivate");
   preferences.enabled=true;dock.Configure(monitor,preferences,48);Check(Commands().Length==5&&Commands()[4].StartsWith("[!ActivateConfig"),"reenabling activates without rescanning all skins");
   dock.Dispose();dock=null;Check(!Directory.Exists(Path.Combine(fixture,"Dock-monitor-1")),"owned generated skin is removed on dispose");
   CheckCommandDelivery();Console.WriteLine("Completed "+passed+" MonitorDock activation and command checks; installed Rainmeter was never called.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(dock!=null)dock.Dispose();Environment.SetEnvironmentVariable("PRISMGLASS_DOCK_TEST_LOG",null);if(Directory.Exists(fixture)){Directory.Delete(fixture,true);if(Directory.Exists(fixture))throw new IOException("Fixture cleanup failed");}}
 }
}
