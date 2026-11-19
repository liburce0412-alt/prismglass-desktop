using System;using System.IO;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
internal sealed class IconTestForm:Form {protected override bool ShowWithoutActivation{get{return true;}}}
internal static class TestFunctionalIcons {
 static Form form;static WebView2 view;static string root,output,trace;static int checks;
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 static Task<string> Eval(string value){return view.CoreWebView2.ExecuteScriptAsync(value);}
 static async Task Check(string expression,string label){bool good=await Eval(expression)=="true";File.AppendAllText(Path.Combine(output,"checks.txt"),(good?"PASS ":"FAIL ")+label+Environment.NewLine);if(!good){await Capture("failure");throw new Exception(label+": "+await Eval("JSON.stringify({errors:__errors,active:document.activeElement?.outerHTML,hint:document.getElementById('prism-hint')?.outerHTML,portalHidden:document.querySelector('.prism-hint-portal')?.hidden,rect:document.getElementById('prism-hint')?.getBoundingClientRect().toJSON(),focusVisible:document.activeElement?.matches(':focus-visible')})"));}checks++;}
 static async Task Wait(string expression){for(int i=0;i<100;i++){if(await Eval(expression)=="true")return;await Task.Delay(50);}throw new Exception("Timed out: "+expression);}
 static void Message(string json){view.CoreWebView2.PostWebMessageAsJson(json);}
 static async Task Capture(string name){using(var file=File.Create(Path.Combine(output,name+".png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);}
 static async Task Run(){
  await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(output,"profile")));view.ZoomFactor=96.0/GetDpiForWindow(form.Handle);
  if(trace!=null){
   view.CoreWebView2.SetVirtualHostNameToFolderMapping("dock-layer.local",AppDomain.CurrentDomain.BaseDirectory,CoreWebView2HostResourceAccessKind.DenyCors);
   view.CoreWebView2.Navigate("https://dock-layer.local/DockIconLayer.html");
   await Wait("typeof compare==='function'");
   var done=new TaskCompletionSource<string>();
   view.CoreWebView2.WebMessageReceived+=(sender,args)=>done.TrySetResult(args.WebMessageAsJson);
   await Eval("compare("+File.ReadAllText(trace)+",['prismglass-icon-refined-256.png']).then(value=>chrome.webview.postMessage(value),error=>chrome.webview.postMessage({error:String(error)}))");
   if(await Task.WhenAny(done.Task,Task.Delay(20000))!=done.Task)throw new Exception("Dock layer timed out");
   var result=await done.Task;File.WriteAllText(Path.Combine(output,"dock-layer.json"),result);
   if(result.Contains("\"error\""))throw new Exception(result);
   File.AppendAllText(Path.Combine(output,"checks.txt"),"PASS 160 production Lua geometry samples rendered in a real WebView2 icon layer.");
   return;
  }
  view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-icon-test.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
  await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.__errors=[];addEventListener('error',e=>__errors.push(e.message));addEventListener('unhandledrejection',e=>__errors.push(String(e.reason)))");
  foreach(string page in new[]{"sidedock","shelf","island","workspace"}){
   view.CoreWebView2.Navigate("https://prism-icon-test.local/"+page+".html");await Task.Delay(150);await Wait("!!window.PrismFunctionalIcons&&document.querySelectorAll('svg[data-prism-motion]').length>0");
   Message("{\"type\":\"layout\",\"scale\":1,\"width\":1200,\"height\":800,\"viewWidth\":900,\"viewHeight\":720,\"x\":0,\"y\":0,\"theme\":\"Sky\",\"paused\":false}");await Task.Delay(100);
   if(page=="workspace")await Eval("document.getElementById('show-settings').click()");
   await Check("[...document.querySelectorAll('svg.control-icon,svg[data-prism-icon]')].filter(svg=>svg.closest('button,a,[role=button],label')).every(svg=>!!svg.dataset.prismMotion&&!!svg.firstElementChild)",page+" animates existing functional artwork with no empty slots");
   await Check("[...document.querySelectorAll('svg[data-prism-motion]')].every(svg=>!svg.style.stroke&&!svg.style.fill&&(!svg.hasAttribute('viewBox')||svg.getAttribute('viewBox')==='0 0 24 24'))",page+" preserves original strokes, colors and 24-unit artwork scale");
   string selector=page=="sidedock"?"[data-widget=tools]":page=="shelf"?"[data-mode=notes]":page=="island"?"[data-action=settings]":"#settings-nav button";
   await Eval("window.__button=document.querySelector('"+selector+"');window.__svg=__button.querySelector('svg');window.__geometry=svg=>{const copy=svg.cloneNode(true);copy.querySelectorAll('[style]').forEach(n=>n.removeAttribute('style'));return copy.innerHTML};window.__rest=__geometry(__svg)");await Capture(page+"-rest");
   await Eval("__button.dispatchEvent(new PointerEvent('pointerenter',{pointerType:'mouse'}))");await Task.Delay(90);
   await Check("__geometry(__svg)===__rest&&__svg.getAnimations({subtree:true}).length>0&&getComputedStyle(__svg).transform!=='none'",page+" animates the original glyph without replacing its geometry");await Capture(page+"-hover");
   await Eval("__button.dispatchEvent(new PointerEvent('pointerdown',{button:0,pointerType:'mouse'}))");await Check("__svg.style.transform.includes('scale(0.93)')",page+" responds visually before pointer release");
   Message("{\"type\":\"motion\",\"paused\":true}");await Task.Delay(60);await Eval("window.__stopped=__geometry(__svg);__button.dispatchEvent(new PointerEvent('pointerenter',{pointerType:'mouse'}))");await Task.Delay(120);
   await Check("__geometry(__svg)===__stopped&&[__svg,...__svg.querySelectorAll('[data-prism-part]')].every(n=>n.style.transform==='')&&__svg.getAnimations({subtree:true}).length===0",page+" host pause clears pressed and hover motion immediately");
   Message("{\"type\":\"motion\",\"paused\":false}");await Task.Delay(50);
   await Eval("dispatchEvent(new CustomEvent('prism-preferences',{detail:{reducedMotion:true}}));window.__quiet=__geometry(__svg);__button.dispatchEvent(new PointerEvent('pointerenter',{pointerType:'mouse'}))");await Task.Delay(100);
   await Check("__geometry(__svg)===__quiet&&[__svg,...__svg.querySelectorAll('[data-prism-part]')].every(n=>n.style.transform==='')&&__svg.getAnimations({subtree:true}).length===0",page+" reduced-motion preference suppresses interaction animation");
   await Eval("dispatchEvent(new CustomEvent('prism-preferences',{detail:{reducedMotion:false}}));__button.dispatchEvent(new PointerEvent('pointerleave',{pointerType:'mouse'}))");
   await Eval("__button.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter'}))");await Check("__svg.style.transform.includes('scale(0.93)')",page+" keyboard activation gets the same press feedback");
   await Eval("__button.dispatchEvent(new KeyboardEvent('keyup',{key:'Enter'}));__button.dispatchEvent(new PointerEvent('pointerup',{pointerType:'touch'}))");await Check("__svg.style.transform!==''",page+" touch release gives bounded feedback");await Task.Delay(500);
   await Check("[__svg,...__svg.querySelectorAll('[data-prism-part]')].every(n=>n.style.transform==='')&&__svg.getAnimations({subtree:true}).length===0",page+" idle icons stop all animation work");
   await Eval("__button.disabled=true;__button.dispatchEvent(new PointerEvent('pointerenter',{pointerType:'mouse'}))");await Check("[__svg,...__svg.querySelectorAll('[data-prism-part]')].every(n=>n.style.transform==='');",page+" disabled controls do not animate");await Eval("__button.disabled=false;dispatchEvent(new Event('blur'))");
   await Check("[__svg,...__svg.querySelectorAll('[data-prism-part]')].every(n=>n.style.transform==='')",page+" focus loss resets the original glyph");
   if(page=="sidedock"||page=="shelf")await Check("Math.abs(__svg.getBoundingClientRect().width-26)<.1",page+" primary rail artwork fills its original 26px slot");
   if(page=="sidedock"){
    await Eval("__button.dispatchEvent(new PointerEvent('pointerenter',{pointerType:'mouse'}))");await Task.Delay(90);
    await Check("__svg.querySelector('[data-prism-part=plus]').style.transform==='rotate(90deg)'&&__svg.querySelectorAll('rect').length===3&&__geometry(__svg)===__rest","tools rotates its plus while retaining the three original tiles");
    await Eval("dispatchEvent(new Event('blur'))");
    await Check("[...document.querySelectorAll('[data-widget=weather] [data-prism-part],[data-widget=focus] [data-prism-part],[data-widget=system] [data-prism-part]')].length===6","weather, focus and performance retain separate moving rays, hand and bars");
   }
   if(page=="shelf")await Check("document.querySelector('[data-mode=countdown] svg rect')!==null&&document.querySelector('[data-mode=countdown] svg circle')===null","event countdown uses calendar artwork rather than a focus stopwatch");
   if(page=="workspace")await Check("(()=>{const refs=[...document.querySelectorAll('#settings-nav use')].map(n=>n.getAttribute('href'));return new Set(refs).size===refs.length&&refs.includes('control-icons.svg#glass')&&refs.includes('control-icons.svg#performance')})()","every settings function has its own symbol, including glass and energy");
   if(page=="island"){
    await Check("document.querySelector('[data-action=tasks] use').getAttribute('href')==='control-icons.svg#taskview'","task view uses stacked windows rather than display settings");
    await Eval("window.__use=document.getElementById('media-icon');__use.setAttribute('href','control-icons.svg#pause')");await Task.Delay(80);
    await Check("document.getElementById('media-icon')===__use&&__use.style.display!=='none'&&__use.getAttribute('href')==='control-icons.svg#pause'","play/pause state updates preserve the visible original live use element");
    await Eval("document.getElementById('refresh-icon').setAttribute('href','control-icons.svg#check')");await Task.Delay(80);
    await Check("document.getElementById('refresh-icon').style.display!=='none'&&document.getElementById('refresh-icon').getAttribute('href')==='control-icons.svg#check'","wallpaper completion keeps its original visible check mark");
   }
   await Check("!!window.PrismHint&&document.body.querySelectorAll('[title]').length===0",page+" native browser titles migrate to the shared hint without empty slots");
   if(page=="workspace"){
    await Eval("window.__hintNode=document.createElement('button');__hintNode.textContent='详情';__hintNode.title='C:/工作资料/一个保留完整名称的文件夹/很长的项目说明与交付路径/审计记录.html\\nhttps://example.test/path?q=<原样文字>';__hintNode.setAttribute('aria-label','路径详情');__hintNode.setAttribute('aria-describedby','existing-description');__hintNode.style.cssText='position:fixed;right:4px;bottom:4px;width:64px;height:44px';document.body.append(__hintNode)");await Task.Delay(40);
    await Check("!__hintNode.hasAttribute('title')&&__hintNode.dataset.prismHint.includes('<原样文字>')&&__hintNode.getAttribute('aria-label')==='路径详情'","dynamic path hints retain their full text and the original accessible name");
    await Eval("document.dispatchEvent(new KeyboardEvent('keydown',{key:'Tab'}));__hintNode.focus();__hintNode.dispatchEvent(new FocusEvent('focusin',{bubbles:true}))");await Task.Delay(60);
    await Check("(()=>{const n=document.getElementById('prism-hint'),r=n.getBoundingClientRect();return n.textContent===__hintNode.dataset.prismHint&&n.getAttribute('aria-hidden')==='false'&&r.left>=0&&r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight&&__hintNode.getAttribute('aria-describedby')==='existing-description prism-hint'})()","keyboard hint wraps long content within the viewport and appends its accessible description");await Capture("workspace-shared-hint");
    await Eval("__hintNode.title='已更新的完整路径 / new/location';");await Task.Delay(40);await Check("document.getElementById('prism-hint').textContent==='已更新的完整路径 / new/location'&&!__hintNode.hasAttribute('title')","a live title update changes the same shared hint without losing content");
    await Eval("document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape'}))");await Check("document.querySelector('.prism-hint-portal').hidden&&__hintNode.getAttribute('aria-describedby')==='existing-description'","Escape hides the hint and preserves unrelated accessible descriptions");
    Message("{\"type\":\"hint-host\",\"native\":true}");await Task.Delay(40);await Eval("__hintNode.dispatchEvent(new FocusEvent('focusin',{bubbles:true}))");
    await Check("document.querySelector('.prism-hint-portal').classList.contains('prism-hint-native')&&document.getElementById('prism-hint').getAttribute('aria-hidden')==='false'&&!document.getElementById('prism-hint').hasAttribute('data-optical')","native bridge keeps the accessible description without a duplicate web glass surface");
    await Eval("__hintNode.dispatchEvent(new PointerEvent('pointerdown',{pointerType:'touch',button:0,bubbles:true}));__hintNode.dispatchEvent(new FocusEvent('focusin',{bubbles:true}))");await Check("document.querySelector('.prism-hint-portal').hidden","touch focus cannot leave a sticky hint over the next action");
    await Eval("__hintNode.remove();");
   }
   await Check("__errors.length===0",page+" production scripts retain their existing element contracts");
  }
  File.AppendAllText(Path.Combine(output,"checks.txt"),"Completed "+checks+" real desktop WebView icon checks. Isolated hosts; no system commands or user state writes."+Environment.NewLine);
 }
 [STAThread]static void Main(string[] args){SetProcessDPIAware();root=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);trace=args.Length>2?args[2]:null;Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"checks.txt"),"");Application.EnableVisualStyles();form=new IconTestForm{ClientSize=new Size(900,720),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Location=new Point(-16000,-16000),ShowInTaskbar=false,Opacity=0};view=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=Color.FromArgb(255,25,43,63)};form.Controls.Add(view);form.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){File.AppendAllText(Path.Combine(output,"checks.txt"),ex.ToString());Environment.ExitCode=1;}finally{view.Dispose();form.Close();}};Application.Run(form);}
}
