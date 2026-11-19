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
 [DllImport("user32.dll")]static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
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
 static double ExternalPosition(double seconds){if(seconds<.3)return .08;if(seconds<1.3)return .08+.84*(seconds-.3);if(seconds<1.6)return .92;if(seconds<2.6)return .92-.84*(seconds-1.6);if(seconds<2.9)return .08;if(seconds<3.9)return .08+.84*(seconds-2.9);return seconds<4.2?.92:-1;}
 static void OpacityFollow(IntPtr window,string root,MonitorDock dock){
  Action<string> command=value=>{if(!MonitorDock.SendSkinCommand(window,"!CommandMeasure Dock \""+value+"\" \"PrismGlass\\Dock\""))throw new Exception("Opacity-follow command not acknowledged");};
  Func<int[]> widths=()=>{command("GeometrySnapshot()");return File.ReadAllLines(Path.Combine(root,"geometry.txt")).Select(line=>Int32.Parse(line.Split('|')[3])).ToArray();};
  var initial=widths();Check(initial.Length>=19,"opacity-follow fixture has enough actual meters for distant pointer targets");int size=initial[4];
  Change(dock,root,0);command("FocusIcon(5)");Thread.Sleep(180);Check(widths().All(w=>w==size),"fully hidden native meters ignore hover magnification");
  Change(dock,root,252);Thread.Sleep(300);var visible=widths();Console.WriteLine("Alpha 252, target 5 after 300 ms: width="+visible[4]);
  Check(visible[4]>size+20,"actual native icon grows during partial reveal instead of waiting for alpha 255");
  Change(dock,root,96);command("FocusIcon(19)");Thread.Sleep(140);Change(dock,root,180);Thread.Sleep(200);var reversed=widths();Console.WriteLine("Fade 96 -> 180, target 19: width="+reversed[18]+"; previous target="+reversed[4]);
  Check(reversed[18]>size+20&&reversed[4]<size+5,"native wave follows a distant icon through a fade reversal");
  Change(dock,root,0);command("FocusIcon(9)");Thread.Sleep(300);Check(widths()[8]==size,"hidden native target remains at its original size after another pointer change");
  Change(dock,root,128);Thread.Sleep(300);var resumed=widths();Console.WriteLine("Alpha 128, retained target 9: width="+resumed[8]);
  Check(resumed[8]>size+20,"native icons resume the last pointer during reveal without another hover event");
 }
 static void Geometry(IntPtr window,string root){
  Action<string> command=value=>{if(!MonitorDock.SendSkinCommand(window,"!CommandMeasure Dock \""+value+"\" \"PrismGlass\\Dock\""))throw new Exception("Geometry command not acknowledged");};
  for(int i=0;i<14;i++){command("FocusIcon(5)");Thread.Sleep(60);}
  command("GeometrySnapshot()");string output=AppDomain.CurrentDomain.BaseDirectory;
  File.Copy(Path.Combine(root,"geometry.txt"),Path.Combine(output,"dock-geometry.txt"),true);
  RECT rect;GetWindowRect(window,out rect);
  using(var bitmap=new Bitmap(rect.right-rect.left,rect.bottom-rect.top,System.Drawing.Imaging.PixelFormat.Format24bppRgb)){
   using(var graphics=Graphics.FromImage(bitmap)){IntPtr dc=graphics.GetHdc();try{Check(PrintWindow(window,dc,2),"native Rainmeter draws the isolated icon geometry into the capture DC");}finally{graphics.ReleaseHdc(dc);}}
   bitmap.Save(Path.Combine(output,"dock-geometry.png"),System.Drawing.Imaging.ImageFormat.Png);
   int colored=0;for(int y=0;y<bitmap.Height;y+=2)for(int x=0;x<bitmap.Width;x+=2){Color p=bitmap.GetPixel(x,y);if(Math.Max(p.R,Math.Max(p.G,p.B))-Math.Min(p.R,Math.Min(p.G,p.B))>35)colored++;}
   Check(colored>200,"native capture contains actual colored icon pixels rather than an empty or black surface");
  }
 }
 static void ExternalSweep(IntPtr window,string root,MonitorDock dock){
  var offered=new System.Collections.Generic.List<string>();var timer=Stopwatch.StartNew();
  Check(MonitorDock.SendSkinCommand(window,"!CommandMeasure Dock \"BeginExternalSweep()\" \"PrismGlass\\Dock\""),"independent input trace begins on the actual Rainmeter endpoint");
  double handshake=timer.Elapsed.TotalMilliseconds;timer.Restart();double next=0;
  while(timer.Elapsed.TotalSeconds<5){double now=timer.Elapsed.TotalSeconds;if(now<next){Thread.Sleep(1);continue;}next=now+.008;double fraction=ExternalPosition(now);dock.Pointer(fraction);offered.Add(now.ToString("F6",CultureInfo.InvariantCulture)+","+fraction.ToString("F12",CultureInfo.InvariantCulture));}
  dock.Pointer(-1);
  Check(MonitorDock.SendSkinCommand(window,"!CommandMeasure Dock \"EndExternalSweep()\" \"PrismGlass\\Dock\""),"independent input and rendered-meter traces complete");
  string output=AppDomain.CurrentDomain.BaseDirectory;File.WriteAllLines(Path.Combine(output,"dock-external-offers.csv"),offered);
  foreach(var name in new[]{"inputs","frames"})File.Copy(Path.Combine(root,"external-"+name+".csv"),Path.Combine(output,"dock-external-"+name+".csv"),true);
  var inputs=File.ReadAllLines(Path.Combine(root,"external-inputs.csv"));var frames=File.ReadAllLines(Path.Combine(root,"external-frames.csv"));
  var last=frames.Last().Split(',');
  Check(inputs.Length>50&&frames.Length>50,"external pointer requests and native meter updates advance on independent clocks");
  Check(last[1]=="-1.000000"&&last[5]=="0"&&Int32.Parse(last[4])<=49,"the final external exit clears hover and settles the actual icon meters");
  Console.WriteLine("External input trace: "+offered.Count+" offers, "+inputs.Length+" Lua receives, "+frames.Length+" native meter frames / 5 s; begin-command round trip "+handshake.ToString("F3")+" ms. CSV traces include actual targets, wave and largest icon centers; no display presentation claim.");
 }
 static void Sweep(IntPtr window,string root,MonitorDock dock){
    var send=typeof(MonitorDock).GetMethod("SendSkinCommand",BindingFlags.Static|BindingFlags.NonPublic);
    Check((bool)send.Invoke(null,new object[]{window,"!CommandMeasure Dock \"BeginSweep()\" \"PrismGlass\\Dock\""}),"isolated native skin starts the continuous pointer sweep");
    string sweep=Path.Combine(root,"sweep.csv");var wait=Stopwatch.StartNew();while(!File.Exists(sweep)&&wait.ElapsedMilliseconds<8000)Thread.Sleep(50);
    Check(File.Exists(sweep),"real Rainmeter updates complete the sweep and release");
    File.Copy(sweep,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"dock-sweep.csv"),true);
    var frames=File.ReadAllLines(sweep).Select(line=>line.Split(',').Select(s=>Double.Parse(s,CultureInfo.InvariantCulture)).ToArray()).ToArray();
    var gaps=frames.Skip(1).Select((f,i)=>(f[0]-frames[i][0])*1000).OrderBy(v=>v).ToArray();
    Check(frames.Max(f=>f[2])>=78&&frames.Last()[2]<=49,"real icon wave reaches its crest and every icon settles after leaving");
    Check(frames.All(f=>f[3]==1),"actual rectangular hit meters remain contiguous and contain their moving icon centers throughout the sweep");
    Console.WriteLine("Continuous native meter sweep: "+frames.Length+" updates / "+frames.Last()[0].ToString("F2")+" s; p50="+gaps[(int)(gaps.Length*.5)].ToString("F1")+" ms; p95="+gaps[(int)(gaps.Length*.95)].ToString("F1")+" ms; p99="+gaps[(int)(gaps.Length*.99)].ToString("F1")+" ms; long16="+gaps.Count(n=>n>16.7)+"; long33="+gaps.Count(n=>n>33.3)+"; max="+gaps.Last().ToString("F1")+" ms. This measures meter updates, not display presentation.");
    send.Invoke(null,new object[]{window,"!CommandMeasure Dock \"CadenceSnapshot()\" \"PrismGlass\\Dock\""});var cadence=File.ReadAllText(Path.Combine(root,"cadence.txt"));Thread.Sleep(300);
    send.Invoke(null,new object[]{window,"!CommandMeasure Dock \"CadenceSnapshot()\" \"PrismGlass\\Dock\""});Check(cadence==File.ReadAllText(Path.Combine(root,"cadence.txt")),"settled Dock stops ActionTimer callbacks instead of keeping an idle high-frequency loop");
    foreach(double fraction in new[]{.85,-1}){
     dock.Pointer(fraction);wait.Restart();bool received=false;
     while(!received&&wait.ElapsedMilliseconds<1500){
      send.Invoke(null,new object[]{window,"!CommandMeasure Dock \"PointerSnapshot()\" \"PrismGlass\\Dock\""});
      var state=File.ReadAllText(Path.Combine(root,"pointer.txt")).Split('|');
      received=Math.Abs(Double.Parse(state[0],CultureInfo.InvariantCulture)-fraction)<.0001&&(fraction<0?Int32.Parse(state[1])==0:Int32.Parse(state[1])>1);
      if(!received){Thread.Sleep(10);dock.Pointer(fraction);}
     }
     Check(received,fraction<0?"background pointer delivery clears real Rainmeter hover on exit":"background pointer delivery reaches the real skin and updates hover without a MouseOver event");
    }

    File.Copy(sweep,Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"dock-wave-native.csv"),true);
 }
 static void FunctionalIcons(IntPtr window,string root){
  var send=typeof(MonitorDock).GetMethod("SendSkinCommand",BindingFlags.Static|BindingFlags.NonPublic);
  Action<string> command=value=>{if(!(bool)send.Invoke(null,new object[]{window,"!CommandMeasure Dock \""+value+"\" \"PrismGlass\\Dock\""}))throw new Exception("Icon command not acknowledged");};
  Func<string[][]> read=()=>{command("IconFrameSnapshot()");return File.ReadAllLines(Path.Combine(root,"icon-frames.txt")).Select(line=>line.Split('|')).ToArray();};
  var rest=read();
  Check(rest[0][1].EndsWith("workspace.png")&&rest.Last()[1].EndsWith("trash-full.png"),"actual functional Image meters retain the backend workspace and full-bin image paths");
  Check(rest[1][1].EndsWith("icon.png")&&rest.All(row=>Int32.Parse(row[2])>0&&Int32.Parse(row[3])>0),"brand images remain native and every real meter has nonzero dimensions");
  command("Hover(1)");Thread.Sleep(180);var hover=read();
  Check(hover[0][1]==rest[0][1]&&Int32.Parse(hover[0][2])>Int32.Parse(rest[0][2]),"actual application-center meter magnifies the original artwork");
  command("Press(1)");Thread.Sleep(140);var pressed=read();
  Check(pressed[0][1]==rest[0][1]&&Int32.Parse(pressed[0][2])<Int32.Parse(hover[0][2]),"actual application-center meter compresses its original image on press");
  command("Release()");Thread.Sleep(450);Check(Int32.Parse(read()[0][2])>Int32.Parse(pressed[0][2]),"actual application-center meter returns smoothly after release");
  command("Hover(0)");Thread.Sleep(800);var leave=read();Check(leave[0][1]==rest[0][1]&&leave[0][2]==rest[0][2],"actual workspace meter returns to its original size and image on exit");
  command("Hover("+rest.Length+")");Thread.Sleep(180);var trash=read().Last();Check(trash[1]==rest.Last()[1]&&Int32.Parse(trash[2])>Int32.Parse(rest.Last()[2]),"actual full recycle-bin meter animates without replacing its original artwork");
  File.WriteAllText(Path.Combine(root,"dock-motion-primary.txt"),"true");Thread.Sleep(350);
  var paused=read().Last();Check(paused[1]==rest.Last()[1]&&paused[2]==rest.Last()[2],"native pause restores the full-bin image to its original size during hover");
  command("CadenceSnapshot()");string before=File.ReadAllText(Path.Combine(root,"cadence.txt"));Thread.Sleep(200);command("CadenceSnapshot()");
  Check(before==File.ReadAllText(Path.Combine(root,"cadence.txt")),"paused functional icon animation stops the actual ActionTimer callbacks");
  File.WriteAllText(Path.Combine(root,"dock-motion-primary.txt"),"false");command("Hover(0)");Thread.Sleep(400);
 }
 [STAThread]static int Main(string[] args){
  try{
   if(args.Length<2||args.Length>4)throw new ArgumentException("Pass fixture directory and its Rainmeter process id.");
   string root=Path.GetFullPath(args[0]),skins=Path.Combine(root,"Skins","PrismGlass"),main=Path.Combine(skins,"Dock","Main.ini");int expectedPid=Int32.Parse(args[1]);
   IntPtr window=IntPtr.Zero;var wait=Stopwatch.StartNew();
   while(wait.ElapsedMilliseconds<10000){window=FindWindow("RainmeterMeterWindow",main);if(window!=IntPtr.Zero&&File.Exists(Path.Combine(root,"live.txt")))break;Thread.Sleep(50);}
   uint pid;GetWindowThreadProcessId(window,out pid);Check(window!=IntPtr.Zero&&pid==(uint)expectedPid,"exact test skin belongs to the independently launched Rainmeter process");
   Check(File.Exists(Path.Combine(root,"live.txt")),"production script and fixture meter observer initialized");
   RECT rect;Check(GetWindowRect(window,out rect)&&!Screen.AllScreens.Any(s=>s.Bounds.IntersectsWith(Rectangle.FromLTRB(rect.left,rect.top,rect.right,rect.bottom))),"real production Dock meters remain entirely outside every display");
   using(var dock=new MonitorDock(root,"primary")){
    Check(SpinWait.SpinUntil(()=>dock.Responsive(window),2000)&&dock.LastResponse!=DateTime.MinValue,"responsive skin records its last successful bounded probe");
    Check(!dock.Responsive(IntPtr.Zero)&&dock.LastResponse==DateTime.MinValue,"removed skin invalidates its previous health result");
    typeof(MonitorDock).GetField("skinRoot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dock,skins);
    typeof(MonitorDock).GetField("enabled",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dock,true);
    var initial=Read(Path.Combine(root,"live.txt"));Check(initial[1]==255&&initial[6]>0&&initial[7]>0,"real Image meter loaded its fixture PNG at nonzero dimensions");
    if(args.Skip(2).Contains("--opacity-follow")){OpacityFollow(window,root,dock);Console.WriteLine("Completed "+checks+" native opacity-follow checks.");return 0;}
    if(args.Skip(2).Contains("--geometry-only")){Geometry(window,root);Console.WriteLine("Completed native icon geometry capture.");return 0;}
    if(args.Skip(2).Contains("--external-input")){ExternalSweep(window,root,dock);Console.WriteLine("Completed external pointer input trace.");return 0;}
    if(args.Skip(2).Contains("--functional-icons"))FunctionalIcons(window,root);
    if(args.Skip(2).Contains("--sweep-only")){Sweep(window,root,dock);Console.WriteLine("Completed focused meter sweep.");return 0;}
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
    Sweep(window,root,dock);
   }
   Console.WriteLine("Completed "+checks+" native Dock icon bridge checks; no input or focus operations were issued.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
 }
}
