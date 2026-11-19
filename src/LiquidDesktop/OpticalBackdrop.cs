using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Window textures stay in memory; the existing WebGL renderer supplies the lens and interaction.
internal sealed class OpticalBackdrop : IDisposable {
 sealed class Layer {internal LiveGlassBackdrop.Source window;internal bool dock;}
 sealed class Source : IDisposable {
  internal LiveGlassBackdrop.Source window;internal SharedWindowCapture.Subscription capture;internal CoreWebView2SharedBuffer buffer;internal string id;internal int width,height;internal long serial,frame=-1,notBefore;internal bool awaiting,copying,disposed;internal DateTime sent;
  public void Dispose(){disposed=true;if(capture!=null)capture.Dispose();capture=null;if(!copying)ReleaseBuffer();}
  internal void ReleaseBuffer(){if(buffer!=null)buffer.Dispose();buffer=null;}
 }
 static Task<bool> permission;
 readonly Func<Control> current;readonly WebView2 view;readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=8};readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly Dictionary<long,Source> sources=new Dictionary<long,Source>();
 Control owner;bool connected,disposed,busy,active;long serial;string layout="";DateTime enumerated,retry;public long Version {get;private set;}
 IntPtr overlay;Rectangle overlayBounds;
 readonly SharedWindowCapture.StageTiming bufferCopyTiming=new SharedWindowCapture.StageTiming();
 internal object Diagnostics {get{return new{bufferCopy=bufferCopyTiming.Snapshot()};}}
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 internal string Error="";
 public OpticalBackdrop(Func<Control> owner,WebView2 browser){current=owner;view=browser;timer.Tick+=Tick;view.CoreWebView2InitializationCompleted+=(s,e)=>{if(e.IsSuccess)Connect();};view.VisibleChanged+=(s,e)=>Refresh();view.Disposed+=(s,e)=>Dispose();Connect();}
 bool Trusted(){Uri page;return view.CoreWebView2!=null&&Uri.TryCreate(view.CoreWebView2.Source,UriKind.Absolute,out page)&&page.Scheme=="https"&&(page.Host=="desktop.local"||page.Host.StartsWith("prism",StringComparison.Ordinal)&&page.Host.EndsWith(".local",StringComparison.Ordinal));}
 void Connect(){if(connected||view.CoreWebView2==null)return;connected=true;view.CoreWebView2.NavigationStarting+=(s,e)=>Clear();view.CoreWebView2.NavigationCompleted+=(s,e)=>{Refresh();};view.CoreWebView2.WebMessageReceived+=Message;Refresh();}
 void Message(object sender,CoreWebView2WebMessageReceivedEventArgs e){
  if(!Trusted()||e.Source!=view.CoreWebView2.Source)return;string message;try{message=e.TryGetWebMessageAsString();}catch(ArgumentException){return;}if(String.IsNullOrEmpty(message))return;
  if(message=="optical-ready"){layout="";Refresh();return;}
  if(message=="optical-reset"){Clear();Refresh();return;}
  long acknowledged;if(message.StartsWith("optical-ack:",StringComparison.Ordinal)&&Int64.TryParse(message.Substring(12),out acknowledged))foreach(var source in sources.Values)if(source.serial==acknowledged)source.awaiting=false;
 }
 bool Enabled(){return !disposed&&LiveGlassBackdrop.Enabled&&!view.IsDisposed&&view.Visible&&owner!=null&&!owner.IsDisposed&&owner.IsHandleCreated&&owner.Visible&&Trusted();}
 public void Refresh(){if(disposed)return;var next=current();if(owner!=next){Clear();owner=next;}if(Enabled()){timer.Start();}else{timer.Stop();Clear();Error="";}}
 // The Dock plate displays this renderer at a different Z level. Its samples must
 // stay assigned to the Dock instead of changing the widget host's background.
 internal void SetOverlay(IntPtr window,Rectangle bounds){if(overlay==window&&overlayBounds==bounds)return;overlay=window;overlayBounds=bounds;enumerated=DateTime.MinValue;}
 void Send(object value){if(!view.IsDisposed&&Trusted())view.CoreWebView2.PostWebMessageAsJson(json.Serialize(value));}
 void Clear(){foreach(var source in sources.Values)source.Dispose();sources.Clear();layout="";enumerated=DateTime.MinValue;if(active){active=false;Version++;try{Send(new{type="optical-layout",active=false,sources=new object[0]});}catch(InvalidOperationException){}catch(COMException){}}}
 async void Tick(object sender,EventArgs e){
  if(busy||disposed)return;if(owner!=current()){Refresh();return;}if(!Enabled()){Refresh();return;}var form=owner as Form;if(form!=null&&form.Opacity==0){Clear();return;}if(DateTime.UtcNow<retry)return;busy=true;
  try{
   if(permission==null)permission=WindowCapture.Borderless();if(!await permission){timer.Stop();Clear();Error="Windows 未允许无边框窗口捕获，实时折射未启用。";return;}if(!Enabled())return;
   var area=owner.RectangleToScreen(owner.ClientRectangle);if(area.Width<1||area.Height<1||(long)area.Width*area.Height>16777216){Clear();return;}
   var now=DateTime.UtcNow;
   if((now-enumerated).TotalMilliseconds>=50){
    enumerated=now;long pixels=0;var visible=new List<LiveGlassBackdrop.Source>();var layers=new List<Layer>();var selected=new HashSet<long>();
    var candidates=new List<Layer>();
    foreach(var window in LiveGlassBackdrop.Sources(owner.Handle,area)){
     var relative=window.visible;relative.Offset(-area.X,-area.Y);if(owner.Region!=null&&!owner.Region.IsVisible(relative))continue;
     candidates.Add(new Layer{window=window});
    }
    var overlayClip=Rectangle.Intersect(area,overlayBounds);
    if(overlay!=IntPtr.Zero&&IsWindowVisible(overlay)&&overlayClip.Width>0&&overlayClip.Height>0){
     var foreground=new List<Layer>();foreach(var window in LiveGlassBackdrop.Sources(overlay,overlayClip))foreground.Add(new Layer{window=window,dock=true});
     // Layout is front to back; the compositor reverses it. Existing widget
     // sources retain their budget before optional raised-Dock captures.
     candidates.AddRange(foreground);
    }
    foreach(var layer in candidates){var window=layer.window;long handle=window.handle.ToInt64();
     if(!selected.Contains(handle)){long count=(long)window.bounds.Width*window.bounds.Height;if(pixels+count>16777216||visible.Count==6)continue;pixels+=count;selected.Add(handle);visible.Add(window);}
     layers.Add(layer);
    }
    layers=layers.Where(l=>l.dock).Concat(layers.Where(l=>!l.dock)).ToList();
    var handles=new HashSet<long>(visible.Select(w=>w.handle.ToInt64()));foreach(var key in sources.Keys.Where(k=>!handles.Contains(k)).ToArray()){sources[key].Dispose();sources.Remove(key);}
    foreach(var window in visible){long key=window.handle.ToInt64();Source source;if(sources.TryGetValue(key,out source)&&source.window.pid!=window.pid){source.Dispose();sources.Remove(key);source=null;}
     if(source==null){source=new Source{window=window,id=key+"-"+window.pid,notBefore=window.wallpaper?(long)(System.Diagnostics.Stopwatch.GetTimestamp()*(10000000.0/System.Diagnostics.Stopwatch.Frequency)):0};source.capture=SharedWindowCapture.Acquire(window.handle,window.pid);sources.Add(key,source);}source.window=window;
    }
    string nextLayout=json.Serialize(new{type="optical-layout",active=true,width=area.Width,height=area.Height,sources=layers.Select(l=>new{id=sources[l.window.handle.ToInt64()].id,x=l.window.visible.X-area.X,y=l.window.visible.Y-area.Y,w=l.window.visible.Width,h=l.window.visible.Height,target=l.dock?"Dock":null}).ToArray()});
    if(nextLayout!=layout){layout=nextLayout;active=true;Version++;view.CoreWebView2.PostWebMessageAsJson(nextLayout);}
   }
   foreach(var source in sources.Values.ToArray()){
    if(source.window.wallpaper&&LiveGlassBackdrop.HasVisibleDesktopIcons(source.window.handle)){Clear();return;}
    if(source.capture==null)continue;if(!source.capture.Valid){if(!String.IsNullOrEmpty(source.capture.Error))throw new InvalidOperationException(source.capture.Error);layout="";enumerated=DateTime.MinValue;continue;}
    if(source.awaiting){if((now-source.sent).TotalSeconds>2)throw new TimeoutException("玻璃渲染器未及时接收画面。");continue;}
    using(var pixels=source.capture.Latest(source.frame,source.notBefore)){
     Source currentSource;if(pixels==null||!Enabled()||!sources.TryGetValue(source.window.handle.ToInt64(),out currentSource)||source!=currentSource)continue;
     int width=pixels.Width,height=pixels.Height;bool created=source.buffer==null||source.width!=width||source.height!=height;
     if(created){if(source.buffer!=null)source.buffer.Dispose();source.buffer=view.CoreWebView2.Environment.CreateSharedBuffer((ulong)width*(ulong)height*4);source.width=width;source.height=height;}
     if((ulong)width*(ulong)height*4>source.buffer.Size)throw new InvalidOperationException("玻璃画面缓冲区大小不匹配。");
     var target=source.buffer.Buffer;source.copying=true;
     try{await Task.Run(()=>{long started=System.Diagnostics.Stopwatch.GetTimestamp();pixels.CopyTo(target);bufferCopyTiming.Record(started);});}
     finally{source.copying=false;if(source.disposed)source.ReleaseBuffer();}
     if(source.disposed||!Enabled())continue;
     if(source.window.wallpaper&&LiveGlassBackdrop.HasVisibleDesktopIcons(source.window.handle)){Clear();return;}
     source.frame=pixels.Serial;
     source.serial=++serial;source.awaiting=true;source.sent=DateTime.UtcNow;var data=new{type="optical-frame",id=source.id,serial=source.serial,width=width,height=height};
     if(created)view.CoreWebView2.PostSharedBufferToScript(source.buffer,CoreWebView2SharedBufferAccess.ReadOnly,json.Serialize(data));else Send(data);Version++;
    }
   }
   Error="";
  }catch(Exception error){Clear();retry=DateTime.UtcNow.AddSeconds(3);Error="实时折射暂不可用："+error.Message;}
  finally{busy=false;}
 }
 public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Dispose();Clear();}
}
