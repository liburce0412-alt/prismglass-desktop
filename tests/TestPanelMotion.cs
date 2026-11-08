using System;using System.IO;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
internal sealed class PanelMotionForm:Form {protected override bool ShowWithoutActivation{get{return true;}}}
internal static class TestPanelMotion {
 static Form form;static WebView2 view;static string root,output;
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr handle);
 static Task<string> Eval(string source){return view.CoreWebView2.ExecuteScriptAsync(source);}
 static Task Message(string value){return Eval("chrome.webview.dispatchEvent(new MessageEvent('message',{data:"+value+"}))");}
 static async Task Page(string name,string trace,string toggle,string endpoint,int width,int height){
  form.ClientSize=new Size(width,height);var loaded=new TaskCompletionSource<bool>();EventHandler<CoreWebView2NavigationCompletedEventArgs> completed=(s,e)=>loaded.TrySetResult(e.IsSuccess);view.CoreWebView2.NavigationCompleted+=completed;
  view.Source=new Uri("https://prism-motion.local/"+name+".html");if(!await loaded.Task)throw new Exception("Navigation failed: "+name);view.CoreWebView2.NavigationCompleted-=completed;
  await Message("{type:'layout',theme:'Sky',scale:1,x:0,y:0,width:"+width+",height:"+height+",viewWidth:"+width+",viewHeight:"+height+",sceneWidth:1600,sceneHeight:900,originX:0,originY:0,paused:false,wallpaper:1}");
  await Task.Delay(450);
  await Eval("window."+trace+"=[];window.__reads=0;window.__shapes=0;window.__clicks=0;(function run(){"+toggle+";if(++__clicks<6)setTimeout(run,80)})();");
  await Task.Delay(1250);
  var result=await Eval("(()=>{const rows=window."+trace+",gaps=rows.slice(1).map((r,i)=>r.time-rows[i].time).sort((a,b)=>a-b);return {page:'"+name+"',samples:rows.length,spanMs:rows.length?rows.at(-1).time-rows[0].time:0,medianMs:gaps[Math.floor(gaps.length*.5)],p95Ms:gaps[Math.floor(gaps.length*.95)],maxMs:gaps.at(-1),selectionReads:__reads,shapeMessages:__shapes,endpoint:"+endpoint+",errors:__errors,trace:rows}})()");
  File.AppendAllText(output,result+Environment.NewLine);
  if(await Eval("__errors.length===0&&window."+trace+".length>5&&("+endpoint+")")!="true")throw new Exception("Motion scenario failed: "+name);
 }
 static async Task Run(){
  await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"runtime")));view.ZoomFactor=96.0/GetDpiForWindow(form.Handle);
  view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-motion.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
  await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.__errors=[];window.__reads=0;window.__shapes=0;addEventListener('error',e=>__errors.push(e.message));const read=Element.prototype.getBoundingClientRect;Element.prototype.getBoundingClientRect=function(){if(this.matches('.rail-item,#triggers button'))__reads++;return read.call(this)};const post=chrome.webview.postMessage.bind(chrome.webview);chrome.webview.postMessage=x=>{if(typeof x==='string'&&(x.startsWith('shape:')||x.includes('\"action\":\"shape\"')||x.startsWith('outline:'))) __shapes++;post(x)};");
  await Page("sidedock","sideTrace","document.querySelector('[data-widget=weather]').click()","Number(document.querySelector('#detail').style.width.replace('px',''))<1",852,680);
  await Page("shelf","shelfTrace","document.querySelector('[data-mode=quota]').click()","document.querySelector('#panel').getAttribute('aria-hidden')==='true'&&Number(document.querySelector('#panel').style.opacity)<.001",512,620);
  await Page("island","islandTrace","PrismIsland.change(__clicks%2===0?'open':'pill')","islandState.mode==='pill'&&Math.abs(islandState.height-48)<.2",1100,328);
 }
 [STAThread]static int Main(string[] args){
  root=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);SetProcessDPIAware();Application.EnableVisualStyles();form=new PanelMotionForm{Text="PrismGlass · isolated motion check",StartPosition=FormStartPosition.Manual,Location=new Point(80,80),ClientSize=new Size(852,680),ShowInTaskbar=false};view=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=Color.Transparent};form.Controls.Add(view);int exit=0;
  form.Shown+=async(s,e)=>{try{await Run();}catch(Exception error){File.AppendAllText(output,"ERROR "+error+Environment.NewLine);exit=1;}finally{view.Dispose();form.Close();}};Application.Run(form);return exit;
 }
}
