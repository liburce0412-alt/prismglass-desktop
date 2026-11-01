using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;
internal static class RuntimePaths {public static string Rainmeter {get{throw new Exception("The fixture must never command the user's Rainmeter instance.");}}}
internal sealed class MonitorPreferences {public bool enabled=true,dock=true;public int dockOffset=18;}
internal static class TestDesktopWidgets {
 [StructLayout(LayoutKind.Sequential)]struct Rect {public int l,t,r,b;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string name);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 static int checks;static void Check(bool pass,string name){if(!pass)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static Rectangle Bounds(IntPtr h){Rect r;GetWindowRect(h,out r);return Rectangle.FromLTRB(r.l,r.t,r.r,r.b);}
 [STAThread]static int Main(string[] args){try{
  var area=new Rectangle(0,0,1920,1080);var left=new Rectangle(24,64,88,668);var right=new Rectangle(1808,64,88,502);var rails=new List<Rectangle>{left,right};
  var card=new Rectangle(24,120,244,196);var placed=DesktopWidgets.Place(card,area,rails);
  Check(placed.X==120&&placed.Y==card.Y,"left rail reserves its actual width and an eight-pixel gap");
  Check(DesktopWidgets.Place(card,area,new List<Rectangle>{right})==card,"hiding the left rail frees its former position");
  card=new Rectangle(1600,120,244,196);placed=DesktopWidgets.Place(card,area,rails);Check(placed.Right==1800,"right rail rejects an overlapping drop");
  card=new Rectangle(24,800,244,196);Check(DesktopWidgets.Place(card,area,rails)==card,"space below a short rail remains usable");
  card=new Rectangle(450,200,244,196);Check(DesktopWidgets.Place(card,area,rails)==card,"legal saved placement never moves");
  Check(DesktopWidgets.Place(new Rectangle(-1920,200,244,196),new Rectangle(-1920,0,1920,1080),rails).X==-1920,"another monitor is not blocked by primary-screen rails");
  Check(DesktopWidgets.Place(new Rectangle(0,0,244,196),new Rectangle(0,0,200,160),rails).IsEmpty,"a card that cannot fit is rejected rather than placed over a rail");
  if(args.Length==0){Console.WriteLine("Completed "+checks+" placement checks.");return 0;}
  string root=Path.GetFullPath(args[0]),skins=Path.Combine(root,"Skins","PrismGlass");var windows=new Dictionary<string,IntPtr>();
  foreach(var name in DesktopWidgets.Names){var file=Path.Combine(skins,name,"Main.ini");IntPtr h=IntPtr.Zero;for(int i=0;i<100;i++){h=FindWindow("RainmeterMeterWindow",file);if(h!=IntPtr.Zero)break;Thread.Sleep(50);}uint pid;GetWindowThreadProcessId(h,out pid);Check(h!=IntPtr.Zero&&pid==UInt32.Parse(args[1]),name+" uses the independent real Rainmeter profile");windows[name]=h;}
  using(var widgets=new DesktopWidgets(root)){
   widgets.Sync(new string[0],true);Check(Array.TrueForAll(DesktopWidgets.Names,n=>!IsWindowVisible(windows[n])),"default preference leaves all native cards hidden");
   widgets.Sync(DesktopWidgets.Names,true);Check(Array.TrueForAll(DesktopWidgets.Names,n=>IsWindowVisible(windows[n])),"all nine retained cards can be shown");
   var clock=windows["Clock"];MonitorDock.SendSkinCommand(clock,"!Move 24 120 \"PrismGlass\\Clock\"");widgets.Arrange(rails);Check(!Bounds(clock).IntersectsWith(left),"native card is moved outside the rail through the production bridge");
   MonitorDock.SendSkinCommand(clock,"!Move 24 120 \"PrismGlass\\Clock\"");widgets.Arrange(new List<Rectangle>{right});Check(Bounds(clock).X==24,"native position is allowed after the rail is hidden");
   MonitorDock.SendSkinCommand(clock,"!Move 340 220 \"PrismGlass\\Clock\"");var before=Bounds(clock);MonitorDock.SendSkinCommand(clock,"!Refresh \"PrismGlass\\Clock\"");Thread.Sleep(200);widgets.Sync(DesktopWidgets.Names,true);Check(Bounds(clock)==before&&IsWindowVisible(clock),"refresh preserves the saved position and restores the selected card");
   widgets.Sync(new[]{"Calendar"},true);Check(!IsWindowVisible(clock)&&IsWindowVisible(windows["Calendar"])&&Bounds(clock)==before,"removing a card preserves its native window and position");
   Check(!widgets.Owns(Path.Combine(skins,"Dock","Main.ini"))&&widgets.Owns(Path.Combine(skins,"Calendar","Main.ini")),"renderer ownership includes only selected widget paths");
   widgets.Sync(new[]{"Clock"},true);Check(Bounds(clock)==before,"re-adding a card restores its existing placement");
   widgets.Sync(new[]{"Clock"},false);Check(!IsWindowVisible(clock),"disabling the desktop hides independent cards");widgets.Sync(new[]{"Clock"},true);
  }
  Check(Array.TrueForAll(DesktopWidgets.Names,n=>!IsWindowVisible(windows[n])),"disposing the desktop leaves no visible widget behind");
  Console.WriteLine("Completed "+checks+" desktop widget checks.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
