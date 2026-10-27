using System;
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

internal sealed class PixelTestForm : Form {
 readonly WebView2 view=new WebView2();readonly WebViewPixelScale pixels;readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly string content,profile;string page;float[][] outline;int passed;
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr h);
 protected override bool ShowWithoutActivation{get{return true;}}
 public PixelTestForm(string source,string fixture){content=source;profile=fixture;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;Opacity=0;Bounds=new Rectangle(Screen.PrimaryScreen.Bounds.Left+24,Screen.PrimaryScreen.Bounds.Top+2,625,775);view.Dock=DockStyle.Fill;Controls.Add(view);pixels=new WebViewPixelScale(view,Publish);Shown+=Run;}
 void Check(bool result,string label){if(!result)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
 async Task<Dictionary<string,object>> Metrics(){string text=await view.CoreWebView2.ExecuteScriptAsync("({dpr:devicePixelRatio,width:innerWidth,height:innerHeight})");return json.Deserialize<Dictionary<string,object>>(text);}
 double Number(Dictionary<string,object> map,string key){return Convert.ToDouble(map[key]);}
 async Task Navigate(string file){var done=new TaskCompletionSource<bool>();EventHandler<CoreWebView2NavigationCompletedEventArgs> handler=null;handler=(s,e)=>{view.CoreWebView2.NavigationCompleted-=handler;if(e.IsSuccess)done.SetResult(true);else done.SetException(new IOException(e.WebErrorStatus.ToString()));};view.CoreWebView2.NavigationCompleted+=handler;view.Source=new Uri("https://pixel-test.local/"+file);await done.Task;}
 void Publish(){if(view.CoreWebView2==null||page==null)return;var screen=Screen.FromRectangle(Bounds).Bounds;object message=page=="island"?(object)new{type="layout",theme="Sky",paused=true,scale=1.0,x=Left-screen.Left,y=Top-screen.Top,width=screen.Width,height=screen.Height,wallpaper="fixture"}:new{type="layout",theme="Sky",paused=true,scale=1.25,width=Width,height=Height,originX=Left-screen.Left,originY=Top-screen.Top,sceneWidth=screen.Width,sceneHeight=screen.Height,wallpaper="fixture"};view.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));}
 async void Run(object sender,EventArgs args){
  try{
   var env=await CoreWebView2Environment.CreateAsync(null,profile);await view.EnsureCoreWebView2Async(env);view.CoreWebView2.SetVirtualHostNameToFolderMapping("pixel-test.local",content,CoreWebView2HostResourceAccessKind.DenyCors);
   view.CoreWebView2.WebMessageReceived+=(s,e)=>{string message=e.TryGetWebMessageAsString();if(page=="island"&&message.StartsWith("outline:")){var shapes=json.Deserialize<float[][][]>(message.Substring(8));if(shapes.Length==1)outline=shapes[0];}};
   foreach(string current in new[]{"island","shelf"}){
    page=null;await Navigate(current+".html");page=current;
    // Moving the same transparent window exercises monitor-scale transitions.
    var screens=new List<Screen>(Screen.AllScreens);if(screens.Count>1)screens.Add(screens[0]);
    foreach(var screen in screens){
     var work=screen.WorkingArea;Bounds=current=="island"?new Rectangle(work.Left+24,work.Top+2,work.Width-48,410):new Rectangle(work.Right-645,work.Top+100,625,775);await Task.Delay(500);var before=await Metrics();
     Console.WriteLine("METRIC "+current+" "+screen.DeviceName+" nativeDpi="+GetDpiForWindow(Handle)+" before="+json.Serialize(before));
     await pixels.SyncAsync(true);await Task.Delay(250);await pixels.SyncAsync(true);Publish();await Task.Delay(500);var after=await Metrics();
     Console.WriteLine("METRIC after zoom="+view.ZoomFactor+" raster="+pixels.RasterizationScale+" "+json.Serialize(after));
     Check(Math.Abs(Number(after,"dpr")-1)<.005,current+" CSS pixels match physical pixels on "+screen.DeviceName);
     Check(Math.Abs(Number(after,"width")-ClientSize.Width)<=1,current+" CSS viewport fills native client on "+screen.DeviceName);
     double stableZoom=view.ZoomFactor;bool stable=true;for(int sample=0;sample<3;sample++){await Task.Delay(80);stable&=!await pixels.SyncAsync(true)&&Math.Abs(view.ZoomFactor-stableZoom)<.0001;}Check(stable,current+" calibration settles without oscillation on "+screen.DeviceName);
     if(current=="island"){
      Check(outline!=null&&outline.Length>=4,"island emits real rendered outline on "+screen.DeviceName);
      float maxX=0;var points=new PointF[outline.Length];for(int i=0;i<outline.Length;i++){maxX=Math.Max(maxX,outline[i][0]);points[i]=new PointF(outline[i][0],outline[i][1]);}
      using(var path=new GraphicsPath()){path.AddPolygon(points);var old=Region;Region=new Region(path);if(old!=null)old.Dispose();}
      Check(maxX>ClientSize.Width-12,"island native clipping reaches the far edge on "+screen.DeviceName);
      var right=json.Deserialize<Dictionary<string,double>>(await view.CoreWebView2.ExecuteScriptAsync("(()=>{const r=document.getElementById('bar-right').getBoundingClientRect();return {x:r.right-8,y:r.top+r.height/2}})()"));
      Check(Region.IsVisible(new PointF((float)right["x"],(float)right["y"])),"island right-side controls are inside native region on "+screen.DeviceName);
     }else{
      var old=Region;Region=new Region(new RectangleF(417*1.25f,14,82*1.25f,720));if(old!=null)old.Dispose();
      var button=json.Deserialize<Dictionary<string,double>>(await view.CoreWebView2.ExecuteScriptAsync("(()=>{const r=document.querySelector('#triggers [data-mode]').getBoundingClientRect();return {left:r.left,right:r.right,x:r.left+r.width/2,y:r.top+r.height/2}})()"));
      Check(button["right"]<=ClientSize.Width&&Region.IsVisible(new PointF((float)button["x"],(float)button["y"])),"shelf trigger is visible inside native clipping on "+screen.DeviceName);
     }
    }
   }
   Console.WriteLine("Completed "+passed+" real WebView pixel/region checks without activating a window.");Environment.ExitCode=0;
  }catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}finally{view.Dispose();Close();}
 }
}
internal static class TestWebViewPixels {
 [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [STAThread]static int Main(string[] args){SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new PixelTestForm(Path.GetFullPath(args[0]),Path.GetFullPath(args[1])));return Environment.ExitCode;}
}
