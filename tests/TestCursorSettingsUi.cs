// Runs the actual FileShelf UI in preview mode: no system pointer changes or user settings writes.
using System;using System.IO;using System.Drawing;using System.Drawing.Drawing2D;using System.Reflection;using System.Threading.Tasks;using System.Windows.Forms;using Microsoft.Web.WebView2.WinForms;using Microsoft.Web.WebView2.Core;
internal static class TestCursorSettingsUi {
 static FileShelf shelf;static WebView2 view;static string output,root;static int checks;
 static readonly BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 static Task<string> Eval(string script){return view.CoreWebView2.ExecuteScriptAsync(script);}
 static void Check(bool pass,string name){File.AppendAllText(Path.Combine(output,"settings-ui-test.txt"),(pass?"PASS ":"FAIL ")+name+Environment.NewLine);if(!pass)throw new Exception(name);checks++;}
 static async Task Assert(string expression,string name){Check(await Eval(expression)=="true",name);}
 static async Task Ready(){for(int i=0;i<150;i++){await Task.Delay(100);if(view.CoreWebView2!=null&&await Eval("!!window.PrismCursorSettings&&document.readyState==='complete'")=="true"){await Task.Delay(600);return;}}throw new Exception("Preview did not become ready");}
 static async Task Change(string script){await Eval(script);await Task.Delay(150);for(int i=0;i<30;i++){if(await Eval("document.getElementById('cursor-settings').getAttribute('aria-busy')==='false'")=="true")return;await Task.Delay(100);}throw new Exception("Setting did not finish applying");}
 static async Task Save(string name){using(var file=File.Create(Path.Combine(output,name+".png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);}
 static ShelfState State(){return (ShelfState)typeof(FileShelf).GetField("state",flags).GetValue(shelf);}
 static async Task Exercise(){
  await Ready();shelf.Sync("Sky",Screen.PrimaryScreen.Bounds,IntPtr.Zero,false);
  await Eval("document.getElementById('manage-trigger').click()");await Task.Delay(800);
  await Assert("!document.getElementById('manage-page').hidden","management opens through the actual entry point");
  await Assert("document.querySelector('#cursor-sizes [data-value=\"150\"]').getAttribute('aria-pressed')==='true'","existing size appears selected");
  await Assert("document.querySelectorAll('#cursor-styles button').length===4&&document.querySelectorAll('#cursor-trails button').length===3","all pointer and trail choices rendered");
  foreach(string style in CursorPreferences.Styles){await Change("document.querySelector('#cursor-styles [data-value=\""+style+"\"]').click()");Check(State().cursor.style==style,"UI to backend pointer choice "+style);}
  await Assert("[...document.querySelectorAll('#cursor-sizes button')].every(b=>b.disabled)","Windows original mode retains its own size");await Save("settings-windows");
  await Change("document.querySelector('#cursor-styles [data-value=\"rounded-light\"]').click()");
  foreach(int size in CursorPreferences.Sizes){await Change("document.querySelector('#cursor-sizes [data-value=\""+size+"\"]').click()");Check(State().cursorScale==size,"UI to backend size "+size);}
  await Change("document.querySelector('#cursor-sizes [data-value=\"150\"]').click()");
  foreach(string style in CursorPreferences.Trails){await Change("document.querySelector('#cursor-trails [data-value=\""+style+"\"]').click()");Check(State().cursor.trailStyle==style,"UI to backend trail choice "+style);}
  await Change("document.getElementById('cursor-trail-toggle').click()");Check(!State().cursor.trailEnabled,"trail switch disables effect");
  await Assert("[...document.querySelectorAll('#cursor-trail-options button,#cursor-trail-options input')].every(b=>b.disabled)","trail controls disabled without losing choices");await Save("settings-off");
  await Change("document.getElementById('cursor-trail-toggle').click()");Check(State().cursor.trailEnabled&&State().cursor.trailStyle=="ribbon","reenabling preserves trail choice");
  await Change("document.querySelector('[data-color=\"#B29EEA\"]').click()");Check(State().cursor.trailColor=="#B29EEA","color swatch applies");
  await Change("document.getElementById('cursor-custom-color').value='#ff8855';document.getElementById('cursor-color-form').requestSubmit()");Check(State().cursor.trailColor=="#FF8855","custom hex color applies");
  await Eval("document.getElementById('cursor-custom-color').value='#nope';document.getElementById('cursor-color-form').requestSubmit()");await Task.Delay(100);
  Check(State().cursor.trailColor=="#FF8855","invalid custom color rejected");
  await Eval("document.getElementById('cursor-custom-color').value='#FF8855';document.getElementById('manage-scroll').scrollTop=0");
  await Assert("document.getElementById('manage-scroll').scrollHeight>document.getElementById('manage-scroll').clientHeight","combined cursor and component settings are scrollable");
  foreach(string theme in new[]{"Sky","Astro"}){shelf.Sync(theme,Screen.PrimaryScreen.Bounds,IntPtr.Zero,false);await Task.Delay(600);await Save("settings-"+theme.ToLowerInvariant());}
  await Eval("document.getElementById('cursor-color-form').scrollIntoView({block:'center'})");await Task.Delay(150);await Save("settings-colors");
  await Eval("document.getElementById('manage-scroll').scrollTop=99999");await Task.Delay(150);
  await Assert("document.getElementById('manage-list').getBoundingClientRect().bottom<=document.getElementById('manage-scroll').getBoundingClientRect().bottom+2","last component toggle remains reachable");await Save("settings-components");
  await Eval("document.getElementById('close').click()");await Task.Delay(500);await Eval("document.getElementById('manage-trigger').click()");await Task.Delay(600);
  Check(State().cursor.style=="rounded-light"&&State().cursorScale==150&&State().cursor.trailColor=="#FF8855","close and reopen retains preview choices");
  Check(!File.Exists(Path.Combine(root,"cursor-options.json"))&&!File.Exists(Path.Combine(root,"cursor-theme.active")),"preview never saves or applies the system cursor");
  File.AppendAllText(Path.Combine(output,"settings-ui-test.txt"),"Completed "+checks+" checks. Preview interaction only; native rendering tested separately."+Environment.NewLine);
 }
 [STAThread]static void Main(string[] args){
  SetProcessDPIAware();root=args[0];output=args[1];Directory.CreateDirectory(root);Directory.CreateDirectory(output);
  using(var bitmap=new Bitmap(1600,1000)){using(var g=Graphics.FromImage(bitmap))using(var gradient=new LinearGradientBrush(new Rectangle(0,0,1600,1000),Color.FromArgb(108,131,172),Color.FromArgb(32,47,77),40f))g.FillRectangle(gradient,0,0,1600,1000);bitmap.Save(Path.Combine(root,"wallpaper.png"));}
  File.WriteAllText(Path.Combine(root,"cursor-scale.txt"),"150");Application.EnableVisualStyles();shelf=new FileShelf(root,true);view=(WebView2)typeof(FileShelf).GetField("view",flags).GetValue(shelf);
  shelf.Shown+=async(s,e)=>{try{await Exercise();}catch(Exception ex){File.AppendAllText(Path.Combine(output,"settings-ui-test.txt"),ex.ToString()+Environment.NewLine);Environment.ExitCode=1;}finally{shelf.Close();}};
  Application.Run(shelf);
 }
}
