using System;using System.Collections;using System.Collections.Generic;using System.Drawing;using System.IO;using System.Reflection;using System.Threading.Tasks;using System.Windows.Forms;using System.Web.Script.Serialization;
internal static class TestStagePosition {
 static Assembly product;static string root,log;static int checks,result;static Form host;static readonly BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static void Write(string text){File.AppendAllText(log,text+Environment.NewLine);}
 static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Write("PASS "+text);}
 static object New(string name,params object[] args){return Activator.CreateInstance(product.GetType(name,true),flags,null,args,null);}
 static void Set(object obj,string name,object value){obj.GetType().GetField(name,flags).SetValue(obj,value);}
 static object Call(object obj,string name,params object[] args){return obj.GetType().GetMethod(name,flags).Invoke(obj,args);}
 static async Task Run(){
  var displays=(IEnumerable)product.GetType("MonitorCatalog").GetMethod("Read",flags).Invoke(null,null);string monitor=null;foreach(var item in displays)if(((Screen)item.GetType().GetField("Screen").GetValue(item)).Primary)monitor=(string)item.GetType().GetField("Id").GetValue(item);
  var saved=new Dictionary<string,object>{{monitor+"/stage",new{x=.31,y=.21}}};File.WriteAllText(Path.Combine(root,"component-positions.json"),new JavaScriptSerializer().Serialize(saved));
  var area=Screen.PrimaryScreen.WorkingArea;var manager=New("StageManager",root,(Action)(()=>{}));
  try{for(int run=0;run<3;run++){
   var window=New("DockWindow");Set(window,"handle",host.Handle.ToInt64());Set(window,"pid",(uint)System.Diagnostics.Process.GetCurrentProcess().Id);Set(window,"path",Application.ExecutablePath);Set(window,"title","Position fixture");
   var member=New("StageManager+Member");Set(member,"window",window);var placement=product.GetType("StageManager").GetMethod("Read",flags).Invoke(null,new[]{window});Set(member,"layout",placement);Set(member,"original",placement);
   var group=New("StageManager+Group");Set(group,"monitor",Screen.PrimaryScreen.DeviceName);((IList)group.GetType().GetField("members",flags).GetValue(group)).Add(member);
   var groups=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(group.GetType()));groups.Add(group);
   var strip=(System.Windows.Window)New("StageManager+StageStrip",manager,Screen.PrimaryScreen.DeviceName);int suspended=0;var counter=strip.Dispatcher.GetType().GetField("_disableProcessingCount",flags);
   Check(counter!=null,"Dispatcher processing-suspension probe is available "+run);strip.SizeChanged+=(s,e)=>{if(Convert.ToInt32(counter.GetValue(strip.Dispatcher))>0)suspended++;};
   try{
    Call(strip,"RefreshGroups",groups,area);await Task.Delay(100);var placed=(Rectangle)strip.GetType().GetField("placed",flags).GetValue(strip);
    Check(strip.IsVisible&&Math.Abs(placed.X-(area.Left+.31*area.Width))<=1&&Math.Abs(placed.Y-(area.Top+.21*area.Height))<=1,"saved Stage anchor restores on startup "+run);
    var second=New("StageManager+Group");Set(second,"monitor",Screen.PrimaryScreen.DeviceName);((IList)second.GetType().GetField("members",flags).GetValue(second)).Add(member);groups.Add(second);Call(strip,"RefreshGroups",groups,area);await Task.Delay(70);Check(strip.ActualHeight>=296,"Stage host resizes after group count changes "+run);
    groups.RemoveAt(1);Call(strip,"RefreshGroups",groups,area);await Task.Delay(70);Check(strip.ActualHeight<200,"Stage host shrinks while preserving startup position "+run);
    Call(strip,"SetPaused",true);Call(strip,"SetPaused",false);await Task.Delay(70);Check(strip.IsVisible,"Stage pause/resume restores the saved host "+run);Write("INFO SizeChanged with Dispatcher processing suspended: "+suspended);
   }finally{((IDisposable)strip).Dispose();}
  }}finally{((IDisposable)manager).Dispose();}
  Check(File.ReadAllText(Path.Combine(root,"component-positions.json")).Contains("0.31"),"layout and startup did not overwrite the saved position");Write("Completed "+checks+" focused Stage position checks");
 }
 [STAThread]static int Main(string[] args){product=Assembly.LoadFrom(args[0]);root=args[1];log=args[2];Directory.CreateDirectory(root);File.WriteAllText(log,"");AppDomain.CurrentDomain.UnhandledException+=(s,e)=>Write("UNHANDLED "+e.ExceptionObject);Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();host=new Form{Text="PrismGlass isolated Stage position check",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(500,300,640,400),ShowInTaskbar=false};host.Shown+=async(s,e)=>{try{await Run();}catch(Exception ex){result=1;Write(ex.ToString());}finally{host.Close();}};Application.Run(host);return result;}
}
