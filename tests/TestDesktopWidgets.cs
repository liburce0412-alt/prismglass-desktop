using System;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;
internal static class RuntimePaths {public static string Recorder;public static int RecorderReads;public static string Rainmeter {get{if(Recorder!=null){Interlocked.Increment(ref RecorderReads);return Recorder;}throw new Exception("The fixture must never command the user's Rainmeter instance.");}}}
internal sealed class WidgetTestBackdrop:Form {protected override bool ShowWithoutActivation{get{return true;}}}

internal static class TestDesktopWidgets {
 [StructLayout(LayoutKind.Sequential)]struct Rect {public int l,t,r,b;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string name);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")]static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")]static extern uint GetDpiForSystem();
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 static void RaiseCapture(Form backdrop,IEnumerable<IntPtr> cards){SetWindowPos(backdrop.Handle,new IntPtr(-1),0,0,0,0,19);foreach(var card in cards)SetWindowPos(card,new IntPtr(-1),0,0,0,0,19);Application.DoEvents();Thread.Sleep(60);}
 static int checks;static void Check(bool pass,string name){if(!pass)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static Rectangle Bounds(IntPtr h){Rect r;GetWindowRect(h,out r);return Rectangle.FromLTRB(r.l,r.t,r.r,r.b);}
 static void CheckPreload(){
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"widget-preload-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(root);
  try{
   File.WriteAllText(Path.Combine(root,"skin-path.txt"),root);
   foreach(var name in new[]{"Clock","Moon"}){
    var directory=Directory.CreateDirectory(Path.Combine(root,name));
    File.WriteAllText(Path.Combine(directory.FullName,"Main.ini"),"[Rainmeter]\nOnRefreshAction=[!Hide]");
   }
   RuntimePaths.Recorder=System.Reflection.Assembly.GetExecutingAssembly().Location;
   using(var widgets=new DesktopWidgets(root){Dispatch=new RainmeterDispatch()}){
    widgets.Preload(new[]{"Clock","Calendar"});
    Check(SpinWait.SpinUntil(()=>Volatile.Read(ref RuntimePaths.RecorderReads)>0,2000),"the selected installed card is queued before the first visibility sync");
    var selected=new[]{"Clock"};
    Check(SpinWait.SpinUntil(()=>{try{widgets.Sync(selected,true);return false;}catch(InvalidOperationException){return true;}},2000),"normal sync observes the same queued activation result and starts its existing retry delay");
    widgets.Sync(selected,true);
    Check(Volatile.Read(ref RuntimePaths.RecorderReads)==1,"preload skips missing and unselected cards and does not duplicate normal activation");
    Check(!widgets.StartupReady(selected,true,null,null,null),"failed preload cannot satisfy actual widget readiness");
   }
  }finally{RuntimePaths.Recorder=null;Directory.Delete(root,true);}
 }
 static void CheckPositions(){
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"widget-positions-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var screens=new Dictionary<string,Rectangle>{{"left",new Rectangle(-1920,0,1920,1040)},{"main",new Rectangle(0,0,2560,1400)}};var saved=new Rectangle(-1600,200,244,196);var positions=new WidgetPositions(root);bool temporary;
   positions.Remember("Moon",saved,screens);string bytes=File.ReadAllText(Path.Combine(root,"widget-positions.json"));Check(positions.Restore("Moon",new Rectangle(20,20,244,196),screens,out temporary)==saved&&!temporary,"saved widget display identity wins over a transient native position");
   var disconnected=new Dictionary<string,Rectangle>{{"main",screens["main"]}};var fallback=positions.Restore("Moon",new Rectangle(20,20,244,196),disconnected,out temporary);Check(temporary&&screens["main"].Contains(fallback),"disconnected widget fits temporarily on a connected display");Check(File.ReadAllText(Path.Combine(root,"widget-positions.json"))==bytes,"temporary fallback never overwrites the saved home layout");
   Check(new WidgetPositions(root).Restore("Moon",fallback,screens,out temporary)==saved,"reconnection restores the original screen and position across restart");
   var shifted=new Dictionary<string,Rectangle>{{"left",new Rectangle(0,0,1920,1040)},{"main",new Rectangle(1920,0,2560,1400)}};var moved=positions.Restore("Moon",saved,shifted,out temporary);Check(moved.Location==new Point(320,200),"primary-screen change preserves display-relative anchor with equal geometry");
   var smaller=new Dictionary<string,Rectangle>{{"left",new Rectangle(-960,0,960,520)}};var scaled=positions.Restore("Moon",saved,smaller,out temporary);Check(scaled.Location==new Point(-800,100)&&scaled.Size==saved.Size,"resolution change scales anchor while retaining native widget size");
   positions.Remember("Moon",new Rectangle(800,350,244,196),screens);Check(new WidgetPositions(root).Restore("Moon",saved,screens,out temporary).Location==new Point(800,350),"explicit cross-screen drag updates the home assignment");
   positions.Remember("Photo",new Rectangle(2450,1300,244,196),screens);var photo=positions.Restore("Photo",new Rectangle(10,10,400,300),screens,out temporary);Check(temporary&&screens["main"].Contains(photo)&&photo.Size==new Size(400,300),"changed photo size remains fully visible without rewriting its anchor");
   File.WriteAllText(Path.Combine(root,"widget-positions.json"),"{invalid");bool refused=false;try{new WidgetPositions(root).Remember("Moon",saved,screens);}catch{refused=true;}Check(refused&&File.ReadAllText(Path.Combine(root,"widget-positions.json"))=="{invalid","corrupt positions are rejected without overwriting user data");
   Check(DesktopWidgets.Tooltip("Photo6").Contains("双击")&&DesktopWidgets.Tooltip("DigitalClock").Contains("本机时间"),"shared tooltip captions include photo copies and clock meaning");
  }finally{Directory.Delete(root,true);}
 }
 static int CheckCatalogRecovery(){
  string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"widget-catalog-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"BreathingOrb"));
  string log=Path.Combine(root,"commands.txt");
  try{
   File.WriteAllText(Path.Combine(root,"skin-path.txt"),root);File.WriteAllText(Path.Combine(root,"BreathingOrb","Main.ini"),"[Rainmeter]");
   Environment.SetEnvironmentVariable("PRISMGLASS_WIDGET_COMMAND_LOG",log);RuntimePaths.Recorder=System.Reflection.Assembly.GetExecutingAssembly().Location;
   using(var widgets=new DesktopWidgets(root)){
    var selected=new[]{"BreathingOrb"};widgets.Sync(selected,true);
    Check(File.ReadAllLines(log).Length==1&&File.ReadAllLines(log)[0].StartsWith("!ActivateConfig"),"unopened installed card first receives an ordinary activation");
    var retry=(Dictionary<string,DateTime>)typeof(DesktopWidgets).GetField("retries",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(widgets);retry["BreathingOrb"]=DateTime.MinValue;
    widgets.Sync(selected,true);Check(File.ReadAllLines(log).Length==2&&File.ReadAllLines(log)[1]=="!RefreshApp","failed activation refreshes the catalog once for installed cards");
    Check(!MonitorDock.SkinCatalogReady,"catalog registration has a separate delayed readiness boundary");
    widgets.Sync(selected,true);Check(File.ReadAllLines(log).Length==2,"activation waits for asynchronous catalog registration");
    typeof(MonitorDock).GetField("catalogReady",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).SetValue(null,DateTime.MinValue);
    widgets.Sync(selected,true);Check(File.ReadAllLines(log).Length==3&&File.ReadAllLines(log)[2].StartsWith("!ActivateConfig"),"card activation retries after catalog registration");
    retry["BreathingOrb"]=DateTime.MinValue;widgets.Sync(selected,true);
    Check(File.ReadAllLines(log).Length==4&&File.ReadAllLines(log)[3].StartsWith("!ActivateConfig"),"persistent activation failure never loops global refreshes");
    bool missing=false;try{widgets.Sync(new[]{"Calendar"},true);}catch(IOException){missing=true;}
    Check(missing&&File.ReadAllLines(log).Length==4,"missing installed files report an actionable error without global refresh");
   }
   Console.WriteLine("Completed "+checks+" widget catalog checks.");return 0;
  }finally{RuntimePaths.Recorder=null;Environment.SetEnvironmentVariable("PRISMGLASS_WIDGET_COMMAND_LOG",null);Directory.Delete(root,true);}
 }
 [STAThread]static int Main(string[] args){try{
  if(Array.IndexOf(args,"--experience-only")>=0||Array.IndexOf(args,"--classic-only")>=0)SetProcessDpiAwarenessContext(new IntPtr(-4));
  if(args.Length>0&&args[0].StartsWith("!",StringComparison.Ordinal)){File.AppendAllText(Environment.GetEnvironmentVariable("PRISMGLASS_WIDGET_COMMAND_LOG"),String.Join(" ",args)+Environment.NewLine);return 0;}
  if(args.Length==1&&args[0]=="--catalog")return CheckCatalogRecovery();
  if(args.Length==1&&args[0]=="--preload"){CheckPreload();return 0;}
  if(args.Length==0)CheckPositions();
  if(Array.IndexOf(args,"--new-cards")>=0||Array.IndexOf(args,"--photos-only")>=0||Array.IndexOf(args,"--experience-only")>=0||Array.IndexOf(args,"--classic-only")>=0){
   bool photoMode=Array.IndexOf(args,"--photos-only")>=0;
   bool experienceMode=Array.IndexOf(args,"--experience-only")>=0;
   bool classicMode=Array.IndexOf(args,"--classic-only")>=0;
   string fixture=Path.GetFullPath(args[0]);var names=photoMode?new[]{"Photo","Photo2","Photo3"}:experienceMode?new[]{"Hourglass","SunPath","YearDots","Tide","Orrery","Bonsai"}:classicMode?new[]{"Clock","Calendar","System","Moon"}:new[]{"BreathingOrb","DigitalClock","Moon","Photo"};
   var cards=new Dictionary<string,IntPtr>();
   foreach(var name in names){
    IntPtr h=IntPtr.Zero;string path=Path.Combine(fixture,"Skins","PrismGlass",name,"Main.ini");
    for(int i=0;i<80;i++){h=FindWindow("RainmeterMeterWindow",path);if(h!=IntPtr.Zero)break;Thread.Sleep(40);}
    uint pid;GetWindowThreadProcessId(h,out pid);Check(h!=IntPtr.Zero&&pid==UInt32.Parse(args[1]),name+" loads in the isolated native Rainmeter profile");cards[name]=h;
   }
   using(var backdrop=new WidgetTestBackdrop{StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(190,170,1080,650),BackColor=Color.FromArgb(22,41,60),FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,TopMost=true})using(var widgets=new DesktopWidgets(fixture)){
    if(experienceMode||classicMode){backdrop.Show();Application.DoEvents();}
    foreach(var name in names){
     widgets.Sync(new[]{name},true);Thread.Sleep(120);var h=cards[name];
     var expected=name=="System"?new Size(504,168):new Size(244,196);if(photoMode){var metadata=File.ReadAllLines(Path.Combine(fixture,"LiquidDesktop","photo-cards",name+".txt"));expected=new Size(Int32.Parse(metadata[1]),Int32.Parse(metadata[2]));}
     if((experienceMode||classicMode)&&GetDpiForWindow(h)==96){double scale=GetDpiForSystem()/96.0;expected=new Size((int)Math.Round(expected.Width*scale),(int)Math.Round(expected.Height*scale));}
     Check(IsWindowVisible(h)&&Bounds(h).Size==expected,name+" shows with its actual source-proportioned native card bounds ("+Bounds(h).Size+", visible="+IsWindowVisible(h)+")");
     if(Array.IndexOf(args,"--capture-ambient")>=0){
      MonitorDock.SendSkinCommand(h,"!ZPos 2 \"PrismGlass\\"+name+"\"");Thread.Sleep(120);
      using(var bitmap=new Bitmap(expected.Width,expected.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(Bounds(h).Location,Point.Empty,bitmap.Size);bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,(photoMode?"photo-native-":"ambient-")+name+".png"),System.Drawing.Imaging.ImageFormat.Png);}
      MonitorDock.SendSkinCommand(h,"!ZPos -2 \"PrismGlass\\"+name+"\"");
     }
    }
    if(classicMode){
     widgets.Sync(names,true);int index=0;foreach(var name in names){var at=name=="System"?new Point(200,392):name=="Moon"?new Point(720,180):new Point(200+index*260,180);MonitorDock.SendSkinCommand(cards[name],"[!Move "+at.X+" "+at.Y+" \"PrismGlass\\"+name+"\"][!ZPos 2 \"PrismGlass\\"+name+"\"]");index++;}Thread.Sleep(200);
     RaiseCapture(backdrop,cards.Values);var union=Bounds(cards["Clock"]);foreach(var h in cards.Values)union=Rectangle.Union(union,Bounds(h));
     if(Array.IndexOf(args,"--capture-ambient")>=0)using(var bitmap=new Bitmap(union.Width,union.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(union.Location,Point.Empty,bitmap.Size);bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"classic-native-comparison.png"),System.Drawing.Imaging.ImageFormat.Png);}
     var clock=cards["Clock"];Func<string> probe=()=>{MonitorDock.SendSkinCommand(clock,"!CommandMeasure Ambient \"ProbeClock()\" \"PrismGlass\\Clock\"");return File.ReadAllText(Path.Combine(fixture,"LiquidDesktop","clock-probe.txt"));};
     string before=probe();Thread.Sleep(160);string after=probe();File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"clock-live-probe.txt"),before+Environment.NewLine+after);Check(before!=after&&after.EndsWith("divider=1"),"real Clock moves its separate hand layer between civil-second labels ("+before+" -> "+after+")");
     MonitorDock.SendSkinCommand(clock,"[!SetVariable AmbientPaused 1 \"PrismGlass\\Clock\"][!CommandMeasure Ambient \"MotionPolicy()\" \"PrismGlass\\Clock\"]");Check(probe().EndsWith("divider=63"),"Clock host pause changes the actual script to its low-frequency policy");
     widgets.Sync(new[]{"Calendar","System","Moon"},true);Check(!IsWindowVisible(clock),"hiding the clock preserves the other currently selected component types");
    }
    if(experienceMode){
     widgets.Sync(names,true);Check(Array.TrueForAll(names,n=>IsWindowVisible(cards[n])),"all six independent experience widgets are visible together");
     for(int index=0;index<names.Length;index++){var h=cards[names[index]];MonitorDock.SendSkinCommand(h,"!Move "+(200+index%3*260)+" "+(180+index/3*212)+" \"PrismGlass\\"+names[index]+"\"");MonitorDock.SendSkinCommand(h,"!ZPos 2 \"PrismGlass\\"+names[index]+"\"");}
     Thread.Sleep(200);
     foreach(var name in names){Rectangle hoverBounds;string caption;var at=Bounds(cards[name]);Check(widgets.Hover(new Point(at.Left+122,at.Top+98),out hoverBounds,out caption)==name&&hoverBounds==at&&caption.Length>0,name+" native window is hit by the shared glass tooltip path");}
     string positionsFile=Path.Combine(fixture,"widget-positions.json"),positionBytes=File.Exists(positionsFile)?File.ReadAllText(positionsFile):"absent";
     var cursor=Cursor.Position;try{var at=Bounds(cards["Hourglass"]);Cursor.Position=new Point(at.Left+122,at.Top+98);mouse_event(2,0,0,0,UIntPtr.Zero);Thread.Sleep(40);Rectangle hoverBounds;string caption;Check(widgets.Hover(Cursor.Position,out hoverBounds,out caption)=="","pointer-down suppresses the shared widget tooltip");Check(widgets.Dragging,"native press starts a tracked widget drag");widgets.DisplaysChanged();Check(!widgets.Dragging,"a display change cancels and suppresses that drag until release");}finally{mouse_event(4,0,0,0,UIntPtr.Zero);Cursor.Position=cursor;}
     Check(!widgets.Dragging&&(File.Exists(positionsFile)?File.ReadAllText(positionsFile):"absent")==positionBytes,"display-change cancellation preserves saved widget positions after release");
     widgets.LockLayout(true);foreach(var name in names){Rectangle hoverBounds;string caption;var at=Bounds(cards[name]);widgets.Hover(new Point(at.Left+122,at.Top+98),out hoverBounds,out caption);Check(!caption.Contains("拖动"),name+" locked tooltip never advertises dragging");}
     var union=Bounds(cards[names[0]]);foreach(var name in names)union=Rectangle.Union(union,Bounds(cards[name]));
     RaiseCapture(backdrop,cards.Values);
     if(Array.IndexOf(args,"--capture-ambient")>=0)using(var bitmap=new Bitmap(union.Width,union.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(union.Location,Point.Empty,bitmap.Size);bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"experience-native-comparison.png"),System.Drawing.Imaging.ImageFormat.Png);}
     var timerBounds=Bounds(cards["Hourglass"]);double scale=timerBounds.Width/244.0;var savedCursor=Cursor.Position;
     try{
      MonitorDock.SendSkinCommand(cards["Hourglass"],"[!SetOption Input ToolTipHidden 1 \"PrismGlass\\Hourglass\"][!UpdateMeter Input \"PrismGlass\\Hourglass\"]");
      Action<int,int> click=(x,y)=>{RaiseCapture(backdrop,cards.Values);Cursor.Position=new Point(timerBounds.Left+(int)(x*scale),timerBounds.Top+(int)(y*scale));Thread.Sleep(60);Check(WindowFromPoint(Cursor.Position)==cards["Hourglass"],"pointer action targets the actual hourglass window");mouse_event(2,0,0,0,UIntPtr.Zero);Thread.Sleep(35);mouse_event(4,0,0,0,UIntPtr.Zero);Thread.Sleep(100);};
      Func<string> caption=()=>{MonitorDock.SendSkinCommand(cards["Hourglass"],"!CommandMeasure Ambient \"ProbeWork()\" \"PrismGlass\\Hourglass\"");return File.ReadAllText(Path.Combine(fixture,"LiquidDesktop","work-Hourglass.txt"));};
      MonitorDock.SendSkinCommand(cards["Hourglass"],"!CommandMeasure Ambient \"ProbeControls()\" \"PrismGlass\\Hourglass\"");var centers=new Dictionary<string,Point>();foreach(var row in File.ReadAllLines(Path.Combine(fixture,"LiquidDesktop","work-controls.txt"))){var fields=row.Split(' ');centers[fields[0]]=new Point((int)(Double.Parse(fields[1])+Double.Parse(fields[3])/2),(int)(Double.Parse(fields[2])+Double.Parse(fields[4])/2));}
      click(centers["Preset3"].X,centers["Preset3"].Y);Check(caption()=="25:00","native pointer preset selects twenty-five minutes");click(centers["Primary"].X,centers["Primary"].Y);string started=caption();for(int retry=0;retry<25&&!started.StartsWith("24:");retry++){Thread.Sleep(100);started=caption();}Check(started.StartsWith("24:"),"native pointer starts the selected countdown ("+started+")");click(centers["Primary"].X,centers["Primary"].Y);string held=caption();Thread.Sleep(1100);Check(caption()==held,"native pointer pause preserves remaining time");click(centers["Secondary"].X,centers["Secondary"].Y);Check(caption()=="25:00","native pointer reset keeps the selected preset ("+caption()+")");
      MonitorDock.SendSkinCommand(cards["Tide"],"!CommandMeasure Ambient \"Activate()\" \"PrismGlass\\Tide\"");Thread.Sleep(100);
      RaiseCapture(backdrop,cards.Values);
      if(Array.IndexOf(args,"--capture-ambient")>=0)using(var bitmap=new Bitmap(union.Width,union.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(union.Location,Point.Empty,bitmap.Size);bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"experience-native-after-actions.png"),System.Drawing.Imaging.ImageFormat.Png);}
     }finally{mouse_event(4,0,0,0,UIntPtr.Zero);Cursor.Position=savedCursor;}
     string heartbeat=Path.Combine(fixture,"LiquidDesktop","heartbeat.txt");File.WriteAllText(heartbeat,((long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds).ToString()+" Sky");
     foreach(var name in names){MonitorDock.SendSkinCommand(cards[name],"[!CommandMeasure LiquidState \"Update()\" \"PrismGlass\\"+name+"\"][!CommandMeasure Ambient \"ProbeTooltip()\" \"PrismGlass\\"+name+"\"]");Check(File.ReadAllText(Path.Combine(fixture,"LiquidDesktop","tooltip-"+name+".txt"))=="1",name+" disables the native tooltip while the shared glass host is alive");}
     File.Delete(heartbeat);foreach(var name in names){MonitorDock.SendSkinCommand(cards[name],"[!CommandMeasure LiquidState \"Update()\" \"PrismGlass\\"+name+"\"][!CommandMeasure Ambient \"ProbeTooltip()\" \"PrismGlass\\"+name+"\"]");Check(File.ReadAllText(Path.Combine(fixture,"LiquidDesktop","tooltip-"+name+".txt"))=="0",name+" restores the native tooltip when the shared glass host stops");}
     widgets.Sync(new[]{"SunPath","YearDots","Tide","Orrery","Bonsai"},true);Check(!IsWindowVisible(cards["Hourglass"])&&IsWindowVisible(cards["SunPath"]),"hiding the countdown preserves the other five native widgets");
    }
    if(photoMode){
     widgets.Sync(names,true);Check(Array.TrueForAll(names,n=>IsWindowVisible(cards[n])),"three independent photo windows remain visible together");
     var before=Bounds(cards["Photo2"]);widgets.Sync(new[]{"Photo","Photo3"},true);Check(!IsWindowVisible(cards["Photo2"])&&Bounds(cards["Photo2"])==before&&IsWindowVisible(cards["Photo"]),"hiding one photo preserves its position and the other cards");
     widgets.Sync(names,true);var union=Bounds(cards[names[0]]);foreach(var name in names){union=Rectangle.Union(union,Bounds(cards[name]));MonitorDock.SendSkinCommand(cards[name],"!ZPos 2 \"PrismGlass\\"+name+"\"");}Thread.Sleep(120);
     if(Array.IndexOf(args,"--capture-ambient")>=0)using(var bitmap=new Bitmap(union.Width,union.Height,System.Drawing.Imaging.PixelFormat.Format24bppRgb))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(union.Location,Point.Empty,bitmap.Size);bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"photo-native-comparison.png"),System.Drawing.Imaging.ImageFormat.Png);}
    }
   }
   Console.WriteLine("Completed "+checks+" new native widget checks.");return 0;
  }
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
   var selectedClock=new[]{"Clock"};
   Check(widgets.StartupReady(new string[0],true,null,null,null)&&widgets.StartupReady(selectedClock,false,null,null,null),"empty selection and disabled desktop can start without a card");
   Check(widgets.StartupReady(selectedClock,true,selectedClock,b=>true,null)&&widgets.StartupReady(selectedClock,true,null,null,b=>false),"covered or disabled-monitor cards do not block startup");
   Check(!widgets.StartupReady(selectedClock,true,null,null,null),"a selected hidden native card blocks startup while activation and styling settle");
   widgets.Dispatch=new RainmeterDispatch();
   Check(SpinWait.SpinUntil(()=>{widgets.Sync(selectedClock,true);return widgets.StartupReady(selectedClock,true,null,null,null);},2500),"the async style, motion and show commands settle without requeuing style on a hidden card");
   Check(IsWindowVisible(windows["Clock"]),"startup readiness confirms the configured card is visible");
   widgets.Dispatch.Reset();widgets.Dispatch=null;
   widgets.Sync(DesktopWidgets.Names,true);Check(Array.TrueForAll(DesktopWidgets.Names,n=>IsWindowVisible(windows[n])),"all functional and decorative cards can be shown");
   foreach(var name in new[]{"BreathingOrb"}){
    Check(Bounds(windows[name]).Size==new Size(244,196),name+" keeps the shared glass card bounds");
    if(Array.IndexOf(args,"--capture-ambient")>=0){
     MonitorDock.SendSkinCommand(windows[name],"!ZPos 2 \"PrismGlass\\"+name+"\"");Thread.Sleep(100);
     using(var bitmap=new Bitmap(244,196,System.Drawing.Imaging.PixelFormat.Format24bppRgb))using(var graphics=Graphics.FromImage(bitmap)){
      graphics.CopyFromScreen(Bounds(windows[name]).Location,Point.Empty,bitmap.Size);
      bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ambient-"+name+".png"),System.Drawing.Imaging.ImageFormat.Png);
     }
     MonitorDock.SendSkinCommand(windows[name],"!ZPos -2 \"PrismGlass\\"+name+"\"");
    }
    MonitorDock.SendSkinCommand(windows[name],"!CommandMeasure Ambient \"Cycle()\" \"PrismGlass\\"+name+"\"");
    string state=Path.Combine(root,"LiquidDesktop","ambient-"+name+".txt");
    for(int i=0;i<20&&!File.Exists(state);i++)Thread.Sleep(20);
    Check(File.Exists(state)&&File.ReadAllText(state)=="2",name+" click cycles and saves only its local decoration state");
   }
   var clock=windows["Clock"];MonitorDock.SendSkinCommand(clock,"!Move 24 120 \"PrismGlass\\Clock\"");widgets.Arrange(rails);Check(!Bounds(clock).IntersectsWith(left),"native card is moved outside the rail through the production bridge");
   MonitorDock.SendSkinCommand(clock,"!Move 24 120 \"PrismGlass\\Clock\"");widgets.Arrange(new List<Rectangle>{right});Check(Bounds(clock).X==24,"native position is allowed after the rail is hidden");
   MonitorDock.SendSkinCommand(clock,"!Move 340 220 \"PrismGlass\\Clock\"");var before=Bounds(clock);MonitorDock.SendSkinCommand(clock,"!Refresh \"PrismGlass\\Clock\"");Thread.Sleep(200);widgets.Sync(DesktopWidgets.Names,true);Check(Bounds(clock)==before&&IsWindowVisible(clock),"refresh preserves the saved position and restores the selected card");
   widgets.Sync(new[]{"Calendar"},true);Check(!IsWindowVisible(clock)&&IsWindowVisible(windows["Calendar"])&&Bounds(clock)==before,"removing a card preserves its native window and position");
   Check(!widgets.Owns(Path.Combine(skins,"Dock","Main.ini"))&&widgets.Owns(Path.Combine(skins,"Calendar","Main.ini")),"renderer ownership includes only selected widget paths");
   widgets.Sync(new[]{"Clock"},true);Check(Bounds(clock)==before,"re-adding a card restores its existing placement");
   using(var cover=new Form{StartPosition=FormStartPosition.Manual,Bounds=before,ShowInTaskbar=false}){
    cover.Show();Application.DoEvents();Check(widgets.Sync(new[]{"Clock"},true,new[]{"Clock"},b=>b.IntersectsWith(cover.Bounds))&&!IsWindowVisible(clock),"actual overlap hides the selected Rainmeter card and invalidates glass geometry");
    Check(!widgets.Sync(new[]{"Clock"},true,new[]{"Clock"},b=>b.IntersectsWith(cover.Bounds)),"unchanged hidden card does not rebuild glass geometry");
    cover.Location=new Point(before.Right+20,before.Top);Check(widgets.Sync(new[]{"Clock"},true,new[]{"Clock"},b=>b.IntersectsWith(cover.Bounds))&&IsWindowVisible(clock),"moving the real covering window restores card and geometry");
    cover.Bounds=before;widgets.Sync(new[]{"Clock"},true,new string[0],b=>b.IntersectsWith(cover.Bounds));Check(IsWindowVisible(clock),"disabling occlusion keeps the covered native card visible");
    widgets.Sync(new[]{"Clock"},true,new string[0],b=>false,b=>false);Check(!IsWindowVisible(clock),"a disabled card monitor wins even when occlusion is off");
   }
   widgets.Sync(new[]{"Clock"},false);Check(!IsWindowVisible(clock),"disabling the desktop hides independent cards");widgets.Sync(new[]{"Clock"},true);
  }
  Check(Array.TrueForAll(DesktopWidgets.Names,n=>!IsWindowVisible(windows[n])),"disposing the desktop leaves no visible widget behind");
  Console.WriteLine("Completed "+checks+" desktop widget checks.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
