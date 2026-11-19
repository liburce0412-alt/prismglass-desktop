using System;using System.Collections.Generic;
class TestIslandEvents {
 static void Assert(bool value){if(!value)throw new Exception("Assertion failed");}
 static void Main(){var notices=new List<IslandEvents.Notice>();IslandEvents.Changed+=notices.Add;
 IslandEvents.BeginLaunch("app","应用","icon",new long[]{1});Assert(notices.Count==1&&notices[0].kind=="launching");
 IslandEvents.ObserveWindow("other",2);IslandEvents.ObserveWindow("app",1);Assert(notices.Count==1);
 IslandEvents.ObserveWindow("app",2);Assert(notices.Count==2&&notices[1].kind=="launched");IslandEvents.ObserveWindow("app",3);Assert(notices.Count==2);
 IslandEvents.BeginLaunch("second","另一个","",null);IslandEvents.LaunchFailed("app");Assert(notices.Count==3);IslandEvents.LaunchFailed("second");Assert(notices.Count==4&&notices[3].kind=="launch-failed");
 IslandEvents.Changed+=n=>{throw new Exception("presentation must not break launch");};IslandEvents.BeginLaunch("third","第三个","",null);Assert(notices.Count==5);
 int opens=0;IslandEvents.ObserveAttention("chat",true,"聊天","",()=>opens++);int count=notices.Count;long attentionSequence=notices[count-1].sequence;IslandEvents.ObserveAttention("chat",true,"聊天","",()=>opens++);Assert(notices.Count==count);IslandEvents.Open(attentionSequence-1);Assert(opens==0);IslandEvents.Open(attentionSequence);IslandEvents.Open(attentionSequence);Assert(opens==1);IslandEvents.ObserveAttention("chat",false,"聊天","",null);IslandEvents.ObserveAttention("chat",true,"聊天","",()=>opens++);Assert(notices.Count==count+1);
 IslandEvents.BeginLaunch("a","A","",null);IslandEvents.BeginLaunch("b","B","",null);IslandEvents.LaunchFailed("a");Assert(notices[notices.Count-1].name=="A");IslandEvents.ObserveWindow("b",7);Assert(notices[notices.Count-1].name=="B");
 int first=0,second=0;IslandEvents.ObserveAttention("A",true,"A","",()=>first++);long a=notices[notices.Count-1].sequence;IslandEvents.ObserveAttention("B",true,"B","",()=>second++);long b=notices[notices.Count-1].sequence;IslandEvents.Open(a);IslandEvents.Open(b);Assert(first==1&&second==1);
 IslandEvents.BeginLaunch("waiting","W","",null);Assert(IslandEvents.DockLaunchState("waiting",null)=="launching");
 IslandEvents.ObserveWindow("waiting",9);Assert(IslandEvents.DockLaunchState("waiting",null)=="");
 IslandEvents.BeginLaunch("path","P","",null);IslandEvents.LaunchFailed("path");Assert(IslandEvents.DockLaunchState("pin","path")=="launch-failed");
 IslandEvents.ObserveWindow("path",10);Assert(IslandEvents.DockLaunchState("pin","path")=="launch-failed");notices[notices.Count-1].expiresAt=0;Assert(IslandEvents.DockLaunchState("pin","path")=="");
 Console.WriteLine("PASS begin, new-window-only completion, key isolation, failure, subscriber isolation");}
}
