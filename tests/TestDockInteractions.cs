using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Windows.Forms;
using System.Web.Script.Serialization;

internal static class TestDockInteractions {
 static int checks;
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static string[] Ids(string root){return DockBackend.ReadPins(root).Select(p=>"p"+p.id).ToArray();}
 static void Request(string root,string id){File.WriteAllText(Path.Combine(root,"dock-drag-request-primary.txt"),((long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds)+"|"+id);}
 [STAThread]static void Main(string[] args){
  if(args.Length!=1||Directory.Exists(args[0]))throw new ArgumentException("Pass a new disposable fixture directory.");
  string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
  Check(DockBackend.ResolveClick(0,false,false,false)==DockClickAction.Launch,"an app with no live windows launches");
  Check(DockBackend.ResolveClick(1,true,false,false)==DockClickAction.Minimize,"clicking the single foreground window minimizes it");
  Check(DockBackend.ResolveClick(1,false,false,false)==DockClickAction.Activate,"clicking a background window activates it");
  Check(DockBackend.ResolveClick(1,false,true,false)==DockClickAction.Activate,"clicking a minimized window restores and activates it");
  Check(DockBackend.ResolveClick(1,true,true,false)==DockClickAction.Activate,"a minimized window is never minimized again from a stale active state");
  Check(DockBackend.ResolveClick(2,true,false,false)==DockClickAction.Minimize,"the foreground member minimizes even when its app has other windows");
  Check(DockBackend.ResolveClick(2,false,true,false)==DockClickAction.ChooseWindow,"multiple background windows retain selection");
  Check(DockBackend.ResolveClick(1,true,false,true)==DockClickAction.Launch&&DockBackend.ResolveClick(2,true,false,true)==DockClickAction.Launch,"explicit new-window requests always launch instead of toggling or selecting");
  Check(DockBackend.Matches(new DockPin{path="C:\\One\\editor.exe"},new DockWindow{path="c:\\one\\EDITOR.EXE"})&&!DockBackend.Matches(new DockPin{path="C:\\One\\editor.exe"},new DockWindow{path="C:\\Other\\editor.exe"}),"shared app lookup uses full executable identity rather than a matching filename");
  Check(DockBackend.Matches(new DockPin{path="shell:AppsFolder\\Example.Package!App"},new DockWindow{path="C:\\Package\\App.exe",appId="example.package!app"})&&!DockBackend.Matches(new DockPin{path="shell:AppsFolder\\Example.Package!App"},new DockWindow{path="C:\\Package\\App.exe",appId="Different.Package!App"}),"shared store-app lookup uses the application model identity");
  var focus=new DockForegroundHistory();var application=new DockWindow{handle=10,pid=1,path="app.exe"};var other=new DockWindow{handle=20,pid=2,path="other.exe"};var focusTime=DateTime.UtcNow;
  focus.Observe(10,application,false,focusTime);focus.Observe(30,null,true,focusTime.AddMilliseconds(20));focus.Observe(30,null,true,focusTime.AddMilliseconds(40));
  Check(focus.PreviousFor(30,focusTime.AddMilliseconds(50))==application,"Dock activation retains the immediately preceding application across repeated samples");
  Check(focus.PreviousFor(31,focusTime.AddMilliseconds(50))==null&&focus.PreviousFor(30,focusTime.AddSeconds(1))==null,"foreground fallback refuses another Dock handle or stale observations");
  focus.Observe(30,null,true,focusTime.AddSeconds(2));Check(focus.PreviousFor(30,focusTime.AddSeconds(2))==null,"repeated Dock samples cannot keep an old click foreground alive indefinitely");
  focus.Observe(20,other,false,focusTime.AddMilliseconds(60));focus.Observe(30,null,true,focusTime.AddMilliseconds(80));Check(focus.PreviousFor(30,focusTime.AddMilliseconds(90))==other,"rapid application switches replace the previous foreground identity");
  focus.Invalidate();focus.Observe(30,null,true,focusTime.AddMilliseconds(100));Check(focus.PreviousFor(30,focusTime.AddMilliseconds(110))==null,"a consumed click cannot reuse the preceding application");
  focus.Observe(10,application,false,focusTime.AddMilliseconds(120));focus.Observe(0,null,false,focusTime.AddMilliseconds(130));focus.Observe(30,null,true,focusTime.AddMilliseconds(140));Check(focus.PreviousFor(30,focusTime.AddMilliseconds(150))==null,"an intervening desktop or missing foreground cannot revive the prior app");
  focus.Observe(10,application,false,focusTime);focus.Observe(30,null,true,focusTime.AddSeconds(1));Check(focus.PreviousFor(30,focusTime.AddSeconds(1))==null,"a Dock activation cannot reuse an application sample older than 500ms");
  string skins=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Rainmeter","Skins","PrismGlass");
  Check(DockBackend.IsPrismDock("RainmeterMeterWindow",Path.Combine(skins,"Dock","Main.ini"))&&DockBackend.IsPrismDock("RainmeterMeterWindow",Path.Combine(skins,"Dock-monitor-1","Main.ini")),"only exact primary and monitor Dock skin identities allow the foreground fallback");
  Check(!DockBackend.IsPrismDock("RainmeterMeterWindow",Path.Combine(skins,"Topbar","Main.ini"))&&!DockBackend.IsPrismDock("RainmeterMeterWindow",Path.Combine(skins,"Dock-monitor-other","Main.ini"))&&!DockBackend.IsPrismDock("OtherClass",Path.Combine(skins,"Dock","Main.ini")),"other Rainmeter skins and unrelated windows never enable the foreground fallback");
  string customSkin=Path.Combine(root,"自定义 皮肤","PrismGlass");Check(DockBackend.IsPrismDock("RainmeterMeterWindow",Path.Combine(customSkin,"Dock","Main.ini"),customSkin)&&!DockBackend.IsPrismDock("RainmeterMeterWindow",Path.Combine(customSkin+"Other","Dock","Main.ini"),customSkin),"custom skin paths keep exact foreground identity boundaries");
  string a=Path.Combine(root,"alpha.txt"),b=Path.Combine(root,"beta.txt"),c=Path.Combine(root,"gamma.txt");foreach(var path in new[]{a,b,c})File.WriteAllText(path,"source content");
  string first=DockBackend.Pin(root,a,"","Alpha",""),second=DockBackend.Pin(root,b,"","Beta",""),third=DockBackend.Pin(root,c,"","Gamma","");
  Check(DockBackend.Pin(root,a,"","Renamed","")==first&&Ids(root).Length==3,"pinning the same launch target does not duplicate or reposition it");
  string withArgs=DockBackend.Pin(root,a,"--other","Alpha alternate","");Check(Ids(root).Length==4,"different launch arguments remain distinct");DockBackend.Unpin(root,withArgs);
  DockBackend.Move(root,third,first);Check(Ids(root).SequenceEqual(new[]{third,first,second}),"move uses the destination identity and preserves unrelated pins");
  DockBackend.Move(root,third,"");Check(Ids(root).SequenceEqual(new[]{first,second,third}),"move to end preserves pin identities");
  var backend=new DockBackend(root);backend.Refresh();File.WriteAllText(Path.Combine(root,"dock-layout.json"),"{\"cell\":100,\"dpi\":1}");
  var docks=new Dictionary<string,Rectangle>{{"primary",new Rectangle(0,0,700,100)},{"monitor-1",new Rectangle(900,0,700,100)}};
  DateTime now=DateTime.UtcNow;Request(root,first);Check(backend.TickDrag(docks,new Point(166,50),true,false,now),"native press begins a Dock transaction");
  backend.TickDrag(docks,new Point(450,50),true,false,now.AddMilliseconds(50));Check(Ids(root).SequenceEqual(new[]{first,second,third}),"moving the pointer previews insertion without saving");
  Check(File.ReadAllText(Path.Combine(root,"dock-drag-feedback-primary.txt")).Contains("|0|0"),"drag feedback exposes a normal insertion target");
  string feedbackFile=Path.Combine(root,"dock-drag-feedback-primary.txt"),feedback=File.ReadAllText(feedbackFile);
  using(var reader=new FileStream(feedbackFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
   Check(backend.TickDrag(docks,new Point(200,50),true,false,now.AddMilliseconds(60))&&File.ReadAllText(feedbackFile)==feedback,"a reader-held feedback file preserves the previous frame without cancelling the drag");
  }
  backend.TickDrag(docks,new Point(200,50),true,false,now.AddMilliseconds(70));Check(File.ReadAllText(feedbackFile).EndsWith("|2|0|0"),"the unchanged pointer retries a failed feedback update after the reader releases it");
  string monitorFeedback=Path.Combine(root,"dock-drag-feedback-monitor-1.txt");File.WriteAllText(monitorFeedback,"");
  using(var reader=new FileStream(monitorFeedback,FileMode.Open,FileAccess.Read,FileShare.Read)){
   Check(backend.TickDrag(docks,new Point(1050,50),true,false,now.AddMilliseconds(75))&&File.ReadAllText(monitorFeedback)=="","switching monitors tolerates a reader-held destination feedback file");
   backend.TickDrag(docks,new Point(200,50),true,false,now.AddMilliseconds(76));Check(File.ReadAllText(feedbackFile).EndsWith("|2|0|0"),"returning from a failed monitor switch restores the cleared original feedback");
   backend.TickDrag(docks,new Point(1050,50),true,false,now.AddMilliseconds(77));
  }
  backend.TickDrag(docks,new Point(1050,50),true,false,now.AddMilliseconds(80));Check(File.ReadAllText(monitorFeedback).EndsWith("|0|0"),"a failed monitor switch is retried without requiring another pointer movement");
  backend.TickDrag(docks,new Point(450,50),true,false,now.AddMilliseconds(90));
  using(var reader=new FileStream(feedbackFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
   backend.TickDrag(docks,new Point(450,50),false,false,now.AddMilliseconds(100));
   Check(!Directory.EnumerateFiles(root,"dock-hold-*.txt").Any(),"a reader-held final feedback file cannot retain the drag visibility lease");
  }
  Check(Ids(root).SequenceEqual(new[]{second,third,first}),"physical mouse release commits the reorder once");
  now=now.AddSeconds(1);Request(root,first);backend.TickDrag(docks,new Point(366,50),true,false,now);backend.TickDrag(docks,new Point(15,50),true,false,now.AddMilliseconds(50));
  using(var reader=new FileStream(feedbackFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
   backend.TickDrag(docks,new Point(15,50),true,true,now.AddMilliseconds(100));
   long expiry=Int64.Parse(File.ReadAllText(feedbackFile).Split('|')[0]);
   Check(!Directory.EnumerateFiles(root,"dock-hold-*.txt").Any()&&expiry<=(long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds+2,"Escape releases the drag when clearing is blocked and leaves only an expiring feedback frame");
  }
  Check(Ids(root).SequenceEqual(new[]{second,third,first}),"Escape cancels a preview without rewriting the saved order");
  now=now.AddSeconds(1);Request(root,second);backend.TickDrag(docks,new Point(166,50),true,false,now);backend.TickDrag(docks,new Point(700,300),true,false,now.AddMilliseconds(50));backend.TickDrag(docks,new Point(700,300),false,false,now.AddMilliseconds(100));
  Check(!Ids(root).Contains(second)&&File.ReadAllText(b)=="source content","dropping outside removes only the pinned reference");
  now=now.AddSeconds(1);Request(root,first);backend.TickDrag(docks,new Point(266,50),true,false,now);backend.TickDrag(docks,new Point(1,50),true,false,now.AddMilliseconds(50));
  string concurrent=DockBackend.Pin(root,b,"","Beta","");backend.TickDrag(docks,new Point(1,50),false,false,now.AddMilliseconds(100));
  Check(Ids(root).Contains(concurrent)&&Ids(root).First()==first,"drag commit rereads pins so a concurrent add is not overwritten");
  now=now.AddSeconds(1);DockBackend.BeginExternalDrag(root,c,"--new","External","");backend.TickDrag(docks,new Point(600,300),true,false,now);backend.TickDrag(docks,new Point(1050,50),true,false,now.AddMilliseconds(50));int count=Ids(root).Length;
  backend.TickDrag(docks,new Point(1050,50),false,false,now.AddMilliseconds(100));Check(Ids(root).Length==count,"external physical release waits for the browser's drop decision");
  DockBackend.EndExternalDrag(root,false);backend.TickDrag(docks,new Point(1050,50),false,false,now.AddMilliseconds(150));Check(Ids(root).Length==count+1,"application-center drop on another screen pins the selected entry");
  now=now.AddSeconds(1);DockBackend.BeginExternalDrag(root,c,"--cancel","Cancelled","");backend.TickDrag(docks,new Point(600,300),true,false,now);DockBackend.EndExternalDrag(root,true);backend.TickDrag(docks,new Point(100,50),false,false,now.AddMilliseconds(50));Check(Ids(root).Length==count+1,"an internal or Escape-cancelled browser drop never pins an entry");
  now=now.AddSeconds(1);string saved=File.ReadAllText(Path.Combine(root,"pins.json"));Request(root,"workspace");backend.TickDrag(docks,new Point(66,50),true,false,now);backend.TickDrag(docks,new Point(600,300),true,false,now.AddMilliseconds(50));
  Check(File.ReadAllText(Path.Combine(root,"pins.json"))==saved,"the application-center system entry cannot be moved or unpinned");
  DockBackend.Click(root,"workspace",false);Check(File.ReadAllText(Path.Combine(root,"workspace.request"))=="apps","the system entry routes to the real application center");
  var many=new List<DockEntry>{DockBackend.WorkspaceEntry("grid")};for(int i=0;i<35;i++)many.Add(new DockEntry{id="p"+i,name="App "+i,pinned=true});
  var visible=DockBackend.DisplayEntries(many,"more");Check(visible.Count==32&&visible[0].id=="workspace"&&visible[31].name=="更多应用 · 5","overflow counts include the fixed system entry without losing hidden apps");
  int slot;Check(DockBackend.BeforeAt(many,"p2",0,100,1,out slot)=="p0"&&slot==1,"insertion can never precede the system entry");
  var jobs=new List<Thread>();Exception error=null;for(int i=0;i<12;i++){int n=i;var job=new Thread(()=>{try{DockBackend.Pin(root,Path.Combine(root,"concurrent-"+n+".exe"),"","Concurrent","");}catch(Exception ex){error=ex;}});jobs.Add(job);job.Start();}foreach(var job in jobs)job.Join();
  Check(error==null&&DockBackend.ReadPins(root).Count(p=>p.name=="Concurrent")==12,"concurrent writers serialize whole transactions without lost entries");
  string pinsFile=Path.Combine(root,"pins.json");saved=File.ReadAllText(pinsFile);bool writeFailed=false;
  using(var reader=new FileStream(pinsFile,FileMode.Open,FileAccess.Read,FileShare.Read)){
   try{DockBackend.Pin(root,a,"--locked","Locked write","");}catch(IOException){writeFailed=true;}
   Check(writeFailed&&File.ReadAllText(pinsFile)==saved,"reader-held persistent pins still report write failures and preserve the saved configuration");
  }
  saved=File.ReadAllText(Path.Combine(root,"pins.json"));File.WriteAllText(Path.Combine(root,"pins.json"),"broken user configuration");bool refused=false;try{DockBackend.Pin(root,a,"","A","");}catch(ArgumentException){refused=true;}catch(InvalidDataException){refused=true;}
  Check(refused&&File.ReadAllText(Path.Combine(root,"pins.json"))=="broken user configuration","invalid existing configuration is preserved instead of silently replaced");File.WriteAllText(Path.Combine(root,"pins.json"),saved);
  Check(!Directory.EnumerateFiles(root,"*.tmp").Any(),"atomic transactions leave no staging files");
  Check(DockPreview.NextSelection(0,-1,5)==4&&DockPreview.NextSelection(4,1,5)==0&&DockPreview.ColumnsForWidth(640)==3,"preview navigation wraps across pages and adapts to small screens");
  var previewEntry=new DockEntry{id="test-preview",name="Fixture"};for(int i=0;i<5;i++)previewEntry.windows.Add(new DockWindow{handle=0,pid=UInt32.MaxValue,title="Fixture "+i,path="fixture"});
  using(var picker=new DockPreview(previewEntry,new Rectangle(0,400,700,100),false,true)){
   var keys=typeof(DockPreview).GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic);var selection=typeof(DockPreview).GetField("selected",BindingFlags.Instance|BindingFlags.NonPublic);
   object[] input={new Message(),Keys.Right};Check((bool)keys.Invoke(picker,input)&&(int)selection.GetValue(picker)==1,"the native preview consumes arrow keys and changes the selected real-window slot");
   keys.Invoke(picker,new object[]{new Message(),Keys.End});Check((int)selection.GetValue(picker)==4,"End crosses to the last preview page");
   keys.Invoke(picker,new object[]{new Message(),Keys.Tab});Check((int)selection.GetValue(picker)==0,"Tab is handled before WinForms default focus traversal");
   keys.Invoke(picker,new object[]{new Message(),Keys.Escape});Check(picker.IsDisposed,"Escape closes the native preview without activating a window");
  }
  backend.CancelDrag();Check(!Directory.EnumerateFiles(root,"dock-hold-*.txt").Any(),"cancel and commit release their Dock visibility leases");
  Console.WriteLine("Completed "+checks+" Dock interaction checks.");
 }
}
