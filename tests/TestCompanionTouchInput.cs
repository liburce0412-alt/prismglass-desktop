using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Production touch requests are confined to visible points owned by this fixture.
internal sealed class TestCompanionTouchInput : Form {
 [DllImport("user32.dll")]static extern bool GetPointerInfo(uint id,out CompanionControls.TouchPointer info);
 [DllImport("user32.dll",SetLastError=true)]static extern bool InjectTouchInput(uint count,[In]CompanionControls.TouchContact[] contacts);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
 [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr handle,int index,System.Text.StringBuilder value,int length,out int needed);
 bool Locked(){var desktop=OpenInputDesktop(0,false,1);if(desktop==IntPtr.Zero)return true;try{int size;var name=new System.Text.StringBuilder(256);return !GetUserObjectInformation(desktop,2,name,512,out size)||name.ToString()!="Default";}finally{CloseDesktop(desktop);}}
 [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 readonly HashSet<uint> active=new HashSet<uint>();int down,up,updates,simultaneous,cancelled;static string log;static int result;
 protected override void WndProc(ref Message message){
  if(message.Msg==0x245||message.Msg==0x246||message.Msg==0x247){
   CompanionControls.TouchPointer info;if(GetPointerInfo((uint)message.WParam.ToInt64()&65535,out info)&&info.type==2){
    if(message.Msg==0x246){down++;active.Add(info.id);simultaneous=Math.Max(simultaneous,active.Count);}
    if(message.Msg==0x245)updates++;if(message.Msg==0x247){up++;active.Remove(info.id);if((info.flags&0x8000)!=0)cancelled++;}
    message.Result=IntPtr.Zero;return;
   }
  }base.WndProc(ref message);
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(log,"PASS "+message+Environment.NewLine);}
 static Dictionary<string,object> Contact(int id,int x){return new Dictionary<string,object>{{"id",id},{"x",x},{"y",30000}};}
 async Task Run(){
  await Task.Delay(150);TopMost=true;Show();Activate();SetForegroundWindow(Handle);await Task.Delay(200);
  Check(!Locked(),"interactive default desktop is available");
  Check(Marshal.SizeOf(typeof(CompanionControls.TouchPointer))==96&&Marshal.SizeOf(typeof(CompanionControls.TouchContact))==144,"production x64 touch layouts match Windows ABI");
  var grant=new CompanionGrant{Active=true,pointer=true};var json=new JavaScriptSerializer();
  using(var controls=new CompanionControls(Locked)){
   var begin=controls.Pointer("/api/pointer/begin",new Dictionary<string,object>(),grant);
   string lease=(string)json.Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(begin.Body))["leaseId"];int seq=0;
   var area=RectangleToScreen(new Rectangle(50,50,440,200));
   Func<object[],CompanionReply> send=points=>{
    foreach(Dictionary<string,object> p in points){var target=new Point(area.Left+(int)Math.Round((int)p["x"]*(area.Width-1)/65535.0),area.Top+(int)Math.Round((int)p["y"]*(area.Height-1)/65535.0));if(WindowFromPoint(target)!=Handle)throw new Exception("Touch target is not owned by the test window; no input sent");}
    return controls.Pointer("/api/pointer",new Dictionary<string,object>{{"kind","touch"},{"leaseId",lease},{"seq",++seq},{"screenLease","fixture"},{"contacts",points}},grant,area);
   };
   Check(send(new object[]{Contact(0,20000)}).Status==200,"production first finger accepted");await Task.Delay(100);
   Check(send(new object[]{Contact(0,20000),Contact(1,40000)}).Status==200,"production second finger accepted");await Task.Delay(100);
   for(int i=0;i<5;i++){Check(send(new object[]{Contact(0,20000-i*1000),Contact(1,40000+i*1000)}).Status==200,"production two-contact movement accepted");await Task.Delay(100);}
   Check(down==2&&simultaneous==2&&updates>=2&&active.Count==2,"Windows delivers both production contacts as native touch");
   Check(send(new object[]{Contact(1,44000)}).Status==200,"lifting one contact keeps the other active");await Task.Delay(100);
   for(int i=0;i<5;i++){Check(send(new object[]{Contact(1,44000)}).Status==200,"stationary native press remains renewable");await Task.Delay(250);}
   Check(active.Count==1&&up==1,"held touch survives beyond the disconnect safety deadline with updates");
   Check(send(new object[0]).Status==200,"normal final lift accepted");await Task.Delay(100);Check(up==2&&active.Count==0,"normal release leaves no native contact");
   Check(send(new object[]{Contact(0,20000),Contact(1,40000)}).Status==200,"new native gesture accepted");await Task.Delay(100);
   var cursor=new CompanionControls.CursorInfo{size=Marshal.SizeOf(typeof(CompanionControls.CursorInfo))};Check(CompanionControls.GetCursorInfo(ref cursor),"cursor readable before cancellation");int x=cursor.x,y=cursor.y;
   controls.EndScreen(grant);await Task.Delay(150);
   Check(active.Count==0&&cancelled==2&&!controls.IsActive,"screen stop cancels both native contacts");
   cursor=new CompanionControls.CursorInfo{size=Marshal.SizeOf(typeof(CompanionControls.CursorInfo))};
   Check(CompanionControls.GetCursorInfo(ref cursor)&&(cursor.flags&1)!=0&&cursor.x==x&&cursor.y==y,"production cleanup restores visible cursor at its original position");
   lease=(string)json.Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(controls.Pointer("/api/pointer/begin",new Dictionary<string,object>(),grant).Body))["leaseId"];seq=0;
   Check(send(new object[]{Contact(0,20000)}).Status==200,"new gesture starts before Windows cancellation probe");await Task.Delay(100);
   Check(send(new object[]{Contact(0,20000)}).Status==200,"Windows cancellation probe has a last accepted update");await Task.Delay(100);
   var wrongPoint=new Point(area.Left+(int)Math.Round(20000*(area.Width-1)/65535.0)+1,area.Top+(int)Math.Round(30000*(area.Height-1)/65535.0));
   Check(WindowFromPoint(wrongPoint)==Handle,"invalid release probe is still confined to the test window");
   var invalidUp=CompanionControls.TouchFrame(new Dictionary<int,Point>{{0,wrongPoint}},new Dictionary<int,Point>(),false);
   Check(!InjectTouchInput((uint)invalidUp.Length,invalidUp)&&Marshal.GetLastWin32Error()==87,"Windows rejects mismatched release coordinates and cancels native contacts");await Task.Delay(100);
   controls.EndScreen(grant);await Task.Delay(100);
   File.AppendAllText(log,"Native contacts after Windows cancellation: "+active.Count+"; production input active: "+controls.IsActive+Environment.NewLine);
   Check(active.Count==0&&!controls.IsActive,"Windows-cancelled contacts cannot leave the production release state blocked");
   Check(controls.Pointer("/api/pointer/begin",new Dictionary<string,object>(),grant).Status==200,"a new control lease works after Windows cancels injection");controls.EndScreen(grant);
  }
 }
 [STAThread]static int Main(string[] args){log=args[0];File.WriteAllText(log,"");SetProcessDPIAware();Application.EnableVisualStyles();using(var form=new TestCompanionTouchInput{Text="PrismGlass 原生触摸链路验收",ClientSize=new Size(540,300),StartPosition=FormStartPosition.CenterScreen,BackColor=Color.LightSteelBlue}){form.Shown+=async(s,e)=>{try{await form.Run();}catch(Exception error){File.AppendAllText(log,error+Environment.NewLine);result=1;}finally{form.Close();}};Application.Run(form);}return result;}
}
