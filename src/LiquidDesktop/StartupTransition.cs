using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

public static class StartupTransition {
 [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 sealed class Mask {
  internal readonly StartupNotice Window;
  internal readonly TaskCompletionSource<bool> Frame=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  internal readonly TaskCompletionSource<bool> Closed=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  internal Mask(StartupNotice window){Window=window;}
 }
 internal static string DesktopModeShell(string script){
  object configured;var record=DesktopSettingsFiles.Map(DesktopSettingsFiles.Read(Path.Combine(Path.GetDirectoryName(script),"prismglass-install.json")));
  if(!record.TryGetValue("powershell",out configured))return Path.Combine(Environment.SystemDirectory,"WindowsPowerShell","v1.0","powershell.exe");
  string path=configured as string,name=Path.GetFileName(path);
  if(!Path.IsPathRooted(path)||!(String.Equals(name,"powershell.exe",StringComparison.OrdinalIgnoreCase)||String.Equals(name,"pwsh.exe",StringComparison.OrdinalIgnoreCase)))throw new IOException("安装记录中的 PowerShell 路径无效。");
  return path;
 }
 public static int Run(string[] args){
  var startupClock=Stopwatch.StartNew();
  bool exit=args.Length==1&&args[0]=="--exit",login=args.Length==1&&args[0]=="--login";
  if(args.Length!=0&&!login&&!exit)throw new ArgumentException("未知启动参数。");
  if(!exit&&AutoUpdateRuntime.IsApplying())return 0;
  string root=AppDomain.CurrentDomain.BaseDirectory;
  string script=Path.GetFullPath(Path.Combine(root,"..","DesktopMode.ps1"));
  if(!File.Exists(script))throw new FileNotFoundException("找不到桌面启动脚本。",script);
  bool first;
  using(var gate=new Mutex(true,"Local\\PrismGlassLauncher"+(exit?"Exit":"Beauty"),out first)){
   if(!first)return 0;
   string shell=DesktopModeShell(script);
   var start=new ProcessStartInfo(shell,"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \""+script+"\" -Mode "+(exit?"Game -ShowErrors":"Beauty")){
    UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(script),RedirectStandardOutput=!exit
   };
   DateTime started=DateTime.UtcNow;
   if(exit){using(var child=Process.Start(start)){child.WaitForExit();return child.ExitCode;}}
   try{SetProcessDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}
   var ui=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var settings=new DesktopPreferences(root);
   var handoff=new TaskCompletionSource<bool>();
   var notices=new Dictionary<string,Mask>(StringComparer.OrdinalIgnoreCase);
   var timings=new System.Collections.Concurrent.ConcurrentQueue<string>();
   Action<string> mark=phase=>timings.Enqueue(startupClock.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)+" "+phase);
   string[] phases={"SHELL","SERVICES","RENDERER","PANELS","HANDOFF","ROLLBACK"};
   double[] fractions={.12,.33,.52,.73,.91,0};
   string[] captions={"正在准备 Windows 桌面","正在启动桌面服务","正在加载桌面组件","正在同步桌面面板","正在交接桌面","正在恢复普通桌面"};
   int lastStage=-1;
   double currentFraction=0;string currentCaption=null;
   Action<double,string> progress=(fraction,caption)=>{
    currentFraction=fraction;currentCaption=caption;
    foreach(var mask in notices.Values)mask.Window.SetProgress(fraction,caption);
   };
   Action<Exception> logError=error=>{try{File.WriteAllText(Path.Combine(root,"startup-visual-error.log"),error.ToString());}catch(IOException){}catch(UnauthorizedAccessException){}};
   var topology=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
   Action topologyChanged=()=>{
    var previous=topology;topology=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);previous.TrySetResult(true);
   };
   Action<bool> refreshDisplays=force=>{
    var displays=MonitorCatalog.Read();var active=displays.Where(m=>settings.ForMonitor(m.Id,m.Primary).enabled).ToList();
    if(active.Count==0&&displays.Count>0)active.Add(displays.Find(m=>m.Primary)??displays[0]);
    var wanted=new HashSet<string>(StringComparer.OrdinalIgnoreCase);bool changed=false;
    foreach(var display in active){
     string id=display.Id;if(!wanted.Add(id))continue;
     Mask current;if(notices.TryGetValue(id,out current)){changed|=current.Window.UpdateDisplay(display.Screen,force);continue;}
     var window=new StartupNotice(root,handoff.Task,settings,display.Screen);var mask=new Mask(window);
     notices.Add(id,mask);changed=true;
     window.Closing+=(s,e)=>{if(!handoff.Task.IsCompleted&&notices.TryGetValue(id,out current)&&current==mask)e.Cancel=true;};
     window.ContentRendered+=(s,e)=>mask.Frame.TrySetResult(true);
     window.Closed+=(s,e)=>{mask.Frame.TrySetResult(false);mask.Closed.TrySetResult(true);};
     if(currentCaption!=null)window.SetProgress(currentFraction,currentCaption);
     window.Show();
    }
    foreach(var id in notices.Keys.Where(id=>!wanted.Contains(id)).ToArray()){
     var window=notices[id].Window;notices.Remove(id);window.Close();changed=true;
    }
    if(changed)topologyChanged();
   };
   Exception topologyError=null;
   EventHandler displayChanged=(s,e)=>ui.Dispatcher.BeginInvoke(new Action(()=>{
    if(handoff.Task.IsCompleted)return;
    try{refreshDisplays(true);topologyError=null;}
    catch(Exception ex){topologyError=ex;topologyChanged();logError(ex);}
   }));
   int result=1;string failure=null;
   ui.Startup+=async(sender,eventArgs)=>{
    try{
     SystemEvents.DisplaySettingsChanged+=displayChanged;
     refreshDisplays(false);
     var frameDeadline=Task.Delay(5000);var composeDeadline=Task.Delay(7000);
     while(true){
      if(topologyError!=null)throw topologyError;
      if(composeDeadline.IsCompleted)throw new IOException("启动遮罩未能完成桌面合成。");
      var revision=topology.Task;
      if(notices.Count==0){
       if(await Task.WhenAny(revision,frameDeadline)!=revision)throw new IOException("未找到可显示启动遮罩的屏幕。");
       continue;
      }
      var frames=Task.WhenAll(notices.Values.Select(mask=>mask.Frame.Task));
      var completed=await Task.WhenAny(frames,revision,frameDeadline);
      if(revision.IsCompleted)continue;
      if(completed!=frames||!(await frames).All(ok=>ok))throw new IOException("启动遮罩未能在所有屏幕完成首帧。");
      var composed=Task.Run((Func<int>)DwmFlush);
      if(await Task.WhenAny(composed,Task.Delay(2000),composeDeadline)!=composed||await composed!=0)throw new IOException("启动遮罩未能完成桌面合成。");
      refreshDisplays(false);
      if(revision.IsCompleted)continue;
      if(topologyError!=null)throw topologyError;
      if(notices.Values.Any(mask=>!mask.Window.IsVisible))throw new IOException("启动遮罩未能完成桌面合成。");
      break;
     }
     mark("mask-composed");
     var marker=new TaskCompletionSource<bool>();bool ready=false;
     using(var child=new Process{StartInfo=start}){
      child.OutputDataReceived+=(s,e)=>{
       if(e.Data=="PRISMGLASS_DESKTOP_READY"){mark("ready-signal");marker.TrySetResult(true);return;}
       if(e.Data==null){marker.TrySetResult(false);return;}
       const string prefix="PRISMGLASS_STARTUP_STAGE ";
       if(!e.Data.StartsWith(prefix,StringComparison.Ordinal))return;
       int stage=Array.IndexOf(phases,e.Data.Substring(prefix.Length));
       if(stage<0)return;
       mark(phases[stage]);
       ui.Dispatcher.BeginInvoke(new Action(()=>{
        if(handoff.Task.IsCompleted||stage<=lastStage)return;
        double fraction=stage==phases.Length-1?(lastStage<0?0:fractions[lastStage]):fractions[stage];
        lastStage=stage;progress(fraction,captions[stage]);
       }));
      };
      mark("script-start");
      child.Start();child.BeginOutputReadLine();
      // A resident descendant may inherit stdout. Process completion is the rollback/cleanup boundary.
      result=await Task.Run(()=>{child.WaitForExit(Int32.MaxValue);return child.ExitCode;});
      if(result==0&&await Task.WhenAny(marker.Task,Task.Delay(1000))==marker.Task)ready=await marker.Task;
     }
     if(result==0&&!ready){result=1;failure="桌面启动进程已退出，但未确认就绪。";}
     mark("script-exit");
     bool succeeded=result==0&&ready;
     if(succeeded)progress(1,"桌面已就绪");
     handoff.TrySetResult(succeeded);
     if(succeeded)await Task.WhenAny(Task.WhenAll(notices.Values.Select(mask=>mask.Closed.Task)),Task.Delay(500));
    }catch(Exception ex){result=1;failure=ex.Message;logError(ex);}
    finally{
     SystemEvents.DisplaySettingsChanged-=displayChanged;
     handoff.TrySetResult(false);foreach(var mask in notices.Values)if(mask.Window.IsVisible)mask.Window.Close();
     mark(result==0?"finished":"failed");
     try{File.WriteAllLines(Path.Combine(root,"startup-timing.log"),timings.ToArray());}catch(IOException){}catch(UnauthorizedAccessException){}
     ui.Shutdown();
    }
   };
   ui.Run();
   if(result!=0&&!login){
    string log=Path.Combine(root,"startup-error.log");
    string reason=failure??(File.Exists(log)&&File.GetLastWriteTimeUtc(log)>=started?File.ReadAllText(log):"桌面启动进程提前退出，请重新运行安装器检查所需组件。");
    MessageBox.Show(reason+"\n请处理后重试。","PrismGlass 未能启动",MessageBoxButton.OK,MessageBoxImage.Error);
   }
   return result;
  }
 }
 [DllImport("dwmapi.dll")] static extern int DwmFlush();
}

