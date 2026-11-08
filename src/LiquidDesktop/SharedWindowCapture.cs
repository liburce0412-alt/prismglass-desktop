using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

// One bounded, background-only capture per source. Consumers lease immutable
// latest frames; a slow WebView never queues work or retains an unbounded history.
internal static class SharedWindowCapture {
 const int MaximumSources=6,MaximumInputPixels=33554432,MaximumOutputPixels=1048576,MaximumOutputWidth=1600;
 internal const int TargetFps=60;
 [DllImport("winmm.dll")]static extern uint timeBeginPeriod(uint period);
 [DllImport("winmm.dll")]static extern uint timeEndPeriod(uint period);
 internal static int FrameWait(double elapsed){return Math.Max(1,(int)Math.Ceiling(1000.0/TargetFps-elapsed));}
 internal sealed class Buffer {internal byte[] pixels;internal int width,height,references;internal long serial;internal bool writing;}
 internal sealed class Entry {
  internal IntPtr window;internal uint pid;internal string key,error="";internal int subscribers,inputPixels;
  internal bool stop,finished;internal DateTime retryAfter;internal long serial;internal Buffer latest;
  internal readonly Buffer[] buffers={new Buffer(),new Buffer(),new Buffer()};
 }
 static readonly object gate=new object();
 static readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
 static int activeSources,leases,totalInputPixels;static long starts,readbacks;
 [StructLayout(LayoutKind.Sequential)]struct RECT {internal int left,top,right,bottom;}
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")]static extern bool GetWindowDisplayAffinity(IntPtr window,out uint affinity);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT bounds);

