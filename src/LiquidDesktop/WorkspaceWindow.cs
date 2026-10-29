using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class WorkspacePlacement {
 public static Rectangle Calculate(Rectangle area,Rectangle screen,Rectangle dock,bool apps){
  if(area.Width<=0||area.Height<=0)return Rectangle.Empty;
  int width=Math.Min(1000,Math.Min(area.Width,Math.Max(560,area.Width-80)));
  int height=Math.Min(720,Math.Min(area.Height,Math.Max(420,area.Height-100)));
  int x=area.Left+(area.Width-width)/2,y=area.Top+(area.Height-height)/2;
  if(apps){
   // All rectangles are physical screen coordinates; a neighboring Dock is never an anchor.
   bool anchored=dock.Width>0&&dock.Height>0&&screen.Contains(new Point(dock.Left+dock.Width/2,dock.Top+dock.Height/2));
   int bottom=anchored?dock.Top-16:area.Bottom-16;
   if(bottom>area.Top)height=Math.Min(height,bottom-area.Top);
   x=anchored?dock.Left+(dock.Width-width)/2:x;y=bottom-height;
  }
  x=Math.Max(area.Left,Math.Min(x,area.Right-width));y=Math.Max(area.Top,Math.Min(y,area.Bottom-height));
  return new Rectangle(x,y,width,height);
 }
}

