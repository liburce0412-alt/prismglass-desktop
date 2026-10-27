using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

// Session objects, rather than client-supplied device IDs, own every lease and confirmation.
internal sealed class CompanionControls : IDisposable {
 internal sealed class MouseCommand {
  public readonly string kind,button;public readonly int dx,dy,steps;
  public MouseCommand(string type,int x=0,int y=0,int scroll=0,string click=""){kind=type;dx=x;dy=y;steps=scroll;button=click;}
 }
 sealed class Lease {public CompanionGrant owner;public string id;public int seq;public DateTime expires,dragExpires;public bool dragging;}
 sealed class Ticket {public string id,action;public DateTime expires;}
 sealed class Job {public CompanionGrant owner;public string id,action,status,message;public DateTime executeAt;public int stage;}
 readonly object gate=new object();readonly Func<bool> locked;readonly Func<DateTime> clock;readonly Func<MouseCommand,bool> mouse;readonly Func<string,string> executePower;readonly Timer timer;
 readonly Dictionary<CompanionGrant,Ticket> tickets=new Dictionary<CompanionGrant,Ticket>();Lease lease;Job job;volatile bool disposed;bool releasePending;
 public CompanionControls(Func<bool> isLocked):this(isLocked,()=>DateTime.UtcNow,SendMouse,SendPower,true){}
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
 static CompanionReply Error(int status,string code,string message){return CompanionReply.Json(status,new{error=code,message=message});}
 static CompanionReply Invalid(){return Error(400,"invalid_request","控制参数无效，请重新操作。");}
 static object JobState(Job value){return value==null?(object)new{status="idle",action="",id="",executeAt=0L,message=""}:new{status=value.status,action=value.action,id=value.id,executeAt=Stamp(value.executeAt),message=value.message};}
 void CancelPending(string reason){if(job!=null&&(job.status=="pending"||job.status=="executing")&&Interlocked.CompareExchange(ref job.stage,2,0)==0){job.status="cancelled";job.message=reason;}}
 void TryRelease(){if(!releasePending)return;try{if(mouse(new MouseCommand("up",click:"left")))releasePending=false;}catch{}}
 void ReleaseDrag(Lease value){if(value==null||!value.dragging)return;value.dragging=false;releasePending=true;TryRelease();TryRelease();}
 void EndLease(){var previous=lease;lease=null;ReleaseDrag(previous);}
 void Prune(DateTime now){
  bool unavailable=disposed||Locked();
  TryRelease();
  if(lease!=null&&(unavailable||!lease.owner.Active||!lease.owner.pointer||now>=lease.expires||lease.dragging&&now>=lease.dragExpires))EndLease();
  var remove=new List<CompanionGrant>();foreach(var item in tickets)if(unavailable||!item.Key.Active||!item.Key.power||now>=item.Value.expires)remove.Add(item.Key);foreach(var grant in remove)tickets.Remove(grant);
  if(job!=null&&job.status=="pending"&&(unavailable||!job.owner.Active||!job.owner.power))CancelPending("电脑锁定、权限变更或伴随屏关闭，操作已取消。");
 }
 public object Snapshot(CompanionGrant grant){lock(gate){Prune(clock());bool own=lease!=null&&ReferenceEquals(lease.owner,grant)&&Allowed(grant,true);object power=JobState(null);
  if(Allowed(grant,false)){if(job!=null&&ReferenceEquals(job.owner,grant))power=JobState(job);else if(Pending(job))power=new{status="busy",action="",id="",executeAt=0L,message="另一台设备已有电源操作，请稍后再试。"};}
  return new{pointer=new{active=own,expiresAt=own?Stamp(lease.expires):0},power=power};
 }}
 public CompanionReply Pointer(string path,Dictionary<string,object> request,CompanionGrant grant){lock(gate){
  DateTime now=clock();Prune(now);if(!Allowed(grant,true))return Error(403,"forbidden","此设备未获鼠标控制权限，或电脑已锁定。");
  if(path=="/api/pointer/begin"){
   if(!Keys(request))return Invalid();if(releasePending)return Error(503,"input_blocked","Windows 暂未接受鼠标松开，请在电脑端查看后重试。");if(lease!=null&&!ReferenceEquals(lease.owner,grant))return Error(409,"pointer_busy","另一台设备正在控制鼠标，请稍后再试。");
   EndLease();if(releasePending)return Error(503,"input_blocked","Windows 暂未接受鼠标松开，请在电脑端查看后重试。");lease=new Lease{owner=grant,id=Id(),expires=now.AddSeconds(20)};return CompanionReply.Json(200,new{leaseId=lease.id,expiresAt=Stamp(lease.expires)});
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
  MouseCommand command=null;string kind=Text(request,"kind"),phase="";int x,y,steps;
  if(kind=="move"){if(!Keys(request,"leaseId","seq","kind","dx","dy")||!Integer(request,"dx",out x)||!Integer(request,"dy",out y))return Invalid();command=new MouseCommand(kind,Clamp(x,500),Clamp(y,500));}
  else if(kind=="scroll"){if(!Keys(request,"leaseId","seq","kind","steps")||!Integer(request,"steps",out steps))return Invalid();command=new MouseCommand(kind,scroll:Clamp(steps,5));}
  else if(kind=="click"){string button=Text(request,"button");if(!Keys(request,"leaseId","seq","kind","button")||(button!="left"&&button!="right"))return Invalid();command=new MouseCommand(kind,click:button);}
  else if(kind=="drag"){phase=Text(request,"phase");if(!Keys(request,"leaseId","seq","kind","phase")||(phase!="start"&&phase!="hold"&&phase!="end"))return Invalid();}
  else return Invalid();
  if(sequence<=lease.seq)return CompanionReply.Json(200,new{ignored=true,seq=lease.seq,expiresAt=Stamp(lease.expires)});
  // Record before injection: even a partial native failure must never replay a click.
  lease.seq=sequence;if(!Allowed(grant,true)){EndLease();return Error(403,"forbidden","鼠标权限已撤销或电脑已锁定。");}
  if(lease.dragging&&(kind=="click"||kind=="scroll"))return Error(409,"drag_active","请先松开拖拽，再点击或滚动。");
  if(kind=="drag"){
   if(phase=="end"){ReleaseDrag(lease);if(releasePending){EndLease();return Error(503,"input_blocked","Windows 暂未接受鼠标松开，请在电脑端查看后重试。");}}
   else if(phase=="hold"&&!lease.dragging)return Error(409,"drag_expired","拖拽已结束，请松开手指后重新操作。");
   else if(phase=="start"&&!lease.dragging){lease.dragging=true;command=new MouseCommand("down",click:"left");}
  }
  bool sent=false;try{sent=command==null||(kind=="move"&&command.dx==0&&command.dy==0)||(kind=="scroll"&&command.steps==0)||mouse(command);}catch{}finally{if(!sent)EndLease();}
  if(!sent)return Error(503,"input_blocked","Windows 未接受鼠标输入；较高权限窗口或系统安全界面可能阻止控制。请在电脑端操作。");
  if(lease.dragging)lease.dragExpires=now.AddMilliseconds(750);
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
  due=job;due.status="executing";due.message="正在向 Windows 提交操作。";EndLease();tickets.Clear();
 }
 // Native power calls may block until resume. Do not hold the state lock across them.
 string failure;bool cancelled=false;try{if(!Allowed(due.owner,false)||Interlocked.CompareExchange(ref due.stage,1,0)!=0){cancelled=true;failure="权限已变更、操作已取消或电脑已锁定。";}else if(!Allowed(due.owner,false)){cancelled=true;failure="权限已变更或电脑已锁定，操作已取消。";}else failure=executePower(due.action);}catch{failure="Windows 未接受此电源操作，请在电脑端检查权限与系统状态。";}
 lock(gate){if(ReferenceEquals(job,due)){due.status=cancelled?"cancelled":String.IsNullOrEmpty(failure)?"submitted":"failed";due.message=String.IsNullOrEmpty(failure)?"已提交给 Windows，请在电脑端确认结果。":failure;}}
 }
 public void CancelAll(){lock(gate){EndLease();tickets.Clear();CancelPending("伴随屏已关闭或电脑已锁定，操作已取消。");}}
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;EndLease();tickets.Clear();CancelPending("伴随屏已关闭，操作已取消。");}if(timer!=null)timer.Dispose();}

 [StructLayout(LayoutKind.Sequential)]internal struct Input {public uint type;public InputUnion data;}
 [StructLayout(LayoutKind.Explicit)]internal struct InputUnion {[FieldOffset(0)]public MouseInput mouse;}
 [StructLayout(LayoutKind.Sequential)]internal struct MouseInput {public int x,y;public uint data,flags,time;public UIntPtr extra;}
 internal static Input[] Inputs(MouseCommand command){
  if(command.kind=="move")return new[]{Mouse(command.dx,command.dy,0,0x0001)};
  if(command.kind=="scroll")return new[]{Mouse(0,0,unchecked((uint)(command.steps*120)),0x0800)};
  if(command.kind=="down")return new[]{Mouse(0,0,0,0x0002)};
  if(command.kind=="up")return new[]{Mouse(0,0,0,0x0004)};
  if(command.kind!="click"||(command.button!="left"&&command.button!="right"))throw new ArgumentException("Unsupported pointer input");
  return command.button=="left"?new[]{Mouse(0,0,0,0x0002),Mouse(0,0,0,0x0004)}:new[]{Mouse(0,0,0,0x0008),Mouse(0,0,0,0x0010)};
 }
 static Input Mouse(int x,int y,uint data,uint flags){return new Input{type=0,data=new InputUnion{mouse=new MouseInput{x=x,y=y,data=data,flags=flags}}};}
 static bool SendMouse(MouseCommand command){var inputs=Inputs(command);uint sent=SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(Input)));if(sent==inputs.Length)return true;
  if(command.kind=="click"&&sent>0)SendInput(1,new[]{inputs[1]},Marshal.SizeOf(typeof(Input)));return false;
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
