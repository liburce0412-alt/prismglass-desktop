$ErrorActionPreference='Stop'
$source=Get-Content (Join-Path $PSScriptRoot '../src/LiquidDesktop/StageManager.cs') -Raw
$start=$source.IndexOf(' void ReconcileMonitorMembership(){')
$end=$source.IndexOf(' void Refresh(){',$start)
if($start -lt 0 -or $end -lt 0){throw 'Monitor reconciliation method not found'}
$reconcile=$source.Substring($start,$end-$start).Replace(' void ReconcileMonitorMembership()', ' static void ReconcileMonitorMembership(List<Group> groups,Dictionary<string,bool> areas,Dictionary<string,Group> active)')
$visibleStart=$source.IndexOf(' IEnumerable<Member> VisibleThumbnails()')
$visible=$source.Substring($visibleStart,$source.IndexOf(' async void FillMissingThumbnails()',$visibleStart)-$visibleStart).Replace(' IEnumerable<Member> VisibleThumbnails()', ' static IEnumerable<Member> VisibleThumbnails(List<Group> groups)')
$code=@"
using System;
using System.Linq;
using System.Collections.Generic;
public static class StageMonitorFixture {
 const int ThumbnailByteBudget=96*1024*1024;
 class Window {public long handle;}
 class Member {public Window window;public int layout,standalone,thumbnailBytes;public bool thumbnailProtected=false;}
 class Group {public string monitor;public List<Member> members=new List<Member>();}
 class Screen {public string DeviceName;public static Screen FromHandle(IntPtr h){return new Screen{DeviceName=h.ToInt64()<3?"B":"A"};}}
 class NativeWindowMotion {public static bool IsIconic(IntPtr h){return h.ToInt64()==2;}}
 static int Read(Window w){return (int)w.handle;}
 static bool MonitorPaused(string monitor){return monitor=="paused";}
 class Pose {public Bounds bounds=new Bounds();}
 class Bounds {public bool IsEmpty;}
 static Pose Card(Group group,Member member){return new Pose{bounds=new Bounds{IsEmpty=group.monitor=="hidden"}};}
$reconcile
$visible
 public static void Run(){
 var visibleGroups=new List<Group>();
 foreach(string name in new[]{"hidden","paused","C","D","E","F","G","H"}){
  var card=new Group{monitor=name};visibleGroups.Add(card);
  for(int i=0;i<17;i++)card.members.Add(new Member{window=new Window{handle=i+1}});
 }
 foreach(var card in visibleGroups)foreach(var m in card.members)m.thumbnailBytes=6*1024*1024;
 var selected=VisibleThumbnails(visibleGroups).ToList();
 if(selected.Count!=16||selected.Any(m=>m.window.handle>3)||selected.Intersect(visibleGroups.Take(2).SelectMany(g=>g.members)).Any())throw new Exception("visible thumbnail budget");
 foreach(var card in visibleGroups)foreach(var m in card.members)m.thumbnailBytes=10*1024*1024;
 if(VisibleThumbnails(visibleGroups).Count()!=9)throw new Exception("visible byte budget");
 var areas=new Dictionary<string,bool>{{"A",true},{"B",true}};
 var groupA=new Group{monitor="A"};var groups=new List<Group>{groupA};
 var active=new Dictionary<string,Group>{{"A",groupA}};
 for(int n=1;n<=3;n++)groupA.members.Add(new Member{window=new Window{handle=n}});
 for(int pass=0;pass<2;pass++){
 ReconcileMonitorMembership(groups,areas,active);
 if(pass==0){
 if(groups.Count!=2||groupA.members.Count!=2||groups[1].monitor!="B"||groups[1].members[0].layout!=1||active["A"]!=groupA)throw new Exception("split or minimized member");
 groupA.members.Clear();groupA.members.Add(new Member{window=new Window{handle=1}});
 }else if(groupA.monitor!="B"||active.ContainsKey("A")||groups.Count!=2)throw new Exception("whole group");
 }
 }
}
"@
Add-Type -TypeDefinition $code
[StageMonitorFixture]::Run()
'PASS group split, whole-group move, minimized member, active mapping, placement refresh and visible thumbnail budget'
