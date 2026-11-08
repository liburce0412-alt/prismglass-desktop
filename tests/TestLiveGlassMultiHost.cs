using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static partial class TestLiveGlassBackdrop {
 sealed class MultiSurface {
  internal string name;internal GlassCaptureHost host;internal WebView2 browser;internal OpticalBackdrop backdrop;
  internal long startVersion,endVersion;internal int frameStart,frameEnd;internal string pixelStart,pixelEnd;
 }
 [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
 static readonly JavaScriptSerializer metricsJson=new JavaScriptSerializer();
 static double Percentile(List<double> values,double fraction){var sorted=values.OrderBy(n=>n).ToArray();return sorted.Length==0?0:sorted[(int)Math.Ceiling((sorted.Length-1)*fraction)];}
 static Task<string> MultiEval(MultiSurface surface,string script){return surface.browser.CoreWebView2.ExecuteScriptAsync(script);}
 static async Task MultiReady(MultiSurface surface,string expression){for(int n=0;n<80;n++){if(await MultiEval(surface,expression)=="true")return;await Task.Delay(75);}throw new Exception("Multi-host timeout: "+surface.name+"; "+surface.backdrop.Error+"; "+await MultiEval(surface,"window.stats?JSON.stringify(stats()):'no renderer'"));}
 static object CaptureCounts(){return SharedWindowCapture.Diagnostics;}
 static long Counter(object snapshot,string name){var values=metricsJson.Deserialize<Dictionary<string,object>>(metricsJson.Serialize(snapshot));object value;if(!values.TryGetValue(name,out value))throw new Exception("Missing shared capture metric: "+name);return Convert.ToInt64(value);}
 static async Task MultiHost(IntPtr source){
  var surfaces=new List<MultiSurface>();var area=Screen.PrimaryScreen.Bounds;
  var timers=new List<double>();var native=new List<double>();var browser=new List<double>();int posted=0,received=0;
  var clock=new Stopwatch();var heartbeat=new System.Windows.Forms.Timer{Interval=16};double previous=0;
  object before=null,started=null,after=null,stopped=null,idle=null;string errors="";double elapsed=0;
  try{
   LiveGlassBackdrop.Configure(root,false,false);SendMessage(source,0x8004,IntPtr.Zero,IntPtr.Zero);await Task.Delay(100);
   RECT sourceBounds;GetWindowRect(source,out sourceBounds);Check(sourceBounds.Rectangle==area,"dynamic fixture covers the actual monitor at native resolution");
   var layouts=new[]{area,new Rectangle(area.X+40,area.Y+16,area.Width-80,72),new Rectangle(area.X+20,area.Y+130,200,area.Height-260),new Rectangle(area.Right-220,area.Y+130,200,area.Height-260),new Rectangle(area.X+(area.Width-820)/2,area.Y+(area.Height-600)/2,820,600)};
   var names=new[]{"main","top","left","right","settings"};
   var environment=view.CoreWebView2.Environment;
   for(int index=0;index<layouts.Length;index++){
    MultiSurface surface;
    if(index==0){form.Bounds=layouts[index];form.Opacity=1;surface=new MultiSurface{name=names[index],host=(GlassCaptureHost)form,browser=view,backdrop=optical};}
    else{
     var window=new GlassCaptureHost{Text="PrismGlass isolated multi-host "+names[index],StartPosition=FormStartPosition.Manual,Bounds=layouts[index],FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,TopMost=true};
     var web=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=Color.Transparent};window.Controls.Add(web);var material=GlassWindow.Configure(window,web);surface=new MultiSurface{name=names[index],host=window,browser=web,backdrop=material};
     surfaces.Add(surface);window.Show();await web.EnsureCoreWebView2Async(environment);web.ZoomFactor=96.0/GetDpiForWindow(window.Handle);web.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-test.local",root,CoreWebView2HostResourceAccessKind.DenyCors);web.Source=new Uri("https://prism-test.local/fixture.html");
    }
    if(index==0)surfaces.Add(surface);SetWindowPos(surface.host.Handle,new IntPtr(-1),0,0,0,0,0x13);
    await MultiReady(surface,"!!window.stats&&stats().ready");
    await MultiEval(surface,"window.probes=0;const b=document.createElement('button');b.textContent='响应';b.id='probe';b.style.cssText='position:absolute;z-index:2;left:12px;top:12px';b.onclick=()=>{window.probes++;b.textContent='响应 '+probes};document.body.appendChild(b);window.sample=()=>{const g=document.querySelector('canvas').getContext('webgl'),p=new Uint8Array(4);g.readPixels(Math.floor(g.drawingBufferWidth/2),Math.floor(g.drawingBufferHeight/2),1,1,g.RGBA,g.UNSIGNED_BYTE,p);return Array.from(p).join(',')};receive({type:'layout',width:innerWidth,height:innerHeight,sceneWidth:innerWidth,sceneHeight:innerHeight,originX:0,originY:0,theme:'Sky',paused:false,cards:[{name:'Test',x:8,y:8,w:innerWidth-16,h:innerHeight-16,r:24,fixedShape:true}]});");
    surface.host.Probe=sent=>{native.Add((Stopwatch.GetTimestamp()-sent)*1000.0/Stopwatch.Frequency);received++;};
   }
   Check(surfaces.All(s=>{var candidates=LiveGlassBackdrop.Sources(s.host.Handle,s.host.RectangleToScreen(s.host.ClientRectangle));return candidates.Count==1&&candidates[0].handle==source;}),"all five hosts capture only the isolated dynamic source, never user windows");
   before=CaptureCounts();LiveGlassBackdrop.Configure(root,true,false);
   foreach(var surface in surfaces)await MultiEval(surface,"enable(true)");
   foreach(var surface in surfaces)await MultiReady(surface,"stats().liveBackground==='optical'&&!stats().errors.length");
   await Task.Delay(300);started=CaptureCounts();Check(Counter(started,"activeSources")==1&&Counter(started,"leases")==5,"five live glass hosts share exactly one native capture source");
   foreach(var surface in surfaces){surface.startVersion=surface.backdrop.Version;surface.frameStart=Int32.Parse(await MultiEval(surface,"stats().frames"));surface.pixelStart=await MultiEval(surface,"sample()");}
   heartbeat.Tick+=(s,e)=>{double now=clock.Elapsed.TotalMilliseconds;if(previous>0)timers.Add(now-previous);previous=now;};clock.Start();heartbeat.Start();
   var handles=surfaces.Select(s=>s.host.Handle).ToArray();
   var messages=Task.Run(async()=>{int index=0;while(clock.ElapsedMilliseconds<6000){if(PostMessage(handles[index++%handles.Length],0x8050,new IntPtr(Stopwatch.GetTimestamp()),IntPtr.Zero))Interlocked.Increment(ref posted);await Task.Delay(100);}});
   int button=0;
   while(clock.ElapsedMilliseconds<6000){var surface=surfaces[button++%surfaces.Count];var watch=Stopwatch.StartNew();string result=await MultiEval(surface,"document.getElementById('probe').click();probes>0");browser.Add(watch.Elapsed.TotalMilliseconds);if(result!="true")throw new Exception("Browser input acknowledgement failed: "+surface.name);await Task.Delay(350);}
   await messages;heartbeat.Stop();elapsed=clock.Elapsed.TotalMilliseconds;clock.Stop();await Task.Delay(80);after=CaptureCounts();
   foreach(var surface in surfaces){surface.endVersion=surface.backdrop.Version;surface.frameEnd=Int32.Parse(await MultiEval(surface,"stats().frames"));surface.pixelEnd=await MultiEval(surface,"sample()");if(surface.backdrop.Error.Length>0)errors+=surface.name+": "+surface.backdrop.Error+"; ";if(await MultiEval(surface,"stats().errors.length===0")!="true")errors+=surface.name+": renderer error; ";}
   LiveGlassBackdrop.Configure(root,false,false);foreach(var surface in surfaces)await MultiEval(surface,"enable(false)");await Task.Delay(500);stopped=CaptureCounts();await Task.Delay(400);idle=CaptureCounts();
   File.WriteAllText(Path.Combine(output,"multi-host-metrics.json"),metricsJson.Serialize(new{sourceWidth=area.Width,sourceHeight=area.Height,hostCount=surfaces.Count,durationMs=elapsed,timer=new{count=timers.Count,p95Ms=Percentile(timers,.95),maxMs=Percentile(timers,1)},nativeInput=new{posted,received,p95Ms=Percentile(native,.95),maxMs=Percentile(native,1)},browserInput=new{count=browser.Count,p95Ms=Percentile(browser,.95),maxMs=Percentile(browser,1)},capture=new{before,started,after,stopped,idle},hosts=surfaces.Select(s=>new{s.name,width=s.host.ClientSize.Width,height=s.host.ClientSize.Height,versions=s.endVersion-s.startVersion,frames=s.frameEnd-s.frameStart,pixelsChanged=s.pixelStart!=s.pixelEnd}).ToArray(),errors,note="Synthetic source only. Native input probe uses PostMessage, browser button uses DOM click; no user frames recorded."}));
   Check(errors.Length==0&&surfaces.All(s=>s.endVersion>s.startVersion+3&&s.frameEnd>s.frameStart+3&&s.pixelStart!=s.pixelEnd),"every host keeps receiving and rendering changing native pixels during the concurrent run");
   Check(timers.Count>=80&&Percentile(timers,.95)<120&&Percentile(timers,1)<500,"native UI heartbeat stays responsive during five-host glass load");
   Check(posted>=40&&received==posted&&Percentile(native,1)<500&&browser.Count>=10&&Percentile(browser,1)<800,"native message and browser button acknowledgements remain responsive");
   Check(Counter(after,"starts")-Counter(before,"starts")==1&&Counter(after,"readbacks")-Counter(started,"readbacks")>=6&&Counter(after,"readbacks")-Counter(started,"readbacks")<elapsed/1000*(SharedWindowCapture.TargetFps+5)+10,"concurrent hosts reuse one capture and keep readbacks within the shared frame budget");
   Check(Counter(stopped,"activeSources")==0&&Counter(stopped,"leases")==0&&Counter(idle,"readbacks")==Counter(stopped,"readbacks")&&Counter(idle,"starts")==Counter(stopped,"starts"),"switching live glass off releases all captures and stops background readbacks");
   Check(await MultiEval(surfaces[4],"document.getElementById('probe').click();stats().liveBackground==='wallpaper'&&probes>0")=="true","settings input still works after the capture pipeline shuts down");
   File.AppendAllText(Path.Combine(output,"checks.txt"),"Completed "+checks+" focused five-host live glass checks."+Environment.NewLine);
  }finally{
   heartbeat.Stop();heartbeat.Dispose();LiveGlassBackdrop.Configure(root,false,false);SendMessage(source,0x8005,IntPtr.Zero,IntPtr.Zero);
   foreach(var surface in surfaces){surface.host.Probe=null;if(surface.browser!=view){surface.browser.Dispose();surface.host.Close();surface.host.Dispose();}}
  }
 }
}
