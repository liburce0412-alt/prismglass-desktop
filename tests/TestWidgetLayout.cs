using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Linq;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;
internal static class RuntimePaths {public static string Rainmeter {get{throw new Exception("The fixture must never command the user's Rainmeter instance.");}}}
internal static class TestWidgetLayout {
 [StructLayout(LayoutKind.Sequential)]struct Rect {public int l,t,r,b;}
 [StructLayout(LayoutKind.Sequential)]struct Gui {public int size,flags;public IntPtr active,focus,capture,menu,move,caret;public Rect caretRect;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref Gui info);
 [DllImport("user32.dll")]static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
 [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point p);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,System.Text.StringBuilder text,int size);
 [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr h,int index);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 static int checks;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)throw new Exception(name);checks++;}
 static Rectangle Bounds(IntPtr h){Rect r;if(!GetWindowRect(h,out r))throw new Exception("Missing fixture HWND");return Rectangle.FromLTRB(r.l,r.t,r.r,r.b);}
 static void Trace(string when,IntPtr h){uint pid;uint thread=GetWindowThreadProcessId(h,out pid);var info=new Gui{size=Marshal.SizeOf(typeof(Gui))};GetGUIThreadInfo(thread,ref info);var hit=WindowFromPoint(Cursor.Position);var cls=new System.Text.StringBuilder(256);GetClassName(hit,cls,256);Console.WriteLine("INFO "+when+" bounds="+Bounds(h)+" pointer="+Cursor.Position+" hit="+hit+" hitClass="+cls+" window="+h+" ex="+GetWindowLong(h,-20)+" capture="+info.capture+" move="+info.move+" flags="+info.flags);}
 [STAThread]static int Main(string[] args){SetProcessDpiAwarenessContext(new IntPtr(-4));var cursor=Cursor.Position;try{
  string root=args[0];var names=new[]{"Clock","Calendar","Moon","System"};var windows=new Dictionary<string,IntPtr>();
  foreach(var name in names){IntPtr h=IntPtr.Zero;for(int i=0;i<80;i++){h=FindWindow("RainmeterMeterWindow",Path.Combine(root,"Skins","PrismGlass",name,"Main.ini"));if(h!=IntPtr.Zero)break;Thread.Sleep(40);}uint pid;GetWindowThreadProcessId(h,out pid);Check(h!=IntPtr.Zero&&pid==UInt32.Parse(args[1]),name+" belongs to the isolated native profile");windows[name]=h;}
  Check(DesktopWidgets.MapPosition(new Point(-1600,200),new Rectangle(-1920,0,1920,1080),new Rectangle(-1536,0,1536,864))==new Point(-1280,160),"DPI mapping includes a negative monitor origin");
  Check(DesktopWidgets.MapPosition(new Point(3000,300),new Rectangle(2560,-200,3840,2160),new Rectangle(2048,-160,1920,1080))==new Point(2268,90),"DPI mapping uses each monitor's logical origin instead of dividing global coordinates");
  Thread.Sleep(250);var screens=MonitorCatalog.Read().ToDictionary(m=>m.Id,m=>m.Screen.WorkingArea,StringComparer.OrdinalIgnoreCase);var area=Screen.PrimaryScreen.WorkingArea;var origin=new Point(area.Left+600,area.Top+280);var positions=new WidgetPositions(root);var clock=windows["Clock"];
  using(var widgets=new DesktopWidgets(root)){
   widgets.Sync(names,true);foreach(var name in names){var size=Bounds(windows[name]).Size;var at=name=="System"?new Point(area.Left+80,area.Top+40):origin;positions.Remember(name,new Rectangle(at,size),screens);}
   string file=Path.Combine(root,"widget-positions.json"),saved=File.ReadAllText(file);widgets.Arrange(new List<Rectangle>());Thread.Sleep(100);
   var bounds=windows.ToDictionary(p=>p.Key,p=>Bounds(p.Value));foreach(var p in bounds)Console.WriteLine("INFO arranged "+p.Key+" "+p.Value+" targetDpi="+GetDpiForWindow(windows[p.Key]));
   Check(bounds["Clock"].Location==origin,"PMv2 host places the DPI-unaware clock at the requested physical coordinates");
   bool stable=true;for(int i=0;i<4;i++){foreach(var name in names)if(!MonitorDock.SendSkinCommand(windows[name],"[!Update \"PrismGlass\\"+name+"\"][!Redraw \"PrismGlass\\"+name+"\"]"))throw new Exception("Fixture redraw did not complete");Thread.Sleep(30);stable&=windows.All(p=>Bounds(p.Value)==bounds[p.Key]);}Check(stable,"four actual Rainmeter update/redraw cycles preserve every physical card position");
   Check(bounds.Values.All(area.Contains),"all cards fit the active physical work area");Check(bounds.All(p=>bounds.All(q=>p.Key==q.Key||!p.Value.IntersectsWith(q.Value))),"native card rectangles do not overlap after display adaptation");Check(File.ReadAllText(file)==saved,"automatic layout preserves every saved monitor anchor");
   Check(MonitorDock.SendSkinCommand(clock,"!ZPos 2 \"PrismGlass\\Clock\""),"fixture accepts persistent topmost placement");Thread.Sleep(100);Point? hit=null;var card=Bounds(clock);for(int y=20;y<card.Height-20&&!hit.HasValue;y+=8)for(int x=20;x<card.Width-20;x+=8){var p=new Point(card.Left+x,card.Top+y);if(WindowFromPoint(p)==clock){hit=p;break;}}
   if(!hit.HasValue){Console.WriteLine("SKIP native drag: no safely hittable point in the isolated clock; visible="+IsWindowVisible(clock));}else{
   Cursor.Position=hit.Value;Trace("before-down",clock);if(WindowFromPoint(Cursor.Position)!=clock)throw new Exception("Refusing to click outside the isolated clock fixture");
   mouse_event(2,0,0,0,UIntPtr.Zero);Thread.Sleep(60);Trace("held",clock);bool tracked=widgets.Dragging;Check(tracked,"native move loop is tracked while the left button is held");
   Cursor.Position=new Point(hit.Value.X-160,hit.Value.Y+100);Thread.Sleep(100);Trace("moved",clock);Check(widgets.Dragging,"tracking survives the native window moving beneath the pointer");var moved=Bounds(clock);Check(moved.Location!=origin,"actual Rainmeter content moves during the native drag");
   mouse_event(4,0,0,0,UIntPtr.Zero);Thread.Sleep(80);Check(!widgets.Dragging,"release ends native drag tracking");widgets.Arrange(new List<Rectangle>());Thread.Sleep(100);Check(Bounds(clock).Location==moved.Location,"release persists the moved physical anchor without a jump back");MonitorDock.SendSkinCommand(clock,"[!Update \"PrismGlass\\Clock\"][!Redraw \"PrismGlass\\Clock\"]");Thread.Sleep(40);Check(Bounds(clock).Location==moved.Location,"Rainmeter redraw also preserves the dragged destination");
   bool temporary;Check(new WidgetPositions(root).Restore("Clock",new Rectangle(origin,moved.Size),screens,out temporary).Location==moved.Location,"a recreated layout restores the explicit drag destination");
   }
  }
  Console.WriteLine("Completed "+checks+" focused widget layout checks.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{mouse_event(4,0,0,0,UIntPtr.Zero);Cursor.Position=cursor;}}
}
