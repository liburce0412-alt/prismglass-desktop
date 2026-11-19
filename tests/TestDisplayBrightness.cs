using System;
using System.Collections.Generic;
using System.Threading;

internal static class TestDisplayBrightness {
 static int checks;static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static void Wait(Func<bool> condition){if(!SpinWait.SpinUntil(condition,2000))throw new Exception("Worker did not finish");}
 static void Main(){
  Check(DisplayBrightness.Identity(@"\\?\DISPLAY#ACME123#4&abc&0&UID1#{guid}")==DisplayBrightness.Identity(@"DISPLAY\ACME123\4&abc&0&UID1_0"),"GDI device interface maps to exact WMI instance");
  Check(DisplayBrightness.Identity(@"DISPLAY\ACME123\SECOND_0")!=DisplayBrightness.Identity(@"DISPLAY\ACME123\FIRST_0"),"same monitor model on another connector is distinct");
  Check(DisplayBrightness.NearestLevel(48,new byte[]{0,20,40,60,100})==40&&DisplayBrightness.NearestLevel(50,new byte[]{40,60})==40,"internal panel brightness uses nearest supported level deterministically");
  var values=new List<string>();var entered=new ManualResetEventSlim(false);var release=new ManualResetEventSlim(false);bool hold=false,permission=true;int active=0,maximum=0;
  using(var service=new DisplayBrightness((target,value,allowed)=>{int count=Interlocked.Increment(ref active);maximum=Math.Max(maximum,count);try{if(value.HasValue&&hold){entered.Set();if(!release.Wait(2000))throw new Exception("test release timeout");}if(value.HasValue&&allowed())lock(values)values.Add(target+":"+value.Value);return new DisplayBrightness.Reading{available=true,value=value??40};}finally{Interlocked.Decrement(ref active);}},()=>new[]{"panel","external"},()=>new object[0])){
   Check(service.Snapshot().targetId=="panel","initial screen target is explicit in state");Wait(()=>service.Snapshot().available&&!service.Snapshot().busy);
   hold=true;service.Set("panel",10,()=>permission);Check(entered.Wait(1000),"hardware worker runs independently of caller");for(int i=11;i<=80;i++)service.Set("panel",i,()=>permission);release.Set();Wait(()=>!service.Snapshot().busy);
   Check(values.Count==2&&values[0]=="panel:10"&&values[1]=="panel:80"&&maximum==1,"one hardware write in flight and drag retains its last value");
   values.Clear();release.Reset();entered.Reset();service.Set("panel",20,()=>permission);Check(entered.Wait(1000),"second asynchronous write entered");service.Set("panel",99,()=>permission);service.Select("external");release.Set();Wait(()=>!service.Snapshot().busy);
   Check(values.Count==0&&service.Snapshot().targetId=="external","switching target cancels queued and pre-write old-screen changes");
   bool stale=false;try{service.Set("panel",75,()=>true);}catch(InvalidOperationException){stale=true;}Check(stale,"stale target request cannot write a different selected monitor");
   release.Reset();entered.Reset();service.Set("external",25,()=>permission);Check(entered.Wait(1000),"permission test worker entered");permission=false;release.Set();Wait(()=>!service.Snapshot().busy);Check(values.Count==0,"permission revocation is rechecked immediately before hardware write");
   permission=true;release.Reset();entered.Reset();service.Set("external",30,()=>permission);Check(entered.Wait(1000),"dispose test worker entered");service.Dispose();release.Set();Wait(()=>Volatile.Read(ref active)==0);Check(values.Count==0,"disposing cancels a pending pre-write operation");
  }
  using(var unsupported=new DisplayBrightness((id,value,allowed)=>new DisplayBrightness.Reading{reason="unsupported"},()=>new[]{"virtual"},()=>new object[0])){unsupported.Snapshot();Wait(()=>!unsupported.Snapshot().busy);bool denied=false;try{unsupported.Set("virtual",60,()=>true);}catch(InvalidOperationException){denied=true;}Check(denied&&!unsupported.Snapshot().available,"unsupported display remains unavailable without simulated success");}
  Console.WriteLine(checks+" display brightness checks passed; no physical brightness changed.");
 }
}
