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
    await Eval("document.body.dataset.screenExpanded='true';document.body.dataset.screenTools='false'");
    await Assert("(()=>{const p=document.querySelector('#pointer-surface').getBoundingClientRect();return p.left===0&&p.top===0&&p.width===innerWidth&&p.height===innerHeight})()","fullscreen layout still fills "+name);
    await Eval("document.querySelector('#screen-tools-toggle').click()");
    await Assert("(()=>{const p=document.querySelector('#screen-tools').getBoundingClientRect();return p.width>0&&p.left>=0&&p.right<=innerWidth&&p.top>=0&&p.bottom<=innerHeight})()","fullscreen controls remain inside "+name);
    await Eval("document.querySelector('#screen-tools-close').click();document.body.dataset.screenExpanded='false'");
    await Eval("document.querySelector('#computer-tab').click();document.querySelector('#appearance-tab').click()");await Capture(name+"-appearance");
    await Assert("document.documentElement.scrollWidth<=innerWidth","appearance stays within "+name);
    await Eval("document.querySelector('#home-tab').click();document.querySelector('#manage-button').click()");await Capture(name+"-connection");
    await Assert("document.activeElement.id==='settings-close'","connection panel receives focus in "+name);
    await Eval("document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true,cancelable:true}))");
    await Assert("document.querySelector('#connection-settings').hidden&&document.activeElement.id==='manage-button'","Escape closes connection panel and returns focus in "+name);
   }
  }
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
  await Assert("__fixture.writes.length===0","rendering and navigation never mutate the computer");
  File.AppendAllText(Path.Combine(output,"ui-checks.txt"),"Completed "+checks+" isolated companion UI checks; PC WebView2 only."+Environment.NewLine);
 }
 [STAThread]static void Main(string[] args){SetProcessDPIAware();root=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);Application.EnableVisualStyles();form=new CompanionUiTestForm{ClientSize=new Size(768,1024),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Location=new Point(-16000,-16000),ShowInTaskbar=false,Opacity=0};view=new WebView2{Dock=DockStyle.Fill};form.Controls.Add(view);form.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){File.AppendAllText(Path.Combine(output,"ui-checks.txt"),ex.ToString());Environment.ExitCode=1;}finally{view.Dispose();form.Close();}};Application.Run(form);}
}
