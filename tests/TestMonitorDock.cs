using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Compile with MonitorDock.cs, MonitorCatalog.cs and RainmeterCommands.cs.
// The isolated control window records commands without starting Rainmeter.
internal static class RuntimePaths {public static string Rainmeter;}
internal sealed class MonitorPreferences {public bool enabled=true,dock=true;public int dockOffset=18;}
internal static class TestMonitorDock {
 [StructLayout(LayoutKind.Sequential)]struct CopyData {public IntPtr id;public int bytes;public IntPtr text;}
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string title);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 delegate IntPtr WindowProc(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct WindowClass {public uint style;public WindowProc callback;public int classExtra,windowExtra;public IntPtr instance,icon,cursor,background;public string menu,className;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern ushort RegisterClass(ref WindowClass value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateWindowEx(uint extended,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr DefWindowProc(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
 [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool UnregisterClass(string name,IntPtr instance);
 sealed class ControlWindow : IDisposable {
  public IntPtr Handle;public int stall;readonly Thread thread;readonly WindowProc callback;
  public ControlWindow(){callback=Receive;using(var ready=new ManualResetEvent(false)){
   thread=new Thread(()=>{var definition=new WindowClass{callback=callback,className="DummyRainWClass"};if(RegisterClass(ref definition)!=0)Handle=CreateWindowEx(0,definition.className,"Rainmeter control window",0,0,0,0,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);ready.Set();if(Handle!=IntPtr.Zero)Application.Run();UnregisterClass(definition.className,IntPtr.Zero);});thread.SetApartmentState(ApartmentState.STA);thread.Start();if(!ready.WaitOne(5000)||Handle==IntPtr.Zero)throw new Exception("Control fixture failed to start");
  }}
  IntPtr Receive(IntPtr window,uint message,IntPtr wParam,IntPtr lParam){
   if(message==0x004A){var data=(CopyData)Marshal.PtrToStructure(lParam,typeof(CopyData));if(data.id!=new IntPtr(1))throw new Exception("Wrong control protocol");File.AppendAllText(log,Marshal.PtrToStringUni(data.text)+Environment.NewLine);if(Interlocked.Exchange(ref stall,0)==1)Thread.Sleep(400);return IntPtr.Zero;}
   if(message==0x0010){DestroyWindow(window);Application.ExitThread();return IntPtr.Zero;}return DefWindowProc(window,message,wParam,lParam);
  }
  public void Dispose(){PostMessage(Handle,0x0010,IntPtr.Zero,IntPtr.Zero);if(!thread.Join(5000))throw new Exception("Control fixture did not stop");GC.KeepAlive(callback);}
 }
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
 static void CheckPointerDelivery(string fixture){
  var commands=new System.Collections.Generic.List<string>();int block=1,fail=0;
  using(var entered=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
   using(var dock=new MonitorDock(fixture,"primary",command=>{
    lock(commands)commands.Add(command);
    if(Interlocked.Exchange(ref block,0)==1){entered.Set();if(!release.WaitOne(3000))return false;}
    return Interlocked.Exchange(ref fail,0)==0;
   })){
    typeof(MonitorDock).GetField("enabled",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dock,true);
    Func<int,bool> delivered=count=>{var wait=Stopwatch.StartNew();while(wait.ElapsedMilliseconds<1500){lock(commands)if(commands.Count>=count)return true;Thread.Sleep(2);}return false;};
    Func<bool> idle=()=>!(bool)typeof(MonitorDock).GetField("pointerSending",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dock);
    try{
     dock.Pointer(.1);Check(entered.WaitOne(1500),"pointer delivery enters its one background native command");
     var elapsed=Stopwatch.StartNew();for(int i=0;i<1000;i++)dock.Pointer(i/1000.0);dock.Pointer(-1);elapsed.Stop();
     Check(elapsed.ElapsedMilliseconds<50,"a stalled Rainmeter endpoint cannot block UI pointer offers");
     release.Set();Check(delivered(2),"the latest exit is delivered after the in-flight pointer");Thread.Sleep(20);
     lock(commands)Check(commands.Count==2&&commands[1].Contains("Pointer(-1)"),"intermediate positions are coalesced instead of replayed after leaving");
     dock.Pointer(-1);dock.Pointer(Double.NaN);dock.Pointer(2);Thread.Sleep(20);
     lock(commands)Check(commands.Count==2,"duplicate exits and invalid positions do not schedule native commands");
     entered.Reset();release.Reset();Interlocked.Exchange(ref block,1);dock.Pointer(.3);Check(entered.WaitOne(1500),"display recovery fixture holds the old pointer acknowledgement");
     dock.DisplaysChanged();dock.Pointer(.3);release.Set();Check(delivered(4),"display recovery resends the latest pointer even when an old acknowledgement has the same value");
     lock(commands)Check(commands[3].Contains("Pointer(0.3)"),"a display change retains the latest new-monitor intention");
     if(!SpinWait.SpinUntil(idle,1500))throw new Exception("Pointer acknowledgement did not settle");
     Interlocked.Exchange(ref fail,1);dock.Pointer(.7);Check(delivered(5)&&SpinWait.SpinUntil(idle,1500),"a failed pointer delivery enters the existing native retry path");
     dock.Pointer(.3);Thread.Sleep(270);dock.Pointer(.3);Check(delivered(6),"an uncertain native timeout cannot suppress returning to the previously acknowledged position");
    }finally{release.Set();}
   }
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
 static void Expire(MonitorDock dock){typeof(MonitorDock).GetField("nextAttempt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dock,DateTime.MinValue);typeof(MonitorDock).GetField("catalogReady",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,DateTime.MinValue);}
 static void CheckSharedRefresh(string fixture,MonitorRecord monitor){
  using(var primary=new MonitorDock(fixture,"primary"))using(var secondary=new MonitorDock(fixture,"monitor-2")){
   var preferences=new MonitorPreferences{dockOffset=31};primary.Configure(monitor,preferences,40);
   typeof(MonitorDock).GetField("nextAttempt",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(primary,DateTime.UtcNow.AddMinutes(1));
   int before=Commands().Length;secondary.Configure(monitor,preferences,40);primary.Configure(monitor,preferences,40);
   Check(Commands().Length==before+1&&Commands()[before]=="!RefreshApp","all monitor updates wait until the delayed global skin refresh finishes");
   Expire(secondary);primary.Configure(monitor,preferences,40);
   string restored=Commands()[Commands().Length-1].Replace("\"","");
   Check(Commands().Length==before+2&&restored.Contains("[!SetVariable DockIconSize 40 PrismGlass\\Dock]")&&restored.Contains("[!SetVariable DockOffset 31 PrismGlass\\Dock]"),"a secondary catalog refresh reapplies primary layout despite its unchanged geometry and retry deadline");
   Check(restored.Contains("[!SetVariable DockPixelScale "+MonitorDock.PixelScale(monitor.Screen.WorkingArea).ToString(System.Globalization.CultureInfo.InvariantCulture)+" PrismGlass\\Dock]"),"primary DPI is explicit instead of inferred from stale layout files");
   primary.Configure(monitor,preferences,40);Check(Commands().Length==before+2,"the shared refresh is consumed once per monitor");
   primary.DisplaysChanged();primary.Configure(monitor,preferences,40);
   Check(Commands().Length==before+3&&Commands().Last().Contains("[!ZPos 1")&&Commands().Last().Contains("ReloadLayout()"),"display reconnect reapplies Dock geometry and stacking even if monitor size is unchanged");
  }
 }
 static void CheckControlFailure(string fixture,MonitorRecord monitor,ControlWindow endpoint){
  Check(RainmeterCommands.Find(RuntimePaths.Rainmeter)==endpoint.Handle,"control endpoint belongs to the configured executable");
  Check(RainmeterCommands.Find(RuntimePaths.Rainmeter+".other")==IntPtr.Zero,"another executable's control window is not used");
  Check(RainmeterCommands.Probe(RuntimePaths.Rainmeter,50),"control readiness uses a bounded native probe");
  using(var dock=new MonitorDock(fixture,"primary")){
   Expire(dock);endpoint.stall=1;int before=Commands().Length;var elapsed=Stopwatch.StartNew();bool timedOut=false;
   try{dock.Configure(monitor,new MonitorPreferences(),48);}catch(TimeoutException){timedOut=true;}
   Check(timedOut&&elapsed.ElapsedMilliseconds<200,"stalled control delivery times out without waiting for a command process");
   Check(!(bool)typeof(MonitorDock).GetField("enabled",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dock)&&(string)typeof(MonitorDock).GetField("signature",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dock)=="","failed activation is not recorded as applied");
   for(int i=0;i<100;i++)try{dock.Configure(monitor,new MonitorPreferences{dockOffset=i},48);}catch(InvalidOperationException){}
   Check(Commands().Length==before+1,"failure cooldown suppresses even changed-layout retries instead of queueing commands");
   Check(!RainmeterCommands.Probe(RuntimePaths.Rainmeter,20),"hung control window is not reported ready");
   string command="!Hide \"PrismGlass\\中文\"";bool rejected=false;try{RainmeterCommands.Send(RuntimePaths.Rainmeter,command+"\0!Quit",50);}catch(ArgumentException){rejected=true;}Check(rejected,"embedded command terminators are rejected");
   Thread.Sleep(450);typeof(RainmeterCommands).GetField("retryAt",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,DateTime.MinValue);
   dock.Configure(monitor,new MonitorPreferences(),48);
   Check(Commands().Length==before+2&&(bool)typeof(MonitorDock).GetField("enabled",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dock),"control delivery resumes after cooldown and applies the current layout once");
  }
 }
 static void DispatchChecks(){
  var dispatch=new RainmeterDispatch();bool result;int sent=0;
  using(var started=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
   Check(!dispatch.TrySend("busy","first",()=>{started.Set();return release.WaitOne(2000);},out result)&&started.WaitOne(1000),"slow recipient runs off the submitting thread");
   dispatch.TrySend("visibility","old",()=>{Interlocked.Add(ref sent,10);return true;},out result);
   dispatch.TrySend("removed","gone",()=>{Interlocked.Add(ref sent,100);return true;},out result);
   dispatch.Cancel("removed");
   Func<bool> latest=()=>{Interlocked.Increment(ref sent);return true;};
   dispatch.TrySend("visibility","latest",latest,out result);release.Set();
   Check(SpinWait.SpinUntil(()=>dispatch.TrySend("visibility","latest",latest,out result),2000)&&result&&sent==1,"only the latest surviving intent is delivered and acknowledged");
  }
 }
 [STAThread] static int Main(string[] args){
  DispatchChecks();if(args.Length==1&&args[0]=="--dispatch")return 0;
  if(args.Length==2&&args[0]=="--native"){try{return CheckRainmeterDelivery(args[1]);}catch(Exception ex){Console.Error.WriteLine(ex.GetType().FullName+": "+ex.Message);return 1;}}
  string fixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"monitor-dock-"+Guid.NewGuid().ToString("N"));MonitorDock dock=null;ControlWindow endpoint=null;
  try{
   Directory.CreateDirectory(Path.Combine(fixture,"Dock"));log=Path.Combine(fixture,"commands.txt");RuntimePaths.Rainmeter=Assembly.GetExecutingAssembly().Location;endpoint=new ControlWindow();
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
   dock.Dispose();dock=null;Check(SpinWait.SpinUntil(()=>!Directory.Exists(Path.Combine(fixture,"Dock-monitor-1")),2000),"confirmed closed generated skin is removed after disposal");
   dock=new MonitorDock(fixture,"primary");int beforeClose=Commands().Length;dock.Dispose();dock.Dispose();dock=null;Check(Commands().Length==beforeClose+1&&Commands()[beforeClose]=="!DeactivateConfig \"PrismGlass\\Dock\"","primary Dock exits once with its renderer instead of leaving a legacy bar");Check(File.Exists(Path.Combine(fixture,"Dock","Main.ini")),"disposing primary Dock retains its reusable current skin");
   using(var masked=new MonitorDock(fixture,"primary")){Expire(masked);masked.Configure(monitor,new MonitorPreferences(),48,true);Check(Commands().Last().Contains("[!ZPos 0 \"PrismGlass\\Dock\"]"),"active startup mask keeps a newly activated Dock normal");Expire(masked);masked.Configure(monitor,new MonitorPreferences(),48,false);Check(Commands().Last().Contains("[!ZPos 1 \"PrismGlass\\Dock\"]"),"post-handoff activation restores the normal topmost Dock preference");}
   CheckSharedRefresh(fixture,monitor);CheckPointerDelivery(fixture);CheckCommandDelivery();CheckControlFailure(fixture,monitor,endpoint);
   var retained=new MonitorDock(fixture,"monitor-1");Expire(retained);retained.Configure(monitor,new MonitorPreferences(),48);
   string current=Path.Combine(fixture,"Dock-monitor-1","Main.ini");
   File.WriteAllText(current,"; PrismGlass generated monitor instance NEW\r\n");retained.Dispose();
   Check(retained.Cleanup.Wait(2000)&&File.Exists(current)&&File.ReadAllText(current).Contains("NEW"),"old instance cleanup preserves a different generation");
   var stalled=new MonitorDock(fixture,"monitor-2");Expire(stalled);stalled.Configure(monitor,new MonitorPreferences(),48);
   endpoint.stall=1;stalled.Dispose();
   Check(File.Exists(Path.Combine(fixture,"Dock-monitor-2","Main.ini"))&&!String.IsNullOrEmpty(MonitorDock.CleanupError),"unconfirmed deactivation retains the generated skin and exposes its error");
   Thread.Sleep(450);typeof(RainmeterCommands).GetField("retryAt",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,DateTime.MinValue);
   Console.WriteLine("Completed "+passed+" MonitorDock activation and command checks; installed Rainmeter was never called.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{if(dock!=null)dock.Dispose();if(endpoint!=null)endpoint.Dispose();if(Directory.Exists(fixture)){Directory.Delete(fixture,true);if(Directory.Exists(fixture))throw new IOException("Fixture cleanup failed");}}
 }
}
