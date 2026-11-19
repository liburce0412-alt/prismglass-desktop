using System;using System.Threading;using System.Diagnostics;
internal sealed class SideDockData {public SideDockData(string root){}public object Read(){return null;}}
internal sealed class QuotaSnapshot {}
internal sealed class QuotaReader:IDisposable {public QuotaReader(string root){}public QuotaSnapshot Cached(){return new QuotaSnapshot();}public System.Threading.Tasks.Task<QuotaSnapshot> ReadAsync(){return System.Threading.Tasks.Task.FromResult(Cached());}public void Dispose(){}}
internal sealed class WidgetBackend:IDisposable {
 internal readonly ManualResetEventSlim entered=new ManualResetEventSlim(),release=new ManualResetEventSlim(),synced=new ManualResetEventSlim(),closed=new ManualResetEventSlim();internal int reads,syncs;internal object value=new object();
 public WidgetBackend(string root){}public object Read(){Interlocked.Increment(ref reads);entered.Set();release.Wait();return value;}public void SyncWorkWidgets(object snapshot){if(!Object.ReferenceEquals(snapshot,value))throw new Exception("cache changed");Interlocked.Increment(ref syncs);synced.Set();}public void Dispose(){closed.Set();}
}
internal static class TestSharedWorkSync {
 static int checks;static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 static void Main(){
  var shared=new SharedDesktopData("fixture");var clock=Stopwatch.StartNew();shared.SyncWorkWidgets();Check(clock.ElapsedMilliseconds<250&&shared.Widgets.entered.Wait(2000),"host dispatch returns while hardware read remains blocked on the worker");
  for(int i=0;i<50;i++)shared.SyncWorkWidgets();Check(shared.Widgets.reads==1,"repeated host ticks create at most one work sync in flight");
  shared.Widgets.release.Set();Check(shared.Widgets.synced.Wait(2000)&&Object.ReferenceEquals(shared.ReadWidgets(),shared.Widgets.value)&&shared.Widgets.reads==1,"work export reuses the shared widget cache");shared.Dispose();Check(shared.Widgets.closed.Wait(2000),"completed work releases its backend during disposal");
  shared=new SharedDesktopData("fixture");shared.SyncWorkWidgets();shared.Widgets.entered.Wait(2000);clock.Restart();shared.Dispose();Check(clock.ElapsedMilliseconds<250&&!shared.Widgets.closed.IsSet,"disposal waits asynchronously for the in-flight backend use");shared.SyncWorkWidgets();shared.Widgets.release.Set();Check(shared.Widgets.closed.Wait(2000)&&shared.Widgets.reads==1&&shared.Widgets.syncs==0,"disposed hosts cannot schedule another read and release exactly after work");
  Console.WriteLine("Completed "+checks+" shared work sync checks.");
 }
}
