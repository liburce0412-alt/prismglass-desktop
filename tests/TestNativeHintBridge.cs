using System;using System.IO;using System.Drawing;using System.Threading.Tasks;using System.Runtime.InteropServices;using System.Windows.Forms;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
// Optical capture is unrelated to the message bridge. GlassWindow's real registration and DWM setup run unchanged.
internal sealed class OpticalBackdrop{}
internal static class LiveGlassBackdrop{public static OpticalBackdrop Attach(Func<Form> owner,WebView2 view){return null;}}
internal static class TestNativeHintBridge{
 static Form form;static WebView2 view;static string output;static int checks;
 [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 static Task<string> Eval(string script){return view.CoreWebView2.ExecuteScriptAsync(script);}
 static void Check(bool good,string name){File.AppendAllText(Path.Combine(output,"checks.txt"),(good?"PASS ":"FAIL ")+name+Environment.NewLine);if(!good)throw new Exception(name);checks++;}
 static async Task Wait(string script){for(int i=0;i<100;i++){if(await Eval(script)=="true")return;await Task.Delay(40);}throw new Exception(script);}
 static async Task ShowHint(){await Eval("document.dispatchEvent(new KeyboardEvent('keydown',{key:'Tab'}));document.getElementById('target').focus();document.getElementById('target').dispatchEvent(new FocusEvent('focusin',{bubbles:true}))");await Task.Delay(80);}
 static async Task Run(string root){
  await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"profile")));view.ZoomFactor=96.0/GetDpiForWindow(form.Handle);
  view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-workspace.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
  view.CoreWebView2.SetVirtualHostNameToFolderMapping("untrusted-hint.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
  await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.__acks=0;window.__errors=[];chrome.webview.addEventListener('message',e=>{if(e.data?.type==='hint-host'&&e.data.native)__acks++});addEventListener('error',e=>__errors.push(e.message))");
  view.Source=new Uri("https://prism-workspace.local/workspace.html");await Wait("window.__acks===1&&!!window.PrismHint");
  Check(await Eval("!document.getElementById('target').hasAttribute('title')&&document.getElementById('target').dataset.prismHint.includes('完整文件名')")=="true","production GlassWindow hook receives real hint:ready and acknowledges the trusted page");
  form.Activate();view.Focus();await ShowHint();Rectangle anchor;string caption;
  bool shown=NativeHintHost.Hover(Point.Empty,out anchor,out caption);var viewport=new Rectangle(view.PointToScreen(Point.Empty),view.ClientSize);
  Check(shown&&form.ContainsFocus&&viewport.Contains(anchor)&&anchor.Width>100&&anchor.Height>20&&caption.Contains("完整文件名"),"real page title crosses WebView message registration and yields a valid focused native target");
  Check(await Eval("document.querySelector('.prism-hint-portal').classList.contains('prism-hint-native')&&document.getElementById('target').getAttribute('aria-describedby').includes('prism-hint')")=="true","native presentation keeps its accessible description without duplicate web glass");
  await Eval("document.getElementById('target').title='C:/工作资料/更新后的完整路径.html'");await Task.Delay(80);
  Check(NativeHintHost.Hover(Point.Empty,out anchor,out caption)&&caption=="C:/工作资料/更新后的完整路径.html","dynamic title updates reach the same native hint state");
  await Eval("document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape'}))");await Task.Delay(80);
  Check(!NativeHintHost.Hover(Point.Empty,out anchor,out caption),"real hint:hide clears the native target on Escape");
  await ShowHint();await Eval("chrome.webview.postMessage('hint:'+JSON.stringify({text:'outside',x:99999,y:0,w:20,h:20,viewportWidth:innerWidth,viewportHeight:innerHeight,keyboard:true}))");await Task.Delay(80);
  Check(!NativeHintHost.Hover(Point.Empty,out anchor,out caption),"a real malformed message cannot draw a hint beyond the owner viewport");
  await ShowHint();form.Hide();Check(!NativeHintHost.Hover(Point.Empty,out anchor,out caption),"hiding the real owner clears its native hint immediately");form.Show();form.Activate();view.Focus();
  view.Source=new Uri("https://untrusted-hint.local/workspace.html");await Wait("!!window.PrismHint&&document.readyState==='complete'");await Task.Delay(80);await ShowHint();
  Check(await Eval("window.__acks===0")=="true"&&!NativeHintHost.Hover(Point.Empty,out anchor,out caption),"an untrusted page receives no native hint acknowledgement or target");
  Check(await Eval("__errors.length===0")=="true","trusted and untrusted bridge paths produce no browser errors");
  File.AppendAllText(Path.Combine(output,"checks.txt"),"Completed "+checks+" real WebView/native hint bridge checks. Production GlassWindow and NativeHintHost; optical backdrop capture stubbed because glass composition is tested separately."+Environment.NewLine);
 }
 [STAThread]static void Main(string[] args){SetProcessDPIAware();output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"checks.txt"),"");string root=Path.GetFullPath(args[0]);File.WriteAllText(Path.Combine(root,"workspace.html"),"<!doctype html><html><head><meta charset=\"utf-8\"><link rel=\"stylesheet\" href=\"prism-hint.css\"><style>body{background:#19344c;color:white;font:16px 'Microsoft YaHei UI'}#target{position:absolute;left:36px;top:48px;width:220px;height:48px}</style></head><body><button id=\"target\" title=\"查看工作资料&#10;C:/项目/完整文件名.html\">查看详情</button><script src=\"prism-hint.js\"></script></body></html>");var previous=GetForegroundWindow();Application.EnableVisualStyles();form=new Form{ClientSize=new Size(640,300),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedToolWindow,Text="PrismGlass · 提示集成短测",ShowInTaskbar=false};view=new WebView2{Dock=DockStyle.Fill};form.Controls.Add(view);GlassWindow.Hint=NativeHintHost.Receive;GlassWindow.Configure(form,view);form.Shown+=async(s,e)=>{try{await Run(root);}catch(Exception ex){File.AppendAllText(Path.Combine(output,"checks.txt"),ex.ToString());Environment.ExitCode=1;}finally{bool restore=GetForegroundWindow()==form.Handle;form.Close();if(restore&&previous!=IntPtr.Zero)SetForegroundWindow(previous);}};Application.Run(form);}
}
