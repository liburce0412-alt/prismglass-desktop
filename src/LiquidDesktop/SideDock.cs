using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class SideDock : Form {
 readonly string root;
 readonly bool preview;
 readonly WebView2 view=new WebView2();
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 readonly SideDockData source;readonly WidgetBackend widgets;
 readonly Timer timer=new Timer();
 bool ready,painted,reading,closed;string theme="",layout="";float scale=1;float panelWidth,panelHeight,railHeight=668;
 DateTime visibilityRead=DateTime.MinValue,visibilityStamp=DateTime.MinValue;string visibilityState="";
 DateTime widgetRead=DateTime.MinValue,dataRead=DateTime.MinValue,heartbeat=DateTime.MinValue;string lastWidgets="";bool automaticPause;
 public bool IsReady {get{return painted&&!closed;}}
 Rectangle screen;bool opened;string pendingMode;bool motionPaused;float networkShape,bridgeY=40,panelTop=4;
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr h,uint command);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr h);
 protected override bool ShowWithoutActivation {get{return !preview;}}
 protected override CreateParams CreateParams {get{var p=base.CreateParams;if(!preview){p.ExStyle|=0x80;p.ExStyle&=~0x40000;}return p;}}
 public SideDock(string directory,bool isPreview){
  root=directory;preview=isPreview;source=new SideDockData(root);widgets=new WidgetBackend(root);
  Text=preview?"PrismGlass SideDock · 验证":"PrismGlass SideDock";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=preview;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;
  screen=Screen.PrimaryScreen.Bounds;Place();BackColor=Color.FromArgb(18,34,46);ApplyRegion();view.Dock=DockStyle.Fill;Controls.Add(view);
  Shown+=async(s,e)=>{try{
   var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,preview?"side-preview-runtime":"runtime-data"));
   await view.EnsureCoreWebView2Async(environment);
   SyncPixelScale();view.CoreWebView2.Settings.IsZoomControlEnabled=false;
   view.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;view.CoreWebView2.Settings.AreDevToolsEnabled=false;view.CoreWebView2.Settings.IsStatusBarEnabled=false;
   view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-side.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
   view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-media.local",Path.GetFullPath(Path.Combine(root,"..","music-data")),CoreWebView2HostResourceAccessKind.DenyCors);
   view.CoreWebView2.NavigationStarting+=(sender,a)=>{if(!a.Uri.StartsWith("https://prism-side.local/",StringComparison.Ordinal))a.Cancel=true;};
   view.CoreWebView2.NewWindowRequested+=(sender,a)=>a.Handled=true;
   view.CoreWebView2.WebMessageReceived+=(sender,a)=>{if(a.Source.StartsWith("https://prism-side.local/",StringComparison.Ordinal))Receive(a.TryGetWebMessageAsString());};
   view.CoreWebView2.ProcessFailed+=(sender,a)=>Fail("WebView2: "+a.ProcessFailedKind);
   view.Source=new Uri("https://prism-side.local/sidedock.html");timer.Interval=1000;timer.Tick+=(sender,a)=>Tick();timer.Start();
  }catch(Exception ex){Fail(ex.ToString());}};
  Deactivate+=DismissOnDeactivate;
  FormClosed+=(s,e)=>{closed=true;timer.Stop();timer.Dispose();view.Dispose();};
 }
 void Fail(string text){try{File.WriteAllText(Path.Combine(root,"side-error.txt"),text);}catch{}Close();}
 void DismissOnDeactivate(object sender,EventArgs e){Send(new{type="dismiss"});}
 void SyncPixelScale(){
  // Native bounds, clipping and glass geometry use physical pixels. Cancel WebView's DPI zoom.
  if(view.CoreWebView2==null)return;
  uint dpi=GetDpiForWindow(Handle);double zoom=96.0/(dpi==0?96:dpi);
  if(Math.Abs(view.ZoomFactor-zoom)>.001)view.ZoomFactor=zoom;
 }
 void Place(){
  scale=Math.Min(1.25f,Math.Min((screen.Height-240)/680f,(screen.Width-48)/852f));scale=Math.Max(.5f,scale);
  Bounds=new Rectangle(screen.Left+(preview?80:16),screen.Top+Math.Max(72,(screen.Height-(int)(680*scale))/2),(int)Math.Ceiling(852*scale),(int)Math.Ceiling(680*scale));

 }
 public void Open(string mode,Point at){widgetRead=DateTime.MinValue;lastWidgets="";pendingMode=mode;if(ready){Send(new{type="open",mode=mode});pendingMode=null;}}
 public void Sync(string nextTheme,Rectangle bounds,IntPtr firstSkin){Sync(nextTheme,bounds,firstSkin,false);}
 public void Sync(string nextTheme,Rectangle bounds,IntPtr firstSkin,bool paused){
  automaticPause=paused;
  if(closed)return;
  SyncPixelScale();
  if(bounds!=screen){screen=bounds;Place();ApplyRegion();layout="";}
  if(theme!=nextTheme){theme=nextTheme;layout="";}
  PublishLayout();PublishVisibility(false);
  // Sit directly above desktop widgets, preserving the z-order of ordinary application windows.
  if(!preview&&firstSkin!=IntPtr.Zero&&GetForegroundWindow()!=Handle){var previous=GetWindow(firstSkin,3);if(previous!=Handle)SetWindowPos(Handle,previous,0,0,0,0,0x13);}
 }
 void PublishLayout(){
  if(!ready)return;
  string stamp=File.GetLastWriteTimeUtc(Path.Combine(root,"wallpaper.png")).Ticks.ToString();
  string key=theme+Bounds.ToString()+screen.ToString()+stamp;
  if(layout==key)return;layout=key;
  Send(new {type="layout",theme=theme,scale=scale,x=Left-screen.Left,y=Top-screen.Top,width=screen.Width,height=screen.Height,viewWidth=Width,viewHeight=Height,wallpaper=stamp,preview=preview});
 }
 void Send(object value){if(ready&&!closed&&view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(value));}
 void PublishVisibility(bool force){
  if(!ready||closed)return;var now=DateTime.UtcNow;if(!force&&(now-visibilityRead).TotalSeconds<1)return;visibilityRead=now;
  try{
   var file=Path.Combine(root,"sidebar-visibility.json");var stamp=File.Exists(file)?File.GetLastWriteTimeUtc(file):DateTime.MinValue;
   if(!force&&stamp==visibilityStamp&&visibilityState.Length>0)return;
   var hidden=new List<string>();var allowed=new[]{"network","Weather","Music","Focus","Folders","System","Tools","Life"};
   if(File.Exists(file)){
    if(new FileInfo(file).Length>65536)return;
    var data=json.DeserializeObject(File.ReadAllText(file)) as Dictionary<string,object>;object raw;
    if(data!=null&&data.TryGetValue("hiddenLeftCards",out raw)&&raw is System.Collections.IEnumerable&&!(raw is string))foreach(var item in (System.Collections.IEnumerable)raw){var name=item as string;if(name==null)continue;foreach(var key in allowed)if(String.Equals(name,key,StringComparison.OrdinalIgnoreCase)&&!hidden.Contains(key)){hidden.Add(key);break;}}
   }
   hidden.Sort(StringComparer.Ordinal);string current=json.Serialize(hidden);visibilityStamp=stamp;
   if(force||current!=visibilityState){visibilityState=current;Send(new{type="visibility",hiddenLeftCards=hidden.ToArray()});}
  }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
 }
 void Receive(string text){
  try{
   if(text=="ready"){ready=true;layout="";lastWidgets="";widgetRead=DateTime.MinValue;theme=ReadTheme();PublishLayout();PublishVisibility(true);Tick();if(pendingMode!=null){Send(new{type="open",mode=pendingMode});pendingMode=null;}}
   else if(text=="painted")painted=true;
   else if(text.StartsWith("shape:")){var shape=json.Deserialize<float[]>(text.Substring(6));if(shape.Length<2)return;panelWidth=Math.Max(0,Math.Min(736,shape[0]));panelHeight=Math.Max(0,Math.Min(660,shape[1]));networkShape=shape.Length>2?Math.Max(0,Math.Min(1,shape[2])):0;bridgeY=shape.Length>3?Math.Max(12,Math.Min(620,shape[3])):40;panelTop=shape.Length>4?Math.Max(4,Math.Min(668-panelHeight,shape[4])):4;railHeight=shape.Length>5?Math.Max(0,Math.Min(668,shape[5])):668;bool wasOpened=opened;opened=panelWidth>1;if(opened&&!wasOpened){widgetRead=DateTime.MinValue;lastWidgets="";Tick();}ApplyRegion();}
   else if(text.StartsWith("status:")){File.WriteAllText(Path.Combine(root,"side-render-state.json"),text.Substring(7));}
   else if(text.StartsWith("error:")){File.WriteAllText(Path.Combine(root,"side-error.txt"),text.Substring(6));}
   else if(text=="life-open-focus"){Send(new{type="open",mode="focus"});}
   else if(text=="action:gallery-add"){using(var picker=new OpenFileDialog()){picker.Filter="图片|*.png;*.jpg;*.jpeg;*.webp";if(picker.ShowDialog(this)==DialogResult.OK){var folder=Path.Combine(root,"collection");Directory.CreateDirectory(folder);var name=Guid.NewGuid().ToString("N")+Path.GetExtension(picker.FileName).ToLowerInvariant();File.Copy(picker.FileName,Path.Combine(folder,name));Send(new{type="gallery",url="https://prism-side.local/collection/"+name});}}}
   else if(text.StartsWith("action:")){widgets.Action(text.Substring(7));File.WriteAllText(Path.Combine(root,"last-widget-action.txt"),DateTime.UtcNow.ToString("o")+" "+text.Substring(7));Send(widgets.Read());}
   else if(text=="settings-power")OpenSettings("ms-settings:powersleep");
   else if(text=="settings-network")OpenSettings("ms-settings:network-status");
   else if(text=="close-preview"&&preview)Close();
  }catch(Exception ex){File.WriteAllText(Path.Combine(root,"side-error.txt"),ex.ToString());Send(new{type="action-error",message="未能打开，请重试。"});}
 }
 string ReadTheme(){try{return File.ReadAllText(Path.Combine(root,"theme.txt")).Trim()=="Astro"?"Astro":"Sky";}catch{return "Sky";}}
 static void OpenSettings(string uri){Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});}
 async void Tick(){
  if(!ready||closed)return;
  var next=ReadTheme();if(theme!=next){theme=next;layout="";}PublishLayout();PublishVisibility(false);
  bool paused=automaticPause||(File.Exists(Path.Combine(root,"paused.txt"))&&File.ReadAllText(Path.Combine(root,"paused.txt")).Trim()=="true");
  if(paused!=motionPaused){motionPaused=paused;Send(new{type="motion",paused=paused});}
  var now=DateTime.UtcNow;
  if(painted&&(now-heartbeat).TotalSeconds>=2){heartbeat=now;File.WriteAllText(Path.Combine(root,"side-heartbeat.txt"),now.ToString("o"));}
  if((now-widgetRead).TotalSeconds>=(paused?15:opened?1:5)){widgetRead=now;var data=json.Serialize(widgets.Read());if(data!=lastWidgets){lastWidgets=data;view.CoreWebView2.PostWebMessageAsJson(data);}}
  if((now-dataRead).TotalSeconds<(paused?30:5)||reading)return;dataRead=now;reading=true;
  try{var data=await Task.Run(()=>source.Read());if(!closed){Send(data);if(painted)File.WriteAllText(Path.Combine(root,"side-heartbeat.txt"),DateTime.UtcNow.ToString("o"));}}
  catch(Exception ex){if(!closed)File.WriteAllText(Path.Combine(root,"side-error.txt"),ex.Message);}
  finally{reading=false;}
 }
 static void Round(GraphicsPath p,float x,float y,float w,float h,float radius){
  float d=Math.Min(radius*2,Math.Min(w,h));if(w<=0||h<=0)return;
  p.StartFigure();p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();
 }
 void ApplyRegion(){
  using(var path=new GraphicsPath()){
   if(railHeight>0)Round(path,4*scale,4*scale,88*scale,railHeight*scale,28*scale);
   if(panelWidth>1){Round(path,108*scale,panelTop*scale,panelWidth*scale,panelHeight*scale,28*scale);}
   var previous=Region;Region=new Region(path);if(previous!=null)previous.Dispose();
  }
 }
}