internal sealed class WorkspaceWindow : Form {
 readonly string root;readonly AppLibrary library;readonly DesktopPreferences preferences;
 readonly WebView2 view=new WebView2();readonly WebViewPixelScale pixels;readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=16777216};
 readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();readonly WidgetBackend widgets;
 CancellationTokenSource weatherSearch;List<WeatherPlace> weatherResults=new List<WeatherPlace>();string weatherObserved;int weatherRequest;string requestedSection="";
 CancellationTokenSource releaseCheck;ReleaseUpdateState releaseUpdate=new ReleaseUpdateState{current=Application.ProductVersion};
 bool ready,closed,reading,interacting,picker,runtimePaused;int generation;string page="apps",widgetKey="",preferenceKey="";Screen display;Rectangle dockAnchor;object runtime;
 public event Action SettingsChanged;
 CompanionHost companion;
 public CompanionHost Companion {get{return companion;}set{if(companion!=null)companion.ManagementChanged-=OnCompanionChanged;companion=value;if(companion!=null)companion.ManagementChanged+=OnCompanionChanged;}}
 void OnCompanionChanged(){if(IsOpen&&companionVisible)PublishCompanion();}
 readonly System.Windows.Forms.Timer dismissal=new System.Windows.Forms.Timer{Interval=100};
 Rectangle settingsBounds;bool placing,companionVisible;string companionKey="";
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder value,int length);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder value,int length);
 static bool DockActive(){var h=GetForegroundWindow();var name=new StringBuilder(256);var title=new StringBuilder(1024);GetClassName(h,name,name.Capacity);GetWindowText(h,title,title.Capacity);return DockBackend.IsPrismDock(name.ToString(),title.ToString());}
 public SharedDesktopData SharedData {get;set;}
 public bool IsOpen {get{return Visible&&!closed&&WindowState!=FormWindowState.Minimized;}}
 public bool IsInteracting {get{return interacting||picker;}}
 public WorkspaceWindow(string directory,AppLibrary apps,DesktopPreferences settings){
  root=directory;library=apps;preferences=settings;widgets=new WidgetBackend(root);pixels=new WebViewPixelScale(view,()=>PublishLayout());Text="PrismGlass · 应用与设置";
  FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=true;TopMost=true;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;BackColor=Color.FromArgb(23,37,57);MinimumSize=new Size(560,420);
  view.Dock=DockStyle.Fill;Controls.Add(view);WebViewVisibility.Track(view);display=Screen.PrimaryScreen;Place();
  Shown+=async(s,e)=>{if(ready)return;try{
   await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"runtime-data")));pixels.Sync();
   view.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;view.CoreWebView2.Settings.AreDevToolsEnabled=false;view.CoreWebView2.Settings.IsZoomControlEnabled=false;view.CoreWebView2.Settings.IsStatusBarEnabled=false;view.CoreWebView2.Settings.IsNonClientRegionSupportEnabled=true;
   view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-workspace.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
   view.CoreWebView2.NavigationStarting+=(sender,a)=>{if(a.Uri!="https://prism-workspace.local/workspace.html")a.Cancel=true;};
   view.CoreWebView2.NewWindowRequested+=(sender,a)=>a.Handled=true;
   view.CoreWebView2.WebMessageReceived+=(sender,a)=>{if(a.Source=="https://prism-workspace.local/workspace.html")Receive(a.TryGetWebMessageAsString());};
   view.CoreWebView2.ProcessFailed+=(sender,a)=>{ready=false;Hide();File.WriteAllText(Path.Combine(root,"workspace-error.txt"),a.ProcessFailedKind.ToString());Dispose();};
   view.Source=new Uri("https://prism-workspace.local/workspace.html");
  }catch(Exception ex){File.WriteAllText(Path.Combine(root,"workspace-error.txt"),ex.ToString());Hide();}};
  Deactivate+=(s,e)=>{if(page=="apps"&&!IsInteracting){if(DockActive()||dockAnchor.Contains(Cursor.Position))dismissal.Start();else Hide();}};
  Activated+=(s,e)=>dismissal.Stop();
  dismissal.Tick+=(s,e)=>{if(!Visible||page!="apps"||ContainsFocus){dismissal.Stop();return;}if(!IsInteracting&&!DockActive()){dismissal.Stop();Hide();}};
  VisibleChanged+=(s,e)=>{if(!Visible){generation++;interacting=false;CancelWeatherSearch();Send(new{type="visibility",visible=false});}else if(ready){Send(new{type="visibility",visible=true});Publish();}};
  FormClosing+=(s,e)=>{if(!closed&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
  timer.Interval=1000;timer.Tick+=(s,e)=>{if(IsOpen){pixels.Sync();PublishSettings();if(page=="settings"){PublishLyrics();PublishCompanion();}}};timer.Start();
  Resize+=(s,e)=>{view.Visible=Visible&&WindowState!=FormWindowState.Minimized;Shape();if(ready){Send(new{type="visibility",visible=IsOpen});PublishLayout();}};
  Move+=(s,e)=>{if(!placing&&Visible){display=Screen.FromControl(this);if(page=="settings")settingsBounds=Bounds;if(ready)PublishLayout();}};
 }
 protected override void SetVisibleCore(bool value){if(!value)view.Visible=false;base.SetVisibleCore(value);if(value)view.Visible=WindowState!=FormWindowState.Minimized;}
 void Shape(){if(Width<24||Height<24)return;using(var path=new GraphicsPath()){const int d=48;path.AddArc(0,0,d,d,180,90);path.AddArc(Width-d,0,d,d,270,90);path.AddArc(Width-d,Height-d,d,d,0,90);path.AddArc(0,Height-d,d,d,90,90);path.CloseFigure();var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}}
 void Place(){placing=true;try{var bounds=WorkspacePlacement.Calculate(display.WorkingArea,display.Bounds,dockAnchor,page=="apps");if(page=="settings"&&!settingsBounds.IsEmpty){display=Screen.FromRectangle(settingsBounds);var area=display.WorkingArea;bounds=new Rectangle(Math.Max(area.Left,Math.Min(settingsBounds.Left,area.Right-bounds.Width)),Math.Max(area.Top,Math.Min(settingsBounds.Top,area.Bottom-bounds.Height)),Math.Min(bounds.Width,area.Width),Math.Min(bounds.Height,area.Height));}MinimumSize=new Size(Math.Min(560,bounds.Width),Math.Min(420,bounds.Height));Bounds=bounds;Shape();pixels.Sync();}finally{placing=false;}}
 public void OpenPage(string next,Screen screen,Rectangle dockBounds=default(Rectangle)){if(closed)return;if(next=="toggle-apps"&&IsOpen&&page=="apps"){Hide();return;}if(next=="toggle-apps")next="apps";dismissal.Stop();display=screen??Screen.PrimaryScreen;dockAnchor=dockBounds;requestedSection=next=="welcome"||next=="weather"||next=="updates"||next=="companion"?next:"";page=next=="apps"?"apps":"settings";WindowState=FormWindowState.Normal;Place();Show();Activate();view.Focus();if(ready){Publish();RefreshApps(false);}if(next=="updates")CheckUpdates();}
 void PublishCompanion(){if(Companion==null||!IsOpen||!companionVisible)return;string key=json.Serialize(Companion.ManagementSnapshot());if(key!=companionKey){companionKey=key;Send(new{type="companion",value=json.DeserializeObject(key)});}}
 public void PublishRuntime(object value,bool motionPaused=false){runtime=value;runtimePaused=motionPaused;if(IsOpen){Send(new{type="runtime",value=runtime});PublishLayout();}}
 void Send(object message){if(ready&&!closed&&view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));}
 void PublishLayout(){var b=display.Bounds;Send(new{type="layout",width=ClientSize.Width,height=ClientSize.Height,sceneWidth=b.Width,sceneHeight=b.Height,originX=Left-b.Left,originY=Top-b.Top,theme=preferences.Current.theme,paused=runtimePaused||preferences.Current.manualPaused||preferences.Current.reducedMotion,wallpaper=File.GetLastWriteTimeUtc(Path.Combine(root,"wallpaper.png")).Ticks});}
 void Publish(){PublishLayout();PublishSettings(true);PublishLyrics(true);PublishWeather();PublishStartup();PublishUpdates();Send(new{type="page",page=page,section=requestedSection});requestedSection="";Send(new{type="apps",snapshot=library.GetSnapshot()});if(runtime!=null)Send(new{type="runtime",value=runtime});}
 void PublishStartup(){Send(new{type="startup",value=LoginStartup.Read(root)});}
 void PublishUpdates(){Send(new{type="updates",value=releaseUpdate});}
 async void CheckUpdates(){
  if(releaseCheck!=null)return;var operation=new CancellationTokenSource();releaseCheck=operation;
  releaseUpdate=new ReleaseUpdateState{current=Application.ProductVersion,state="checking",message="正在检查最新正式版本…"};PublishUpdates();
  try{var result=await ReleaseUpdates.Check(Application.ProductVersion,operation.Token);if(!closed){releaseUpdate=result;PublishUpdates();}}
  catch(OperationCanceledException){}finally{if(releaseCheck==operation)releaseCheck=null;operation.Dispose();}
 }
 void PublishWeather(){try{weatherObserved=WeatherLocation.ReadText(root);Send(new{type="weather-location",place=WeatherLocation.ParseSaved(weatherObserved),error=""});}catch(Exception ex){Send(new{type="weather-location",place=(WeatherPlace)null,error=ex.Message});}}
 void CancelWeatherSearch(){weatherRequest++;weatherResults.Clear();if(weatherSearch!=null){weatherSearch.Cancel();weatherSearch=null;}}
 async void SearchWeather(string query,int request){CancelWeatherSearch();weatherRequest=request;var operation=new CancellationTokenSource();weatherSearch=operation;try{var results=await WeatherLocation.Search(query,operation.Token);if(!closed&&IsOpen&&weatherSearch==operation){weatherResults=results;Send(new{type="weather-results",request=request,places=results,error=""});}}
  catch(OperationCanceledException){if(!operation.IsCancellationRequested&&!closed&&IsOpen)Send(new{type="weather-results",request=request,places=new WeatherPlace[0],error="城市搜索超时，请检查网络后重试。"});}
  catch(Exception ex){if(!operation.IsCancellationRequested&&!closed&&IsOpen)Send(new{type="weather-results",request=request,places=new WeatherPlace[0],error=ex is HttpRequestException?"无法连接城市服务，请检查网络后重试。":ex.Message});}
  finally{if(weatherSearch==operation)weatherSearch=null;operation.Dispose();}
 }
 void PublishSettings(bool force=false){preferences.Reload();var snapshot=preferences.Snapshot();string key=json.Serialize(snapshot);if(force||key!=preferenceKey){preferenceKey=key;Send(new{type="settings",snapshot=snapshot});PublishLayout();}}
 void PublishLyrics(bool force=false){var state=SharedData==null?widgets.Read():SharedData.ReadWidgets();string key=json.Serialize(state);if(force||key!=widgetKey){widgetKey=key;Send(state);}}
 async void RefreshApps(bool force){if(reading)return;if(!force&&!library.NeedsRefresh){Send(new{type="apps",snapshot=library.GetSnapshot()});return;}reading=true;int ticket=++generation;Send(new{type="apps-loading",loading=true});try{await Task.Run(()=>library.Refresh());if(!closed&&(ticket==generation||IsOpen))Send(new{type="apps",snapshot=library.GetSnapshot()});}catch(Exception ex){if(!closed)Send(new{type="error",message=ex.Message});}finally{reading=false;if(!closed)Send(new{type="apps-loading",loading=false});}}
 static string Value(Dictionary<string,object> message,string key){object value;return message.TryGetValue(key,out value)?Convert.ToString(value):"";}
 void Receive(string text){try{
  if(text=="ready"){ready=true;Publish();RefreshApps(false);return;}
  if(text=="painted")return;
  if(text!=null&&text.StartsWith("error:",StringComparison.Ordinal)){File.WriteAllText(Path.Combine(root,"workspace-error.txt"),text);return;}
  if(text!=null&&text.StartsWith("action:lyrics-",StringComparison.Ordinal)){widgets.Action(text.Substring(7));if(SharedData!=null)SharedData.Invalidate();PublishLyrics(true);return;}
  if(String.IsNullOrEmpty(text)||text.Length>65536)return;
  var message=json.Deserialize<Dictionary<string,object>>(text);string action=Value(message,"action");
  if(action=="close"){Hide();return;}if(action=="interaction"){interacting=Value(message,"active").Equals("true",StringComparison.OrdinalIgnoreCase);return;}
  if(action=="section"){companionVisible=Value(message,"section")=="companion";return;}
  if(action=="page"){companionVisible=Value(message,"page")=="settings"&&Value(message,"section")=="companion";string next=Value(message,"page")=="apps"?"apps":"settings";if(page!=next){page=next;Place();PublishLayout();}return;}
  if(action=="settings"){
   object patch,revision;if(!message.TryGetValue("changes",out patch)||!(patch is Dictionary<string,object>)||!message.TryGetValue("revision",out revision))throw new ArgumentException("设置内容不完整，请重新打开。");
   preferences.Apply((Dictionary<string,object>)patch,Convert.ToInt64(revision));PublishSettings(true);Send(new{type="saved",scope="settings",message="已保存"});var handler=SettingsChanged;if(handler!=null)handler();return;
  }
  if(action=="companion"){if(Companion!=null){Companion.DialogOwner=this;companionVisible=true;Companion.Manage(message);PublishCompanion();}return;}
  if(action=="updates-check"){CheckUpdates();return;}
  if(action=="updates-open"){string address=releaseUpdate.state=="error"?ReleaseUpdates.LatestPage:releaseUpdate.state=="available"&&ReleaseUpdates.IsReleaseUrl(releaseUpdate.url,releaseUpdate.tag)?releaseUpdate.url:null;if(address==null)throw new InvalidOperationException("请先检查更新，取得有效的官方发布地址。");try{Process.Start(new ProcessStartInfo(address){UseShellExecute=true});}catch{releaseUpdate.state="error";releaseUpdate.message="无法打开浏览器，请检查默认浏览器后重试。";PublishUpdates();}return;}
  if(action=="startup-read"){PublishStartup();return;}
  if(action=="startup"){object value;if(!message.TryGetValue("enabled",out value)||!(value is bool))throw new ArgumentException("自启设置无效。");try{var state=LoginStartup.Set(root,(bool)value);Send(new{type="startup",value=state});if(state.error.Length>0)throw new IOException(state.error);Send(new{type="saved",message=state.enabled?"已开启：下次登录 Windows 时启动 PrismGlass":"已关闭开机自启"});}catch{PublishStartup();throw;}return;}
  if(action=="weather-cancel"){CancelWeatherSearch();return;}
  if(action=="weather-search"){int request;if(!Int32.TryParse(Value(message,"request"),out request)||request<1)throw new ArgumentException("城市搜索请求无效。");SearchWeather(Value(message,"query"),request);return;}
  if(action=="weather-select"){int request,index;if(!Int32.TryParse(Value(message,"request"),out request)||request!=weatherRequest||!Int32.TryParse(Value(message,"index"),out index)||index<0||index>=weatherResults.Count)throw new ArgumentException("城市列表已经更新，请重新搜索。");WeatherLocation.Save(root,weatherResults[index],weatherObserved);PublishWeather();Send(new{type="saved",message="城市已保存，天气正在更新"});return;}
  if(action=="system"){SystemAction(Value(message,"command"));return;}
  if(action=="apps")Apps(message);
 }catch(Exception ex){PublishSettings(true);Send(new{type="error",message=ex.Message});Send(new{type="action-error",message=ex.Message});}}
 void Apps(Dictionary<string,object> message){string command=Value(message,"command"),id=Value(message,"id");
  if(command=="refresh"){RefreshApps(true);return;}
  if(command=="dragBegin"){var entry=library.Find(id);if(entry!=null&&entry.exists&&entry.kind!="action"){DockBackend.BeginExternalDrag(root,entry.path,entry.arguments,entry.name,null);interacting=true;}return;}
  if(command=="dragEnd"){DockBackend.EndExternalDrag(root,Value(message,"cancelled").Equals("true",StringComparison.OrdinalIgnoreCase));interacting=false;return;}
  if(command=="pick"||command=="pickFolder"||command=="repair"){
   var repairing=command=="repair"?library.Find(id):null;
   picker=true;try{string path=null;if(command=="pickFolder"||repairing!=null&&repairing.kind=="directory"){using(var dialog=new FolderBrowserDialog{Description="选择要收录的文件夹",ShowNewFolderButton=false})if(dialog.ShowDialog(this)==DialogResult.OK)path=dialog.SelectedPath;}
    else using(var dialog=new OpenFileDialog{Title=command=="repair"?"重新定位入口":"添加应用、快捷方式或文件",CheckFileExists=true,Filter="应用、快捷方式和文件|*.*"})if(dialog.ShowDialog(this)==DialogResult.OK)path=dialog.FileName;
    if(path!=null){if(command=="repair")library.Repair(id,path);else library.Add(path);Send(new{type="apps",snapshot=library.GetSnapshot()});}
   }finally{picker=false;Activate();}return;
  }
  if(command=="open"||command=="pin"||command=="reveal"){
   if(id.StartsWith("action:",StringComparison.Ordinal)){if(command=="open")SystemAction(id.Substring(7));return;}
   var entry=library.Find(id);if(entry==null)throw new ArgumentException("入口已更新，请重新搜索。");
   if(entry.kind=="action"){if(command=="open")SystemAction(entry.path);return;}
   if(!entry.exists)throw new FileNotFoundException("入口已失效，请在菜单中重新定位。");
   if(command=="pin"){DockBackend.Pin(root,entry.path,entry.arguments,entry.name,null);Send(new{type="saved",message="已固定到底部 Dock"});return;}
   if(command=="reveal"){if(entry.kind=="uwp")throw new InvalidOperationException("此商店应用没有可打开的文件位置。");Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+entry.path+"\""){UseShellExecute=true});return;}
   CompanionAppLaunch.Open(entry);Hide();return;
  }
  library.Apply(command,id,Value(message,"target"),Value(message,"name"));Send(new{type="apps",snapshot=library.GetSnapshot()});Send(new{type="saved",message="已保存"});
 }
 void SystemAction(string command){switch(command){
  case "settings":page="settings";Place();PublishLayout();Send(new{type="page",page=page});return;
  case "windows-settings":Process.Start(new ProcessStartInfo("ms-settings:"){UseShellExecute=true});break;
  case "startup-settings":Process.Start(new ProcessStartInfo("ms-settings:startupapps"){UseShellExecute=true});break;
  case "cursor-settings":File.WriteAllText(Path.Combine(root,"shelf.request"),"manage");Hide();break;
  case "task-manager":Process.Start(new ProcessStartInfo("taskmgr.exe"){UseShellExecute=true});break;
  case "explorer":Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true});break;
  default:throw new ArgumentException("无法识别的系统操作。");
 }}
 protected override void Dispose(bool disposing){if(disposing&&!closed){closed=true;Companion=null;generation++;CancelWeatherSearch();if(releaseCheck!=null)releaseCheck.Cancel();timer.Stop();timer.Dispose();dismissal.Dispose();view.Dispose();}base.Dispose(disposing);}
}
