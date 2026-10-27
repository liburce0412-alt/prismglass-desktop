using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class TopIsland : Form {
 readonly string root;readonly bool preview;readonly WebView2 view=new WebView2();readonly WebViewPixelScale pixels;readonly WidgetBackend widgets;readonly SharedDesktopData shared;readonly string instance;readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly Timer timer=new Timer();
 bool ready,painted,closed,dragging,paused,outlineReady,readyNotified;string theme="Sky",layout="",mode="bar",widgetState="",outlineKey="";Rectangle screen;float scale=1;int ticks,regionUpdates;object pendingPreferences;
 public event Action Ready;public event Action<string> Failed;
 public bool IsPainted {get{return painted;}}
 public Rectangle OcclusionBounds {get{int width=mode=="bar"?Width-8:(int)Math.Min(Width-8,272*scale);return new Rectangle(Left+(Width-width)/2,Top+12,width,(int)((mode=="bar"?42:48)*scale));}}
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr h,uint command);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 protected override bool ShowWithoutActivation{get{return !preview;}}
 protected override CreateParams CreateParams{get{var p=base.CreateParams;if(!preview){p.ExStyle|=0x80;p.ExStyle&=~0x40000;}return p;}}
 public TopIsland(string directory,bool isPreview):this(directory,isPreview,null,"primary"){}
 public TopIsland(string directory,bool isPreview,SharedDesktopData data,string screenInstance){
  pixels=new WebViewPixelScale(view,()=>{Place();outlineKey="";Publish();});
  root=directory;preview=isPreview;shared=data;instance=screenInstance;widgets=shared==null?new WidgetBackend(root):shared.Widgets;Text=preview?"PrismGlass TopIsland · 验证":"PrismGlass TopIsland · "+instance;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=preview;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;BackColor=Color.FromArgb(35,44,57);screen=Screen.PrimaryScreen.Bounds;Place();view.Dock=DockStyle.Fill;Controls.Add(view);SetInitialShape();
  Shown+=async(sender,args)=>{try{
   var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,preview?"island-preview-runtime":"runtime-data"));await view.EnsureCoreWebView2Async(environment);SyncPixelScale();
   var settings=view.CoreWebView2.Settings;settings.IsZoomControlEnabled=false;settings.AreDefaultContextMenusEnabled=false;settings.AreDevToolsEnabled=false;settings.IsStatusBarEnabled=false;
   view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-island.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
   view.CoreWebView2.NavigationStarting+=(s,e)=>{if(!String.Equals(e.Uri,"https://prism-island.local/island.html",StringComparison.Ordinal))e.Cancel=true;};
   view.CoreWebView2.NewWindowRequested+=(s,e)=>e.Handled=true;
   view.CoreWebView2.WebMessageReceived+=(s,e)=>{if(String.Equals(e.Source,"https://prism-island.local/island.html",StringComparison.Ordinal))Receive(e.TryGetWebMessageAsString());};
   view.CoreWebView2.ProcessFailed+=(s,e)=>Fail("WebView2: "+e.ProcessFailedKind);view.Source=new Uri("https://prism-island.local/island.html");timer.Interval=1000;timer.Tick+=(s,e)=>Tick();timer.Start();
  }catch(Exception ex){Fail(ex.ToString());}};
  Deactivate+=(s,e)=>{if(!dragging)Send(new{type="dismiss"});};
  FormClosed+=(s,e)=>{closed=true;timer.Stop();timer.Dispose();view.Dispose();};
 }
 void Place(){scale=Math.Max(.8f,Math.Min(1.25f,(float)pixels.RasterizationScale));Bounds=new Rectangle(screen.Left+24,screen.Top+2,Math.Max(320,screen.Width-48),Math.Min(screen.Height-90,(int)Math.Ceiling(328*scale)));layout="";}
 void SyncPixelScale(){pixels.Sync();}
 public void Sync(string nextTheme,Rectangle bounds,IntPtr anchor,bool motionPaused){
  if(closed)return;float nextScale=Math.Max(.8f,Math.Min(1.25f,(float)pixels.RasterizationScale));if(bounds!=screen||Math.Abs(nextScale-scale)>.01){screen=bounds;Place();outlineKey="";}SyncPixelScale();
  if(theme!=nextTheme||paused!=motionPaused){theme=nextTheme=="Astro"?"Astro":"Sky";paused=motionPaused;layout="";}Publish();
  if(!preview)LiquidDesktop.PlaceComponent(this,anchor);
 }
 void Publish(){if(!ready)return;string stamp=File.GetLastWriteTimeUtc(Path.Combine(root,"wallpaper.png")).Ticks.ToString();string key=theme+Bounds.ToString()+screen.ToString()+paused+stamp;if(key==layout)return;layout=key;var scene=Screen.FromRectangle(screen).Bounds;Send(new{type="layout",theme=theme,paused=paused,scale=scale,x=Left-scene.Left,y=Top-scene.Top,width=scene.Width,height=scene.Height,wallpaper=stamp});}
 void Send(object value){if(ready&&!closed&&view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(value));}
 public void ApplyPreferences(object snapshot){pendingPreferences=snapshot;Send(new{type="preferences",snapshot=snapshot});}
 public void DismissTransient(){if(!dragging)Send(new{type="dismiss"});}
 string Diagnostic(string name){return Path.Combine(root,instance=="primary"?name:Path.GetFileNameWithoutExtension(name)+"-"+instance+Path.GetExtension(name));}
 void Fail(string error){if(closed)return;try{File.WriteAllText(Path.Combine(root,preview?"island-preview-error.txt":"island-error.txt"),error);}catch{}var handler=Failed;if(handler!=null)handler(error);Close();}
 void Receive(string text){try{
  if(text=="ready"){ready=true;Place();Publish();if(pendingPreferences!=null)ApplyPreferences(pendingPreferences);Tick(true);}
  else if(text=="painted"&&!painted){painted=true;NotifyReady();}
  else if(text.StartsWith("outline:",StringComparison.Ordinal)){var outlines=json.Deserialize<float[][][]>(text.Substring(8));if(outlines.Length==1&&outlines[0].Length>=4){using(var path=new GraphicsPath()){var points=new PointF[outlines[0].Length];for(int i=0;i<points.Length;i++){if(outlines[0][i].Length!=2)return;float x=outlines[0][i][0],y=outlines[0][i][1];if(float.IsNaN(x)||float.IsNaN(y)||float.IsInfinity(x)||float.IsInfinity(y)||Math.Abs(x)>Width+100||Math.Abs(y)>Height+100)return;points[i]=new PointF((float)(Math.Round(x*4)/4),(float)(Math.Round(y*4)/4));}string key=json.Serialize(points);if(key!=outlineKey){path.AddPolygon(points);ReplaceRegion(new Region(path));outlineKey=key;regionUpdates++;}outlineReady=true;NotifyReady();}}}
  else if(text=="drag:true")dragging=true;
  else if(text=="drag:false")dragging=false;
  else if(text.StartsWith("mode:",StringComparison.Ordinal)){mode=text.Substring(5);if(!preview)File.WriteAllText(Diagnostic("island-state.json"),json.Serialize(new{mode=mode,updated=DateTime.UtcNow.ToString("o")}));}
  else if(text.StartsWith("action:",StringComparison.Ordinal)){if(preview){Send(new{type="feedback",message="预览模式：未执行系统操作"});return;}Action(text.Substring(7));Tick(true);}
  else if(text.StartsWith("error:",StringComparison.Ordinal))Fail(text.Substring(6));
 }catch(Exception ex){Send(new{type="feedback",message="操作未完成，请重试"});try{File.WriteAllText(Path.Combine(root,preview?"island-preview-error.txt":"island-error.txt"),ex.ToString());}catch{}}}
 void NotifyReady(){if(!readyNotified&&painted&&outlineReady){readyNotified=true;var handler=Ready;if(handler!=null)handler();}}
 void SetInitialShape(){float x=4,y=12,w=Width-8,h=42*scale,d=h;using(var path=new GraphicsPath()){path.AddArc(x,y,d,d,180,90);path.AddArc(x+w-d,y,d,d,270,90);path.AddArc(x+w-d,y+h-d,d,d,0,90);path.AddArc(x,y+h-d,d,d,90,90);path.CloseFigure();ReplaceRegion(new Region(path));}}
 void ReplaceRegion(Region next){var old=Region;Region=next;if(old!=null)old.Dispose();}
 void Tick(){Tick(false);}
 void Tick(bool force){if(!ready||closed)return;if(force||ticks%(paused?15:2)==0){var data=shared==null?widgets.Read():shared.ReadWidgets();string snapshot=json.Serialize(data);if(force||snapshot!=widgetState){widgetState=snapshot;Send(data);}}if(ticks++%15==0){try{var drive=new DriveInfo("C");Send(new{type="status",disk="C: "+(drive.AvailableFreeSpace/1073741824.0).ToString("0.0")+" GB 可用"});}catch{}}if(!preview&&painted&&ticks%5==0)try{File.WriteAllText(Diagnostic("island-heartbeat.txt"),DateTime.UtcNow.ToString("o"));}catch{}}
 void Script(string name,string args){string file=Path.GetFullPath(Path.Combine(root,"..",name));if(!File.Exists(file))throw new FileNotFoundException(name);Process.Start(new ProcessStartInfo("pwsh.exe","-NoProfile -WindowStyle Hidden -File \""+file+"\" "+args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});}
 static void Launch(string path,string args){Process.Start(new ProcessStartInfo(path,args){UseShellExecute=true});}
 public async void OpenTray(){if(preview||closed)return;bool opened=await SystemTray.OpenAsync();Send(new{type="feedback",message=opened?"系统托盘已打开":"系统任务栏已显示，可点击隐藏图标箭头"});}
 void Action(string id){switch(id){
  case "theme":File.WriteAllText(Path.Combine(root,"theme.txt"),theme=="Astro"?"Sky":"Astro");break;
  case "motion":var motion=Path.Combine(root,"paused.txt");File.WriteAllText(motion,File.Exists(motion)&&File.ReadAllText(motion).Trim()=="true"?"false":"true");break;
  case "refresh":Script("RefreshGlass.ps1","");break;
  case "game":Script("DesktopMode.ps1","-Mode Game");break;
  case "settings":File.WriteAllText(Path.Combine(root,"workspace.request"),"settings");break;
  case "apps":File.WriteAllText(Path.Combine(root,"workspace.request"),"apps");break;
  case "windows-settings":Launch("ms-settings:","");break;
  case "bluetooth":Launch("ms-settings:bluetooth","");break;
  case "tasks":Launch("explorer.exe","shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}");break;
  case "tray":OpenTray();break;
  case "disk":Launch("explorer.exe","C:\\");break;
  case "calendar":File.WriteAllText(Path.Combine(root,"panel.request"),"calendar");break;
  case "media-prev":Media(0xB1);break;case "media-toggle":Media(0xB3);break;case "media-next":Media(0xB0);break;
  case "focus-toggle":case "focus-reset":case "focus-25":case "focus-5":widgets.Action(id);if(shared!=null)shared.Invalidate();break;
  default:throw new ArgumentException("Unknown island action");
 }}
 internal static void Media(byte code){keybd_event(code,0,0,UIntPtr.Zero);keybd_event(code,0,2,UIntPtr.Zero);}
}
