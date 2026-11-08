using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Opt-in live desktop check. Changes only this invisible test window's Z order.
// Captured pixels remain in memory; output contains counts and dimensions only.
internal static class TestLiveDesktopSource {
 sealed class Owner : Form {
  protected override bool ShowWithoutActivation {get{return true;}}
  public Owner(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Bounds=new Rectangle(0,0,1,1);Opacity=0;}
 }
 delegate bool EnumWindow(IntPtr window,IntPtr parameter);
 [StructLayout(LayoutKind.Sequential)]struct RECT {public int left,top,right,bottom;public Rectangle Bounds {get{return Rectangle.FromLTRB(left,top,right,bottom);}}}
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,IntPtr parameter);
 [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr root,EnumWindow callback,IntPtr parameter);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder name,int length);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT bounds);
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr value);
 static readonly StringBuilder report=new StringBuilder();
 static void Log(string text){report.AppendLine(text);Console.WriteLine(text);}
 static void Check(bool value,string name){if(!value)throw new Exception(name);Log("PASS "+name);}
 static string Class(IntPtr window){var name=new StringBuilder(128);GetClassName(window,name,name.Capacity);return name.ToString();}
 static string ProcessName(IntPtr window){uint pid;GetWindowThreadProcessId(window,out pid);try{using(var process=Process.GetProcessById((int)pid))return process.ProcessName;}catch(ArgumentException){return "";}}
 static bool HasPlayer(IntPtr root,out int self){bool found=false;int own=0,children=0;EnumChildWindows(root,(child,unused)=>{children++;var name=ProcessName(child);if(name.Equals("LiquidDesktop",StringComparison.OrdinalIgnoreCase)||name.Equals("Rainmeter",StringComparison.OrdinalIgnoreCase)||Class(child)=="RainmeterMeterWindow")own++;if(name.StartsWith("Lively.Player.",StringComparison.OrdinalIgnoreCase)&&IsWindowVisible(child))found=true;return true;},IntPtr.Zero);self=own;Log("ROOT 0x"+root.ToInt64().ToString("X")+" descendants="+children+" PrismGlassOrRainmeter="+own+" livePlayer="+found);return found;}
 static void Capture(LiveGlassBackdrop.Source source){
  using(var capture=new WindowCapture(source.handle,source.pid,true)){
   var timer=Stopwatch.StartNew();int frames=0,changed=0;byte[] previous=null;
   while(timer.ElapsedMilliseconds<3000&&frames<2){Thread.Sleep(frames==0?80:700);using(var pixels=capture.Next().GetAwaiter().GetResult()){
    if(pixels==null)continue;Check(pixels.PixelWidth>0&&pixels.PixelHeight>0,"selected desktop source returns a complete-sized WGC frame");
    int stride=pixels.PixelWidth*4;IntPtr data=Marshal.AllocHGlobal(stride*pixels.PixelHeight);
    try{WindowCapture.Copy(pixels,data,stride);int nonblack=0;byte[] sample=new byte[768];for(int i=0;i<256;i++){int x=(i%16*2+1)*pixels.PixelWidth/32,y=(i/16*2+1)*pixels.PixelHeight/32;int at=y*stride+x*4;Marshal.Copy(IntPtr.Add(data,at),sample,i*3,3);if(sample[i*3]>4||sample[i*3+1]>4||sample[i*3+2]>4)nonblack++;}if(previous!=null)for(int i=0;i<sample.Length;i++)if(sample[i]!=previous[i])changed++;Log("FRAME "+pixels.PixelWidth+"x"+pixels.PixelHeight+" nonblackSamplePoints="+nonblack+"/256 changedChannels="+(previous==null?"first":changed.ToString()));Check(nonblack>0,"desktop frame contains rendered pixels");previous=sample;}
    finally{Marshal.FreeHGlobal(data);}frames++;
   }}
   Check(frames==2,"existing WindowCapture receives two frames from the source selected by Sources");Check(changed>0,"wallpaper samples change across the two frames");capture.Dispose();Check(!capture.Valid,"disposed capture stops accepting frames");
  }
 }
 [STAThread]static int Main(string[] args){int result=0;try{
  SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();var area=Screen.PrimaryScreen.Bounds;var roots=new List<IntPtr>();
  EnumWindows((window,unused)=>{string cls=Class(window);RECT bounds;if((cls=="Progman"||cls=="WorkerW")&&IsWindowVisible(window)&&GetWindowRect(window,out bounds)&&bounds.Bounds.IntersectsWith(area))roots.Add(window);return true;},IntPtr.Zero);
  Check(roots.Count>0,"current desktop shell root exists");LiveGlassBackdrop.Configure("",true,false);Check(LiveGlassBackdrop.Enabled,"isolated test enables capture without writing installed preferences");
  int live=0;using(var owner=new Owner()){owner.Show();Application.DoEvents();foreach(var root in roots){
   int self;bool player=HasPlayer(root,out self);Check(self==0,"current capture root has no PrismGlass or Rainmeter descendants");
   // Keep the test owner immediately above the shell, below the user's windows.
   // This isolates desktop-source discovery without moving or minimizing apps.
   Check(SetWindowPos(owner.Handle,GetWindow(root,3),0,0,1,1,0x10|0x40|0x200),"test owner is positioned above shell without activation");Application.DoEvents();
   var sources=LiveGlassBackdrop.Sources(owner.Handle,area);var selected=sources.Find(source=>source.handle==root);
   if(player){Check(selected!=null,"Sources includes the permitted Lively desktop root");live++;Capture(selected);}
   else Check(selected==null,"Sources excludes a static shell without a running Lively player");
  }}
  LiveGlassBackdrop.Configure("",false,false);Check(!LiveGlassBackdrop.Enabled,"isolated capture is disabled after owner closes");
  if(live==0)Log("NOT RUN positive Sources-to-WGC path: no visible Lively.Player window is currently running.");
 }catch(Exception error){Log("FAIL "+error);result=1;}finally{LiveGlassBackdrop.Configure("",false,false);if(args.Length>0)File.WriteAllText(args[0],report.ToString());}return result;}
}
