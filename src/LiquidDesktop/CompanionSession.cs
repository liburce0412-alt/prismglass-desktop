using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class CompanionProcessState {
 internal const string FileName="companion-host.json";
 public int version=1,pid,connected;
 public string startTicks,state;
 public bool companionRunning,active,running,screen,pointer,audio;

 internal static CompanionProcessState Read(string root,string fileName=FileName){
  try{
   string source=DesktopSettingsFiles.Read(Path.Combine(root,fileName));
   if(String.IsNullOrEmpty(source)||source.Length>4096)return new CompanionProcessState();
   var status=new JavaScriptSerializer().Deserialize<CompanionProcessState>(source);
   if(status==null||status.version!=1||status.pid<=0)return new CompanionProcessState();
   using(var current=Process.GetCurrentProcess())using(var process=Process.GetProcessById(status.pid)){
    if(process.SessionId==current.SessionId&&process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)==status.startTicks&&String.Equals(process.MainModule.FileName,Path.Combine(root,"LiquidDesktop.exe"),StringComparison.OrdinalIgnoreCase))return status;
   }
  }catch(IOException){}catch(ArgumentException){}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}catch(UnauthorizedAccessException){}
  return new CompanionProcessState();
 }
}

internal sealed class CompanionSession : ApplicationContext {
 internal const string MutexName="Local\\PrismGlassCompanion";
 internal const string OpenRequest="companion.request",StopRequest="companion-stop.request",UpdateStopRequest="companion-update-stop.request",ControlsRequest="companion-controls-stop.request";
 readonly string root,hostFile;
 readonly Control owner=new Control();
 readonly Timer timer=new Timer{Interval=200};
 readonly Icon icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath)??SystemIcons.Application;
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 readonly CompanionProcessState status=new CompanionProcessState{state="starting"};
 readonly AppLibrary library;
 readonly SharedDesktopData shared;
 readonly DockLyrics lyrics;
 readonly CompanionHost companion;
 readonly AutoUpdates updates;
 readonly bool recoverOnly;
 CompanionManagementWindow management;
 NotifyIcon tray;
 string published="";
 bool starting=true,retainedSources,updating,readingStatus,closing,recoveryPending;
 DateTime recoveryAt,updateProbe;

 internal static void Open(string root){
  using(var process=Process.Start(new ProcessStartInfo(Path.Combine(root,"LiquidDesktop.exe"),"--companion"){UseShellExecute=false,CreateNoWindow=true})){}
 }
 internal CompanionSession(string directory,string mode){
  root=directory;hostFile=Path.Combine(root,CompanionProcessState.FileName);
  recoverOnly=mode=="--recover-companion";
  using(var process=Process.GetCurrentProcess()){status.pid=process.Id;status.startTicks=process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);}
  Publish();
  owner.CreateControl();
  updates=AutoUpdateRuntime.Create(root);
  var preferences=new DesktopPreferences(root);shared=new SharedDesktopData(root);lyrics=new DockLyrics(root,companionProcess:true);library=new AppLibrary(root);
  companion=new CompanionHost(owner,root,library,preferences,shared,lyrics);
  companion.ManagementRequested+=ShowManagement;
  Task.Run(()=>library.Refresh());
  timer.Tick+=(s,e)=>Tick();
  owner.BeginInvoke(new Action(async()=>{
   await RecoverDisplay();
   recoveryAt=DateTime.UtcNow.AddSeconds(5);
   if(mode=="--ensure-companion"&&!await companion.RestoreService())companion.Show();
   if(mode=="--companion")companion.Show();
   starting=false;status.state="running";timer.Start();Tick();
  }));
 }
 void Publish(){
  string state=json.Serialize(status);
  if(state==published)return;
  try{DesktopSettingsFiles.Write(hostFile,state);published=state;}catch(IOException){}
 }
 void Tick(){
  if(starting)return;
  if(DesktopSession.Take(root,StopRequest)){ExitThread();return;}
  if(DesktopSession.Take(root,UpdateStopRequest)&&companion.TryDrainIdle()){updating=true;ExitThread();return;}
  if(DesktopSession.Take(root,OpenRequest))companion.Show();
  if(DesktopSession.Take(root,ControlsRequest)){var pending=companion.StopRemoteControls();}
  DateTime now=DateTime.UtcNow;
  if(now>=updateProbe){updateProbe=now.AddSeconds(1);updates.Tick();}
  if(now>=recoveryAt){recoveryAt=now.AddSeconds(5);var recovery=RecoverDisplay();}
  status.companionRunning=companion.IsRunning;
  if(status.companionRunning)PublishActivity();
  else{status.running=status.screen=status.pointer=status.audio=false;status.connected=0;}
  status.active=companion.IsBusy;
  Publish();
  if(status.companionRunning){retainedSources=true;ShowTray();lyrics.Tick(now,false,false,false);return;}
  CloseTray();CleanupSources();
  if(!status.active&&(management==null||management.IsDisposed)&&!(recoverOnly&&recoveryPending&&CompanionProcessState.Read(root,"desktop-host.json").pid!=0))ExitThread();
 }
 async Task RecoverDisplay(){recoveryPending=!await companion.RecoverIdleDisplay();}
 async void PublishActivity(){
  if(readingStatus)return;
  readingStatus=true;int revision=companion.IslandRevision;
  try{
   var values=await Task.Run(()=>companion.IslandSnapshot());
   if(closing||!status.companionRunning||revision!=companion.IslandRevision)return;
   status.running=values.running;status.connected=values.connected;status.screen=values.screen;status.pointer=values.pointer;status.audio=values.audio;
   Publish();
  }finally{readingStatus=false;}
 }
 void ShowManagement(){
  if(management==null||management.IsDisposed){management=new CompanionManagementWindow(root,companion,icon);management.FormClosed+=(s,e)=>companion.DialogOwner=null;}
  companion.DialogOwner=management;management.Show();
  if(management.WindowState==FormWindowState.Minimized)management.WindowState=FormWindowState.Normal;
  management.Activate();
 }
 void ShowTray(){
  if(tray!=null)return;
  var menu=new ContextMenuStrip();
  menu.Items.Add("管理 iPad 伴随屏",null,(s,e)=>companion.Show());
  menu.Items.Add("关闭 iPad 伴随屏",null,(s,e)=>companion.Manage(new Dictionary<string,object>{{"command","stop"}}));
  tray=new NotifyIcon{Icon=icon,Text="PrismGlass · iPad 伴随屏",ContextMenuStrip=menu,Visible=true};
  tray.DoubleClick+=(s,e)=>companion.Show();
 }
 void CloseTray(){if(tray!=null){tray.Visible=false;tray.ContextMenuStrip.Dispose();tray.Dispose();tray=null;}}
 void CleanupSources(){
  if(!retainedSources)return;
  lyrics.StopSampling();
  try{
   string script=Path.GetFullPath(Path.Combine(root,"..","DesktopMode.ps1"));
   using(var process=Process.Start(new ProcessStartInfo(StartupTransition.DesktopModeShell(script),"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -Mode CompanionCleanup"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){}
   retainedSources=false;
  }catch(Exception error){try{File.WriteAllText(Path.Combine(root,"companion-cleanup-error.txt"),error.ToString());}catch(IOException){}}
 }
 protected override void ExitThreadCore(){
  closing=true;timer.Stop();updates.Dispose();status.state="stopping";Publish();CloseTray();
  if(management!=null)management.Dispose();
  try{companion.Dispose();}finally{
   status.companionRunning=false;Publish();if(!updating)CleanupSources();
   lyrics.Dispose();library.Dispose();shared.Dispose();owner.Dispose();timer.Dispose();
   try{File.Delete(hostFile);}catch(IOException){}
   base.ExitThreadCore();
  }
 }
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
   var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"runtime-data","companion-webview"));if(IsDisposed)return;
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