 internal sealed class Subscription : IDisposable {
  Entry entry;readonly string rejected;
  internal Subscription(string error){rejected=error;}
  private Subscription(Entry value){entry=value;rejected="";}
  internal bool Valid {get{lock(gate){return entry!=null&&!entry.stop&&!entry.finished&&entry.error.Length==0&&WindowValid(entry.window,entry.pid);}}}
  internal string Error {get{lock(gate){return entry==null?rejected:entry.error;}}}
  internal FrameLease Latest(long afterSerial){lock(gate){
   if(entry==null||entry.stop||entry.finished||entry.error.Length!=0||!WindowValid(entry.window,entry.pid))return null;
   var frame=entry.latest;if(frame==null||frame.writing||frame.serial<=afterSerial)return null;
   frame.references++;return new FrameLease(frame);
  }}
  public void Dispose(){lock(gate){if(entry==null)return;var previous=entry;entry=null;leases--;if(--previous.subscribers==0){previous.stop=true;Retire(previous);}}}
  internal static Subscription Create(Entry value){return new Subscription(value);}
 }
 internal sealed class FrameLease : IDisposable {
  Buffer buffer;
  internal readonly long Serial;internal readonly int Width,Height;internal readonly byte[] Pixels;
  internal FrameLease(Buffer value){buffer=value;Serial=value.serial;Width=value.width;Height=value.height;Pixels=value.pixels;}
  public void Dispose(){lock(gate){if(buffer==null)return;buffer.references--;buffer=null;}}
 }
 internal static object Diagnostics {get{lock(gate){return new{activeSources=activeSources,leases=leases,starts=starts,readbacks=readbacks};}}}
 internal static Subscription Acquire(IntPtr window,uint pid){
  if(!WindowValid(window,pid))return new Subscription("窗口不允许捕获。");
  Entry entry;string key=window.ToInt64()+":"+pid;
  lock(gate){
   var expired=new List<string>();foreach(var previous in entries.Values)if(previous.finished&&previous.subscribers==0&&(previous.error.Length==0||DateTime.UtcNow>=previous.retryAfter))expired.Add(previous.key);
   foreach(var previous in expired)entries.Remove(previous);
   if(entries.TryGetValue(key,out entry)&&(entry.stop||entry.finished||entry.error.Length!=0))return new Subscription(entry.error.Length==0?"窗口采样正在停止。":entry.error);
   if(entry==null){
    if(activeSources>=MaximumSources)return new Subscription("实时背景已达到共享捕获数量限制。");
    entry=new Entry{window=window,pid=pid,key=key};entries.Add(key,entry);activeSources++;
    var pending=entry;Task.Run(()=>Run(pending));
   }
   entry.subscribers++;leases++;return Subscription.Create(entry);
  }
 }
 static bool WindowValid(IntPtr window,uint pid){uint current,affinity;return window!=IntPtr.Zero&&pid!=0&&GetWindowThreadProcessId(window,out current)!=0&&current==pid&&IsWindowVisible(window)&&!IsIconic(window)&&(!GetWindowDisplayAffinity(window,out affinity)||affinity==0);}
 static bool Stopping(Entry entry){lock(gate){return entry.stop;}}
 static void Fail(Entry entry,string message){lock(gate){entry.error=message;entry.stop=true;entry.retryAfter=DateTime.UtcNow.AddSeconds(30);}}
 static void Retire(Entry entry){
  // Keep a timed-out entry until its in-flight operation has completed, even if
  // all consumers leave. Reacquisition cannot start a second hung GPU operation.
  if(entry.finished&&entry.subscribers==0&&entry.error.Length==0){Entry current;if(entries.TryGetValue(entry.key,out current)&&current==entry)entries.Remove(entry.key);}
 }
 static bool ReserveInput(Entry entry){
  RECT bounds;if(!WindowValid(entry.window,entry.pid)||!GetWindowRect(entry.window,out bounds)){Fail(entry,"窗口不再允许捕获。");return false;}
  long width=(long)bounds.right-bounds.left,height=(long)bounds.bottom-bounds.top,count=width*height;
  if(width<1||height<1||count>MaximumInputPixels){Fail(entry,"窗口画面超过共享捕获预算。");return false;}
  lock(gate){if(entry.stop)return false;if(totalInputPixels-entry.inputPixels+count>MaximumInputPixels){entry.error="实时背景已达到共享像素预算。";entry.stop=true;entry.retryAfter=DateTime.UtcNow.AddSeconds(30);return false;}totalInputPixels=totalInputPixels-entry.inputPixels+(int)count;entry.inputPixels=(int)count;return true;}
 }
 static Buffer ReserveBuffer(Entry entry){lock(gate){
  if(entry.stop)return null;Buffer result=null;
  foreach(var buffer in entry.buffers)if(buffer.references==0&&!buffer.writing){result=buffer;if(buffer!=entry.latest)break;}
  if(result!=null){result.writing=true;if(result==entry.latest)entry.latest=null;}return result;
 }}
 static async Task Run(Entry entry){
  WindowCapture capture=null;bool precise=timeBeginPeriod(1)==0;var cadence=Stopwatch.StartNew();
  try{
   if(Stopping(entry)||!ReserveInput(entry))return;
   capture=new WindowCapture(entry.window,entry.pid,true);lock(gate){starts++;}
   while(!Stopping(entry)){
    cadence.Restart();
    if(!ReserveInput(entry))break;
    var buffer=ReserveBuffer(entry);
    if(buffer!=null)try{
     var pending=capture.Next();
     if(await Task.WhenAny(pending,Task.Delay(3000)).ConfigureAwait(false)!=pending){
      Fail(entry,"GPU 窗口读回超时，实时背景已停止。");
      // Next owns the GPU surface until it completes. Wait on this worker, not
      // the UI thread, before disposing its result and capture session.
      using(var late=await pending.ConfigureAwait(false)){}break;
     }
     using(var pixels=await pending.ConfigureAwait(false)){
      if(pixels!=null){
       lock(gate){readbacks++;}if(Stopping(entry))break;
       int inputWidth=pixels.PixelWidth,inputHeight=pixels.PixelHeight;
       if(inputWidth<1||inputHeight<1||(long)inputWidth*inputHeight>MaximumInputPixels)throw new InvalidOperationException("窗口画面超过共享捕获预算。");
       double scale=Math.Min(1,Math.Min((double)MaximumOutputWidth/inputWidth,Math.Sqrt((double)MaximumOutputPixels/((long)inputWidth*inputHeight))));
       int width=Math.Max(1,(int)Math.Floor(inputWidth*scale)),height=Math.Max(1,(int)Math.Floor(inputHeight*scale));
       int length=checked(width*height*4);if(buffer.pixels==null||buffer.pixels.Length!=length)buffer.pixels=new byte[length];
       var pinned=GCHandle.Alloc(buffer.pixels,GCHandleType.Pinned);
       try{WindowCapture.CopyScaled(pixels,pinned.AddrOfPinnedObject(),width,height);}finally{pinned.Free();}
       lock(gate){if(!entry.stop){buffer.width=width;buffer.height=height;buffer.serial=++entry.serial;buffer.writing=false;entry.latest=buffer;}}
      }
     }
    }finally{lock(gate){buffer.writing=false;}}
    // Charge readback and scaling to this frame's budget; never queue catch-up frames.
    await Task.Delay(FrameWait(cadence.Elapsed.TotalMilliseconds)).ConfigureAwait(false);
   }
  }catch(Exception error){if(!Stopping(entry))Fail(entry,"实时背景采样失败："+error.Message);}
  finally{
   if(precise)timeEndPeriod(1);
   try{if(capture!=null)capture.Dispose();}catch(Exception error){Fail(entry,"实时背景采样释放失败："+error.Message);}
   lock(gate){entry.stop=true;entry.finished=true;entry.latest=null;totalInputPixels-=entry.inputPixels;entry.inputPixels=0;activeSources--;Retire(entry);}
  }
 }
}
