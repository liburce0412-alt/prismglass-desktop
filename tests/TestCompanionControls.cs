using System;using System.Collections.Generic;using System.Linq;using System.Runtime.InteropServices;using System.Text;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;
internal static class TestCompanionControls {
 static int checks;static readonly JavaScriptSerializer json=new JavaScriptSerializer();
 sealed class Fixture:IDisposable{
  public DateTime now=new DateTime(2026,10,26,12,0,0,DateTimeKind.Utc);public bool locked,mouseOk=true;public string powerFailure="";public int lockReads;public Action onLockRead;public Func<CompanionControls.MouseCommand,bool> onMouse;public readonly List<CompanionControls.MouseCommand> inputs=new List<CompanionControls.MouseCommand>();public readonly List<string> powerCalls=new List<string>();public CompanionControls controls;
  public Fixture(){controls=new CompanionControls(()=>{lockReads++;if(onLockRead!=null)onLockRead();return locked;},()=>now,command=>{inputs.Add(command);return onMouse==null?mouseOk:onMouse(command);},action=>{powerCalls.Add(action);return powerFailure;});}
  public void Dispose(){controls.Dispose();}
  public void Advance(double seconds){now=now.AddSeconds(seconds);controls.Tick();}
 }
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static Dictionary<string,object> Body(params object[] entries){var result=new Dictionary<string,object>();for(int i=0;i<entries.Length;i+=2)result.Add((string)entries[i],entries[i+1]);return result;}
 static Dictionary<string,object> Map(object value){return json.Deserialize<Dictionary<string,object>>(json.Serialize(value));}
 static Dictionary<string,object> Map(CompanionReply reply){return json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(reply.Body));}
 static Dictionary<string,object> Part(object value,string name){return (Dictionary<string,object>)Map(value)[name];}
 static CompanionGrant Grant(){return new CompanionGrant{Active=true,pointer=true,power=true};}
 static string Begin(Fixture f,CompanionGrant grant){return (string)Map(f.controls.Pointer("/api/pointer/begin",Body(),grant))["leaseId"];}
 static CompanionReply Move(Fixture f,CompanionGrant grant,string lease,int seq,object x,object y){return f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",seq,"kind","move","dx",x,"dy",y),grant);}
 static CompanionReply Drag(Fixture f,CompanionGrant grant,string lease,int seq,string phase){return f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",seq,"kind","drag","phase",phase),grant);}
 static CompanionReply Keepalive(Fixture f,CompanionGrant grant,string lease,int seq){return f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",seq,"kind","keepalive"),grant);}
 static string Prepare(Fixture f,CompanionGrant grant,string action){return (string)Map(f.controls.Power("/api/power/prepare",Body("action",action),grant))["ticketId"];}
 static CompanionReply Commit(Fixture f,CompanionGrant grant,string ticket){return f.controls.Power("/api/power/commit",Body("ticketId",ticket),grant);}
 static Dictionary<string,object> Schedule(Fixture f,CompanionGrant grant,string action){return Map(Commit(f,grant,Prepare(f,grant,action)));}
 static string Status(Fixture f,CompanionGrant grant){return (string)Part(f.controls.Snapshot(grant),"power")["status"];}
 static void RealTimerChecks(){
  int calls=0;long elapsed=0;var timer=System.Diagnostics.Stopwatch.StartNew();var grant=Grant();
  using(var invoked=new ManualResetEvent(false))using(var controls=new CompanionControls(()=>false,()=>DateTime.UtcNow,command=>true,action=>{elapsed=timer.ElapsedMilliseconds;Interlocked.Increment(ref calls);invoked.Set();return "";},true)){
   string ticket=(string)Map(controls.Power("/api/power/prepare",Body("action","shutdown"),grant))["ticketId"];
   timer.Restart();Check(controls.Power("/api/power/commit",Body("ticketId",ticket),grant).Status==202,"real timer accepts a countdown with a fake power executor");
   Check(invoked.WaitOne(18000),"real timer reaches the fake executor without manual Tick calls");
   Check(elapsed>=15000&&elapsed<=18000,"real shutdown countdown observes its fifteen-second deadline");
   Check(SpinWait.SpinUntil(()=>(string)Part(controls.Snapshot(grant),"power")["status"]=="submitted",2000),"real timer publishes executor completion for browser polling");
   Thread.Sleep(300);Check(calls==1,"later timer ticks cannot execute the same power job twice");
  }
 }
 static void DragChecks(){
  using(var f=new Fixture()){
   var owner=Grant();int attempts=0;f.controls.RestorePointerVisibility=()=>{attempts++;return false;};
   Begin(f,owner);f.controls.EndScreen(owner);Begin(f,owner);int before=attempts;
   f.Advance(.25);Check(attempts==before,"deferred cursor recovery cannot move a new active pointer session");
   f.controls.EndScreen(owner);Check(attempts>before,"ending the new session permits recovery again");
  }
  using(var f=new Fixture()){
   var owner=Grant();string lease=Begin(f,owner);Drag(f,owner,lease,1,"start");int restored=0;
   f.controls.RestorePointerVisibility=()=>{Check(f.inputs.Last().kind=="up","cursor recovery follows mouse release");restored++;return true;};
   f.mouseOk=false;f.controls.EndScreen(owner);Check(restored==0,"failed mouse release defers cursor movement");
   f.mouseOk=true;f.Advance(.25);Check(restored==1&&!f.controls.IsActive,"successful release retry restores the pointer");f.Advance(.25);Check(restored==1,"completed cursor recovery never repeats");
   Begin(f,owner);f.locked=true;f.controls.EndScreen(owner);Check(restored==1,"locked desktop never receives cursor recovery input");f.locked=false;f.Advance(.25);Check(restored==2,"unlock permits pending pointer recovery");
  }
  using(var f=new Fixture()){
   var owner=Grant();string lease=Begin(f,owner);Drag(f,owner,lease,1,"start");
   f.controls.EndScreen(Grant());Check(f.controls.IsActive&&f.inputs.Count==1,"stopping another device screen cannot release this pointer");
   f.controls.EndScreen(owner);Check(!f.controls.IsActive&&f.inputs.Select(i=>i.kind).SequenceEqual(new[]{"down","up"}),"stopping the owner screen releases the mouse and ends touch control");
   Check(Move(f,owner,lease,2,5,0).Status==409,"late input cannot continue after screen stop");
   f.controls.EndScreen(owner);Check(f.inputs.Count==2,"duplicate screen stop does not inject input");
  }
  using(var f=new Fixture()){
   var a=Grant();var b=Grant();string lease=Begin(f,a);
   Check(Drag(f,b,lease,1,"start").Status==409&&f.inputs.Count==0,"drag requires ownership of the authenticated lease");
   Check(Drag(f,a,lease,1,"invalid").Status==400&&f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",1,"kind","drag","phase","start","button","right"),a).Status==400,"drag has fixed phases and cannot choose arbitrary buttons");
   Check(Drag(f,a,lease,1,"hold").Status==409&&f.inputs.Count==0,"heartbeat cannot press a mouse button without a drag start");
   Check(Drag(f,a,lease,2,"start").Status==200&&f.inputs.Select(i=>i.kind).SequenceEqual(new[]{"down"}),"drag start presses left exactly once");
   Drag(f,a,lease,2,"start");Drag(f,a,lease,3,"start");Check(f.inputs.Count==1,"duplicate and repeated drag starts never press twice");
   f.Advance(.5);Check(Drag(f,a,lease,4,"hold").Status==200&&f.inputs.Count==1,"fresh heartbeat extends held drag without injecting mouse input");f.Advance(.5);Check((bool)Part(f.controls.Snapshot(a),"pointer")["active"],"stationary drag remains active with bounded heartbeat traffic");
   Check(Move(f,a,lease,5,3,-2).Status==200&&f.inputs.Last().kind=="move","held drag accepts ordinary relative movement");Check(f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",6,"kind","click","button","right"),a).Status==409,"click cannot accidentally release or change a held drag button");
   Check(Drag(f,a,lease,7,"end").Status==200&&f.inputs.Last().kind=="up","explicit drag end releases the held left button");int count=f.inputs.Count;Drag(f,a,lease,7,"end");Drag(f,a,lease,8,"end");f.controls.CancelAll();Check(f.inputs.Count==count,"duplicate end and later stop do not invent extra button presses or releases");
  }
  foreach(string reason in new[]{"end","expiry","lock","revoke","permission","restart","stop","dispose","power"})using(var f=new Fixture()){
   var grant=Grant();if(reason=="power"){Schedule(f,grant,"lock");f.now=f.now.AddSeconds(2.9);}string lease=Begin(f,grant);Drag(f,grant,lease,1,"start");
   if(reason=="end")f.controls.Pointer("/api/pointer/end",Body("leaseId",lease),grant);
   else if(reason=="expiry")f.Advance(.75);
   else if(reason=="lock"){f.locked=true;f.Advance(.01);}
   else if(reason=="revoke"){grant.Active=false;f.Advance(.01);}
   else if(reason=="permission"){grant.pointer=false;f.Advance(.01);}
   else if(reason=="restart")Begin(f,grant);
   else if(reason=="stop")f.controls.CancelAll();
   else if(reason=="dispose")f.controls.Dispose();
   else f.Advance(.1);
   Check(f.inputs.Select(i=>i.kind).SequenceEqual(new[]{"down","up"}),"held mouse button is released on "+reason);
   if(reason=="expiry")Check(Drag(f,grant,lease,2,"hold").Status==409,"late heartbeat cannot resurrect a drag after disconnect timeout");
  }
  foreach(bool throwing in new[]{false,true})using(var f=new Fixture()){
   var grant=Grant();string lease=Begin(f,grant);f.onMouse=command=>{if(command.kind=="down"){if(throwing)throw new InvalidOperationException("fixture only");return false;}return true;};
   Check(Drag(f,grant,lease,1,"start").Status==503&&f.inputs.Select(i=>i.kind).SequenceEqual(new[]{"down","up"})&&!(bool)Part(f.controls.Snapshot(grant),"pointer")["active"],"failed drag start always attempts finally release, throwing="+throwing);
  }
  using(var f=new Fixture()){
   var grant=Grant();string lease=Begin(f,grant);Drag(f,grant,lease,1,"start");f.onMouse=command=>command.kind!="move";Check(Move(f,grant,lease,2,1,1).Status==503&&f.inputs.Select(i=>i.kind).SequenceEqual(new[]{"down","move","up"}),"input failure during drag releases the button before ending its lease");
  }
  using(var f=new Fixture()){
   var grant=Grant();string lease=Begin(f,grant);Drag(f,grant,lease,1,"start");int releases=0;f.onMouse=command=>command.kind!="up"||++releases>1;Drag(f,grant,lease,2,"end");Check(releases==2&&f.inputs.Last().kind=="up","failed release receives one bounded immediate retry");
  }
  using(var f=new Fixture()){
   var grant=Grant();string lease=Begin(f,grant);Drag(f,grant,lease,1,"start");f.mouseOk=false;Check(Drag(f,grant,lease,2,"end").Status==503&&f.controls.IsActive&&f.controls.Pointer("/api/pointer/begin",Body(),grant).Status==503,"unaccepted release stays visibly active and blocks a new press");int before=f.inputs.Count;f.mouseOk=true;f.Advance(.25);Check(f.inputs.Count==before+1&&f.inputs.Last().kind=="up"&&!f.controls.IsActive&&f.controls.Pointer("/api/pointer/begin",Body(),grant).Status==200,"timer retries pending mouse-up after Windows input becomes available again");
  }
  using(var f=new Fixture()){
   var grant=Grant();string lease=Begin(f,grant);Drag(f,grant,lease,1,"start");f.mouseOk=false;Check(f.controls.Pointer("/api/pointer/begin",Body(),grant).Status==503&&!(bool)Part(f.controls.Snapshot(grant),"pointer")["active"]&&f.inputs.Count(i=>i.kind=="down")==1,"restarting a held drag cannot create a new lease when its old release fails");
  }
  var down=CompanionControls.Inputs(new CompanionControls.MouseCommand("down",click:"left"));var up=CompanionControls.Inputs(new CompanionControls.MouseCommand("up",click:"left"));Check(down.Length==1&&down[0].data.mouse.flags==2&&up.Length==1&&up[0].data.mouse.flags==4,"native drag structs encode explicit left press and release without invoking SendInput");
 }
 static void Main(){
  using(var f=new Fixture()){
   var grant=Grant();string lease=Begin(f,grant);bool healthy=true;
   for(int seq=1;seq<=15;seq++){f.Advance(5);healthy&=Keepalive(f,grant,lease,seq).Status==200;}
   Check(healthy&&f.controls.IsActive&&(bool)Part(f.controls.Snapshot(grant),"pointer")["active"]&&f.inputs.Count==0,"authenticated keepalives preserve enabled idle touch beyond a minute without injecting input");
   var expires=Part(f.controls.Snapshot(grant),"pointer")["expiresAt"];f.Advance(19);
   Check((bool)Map(Keepalive(f,grant,lease,15))["ignored"]&&Equals(expires,Part(f.controls.Snapshot(grant),"pointer")["expiresAt"]),"replayed keepalives do not renew a lease");
   Check(f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",16,"kind","keepalive","dx",1),grant).Status==400&&f.inputs.Count==0,"keepalive cannot smuggle input fields");
   f.now=f.now.AddSeconds(1);Check(!f.controls.IsActive&&Keepalive(f,grant,lease,16).Status==409,"live status prunes a lost lease even before the periodic timer runs");
   lease=Begin(f,grant);Drag(f,grant,lease,1,"start");f.Advance(.5);Keepalive(f,grant,lease,2);f.Advance(.25);
   Check(f.inputs.Select(i=>i.kind).SequenceEqual(new[]{"down","up"})&&!(bool)Part(f.controls.Snapshot(grant),"pointer")["active"],"idle keepalive cannot extend the separate held-button safety deadline");
   lease=Begin(f,grant);grant.pointer=false;Check(Keepalive(f,grant,lease,1).Status==403,"keepalive cannot preserve revoked pointer permission");grant.pointer=true;lease=Begin(f,grant);f.locked=true;
   Check(Keepalive(f,grant,lease,1).Status==403&&!(bool)Part(f.controls.Snapshot(grant),"pointer")["active"],"locking the desktop ends a lease even when keepalives arrive");
  }
  Check(!new CompanionGrant().keyboard,"existing devices do not silently gain keyboard permission");
  using(var f=new Fixture()){
   var grant=Grant();string lease=Begin(f,grant);var text=Body("leaseId",lease,"seq",1,"kind","text","text","你好 iPad 😀");
   Check(f.controls.Pointer("/api/pointer",text,grant).Status==403&&f.inputs.Count==0,"mouse permission alone cannot type text");grant.keyboard=true;
   Check(f.controls.Pointer("/api/pointer",text,grant).Status==200&&f.inputs.Last().text=="你好 iPad 😀","authorized lease accepts composed Unicode text");
   f.controls.Pointer("/api/pointer",text,grant);Check(f.inputs.Count==1,"replayed text sequence is never typed twice");
   foreach(string invalid in new[]{"",new string('a',1025),"line\nreturn","\ud800","\udc00"})Check(f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",2,"kind","text","text",invalid),grant).Status==400,"invalid or oversized keyboard text is rejected");
   Check(f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",2,"kind","key","key","run-shell"),grant).Status==400,"keyboard only accepts the fixed key list");
   Check(f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",2,"kind","key","key","copy","flags",4),grant).Status==400,"keyboard cannot supply raw native input flags");
   Check(f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",2,"kind","key","key","copy"),grant).Status==200&&f.inputs.Last().key=="copy","authorized keyboard sends a complete shortcut");
   grant.keyboard=false;Check(f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",3,"kind","key","key","enter"),grant).Status==403,"keyboard revocation blocks new keys while preserving separate mouse permission");
   grant.keyboard=true;f.Advance(20);Check(f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",3,"kind","key","key","enter"),grant).Status==409,"keyboard cannot reuse an expired input lease");
  }
  var unicode=CompanionControls.Inputs(new CompanionControls.MouseCommand("text",inputText:"中😀"));Check(unicode.Length==6&&unicode.All(i=>i.type==1)&&unicode[0].data.keyboard.scan=='中'&&unicode[0].data.keyboard.flags==4&&unicode[1].data.keyboard.flags==6,"Unicode input preserves UTF-16 pairs with a release for each packet");
  var shortcut=CompanionControls.Inputs(new CompanionControls.MouseCommand("key",keyName:"copy"));Check(shortcut.Select(i=>i.data.keyboard.key).SequenceEqual(new ushort[]{17,67,67,17})&&shortcut.Select(i=>i.data.keyboard.flags).SequenceEqual(new uint[]{0,0,2,2}),"shortcut releases modifiers in reverse order in the same native input batch");
  Check(!new CompanionGrant().pointer&&!new CompanionGrant().power,"new and old grants default to no pointer or power permission");
  using(var f=new Fixture()){
   var a=Grant();var b=Grant();var denied=new CompanionGrant{Active=true};
   Check(f.controls.Pointer("/api/pointer/begin",Body(),denied).Status==403&&f.controls.Power("/api/power/prepare",Body("action","lock"),denied).Status==403,"separate explicit permissions gate both control families");
   Check(f.controls.Pointer("/api/pointer/begin",Body("deviceId","forged"),a).Status==400,"client-supplied device identity cannot choose lease ownership");
   string lease=Begin(f,a);Check((bool)Part(f.controls.Snapshot(a),"pointer")["active"]&&f.controls.Pointer("/api/pointer/begin",Body(),b).Status==409,"only one authenticated session owns the pointer at a time");
   Check(Move(f,b,lease,1,10,20).Status==409&&f.inputs.Count==0,"knowing another device lease cannot inject input");
   Check(Move(f,a,lease,1,900,-800).Status==200&&f.inputs.Count==1&&f.inputs[0].dx==500&&f.inputs[0].dy==-500,"relative pointer movement is clamped to 500 pixels per axis");
   Move(f,a,lease,1,900,-800);Move(f,a,lease,0,1,1);Move(f,a,lease,3,2,3);var old=Move(f,a,lease,2,4,5);
   Check(f.inputs.Count==2&&(bool)Map(old)["ignored"],"duplicate and out-of-order sequence numbers never repeat input");
   Check(Move(f,a,lease,4,1.25,0).Status==400&&Move(f,a,lease,4,"5",0).Status==400&&Move(f,a,lease,4,Double.NaN,0).Status==400,"fractional, string and nonfinite movement values are rejected");
   var raw=Body("leaseId",lease,"seq",4,"kind","click","button","left","flags",2);
   Check(f.controls.Pointer("/api/pointer",raw,a).Status==400&&f.inputs.Count==2,"raw input flags are never accepted");
   f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",4,"kind","scroll","steps",80),a);
   Check(f.inputs.Last().steps==5,"wheel motion is clamped to five notches");
   f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",5,"kind","click","button","right"),a);
   Check(f.inputs.Last().kind=="click"&&f.inputs.Last().button=="right","right click is a fixed permitted command");
   Check(f.controls.Pointer("/api/pointer",Body("leaseId",lease,"seq",6,"kind","down","button","left"),a).Status==400,"raw button-down packets remain forbidden outside the bounded drag protocol");
   var expires=Part(f.controls.Snapshot(a),"pointer")["expiresAt"];f.now=f.now.AddSeconds(19);Move(f,a,lease,5,1,1);Check(Equals(expires,Part(f.controls.Snapshot(a),"pointer")["expiresAt"]),"replayed packets do not extend an idle pointer lease");
   f.Advance(1);Check(Move(f,a,lease,6,1,1).Status==409&&!(bool)Part(f.controls.Snapshot(a),"pointer")["active"],"pointer lease expires after twenty idle seconds");
   lease=Begin(f,a);f.now=f.now.AddSeconds(15);Move(f,a,lease,1,1,1);f.Advance(6);Check((bool)Part(f.controls.Snapshot(a),"pointer")["active"],"valid pointer traffic renews the idle lease");
   string replacement=Begin(f,a);Check(replacement!=lease&&Move(f,a,lease,2,1,1).Status==409,"restarting the same device creates a new lease and rejects queued old packets");
   f.mouseOk=false;Check(Move(f,a,replacement,1,1,1).Status==503&&!(bool)Part(f.controls.Snapshot(a),"pointer")["active"],"failed or UIPI-blocked injection ends the lease instead of replaying input");
   f.mouseOk=true;lease=Begin(f,a);a.Active=false;f.Advance(.1);Check(Move(f,a,lease,1,1,1).Status==403,"revoking a session clears its pointer lease");
   a=Grant();lease=Begin(f,a);f.locked=true;f.Advance(.1);f.locked=false;Check(Move(f,a,lease,1,1,1).Status==409,"locking the desktop invalidates the old lease after unlock");
   lease=Begin(f,a);Check(f.controls.Pointer("/api/pointer/end",Body("leaseId",lease),a).Status==200&&f.controls.Pointer("/api/pointer/end",Body("leaseId",lease),a).Status==200,"ending a lease is idempotent");
   lease=Begin(f,a);f.controls.CancelAll();Check(Move(f,a,lease,1,1,1).Status==409,"service stop clears the lease without sending mouse-up guesses");
  }
  using(var f=new Fixture()){
   var a=Grant();var b=Grant();Check(f.controls.Power("/api/power/prepare",Body("action","hibernate"),a).Status==400&&f.controls.Power("/api/power/prepare",Body("action","shutdown","force",true),a).Status==400,"power actions are fixed and cannot accept forced shutdown arguments");
   string ticket=Prepare(f,a,"shutdown");Check(f.powerCalls.Count==0&&Status(f,a)=="idle","preparing a power confirmation executes nothing");
   Check(Commit(f,b,ticket).Status==409,"power confirmation is bound to the original session object");
   f.Advance(30);Check(Commit(f,a,ticket).Status==409&&f.powerCalls.Count==0,"power confirmation expires at thirty seconds");
   ticket=Prepare(f,a,"restart");var reply=Commit(f,a,ticket);var scheduled=Map(reply);string id=(string)scheduled["id"];
   Check(reply.Status==202&&Status(f,a)=="pending"&&Convert.ToInt64(scheduled["executeAt"])-(long)(f.now-new DateTime(1970,1,1)).TotalMilliseconds==15000,"restart is scheduled with a cancellable fifteen-second countdown");
   Check(Commit(f,a,ticket).Status==409&&f.powerCalls.Count==0,"one-shot confirmation cannot create a second job");
   Check(Status(f,b)=="busy"&&(string)Part(f.controls.Snapshot(b),"power")["id"]==""&&f.controls.Power("/api/power/cancel",Body("jobId",id),b).Status==404,"other devices cannot inspect or cancel the owner's job");
   f.Advance(14);Check(f.powerCalls.Count==0,"power execution never happens before its deadline");
   Check(f.controls.Power("/api/power/cancel",Body("jobId",id),a).Status==200&&f.controls.Power("/api/power/cancel",Body("jobId",id),a).Status==200,"pending cancellation is idempotent and only affects this job");
   f.Advance(10);Check(f.powerCalls.Count==0&&Status(f,a)=="cancelled","cancelled countdown cannot execute later");
   Schedule(f,a,"lock");f.Advance(2.9);Check(f.powerCalls.Count==0,"lock also waits for a response-safe three-second countdown");f.Advance(.1);f.Advance(10);
   Check(f.powerCalls.SequenceEqual(new[]{"lock"})&&Status(f,a)=="submitted","timer submits exactly once and distinguishes submission from system completion");
   scheduled=Schedule(f,a,"sleep");f.Advance(4.9);Check(f.powerCalls.Count==1,"sleep uses a five-second countdown");f.Advance(.1);Check(f.powerCalls.Last()=="sleep"&&f.powerCalls.Count==2,"sleep reaches only the injected executor after its deadline");
   scheduled=Schedule(f,a,"shutdown");f.powerFailure="权限不足";f.Advance(15);Check(Status(f,a)=="failed"&&(string)Part(f.controls.Snapshot(a),"power")["message"]=="权限不足","native failure remains visible instead of reporting shutdown success");
   Check(f.controls.Power("/api/power/cancel",Body("jobId",scheduled["id"]),a).Status==409,"cancellation cannot abort an unrelated or already submitted Windows shutdown");
   f.powerFailure="";Schedule(f,a,"shutdown");int count=f.powerCalls.Count;a.Active=false;f.Advance(15);a.Active=true;
   Check(f.powerCalls.Count==count&&Status(f,a)=="cancelled","revocation before a timer fires cancels the pending power action");
   Schedule(f,a,"restart");f.locked=true;f.Advance(.1);f.locked=false;f.Advance(15);Check(f.powerCalls.Count==count&&Status(f,a)=="cancelled","locking while a power job waits permanently cancels it");
   ticket=Prepare(f,a,"lock");f.controls.CancelAll();Check(Commit(f,a,ticket).Status==409,"stop clears uncommitted confirmation tickets");
   Schedule(f,a,"shutdown");f.controls.CancelAll();f.Advance(15);Check(f.powerCalls.Count==count&&Status(f,a)=="cancelled","stop cancels all in-memory countdowns");
   Schedule(f,a,"restart");f.controls.Dispose();f.Advance(15);Check(f.powerCalls.Count==count,"disposing the module never lets a pending job execute");
  }
  using(var f=new Fixture()){
   var a=Grant();Schedule(f,a,"shutdown");f.now=f.now.AddSeconds(15);int reads=0;f.onLockRead=()=>{if(++reads==3)a.Active=false;};f.controls.Tick();
   Check(f.powerCalls.Count==0,"timer rechecks authority immediately before calling the executor");
  }
  using(var f=new Fixture()){
   var a=Grant();Schedule(f,a,"shutdown");f.now=f.now.AddSeconds(15);int reads=0;f.onLockRead=()=>{if(++reads==3)f.controls.CancelAll();};f.controls.Tick();
   Check(f.powerCalls.Count==0,"cancel arriving after the timer picks a job still prevents queued execution");
  }
  using(var f=new Fixture()){
   var a=Grant();string lease=Begin(f,a);var results=Task.WhenAll(Enumerable.Range(0,12).Select(i=>Task.Run(()=>Move(f,a,lease,1,1,1)))).Result;
   Check(f.inputs.Count==1&&results.All(r=>r.Status==200),"concurrent duplicate pointer requests inject exactly once");
   Schedule(f,a,"shutdown");f.now=f.now.AddSeconds(15);Task.WaitAll(Enumerable.Range(0,12).Select(i=>Task.Run(()=>f.controls.Tick())).ToArray());Check(f.powerCalls.Count==1,"overlapping timer callbacks submit a power job exactly once");
  }
  string longText=new string('中',500)+"，标点😀";Check(CompanionControls.ValidText(longText)&&CompanionControls.Inputs(new CompanionControls.MouseCommand("text",inputText:longText)).Length==longText.Length*2,"500 Chinese characters and emoji retain all Unicode press/release pairs");
  var left=CompanionControls.Inputs(new CompanionControls.MouseCommand("click",click:"left"));var right=CompanionControls.Inputs(new CompanionControls.MouseCommand("click",click:"right"));
  Check(left.Length==2&&left[0].data.mouse.flags==2&&left[1].data.mouse.flags==4&&right[0].data.mouse.flags==8&&right[1].data.mouse.flags==16,"native click batches contain both press and release in order");
  var move=CompanionControls.Inputs(new CompanionControls.MouseCommand("move",-12,15))[0];var wheel=CompanionControls.Inputs(new CompanionControls.MouseCommand("scroll",scroll:-5))[0];
  Check(move.type==0&&move.data.mouse.flags==1&&move.data.mouse.x==-12&&move.data.mouse.y==15&&wheel.data.mouse.flags==0x800&&unchecked((int)wheel.data.mouse.data)==-600,"native pointer structs encode only relative movement and bounded wheel units");
  Check(Marshal.SizeOf(typeof(CompanionControls.Input))==(IntPtr.Size==8?40:28),"native INPUT layout matches the process architecture without calling SendInput");
  using(var f=new Fixture()){
   var grant=Grant();string lease=Begin(f,grant);var point=Body("leaseId",lease,"seq",1,"kind","point","screenLease","screen","x",0,"y",65535);var area=new System.Drawing.Rectangle(-1280,-100,1280,720);
   Check(f.controls.Pointer("/api/pointer",point,grant).Status==400&&f.inputs.Count==0,"direct point requires trusted capture bounds");
   Check(f.controls.Pointer("/api/pointer",point,grant,area).Status==200&&f.inputs.Last().dx==-1280&&f.inputs.Last().dy==619,"direct coordinates include negative display origin and bottom edge");
   f.controls.Pointer("/api/pointer",point,grant,area);Check(f.inputs.Count==1,"direct point sequence never clicks twice");
   point["seq"]=2;point["x"]=65536;Check(f.controls.Pointer("/api/pointer",point,grant,area).Status==400&&f.inputs.Count==1,"out of frame point cannot inject input");
   point["x"]=32768;point["phase"]="move";Check(f.controls.Pointer("/api/pointer",point,grant,area).Status==409,"absolute drag move requires a held button");
   point["seq"]=3;point["phase"]="start";Check(f.controls.Pointer("/api/pointer",point,grant,area).Status==200&&f.inputs.Last().button=="start","direct drag begins at its mapped screen position");
   int count=f.inputs.Count;f.controls.Pointer("/api/pointer",point,grant,area);Check(f.inputs.Count==count,"replayed direct press never presses twice");
   Check(Move(f,grant,lease,4,2,3).Status==409&&Drag(f,grant,lease,5,"hold").Status==409,"direct drag cannot be moved or renewed without a live capture binding");
   point["seq"]=6;point["phase"]="move";point["screenLease"]="replacement";Check(f.controls.Pointer("/api/pointer",point,grant,area).Status==409,"new screen cannot reuse a held direct drag");
   point["seq"]=7;point["screenLease"]="screen";Check(f.controls.Pointer("/api/pointer",point,grant,area).Status==200&&f.inputs.Last().button=="move","direct drag updates the original capture position");
   f.Advance(.75);Check(f.inputs.Last().kind=="up"&&!f.controls.IsActive,"missing direct movement releases the held button within its safety deadline");
   lease=Begin(f,grant);point["leaseId"]=lease;point["seq"]=1;point["phase"]="start";f.mouseOk=false;
   Check(f.controls.Pointer("/api/pointer",point,grant,area).Status==503&&f.inputs.Last().kind=="up","failed direct press attempts release and invalidates its lease");
  }
  using(var f=new Fixture()){
   var records=new List<string>();f.controls.PowerDiagnostic=value=>records.Add(value);var grant=Grant();
   Schedule(f,grant,"shutdown");f.powerFailure="Windows error 1314";f.Advance(15);
   Check(records.Count==3&&records[0].Contains("pending")&&records[1].Contains("executing")&&records[2].Contains("failed")&&records[2].Contains("1314"),"power diagnostics retain the last execution stage and failure");
   f.controls.Snapshot(grant);Check(records.Count==3&&!records[2].Contains("\"id\"")&&!records[2].Contains("ticket"),"polling does not rewrite diagnostics or persist session identifiers");
   f.controls.PowerDiagnostic=value=>{throw new System.IO.IOException();};f.powerFailure="";Schedule(f,grant,"restart");f.Advance(15);Check(Status(f,grant)=="submitted","diagnostic write failure cannot block an authorized power operation");
  }
  TouchChecks();DragChecks();RealTimerChecks();Console.WriteLine("Completed "+checks+" companion control checks; all input and power executors were fake.");
 }
 static Dictionary<string,object> Contact(int id,int x=20000,int y=30000){return Body("id",id,"x",x,"y",y);}
 static CompanionReply Touch(Fixture f,CompanionGrant owner,string lease,int seq,params object[] points){return f.controls.Pointer("/api/pointer",Body("kind","touch","leaseId",lease,"seq",seq,"screenLease","screen","contacts",points),owner,new System.Drawing.Rectangle(-1280,0,1280,720));}
 static void TouchChecks(){
  using(var f=new Fixture()){
   var frames=new List<CompanionControls.TouchContact[]>();f.controls.SendTouch=frame=>{frames.Add(frame);return true;};var owner=Grant();string lease=Begin(f,owner);
   Check(Touch(f,Grant(),lease,1,Contact(0)).Status==409&&frames.Count==0,"touch cannot steal another device lease");
   Check(Touch(f,owner,lease,1,Contact(0),Contact(0)).Status==400&&frames.Count==0,"duplicate contact ids rejected before injection");
   Check(Touch(f,owner,lease,1,Contact(2)).Status==400&&frames.Count==0,"touch ids limited to two native slots");
   Check(Touch(f,owner,lease,1,Contact(0,-1)).Status==400&&frames.Count==0,"out of range touch coordinates rejected");
   Check(Touch(f,owner,lease,1,null).Status==400&&frames.Count==0,"null contact arrays rejected");
   Check(Touch(f,owner,lease,1,Contact(0,0,65535)).Status==200&&frames[0][0].pointer.flags==0x10006&&frames[0][0].pointer.pixel==new System.Drawing.Point(-1280,719),"touch maps capture edges including negative desktop coordinates");
   Touch(f,owner,lease,1,Contact(0));Check(frames.Count==1,"duplicate touch frames never inject twice");
   Touch(f,owner,lease,2,Contact(0),Contact(1));Check(frames[1].Length==2&&frames[1][0].pointer.flags==0x20006&&frames[1][1].pointer.flags==0x10006,"adding second finger updates first and presses only new contact");
   Check(Move(f,owner,lease,3,5,0).Status==409&&f.inputs.Count==0,"active native touches exclude mouse input");
   var held=frames[1][0].pointer.pixel;Touch(f,owner,lease,4,Contact(1));Check(frames[2][0].pointer.flags==0x40000&&frames[2][0].pointer.pixel==held&&frames[2][1].pointer.flags==0x20006,"lift uses last accepted position and preserves other finger");
   f.Advance(.5);Touch(f,owner,lease,5,Contact(1));f.Advance(.5);Check(f.controls.IsActive,"touch update keeps a stationary press alive");
   Touch(f,owner,lease,6);Check(frames.Last().Length==1&&frames.Last()[0].pointer.flags==0x40000,"empty frame releases final native contact");
   Check(Move(f,owner,lease,7,1,1).Status==200,"mouse input resumes after all native touches lift");
  }
  foreach(string reason in new[]{"end","screen","expiry","lock","revoke","stop","dispose","failure"})using(var f=new Fixture()){
   var frames=new List<CompanionControls.TouchContact[]>();f.controls.SendTouch=frame=>{frames.Add(frame);return true;};var owner=Grant();string lease=Begin(f,owner);Touch(f,owner,lease,1,Contact(0),Contact(1));
   if(reason=="end")f.controls.Pointer("/api/pointer/end",Body("leaseId",lease),owner);
   else if(reason=="screen")f.controls.EndScreen(owner);
   else if(reason=="expiry"){f.Advance(.5);Keepalive(f,owner,lease,2);f.Advance(.25);}
   else if(reason=="lock"){f.locked=true;f.Advance(.01);}
   else if(reason=="revoke"){owner.Active=false;f.Advance(.01);}
   else if(reason=="stop")f.controls.CancelAll();else if(reason=="dispose")f.controls.Dispose();
   else {f.controls.SendTouch=frame=>{frames.Add(frame);return frame[0].pointer.flags==0x48000;};Touch(f,owner,lease,2,Contact(0,40000));}
   Check(frames.Last().Length==2&&frames.Last().All(p=>p.pointer.flags==0x48000)&&!f.controls.IsActive,"native contacts cancelled on "+reason);
  }
  using(var f=new Fixture()){
   var owner=Grant();string lease=Begin(f,owner);bool accept=true;int restored=0;f.controls.SendTouch=frame=>accept;f.controls.RestorePointerVisibility=()=>{restored++;return true;};Touch(f,owner,lease,1,Contact(0));accept=false;f.controls.EndScreen(owner);
   Check(f.controls.IsActive&&restored==0&&f.controls.Pointer("/api/pointer/begin",Body(),owner).Status==503,"failed touch cancellation blocks new input and cursor movement");accept=true;f.Advance(.25);Check(!f.controls.IsActive&&restored==1,"touch cancellation retry releases input before restoring cursor");
  }
 }
}
