using System;using System.Drawing;using System.IO;using System.Reflection;using System.Threading.Tasks;using System.Windows.Forms;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
class TestTopTooltip {
 static Form form;static WebView2 view;static string output;static int checks;
 static async Task Check(string expression,string name){if(await view.CoreWebView2.ExecuteScriptAsync(expression)!="true")throw new Exception(name);File.AppendAllText(Path.Combine(output,"top-tooltip-checks.txt"),"PASS "+name+"\n");checks++;}
 [STAThread]static void Main(string[] args){output=args[1];File.WriteAllText(Path.Combine(output,"top-tooltip-checks.txt"),"");Application.EnableVisualStyles();var assembly=Assembly.LoadFrom(Path.GetFullPath(args[2]));var type=assembly.GetType("TopIsland");form=(Form)Activator.CreateInstance(type,new object[]{args[0],true});form.TopMost=true;view=(WebView2)type.GetField("view",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
 form.Shown+=async(s,e)=>{try{
  for(int i=0;i<300;i++){if(view.CoreWebView2!=null&&await view.CoreWebView2.ExecuteScriptAsync("window.islandState?.ready===true")=="true")break;await Task.Delay(50);}
  await Check("window.islandState?.ready===true","real TopIsland completes rendering");
  await Check("[...document.querySelectorAll('.bar-controls .control-icon use')].every(e=>e.getBBox().width>0)","real top buttons resolve every shared vector icon");
  foreach(var theme in new[]{"Sky","Astro"}){
   type.GetMethod("Sync").Invoke(form,new object[]{theme,Screen.PrimaryScreen.Bounds,IntPtr.Zero,false});await Task.Delay(250);
   await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-action=theme]').dispatchEvent(new PointerEvent('pointerover',{bubbles:true}))");await Task.Delay(400);
   await Check("document.querySelector('#top-tooltip').textContent==='切换 Sky / Astro'&&Number(getComputedStyle(document.querySelector('#top-tooltip')).opacity)>.99&&window.islandOutline.length===2",theme+" tooltip shares the native glass scene and clip");
   using(var stream=File.Create(Path.Combine(output,"top-tooltip-"+theme+".png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
   await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[data-action=settings]').dispatchEvent(new PointerEvent('pointerover',{bubbles:true}))");await Task.Delay(250);
   await Check("document.querySelector('#top-tooltip').textContent==='PrismGlass 设置'&&!document.querySelector('button[title]')&&document.querySelectorAll('.glass-tooltip').length===1",theme+" only the pointed button owns a capsule and browser tooltips are absent");
   await view.CoreWebView2.ExecuteScriptAsync("document.dispatchEvent(new PointerEvent('pointerout',{bubbles:true}))");await Task.Delay(400);
   await Check("getComputedStyle(document.querySelector('#top-tooltip')).display==='none'&&window.islandOutline.length===1",theme+" leaving clears text and native tooltip region");
  }
  foreach(int width in new[]{320,400,640,800,1000}){
   type.GetMethod("Sync").Invoke(form,new object[]{"Sky",new Rectangle(0,0,width+48,900),IntPtr.Zero,false});await Task.Delay(400);
   await Check("[...document.querySelectorAll('.bar-controls button,#handle')].filter(b=>b.getBoundingClientRect().width>0).every(b=>{const r=b.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)?.closest('button')===b})","top buttons and central handle remain individually reachable at width "+width);
  }
  await view.CoreWebView2.ExecuteScriptAsync("PrismIsland.change('pill');document.querySelector('#handle').focus()");await Task.Delay(350);
  await Check("getComputedStyle(document.querySelector('#handle')).boxShadow==='none'&&getComputedStyle(document.querySelector('#handle')).outlineStyle==='none'&&getComputedStyle(document.querySelector('#island-summary')).boxShadow!=='none'","keyboard focus uses the inner rounded summary rather than a blue rectangle across the native clip");
  await view.CoreWebView2.ExecuteScriptAsync("PrismIsland.change('open');document.querySelector('#island-weather').dispatchEvent(new PointerEvent('pointerover',{bubbles:true}))");await Task.Delay(350);
  await Check("getComputedStyle(document.querySelector('#top-tooltip')).display==='none'&&window.islandOutline.length===1","expanded native island keeps weather and focus controls free of overlay hints");
  using(var stream=File.Create(Path.Combine(output,"top-island-focus-corrected.png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
  File.AppendAllText(Path.Combine(output,"top-tooltip-checks.txt"),"Completed "+checks+" native top tooltip checks.\n");
 }catch(Exception ex){File.AppendAllText(Path.Combine(output,"top-tooltip-checks.txt"),ex.ToString());Environment.ExitCode=1;}finally{form.Close();}};Application.Run(form);
 }
}
