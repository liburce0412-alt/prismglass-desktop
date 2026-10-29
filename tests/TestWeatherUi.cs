using System;using System.IO;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
internal sealed class WeatherUiTestForm:Form {protected override bool ShowWithoutActivation{get{return true;}}}
internal static class TestWeatherUi {
 static Form form;static WebView2 view;static string root,output,sourceEvidence;static int checks;static bool captureOnly;
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 static Task<string> Eval(string script){return view.CoreWebView2.ExecuteScriptAsync(script);}
 static async Task Assert(string script,string name){bool ok=await Eval(script)=="true";File.AppendAllText(Path.Combine(output,"ui-checks.txt"),(ok?"PASS ":"FAIL ")+name+Environment.NewLine);if(!ok)throw new Exception(name);checks++;}
 static async Task Wait(string script){for(int i=0;i<100;i++){if(await Eval(script)=="true")return;await Task.Delay(80);}throw new Exception("UI wait timed out: "+script);}
 static async Task Capture(string name){await Task.Delay(180);using(var file=File.Create(Path.Combine(output,name+".png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);}
 static Task Message(string value){return Eval("chrome.webview.dispatchEvent(new MessageEvent('message',{data:"+value+"}))");}
 static string Weather(string state){
  if(state=="unavailable")return "{ready:false,temp:0,code:0,high:null,low:null,updatedAt:0,location:'杭州 · 浙江 · 中国',reason:'天气尚未成功更新，请在电脑检查天气位置与网络。'}";
  if(state=="expired")return "{ready:true,temp:21,code:3,high:24,low:18,updatedAt:Date.now()-86400000,location:'杭州 · 浙江 · 中国',source:'Open-Meteo',kind:'model',reason:'天气数据已过期，请检查电脑网络。'}";
  if(state=="partial")return "{ready:true,temp:0,code:null,high:null,low:null,feelsLike:null,updatedAt:Date.now()-900000,location:'长名称验收城市 · 所在地区 · 中国',source:'Open-Meteo',kind:'model'}";
  if(state=="stale")return "{ready:true,stale:true,temp:21,code:3,high:24,low:18,feelsLike:19,updatedAt:Date.now()-3600000,location:'杭州 · 浙江 · 中国',source:'CMA GRAPES · Open-Meteo',kind:'model',reason:'天气更新失败，保留上次数据；请检查电脑网络。'}";
  return "{ready:true,temp:21,code:3,high:24,low:18,feelsLike:19,updatedAt:Date.now()-900000,fetchedAt:Date.now(),location:'杭州 · 浙江 · 中国',source:'CMA GRAPES · Open-Meteo',kind:'model'}";
 }
 static Task DesktopWeather(string state){return Eval("window.fixtureWeather="+Weather(state)+";chrome.webview.dispatchEvent(new MessageEvent('message',{data:{type:'widgets',weather:fixtureWeather,sourceStatus:{weather:{available:"+(state=="unavailable"||state=="expired"?"false":"true")+",updatedAt:fixtureWeather.updatedAt,reason:fixtureWeather.reason||''}},system:{cpu:12},music:{title:'等待播放',artist:'',cover:'',playing:false},focus:{available:false}}}))");}
 static async Task CompanionWeather(string state){bool available=state!="unavailable"&&state!="expired";await Eval("var w="+Weather(state)+";__fixture.state.weather={available:"+(available?"true":"false")+",updatedAt:"+(available?"w.updatedAt":"0")+",reason:w.reason||'',data:"+(available?"w":"null")+"};document.querySelector('#reconnect').click()");await Wait("document.querySelector('#connection-status').textContent==='已连接电脑'");}
 static async Task Run(){
  if(File.Exists(Path.Combine(root,"weather-source-evidence.json")))sourceEvidence=File.ReadAllText(Path.Combine(root,"weather-source-evidence.json"));
  using(var bitmap=new Bitmap(1920,1080))using(var g=Graphics.FromImage(bitmap))using(var brush=new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0,0,1920,1080),Color.FromArgb(26,43,73),Color.FromArgb(143,169,200),35)){g.FillRectangle(brush,0,0,1920,1080);bitmap.Save(Path.Combine(root,"wallpaper.png"));}
  await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"test-runtime")));view.ZoomFactor=96.0/GetDpiForWindow(form.Handle);
  view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-weather-test.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
  view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-companion-test.local",Path.Combine(root,"companion-web"),CoreWebView2HostResourceAccessKind.DenyCors);
  await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.__errors=[];addEventListener('error',e=>__errors.push(e.message));window.__messages=[];const originalPost=chrome.webview.postMessage.bind(chrome.webview);chrome.webview.postMessage=x=>{__messages.push(x);originalPost(x)};if(location.hostname==='prism-companion-test.local'){"+File.ReadAllText(Path.Combine(root,"companion-web","CompanionUiFixture.js"))+"}");
  view.Source=new Uri("https://prism-weather-test.local/sidedock.html");await Wait("!!document.querySelector('#weather-temp')&&__messages.includes('ready')");
  foreach(string theme in new[]{"Sky","Astro"}){
   await Message("{type:'layout',width:1920,height:1080,viewWidth:490,viewHeight:680,x:60,y:180,scale:1,theme:'"+theme+"',wallpaper:1}");
   if(theme=="Sky")await Message("{type:'open',mode:'weather'}");await Task.Delay(500);
   foreach(string state in new[]{"fresh","stale","unavailable","expired","partial"}){
    await DesktopWeather(state);await Capture("desktop-"+theme.ToLowerInvariant()+"-"+state);
    if(captureOnly)continue;
    await Assert("__errors.length===0","desktop real scripts have no errors: "+theme+" "+state);
    await Assert("(()=>{const p=document.querySelector('[data-widget-page=weather]'),d=document.querySelector('#detail'),b=p.querySelector('[data-action=weather-settings]').getBoundingClientRect();return p.scrollWidth<=p.clientWidth&&b.bottom<=d.getBoundingClientRect().bottom&&b.bottom<=innerHeight})()","desktop weather details and settings fit: "+theme+" "+state);
    await Assert("document.body.dataset.glassReady==='true'","desktop optical glass stays active: "+theme+" "+state);
    if(state=="fresh")await Assert("document.querySelector('#weather-feels').textContent.includes('19°')&&document.querySelector('#weather-stamp').textContent.includes('数据')&&document.querySelector('#weather-source').textContent.includes('数值预报')","desktop labels real feels-like, data time and source kind: "+theme);
    if(state=="stale")await Assert("document.querySelector('#weather-stamp').textContent.startsWith('上次天气')&&!document.querySelector('#weather-status').hidden&&document.querySelector('#weather-status').textContent.includes('更新失败')&&document.querySelector('#weather-temp').textContent==='21°'","desktop marks retained weather with time and failure reason: "+theme);
    if(state=="unavailable"||state=="expired")await Assert("document.querySelector('#weather-temp').textContent==='—'&&document.querySelector('#rail-weather').textContent==='—'&&document.querySelector('#weather-status').textContent.includes('天气')&&document.querySelector('.landscape').dataset.code==='unknown'","desktop rejects unavailable or expired numeric placeholders: "+theme+" "+state);
    if(state=="partial")await Assert("document.querySelector('#weather-temp').textContent==='0°'&&document.querySelector('#weather-high').textContent==='—'&&document.querySelector('#weather-low').textContent==='—'&&document.querySelector('#weather-feels').hidden&&!document.querySelector('#weather-condition').textContent.includes('晴')","desktop distinguishes real zero from missing fields: "+theme);
   }
   if(sourceEvidence!=null){
    await Eval("window.realWeatherEvidence="+sourceEvidence+";chrome.webview.dispatchEvent(new MessageEvent('message',{data:Object.assign({type:'widgets',system:{cpu:12},music:{title:'等待播放',artist:'',cover:'',playing:false},focus:{available:false}},realWeatherEvidence)}))");await Capture("desktop-"+theme.ToLowerInvariant()+"-live-source");
    if(!captureOnly)await Assert("document.querySelector('#weather-temp').textContent===Math.round(realWeatherEvidence.weather.temp)+'°'&&document.querySelector('#weather-feels').textContent.includes(Math.round(realWeatherEvidence.weather.feelsLike)+'°')&&document.querySelector('#weather-source').textContent.includes(realWeatherEvidence.weather.source)&&document.querySelector('#weather-stamp').textContent.includes(new Date(realWeatherEvidence.weather.updatedAt).toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit',hour12:false}))","desktop shows recorded real source values and original data timestamp: "+theme);
   }
  }
  await Assert("!__messages.some(x=>x.startsWith('action:'))","desktop weather review performs no host action");
  form.ClientSize=new Size(768,1024);view.Source=new Uri("https://prism-companion-test.local/index.html");await Wait("!!document.querySelector('#connection-status')&&document.querySelector('#connection-status').textContent==='已连接电脑'");
  foreach(var size in new[]{new Size(768,1024),new Size(1024,768)}){
   form.ClientSize=size;
   foreach(string theme in new[]{"Sky","Astro"}){
    await Eval("__fixture.state.theme='"+theme+"';__fixture.state.settings.theme='"+theme+"'");
    foreach(string state in new[]{"fresh","stale","unavailable","expired","partial"}){
     await CompanionWeather(state);await Eval("document.querySelector('#home-tab').click()");string name=theme.ToLowerInvariant()+"-"+size.Width+"x"+size.Height+"-"+state;await Capture("companion-"+name);
     if(captureOnly)continue;
     await Assert("__errors.length===0&&document.documentElement.scrollWidth<=innerWidth","companion weather layout and scripts: "+name);
     await Assert("(()=>{const w=document.querySelector('#weather'),r=w.getBoundingClientRect(),p=document.querySelector('#main-content').getBoundingClientRect();return !w.hidden&&r.width>0&&r.top>=p.top&&r.bottom<=p.bottom&&r.right<=innerWidth})()","companion weather remains visible including unavailable state: "+name);
     await Assert("document.body.dataset.optics==='ready'&&PrismGlassOptics.inspect().contextCreations===1","companion keeps shared optical glass: "+name);
     if(state=="fresh")await Assert("document.querySelector('#weather-location').textContent.includes('杭州')&&document.querySelector('#weather-detail').textContent.includes('体感 19°')&&document.querySelector('#weather-stamp').textContent.includes('数值预报')","companion home includes location, real feels-like and provenance: "+name);
     if(state=="stale")await Assert("document.querySelector('#weather-stamp').textContent.startsWith('上次天气')&&!document.querySelector('#weather-status').hidden&&document.querySelector('#weather-status').textContent.includes('更新失败')","companion distinguishes retained weather from current data: "+name);
     if(state=="unavailable"||state=="expired")await Assert("document.querySelector('#weather-main').textContent==='天气暂不可用'&&document.querySelector('#weather-detail').textContent.includes('天气')","companion home explains unavailable weather: "+name);
     if(state=="partial")await Assert("document.querySelector('#weather-main').textContent.startsWith('0°')&&!document.querySelector('#weather-detail').textContent.includes('体感')&&!document.querySelector('#weather-main').textContent.includes('晴')","companion does not invent missing conditions or apparent temperature: "+name);
    }
   }
  }
  await CompanionWeather("fresh");await Eval("document.querySelector('#home-tools').click();document.querySelector('#widget-card-weather').scrollIntoView({block:'center'})");await Capture("companion-widget-fresh");
  if(!captureOnly)await Assert("document.querySelector('#widget-weather-location').textContent.includes('杭州')&&document.querySelector('#widget-weather-summary').textContent.includes('体感 19°')&&document.querySelector('#widget-weather-status').textContent.includes('数值预报')","weather widget shows matching real location, feels-like and source");
  await CompanionWeather("unavailable");await Eval("document.querySelector('#widget-card-weather').scrollIntoView({block:'center'})");await Capture("companion-widget-unavailable");
  if(!captureOnly)await Assert("document.querySelector('#widget-weather-temp').textContent==='—'&&document.querySelector('#widget-weather-summary').textContent.includes('检查')","weather widget preserves useful unavailable reason");
  await CompanionWeather("stale");await Eval("document.querySelector('#widget-card-weather').scrollIntoView({block:'center'})");await Capture("companion-widget-stale");
  if(!captureOnly)await Assert("document.querySelector('#widget-weather-status').textContent.startsWith('上次天气')&&document.querySelector('#widget-weather-reason').textContent.includes('更新失败')","weather widget labels retained data and update failure");
  if(sourceEvidence!=null){
   await Eval("window.realWeatherEvidence="+sourceEvidence+";__fixture.state.weather={available:realWeatherEvidence.sourceStatus.weather.available,updatedAt:realWeatherEvidence.weather.updatedAt,reason:realWeatherEvidence.sourceStatus.weather.reason,data:realWeatherEvidence.weather};document.querySelector('#reconnect').click()");await Wait("document.querySelector('#connection-status').textContent==='已连接电脑'");await Eval("document.querySelector('#home-tab').click()");await Capture("companion-live-source-home");
   if(!captureOnly)await Assert("document.querySelector('#weather-main').textContent.startsWith(Math.round(realWeatherEvidence.weather.temp)+'°')&&document.querySelector('#weather-location').textContent===realWeatherEvidence.weather.location&&document.querySelector('#weather-stamp').textContent.includes(realWeatherEvidence.weather.source)","companion shows original recorded real source location and weather values");
   await Eval("document.querySelector('#home-tools').click();document.querySelector('#widget-card-weather').scrollIntoView({block:'center'})");await Capture("companion-live-source-widget");
  }
  await Eval("__fixture.mode='locked';document.querySelector('#reconnect').click()");await Wait("document.querySelector('#connection-status').textContent==='电脑已锁定'");
  if(!captureOnly)await Assert("document.querySelector('#weather').hidden&&document.querySelector('#weather-location').textContent===''&&document.querySelector('#weather-stamp').textContent===''&&document.querySelector('#widget-weather-location').textContent==='电脑天气位置'","locking clears all added private weather metadata");
  await Assert("__fixture.writes.length===0","companion weather review performs no computer writes");
  File.AppendAllText(Path.Combine(output,"ui-checks.txt"),captureOnly?"Capture-only baseline; semantic assertions skipped."+Environment.NewLine:"Completed "+checks+" weather UI checks. PC WebView2, isolated fixture data; no real-device acceptance."+Environment.NewLine);
 }
 [STAThread]static void Main(string[] args){SetProcessDPIAware();root=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);captureOnly=Boolean.Parse(args[2]);Directory.CreateDirectory(output);Application.EnableVisualStyles();form=new WeatherUiTestForm{ClientSize=new Size(490,680),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Location=new Point(-16000,-16000),ShowInTaskbar=false,Opacity=0};view=new WebView2{Dock=DockStyle.Fill};form.Controls.Add(view);form.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){File.AppendAllText(Path.Combine(output,"ui-checks.txt"),ex.ToString());Environment.ExitCode=1;}finally{view.Dispose();form.Close();}};Application.Run(form);}
}
