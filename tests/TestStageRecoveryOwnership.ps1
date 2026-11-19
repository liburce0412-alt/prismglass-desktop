$ErrorActionPreference='Stop'
$s=Get-Content (Join-Path $PSScriptRoot '../src/LiquidDesktop/StageManager.cs') -Raw
function Block([string]$first,[string]$end){$i=$s.IndexOf($first);$s.Substring($i,$s.IndexOf($end,$i)-$i)}
$types=Block ' [StructLayout' ' internal sealed class Member'
$recover=Block ' static Placement RecoveryTarget(' ' async Task RecoverMember('
$edge=Block 'string edgeOpen,edgeCandidate;' ' internal void DisplaysChanged('
$code=@"
using System;using System.Drawing;using System.Collections.Generic;using System.Runtime.InteropServices;
public sealed class RecoveryFixture{
$types
 class Member{public Placement original,layout,geometryIntent;public int recoveryVersion,visibilityIntent;}
 class StageStrip{internal Rectangle OccupiedBounds=new Rectangle(8,80,240,420);}
 Dictionary<string,StageStrip> strips=new Dictionary<string,StageStrip>();HashSet<string> fullscreen=new HashSet<string>{"A"};bool suspended=false,switching=false;
$recover
$edge
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
 }
}
"@
Add-Type -TypeDefinition $code
[RecoveryFixture]::new().Run()
'PASS explicit geometry/visibility ownership, legacy migration, later manual edits, bounded edge dwell, leave and exclusive gating'
