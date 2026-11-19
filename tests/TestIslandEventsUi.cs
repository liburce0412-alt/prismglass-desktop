using System;using System.IO;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
internal sealed class IslandEventTestForm:Form{protected override bool ShowWithoutActivation{get{return true;}}}
internal static class TestIslandEventsUi {
 static WebView2 view;static Form form;static string root;static string action="";
 static Task<string> Eval(string js){return view.CoreWebView2.ExecuteScriptAsync(js);}
 static Task Send(string js){return Eval("PrismIsland.receive("+js+")");}
 static async Task Check(string js,string label){if(await Eval(js)!="true")throw new Exception(label);File.AppendAllText(Path.Combine(root,"checks.txt"),"PASS "+label+Environment.NewLine);}
 static async Task Run(){
  await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"profile")));view.ZoomFactor=1;
  view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-island.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
  view.CoreWebView2.WebMessageReceived+=(s,e)=>{string message=e.TryGetWebMessageAsString();if(message.StartsWith("action:"))action=message;};
  await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.errors=[];addEventListener('error',e=>errors.push(e.message));");
  view.Source=new Uri("https://prism-island.local/island.html");for(int i=0;i<60;i++){if(await Eval("!!window.PrismIsland")=="true")break;await Task.Delay(100);}
  long sequence=0;
  foreach(string theme in new[]{"Sky","Astro"}){
   await Send("{type:'layout',theme:'"+theme+"',scale:1,x:0,y:0,width:1000,height:328,paused:false}");await Eval("PrismIsland.change('pill')");
   await Send("{type:'island-event',sequence:"+(++sequence)+",kind:'launching',name:'文件资源管理器',icon:'https://prism-island.local/dock-icons/test.png',expiresAt:Date.now()+3000}");await Task.Delay(600);
   await Check("Math.abs(islandState.width-316)<2&&document.querySelector('#island-event-icon').naturalWidth>0&&document.querySelector('#island-detail').textContent==='正在启动'",theme+" icon and spring expansion");
   using(var file=File.Create(Path.Combine(root,theme+"-launch.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
   await Send("{type:'island-event',sequence:"+(++sequence)+",kind:'attention',name:'聊天应用',icon:'https://prism-island.local/dock-icons/test.png',expiresAt:Date.now()+3000}");await Task.Delay(250);
   await Eval("document.querySelector('#handle').click()");await Task.Delay(200);if(action!="action:island-open:"+sequence)throw new Exception("attention click");
   await Send("{type:'preferences',snapshot:{reducedMotion:true}}");await Send("{type:'island-event',sequence:"+(++sequence)+",kind:'launched',name:'测试应用',expiresAt:Date.now()+400}");await Task.Delay(700);
   await Check("islandState.mode==='pill'&&Math.abs(islandState.width-272)<1&&document.querySelector('#island-event-icon').hidden",theme+" expiry and reduced motion");
   await Send("{type:'preferences',snapshot:{reducedMotion:false}}");
  }
  form.ClientSize=new Size(420,328);await Send("{type:'layout',theme:'Sky',scale:1.25,x:0,y:0,width:420,height:328,paused:false}");await Send("{type:'power',available:true,percent:64,plugged:false}");await Send("{type:'power',available:true,percent:64,plugged:true}");await Task.Delay(600);
  await Check("document.querySelector('#island-detail').textContent==='已接通电源'&&islandState.width<=innerWidth", "narrow scaled power event");
  using(var file=File.Create(Path.Combine(root,"power-narrow.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
  await Check("errors.length===0","no page errors");
 }
 [STAThread]static int Main(string[] args){root=Path.GetFullPath(args[0]);Application.EnableVisualStyles();form=new IslandEventTestForm{Text="PrismGlass isolated event check",ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(100,100),ClientSize=new Size(1000,328)};view=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=Color.Transparent};form.Controls.Add(view);int result=0;form.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){File.AppendAllText(Path.Combine(root,"checks.txt"),ex.ToString());result=1;}finally{view.Dispose();form.Close();}};Application.Run(form);return result;}
}
