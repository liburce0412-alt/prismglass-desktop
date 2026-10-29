using System;using System.IO;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
internal sealed class VisibilityTestForm : Form {
 readonly WebView2 view=new WebView2();readonly string profile,log;int checks;
 protected override void SetVisibleCore(bool value){view.Visible=value;base.SetVisibleCore(value);}
 protected override bool ShowWithoutActivation{get{return true;}}
 public VisibilityTestForm(string directory,string output){profile=directory;log=output;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Bounds=new Rectangle(-16000,-16000,600,400);Opacity=0;view.Dock=DockStyle.Fill;Controls.Add(view);WebViewVisibility.Track(view);Shown+=Run;}
 void Check(bool value,string name){File.AppendAllText(log,(value?"PASS ":"FAIL ")+name+Environment.NewLine);if(!value)throw new Exception(name);checks++;}
 async void Run(object sender,EventArgs args){
  try{
   await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,profile));var core=view.CoreWebView2;var loaded=new TaskCompletionSource<bool>();core.NavigationCompleted+=(s,e)=>loaded.TrySetResult(e.IsSuccess);core.NavigateToString("<!doctype html><html><body><textarea id='draft'></textarea><script>window.received=[];chrome.webview.addEventListener('message',e=>received.push(e.data));</script></body></html>");Check(await loaded.Task,"fixture page loads");await core.ExecuteScriptAsync("document.querySelector('#draft').value='unsaved draft'");
   Hide();await Task.Delay(300);Show();await Task.Delay(1300);Check(!core.IsSuspended,"brief hiding never suspends a reopened view");
   Hide();for(int i=0;i<50&&!core.IsSuspended;i++)await Task.Delay(100);Check(core.IsSuspended,"persistently hidden WebView enters suspended state");
   var pixels=new WebViewPixelScale(view,()=>{});Check(!await pixels.SyncAsync()&&core.IsSuspended,"hidden pixel calibration does not wake the suspended renderer");
   core.PostWebMessageAsJson("{\"saved\":true}");Show();await Task.Delay(300);Check(!core.IsSuspended,"showing the window resumes its existing renderer");
   Check(await core.ExecuteScriptAsync("document.querySelector('#draft').value==='unsaved draft'&&received.length===1&&received[0].saved") == "true","draft and asynchronous completion survive suspension");
   for(int i=0;i<3;i++){Hide();await Task.Delay(1005);Show();await Task.Delay(100);}Check(!core.IsSuspended,"rapid show during suspend completion cannot leave visible content suspended");
   Hide();view.Dispose();await Task.Delay(1100);Check(view.IsDisposed,"disposing a hidden view cancels deferred suspension");File.AppendAllText(log,"Completed "+checks+" real WebView visibility checks."+Environment.NewLine);
  }catch(Exception ex){File.AppendAllText(log,ex.ToString());Environment.ExitCode=1;}finally{view.Dispose();Close();}
 }
}
internal static class TestWebViewVisibility {
 [STAThread]static int Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new VisibilityTestForm(Path.GetFullPath(args[0]),Path.GetFullPath(args[1])));return Environment.ExitCode;}
}
