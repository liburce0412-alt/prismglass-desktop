using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class DesktopSession : ApplicationContext {
 readonly string root,hostFile,startTicks;
 readonly int pid;
 readonly Control owner=new Control();
 readonly Timer timer=new Timer{Interval=200};
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 readonly Icon icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath)??SystemIcons.Application;
 internal readonly DesktopPreferences Preferences;
 internal readonly SharedDesktopData SharedData;
 internal readonly DockLyrics Lyrics;
 internal readonly AppLibrary Library;
 internal readonly CompanionHost Companion;
 LiquidDesktop renderer;
 CompanionManagementWindow management;
 NotifyIcon companionTray;
 string phase="detached",published="";
 bool attachRequested,openRequested,maintenance,precise,retainedSources;
 DateTime recoveryAt;

 internal DesktopSession(string directory,bool desktop){
  root=directory;hostFile=Path.Combine(root,"desktop-host.json");
  using(var process=Process.GetCurrentProcess()){pid=process.Id;startTicks=process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);}
  owner.CreateControl();
  Preferences=new DesktopPreferences(root);SharedData=new SharedDesktopData(root);Lyrics=new DockLyrics(root);Library=new AppLibrary(root);
  Companion=new CompanionHost(owner,root,Library,Preferences,SharedData,Lyrics);
  Companion.ManagementRequested+=ShowCompanion;
  Task.Run(()=>Library.Refresh());
  attachRequested=desktop;openRequested=!desktop;
  if(desktop)phase="starting";
  Publish();
  Companion.RecoverIdleDisplay(startup:true).GetAwaiter().GetResult();recoveryAt=DateTime.UtcNow.AddSeconds(5);
  timer.Tick+=(s,e)=>Tick();timer.Start();
  owner.BeginInvoke(new Action(Tick));
 }

 internal static void Request(string root,string name){DesktopSettingsFiles.Write(Path.Combine(root,name),"requested");}
 bool Take(string name){
  string path=Path.Combine(root,name);
  if(!File.Exists(path))return false;
  try{File.Delete(path);return true;}catch(IOException){return false;}
 }
 void Publish(){
  string state=json.Serialize(new{pid=pid,startTicks=startTicks,state=phase,companionRunning=Companion.IsRunning});
  if(published==state)return;
  try{DesktopSettingsFiles.Write(hostFile,state);published=state;}catch(IOException){}
 }
 void Tick(){
  if(Take("maintenance-stop.request"))maintenance=true;
  if(maintenance||Take("stop.request")){attachRequested=false;if(renderer!=null&&phase!="stopping")renderer.Close();}
  if(maintenance){if(renderer==null)ExitThread();return;}
  if(Take("desktop-start.request")&&(renderer==null||phase=="stopping"))attachRequested=true;
  if(renderer==null&&attachRequested&&WallpaperRefresh.CanPrepareStartup)Attach();
  if(Take("companion.request")||openRequested){openRequested=false;Companion.Show();}
  DateTime now=DateTime.UtcNow;
  if(now>=recoveryAt){recoveryAt=now.AddSeconds(5);var recovery=Companion.RecoverIdleDisplay();}
  if(Companion.IsRunning&&(renderer==null||!renderer.SamplesMedia)){Lyrics.DesktopMediaActive=false;Lyrics.Tick(now,false,false,false);}
  Publish();
  if(renderer!=null)return;
  if(Companion.IsRunning){retainedSources=true;ShowTray();return;}
  CloseTray();CleanupSources();
  if(!attachRequested&&(management==null||management.IsDisposed))ExitThread();
 }

 void Attach(){
  attachRequested=false;phase="starting";Publish();
  CloseManagement();CloseTray();
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
 internal void Running(){if(phase=="starting"){phase="running";retainedSources=false;Publish();}}
 internal void Stopping(){phase="stopping";Publish();}
 void Detach(){
  if(precise){timeEndPeriod(1);precise=false;}
  try{if(!CursorPreferences.Restore(Path.GetFullPath(Path.Combine(root,".."))))throw new InvalidOperationException("Windows 未接受指针恢复。");}
  catch(Exception error){Log("exit-cleanup-error.txt",error);}
  try{File.Delete(Path.Combine(root,"readiness.json"));}catch(IOException error){Log("exit-cleanup-error.txt",error);}
  renderer=null;phase="detached";retainedSources|=Companion.IsRunning;
  if(!Companion.IsRunning&&!retainedSources)Lyrics.StopSampling();
  Publish();
 }

 void ShowCompanion(){
  if(maintenance)return;
  if(renderer!=null&&phase!="stopping"){renderer.OpenWorkspace("companion");return;}
  if(management==null||management.IsDisposed){management=new CompanionManagementWindow(root,Companion,icon);management.FormClosed+=(s,e)=>Companion.DialogOwner=null;}
  Companion.DialogOwner=management;management.Show();
  if(management.WindowState==FormWindowState.Minimized)management.WindowState=FormWindowState.Normal;
  management.Activate();
 }
 void ShowTray(){
  if(companionTray!=null)return;
  var menu=new ContextMenuStrip();
  menu.Items.Add("管理 iPad 伴随屏",null,(s,e)=>ShowCompanion());
  menu.Items.Add("关闭 iPad 伴随屏",null,(s,e)=>Companion.Manage(new Dictionary<string,object>{{"command","stop"}}));
  companionTray=new NotifyIcon{Icon=icon,Text="PrismGlass · iPad 伴随屏",ContextMenuStrip=menu,Visible=true};
  companionTray.DoubleClick+=(s,e)=>ShowCompanion();
 }
 void CloseTray(){if(companionTray!=null){companionTray.Visible=false;companionTray.ContextMenuStrip.Dispose();companionTray.Dispose();companionTray=null;}}
 void CloseManagement(){if(management!=null){management.Dispose();management=null;Companion.DialogOwner=null;}}
 void CleanupSources(){
  if(!retainedSources||Companion.IsRunning)return;
  Lyrics.StopSampling();
  try{
   string script=Path.GetFullPath(Path.Combine(root,"..","DesktopMode.ps1"));
   using(var process=Process.Start(new ProcessStartInfo(StartupTransition.DesktopModeShell(script),"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -Mode CompanionCleanup"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){}
   retainedSources=false;
  }catch(Exception error){Log("companion-cleanup-error.txt",error);}
 }
 void Log(string name,Exception error){try{File.WriteAllText(Path.Combine(root,name),error.ToString());}catch(IOException){}}
 protected override void ExitThreadCore(){
  timer.Stop();CloseTray();CloseManagement();
  try{Companion.Dispose();}finally{
   Publish();if(!maintenance)CleanupSources();
   Lyrics.Dispose();Library.Dispose();SharedData.Dispose();owner.Dispose();timer.Dispose();
   try{File.Delete(hostFile);}catch(IOException){}
   base.ExitThreadCore();
  }
 }
 [DllImport("winmm.dll")]static extern uint timeBeginPeriod(uint period);
 [DllImport("winmm.dll")]static extern uint timeEndPeriod(uint period);
}

internal sealed class CompanionManagementWindow : Form {
 const string Page="https://prism-companion.local/companion-management.html";
 readonly WebView2 view=new WebView2{Dock=DockStyle.Fill};
 readonly Timer refresh=new Timer{Interval=1000};
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 readonly CompanionHost companion;
 string previous="";
 bool ready;
 internal CompanionManagementWindow(string root,CompanionHost companion,Icon icon){
  this.companion=companion;Icon=icon;Text="PrismGlass · iPad 伴随屏";StartPosition=FormStartPosition.CenterScreen;
  ClientSize=new Size(804,650);MinimumSize=new Size(420,400);Controls.Add(view);
  companion.ManagementChanged+=UpdateSnapshot;
  refresh.Tick+=(s,e)=>UpdateSnapshot();refresh.Start();
  Shown+=async(s,e)=>{try{
   var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"runtime-data"));if(IsDisposed)return;
   await view.EnsureCoreWebView2Async(environment);if(IsDisposed)return;
   var web=view.CoreWebView2;web.Settings.AreDevToolsEnabled=false;web.Settings.AreDefaultContextMenusEnabled=false;web.Settings.IsStatusBarEnabled=false;
   web.SetVirtualHostNameToFolderMapping("prism-companion.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
   web.NavigationStarting+=(sender,e2)=>{if(e2.Uri!=Page)e2.Cancel=true;};web.NewWindowRequested+=(sender,e2)=>e2.Handled=true;
   web.WebMessageReceived+=(sender,e2)=>{
    if(e2.Source!=Page)return;
    try{
     var message=json.Deserialize<Dictionary<string,object>>(e2.TryGetWebMessageAsString());
     if(message==null||CompanionBridge.Text(message,"action")!="companion")return;
     ready=true;companion.Manage(message);UpdateSnapshot();
    }catch(ArgumentException){}catch(InvalidOperationException){}
   };
   view.Source=new Uri(Page);
  }catch(Exception error){
   if(IsDisposed)return;
   view.Hide();Controls.Add(new Label{AutoSize=true,Left=16,Top=16,Text="无法打开伴随屏管理。可从托盘关闭连接。"});
   try{File.WriteAllText(Path.Combine(root,"workspace-error.txt"),error.ToString());}catch(IOException){}
  }};
 }
 void UpdateSnapshot(){
  if(!ready||IsDisposed)return;
  string next=json.Serialize(companion.ManagementSnapshot());
  if(next!=previous){view.CoreWebView2.PostWebMessageAsJson(next);previous=next;}
 }
 protected override void Dispose(bool disposing){if(disposing){companion.ManagementChanged-=UpdateSnapshot;refresh.Dispose();}base.Dispose(disposing);}
}
