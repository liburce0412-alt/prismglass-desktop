using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class TestWorkspaceWindow {
 const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static Assembly assembly;static Form runner,window;static WebView2 view;static object library;static string root,log;static int checks;
 [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")]static extern int GetWindowRgnBox(IntPtr window,out RECT bounds);
 [StructLayout(LayoutKind.Sequential)]struct RECT {public int left,top,right,bottom;}
 sealed class Runner:Form {protected override bool ShowWithoutActivation{get{return true;}}}
 static object Create(string type,params object[] args){return Activator.CreateInstance(assembly.GetType(type,true),Instance,null,args,null);}
 static object Field(object target,string name){return target.GetType().GetField(name,Instance).GetValue(target);}
 static void SetField(object target,string name,object value){target.GetType().GetField(name,Instance).SetValue(target,value);}
 static void Check(bool value,string name){File.AppendAllText(log,(value?"PASS ":"FAIL ")+name+Environment.NewLine);if(!value)throw new Exception(name);checks++;}
 static async Task Until(Func<bool> condition,string name,int timeout=6000){var end=DateTime.UtcNow.AddMilliseconds(timeout);while(!condition()&&DateTime.UtcNow<end)await Task.Delay(40);Check(condition(),name);}
 static void Open(string page,Screen screen=null){window.GetType().GetMethod("OpenPage").Invoke(window,new object[]{page,screen??Screen.PrimaryScreen,Rectangle.Empty});}
 static CoreWebView2Controller Controller(){
  for(var type=view.GetType();type!=null;type=type.BaseType)foreach(var field in type.GetFields(Instance|BindingFlags.DeclaredOnly))if(typeof(CoreWebView2Controller).IsAssignableFrom(field.FieldType))return (CoreWebView2Controller)field.GetValue(view);
  throw new InvalidOperationException("WebView2 controller field is unavailable in this SDK.");
 }
 static void GeometryChecks(){
  Directory.CreateDirectory(root);var foreground=GetForegroundWindow();
  using(var left=(Form)Create("SideDock",root,false))using(var right=(Form)Create("FileShelf",root,false)){
   foreach(var area in new[]{new Rectangle(0,0,2560,1408),new Rectangle(-1920,40,1920,1040),new Rectangle(2560,-164,2560,1560),new Rectangle(0,0,1280,720),new Rectangle(0,0,800,600),new Rectangle(-640,200,640,480)}){
    SetField(left,"screen",area);SetField(right,"screen",area);left.GetType().GetMethod("Place",Instance).Invoke(left,null);left.GetType().GetMethod("ApplyRegion",Instance).Invoke(left,null);right.GetType().GetMethod("Place",Instance).Invoke(right,null);
    RECT leftShape,rightShape;GetWindowRgnBox(left.Handle,out leftShape);GetWindowRgnBox(right.Handle,out rightShape);
    var leftCard=(Rectangle)left.GetType().GetProperty("OcclusionBounds").GetValue(left,null);var rightCard=(Rectangle)right.GetType().GetProperty("OcclusionBounds").GetValue(right,null);
    File.AppendAllText(log,"Rail geometry "+area+": left="+left.Bounds+"/"+(left.Top+leftShape.top)+", right="+right.Bounds+"/"+(right.Top+rightShape.top)+Environment.NewLine);
    Check(left.Top+leftShape.top==right.Top+rightShape.top&&leftCard.Top==rightCard.Top,"left and right native glass top edges share a pixel baseline at "+area);
    var hidden=(IList)Field(Field(right,"state"),"hiddenCards");bool fits=true;var names=new[]{"quota","notes","tasks","countdown","links","calculator"};
    for(int count=0;count<names.Length;count++){
     hidden.Clear();for(int i=0;i<count;i++)hidden.Add(names[i]);
     foreach(float amount in new[]{0f,1f}){right.GetType().GetMethod("Shape",Instance).Invoke(right,new object[]{amount});GetWindowRgnBox(right.Handle,out rightShape);fits&=right.Top+rightShape.top==left.Top+leftShape.top&&rightShape.left>=0&&rightShape.top>=0&&rightShape.right<=right.Width&&rightShape.bottom<=right.Height;}
    }
    Check(fits&&area.Contains(leftCard)&&area.Contains(right.Bounds),"one through six right entries and expanded panel stay aligned inside narrow and low monitor bounds at "+area);hidden.Clear();
   }
   Check(!left.Visible&&!right.Visible&&GetForegroundWindow()==foreground,"geometry verification never displays a component or moves foreground focus");
  }
  File.AppendAllText(log,"Completed "+checks+" native desktop alignment checks."+Environment.NewLine);
 }
 static async Task Run(){
  Directory.CreateDirectory(root);
  string source=Path.GetDirectoryName(assembly.Location);
  foreach(var file in Directory.GetFiles(source)){string extension=Path.GetExtension(file).ToLowerInvariant();if(extension==".html"||extension==".js"||extension==".css"||Path.GetFileName(file)=="wallpaper.png"||Path.GetFileName(file)=="app-center-pinyin.txt")File.Copy(file,Path.Combine(root,Path.GetFileName(file)));}
  string libraryFixture="{\"version\":1,\"revision\":1,\"entries\":[],\"categories\":[{\"id\":\"uncategorized\",\"name\":\"验收应用\",\"order\":0}],\"folders\":[],\"excluded\":[]}";File.WriteAllText(Path.Combine(root,"app-library.json"),libraryFixture);
  File.WriteAllText(Path.Combine(root,"skin-path.txt"),Path.Combine(root,"skins","PrismGlass"));
  library=Create("AppLibrary",root);
  // Keep the real snapshot code but detach discovery watchers from the user's application library.
  foreach(IDisposable watcher in (IEnumerable)Field(library,"watchers"))watcher.Dispose();((IList)Field(library,"watchers")).Clear();SetField(library,"dirty",false);SetField(library,"scanned",DateTime.UtcNow);
  window=(Form)Create("WorkspaceWindow",root,library,Create("DesktopPreferences",root));window.Opacity=0;window.ShowInTaskbar=false;
  view=(WebView2)Field(window,"view");
  Open("settings");
  await Until(()=>!window.IsDisposed&&(bool)Field(window,"ready"),"real WorkspaceWindow completes WebView2 initialization",30000);
  Check(window.TopMost&&(GetWindowLongPtr(window.Handle,-20).ToInt64()&8)!=0,"settings window has the actual topmost HWND style");
  Check(view.CoreWebView2.Settings.IsNonClientRegionSupportEnabled&&await view.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.querySelector('.workspace-header')).getPropertyValue('app-region')==='drag'&&getComputedStyle(document.querySelector('#close-workspace')).getPropertyValue('app-region')==='no-drag'&&getComputedStyle(document.querySelector('.workspace-header nav')).getPropertyValue('app-region')==='no-drag'")=="true","native title dragging is enabled while navigation and close remain clickable");
  File.AppendAllText(log,"Initial visibility: form="+window.Visible+", state="+window.WindowState+", view="+view.Visible+", controller="+Controller().IsVisible+", suspended="+view.CoreWebView2.IsSuspended+Environment.NewLine);
  await Until(()=>Controller().IsVisible&&!view.CoreWebView2.IsSuspended,"visible settings controller is active");
  foreach(var screen in Screen.AllScreens){
   SetField(window,"settingsBounds",Rectangle.Empty);Open("settings",screen);var area=screen.WorkingArea;
   var moved=new Point(area.Left+Math.Min(32,Math.Max(0,area.Width-window.Width)),area.Top+Math.Min(28,Math.Max(0,area.Height-window.Height)));window.Location=moved;
   Check(window.Location==moved&&(Rectangle)Field(window,"settingsBounds")==window.Bounds,"native Move saves settings position on "+screen.DeviceName);
   Open("apps",screen);Check(window.Visible&&window.TopMost&&(GetWindowLongPtr(window.Handle,-20).ToInt64()&8)!=0,"application center stays topmost on "+screen.DeviceName);
   Open("settings",screen);Check(window.Location==moved,"returning from apps preserves the moved settings position on "+screen.DeviceName);
   window.Hide();Open("settings",screen);Check(window.Location==moved,"reopening settings preserves its position on "+screen.DeviceName);
  }
  Open("apps");Open("toggle-apps");Check(!window.Visible,"clicking the open application center hides it");
  Open("toggle-apps");await Task.Delay(80);Check(window.Visible,"clicking the closed application center opens it");
  Open("apps");Check(window.Visible,"explicit application-center navigation remains idempotent");
  await Task.Delay(1200);Check(window.Visible,"application center stays open without moving the pointer into it");
  Check(GetForegroundWindow()==window.Handle,"workspace owns the native foreground before the transient activation check");
  typeof(Form).GetMethod("OnDeactivate",Instance).Invoke(window,new object[]{EventArgs.Empty});
  Check(window.Visible,"a transient deactivation does not hide the application center synchronously");
  await Task.Delay(300);Check(window.Visible,"settled native foreground keeps the application center open after a transient deactivation");
  runner.Activate();await Until(()=>!window.Visible,"application center closes after native focus moves outside");
  Open("settings");runner.Activate();await Task.Delay(80);Check(window.Visible,"settings stays open after focus moves outside");
  window.Hide();await Until(()=>!Controller().IsVisible,"hiding the form hides the actual WebView2 controller");
  await Until(()=>view.CoreWebView2.IsSuspended,"hidden workspace renderer enters suspension");
  Open("settings");await Until(()=>Controller().IsVisible&&!view.CoreWebView2.IsSuspended,"reopening resumes the same controller");
  Check(await view.CoreWebView2.ExecuteScriptAsync("!!window.PrismWorkspace&&document.querySelector('#settings-page').hidden===false")=="true","resumed renderer receives the requested settings page");
  window.Hide();await Task.Delay(700);Open("settings");await Task.Delay(500);Check(!view.CoreWebView2.IsSuspended&&Controller().IsVisible,"a fast reopen cancels delayed suspension");
  window.WindowState=FormWindowState.Minimized;await Until(()=>!Controller().IsVisible,"minimizing settings hides its controller");
  await Until(()=>view.CoreWebView2.IsSuspended,"minimized settings suspends its renderer");Open("settings");
  await Until(()=>window.WindowState==FormWindowState.Normal&&Controller().IsVisible&&!view.CoreWebView2.IsSuspended,"opening settings restores the minimized window and renderer");
  foreach(var screen in Screen.AllScreens){
   Open("settings",screen);window.Location=new Point(screen.WorkingArea.Left+40,screen.WorkingArea.Top+44);var saved=(Rectangle)Field(window,"settingsBounds");
   ShowWindow(window.Handle,6);await Until(()=>window.WindowState==FormWindowState.Minimized&&!Controller().IsVisible,"native minimize hides the workspace controller on "+screen.DeviceName);
   await Until(()=>view.CoreWebView2.IsSuspended,"native minimized workspace suspends its renderer on "+screen.DeviceName);
   var minimized=(Rectangle)Field(window,"settingsBounds");File.AppendAllText(log,"Native minimize geometry on "+screen.DeviceName+": saved="+saved+", remembered="+minimized+", current="+window.Bounds+Environment.NewLine);
   ShowWindow(window.Handle,9);SetForegroundWindow(window.Handle);
   await Until(()=>window.WindowState==FormWindowState.Normal&&Controller().IsVisible&&!view.CoreWebView2.IsSuspended,"native restore resumes the controller without OpenPage on "+screen.DeviceName);
   await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('#show-apps').click()");await Until(()=>(string)Field(window,"page")=="apps","actual top-tab message reaches the apps page after native restore on "+screen.DeviceName);
   await Task.Delay(350);var controllerBounds=Controller().Bounds;File.AppendAllText(log,"Restored apps geometry on "+screen.DeviceName+": form="+window.Bounds+", view="+view.Bounds+", controller="+controllerBounds+", visible="+window.Visible+", state="+window.WindowState+Environment.NewLine);
   Check(window.Visible&&window.WindowState==FormWindowState.Normal&&Controller().IsVisible&&!view.CoreWebView2.IsSuspended&&window.ClientSize.Width>=560&&window.ClientSize.Height>=420&&controllerBounds.Width>=560&&controllerBounds.Height>=420&&window.Region.IsVisible(new Point(window.Width/2,window.Height/2)),"restored top-tab navigation keeps a full-size drawable apps window on "+screen.DeviceName);
   Check(minimized==saved,"native minimize preserves the remembered settings bounds on "+screen.DeviceName);
  }
  Check(File.ReadAllText(Path.Combine(root,"app-library.json"))==libraryFixture,"native checks never scan or replace the isolated fixture application library");
  File.AppendAllText(log,"Completed "+checks+" native workspace checks."+Environment.NewLine);
 }
 [STAThread]static void Main(string[] args){
  bool geometry=args.Length==4&&args[3]=="--geometry";if(args.Length!=3&&!geometry)throw new ArgumentException("Pass the candidate assembly, a new fixture directory, and the result log.");
  root=Path.GetFullPath(args[1]);log=Path.GetFullPath(args[2]);if(Directory.Exists(root))throw new ArgumentException("Use a new disposable fixture directory.");File.WriteAllText(log,"");
  SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));var foreground=GetForegroundWindow();
  if(geometry){try{GeometryChecks();}catch(Exception error){File.AppendAllText(log,error.GetType().Name+": "+error.Message+Environment.NewLine+error.StackTrace);Environment.ExitCode=1;}return;}
  runner=new Runner{Opacity=0,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(-20000,-20000,1,1)};
  runner.Shown+=async(s,e)=>{try{await Run();}catch(Exception error){File.AppendAllText(log,error.GetType().Name+": "+error.Message+Environment.NewLine+error.StackTrace);Environment.ExitCode=1;}finally{if(window!=null)window.Dispose();if(library!=null)((IDisposable)library).Dispose();runner.Close();SetForegroundWindow(foreground);}};
  Application.Run(runner);
 }
}
