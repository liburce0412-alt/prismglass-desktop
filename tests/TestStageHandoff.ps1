$ErrorActionPreference='Stop'
$src=Join-Path $PSScriptRoot '../src/LiquidDesktop'
$stage=Get-Content (Join-Path $src 'StageManager.cs') -Raw
$motion=Get-Content (Join-Path $src 'NativeWindowMotion.cs') -Raw
function Method([string]$source,[string]$name,[string]$next){
 $start=$source.IndexOf($name)
 if($start -lt 0){throw "Missing $name"}
 $source.Substring($start,$source.IndexOf($next,$start)-$start)
}
$restore=Method $stage ' static bool Restore(Member member' ' internal sealed class PlacementLane'
$lane=Method $stage ' internal sealed class PlacementLane' ' async Task<bool> WatchPlacement('
$switch=Method $stage ' async Task<bool> SwitchMember(' ' internal async void Switch('
$retire=Method $stage ' bool RetireWanted(' ' CardPose Card('
$async=Method $motion ' internal static async Task<bool> SetStateAsync' ' async Task<bool> Minimize(Flight'
$spring=$motion.Substring($motion.IndexOf(' internal static bool AdvanceMotion('))
# End at the method boundary, independently of fields following the spring.
$spring=$spring.Substring(0,$spring.IndexOf('return true;')+'return true;'.Length)+'}'
$code=@"
using System;using System.IO;using System.Threading.Tasks;using System.Runtime.InteropServices;using System.Collections.Generic;using System.Linq;
public sealed class HandoffFixture{
 internal class DockWindow{public long handle=7;public int pid=17;}
 struct Rect {public int l,t,r,b;public Rect(int n){l=n;t=0;r=n+20;b=50;}} struct Placement{public int length,show,flags;public Rect normal;}
 class Member{public string placementFailure="";public DockWindow window=new DockWindow();public Placement original,layout,geometryIntent;public int visibilityIntent;public readonly PlacementLane placements=new PlacementLane();}
 class Group{public readonly List<Member> members=new List<Member>();public string monitor="A";}
 class Surface{public bool disposed;public void Dispose(){disposed=true;}}
 class StageFlight{public Member member=new Member();public bool hiddenForSnapshot=true;public readonly Surface surface=new Surface();}
 static bool hung,iconic,rejectGeometry;static int writes,suppressions,placementReads,postedShows,restoredOpacity;static Placement actual;
 int epoch=1,saved,refreshed,retiring;
 bool enabled=true,switching;
 string recoveryWarning="",Error="",edgeOpen="";
 readonly List<Group> groups=new List<Group>();readonly List<Member> unresolved=new List<Member>();readonly List<StageFlight> flights=new List<StageFlight>();readonly Dictionary<string,Group> active=new Dictionary<string,Group>();
 readonly Dictionary<string,bool> areas=new Dictionary<string,bool>{{"A",true}};readonly Dictionary<long,int> unmanaged=new Dictionary<long,int>();
 readonly TaskCompletionSource<bool> backgroundRestore=new TaskCompletionSource<bool>();
 void Save(){saved++;}void RefreshStrip(string monitor){refreshed++;}void ForgetPicture(long handle){}
 void Stop(){enabled=false;}bool MonitorPaused(string monitor){return false;}
 Task Capture(Member member,int version){return Task.FromResult(true);}
 Task<bool> Minimize(Member member,Func<bool> wanted){return Task.FromResult(false);}
 Task<bool> Place(Member member,bool original){return backgroundRestore.Task;}
 static Placement Read(DockWindow w){return actual;}
 static class DockBackend{
  internal static bool Valid(DockWindow w){return true;}internal static bool CanManage(DockWindow w){return true;}
  internal static Task<bool> SetMinimizedAsync(DockWindow w,bool m,Func<bool> f){return Task.FromResult(true);}
 }
 static class NativeWindowMotion{internal static bool IsIconic(IntPtr h){return iconic;}internal static void RestoreOpacity(IntPtr h){restoredOpacity++;}}
 static bool IsIconic(IntPtr h){return iconic;}
 static int Suppress(DockWindow w){return ++suppressions;}static void Unsuppress(int state){}
 static bool IsHungAppWindow(IntPtr h){return hung;}
 static bool SetWindowPlacement(IntPtr h,ref Placement p){writes++;actual=p;if(rejectGeometry)actual.normal=new Rect(-55);return true;}
 static bool GetWindowPlacement(IntPtr h,ref Placement p){placementReads++;p=actual;return true;}
 static bool ShowWindowAsync(IntPtr h,int cmd){postedShows++;return true;}
 static int DwmGetWindowAttribute(IntPtr h,int key,out int value,int size){value=0;return 0;}
 static int DwmSetWindowAttribute(IntPtr h,int key,ref int value,int size){writes++;return 0;}
$restore
$lane
$switch
$retire
$async
$spring
 public static void Run(){
  using(var entered=new System.Threading.ManualResetEventSlim())using(var unblock=new System.Threading.ManualResetEventSlim()){
   var queue=new PlacementLane();int state=0,obsolete=0;
   var first=queue.Submit(()=>{entered.Set();unblock.Wait();state=1;return true;});
   if(!entered.Wait(2000))throw new Exception("placement worker never started");
   var middle=queue.Submit(()=>{obsolete++;state=2;return true;});var last=queue.Submit(()=>{state=3;return true;});
   if(last.IsCompleted||!queue.Busy)throw new Exception("serialized handoff did not wait");
   unblock.Set();if(!Task.WaitAll(new Task[]{first,middle,last},2000)||state!=3||obsolete!=0)throw new Exception("stale placement survived correction");
  }
  var m=new Member{original=new Placement{show=1,normal=new Rect(1),flags=0},layout=new Placement{show=1,normal=new Rect(2)}};
  hung=true;if(Restore(m,false)||writes!=0)throw new Exception("hung placement wrote external state");
  hung=false;if(!Restore(m,false)||writes!=3)throw new Exception("responsive placement lost");
  iconic=true;if(!SetStateAsync(m.window,true).Result||suppressions!=0)throw new Exception("redundant DWM changes");
  if(!SetStateAsync(m.window,false).Result||suppressions!=1)throw new Exception("missing handoff");
  double p=0,v=0;AdvanceMotion(ref p,ref v,1,.02);double before=p;AdvanceMotion(ref p,ref v,0,.0001);
  if(p<=before||v<=0)throw new Exception("reversal discontinuity");
  for(int n=0;n<100;n++)AdvanceMotion(ref p,ref v,0,.016);
  if(p!=0||v!=0)throw new Exception("reversal failed to settle");
  var stage=new HandoffFixture();var healthy=new Group();var target=new Group();target.members.Add(m);
  stage.groups.Add(healthy);stage.groups.Add(target);stage.active["A"]=healthy;
  var flight=new StageFlight{member=m};stage.flights.Add(flight);
  actual.show=2;rejectGeometry=true;
  if(!stage.SwitchMembers(target.members,false,1).Result||!stage.groups.SequenceEqual(new[]{healthy})||stage.active["A"]!=healthy)throw new Exception("failed switch disturbed a healthy group");
  if(!stage.unresolved.SequenceEqual(new[]{m})||m.geometryIntent.normal.l!=-55||m.visibilityIntent!=1||stage.saved<2||stage.refreshed!=1||stage.recoveryWarning.Length==0)throw new Exception("partial native write was not retained for recovery");
  if(!flight.surface.disposed||stage.flights.Count!=0||restoredOpacity!=1||postedShows!=0)throw new Exception("failed switch left an overlay or posted an unconfirmed show");
  foreach(bool superseded in new[]{false,true}){
   var retirement=new HandoffFixture();var front=new Group();var background=new Group();background.members.Add(m);
   retirement.groups.Add(front);retirement.groups.Add(background);retirement.active["A"]=front;
   retirement.RetireBackground("A",1);if(retirement.retiring!=1)throw new Exception("background recovery was not pending");
   retirement.switching=superseded;retirement.backgroundRestore.SetResult(false);
   if(retirement.retiring!=0||!retirement.enabled||retirement.Error.Length>0||retirement.active["A"]!=front)throw new Exception("background failure stopped active Stage");
   if(superseded){
    if(!retirement.groups.Contains(background)||retirement.unresolved.Count!=0)throw new Exception("obsolete background recovery changed membership");
   }else if(retirement.groups.Contains(background)||!retirement.unresolved.SequenceEqual(new[]{m})||retirement.saved!=1)throw new Exception("failed background member did not retain recovery ownership");
  }
 }
}
"@
Add-Type -TypeDefinition $code
[HandoffFixture]::Run()
'PASS hung placement, responsive placement, idempotent handoff, spring reversal, and isolated or superseded background recovery'
