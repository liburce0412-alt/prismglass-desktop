using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class WebViewVisibility {
 readonly WebView2 view;readonly Timer delay=new Timer{Interval=1000};bool disposed,suspending;
 public static void Track(WebView2 view){new WebViewVisibility(view);}
 WebViewVisibility(WebView2 control){
  view=control;view.VisibleChanged+=Changed;view.CoreWebView2InitializationCompleted+=Initialized;view.Disposed+=Disposed;delay.Tick+=Suspend;Changed(null,EventArgs.Empty);
 }
 void Initialized(object sender,CoreWebView2InitializationCompletedEventArgs e){if(e.IsSuccess)Changed(null,EventArgs.Empty);}
 void Changed(object sender,EventArgs e){
  if(disposed)return;delay.Stop();if(view.CoreWebView2==null)return;
  if(!view.Visible){delay.Start();return;}
  try{view.CoreWebView2.Resume();}catch(InvalidOperationException){}catch(COMException){}
 }
 async void Suspend(object sender,EventArgs e){
  delay.Stop();if(disposed||view.Visible||view.CoreWebView2==null||suspending)return;
  var core=view.CoreWebView2;suspending=true;
  try{
   await core.TrySuspendAsync();
   // Showing the window can race with the completion of the earlier suspend request.
   if(!disposed&&view.Visible&&view.CoreWebView2==core)core.Resume();
  }catch(InvalidOperationException){}catch(COMException){}finally{suspending=false;}
 }
 void Disposed(object sender,EventArgs e){disposed=true;delay.Stop();delay.Dispose();view.VisibleChanged-=Changed;view.CoreWebView2InitializationCompleted-=Initialized;view.Disposed-=Disposed;}
}
