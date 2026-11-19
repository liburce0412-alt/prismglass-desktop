using System;using System.Threading;
internal static class TestAudioOutputState {
 static void Main(){
  var entered=new ManualResetEventSlim(false);var release=new ManualResetEventSlim(false);int calls=0;
  using(var state=new AudioOutputState(()=>{Interlocked.Increment(ref calls);entered.Set();release.Wait();return new AudioOutputState.State{available=true,muted=true,value=27,name="Fixture headphones",reason=""};})){
   if(state.Snapshot().available||!entered.Wait(1000))throw new Exception("Initial read was synchronous or fabricated");for(int i=0;i<100;i++)state.Snapshot();if(calls!=1)throw new Exception("Duplicate background reads");release.Set();if(!SpinWait.SpinUntil(()=>state.Snapshot().available,2000))throw new Exception("Read did not complete");var snapshot=state.Snapshot();if(!snapshot.muted||snapshot.value!=27||snapshot.name!="Fixture headphones")throw new Exception("Actual endpoint metadata not retained");Console.WriteLine("PASS endpoint metadata uses one background read and preserves real mute/value/name without capture or writes");
  }
  using(var state=new AudioOutputState(()=>{throw new InvalidOperationException("PRIVATE");})){state.Snapshot();if(!SpinWait.SpinUntil(()=>state.Snapshot().reason=="默认声音输出暂不可用。",2000)||state.Snapshot().available)throw new Exception("Failed read fabricated a mute state");Console.WriteLine("PASS missing endpoint reports unavailable without fabricating mute state or leaking diagnostics");}
  if(Environment.GetCommandLineArgs().Length>1)using(var state=new AudioOutputState()){state.Snapshot();Thread.Sleep(500);var actual=state.Snapshot();Console.WriteLine("READ_ONLY available="+actual.available+" volume="+actual.value+" muted="+actual.muted+" name="+actual.name+" reason="+actual.reason);}
 }
}
