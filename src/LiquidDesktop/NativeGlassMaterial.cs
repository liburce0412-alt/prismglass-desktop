using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Native menus and DWM thumbnails keep their input behavior; only their background uses the shared shader.
internal static class NativeGlassMaterial {
 static string root;static MaterialHost host;
 public static void Register(string directory){
  root=directory;DockPreview.ConfigureMaterial=owner=>new Surface(owner,18);
  DockBackend.CreateMenu=CreateMenu;Application.ApplicationExit+=(s,e)=>{if(host!=null)host.Dispose();host=null;};
 }
 public static ContextMenuStrip CreateMenu(){var menu=new ContextMenuStrip{Renderer=new MenuRenderer(),BackColor=Color.FromArgb(24,36,60),ForeColor=Color.FromArgb(237,245,255)};new Surface(menu,9);return menu;}
 static MaterialHost Host(){if(host==null||host.IsDisposed){host=new MaterialHost(root);host.Show();}return host;}
 sealed class Surface {
  readonly Control owner;readonly Color originalColor;readonly int radius;readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=300};bool busy;string key="";DateTime retry;
  public Surface(Control control,int corner){owner=control;originalColor=owner.BackColor;radius=corner;owner.BackgroundImageLayout=ImageLayout.Stretch;owner.VisibleChanged+=Changed;owner.SizeChanged+=Changed;owner.LocationChanged+=Changed;owner.Disposed+=Disposed;timer.Tick+=Changed;if(owner.Visible)Changed(null,EventArgs.Empty);}
  void Clear(){key="";var image=owner.BackgroundImage;owner.BackgroundImage=null;if(image!=null)image.Dispose();if(host!=null)host.Release(owner);}
  async void Changed(object sender,EventArgs e){
   if(owner.IsDisposed)return;if(!owner.Visible){timer.Stop();Clear();return;}timer.Interval=LiveGlassBackdrop.Enabled?33:300;timer.Start();if(busy||DateTime.UtcNow<retry||owner.Width<2||owner.Height<2)return;busy=true;
   try{
    var renderer=Host();await renderer.Ready;if(owner.IsDisposed||!owner.Visible)return;
    var bounds=owner.RectangleToScreen(owner.ClientRectangle);string next=renderer.State(bounds,radius);
    if(key==next)return;owner.BackColor=originalColor;
    var image=await renderer.Snapshot(owner,bounds,radius);if(image==null)return;
    if(owner.IsDisposed||!owner.Visible||owner.RectangleToScreen(owner.ClientRectangle)!=bounds){image.Dispose();return;}
    {
     var old=owner.BackgroundImage;owner.BackgroundImage=image;if(old!=null)old.Dispose();key=next;owner.Invalidate();
     if(owner is ContextMenuStrip)using(var path=Round(owner.ClientRectangle,radius)){var previous=owner.Region;owner.Region=new Region(path);if(previous!=null)previous.Dispose();}
    }
   }catch{retry=DateTime.UtcNow.AddSeconds(5);if(!owner.IsDisposed)Clear();if(host!=null&&(host.Failed||host.Ready!=null&&host.Ready.IsFaulted)){host.Dispose();host=null;}}
   finally{busy=false;}
  }
  void Disposed(object sender,EventArgs e){timer.Stop();timer.Dispose();Clear();owner.VisibleChanged-=Changed;owner.SizeChanged-=Changed;owner.LocationChanged-=Changed;owner.Disposed-=Disposed;}
 }
 static GraphicsPath Round(Rectangle bounds,int radius){var path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));path.AddArc(0,0,d,d,180,90);path.AddArc(bounds.Width-d,0,d,d,270,90);path.AddArc(bounds.Width-d,bounds.Height-d,d,d,0,90);path.AddArc(0,bounds.Height-d,d,d,90,90);path.CloseFigure();return path;}
 sealed class MenuRenderer:ToolStripProfessionalRenderer {
  protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(e.ToolStrip.BackColor);if(e.ToolStrip.BackgroundImage!=null)e.Graphics.DrawImage(e.ToolStrip.BackgroundImage,e.ToolStrip.ClientRectangle);}
  protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
  protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){if(e.Item.Selected&&e.Item.Enabled)using(var brush=new SolidBrush(Color.FromArgb(45,190,221,255)))e.Graphics.FillRectangle(brush,new Rectangle(Point.Empty,e.Item.Size));}
  protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){TextRenderer.DrawText(e.Graphics,e.Text,e.TextFont,e.TextRectangle,e.Item.Enabled?Color.FromArgb(237,245,255):Color.FromArgb(174,191,211),e.TextFormat);}
  protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var pen=new Pen(Color.FromArgb(55,228,242,255)))e.Graphics.DrawLine(pen,8,e.Item.Height/2,e.Item.Width-8,e.Item.Height/2);}
  protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){}
 }
 sealed class MaterialHost:Form {
  const string Page="https://prism-native.local/native-glass.html";
  readonly string directory;readonly WebView2 view=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=Color.Transparent};readonly DesktopPreferences preferences;readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);readonly OpticalBackdrop optical;
  readonly TaskCompletionSource<bool> loaded=new TaskCompletionSource<bool>();TaskCompletionSource<bool> painted,wallpaperReady;string wallpaperVersion;Control active;RuntimePolicy runtime;int serial;bool paused;DateTime wallpaperStamp;WebViewPixelScale pixels;
  public Task Ready {get;private set;}
  public bool Failed {get;private set;}
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool SystemParametersInfo(uint action,uint parameter,out bool value,uint flags);
  protected override bool ShowWithoutActivation{get{return true;}}
  protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
  public MaterialHost(string folder){
   directory=folder;preferences=new DesktopPreferences(folder);FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-24000,-24000);ClientSize=new Size(400,240);AutoScaleMode=AutoScaleMode.None;Opacity=0;Controls.Add(view);
   WebViewVisibility.Track(view);pixels=new WebViewPixelScale(view,null);optical=LiveGlassBackdrop.Attach(()=>active,view);
   Shown+=(s,e)=>{runtime=new RuntimePolicy(Handle);Ready=Initialize();};
  }
  async Task Initialize(){
   await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(directory,"runtime-data")));
   var core=view.CoreWebView2;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.IsZoomControlEnabled=false;core.Settings.IsStatusBarEnabled=false;
   core.SetVirtualHostNameToFolderMapping("prism-native.local",directory,CoreWebView2HostResourceAccessKind.DenyCors);
   core.NavigationStarting+=(s,e)=>{if(e.Uri!=Page)e.Cancel=true;};core.NewWindowRequested+=(s,e)=>e.Handled=true;
   core.WebMessageReceived+=(s,e)=>{if(e.Source!=Page)return;string message=e.TryGetWebMessageAsString();if(message=="painted")loaded.TrySetResult(true);else if(message=="snapshot:"+serial&&painted!=null)painted.TrySetResult(true);else if(message=="wallpaper-loaded:"+wallpaperVersion&&wallpaperReady!=null)wallpaperReady.TrySetResult(true);else if(message.StartsWith("error:",StringComparison.Ordinal)){Failed=true;loaded.TrySetException(new InvalidOperationException(message));}};
   core.ProcessFailed+=(s,e)=>{Failed=true;loaded.TrySetException(new InvalidOperationException("Native glass renderer stopped"));if(painted!=null)painted.TrySetResult(false);};
   view.Source=new Uri(Page);if(await Task.WhenAny(loaded.Task,Task.Delay(7000))!=loaded.Task)throw new TimeoutException("Native glass renderer did not start");await loaded.Task;
  }
  public string State(Rectangle bounds,int radius){
   if(Failed)throw new InvalidOperationException("Native glass renderer stopped");
   preferences.Reload();runtime.ReadPower();bool animations=true;SystemParametersInfo(0x1042,0,out animations,0);paused=RuntimePolicy.Reasons(preferences.Current,runtime.Signals,false,false,animations).Length>0;
   LiveGlassBackdrop.Configure(directory,preferences.Current.liveGlassBackground,paused);
   return bounds+"|"+radius+"|"+preferences.Current.theme+"|"+preferences.Current.glassOpacity+"|"+preferences.Current.glassStyle+"|"+json.Serialize(preferences.Current.glassOverrides)+"|"+json.Serialize(preferences.Current.glassPresets)+"|"+preferences.Current.liveGlassBackground+"|"+paused+"|"+File.GetLastWriteTimeUtc(Path.Combine(directory,"wallpaper.png")).Ticks+"|"+(LiveGlassBackdrop.Enabled?optical.Version:0);
  }
  public async Task<Bitmap> Snapshot(Control owner,Rectangle bounds,int radius){
   if(!await gate.WaitAsync(0))return null;
   try{
    if(owner.IsDisposed||!owner.Visible||IsDisposed)return null;bool reset=active!=owner,resized=ClientSize!=bounds.Size;active=owner;view.Visible=true;view.CoreWebView2.Resume();ClientSize=bounds.Size;optical.Refresh();await pixels.SyncAsync(reset||resized);
    var screen=Screen.FromRectangle(bounds).Bounds;var stamp=File.GetLastWriteTimeUtc(Path.Combine(directory,"wallpaper.png"));
    if(wallpaperStamp!=stamp){wallpaperVersion=stamp.Ticks.ToString();wallpaperReady=new TaskCompletionSource<bool>();await view.CoreWebView2.ExecuteScriptAsync("receive({type:'wallpaper',version:'"+wallpaperVersion+"'})");if(await Task.WhenAny(wallpaperReady.Task,Task.Delay(2000))!=wallpaperReady.Task)return null;wallpaperStamp=stamp;}
    // Native regions already clip the edge; do not composite a second transparent inset onto a dark GDI background.
    var layout=new{type="layout",width=bounds.Width,height=bounds.Height,sceneWidth=screen.Width,sceneHeight=screen.Height,originX=bounds.Left-screen.Left,originY=bounds.Top-screen.Top,theme=preferences.Current.theme,paused=paused,cards=new[]{new{name="NativePopup",x=-2,y=-2,w=bounds.Width+4,h=bounds.Height+4,r=radius,fixedShape=true}}};
    int id=++serial;painted=new TaskCompletionSource<bool>();view.CoreWebView2.PostWebMessageAsJson(json.Serialize(new{type="snapshot",id=id,reset=reset,preferences=new{glassPresets=preferences.Current.glassPresets,glassOverrides=preferences.Current.glassOverrides,glassStyle=preferences.Current.glassStyle,glassOpacity=preferences.Current.glassOpacity,liveGlassBackground=preferences.Current.liveGlassBackground&&!paused},layout=layout}));
    if(await Task.WhenAny(painted.Task,Task.Delay(2000))!=painted.Task||!await painted.Task||IsDisposed||owner.IsDisposed||!owner.Visible||active!=owner)return null;
    using(var stream=new MemoryStream()){await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);stream.Position=0;using(var image=Image.FromStream(stream))return new Bitmap(image);}
   }finally{gate.Release();}
  }
  public void Release(Control owner){if(active!=owner)return;active=null;if(painted!=null)painted.TrySetResult(false);if(!view.IsDisposed){try{if(view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson("{\"type\":\"stop\"}");}catch(InvalidOperationException){}catch(COMException){}view.Visible=false;}}
  protected override void WndProc(ref Message message){if(runtime!=null)runtime.WindowMessage(ref message);base.WndProc(ref message);}
  protected override void Dispose(bool disposing){if(disposing){active=null;if(painted!=null)painted.TrySetResult(false);if(runtime!=null)runtime.Dispose();view.Dispose();}base.Dispose(disposing);}
 }
}
