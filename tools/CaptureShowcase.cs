// Captures the real component renderer with isolated, explicitly fictional data.
using System;using System.IO;using System.Drawing;using System.Reflection;using System.Threading.Tasks;using System.Windows.Forms;using System.Diagnostics;using System.Globalization;using Microsoft.Web.WebView2.WinForms;using Microsoft.Web.WebView2.Core;
internal static class CaptureShowcase {
 static readonly BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static WebView2 view;static Form form;static string output;
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 static Task<string> Eval(string s){return view.CoreWebView2.ExecuteScriptAsync(s);}
 static async Task Save(string name){if(form is SideDock){var shot=await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot","{\"format\":\"png\",\"captureBeyondViewport\":false,\"clip\":{\"x\":0,\"y\":0,\"width\":488,\"height\":680,\"scale\":1}}");var data=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(shot);File.WriteAllBytes(Path.Combine(output,name+".png"),Convert.FromBase64String((string)data["data"]));return;}using(var file=File.Create(Path.Combine(output,name+".png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);}
 static void StopTimer(){var field=form.GetType().GetField("timer",flags);if(field!=null)((Timer)field.GetValue(form)).Stop();}
 static async Task Wait(){for(int i=0;i<150;i++){await Task.Delay(100);if(view.CoreWebView2!=null&&await Eval("document.readyState==='complete'")=="true"){await Task.Delay(1200);StopTimer();return;}}throw new Exception("Component did not become ready");}
 static async Task Island(){
  var island=(TopIsland)form;island.Sync("Sky",Screen.PrimaryScreen.Bounds,IntPtr.Zero,false);
  await Eval("window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:{type:'status',disk:'本地工作台'}}))");
  await Task.Delay(500);await Save("top-bar");
  var frames=Path.Combine(output,"frames");Directory.CreateDirectory(frames);var times=new System.Collections.Generic.List<double>();var watch=Stopwatch.StartNew();int index=0,stage=0;
  while(watch.ElapsedMilliseconds<6500){
   var ms=watch.ElapsedMilliseconds;
   if(stage==0&&ms>=600){await Eval("PrismIsland.change('pill')");stage++;}
   if(stage==1&&ms>=2100){await Eval("PrismIsland.change('open')");stage++;}
   if(stage==2&&ms>=4200){await Eval("PrismIsland.change('bar')");stage++;}
   times.Add(watch.Elapsed.TotalSeconds);using(var file=File.Create(Path.Combine(frames,(index++).ToString("D4")+".png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);
   await Task.Delay(35);
  }
  using(var list=new StreamWriter(Path.Combine(frames,"frames.txt"))){for(int i=0;i<times.Count;i++){list.WriteLine("file '"+i.ToString("D4")+".png'");list.WriteLine("duration "+(i+1<times.Count?times[i+1]-times[i]:.1).ToString("0.000000",CultureInfo.InvariantCulture));}list.WriteLine("file '"+(times.Count-1).ToString("D4")+".png'");}
  await Eval("PrismIsland.change('open')");await Task.Delay(1200);await Save("top-expanded");
 }
 static async Task Garden(){
  await Eval("localStorage.clear();localStorage.setItem('prism-completed','20');localStorage.setItem('prism-garden',JSON.stringify({version:2,plant:'fern',credited:20,drops:8,seeds:3,watered:{fern:16,succulent:8,bamboo:12,orchid:16,maple:16,cedar:16},unlocked:['fern','succulent','bamboo','orchid','maple','cedar']}));location.reload()");await Task.Delay(300);await Wait();
  var dock=(SideDock)form;dock.Open("life",Point.Empty);await Task.Delay(650);await Eval("document.querySelector('[data-life-tab=garden]').click()");
  foreach(var theme in new[]{"Sky","Astro"}){dock.Sync(theme,Screen.PrimaryScreen.Bounds,IntPtr.Zero);await Eval("document.getElementById('widget-page').scrollTop=0");await Task.Delay(800);await Save("garden-"+theme.ToLowerInvariant());}
  dock.Sync("Sky",Screen.PrimaryScreen.Bounds,IntPtr.Zero);await Eval("document.getElementById('garden-collection').open=true;document.getElementById('garden-collection').scrollIntoView({block:'start'})");await Task.Delay(800);await Save("garden-collection");
 }
 static async Task Shelf(){
  var shelf=(FileShelf)form;var receive=typeof(FileShelf).GetMethod("Receive",flags);
  foreach(var mode in new[]{"quota","notes","manage"}){
   shelf.Sync(mode=="notes"?"Astro":"Sky",Screen.PrimaryScreen.Bounds,IntPtr.Zero,false);
   receive.Invoke(shelf,new object[]{"{\"action\":\"mode\",\"value\":\""+mode+"\"}"});await Task.Delay(650);
   await Eval("window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:{type:'quota',busy:false,snapshot:{state:'fresh',fetchedAt:Math.floor(Date.now()/1000),message:'演示数据 · 非真实账户额度',weekly:{remainingPercent:72,windowDurationMins:10080,resetsAt:0},shortTerm:null}}}))");
   if(mode=="notes")await Eval("window.chrome.webview.dispatchEvent(new MessageEvent('message',{data:{type:'notes',text:'一个安静的工作台。\\n\\n完成一轮专注，给生态瓶浇水。\\n把突然想到的点子留在这里。\\n\\n—— 这是演示笔记，不是真实个人数据。'}}))");
   await Task.Delay(500);await Save("shelf-"+mode);
  }
 }
 [STAThread]static void Main(string[] args){SetProcessDPIAware();output=args[1];Directory.CreateDirectory(output);Application.EnableVisualStyles();form=args[2]=="island"?(Form)new TopIsland(args[0],true):args[2]=="garden"?(Form)new SideDock(args[0],true):new FileShelf(args[0],true);view=(WebView2)form.GetType().GetField("view",flags).GetValue(form);form.Shown+=async(s,e)=>{try{await Wait();if(args[2]=="island")await Island();else if(args[2]=="garden")await Garden();else await Shelf();Console.WriteLine("Captured "+args[2]);}catch(Exception ex){Console.WriteLine(ex);Environment.ExitCode=1;}finally{form.Close();}};Application.Run(form);}
}
