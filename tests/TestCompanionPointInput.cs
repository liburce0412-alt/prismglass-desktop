using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Native clicks are confined to pixels owned by this fixture, never a user window.
internal static class TestCompanionPointInput {
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
 [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr handle,int index,System.Text.StringBuilder value,int length,out int needed);
 static bool Locked(){var desktop=OpenInputDesktop(0,false,1);if(desktop==IntPtr.Zero)return true;try{int size;var name=new System.Text.StringBuilder(256);return !GetUserObjectInformation(desktop,2,name,512,out size)||name.ToString()!="Default";}finally{CloseDesktop(desktop);}}
 [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [StructLayout(LayoutKind.Sequential)]struct CursorInfo {public int size,flags;public IntPtr handle;public int x,y;}
 [DllImport("user32.dll",SetLastError=true)]static extern bool GetCursorInfo(ref CursorInfo info);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]static extern uint SendInput(uint count,CompanionControls.Input[] input,int size);
 static Form form;static Panel target;static int downs,ups,moves,result;static Point last;static string log;
 static bool Inject(CompanionControls.MouseCommand command){
  if(command.kind!="up"&&Locked())return false;
  if(command.kind=="point"&&(!target.RectangleToScreen(target.ClientRectangle).Contains(command.dx,command.dy)||WindowFromPoint(new Point(command.dx,command.dy))!=target.Handle))return false;
  var input=CompanionControls.Inputs(command);return SendInput((uint)input.Length,input,Marshal.SizeOf(typeof(CompanionControls.Input)))==input.Length;
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(log,"PASS "+message+Environment.NewLine);}
 static async Task Run(){
  await Task.Delay(150);form.TopMost=true;form.Show();form.Activate();await Task.Delay(200);Check(!Locked(),"interactive default desktop is available before native input");
  var grant=new CompanionGrant{Active=true,pointer=true};var json=new JavaScriptSerializer();
  using(var controls=new CompanionControls(Locked,()=>DateTime.UtcNow,Inject,action=>"Power unavailable")){
   var reply=controls.Pointer("/api/pointer/begin",new Dictionary<string,object>(),grant);
   var data=json.Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(reply.Body));string lease=(string)data["leaseId"];
   var area=target.RectangleToScreen(target.ClientRectangle);int seq=0;
   Func<string,int,int,CompanionReply> point=(phase,x,y)=>controls.Pointer("/api/pointer",new Dictionary<string,object>{{"leaseId",lease},{"seq",++seq},{"kind","point"},{"phase",phase},{"screenLease","fixture"},{"x",x},{"y",y}},grant,area);
   Check(point("click",16384,32768).Status==200,"production point request accepted");await Task.Delay(120);
   Check(downs==1&&ups==1&&Math.Abs(last.X-area.Width/4)<=1&&Math.Abs(last.Y-area.Height/2)<=1,"Windows receives one click at the mapped target pixel");
   Check(point("start",16384,32768).Status==200,"production direct press accepted");await Task.Delay(120);
   Check(point("move",49151,32768).Status==200,"production absolute movement accepted");await Task.Delay(120);
   Check(controls.Pointer("/api/pointer",new Dictionary<string,object>{{"leaseId",lease},{"seq",++seq},{"kind","drag"},{"phase","end"}},grant).Status==200,"production release accepted");await Task.Delay(120);
   Check(downs==2&&ups==2&&moves>0&&Math.Abs(last.X-3*area.Width/4)<=1&&(GetAsyncKeyState(1)&0x8000)==0,"Windows observes held movement and final mouse-up at the expected position");
   Check(point("start",32768,32768).Status==200,"production direct drag starts before screen stop");await Task.Delay(120);
   var cursor=new CursorInfo{size=Marshal.SizeOf(typeof(CursorInfo))};Check(GetCursorInfo(ref cursor),"cursor position readable before stopping screen");int cursorX=cursor.x,cursorY=cursor.y;
   controls.EndScreen(grant);await Task.Delay(120);
   Check(!controls.IsActive&&downs==3&&ups==3&&(GetAsyncKeyState(1)&0x8000)==0,"screen stop releases the real Windows mouse button and ends input");
   cursor=new CursorInfo{size=Marshal.SizeOf(typeof(CursorInfo))};Check(GetCursorInfo(ref cursor)&&(cursor.flags&1)!=0&&cursor.handle!=IntPtr.Zero&&cursor.x==cursorX&&cursor.y==cursorY,"screen stop leaves the Windows pointer visible at the same pixel");
  }
  form.FormBorderStyle=FormBorderStyle.None;target.Dock=DockStyle.Fill;
  foreach(var screen in Screen.AllScreens){
   form.Bounds=screen.Bounds;await Task.Delay(200);var area=target.RectangleToScreen(target.ClientRectangle);
   Check(area==screen.Bounds,"test surface covers actual display "+screen.Bounds+" at "+GetDpiForWindow(form.Handle)+" DPI");
   using(var controls=new CompanionControls(Locked,()=>DateTime.UtcNow,Inject,action=>"Power unavailable")){
    string lease=(string)json.Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(controls.Pointer("/api/pointer/begin",new Dictionary<string,object>(),grant).Body))["leaseId"];int seq=0;
    foreach(var position in new[]{new Point(0,0),new Point(65535,0),new Point(65535,65535),new Point(0,65535),new Point(32768,32768)}){
     int beforeDown=downs,beforeUp=ups;var expected=new Point((int)Math.Round(position.X*(area.Width-1)/65535.0),(int)Math.Round(position.Y*(area.Height-1)/65535.0));
     var result=controls.Pointer("/api/pointer",new Dictionary<string,object>{{"leaseId",lease},{"seq",++seq},{"kind","point"},{"phase","click"},{"screenLease","display-fixture"},{"x",position.X},{"y",position.Y}},grant,area);
     Check(result.Status==200,"actual display point accepted at "+expected);await Task.Delay(100);
     Check(downs==beforeDown+1&&ups==beforeUp+1&&Math.Abs(last.X-expected.X)<=1&&Math.Abs(last.Y-expected.Y)<=1,"Windows receives the actual corner or center within one pixel");
    }
    controls.EndScreen(grant);Check(!controls.IsActive&&(GetAsyncKeyState(1)&0x8000)==0,"actual display point checks leave no held input");
   }
  }
 }
 [STAThread]static int Main(string[] args){log=args[0];File.WriteAllText(log,"");SetProcessDPIAware();Application.EnableVisualStyles();using(form=new Form{Text="PrismGlass 直接拖动验收（仅测试窗口）",ClientSize=new Size(600,320),StartPosition=FormStartPosition.CenterScreen})using(target=new Panel{Location=new Point(30,30),Size=new Size(540,260),BackColor=Color.LightSteelBlue}){
  form.Controls.Add(target);target.MouseDown+=(s,e)=>{downs++;last=e.Location;target.Capture=true;};target.MouseMove+=(s,e)=>{if(e.Button==MouseButtons.Left){moves++;last=e.Location;}};target.MouseUp+=(s,e)=>{ups++;last=e.Location;target.Capture=false;};
  form.Shown+=async(s,e)=>{try{await Run();}catch(Exception error){File.AppendAllText(log,error+Environment.NewLine);result=1;}finally{form.Close();}};Application.Run(form);
 }return result;}
}
