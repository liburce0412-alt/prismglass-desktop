using System;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

// Session objects, rather than client-supplied device IDs, own every lease and confirmation.
internal sealed class CompanionControls : IDisposable {
 internal sealed class MouseCommand {
  public readonly string kind,button,text,key;public readonly int dx,dy,steps;
  public MouseCommand(string type,int x=0,int y=0,int scroll=0,string click="",string inputText="",string keyName=""){kind=type;dx=x;dy=y;steps=scroll;button=click;text=inputText;key=keyName;}
 }
 sealed class Lease {public CompanionGrant owner;public string id,screen;public int seq;public DateTime expires,dragExpires;public bool dragging;}
 sealed class Ticket {public string id,action;public DateTime expires;}
 sealed class Job {public CompanionGrant owner;public string id,action,status,message;public DateTime executeAt;public int stage;}
 readonly object gate=new object();readonly Func<bool> locked;readonly Func<DateTime> clock;readonly Func<MouseCommand,bool> mouse;readonly Func<string,string> executePower;readonly Timer timer;
 readonly Dictionary<CompanionGrant,Ticket> tickets=new Dictionary<CompanionGrant,Ticket>();Lease lease;Job job;volatile bool disposed;bool releasePending,restorePending;
 internal Action<string> PowerDiagnostic;
 internal Func<bool> RestorePointerVisibility;
 internal Func<TouchContact[],bool> SendTouch;
 readonly Dictionary<int,Point> touches=new Dictionary<int,Point>();string touchScreen;DateTime touchExpires;bool touchReleasePending;
 string lastPowerDiagnostic="";
 public CompanionControls(Func<bool> isLocked,string root=null):this(isLocked,()=>DateTime.UtcNow,SendMouse,SendPower,true){RestorePointerVisibility=RestorePointer;SendTouch=InjectTouches;if(root!=null)PowerDiagnostic=value=>{string directory=System.IO.Path.Combine(root,"runtime-data");System.IO.Directory.CreateDirectory(directory);System.IO.File.WriteAllText(System.IO.Path.Combine(directory,"companion-power-last.json"),value);};}
 internal CompanionControls(Func<bool> isLocked,Func<DateTime> now,Func<MouseCommand,bool> sendMouse,Func<string,string> power,bool runTimer=false){
  locked=isLocked??(()=>true);clock=now??(()=>DateTime.UtcNow);mouse=sendMouse??(command=>false);executePower=power??(action=>"电源操作暂不可用。");
  if(runTimer)timer=new Timer(ignored=>{try{Tick();}catch{CancelAll();}},null,250,250);
 }
 static long Stamp(DateTime value){return (long)(value-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
 static string Id(){return Guid.NewGuid().ToString("N");}
 bool Locked(){try{return locked();}catch{return true;}}
 bool Allowed(CompanionGrant grant,bool pointer){return !disposed&&grant!=null&&grant.Active&&(pointer?grant.pointer:grant.power)&&!Locked();}
 static bool Pending(Job value){return value!=null&&(value.status=="pending"||value.status=="executing");}
 static string Text(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)?value as string:null;}
 static bool Keys(Dictionary<string,object> data,params string[] names){if(data==null)return false;foreach(string key in data.Keys){if(key=="requestId")continue;if(Array.IndexOf(names,key)<0)return false;}return true;}
 static bool Integer(Dictionary<string,object> data,string key,out int result){result=0;object value;if(!data.TryGetValue(key,out value)||value==null||value is bool||value is string)return false;
  if(!(value is int||value is long||value is double||value is decimal))return false;try{double number=Convert.ToDouble(value,CultureInfo.InvariantCulture);if(Double.IsNaN(number)||Double.IsInfinity(number)||number!=Math.Truncate(number)||number<int.MinValue||number>int.MaxValue)return false;result=(int)number;return true;}catch{return false;}}
 static int Clamp(int value,int limit){return Math.Max(-limit,Math.Min(limit,value));}
 internal static bool ValidText(string value){
  if(String.IsNullOrEmpty(value)||value.Length>1024)return false;
  for(int i=0;i<value.Length;i++){if(Char.IsControl(value[i]))return false;if(Char.IsHighSurrogate(value[i])){if(++i>=value.Length||!Char.IsLowSurrogate(value[i]))return false;}else if(Char.IsLowSurrogate(value[i]))return false;}return true;
 }
 static ushort[] KeyCodes(string key){switch(key){case "escape":return new ushort[]{27};case "tab":return new ushort[]{9};case "enter":return new ushort[]{13};case "backspace":return new ushort[]{8};case "left":return new ushort[]{37};case "up":return new ushort[]{38};case "right":return new ushort[]{39};case "down":return new ushort[]{40};case "select-all":return new ushort[]{17,65};case "copy":return new ushort[]{17,67};case "paste":return new ushort[]{17,86};case "undo":return new ushort[]{17,90};case "switch-window":return new ushort[]{18,9};default:return null;}}
 static CompanionReply Error(int status,string code,string message){return CompanionReply.Json(status,new{error=code,message=message});}
 static CompanionReply Invalid(){return Error(400,"invalid_request","控制参数无效，请重新操作。");}
 object JobState(Job value){ReportPower(value);return value==null?(object)new{status="idle",action="",id="",executeAt=0L,message=""}:new{status=value.status,action=value.action,id=value.id,executeAt=Stamp(value.executeAt),message=value.message};}
 void ReportPower(Job value){if(value==null||PowerDiagnostic==null)return;string key=value.id+value.status+value.message;if(key==lastPowerDiagnostic)return;try{PowerDiagnostic(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{time=clock().ToString("o"),action=value.action,status=value.status,message=value.message}));lastPowerDiagnostic=key;}catch{}}
 void CancelPending(string reason){if(job!=null&&(job.status=="pending"||job.status=="executing")&&Interlocked.CompareExchange(ref job.stage,2,0)==0){job.status="cancelled";job.message=reason;ReportPower(job);}}
 void TryRelease(){if(touchReleasePending)try{if(SendTouch!=null&&SendTouch(TouchFrame(touches,new Dictionary<int,Point>(),true))){touches.Clear();touchScreen=null;touchReleasePending=false;}}catch{}if(!releasePending)return;try{if(mouse(new MouseCommand("up",click:"left")))releasePending=false;}catch{}}
 void ReleaseDrag(Lease value){if(value==null||!value.dragging)return;value.dragging=false;value.screen=null;releasePending=true;TryRelease();TryRelease();}
 void TryRestorePointer(){if(!restorePending)return;if(RestorePointerVisibility==null){restorePending=false;return;}if(lease!=null||releasePending||touchReleasePending||Locked())return;try{if(RestorePointerVisibility())restorePending=false;}catch{}}
 void EndLease(){var previous=lease;lease=null;if(previous!=null)restorePending=true;touchReleasePending=touches.Count>0;ReleaseDrag(previous);TryRelease();TryRestorePointer();}
 internal void EndScreen(CompanionGrant owner){lock(gate){if(lease!=null&&ReferenceEquals(lease.owner,owner))EndLease();}}
 void Prune(DateTime now){
  bool unavailable=disposed||Locked();
  TryRelease();TryRestorePointer();
  if(lease!=null&&(unavailable||!lease.owner.Active||!lease.owner.pointer||now>=lease.expires||lease.dragging&&now>=lease.dragExpires||touches.Count>0&&now>=touchExpires))EndLease();
  var remove=new List<CompanionGrant>();foreach(var item in tickets)if(unavailable||!item.Key.Active||!item.Key.power||now>=item.Value.expires)remove.Add(item.Key);foreach(var grant in remove)tickets.Remove(grant);
  if(job!=null&&job.status=="pending"&&(unavailable||!job.owner.Active||!job.owner.power))CancelPending("电脑锁定、权限变更或伴随屏关闭，操作已取消。");
 }
 public object Snapshot(CompanionGrant grant){lock(gate){Prune(clock());bool own=lease!=null&&ReferenceEquals(lease.owner,grant)&&Allowed(grant,true);object power=JobState(null);
  if(Allowed(grant,false)){if(job!=null&&ReferenceEquals(job.owner,grant))power=JobState(job);else if(Pending(job))power=new{status="busy",action="",id="",executeAt=0L,message="另一台设备已有电源操作，请稍后再试。"};}
  return new{pointer=new{active=own,expiresAt=own?Stamp(lease.expires):0},power=power};
 }}
 internal bool PowerActive {get{lock(gate)return Pending(job);}}
 internal bool IsActive {get{lock(gate){Prune(clock());return lease!=null||releasePending||touchReleasePending;}}}
 public CompanionReply Pointer(string path,Dictionary<string,object> request,CompanionGrant grant,Rectangle? capture=null){lock(gate){
  DateTime now=clock();Prune(now);if(!Allowed(grant,true))return Error(403,"forbidden","此设备未获鼠标控制权限，或电脑已锁定。");
  if(path=="/api/pointer/begin"){
   if(!Keys(request))return Invalid();if(releasePending||touchReleasePending)return Error(503,"input_blocked","Windows 暂未接受输入释放，请在电脑端查看后重试。");if(lease!=null&&!ReferenceEquals(lease.owner,grant))return Error(409,"pointer_busy","另一台设备正在控制鼠标，请稍后再试。");
   EndLease();if(releasePending||touchReleasePending)return Error(503,"input_blocked","Windows 暂未接受输入释放，请在电脑端查看后重试。");lease=new Lease{owner=grant,id=Id(),expires=now.AddSeconds(20)};return CompanionReply.Json(200,new{leaseId=lease.id,expiresAt=Stamp(lease.expires)});
  }
  if(path=="/api/pointer/end"){
   if(!Keys(request,"leaseId")||String.IsNullOrEmpty(Text(request,"leaseId")))return Invalid();
   if(lease==null)return CompanionReply.Json(200,new{active=false});
   if(!ReferenceEquals(lease.owner,grant)||Text(request,"leaseId")!=lease.id)return Error(409,"lease_changed","鼠标控制会话已变更。");
   EndLease();return CompanionReply.Json(200,new{active=false});
  }
  if(path!="/api/pointer")return Error(404,"not_found","控制接口不存在。");
  if(lease==null||!ReferenceEquals(lease.owner,grant)||Text(request,"leaseId")!=lease.id)return Error(409,"lease_expired","鼠标控制已结束，请重新开启触控板。");
  int sequence;if(!Integer(request,"seq",out sequence)||sequence<1)return Invalid();
  MouseCommand command=null;Dictionary<int,Point> nextTouch=null;string kind=Text(request,"kind"),phase="";int x,y,steps;
  if(kind=="move"){if(!Keys(request,"leaseId","seq","kind","dx","dy")||!Integer(request,"dx",out x)||!Integer(request,"dy",out y))return Invalid();command=new MouseCommand(kind,Clamp(x,500),Clamp(y,500));}
  else if(kind=="point"){
   phase=request.ContainsKey("phase")?Text(request,"phase"):"click";
   if(!capture.HasValue||capture.Value.Width<1||capture.Value.Height<1||String.IsNullOrEmpty(Text(request,"screenLease"))||!Keys(request,"leaseId","seq","kind","screenLease","x","y","phase")||(phase!="click"&&phase!="start"&&phase!="move")||!Integer(request,"x",out x)||!Integer(request,"y",out y)||x<0||x>65535||y<0||y>65535)return Invalid();
   var area=capture.Value;command=new MouseCommand(kind,area.Left+(int)Math.Round(x*(area.Width-1)/65535.0),area.Top+(int)Math.Round(y*(area.Height-1)/65535.0),click:phase);
  }
  else if(kind=="touch"){
   object raw;var area=capture.GetValueOrDefault();
   if(!capture.HasValue||area.Width<1||area.Height<1||String.IsNullOrEmpty(Text(request,"screenLease"))||!Keys(request,"leaseId","seq","kind","screenLease","contacts")||!request.TryGetValue("contacts",out raw))return Invalid();
   var contacts=raw as System.Collections.IList;if(contacts==null||contacts.Count>2)return Invalid();nextTouch=new Dictionary<int,Point>();
   foreach(object item in contacts){var data=item as Dictionary<string,object>;int id;if(!Keys(data,"id","x","y")||!Integer(data,"id",out id)||id<0||id>1||nextTouch.ContainsKey(id)||!Integer(data,"x",out x)||!Integer(data,"y",out y)||x<0||x>65535||y<0||y>65535)return Invalid();nextTouch.Add(id,new Point(area.Left+(int)Math.Round(x*(area.Width-1)/65535.0),area.Top+(int)Math.Round(y*(area.Height-1)/65535.0)));}
  }
  else if(kind=="scroll"){if(!Keys(request,"leaseId","seq","kind","steps")||!Integer(request,"steps",out steps))return Invalid();command=new MouseCommand(kind,scroll:Clamp(steps,5));}
  else if(kind=="click"){string button=Text(request,"button");if(!Keys(request,"leaseId","seq","kind","button")||(button!="left"&&button!="right"))return Invalid();command=new MouseCommand(kind,click:button);}
  else if(kind=="drag"){phase=Text(request,"phase");if(!Keys(request,"leaseId","seq","kind","phase")||(phase!="start"&&phase!="hold"&&phase!="end"))return Invalid();}
  else if(kind=="keepalive"){if(!Keys(request,"leaseId","seq","kind"))return Invalid();}
  else if(kind=="text"||kind=="key"){
   if(!grant.keyboard)return Error(403,"keyboard_denied","请在电脑为此设备允许键盘输入。");
   string value=Text(request,kind=="text"?"text":"key");if(!Keys(request,"leaseId","seq","kind",kind=="text"?"text":"key")||(kind=="text"?!ValidText(value):KeyCodes(value)==null))return Invalid();
   command=new MouseCommand(kind,inputText:kind=="text"?value:"",keyName:kind=="key"?value:"");
  }
  else return Invalid();
  if(sequence<=lease.seq)return CompanionReply.Json(200,new{ignored=true,seq=lease.seq,expiresAt=Stamp(lease.expires)});
  // Record before injection: even a partial native failure must never replay a click.
  lease.seq=sequence;if(!Allowed(grant,true)||((kind=="text"||kind=="key")&&!grant.keyboard)){EndLease();return Error(403,"forbidden","输入权限已撤销或电脑已锁定。");}
  if(touches.Count>0&&kind!="touch"&&kind!="keepalive"||kind=="touch"&&lease.dragging)return Error(409,"touch_active","请先抬起手指，再切换输入方式。");
  if(kind=="touch"){
   if(touchScreen!=null&&touchScreen!=Text(request,"screenLease")){EndLease();return Error(409,"screen_changed","触控来源已变化，请重新开始。");}
   var frame=TouchFrame(touches,nextTouch,false);bool accepted=frame.Length==0;
   // Failed frames are not committed; cancellation uses the last accepted contact positions.
   try{if(!accepted)accepted=SendTouch!=null&&SendTouch(frame);}catch{}
   if(!accepted){EndLease();return Error(503,"touch_unavailable","Windows 未接受原生触摸，请停止后使用直接点选或触控板。");}
   touches.Clear();foreach(var p in nextTouch)touches.Add(p.Key,p.Value);touchScreen=touches.Count>0?Text(request,"screenLease"):null;touchExpires=now.AddMilliseconds(750);
  }
  if(kind=="point"&&phase=="move"&&(!lease.dragging||lease.screen!=Text(request,"screenLease")))return Error(409,"drag_expired","画面拖动已结束，请松开手指后重试。");
  if(lease.screen!=null&&(kind=="move"||kind=="drag"&&phase!="end"))return Error(409,"drag_active","请先结束画面拖动。");
  if(lease.dragging&&(kind=="point"&&phase!="move"||kind=="click"||kind=="scroll"||kind=="text"||kind=="key"))return Error(409,"drag_active","请先松开拖拽，再使用其他输入。");
  if(kind=="point"&&phase=="start"){lease.dragging=true;lease.screen=Text(request,"screenLease");}
  if(kind=="drag"){
   if(phase=="end"){ReleaseDrag(lease);if(releasePending){EndLease();return Error(503,"input_blocked","Windows 暂未接受鼠标松开，请在电脑端查看后重试。");}}
   else if(phase=="hold"&&!lease.dragging)return Error(409,"drag_expired","拖拽已结束，请松开手指后重新操作。");
   else if(phase=="start"&&!lease.dragging){lease.dragging=true;command=new MouseCommand("down",click:"left");}
  }
  bool sent=false;try{sent=command==null||(kind=="move"&&command.dx==0&&command.dy==0)||(kind=="scroll"&&command.steps==0)||mouse(command);}catch{}finally{if(!sent)EndLease();}
  if(!sent)return Error(503,"input_blocked","Windows 未接受鼠标输入；较高权限窗口或系统安全界面可能阻止控制。请在电脑端操作。");
  if(lease.dragging&&kind!="keepalive")lease.dragExpires=now.AddMilliseconds(750);
  lease.expires=now.AddSeconds(20);return CompanionReply.Json(200,new{ignored=false,seq=sequence,expiresAt=Stamp(lease.expires)});
 }}
 public CompanionReply Power(string path,Dictionary<string,object> request,CompanionGrant grant){lock(gate){
  DateTime now=clock();Prune(now);if(!Allowed(grant,false))return Error(403,"forbidden","此设备未获电源控制权限，或电脑已锁定。");
  if(path=="/api/power/prepare"){
   string action=Text(request,"action");if(!Keys(request,"action")||(action!="lock"&&action!="sleep"&&action!="restart"&&action!="shutdown"))return Invalid();
   if(Pending(job))return Error(409,"power_busy","已有电源操作正在等待或执行，请先取消或等待完成。");
   if(!tickets.ContainsKey(grant)&&tickets.Count>=8)return Error(429,"too_many_confirmations","待确认操作较多，请稍后重试。");
   var ticket=new Ticket{id=Id(),action=action,expires=now.AddSeconds(30)};tickets[grant]=ticket;
   return CompanionReply.Json(200,new{ticketId=ticket.id,action=ticket.action,expiresAt=Stamp(ticket.expires)});
  }
  if(path=="/api/power/commit"){
   if(!Keys(request,"ticketId")||String.IsNullOrEmpty(Text(request,"ticketId")))return Invalid();Ticket ticket;
   if(!tickets.TryGetValue(grant,out ticket)||Text(request,"ticketId")!=ticket.id)return Error(409,"confirmation_expired","确认已过期或已使用，请重新选择电源操作。");
   if(Pending(job))return Error(409,"power_busy","已有电源操作正在等待或执行。");
   if(!Allowed(grant,false)){tickets.Remove(grant);return Error(403,"forbidden","电源权限已撤销或电脑已锁定。");}
   tickets.Remove(grant);job=new Job{owner=grant,id=Id(),action=ticket.action,status="pending",executeAt=now.AddSeconds(ticket.action=="lock"?3:ticket.action=="sleep"?5:15),message="倒计时中，可以取消。"};
   return CompanionReply.Json(202,JobState(job));
  }
  if(path=="/api/power/cancel"){
   if(!Keys(request,"jobId")||String.IsNullOrEmpty(Text(request,"jobId")))return Invalid();
   if(job==null||!ReferenceEquals(job.owner,grant)||Text(request,"jobId")!=job.id)return Error(404,"job_not_found","未找到此设备的电源任务。");
   if(job.status=="pending")CancelPending("已取消，本次电源操作不会执行。");
   else if(job.status!="cancelled")return Error(409,"already_executing","操作已提交给 Windows，取消窗口已结束。请在电脑端查看结果。");
   return CompanionReply.Json(200,JobState(job));
  }
  return Error(404,"not_found","控制接口不存在。");
 }}
 internal void Tick(){Job due=null;lock(gate){DateTime now=clock();Prune(now);if(disposed||job==null||job.status!="pending"||now<job.executeAt)return;
  if(!Allowed(job.owner,false)){CancelPending("权限已变更或电脑已锁定，操作已取消。");return;}
  due=job;due.status="executing";due.message="正在向 Windows 提交操作。";ReportPower(due);EndLease();tickets.Clear();
 }
 // Native power calls may block until resume. Do not hold the state lock across them.
 string failure;bool cancelled=false;try{if(!Allowed(due.owner,false)||Interlocked.CompareExchange(ref due.stage,1,0)!=0){cancelled=true;failure="权限已变更、操作已取消或电脑已锁定。";}else if(!Allowed(due.owner,false)){cancelled=true;failure="权限已变更或电脑已锁定，操作已取消。";}else failure=executePower(due.action);}catch{failure="Windows 未接受此电源操作，请在电脑端检查权限与系统状态。";}
 lock(gate){if(ReferenceEquals(job,due)){due.status=cancelled?"cancelled":String.IsNullOrEmpty(failure)?"submitted":"failed";due.message=String.IsNullOrEmpty(failure)?(due.action=="shutdown"||due.action=="restart"?"Windows 已接受请求；若电脑仍未关闭，请检查电脑上的未保存应用或关机提示。":"已提交给 Windows，请在电脑端确认结果。"):failure;ReportPower(due);}}
 }
 public void CancelAll(){lock(gate){EndLease();tickets.Clear();CancelPending("伴随屏已关闭或电脑已锁定，操作已取消。");}}
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;EndLease();tickets.Clear();CancelPending("伴随屏已关闭，操作已取消。");}if(timer!=null)timer.Dispose();}

 [StructLayout(LayoutKind.Sequential)]internal struct TouchPointer {
  public uint type,id,frame,flags;public IntPtr source,target;public Point pixel,himetric,raw,rawHimetric;
  public uint time,history;public int input;public uint keys;public ulong counter;public uint button;
 }
 [StructLayout(LayoutKind.Sequential)]internal struct TouchRect {public int left,top,right,bottom;}
 [StructLayout(LayoutKind.Sequential)]internal struct TouchContact {public TouchPointer pointer;public uint flags,mask;public TouchRect area,rawArea;public uint orientation,pressure;}
 internal static TouchContact[] TouchFrame(Dictionary<int,Point> previous,Dictionary<int,Point> next,bool cancel){
  var result=new List<TouchContact>();for(int id=0;id<2;id++){Point point;bool continuing=next.TryGetValue(id,out point);if(!continuing&&!previous.TryGetValue(id,out point))continue;
   result.Add(new TouchContact{pointer=new TouchPointer{type=2,id=(uint)id,pixel=point,flags=continuing?(previous.ContainsKey(id)?0x20006u:0x10006u):cancel?0x48000u:0x40000u},mask=5,pressure=512,area=new TouchRect{left=point.X-2,top=point.Y-2,right=point.X+2,bottom=point.Y+2}});
  }return result.ToArray();
 }
 static bool touchInitialized;
 [DllImport("user32.dll",SetLastError=true)]static extern bool InitializeTouchInjection(uint count,uint feedback);
 [DllImport("user32.dll",SetLastError=true)]static extern bool InjectTouchInput(uint count,[In]TouchContact[] contacts);
 static bool InjectTouches(TouchContact[] frame){
  if(!touchInitialized){if(!InitializeTouchInjection(2,1))return false;touchInitialized=true;}
  for(int attempt=0;attempt<3;attempt++){
   if(InjectTouchInput((uint)frame.Length,frame))return true;int error=Marshal.GetLastWin32Error();
   // Windows can cancel the sequence first; a repeated cancellation then has no live contact to release.
   if(error==87){foreach(var contact in frame)if(contact.pointer.flags!=0x48000u)return false;return frame.Length>0;}
   if(error!=21)return false;Thread.Sleep(1);
  }return false;
 }
 [StructLayout(LayoutKind.Sequential)]internal struct Input {public uint type;public InputUnion data;}
 [StructLayout(LayoutKind.Explicit)]internal struct InputUnion {[FieldOffset(0)]public MouseInput mouse;[FieldOffset(0)]public KeyboardInput keyboard;}
 [StructLayout(LayoutKind.Sequential)]internal struct MouseInput {public int x,y;public uint data,flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Sequential)]internal struct KeyboardInput {public ushort key,scan;public uint flags,time;public UIntPtr extra;}
 internal static Input[] Inputs(MouseCommand command){
  if(command.kind=="text"){
   if(!ValidText(command.text))throw new ArgumentException("Invalid input text");var textInputs=new List<Input>();foreach(char c in command.text){textInputs.Add(Key(0,c,4));textInputs.Add(Key(0,c,6));}return textInputs.ToArray();
  }
  if(command.kind=="key"){
   var codes=KeyCodes(command.key);if(codes==null)throw new ArgumentException("Unsupported key");var keyInputs=new List<Input>();foreach(ushort code in codes)keyInputs.Add(Key(code,0,code>=37&&code<=40?1u:0u));for(int i=codes.Length-1;i>=0;i--)keyInputs.Add(Key(codes[i],0,(codes[i]>=37&&codes[i]<=40?1u:0u)|2u));return keyInputs.ToArray();
  }
  if(command.kind=="point"){
   int left=GetSystemMetrics(76),top=GetSystemMetrics(77),width=GetSystemMetrics(78),height=GetSystemMetrics(79);
   if(width<1||height<1||command.dx<left||command.dx>=left+width||command.dy<top||command.dy>=top+height)throw new ArgumentException("Capture outside virtual desktop");
   var position=Mouse((int)(((long)(command.dx-left)*65536+32768)/width),(int)(((long)(command.dy-top)*65536+32768)/height),0,0xC001);
   if(command.button=="move")return new[]{position};
   if(command.button=="start")return new[]{position,Mouse(0,0,0,2)};
   return new[]{position,Mouse(0,0,0,2),Mouse(0,0,0,4)};
  }
  if(command.kind=="move")return new[]{Mouse(command.dx,command.dy,0,0x0001)};
  if(command.kind=="scroll")return new[]{Mouse(0,0,unchecked((uint)(command.steps*120)),0x0800)};
  if(command.kind=="down")return new[]{Mouse(0,0,0,0x0002)};
  if(command.kind=="up")return new[]{Mouse(0,0,0,0x0004)};
  if(command.kind!="click"||(command.button!="left"&&command.button!="right"))throw new ArgumentException("Unsupported pointer input");
  return command.button=="left"?new[]{Mouse(0,0,0,0x0002),Mouse(0,0,0,0x0004)}:new[]{Mouse(0,0,0,0x0008),Mouse(0,0,0,0x0010)};
 }
 [DllImport("user32.dll")]static extern int GetSystemMetrics(int index);
 [StructLayout(LayoutKind.Sequential)]internal struct CursorInfo {public int size,flags;public IntPtr handle;public int x,y;}
 [DllImport("user32.dll")]internal static extern bool GetCursorInfo(ref CursorInfo info);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 internal static bool RestorePointer(){
  var info=new CursorInfo{size=Marshal.SizeOf(typeof(CursorInfo))};
  if(!GetCursorInfo(ref info))return false;
  if((info.flags&2)!=0){
   if(GetAsyncKeyState(1)<0||GetAsyncKeyState(2)<0||GetAsyncKeyState(4)<0||GetAsyncKeyState(5)<0||GetAsyncKeyState(6)<0)return false;
   // A same-position message clears suppression but can leave no cursor; deliver both moves without coalescing.
   int x=info.x+1<GetSystemMetrics(76)+GetSystemMetrics(78)?info.x+1:info.x-1;
   var away=Inputs(new MouseCommand("point",x,info.y,click:"move"))[0];var back=Inputs(new MouseCommand("point",info.x,info.y,click:"move"))[0];
   away.data.mouse.flags|=0x2000;back.data.mouse.flags|=0x2000;return SendInput(2,new[]{away,back},Marshal.SizeOf(typeof(Input)))==2;
  }
  return true;
 }
 static Input Mouse(int x,int y,uint data,uint flags){return new Input{type=0,data=new InputUnion{mouse=new MouseInput{x=x,y=y,data=data,flags=flags}}};}
 static Input Key(ushort key,ushort scan,uint flags){return new Input{type=1,data=new InputUnion{keyboard=new KeyboardInput{key=key,scan=scan,flags=flags}}};}
 static bool SendMouse(MouseCommand command){var inputs=Inputs(command);uint sent=SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(Input)));if(sent==inputs.Length)return true;
  if((command.kind=="click"||command.kind=="point")&&sent>0)SendInput(1,new[]{Mouse(0,0,0,command.kind=="click"&&command.button=="right"?16u:4u)},Marshal.SizeOf(typeof(Input)));
  if((command.kind=="key"||command.kind=="text")&&sent>0){var release=new List<Input>();for(int i=(int)sent-1;i>=0;i--)if((inputs[i].data.keyboard.flags&2)==0){var input=inputs[i];input.data.keyboard.flags|=2;release.Add(input);}if(release.Count>0)SendInput((uint)release.Count,release.ToArray(),Marshal.SizeOf(typeof(Input)));}return false;
 }
 static string NativeFailure(string action){int code=Marshal.GetLastWin32Error();return code==5||code==1300||code==1314?"当前用户没有执行此电源操作的权限，请在电脑端操作。":"Windows 未接受“"+(action=="lock"?"锁定":action=="sleep"?"睡眠":action=="restart"?"重启":"关机")+"”（错误 "+code.ToString(CultureInfo.InvariantCulture)+"），请在电脑端检查。";}
 static string SendPower(string action){
  if(action=="lock")return LockWorkStation()?"":NativeFailure(action);
  IntPtr token=IntPtr.Zero;TokenPrivileges previous=new TokenPrivileges();bool changed=false;
  try{Luid privilege;if(!OpenProcessToken(GetCurrentProcess(),0x0020|0x0008,out token)||!LookupPrivilegeValue(null,"SeShutdownPrivilege",out privilege))return NativeFailure(action);
   var next=new TokenPrivileges{count=1,luid=privilege,attributes=2};int size;
   if(!AdjustTokenPrivileges(token,false,ref next,Marshal.SizeOf(typeof(TokenPrivileges)),out previous,out size)||Marshal.GetLastWin32Error()!=0)return NativeFailure(action);changed=previous.count>0;
   bool accepted=action=="sleep"?SetSuspendState(false,false,false):InitiateSystemShutdownEx(null,"PrismGlass 伴随屏请求",0,false,action=="restart",0x80040000);
   return accepted?"":NativeFailure(action);
  }finally{if(token!=IntPtr.Zero){if(changed){TokenPrivileges ignored;int length;AdjustTokenPrivileges(token,false,ref previous,Marshal.SizeOf(typeof(TokenPrivileges)),out ignored,out length);}CloseHandle(token);}}
 }
 [StructLayout(LayoutKind.Sequential)]struct Luid {public uint low;public int high;}
 [StructLayout(LayoutKind.Sequential)]struct TokenPrivileges {public uint count;public Luid luid;public uint attributes;}
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] inputs,int size);
 [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool LockWorkStation();
 [DllImport("powrprof.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.U1)]static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)]bool hibernate,[MarshalAs(UnmanagedType.U1)]bool force,[MarshalAs(UnmanagedType.U1)]bool disableWakeEvents);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool InitiateSystemShutdownEx(string machine,string message,uint timeout,bool forceAppsClosed,bool reboot,uint reason);
 [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool LookupPrivilegeValue(string system,string name,out Luid luid);
 [DllImport("advapi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool AdjustTokenPrivileges(IntPtr token,bool disableAll,ref TokenPrivileges value,int length,out TokenPrivileges previous,out int returned);
 [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32.dll")][return:MarshalAs(UnmanagedType.Bool)]static extern bool CloseHandle(IntPtr handle);
}
