// README media from the production HTML/CSS/JS, rendered off-screen with demo text.
using System;using System.IO;using System.Drawing;using System.Drawing.Drawing2D;using System.Diagnostics;using System.Globalization;using System.Collections.Generic;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
internal static class CaptureLyricsShowcase {
 static Form form;static WebView2 view;static string root,output;static readonly JavaScriptSerializer json=new JavaScriptSerializer();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 static Task<string> Eval(string script){return view.CoreWebView2.ExecuteScriptAsync(script);}
 static Task<string> Message(string data){return Eval("window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:"+data+"}))");}
 static async Task Load(string page){
  view.Source=new Uri("https://prism-demo.local/"+page);
  for(int i=0;i<120;i++){await Task.Delay(100);if(await Eval("document.readyState==='complete'&&!!window.PrismLyrics")=="true"){await Task.Delay(200);return;}}
  throw new Exception("Preview page did not load: "+page);
 }
 static async Task Save(string path){
  using(var file=File.Create(path))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
 }
 static async Task Crop(string name,int x,int y,int width,int height){
  string result=await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",json.Serialize(new{format="png",captureBeyondViewport=false,clip=new{x=x,y=y,width=width,height=height,scale=1}}));
  var data=json.Deserialize<Dictionary<string,object>>(result);
  File.WriteAllBytes(Path.Combine(output,name+".png"),Convert.FromBase64String((string)data["data"]));
 }
 static void Wallpaper(){
  using(var image=new Bitmap(1200,800))using(var g=Graphics.FromImage(image)){
   g.SmoothingMode=SmoothingMode.AntiAlias;
   using(var brush=new LinearGradientBrush(new Rectangle(0,0,1200,800),Color.FromArgb(14,29,52),Color.FromArgb(54,92,125),32))g.FillRectangle(brush,0,0,1200,800);
   using(var pen=new Pen(Color.FromArgb(65,147,171,240),180))g.DrawBezier(pen,-100,690,400,-40,760,620,1340,90);
   using(var pen=new Pen(Color.FromArgb(40,183,219,242),95))g.DrawBezier(pen,-80,760,390,240,750,850,1340,230);
   image.Save(Path.Combine(root,"wallpaper.png"));
  }
 }
 static async Task Lyrics(){
  await Load("index.html");
  await Message("{type:'layout',width:1200,height:420,theme:'Sky',paused:false,cards:[{name:'Dock',x:80,y:244,w:1040,h:96,r:28,insetY:16}]}");
  await Eval("document.body.insertAdjacentHTML('beforeend',`<aside id=\"demo-caption\" style=\"position:absolute;left:80px;top:42px;color:#fff;font-family:'Segoe UI','Microsoft YaHei UI',sans-serif;pointer-events:none\"><div style=\"font-size:12px;letter-spacing:3px;color:#bfdcf5\">PRISMGLASS / DOCK LYRICS</div><h1 id=\"demo-title\" style=\"font-size:34px;font-weight:600;margin:16px 0 10px\">让歌词留在玻璃里</h1><div id=\"demo-note\" style=\"font-size:17px;color:#d1e6f9\">播放时收拢 · 靠近时恢复</div></aside><small style=\"position:absolute;left:80px;bottom:25px;font:12px 'Microsoft YaHei UI';color:#c5d7e8\">真实 WebView2 / WebGL 组件 · 演示文字 · 无音频</small>`);window.demoMessage=null");
  var changes=new[]{
   new[]{".65","ice","让歌词留在玻璃里","冰蓝白字 · 轻盈浮起","让光停在歌声里","下一句 · 把此刻写成旋律"},
   new[]{"3.1","aurora","让色彩沿着文字流动","极光流彩 · 字间波浪","把此刻写成旋律","下一句 · 风从身边经过"},
   new[]{"5.7","rose","让每次换句轻轻弹起","玫瑰霓虹 · 弹性入场","风从身边经过","下一句 · 听见夜色的回声"},
   new[]{"8.3","ink","换一种字体，换一种心情","月白书笺 · 楷体聚焦","听见夜色的回声","下一句 · 把安静留给自己"}
  };
  var frames=Path.Combine(output,"frames");Directory.CreateDirectory(frames);
  var times=new List<double>();var watch=Stopwatch.StartNew();int stage=0,index=0;double heartbeat=-1;bool restored=false;
  while(watch.Elapsed.TotalSeconds<13.2){
   double time=watch.Elapsed.TotalSeconds;
   if(stage<changes.Length&&time>=Double.Parse(changes[stage][0],CultureInfo.InvariantCulture)){
    var item=changes[stage++];
    await Eval("document.getElementById('demo-title').textContent="+json.Serialize(item[2])+";document.getElementById('demo-note').textContent="+json.Serialize(item[3])+";window.demoMessage={type:'lyrics',show:true,motionPaused:false,current:"+json.Serialize(item[4])+",next:"+json.Serialize(item[5])+",appearance:PrismLyrics.normalize(PrismLyrics.presets["+json.Serialize(item[1])+"].style)}");
    await Eval("window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:demoMessage}))");heartbeat=time;
   }
   if(!restored&&time>=10.8){
    restored=true;await Eval("demoMessage=null;document.getElementById('demo-title').textContent='靠近时，平滑恢复 Dock';document.getElementById('demo-note').textContent='保留原有玻璃形变与跟手效果'");
    await Message("{type:'lyrics',show:false,current:'',next:'',motionPaused:false}");
    await Message("{type:'pointer',x:640,y:292,down:false}");
   }
   if(!restored&&time-heartbeat>=.65){await Eval("if(demoMessage)window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:demoMessage}))");heartbeat=time;}
   times.Add(watch.Elapsed.TotalSeconds);
   await Save(Path.Combine(frames,(index++).ToString("D4")+".png"));await Task.Delay(40);
  }
  using(var list=new StreamWriter(Path.Combine(frames,"frames.txt"))){
   for(int i=0;i<times.Count;i++){list.WriteLine("file '"+i.ToString("D4")+".png'");list.WriteLine("duration "+(i+1<times.Count?times[i+1]-times[i]:.07).ToString("0.000000",CultureInfo.InvariantCulture));}
   list.WriteLine("file '"+(times.Count-1).ToString("D4")+".png'");
  }
  await Message("{type:'pointer',x:-100,y:-100,down:false}");
  foreach(string preset in new[]{"ice","aurora","rose","ink"}){
   await Eval("window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:{type:'lyrics',show:true,motionPaused:false,current:'让光停在歌声里',next:'下一句 · 把此刻写成旋律',appearance:PrismLyrics.normalize(PrismLyrics.presets['"+preset+"'].style)}}))");
   await Task.Delay(1000);await Crop("lyrics-"+preset,240,216,720,160);
  }
  File.WriteAllText(Path.Combine(output,"capture.json"),json.Serialize(new{frames=index,duration=watch.Elapsed.TotalSeconds,source="production index.html, desktop.js, lyrics-style.js",fictionalText=true}));
 }
 static async Task Music(){
  form.ClientSize=new Size(852,680);await Load("sidedock.html");
  await Message("{type:'layout',theme:'Sky',scale:1,x:0,y:0,width:1200,height:800,viewWidth:852,viewHeight:680,wallpaper:1}");
  await Message("{type:'open',mode:'music'}");
  await Eval("window.demoWidgets={type:'widgets',music:{title:'歌词外观 · 演示曲目',artist:'PrismGlass 组件预览',cover:'',playing:true},lyrics:{enabled:true,online:true,holdGaps:true,offsetMs:0,status:'歌词效果演示',appearance:PrismLyrics.normalize(PrismLyrics.presets.aurora.style)}};window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:demoWidgets}))");
  await Task.Delay(900);
  await Eval("document.getElementById('lyrics-studio').scrollIntoView({block:'start'})");
  await Task.Delay(250);await Crop("lyrics-settings",104,24,376,648);
  await Eval("for(const d of document.querySelectorAll('#lyrics-studio details'))d.open=false;document.querySelectorAll('#lyrics-studio details')[1].open=true;document.querySelectorAll('#lyrics-studio details')[3].open=true");
  await Task.Delay(200);
  await Eval("document.querySelector('[data-lyric-field=font]').scrollIntoView({block:'start'})");
  await Task.Delay(200);await Crop("lyrics-controls",104,24,376,648);
 }
 static async Task Run(){
  await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"showcase-preview-runtime")));
  view.ZoomFactor=96.0/GetDpiForWindow(form.Handle);
  view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-demo.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
  await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.captureErrors=[];addEventListener('error',e=>captureErrors.push(e.message))");
  await Lyrics();await Music();
  string errors=await Eval("JSON.stringify(captureErrors)");
  if(errors!="\"[]\"")throw new Exception("Preview errors: "+errors);
 }
 [STAThread]static void Main(string[] args){
  SetProcessDPIAware();root=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);Wallpaper();
  Application.EnableVisualStyles();form=new Form{ClientSize=new Size(1200,420),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Location=new Point(-16000,-16000),ShowInTaskbar=false};
  view=new WebView2{Dock=DockStyle.Fill};form.Controls.Add(view);
  form.Shown+=async(s,e)=>{try{await Run();Console.WriteLine("Captured real lyric renderer and music settings.");}catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}finally{view.Dispose();form.Close();}};Application.Run(form);
 }
}
