// Isolated off-screen WebView: actual shared shader and music-page HTML, fictional data.
using System;using System.IO;using System.Drawing;using System.Drawing.Drawing2D;using System.Threading.Tasks;using System.Windows.Forms;using System.Collections.Generic;using System.Web.Script.Serialization;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
internal static class TestGlassLyricsUi {
 static WebView2 view;static Form form;static string root,output;static int checks;static bool musicOnly,autoAck=true;static string pendingIcon;static readonly List<string> errors=new List<string>();static readonly List<string> messages=new List<string>();
 // This renderer fixture acknowledges a simulated skin. Native meter ordering is
 // exercised separately by TestDockIconBridge against an isolated Rainmeter.
 static void Ack(string message){var parts=message.Split(':');view.CoreWebView2.PostWebMessageAsJson("{\"type\":\"dock-icons-ack\",\"sequence\":"+parts[1]+",\"alpha\":"+parts[2]+"}");}
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr handle);
 static Task<string> Eval(string script){return view.CoreWebView2.ExecuteScriptAsync(script);}
 static void Check(bool value,string name){File.AppendAllText(Path.Combine(output,"ui-checks.txt"),(value?"PASS ":"FAIL ")+name+Environment.NewLine);if(!value)throw new Exception(name);checks++;}
 static async Task Assert(string script,string name){Check(await Eval(script)=="true",name);}
 static async Task Save(string name){using(var f=File.Create(Path.Combine(output,name+".png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,f);}
 static async Task Load(string file,string condition){view.Source=new Uri("https://prism-test.local/"+file);await Task.Delay(200);for(int i=0;i<100;i++){if(await Eval(condition)=="true")return;await Task.Delay(100);}throw new Exception("Page did not become ready: "+file);}
 static async Task Message(string value){await Eval("window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:"+value+"}))");}
 static void Wallpaper(){
  using(var b=new Bitmap(1200,800)){using(var g=Graphics.FromImage(b)){
   g.SmoothingMode=SmoothingMode.AntiAlias;
   using(var brush=new LinearGradientBrush(new Rectangle(0,0,1200,800),Color.FromArgb(197,219,233),Color.FromArgb(114,153,182),35))g.FillRectangle(brush,0,0,1200,800);
   using(var p=new Pen(Color.FromArgb(145,125,139,205),170))g.DrawBezier(p,-160,670,420,-120,580,1060,1320,180);
   using(var p=new Pen(Color.FromArgb(150,232,188,154),120))g.DrawBezier(p,-50,420,510,100,710,800,1280,430);
   using(var p=new Pen(Color.FromArgb(80,245,251,255),2)){for(int x=0;x<1200;x+=50)g.DrawLine(p,x,0,x,800);for(int y=0;y<800;y+=50)g.DrawLine(p,0,y,1200,y);}
  }b.Save(Path.Combine(root,"wallpaper.png"));}
 }
 static string Fixture(string shader){
  return "<!doctype html><meta charset='utf-8'><style>body{margin:0;color:#20324b;font:16px 'Segoe UI','Microsoft YaHei UI'}canvas{position:absolute;inset:0}section{position:absolute;left:92px;top:140px;width:310px}h1{font-size:32px;font-weight:600;margin:10px 0}p{line-height:1.9}.caption{position:absolute;top:35px;left:70px;font-size:12px;letter-spacing:2px}.lyric{position:absolute;left:80px;top:637px;width:1040px;text-align:center;line-height:1.8;font-size:20px;font-weight:600}.lyric small{display:block;font-size:15px;opacity:.72}</style><canvas id='glass'></canvas><div class='caption'>PRISMGLASS · OPTICAL TEST / SYNTHETIC BACKDROP</div><section><p>LIQUID SURFACE</p><h1>让背景穿过玻璃。</h1><p>边缘折射 · 透明主体 · 细腻高光<br>测试背景与文字均为演示内容</p></section><div class='lyric'>把这一刻，留在光里<small>下一句歌词 · 演示内容</small></div><script src='"+shader+"'></script><script>window.events=[];window.failure=[];onerror=(m)=>failure.push(m);PrismGlassRenderer(document.getElementById('glass'),m=>events.push(m),r=>window.receive=r);window.layout=(theme,paused=false)=>{document.body.style.color=theme==='Astro'?'#e7effc':'#20324b';receive({type:'layout',width:1200,height:800,sceneWidth:1200,sceneHeight:800,originX:0,originY:0,theme,paused,cards:[{name:'Detail',x:65,y:100,w:390,h:440,r:30,fixedShape:true,rimStrength:.5},{name:'Tile',x:665,y:210,w:340,h:250,r:54},{name:'Dock',x:65,y:620,w:1070,h:100,r:28}]})};layout('Sky');</script>";
 }
 static async Task Run(){
  await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"ui-preview-runtime")));
  view.ZoomFactor=96.0/GetDpiForWindow(form.Handle);view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-test.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
  view.CoreWebView2.WebMessageReceived+=(s,e)=>{var m=e.TryGetWebMessageAsString();messages.Add(m);if(m.StartsWith("error:"))errors.Add(m);if(m.StartsWith("dock-icons:")){pendingIcon=m;if(autoAck)Ack(m);}};
  await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.__errors=[];window.addEventListener('error',e=>__errors.push(e.message));");
  if(!musicOnly){
  foreach(var version in new[]{"before","after"}){
   File.WriteAllText(Path.Combine(root,"glass-test-"+version+".html"),Fixture(version=="before"?"glass-baseline.js":"glass-renderer.js"));
   await Load("glass-test-"+version+".html","!!window.events&&events.includes('painted')");await Task.Delay(400);
   foreach(var theme in new[]{"Sky","Astro"}){
    await Eval("layout('"+theme+"')");if(version=="after")await Eval("document.body.style.color='#fff';document.body.style.textShadow='0 1px 2px rgba(6,14,28,.75)'");await Task.Delay(250);await Save("glass-"+version+"-"+theme.ToLowerInvariant());
    await Assert("failure.length===0&&!events.some(m=>m.startsWith('error:'))","GLSL compiles and renders "+version+" "+theme);
   }
   if(version=="after"){
    await Eval("layout('Sky');document.body.style.color='#fff';receive({type:'pointer',x:820,y:325,down:true})");await Task.Delay(500);await Save("glass-after-hover");
    await Eval("receive({type:'pointer',x:990,y:668,down:false})");await Task.Delay(500);await Save("dock-restored-hover");
    await Eval("receive({type:'stats'});window.hoverShape=events.filter(s=>s.startsWith('dock-shape:')).pop()");
    await Assert("(()=>{const s=JSON.parse(events.filter(s=>s.startsWith('stats:')).pop().slice(6));return s.activeCards.includes('Dock')&&s.maxDeformation>1})()","Dock hover still drives the native glass outline");
    await Eval("receive({type:'pointer',x:990,y:668,down:true})");await Task.Delay(300);
    await Assert("events.filter(s=>s.startsWith('dock-shape:')).pop()!==hoverShape","Dock press still deforms its outline");
    await Eval("receive({type:'pointer',x:-999,y:-999,down:false})");await Task.Delay(1200);
    await Eval("receive({type:'stats'})");await Assert("JSON.parse(events.filter(s=>s.startsWith('stats:')).pop().slice(6)).activeCards.length===0","pointer leaves and glass settles");
    await Eval("layout('Astro');receive({type:'pointer',x:990,y:668,down:false})");await Task.Delay(700);
    await Save("dock-astro-effects");
    await Eval("window.astroFrame=document.getElementById('glass').toDataURL()");await Task.Delay(400);
    await Assert("document.getElementById('glass').toDataURL()!==astroFrame&&!events.some(m=>m.startsWith('error:'))","Astro atmospheric effects animate on the actual Dock surface");
    await Eval("layout('Astro',true)");await Task.Delay(1800);
    await Eval("window.pausedFrame=document.getElementById('glass').toDataURL();receive({type:'stats'});window.pausedFrames=JSON.parse(events.filter(s=>s.startsWith('stats:')).pop().slice(6)).frames");await Task.Delay(350);
    await Eval("receive({type:'stats'})");
    await Assert("document.getElementById('glass').toDataURL()===pausedFrame&&JSON.parse(events.filter(s=>s.startsWith('stats:')).pop().slice(6)).paused","pausing effects settles the Dock and stops visual animation");
    await Eval("layout('Astro')");await Task.Delay(500);await Eval("receive({type:'stats'})");
    await Assert("(()=>{const s=JSON.parse(events.filter(s=>s.startsWith('stats:')).pop().slice(6));return !s.paused&&s.frames>pausedFrames&&s.activeCards.includes('Dock')})()","resuming effects restores Dock hover and animation");
   }
  }
  await Load("index.html","!!document.querySelector('#dock-lyrics')&&document.readyState==='complete'");
  await Message("{type:'layout',width:1200,height:800,theme:'Sky',paused:false,cards:[{name:'Dock',x:65,y:620,w:1070,h:100,r:28,insetY:16}]}");
  await Task.Delay(80);autoAck=false;
  await Message("{type:'lyrics',show:true,current:'迟缓宿主测试',next:'等待图标隐藏',motionPaused:false}");await Task.Delay(450);
  await Assert("dockLyricsState.reveal===.18&&dockLyricsState.opacity===0&&dockLyricsState.width>dockLyricsState.targetWidth","busy native skin holds entry before collapse and lyric reveal");
  Ack(pendingIcon);await Task.Delay(80);var oldZero=pendingIcon;
  await Message("{type:'lyrics',show:false,current:'',next:'',motionPaused:false}");
  await Message("{type:'lyrics',show:true,current:'快速反向',next:'旧回执不能显示',motionPaused:false}");Ack(oldZero);await Task.Delay(100);
  await Assert("!dockLyricsState.iconsHidden&&dockLyricsState.opacity===0","delayed zero acknowledgement cannot reveal a later transition");
  autoAck=true;Ack(pendingIcon);await Task.Delay(1100);
  await Assert("dockLyricsState.iconsHidden&&dockLyricsState.opacity===1","current zero acknowledgement releases the same geometry spring");
  await Message("{type:'lyrics',show:false,current:'',next:'',motionPaused:true}");await Task.Delay(100);
  await Message("{type:'lyrics',show:true,current:'玻璃里的光 · 星に願いを 🎵',next:'White core · 冰蓝微光',motionPaused:false}");
  await Task.Delay(180);await Save("lyrics-contracting");
  await Assert("dockLyricsState.width<1070&&dockLyricsState.width>dockLyricsState.targetWidth&&dockLyricsState.velocity<0","lyrics contract progressively instead of jumping to a fixed width");
  await Task.Delay(1150);await Save("lyrics-capsule-sky");
  await Assert("document.querySelector('#lyric-current').textContent==='玻璃里的光 · 星に願いを 🎵'&&dockLyricsState.opacity===1&&dockLyricsState.width<=900","actual glass page renders Chinese, Japanese and emoji in the fitted capsule");
  await Assert("getComputedStyle(document.querySelector('#lyric-current')).color==='rgb(255, 255, 255)'&&parseInt(getComputedStyle(document.querySelector('#lyric-current')).fontWeight)>=600","current lyric keeps a clear white core and stronger type");
  var outline=messages.FindLast(m=>m.StartsWith("dock-shape:"));
  await Eval("window.capsuleOutline="+outline.Substring(11));
  await Assert("Math.min(...capsuleOutline.map(p=>p[0]))>100&&Math.abs((Math.min(...capsuleOutline.map(p=>p[0]))+Math.max(...capsuleOutline.map(p=>p[0])))/2-561)<2","native clipping follows the smaller capsule at the original Dock centre");
  await Message("{type:'lyrics',show:true,current:'<img src=x onerror=alert(1)> #中文 [Measure]',next:'下一句：风从身边经过',motionPaused:false}");
  await Task.Delay(70);
  await Assert("document.querySelector('#lyric-current').textContent.includes('<img')&&!document.querySelector('#dock-lyrics img')&&document.querySelector('#dock-lyrics').getAnimations({subtree:true}).length>0","literal text stays safe and a real line change starts the entrance effect");
  await Message("{type:'pointer',x:600,y:670,down:false}");
  await Task.Delay(140);
  await Assert("!dockLyricsState.active&&dockLyricsState.opacity>0&&dockLyricsState.opacity<1&&dockLyricsState.width<1070&&dockLyricsState.velocity>0","pointer approach fades lyrics while smoothly expanding the Dock");
  await Eval("window.interruptedWidth=dockLyricsState.width");
  await Message("{type:'lyrics',show:true,current:'接住正在移动的玻璃',next:'保留速度，继续收拢',motionPaused:false}");
  await Assert("Math.abs(dockLyricsState.width-interruptedWidth)<80&&dockLyricsState.opacity>0","reversing during expansion preserves position and text without flashing off");
  await Message("{type:'lyrics',show:true,current:'这是一句用于检查超长歌词可以完整保留并缓慢滚动的演示文字。'.repeat(4),next:'下一句仍然清晰',motionPaused:false}");
  await Task.Delay(100);
  await Assert("dockLyricsState.targetWidth<=900&&document.querySelector('.current-window').classList.contains('overflowing')&&document.querySelector('#lyric-current').getAnimations().some(a=>a.effect.getTiming().iterations===Infinity)","long lines have a bounded capsule and readable overflow scrolling");
  await Message("{type:'lyrics',show:true,current:'减少动态效果时保持清晰',next:'中文与白字仍然保留',motionPaused:true}");
  await Task.Delay(100);
  await Assert("dockLyricsState.reduced&&dockLyricsState.width===dockLyricsState.targetWidth&&document.querySelector('#dock-lyrics').getAnimations({subtree:true}).length===0","reduced motion stops text and geometry animation");
  await Message("{type:'layout',width:1200,height:800,theme:'Astro',paused:false,cards:[{name:'Dock',x:65,y:620,w:1070,h:100,r:28,insetY:16}]}");
  await Task.Delay(200);await Save("lyrics-capsule-astro");
  await Assert("dockLyricsState.active&&document.querySelector('#lyric-current').textContent==='减少动态效果时保持清晰'","theme changes preserve the active Unicode lyric");
  await Task.Delay(3100);await Task.Delay(1000);
  await Assert("!dockLyricsState.active&&dockLyricsState.width===1070","expired native heartbeat restores the full Dock");
  await Message("{type:'lyrics',show:true,current:'星光从指间经过 🎵',next:'色彩与字体可以自由组合',appearance:{font:'kai',layout:'split',color:'#fff2cb',accent:'#ffbc69',texture:'shimmer',effect:'wave',widthMode:'fixed',width:840},motionPaused:false}");
  await Task.Delay(250);await Save("lyrics-custom-kai-wave");
  await Assert("dockLyricsState.targetWidth===840&&document.querySelector('#dock-lyrics').dataset.layout==='split'&&document.querySelectorAll('.lyric-letter').length>0&&document.querySelector('#lyric-current').textContent.endsWith('🎵')","custom font, split layout, fixed width and Unicode wave are applied to the real Dock");
  await Message("{type:'lyrics',show:true,current:'静态时也保持清晰',next:'下一句',appearance:{texture:'shimmer',effect:'wave'},motionPaused:true}");
  await Task.Delay(100);
  await Assert("document.querySelector('#dock-lyrics').getAnimations({subtree:true}).length===0","reduced motion stops new textures and character effects too");
  await Message("{type:'lyrics',show:false,current:'',next:'',motionPaused:false}");
  await Message("{type:'pointer',x:990,y:668,down:false}");await Task.Delay(700);
  await Message("{type:'stats'}");
  var desktopStats=messages.FindLast(m=>m.StartsWith("stats:"));
  await Eval("window.desktopStats="+desktopStats.Substring(6));
  await Assert("desktopStats.activeCards.includes('Dock')&&desktopStats.maxDeformation>1&&__errors.length===0","original glass hover returns after the lyric capsule closes");
  }
  form.ClientSize=new Size(852,680);
  await Load("sidedock.html","!!document.querySelector('#lyrics-toggle')&&document.readyState==='complete'");
  await Message("{type:'layout',theme:'Sky',scale:1,x:0,y:0,width:1200,height:800,viewWidth:852,viewHeight:680,wallpaper:1}");
  await Message("{type:'open',mode:'music'}");
  await Message("{type:'widgets',music:{title:'光的方向 · 演示',artist:'测试歌手',cover:'',playing:true},lyrics:{enabled:true,online:false,offsetMs:200,status:'双行歌词 · 鼠标靠近恢复应用'}}");
  await Task.Delay(1200);
  File.AppendAllText(Path.Combine(output,"ui-checks.txt"),await Eval("JSON.stringify({ready:document.body.dataset.glassReady,color:getComputedStyle(document.querySelector('#detail')).backgroundColor,errors:__errors})")+Environment.NewLine);
  await Save("music-sky");
  await Assert("document.body.dataset.glassReady==='true'&&parseFloat(getComputedStyle(document.querySelector('#detail')).backgroundColor.split(',')[3])<.06","actual music panel uses clear surface after glass ready");
  File.AppendAllText(Path.Combine(output,"ui-checks.txt"),"Music text colors: "+await Eval("JSON.stringify(['music-title','playback-state','lyrics-status','lyrics-network-note'].map(id=>[id,getComputedStyle(document.getElementById(id)).color]))")+Environment.NewLine);
  await Assert("document.querySelector('#lyrics-toggle').getAttribute('aria-checked')==='true'&&document.querySelector('#lyrics-online').getAttribute('aria-checked')==='false'","lyric switch state is reflected accessibly");
  await Save("music-sky");
  await Eval("document.getElementById('lyrics-offset').scrollIntoView({block:'center'})");await Task.Delay(150);
  await Assert("(()=>{const a=document.getElementById('lyrics-offset').getBoundingClientRect(),b=document.getElementById('widget-page').getBoundingClientRect();return a.top>=b.top&&a.bottom<=b.bottom})()","timing controls remain reachable in scrolling music panel");
  await Eval("document.getElementById('lyrics-toggle').click();document.getElementById('lyrics-online').click();document.querySelector('[data-action=lyrics-earlier]').click()");
  await Task.Delay(100); // Native postMessage callbacks are dispatched asynchronously.
  Check(messages.Contains("action:lyrics-toggle")&&messages.Contains("action:lyrics-online")&&messages.Contains("action:lyrics-earlier"),"music controls send correct backend commands");
  await Eval("document.querySelector('[data-lyric-preset=aurora]').click();document.querySelector('#lyrics-preview').scrollIntoView({block:'center'})");
  await Task.Delay(250);await Save("music-style-presets");
  Check(messages.Exists(m=>m.StartsWith("action:lyrics-style:")&&m.Contains("\"effect\":\"wave\"")&&m.Contains("\"texture\":\"shimmer\"")),"preset sends the actual combined appearance for persistence");
  await Assert("document.querySelectorAll('[data-lyric-preset]').length===8&&document.querySelector('[data-lyric-field=effect]').options.length===12&&document.querySelector('[data-lyric-field=texture]').options.length===5","music panel offers 8 presets, 12 entrances and 5 composable text finishes");
  await Eval("window.pendingStyle=PrismLyrics.normalize(PrismLyrics.presets.aurora.style)");
  await Message("{type:'widgets',music:{title:'光的方向 · 演示',artist:'测试歌手',cover:'',playing:true},lyrics:{enabled:true,online:false,offsetMs:200,status:'歌词显示中',appearance:window.pendingStyle}}");
  await Assert("document.querySelector('#lyrics-save-state').textContent==='已保存'&&document.querySelector('[data-lyric-field=effect]').value==='wave'","acknowledged appearance remains selected instead of resetting on a widget refresh");
  await Message("{type:'layout',theme:'Astro',scale:1,x:0,y:0,width:1200,height:800,viewWidth:852,viewHeight:680,wallpaper:1}");await Task.Delay(450);await Save("music-astro");
  File.AppendAllText(Path.Combine(output,"ui-checks.txt"),"Music errors: "+await Eval("JSON.stringify(__errors)")+Environment.NewLine);
  await Assert("__errors.length===0","actual music page has no JavaScript errors");
  Check(errors.Count==0,"no renderer errors in actual music page");
  form.ClientSize=new Size(1200,328);
  await Load("island.html","!!document.querySelector('[data-action=tray]')&&document.readyState==='complete'");
  await Message("{type:'layout',theme:'Sky',scale:1,x:0,y:0,width:1200,height:800,paused:false,wallpaper:1}");await Task.Delay(600);
  await Assert("document.querySelector('[data-action=tray]').getAttribute('aria-label')==='打开系统托盘'","top bar exposes a labelled system tray button");
  await Eval("document.querySelector('[data-action=tray]').click()");
  Check(messages.Contains("action:tray"),"system tray button sends the native action");
  await Assert("(()=>{const b=document.querySelector('[data-action=tray]'),r=b.getBoundingClientRect();return document.elementFromPoint(r.x+r.width/2,r.y+r.height/2).closest('[data-action=tray]')===b})()","island drag handle does not cover system tray");
  await Save("topbar-system-tray");
  await Assert("__errors.length===0","top bar has no JavaScript errors");
  File.AppendAllText(Path.Combine(output,"ui-checks.txt"),"Completed "+checks+" isolated render/UI checks."+Environment.NewLine);
 }
 [STAThread]static void Main(string[] args){
  SetProcessDPIAware();root=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);musicOnly=args.Length>2&&args[2]=="--music-only";Directory.CreateDirectory(root);Directory.CreateDirectory(output);Wallpaper();
  Application.EnableVisualStyles();form=new Form{ClientSize=new Size(1200,800),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Location=new Point(-16000,-16000),ShowInTaskbar=false};
  view=new WebView2{Dock=DockStyle.Fill};form.Controls.Add(view);form.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){File.AppendAllText(Path.Combine(output,"ui-checks.txt"),ex.ToString());Environment.ExitCode=1;}finally{view.Dispose();form.Close();}};Application.Run(form);
 }
}