internal sealed class StartupNotice:Window {
 System.Windows.Forms.Screen display;
 readonly DesktopPreferences settings;
 readonly StartupArtwork artwork=new StartupArtwork();
 readonly Stopwatch clock=new Stopwatch();
 readonly System.Windows.Threading.DispatcherTimer levelTimer=new System.Windows.Threading.DispatcherTimer();
 RuntimePolicy policy;
 IntPtr handle,objectHook,foregroundHook;
 WinEventProc onWindowEvent;
 long lastFrame=-100;
 bool drawing,finishing,closed;
 const int ExStyle=-20,TopmostStyle=8;
 [StructLayout(LayoutKind.Sequential)] struct WindowRect {public int left,top,right,bottom;}
 delegate void WinEventProc(IntPtr hook,uint evt,IntPtr window,int id,int child,uint thread,uint time);
 [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
 [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window,int index,int value);
 [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out WindowRect rect);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,System.Text.StringBuilder name,int capacity);
 [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
 [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,uint attribute,out int value,int size);

 void CoverDisplay(){
  var b=display.Bounds;
  if(!SetWindowPos(handle,new IntPtr(-1),b.X,b.Y,b.Width,b.Height,0x10))
   throw new IOException("无法将启动遮罩铺满显示器。");
 }
 internal bool UpdateDisplay(System.Windows.Forms.Screen screen,bool force){
  bool changed=display.Bounds!=screen.Bounds;
  display=screen;
  if(changed||force)CoverDisplay();
  return changed;
 }
 void KeepAbove(){
  if(closed||!IsVisible)return;
  var b=display.Bounds;
  // Other topmost surfaces can appear while the renderer starts. Do not reorder our other monitors.
  for(IntPtr w=GetWindow(handle,3);w!=IntPtr.Zero;w=GetWindow(w,3)){
   if((GetWindowLong(w,ExStyle)&TopmostStyle)==0||!IsWindowVisible(w))continue;
   uint pid;GetWindowThreadProcessId(w,out pid);if(pid==GetCurrentProcessId())continue;
   // Windows keeps its transparent handwriting canvas in a higher shell band.
   var windowClass=new System.Text.StringBuilder(80);
   GetClassName(w,windowClass,windowClass.Capacity);
   if(windowClass.ToString().StartsWith("ShellHandwritingCanvas",StringComparison.Ordinal))continue;
   WindowRect rect;int cloaked;
   if(GetWindowRect(w,out rect)&&rect.right>b.Left&&rect.left<b.Right&&rect.bottom>b.Top&&rect.top<b.Bottom&&
      !(DwmGetWindowAttribute(w,14,out cloaked,4)==0&&cloaked!=0)){
    SetWindowPos(handle,new IntPtr(-1),0,0,0,0,0x13);break;
   }
  }
 }
 protected override void OnSourceInitialized(EventArgs e){
  base.OnSourceInitialized(e);
  handle=new WindowInteropHelper(this).Handle;
  SetWindowLong(handle,ExStyle,GetWindowLong(handle,ExStyle)|0x080000A0);
  CoverDisplay();
  policy=new RuntimePolicy(handle);
  HwndSource.FromHwnd(handle).AddHook((IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled)=>{
   var message=System.Windows.Forms.Message.Create(h,m,w,l);policy.WindowMessage(ref message);return IntPtr.Zero;
  });
  policy.Changed+=ApplyMotion;ApplyMotion();
  onWindowEvent=(hook,evt,w,id,child,thread,time)=>{if(id==0&&child==0)KeepAbove();};
  foregroundHook=SetWinEventHook(3,3,IntPtr.Zero,onWindowEvent,0,0,2);
  objectHook=SetWinEventHook(0x8002,0x8004,IntPtr.Zero,onWindowEvent,0,0,2);
 }
 public StartupNotice(string root,Task<bool> handoff,DesktopPreferences preferences,System.Windows.Forms.Screen screen){
  settings=preferences;display=screen;
  Title="PrismGlass · 正在开启桌面";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.Manual;
  ShowActivated=false;ShowInTaskbar=false;Topmost=true;AllowsTransparency=true;
  Background=new SolidColorBrush(Color.FromRgb(8,15,32));Content=artwork;
  levelTimer.Interval=TimeSpan.FromMilliseconds(80);levelTimer.Tick+=(s,e)=>KeepAbove();
  Loaded+=(s,e)=>{CoverDisplay();clock.Start();levelTimer.Start();};
  ContentRendered+=async(s,e)=>Finish(await handoff);
  Closed+=(s,e)=>{
   closed=true;levelTimer.Stop();StopDrawing();
   if(objectHook!=IntPtr.Zero)UnhookWinEvent(objectHook);
   if(foregroundHook!=IntPtr.Zero)UnhookWinEvent(foregroundHook);
   if(policy!=null){policy.Changed-=ApplyMotion;policy.Dispose();}
  };
 }
 internal void SetProgress(double fraction,string label){if(!finishing&&!closed)artwork.SetProgress(fraction,label);}
 void StopDrawing(){
  if(drawing){CompositionTarget.Rendering-=DrawFrame;drawing=false;}
 }
 void DrawFrame(object sender,EventArgs e){
  long elapsed=clock.ElapsedMilliseconds;
  if(elapsed-lastFrame<16)return;
  lastFrame=elapsed;artwork.SetFrame(elapsed/1000.0,false);
 }
 void ApplyMotion(){
  if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(ApplyMotion));return;}
  if(closed)return;
  bool motion=RuntimePolicy.Reasons(settings.Current,policy.Signals,false,false,SystemParameters.ClientAreaAnimation).Length==0;
  if(finishing){if(!motion)Close();return;}
  if(motion&&!drawing){CompositionTarget.Rendering+=DrawFrame;drawing=true;}
  if(!motion){StopDrawing();artwork.SetFrame(0,true);}
 }
 void Finish(bool success){
  if(finishing||closed||!IsVisible)return;
  finishing=true;
  if(!success||!drawing){Close();return;}
  StopDrawing();
  var fade=new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(200)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseInOut}};
  fade.Completed+=(s,e)=>{if(!closed)Close();};BeginAnimation(OpacityProperty,fade);
 }
}
