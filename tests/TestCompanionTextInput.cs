using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Character-delivery experiment confined to this test window. Never targets a user document.
internal static class TestCompanionTextInput {
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,CompanionControls.Input[] input,int size);
 static Form form;static TextBox editor;static WebView2 web;static string output;static int exitCode;
 static readonly JavaScriptSerializer json=new JavaScriptSerializer();
 static void Log(string message){File.AppendAllText(output,message+Environment.NewLine);}
 static async Task Wait(string expression){for(int i=0;i<100;i++){if(await web.CoreWebView2.ExecuteScriptAsync(expression)=="true")return;await Task.Delay(50);}throw new Exception("Browser focus did not settle");}
 static async Task Send(string value,bool experiment){
  if(GetForegroundWindow()!=form.Handle)throw new Exception("Probe is not foreground; no input sent");
  var input=experiment?value.SelectMany(c=>new[]{new CompanionControls.Input{type=1,data=new CompanionControls.InputUnion{keyboard=new CompanionControls.KeyboardInput{scan=c,flags=4}}},new CompanionControls.Input{type=1,data=new CompanionControls.InputUnion{keyboard=new CompanionControls.KeyboardInput{scan=c,flags=6}}}}).ToArray():CompanionControls.Inputs(new CompanionControls.MouseCommand("text",inputText:value));
  uint sent=SendInput((uint)input.Length,input,Marshal.SizeOf(typeof(CompanionControls.Input)));if(sent!=input.Length)throw new Exception("Native text injection incomplete: "+sent+"/"+input.Length+"; Win32="+Marshal.GetLastWin32Error()+"; INPUT size="+Marshal.SizeOf(typeof(CompanionControls.Input)));await Task.Delay(350);
 }
 static async Task Run(string profile){
  await web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,profile));web.NavigateToString("<!doctype html><html><body><textarea id='draft' rows='6' cols='35'></textarea></body></html>");await Wait("!!document.getElementById('draft')");await Task.Delay(150);form.Activate();form.ActiveControl=editor;editor.Focus();
  string single=new string('测',500)+"，中文标点与 emoji 🧊。";if(!editor.Focused)throw new Exception("Native editor does not have focus");await Send(single,false);for(int i=0;i<30&&editor.Text.Length<single.Length;i++)await Task.Delay(100);if(editor.Text!=single){Log("Native editor received length="+editor.Text.Length+"; expected="+single.Length+"; tail="+json.Serialize(editor.Text.Substring(Math.Max(0,editor.Text.Length-40))));throw new Exception("Native editor did not receive the complete 500-character sample");}Log("PASS actual Windows multiline editor receives 500 Chinese characters, punctuation and emoji through production Unicode input");
  editor.Clear();editor.Focus();await Send("第一段🧊\n第二段。",true);Log("Windows editor LF experiment: "+json.Serialize(editor.Text));
  form.ActiveControl=web;web.Select();web.Focus();await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').focus()");await Wait("document.activeElement.id==='draft'");await Send(single,false);await Wait("document.getElementById('draft').value.length>="+single.Length);string actual=json.Deserialize<string>(await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').value"));if(actual!=single){Log("Browser received length="+actual.Length+"; editor length="+editor.Text.Length+"; web focus="+web.ContainsFocus+"; tail="+json.Serialize(actual.Substring(Math.Max(0,actual.Length-40))));throw new Exception("Browser textarea did not receive the complete 500-character sample");}Log("PASS actual browser textarea receives the same complete sample through production Unicode input");
  await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').value='';document.getElementById('draft').focus()");await Send("第一段🧊\n第二段。",true);Log("Browser textarea LF experiment: "+await web.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').value"));
  Log("No clipboard, user document or remote input used. LF was tested as Unicode, never translated to Enter.");
 }
 [STAThread]static int Main(string[] args){output=args[1];File.WriteAllText(output,"");Application.EnableVisualStyles();using(form=new Form{Text="PrismGlass 输入验收（仅测试窗口）",ClientSize=new Size(620,420)})using(editor=new TextBox{Multiline=true,Dock=DockStyle.Top,Height=160})using(web=new WebView2{Dock=DockStyle.Fill}){form.Controls.Add(web);form.Controls.Add(editor);form.Shown+=async(s,e)=>{try{await Run(args[0]);}catch(Exception error){Log(error.ToString());exitCode=1;}finally{form.Close();}};Application.Run(form);}return exitCode;}
}
