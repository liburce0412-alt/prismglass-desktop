$ErrorActionPreference='Stop'
$s=Get-Content (Join-Path $PSScriptRoot '../src/LiquidDesktop/StageManager.cs') -Raw
function Block([string]$first,[string]$end){$i=$s.IndexOf($first);$s.Substring($i,$s.IndexOf($end,$i)-$i)}
$types=Block ' [StructLayout' ' internal sealed class Member'
$recover=Block ' static Placement RecoveryTarget(' ' async Task RecoverMember('
$edge=Block 'string edgeOpen,edgeCandidate;' ' internal void DisplaysChanged('
$save=Block ' void Save(' ' static Placement RecoveryTarget('
$clear=Block ' void ClearStoppedRecovery(' ' async Task<bool> Minimize('
$drain=Block ' internal async Task DrainClose(' ' public void Dispose('
$code=@"
using System;using System.Diagnostics;using System.Drawing;using System.IO;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Threading.Tasks;
public sealed class RecoveryFixture{
$types
 class Lane{internal bool Busy{get{return false;}}}
 class Member{public Placement original,layout,geometryIntent;public int recoveryVersion,visibilityIntent;internal readonly Lane placements=new Lane();internal string name="";}
 class Group{internal readonly List<Member> members=new List<Member>();}
 class JavaScriptSerializer{internal string Serialize(IEnumerable<Member> members){return String.Join(",",members.Select(m=>m.name));}}
 static class DesktopSettingsFiles{internal static void Write(string path,string text){File.WriteAllText(path,text);}}
 readonly List<Group> groups=new List<Group>();readonly List<Member> unresolved=new List<Member>();
 readonly string journal;
 volatile bool journalClosed;
 int pendingStates;
 bool enabled=false,stopping=false,recovering=false,stoppedRestored;
 string saved="";
 void Dispose(){enabled=false;}
 public RecoveryFixture(string path){journal=path;}
 class StageStrip{internal Rectangle OccupiedBounds=new Rectangle(8,80,240,420);}
 Dictionary<string,StageStrip> strips=new Dictionary<string,StageStrip>();HashSet<string> fullscreen=new HashSet<string>{"A"};bool suspended=false,switching=false;
$recover
$edge
$save
$clear
$drain
 static void Check(bool ok,string s){if(!ok)throw new Exception(s);}
 public void Run(){
  var a=new Placement{length=44,show=1,normal=new RECT{l=10,t=10,r=210,b=180}};
  var m=new Member{original=a,layout=a};var current=a;current.show=3;current.normal.l=40;
  var target=RecoveryTarget(m,current);Check(target.show==3&&target.normal.l==40,"legacy external maximize/movement");
  current.show=2;Check(RecoveryTarget(m,current).show==1,"legacy Stage minimize");
  m.recoveryVersion=1;Check(RecoveryTarget(m,current).show==2,"unowned minimize");
  m.visibilityIntent=2;m.geometryIntent=current;target=RecoveryTarget(m,current);
  Check(target.show==1&&target.normal.l==10,"owned minimize and geometry");
  current.normal.l=55;Check(RecoveryTarget(m,current).normal.l==55,"later user movement");
  var area=new Rectangle(0,0,1200,800);strips["A"]=new StageStrip();
  EdgeReveal("A",area,new Point(1,400));Check(MonitorPaused("A"),"edge must dwell");
  edgeSince=DateTime.UtcNow.AddMilliseconds(-250);EdgeReveal("A",area,new Point(1,400));Check(!MonitorPaused("A"),"covered ordinary window can reveal");
  EdgeReveal("A",area,new Point(50,400));edgeKeep=DateTime.UtcNow.AddMilliseconds(-1);EdgeReveal("A",area,new Point(700,400));Check(MonitorPaused("A"),"leave retracts");
  EdgeReveal(null,area,new Point(1,400));Check(MonitorPaused("A"),"exclusive/no eligible target remains hidden");
  unresolved.Add(new Member{name="pending"});Save();pendingStates=1;
  DrainClose().GetAwaiter().GetResult();
  Check(File.ReadAllText(journal)=="pending","bounded exit leaves unconfirmed recovery for the next renderer");
  var next=new RecoveryFixture(journal);next.unresolved.Add(new Member{name="next"});next.Save();
  unresolved.Add(new Member{name="late"});Save();
  Check(File.ReadAllText(journal)=="next","late old recovery cannot rewrite the new journal");
  unresolved.Clear();pendingStates=0;stoppedRestored=true;ClearStoppedRecovery();
  Check(File.ReadAllText(journal)=="next","late old recovery cannot delete the new journal");
  next.unresolved.Clear();next.stoppedRestored=true;next.ClearStoppedRecovery();
  Check(!File.Exists(journal),"current owner can remove a fully recovered journal");
 }
}
"@
Add-Type -TypeDefinition $code
$journal=Join-Path ([IO.Path]::GetTempPath()) ('PrismStageRecovery-'+[guid]::NewGuid().ToString('N')+'.json')
try{[RecoveryFixture]::new($journal).Run()}finally{if(Test-Path -LiteralPath $journal){Remove-Item -LiteralPath $journal}}
'PASS geometry/visibility ownership, legacy migration, manual edits, edge reveal and bounded recovery journal handoff'
