using System;using System.IO;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
internal sealed class CompanionUiTestForm:Form {protected override bool ShowWithoutActivation{get{return true;}}}
internal static class TestCompanionUi {
 static Form form;static WebView2 view;static string root,output;static int checks;
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 static Task<string> Eval(string script){return view.CoreWebView2.ExecuteScriptAsync(script);}
 static void Check(bool value,string name){File.AppendAllText(Path.Combine(output,"ui-checks.txt"),(value?"PASS ":"FAIL ")+name+Environment.NewLine);if(!value)throw new Exception(name);checks++;}
 static async Task Assert(string script,string name){Check(await Eval(script)=="true",name);}
 static async Task Wait(string script){for(int i=0;i<100;i++){if(await Eval(script)=="true")return;await Task.Delay(80);}throw new Exception("UI wait timed out: "+script);}
 static async Task Capture(string name){await Task.Delay(200);using(var file=File.Create(Path.Combine(output,name+".png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);}
 static async Task CheckPaintedFrameBottom(string name){
  string[] parts=(await Eval("(()=>{const v=document.querySelector('#screen-video'),r=v.getBoundingClientRect(),h=Math.min(r.height,r.width*v.videoHeight/v.videoWidth);return [r.left+Math.min(r.width,r.height*v.videoWidth/v.videoHeight)/2,r.top,r.bottom,h]})()")).Trim('[',']').Split(',');
  double x=double.Parse(parts[0],System.Globalization.CultureInfo.InvariantCulture),top=double.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture),edge=double.Parse(parts[2],System.Globalization.CultureInfo.InvariantCulture),expectedHeight=double.Parse(parts[3],System.Globalization.CultureInfo.InvariantCulture);int paintedBottom=-1,paintedTop=-1;
  using(var bitmap=new Bitmap(Path.Combine(output,"keyboard-"+name+".png"))){
   // Find the actual white border in the captured video, rather than only its element box.
   for(int y=Math.Min(bitmap.Height-1,(int)Math.Ceiling(edge)-1);y>=(int)Math.Ceiling(top);y--){Color pixel=bitmap.GetPixel((int)x,y);if(pixel.R>180&&pixel.G>200&&pixel.B>215){if(paintedBottom<0)paintedBottom=y+1;paintedTop=y;}}
  }
  File.AppendAllText(Path.Combine(output,"layout-metrics.jsonl"),"{\"keyboardFrame\":\""+name+"\",\"paintedBottom\":"+paintedBottom+",\"paintedHeight\":"+(paintedBottom-paintedTop)+",\"layoutBottom\":"+edge.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}"+Environment.NewLine);
  Check(paintedBottom>=0&&Math.Abs(paintedBottom-edge)<2,"painted video bottom aligns with its keyboard layout edge in "+name);
  Check(paintedTop>=0&&Math.Abs(paintedBottom-paintedTop-expectedHeight)<2,"painted frame height preserves the full contain picture in "+name);
 }
 static async Task Run(){
  await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"test-runtime")));view.ZoomFactor=96.0/GetDpiForWindow(form.Handle);
  view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-companion-test.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
  await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(File.ReadAllText(Path.Combine(root,"CompanionUiFixture.js")));
  view.Source=new Uri("https://prism-companion-test.local/index.html");await Wait("!!document.querySelector('#connection-status')&&document.querySelector('#connection-status').textContent==='已连接电脑'");
  await Assert("__errors.length===0","real companion scripts render without JavaScript errors");
  foreach(var size in new[]{new Size(768,1024),new Size(1024,768)}){
   form.ClientSize=size;await Task.Delay(150);
   foreach(string theme in new[]{"Sky","Astro"}){
    await Eval("__fixture.state.theme='"+theme+"';__fixture.state.settings.theme='"+theme+"';document.querySelector('#reconnect').click()");await Wait("document.querySelector('#connection-status').textContent==='已连接电脑'");
    string name=theme.ToLowerInvariant()+"-"+size.Width+"x"+size.Height;
    await Eval("document.querySelector('#home-tab').click()");await Capture(name+"-home");
    await Assert("document.documentElement.scrollWidth<=innerWidth","home stays within "+name);
    await Assert("document.querySelector('#touch-bar').getBoundingClientRect().bottom<=innerHeight+1","navigation fits "+name);
    await Assert("document.querySelector('#home-brief').getBoundingClientRect().top>=document.querySelector('#music-card').getBoundingClientRect().bottom","daily brief never overlaps music controls in "+name);
    await Assert("document.body.dataset.optics==='ready'&&PrismGlassOptics.inspect().contextCreations===1&&PrismGlassOptics.inspect().regions<=3","shared optical glass remains active in "+name);
    await Eval("document.querySelector('#pointer-tab').click()");await Wait("document.querySelector('#screen-monitor').options.length===1");await Capture(name+"-pointer");
    await Assert("(()=>{const p=document.querySelector('#pointer-surface').getBoundingClientRect(),m=document.querySelector('#main-content').getBoundingClientRect();return p.height>=240&&p.top>=m.top&&p.bottom<=m.bottom+1})()","touch surface remains fully visible in "+name);
    await Assert("['screen-start','pointer-enable'].every(id=>{const p=document.getElementById(id).getBoundingClientRect(),m=document.querySelector('#screen-tools').getBoundingClientRect();return p.top>=m.top&&p.bottom<=m.bottom&&p.width>=44&&p.height>=44})","primary screen and touch actions remain reachable in "+name);
    File.AppendAllText(Path.Combine(output,"layout-metrics.jsonl"),await Eval("({size:{width:innerWidth,height:innerHeight},theme:document.body.dataset.theme,surface:document.querySelector('#pointer-surface').getBoundingClientRect().toJSON(),optics:PrismGlassOptics.inspect()})")+Environment.NewLine);
    await Eval("document.body.dataset.screenExpanded='true';document.body.dataset.screenTools='false';PrismCompanionScreen.layout()");
    await Assert("(()=>{const p=document.querySelector('#pointer-surface').getBoundingClientRect();return p.left===0&&p.top===0&&p.width===innerWidth&&p.height===innerHeight})()","fullscreen frame uses the entire viewport with tools closed in "+name);
    await Assert("(()=>{const a=document.querySelector('#screen-keyboard-toggle').getBoundingClientRect(),b=document.querySelector('#screen-tools-toggle').getBoundingClientRect();return b.left-a.right>=8&&a.width>=44&&a.height>=44&&b.width>=44&&b.height>=44})()","quick controls keep touch spacing without flex gap in "+name);
    await Eval("document.querySelector('#screen-tools-toggle').click()");
    await Assert("(()=>{const p=document.querySelector('#screen-tools').getBoundingClientRect();return p.width>0&&p.left>=0&&p.right<=innerWidth&&p.top>=0&&p.bottom<=innerHeight})()","fullscreen controls remain inside "+name);
    await Assert("document.querySelector('#pointer-surface').getBoundingClientRect().width===innerWidth&&document.querySelector('#pointer-surface').getBoundingClientRect().height===innerHeight","opened tools float without shrinking the frame in "+name);
    await Eval("document.querySelector('#screen-tools-close').click()");
    await Assert("(()=>{const s=document.querySelector('#pointer-surface');return getComputedStyle(document.querySelector('#screen-tools')).display==='none'&&s.getBoundingClientRect().width===innerWidth&&s.contains(document.elementFromPoint(innerWidth-24,innerHeight/2))})()","closing tools restores unobstructed touch across the full frame in "+name);
    await Eval("document.body.dataset.screenExpanded='false';PrismCompanionScreen.layout()");
    await Eval("document.querySelector('#computer-tab').click();document.querySelector('#appearance-tab').click()");await Capture(name+"-appearance");
    await Assert("document.documentElement.scrollWidth<=innerWidth","appearance stays within "+name);
    await Eval("document.querySelector('#home-tab').click();document.querySelector('#manage-button').click()");await Capture(name+"-connection");
    await Assert("document.activeElement.id==='settings-close'","connection panel receives focus in "+name);
    await Eval("document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true,cancelable:true}))");
    await Assert("document.querySelector('#connection-settings').hidden&&document.activeElement.id==='manage-button'","Escape closes connection panel and returns focus in "+name);
   }
  }
  await Assert("__fixture.writes.length===0","rendering and navigation never mutate the computer");
  await KeyboardLayouts();
  form.ClientSize=new Size(390,844);await Eval("document.querySelector('#home-tab').click()");await Capture("compact-home");
  await Assert("document.documentElement.scrollWidth<=innerWidth","compact view does not introduce horizontal scrolling");
  form.ClientSize=new Size(1024,768);
  await Eval("__fixture.mode='unpaired';document.querySelector('#reconnect').click()");await Wait("document.querySelector('#connection-status').textContent==='等待配对'");await Capture("unpaired");
  await Eval("__fixture.mode='offline';document.querySelector('#reconnect').click()");await Wait("document.querySelector('#connection-status').textContent==='连接中断'");await Capture("offline");
  await Assert("!document.querySelector('#pair-retry').hidden&&document.querySelector('#music-card').hidden&&document.querySelector('#pair-form').hidden","offline state exposes retry and conceals private data");
  await Eval("__fixture.mode='connected';document.querySelector('#pair-retry').click()");await Wait("document.querySelector('#connection-status').textContent==='已连接电脑'");
  await Assert("document.querySelector('#pair-panel').hidden","retry restores the remembered session");
  await Eval("__fixture.mode='locked';document.querySelector('#reconnect').click()");await Wait("document.querySelector('#connection-status').textContent==='电脑已锁定'");await Capture("locked");
  await Assert("document.querySelector('#pair-heading').textContent==='电脑已锁定'&&document.querySelector('#music-card').hidden","locked state explains the boundary and hides private content");
  await Assert("__errors.length===0","tested states have no JavaScript errors");
  await Assert("__fixture.writes.every(w=>['/api/pointer/begin','/api/pointer','/api/pointer/end'].includes(w.url))","keyboard layout uses only isolated control fixtures and never starts capture");
  File.AppendAllText(Path.Combine(output,"ui-checks.txt"),"Completed "+checks+" isolated companion UI checks; PC WebView2 only."+Environment.NewLine);
 }
 static async Task KeyboardLayouts(){
  await Eval("__fixture.stubPointer=true;__fixture.extensionAvailable=true;document.querySelector('#pointer-tab').click()");await Wait("document.querySelector('#screen-monitor').options.length===1");
  await Eval("document.querySelector('#pointer-enable').click()");await Wait("!document.querySelector('#screen-keyboard-toggle').disabled");
  foreach(var sample in new[]{new[]{1024,768,408,48},new[]{768,1024,664,48},new[]{1024,768,380,0}}){
   form.ClientSize=new Size(sample[0],sample[1]);await Task.Delay(100);
   foreach(var frame in new[]{new Size(1280,720),new Size(1024,768)}){
    string mode=frame.Width==1280?"mirror":"extend",name=mode+"-"+sample[0]+"x"+sample[1]+"-v"+sample[2]+"-"+frame.Width+"x"+frame.Height;
    await Eval("document.querySelector('#screen-mode-"+mode+"').click()");await Wait("document.querySelector('#screen-mode-"+mode+"').getAttribute('aria-pressed')==='true'&&!document.querySelector('#screen-start').disabled");
    await Eval("(()=>{if(window.__screenStream)__screenStream.getTracks().forEach(t=>t.stop());const c=document.createElement('canvas');c.width="+frame.Width+";c.height="+frame.Height+";const x=c.getContext('2d');x.fillStyle='#2d5078';x.fillRect(0,0,c.width,c.height);x.strokeStyle='#d9efff';x.lineWidth=12;x.strokeRect(6,6,c.width-12,c.height-12);x.beginPath();x.arc(c.width/2,c.height/2,100,0,Math.PI*2);x.stroke();x.fillStyle='#fff';x.font='42px sans-serif';x.fillText('Computer frame · "+frame.Width+" × "+frame.Height+"',35,70);window.__screenStream=c.captureStream(1);const v=document.querySelector('#screen-video');v.srcObject=__screenStream;v.hidden=false;v.play();document.querySelector('#pointer-surface').dataset.screen='true';document.body.dataset.screenExpanded='true';document.body.dataset.screenTools='false';PrismCompanionScreen.layout()})()");
    await Wait("document.querySelector('#screen-video').videoWidth==="+frame.Width);
    await Assert("(()=>{const r=document.querySelector('#screen-video').getBoundingClientRect();return r.left===0&&r.top===0&&r.width===innerWidth&&r.height===innerHeight})()","playing video uses the whole viewport in "+name);
    if(mode=="mirror")await Assert("(()=>{const v=document.querySelector('#screen-video'),r=v.getBoundingClientRect(),p=document.querySelector('.screen-shortcuts').getBoundingClientRect(),h=Math.min(r.height,r.width*v.videoHeight/v.videoWidth);return p.top>=r.top+h&&p.bottom<=r.bottom})()","mirror shortcuts occupy the real lower letterbox in "+name);
    else await Assert("document.body.dataset.screenControls==='top'","extension keeps floating controls without adding letterboxes in "+name);
    await Capture("screen-"+name);
    await Eval("document.querySelector('#screen-tools-toggle').click();document.querySelector('#screen-keyboard-toggle').click();document.querySelector('#screen-text').value='中文草稿 · 保持光标';window.__viewportOverride={width:"+sample[0]+",height:"+sample[2]+",offsetTop:"+sample[3]+",offsetLeft:0};__viewport.dispatchEvent(new Event('resize'));__viewport.dispatchEvent(new Event('scroll'))");
    await Assert("document.body.dataset.screenTools==='false'&&document.activeElement.id==='screen-text'&&document.querySelector('#screen-text').value==='中文草稿 · 保持光标'","opening keyboard closes heavy tools and preserves the focused draft in "+name);
    await Assert("(()=>{const p=document.querySelector('#pointer-page').getBoundingClientRect(),b=document.querySelector('#screen-keyboard-bar').getBoundingClientRect(),i=document.querySelector('#screen-text').getBoundingClientRect(),s=document.querySelector('#pointer-surface').getBoundingClientRect(),v=visualViewport,side=document.body.dataset.screenInput==='side';return p.top===v.offsetTop&&p.width===v.width&&p.height===v.height&&Math.abs(b.bottom-(v.offsetTop+v.height))<1&&i.top>=b.top&&i.bottom<=b.bottom&&s.height>150&&(side?s.right<=b.left&&Math.abs(s.bottom-b.bottom)<1:s.bottom<=b.top)})()","input meets the keyboard edge without overlapping the picture in "+name);
    await Assert("(()=>{const b=document.querySelector('#screen-keyboard-bar').getBoundingClientRect(),s=getComputedStyle(document.querySelector('#keyboard-status'));return b.height>=55&&b.height<=56&&s.position==='absolute'&&s.display!=='none'})()","normal input preview stays thin and its status remains accessible in "+name);
    await Assert("(()=>{const v=document.querySelector('#screen-video'),s=v.getBoundingClientRect(),c=getComputedStyle(v),side=document.body.dataset.screenInput==='side',expectedSide=visualViewport.width-visualViewport.height*v.videoWidth/v.videoHeight>=260;return v.videoWidth==="+frame.Width+"&&v.videoHeight==="+frame.Height+"&&c.objectFit==='contain'&&side===expectedSide&&(side?s.height===visualViewport.height&&s.width>=visualViewport.height*v.videoWidth/v.videoHeight:s.width===visualViewport.width)})()","natural side space preserves full video height or falls back to a full width bottom bar in "+name);
    await Capture("keyboard-"+name);
    await CheckPaintedFrameBottom(name);
    await Eval("document.querySelector('#screen-tools-toggle').click();window.__statusBefore=document.querySelector('#keyboard-status').textContent;document.querySelector('#keyboard-status').textContent='未确认发送，请查看电脑后再操作。';document.querySelector('#keyboard-status').dataset.notice='true';PrismCompanionScreen.layout()");
    await Assert("(()=>{const t=document.querySelector('#screen-tools').getBoundingClientRect(),b=document.querySelector('#screen-keyboard-bar').getBoundingClientRect(),n=document.querySelector('#keyboard-status').getBoundingClientRect(),s=document.querySelector('#screen-video').getBoundingClientRect(),side=document.body.dataset.screenInput==='side';return t.width>0&&t.bottom<=b.top&&n.height>1&&n.top>=b.top&&n.bottom<=b.bottom&&(!side||t.left>=s.right)&&document.querySelector('#screen-text').value==='中文草稿 · 保持光标'})()","open tools cannot cover the draft or visible error and stay beside side input in "+name);
    await Assert("(()=>{const t=document.querySelector('#screen-tools');t.scrollTop=t.scrollHeight;const scrolled=t.scrollTop>0;t.scrollTop=0;return scrolled&&document.querySelector('#screen-tools-close').getBoundingClientRect().top>=t.getBoundingClientRect().top})()","keyboard tools scroll and their close control remains reachable in "+name);
    await Capture("keyboard-tools-"+name);
    await Eval("document.querySelector('#keyboard-status').textContent=window.__statusBefore;document.querySelector('#keyboard-status').dataset.notice='false';document.querySelector('#screen-tools-close').click();document.querySelector('#screen-keyboard-close').click();window.__viewportOverride=null;__viewport.dispatchEvent(new Event('resize'))");
    await Assert("(()=>{const r=document.querySelector('#pointer-surface').getBoundingClientRect();return document.querySelector('#screen-keyboard-bar').hidden&&document.activeElement.id==='screen-keyboard-toggle'&&r.width===innerWidth&&r.height===innerHeight})()","closing keyboard restores the whole frame and returns focus in "+name);
    await Assert("getComputedStyle(document.querySelector('#screen-video')).objectPosition==='0% 0%'","closing keyboard restores top aligned video in "+name);
   }
  }
  await Eval("__screenStream.getTracks().forEach(t=>t.stop());document.querySelector('#screen-video').srcObject=null;document.querySelector('#screen-video').hidden=true;document.querySelector('#pointer-surface').dataset.screen='false';document.querySelector('#pointer-stop').click();document.body.dataset.screenExpanded='false';PrismCompanionScreen.layout()");
 }
 [STAThread]static void Main(string[] args){SetProcessDPIAware();root=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);Application.EnableVisualStyles();form=new CompanionUiTestForm{ClientSize=new Size(768,1024),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Location=new Point(-16000,-16000),ShowInTaskbar=false,Opacity=0};view=new WebView2{Dock=DockStyle.Fill};form.Controls.Add(view);form.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){File.AppendAllText(Path.Combine(output,"ui-checks.txt"),ex.ToString());Environment.ExitCode=1;}finally{view.Dispose();form.Close();}};Application.Run(form);}
}
