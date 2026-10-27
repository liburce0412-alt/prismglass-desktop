using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.WinForms;

// Regions and layout messages use physical pixels. Chromium's effective scale
// can differ from GetDpiForWindow for a window created on another monitor.
internal sealed class WebViewPixelScale {
 readonly WebView2 view;readonly Action changed;bool busy;DateTime nextRead=DateTime.MinValue;
 public double RasterizationScale {get;private set;}
 public WebViewPixelScale(WebView2 target,Action onChanged){view=target;changed=onChanged;RasterizationScale=1;}
 public async void Sync(bool force=false){await SyncAsync(force);}
 public async Task<bool> SyncAsync(bool force=false){
  if(busy||view.IsDisposed||view.CoreWebView2==null||(!force&&DateTime.UtcNow<nextRead))return false;busy=true;nextRead=DateTime.UtcNow.AddSeconds(1);
  try{
   string value=await view.CoreWebView2.ExecuteScriptAsync("window.devicePixelRatio");
   if(view.IsDisposed||view.CoreWebView2==null)return false;
   double ratio;if(!Double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out ratio)||Double.IsNaN(ratio)||Double.IsInfinity(ratio)||ratio<.1||ratio>10)return false;
   double raster=ratio/view.ZoomFactor;bool refresh=Math.Abs(raster-RasterizationScale)>.001;RasterizationScale=raster;
   if(Math.Abs(ratio-1)>.001){double zoom=view.ZoomFactor/ratio;if(zoom<.25||zoom>5)return false;view.ZoomFactor=zoom;refresh=true;}
   if(refresh&&changed!=null)changed();return refresh;
  }catch(InvalidOperationException){return false;}catch(COMException){return false;}catch(ArgumentException){return false;}
  finally{busy=false;}
 }
}
