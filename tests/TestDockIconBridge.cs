using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Globalization;

internal static class RuntimePaths {public static string Rainmeter {get{throw new InvalidOperationException("This test must never start a command process.");}}}
internal sealed class MonitorPreferences {public bool enabled=true,dock=true;public int dockOffset=18;}
internal static class TestDockIconBridge {
 [StructLayout(LayoutKind.Sequential)]struct RECT {public int left,top,right,bottom;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string title);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT bounds);
 static int checks;
 static void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static long[] Read(string file){return File.ReadAllText(file).Split('|').Select(Int64.Parse).ToArray();}
 static long[] Change(MonitorDock dock,string root,int alpha){
  var file=Path.Combine(root,"ack.txt");if(File.Exists(file))File.Delete(file);
  Check(dock.SetIconOpacity(alpha),"native command acknowledges opacity "+alpha);
  Check(File.Exists(file),"production function finished its meter readback before the native acknowledgement");
  var state=Read(file);Check(state[1]==alpha&&state[2]==alpha&&state[3]>=state[0]+2&&state[3]<=state[0]+3,"actual Image meter and lease agree with requested opacity "+alpha);
  return state;
 }
 [STAThread]static int Main(string[] args){
  try{
   if(args.Length!=2)throw new ArgumentException("Pass fixture directory and its Rainmeter process id.");
   string root=Path.GetFullPath(args[0]),skins=Path.Combine(root,"Skins","PrismGlass"),main=Path.Combine(skins,"Dock","Main.ini");int expectedPid=Int32.Parse(args[1]);
   IntPtr window=IntPtr.Zero;var wait=Stopwatch.StartNew();
   while(wait.ElapsedMilliseconds<10000){window=FindWindow("RainmeterMeterWindow",main);if(window!=IntPtr.Zero&&File.Exists(Path.Combine(root,"live.txt")))break;Thread.Sleep(50);}
   uint pid;GetWindowThreadProcessId(window,out pid);Check(window!=IntPtr.Zero&&pid==(uint)expectedPid,"exact test skin belongs to the independently launched Rainmeter process");
   Check(File.Exists(Path.Combine(root,"live.txt")),"production script and fixture meter observer initialized");
   RECT rect;Check(GetWindowRect(window,out rect)&&!Screen.AllScreens.Any(s=>s.Bounds.IntersectsWith(Rectangle.FromLTRB(rect.left,rect.top,rect.right,rect.bottom))),"real production Dock meters remain entirely outside every display");
   using(var dock=new MonitorDock(root,"primary")){
    typeof(MonitorDock).GetField("skinRoot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dock,skins);
    typeof(MonitorDock).GetField("enabled",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dock,true);
    var initial=Read(Path.Combine(root,"live.txt"));Check(initial[1]==255&&initial[6]>0&&initial[7]>0,"real Image meter loaded its fixture PNG at nonzero dimensions");
    string tooltip=File.ReadAllText(Path.Combine(root,"tooltip.txt"));Check(tooltip=="","native fallback tooltip is empty instead of a UTF-8 name passed through Lua's ANSI bridge; actual="+tooltip);
    foreach(var hint in new[]{new[]{"0|0","松手调整顺序 · Esc 取消"},new[]{"1|0","松手取消固定 · 保留原文件"},new[]{"2|1","松手取消 · Esc 返回"},new[]{"0|1","松手固定到 Dock · Esc 取消"}}){
     long expiry=(long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds+5;
     File.WriteAllText(Path.Combine(root,"dock-drag-feedback-primary.txt"),expiry+"|1|"+hint[0]);wait.Restart();string actual="";
     while(wait.ElapsedMilliseconds<2500){try{actual=File.ReadAllText(Path.Combine(root,"hint.txt"));}catch(IOException){}if(actual==hint[1])break;Thread.Sleep(30);}
     Check(actual==hint[1],"native Rainmeter preserves Chinese drag hint: "+hint[1]+"; actual="+actual);
    }
    File.WriteAllText(Path.Combine(root,"dock-drag-feedback-primary.txt"),"");
    var hidden=Change(dock,root,0);Check(hidden[4]>0&&hidden[5]>0,"real UpdateMeter and Redraw completed before hide acknowledgement");
    Thread.Sleep(1200);var renewed=Change(dock,root,0);Check(renewed[3]>hidden[3],"same-opacity command renews the three-second lease");
    var partial=Change(dock,root,64);Check(partial[4]>0&&partial[5]>0,"partial alpha updates and redraws the real meter synchronously");
    var shown=Change(dock,root,255);Check(shown[4]>0&&shown[5]>0,"restore updates and redraws before acknowledging");
    var latencies=new double[20];for(int i=0;i<latencies.Length;i++){var elapsed=Stopwatch.StartNew();bool sent=dock.SetIconOpacity(i*13);elapsed.Stop();if(!sent)throw new Exception("Fade command was not acknowledged");latencies[i]=elapsed.Elapsed.TotalMilliseconds;Thread.Sleep(16);}
    Check(Read(Path.Combine(root,"ack.txt"))[1]==247,"twenty native fade updates preserve the last real meter opacity");
    Console.WriteLine("20 real SetIconOpacity calls: average="+latencies.Average().ToString("F2")+" ms; maximum="+latencies.Max().ToString("F2")+" ms; includes fixture readback file write.");
    string before=File.ReadAllText(Path.Combine(root,"ack.txt"));Check(!dock.SetIconOpacity(-1)&&!dock.SetIconOpacity(256)&&File.ReadAllText(Path.Combine(root,"ack.txt"))==before,"invalid native alpha cannot reach the fixture script");
    hidden=Change(dock,root,0);wait.Restart();long[] live=null;
    while(wait.ElapsedMilliseconds<5500){Thread.Sleep(80);try{live=Read(Path.Combine(root,"live.txt"));}catch(IOException){continue;}catch(FormatException){continue;}if(live.Length==8&&live[1]==255&&live[3]==0)break;}
    Check(live!=null&&live.Length==8&&live[0]>=hidden[3]&&live[1]==255&&live[2]==255&&live[3]==0,"expired bridge lease restores the real meter and clears ownership without a host command");
    Check(live[4]>hidden[4]&&live[5]>hidden[5],"lease recovery executes a fresh real meter update and redraw");
    var send=typeof(MonitorDock).GetMethod("SendSkinCommand",BindingFlags.Static|BindingFlags.NonPublic);
    Check((bool)send.Invoke(null,new object[]{window,"!CommandMeasure Dock \"BeginSweep()\" \"PrismGlass\\Dock\""}),"isolated native skin starts the continuous pointer sweep");
    string sweep=Path.Combine(root,"sweep.csv");wait.Restart();while(!File.Exists(sweep)&&wait.ElapsedMilliseconds<8000)Thread.Sleep(50);
    Check(File.Exists(sweep),"real Rainmeter updates complete the sweep and release");
    var frames=File.ReadAllLines(sweep).Select(line=>line.Split(',').Select(s=>Double.Parse(s,CultureInfo.InvariantCulture)).ToArray()).ToArray();
    var gaps=frames.Skip(1).Select((f,i)=>(f[0]-frames[i][0])*1000).OrderBy(v=>v).ToArray();
    Check(frames.Max(f=>f[2])>=78&&frames.Last()[2]<=49,"real icon wave reaches its crest and every icon settles after leaving");
    Console.WriteLine("Continuous native meter sweep: "+frames.Length+" updates / "+frames.Last()[0].ToString("F2")+" s; p95="+gaps[(int)(gaps.Length*.95)].ToString("F1")+" ms; max="+gaps.Last().ToString("F1")+" ms. This measures meter updates, not display presentation.");
    File.Copy(sweep,Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"dock-wave-native.csv"),true);
   }
   Console.WriteLine("Completed "+checks+" native Dock icon bridge checks; no input or focus operations were issued.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
