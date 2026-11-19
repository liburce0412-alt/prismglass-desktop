using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class DesktopSession : ApplicationContext {
 readonly string root,hostFile,startTicks;
 readonly int pid;
 readonly Control owner=new Control();
 readonly Timer timer=new Timer{Interval=200};
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 internal readonly DesktopPreferences Preferences;
 internal readonly SharedDesktopData SharedData;
 internal readonly DockLyrics Lyrics;
 internal readonly AppLibrary Library;
 internal readonly AutoUpdates Updates;
 DateTime updateProbe;
 LiquidDesktop renderer;
 string phase="starting";
 bool precise;

 internal DesktopSession(string directory){
  root=directory;hostFile=Path.Combine(root,"desktop-host.json");
  using(var process=Process.GetCurrentProcess()){pid=process.Id;startTicks=process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);}
  owner.CreateControl();
  Updates=AutoUpdateRuntime.Create(root);
  Preferences=new DesktopPreferences(root);SharedData=new SharedDesktopData(root);Lyrics=new DockLyrics(root);Library=new AppLibrary(root);
  Task.Run(()=>Library.Refresh());
  Publish();
  timer.Tick+=(s,e)=>Tick();timer.Start();
  owner.BeginInvoke(new Action(Tick));
 }

 internal static void Request(string root,string name){DesktopSettingsFiles.Write(Path.Combine(root,name),"requested");}
 internal static bool Take(string root,string name){
  string path=Path.Combine(root,name);
  if(!File.Exists(path))return false;
  try{File.Delete(path);return true;}catch(IOException){return false;}
 }
 void Publish(){try{DesktopSettingsFiles.Write(hostFile,json.Serialize(new{version=1,pid=pid,startTicks=startTicks,state=phase,companionRunning=false}));}catch(IOException){}}
 void Tick(){
  if(Take(root,"maintenance-stop.request")||Take(root,"stop.request")){if(renderer!=null&&phase!="stopping")renderer.Close();else if(renderer==null)ExitThread();return;}
  if(renderer==null&&WallpaperRefresh.CanPrepareStartup)Attach();
  var now=DateTime.UtcNow;
  if(now>=updateProbe){updateProbe=now.AddSeconds(1);Updates.Tick(CompanionProcessState.Read(root).pid==0);}
 }
 void Attach(){
  try{
   File.Delete(Path.Combine(root,"readiness.json"));
   precise=timeBeginPeriod(1)==0;
   Preferences.Reload();WallpaperRefresh.PrepareStartup(root);
   renderer=new LiquidDesktop(this);
   renderer.FormClosed+=(s,e)=>owner.BeginInvoke(new Action(Detach));
   renderer.Show();
  }catch(Exception error){
   Log("startup-exception.txt",error);
   if(renderer!=null)renderer.Close();else Detach();
  }
 }
 internal void Running(){if(phase=="starting"){phase="running";Publish();}}
 internal void Stopping(){phase="stopping";Publish();}
 void Detach(){
  if(precise){timeEndPeriod(1);precise=false;}
  try{if(!CursorPreferences.Restore(Path.GetFullPath(Path.Combine(root,".."))))throw new InvalidOperationException("Windows 未接受指针恢复。");}
  catch(Exception error){Log("exit-cleanup-error.txt",error);}
  try{File.Delete(Path.Combine(root,"readiness.json"));}catch(IOException error){Log("exit-cleanup-error.txt",error);}
  renderer=null;ExitThread();
 }
 void Log(string name,Exception error){try{File.WriteAllText(Path.Combine(root,name),error.ToString());}catch(IOException){}}
 protected override void ExitThreadCore(){
  timer.Stop();Updates.Dispose();Lyrics.Dispose();Library.Dispose();SharedData.Dispose();owner.Dispose();timer.Dispose();
  try{File.Delete(hostFile);}catch(IOException){}
  base.ExitThreadCore();
 }
 [DllImport("winmm.dll")]static extern uint timeBeginPeriod(uint period);
 [DllImport("winmm.dll")]static extern uint timeEndPeriod(uint period);
}
