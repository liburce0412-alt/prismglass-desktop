using System;
using System.IO;
using Microsoft.Web.WebView2.Core;

internal static class WebViewRecovery {
 internal static bool KeepAlive(string root,CoreWebView2ProcessFailedKind kind){
  // WebView2 recreates GPU and utility processes without replacing the page.
  if(kind!=CoreWebView2ProcessFailedKind.GpuProcessExited&&kind!=CoreWebView2ProcessFailedKind.UtilityProcessExited)return false;
  try{File.WriteAllText(Path.Combine(root,"webview-recovery-last.txt"),DateTime.UtcNow.ToString("o")+" "+kind);}catch{}
  return true;
 }
}
