$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$source=Get-Content (Join-Path $repo 'src/LiquidDesktop/StageManager.cs') -Raw
$start=$source.IndexOf(' internal sealed class PlacementLane')
$lane=$source.Substring($start,$source.IndexOf(' async Task<bool> WatchPlacement(',$start)-$start)
$apply=$source.Substring($source.IndexOf(' static bool Restore(Member member'),$start-$source.IndexOf(' static bool Restore(Member member'))
$types=$source.Substring($source.IndexOf(' [StructLayout'),$source.IndexOf(' internal sealed class Member')-$source.IndexOf(' [StructLayout'))
$dir=Join-Path ([IO.Path]::GetTempPath()) ('PrismStageTest-'+[guid]::NewGuid().ToString('N'))
New-Item -Type Directory $dir | Out-Null
try {
 $cs=Join-Path $dir 'Fixture.cs';$exe=Join-Path $dir 'Fixture.exe'
 @"
using System;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using System.Runtime.InteropServices;using System.Windows.Forms;using System.Drawing;
static class Fixture{
$types
 class DockWindow{public long handle;}
 class Member{public string placementFailure;public DockWindow window;public Placement original,layout,geometryIntent;}
 static class DockBackend{
  internal static bool CanManage(DockWindow w){return Valid(w);}
  internal static bool Valid(DockWindow w){uint pid;GetWindowThreadProcessId(new IntPtr(w.handle),out pid);return pid==(uint)Process.GetCurrentProcess().Id;}
 }
 static class NativeWindowMotion{internal static bool IsIconic(IntPtr h){return Fixture.IsIconic(h);}}
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]static extern bool IsHungAppWindow(IntPtr h);
 [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr h,int cmd);
 [DllImport("user32.dll")]static extern bool GetWindowPlacement(IntPtr h,ref Placement p);
 [DllImport("user32.dll")]static extern bool SetWindowPlacement(IntPtr h,ref Placement p);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int key,out int value,int size);
 [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr h,int key,ref int value,int size);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,int flags,uint access,IntPtr security);
 [DllImport("user32.dll",SetLastError=true)]static extern bool SetThreadDesktop(IntPtr desktop);
 [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
$lane
$apply
 static Placement Read(IntPtr hwnd){var p=new Placement{length=Marshal.SizeOf(typeof(Placement))};if(!GetWindowPlacement(hwnd,ref p))throw new Exception("read placement");return p;}
 [STAThread]static int Main(){
  var desktop=CreateDesktop("PrismFixture-"+Guid.NewGuid().ToString("N"),IntPtr.Zero,IntPtr.Zero,0,0x1ff,IntPtr.Zero);
  if(desktop==IntPtr.Zero){Console.WriteLine("SKIP separate desktop unavailable: "+Marshal.GetLastWin32Error());return 2;}
  Exception failure=null;var threads=new Thread[3];var windows=new Form[3];var handles=new IntPtr[3];var ready=new CountdownEvent(3);var lanes=new[]{new PlacementLane(),new PlacementLane(),new PlacementLane()};
  try{
   for(int i=0;i<3;i++){int id=i;threads[i]=new Thread(()=>{
    try{if(!SetThreadDesktop(desktop))throw new Exception("private desktop binding "+Marshal.GetLastWin32Error());
     using(var f=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(15+id*30,15,280,160)}){
      windows[id]=f;f.Shown+=(s,e)=>{handles[id]=f.Handle;ready.Signal();};Application.Run(f);}
    }catch(Exception ex){failure=ex;if(ready.CurrentCount>0)ready.Signal();}
   });threads[i].IsBackground=true;threads[i].SetApartmentState(ApartmentState.STA);threads[i].Start();}
   if(!ready.Wait(3000)||failure!=null)throw failure??new Exception("source timeout");
   var members=new Member[3];var tasks=new Task<bool>[3];
   var clock=Stopwatch.StartNew();
   for(int i=0;i<3;i++){
    var hwnd=handles[i];var original=Read(hwnd);var target=original;target.normal.l+=40;target.normal.r+=40;
    var m=new Member{window=new DockWindow{handle=hwnd.ToInt64()},original=original,layout=target};members[i]=m;
    tasks[i]=lanes[i].Submit(()=>Restore(m,false));
   }
   if(!Task.WaitAll(tasks,2500)||Array.Exists(tasks,t=>!t.Result))throw new Exception("native layout confirmation");
   long outward=clock.ElapsedMilliseconds;clock.Restart();
   for(int i=0;i<3;i++){var m=members[i];tasks[i]=lanes[i].Submit(()=>Restore(m,true));}
   if(!Task.WaitAll(tasks,2500)||Array.Exists(tasks,t=>!t.Result))throw new Exception("native original confirmation");
   long restoreMs=clock.ElapsedMilliseconds;
   using(var blocked=new ManualResetEventSlim())using(var release=new ManualResetEventSlim())using(var entered=new ManualResetEventSlim()){
    windows[0].BeginInvoke(new Action(()=>{blocked.Set();release.Wait(1500);}));
    if(!blocked.Wait(1000))throw new Exception("target queue did not pause");
    var m=members[0];int discarded=0;
    var first=lanes[0].Submit(()=>{entered.Set();return Restore(m,false);});
    if(!entered.Wait(500))throw new Exception("placement worker did not start");
    var obsolete=lanes[0].Submit(()=>{Interlocked.Increment(ref discarded);return Restore(m,false);});
    var correction=lanes[0].Submit(()=>Restore(m,true));
    var other=lanes[1].Submit(()=>Restore(members[1],false));
    if(!other.Wait(450)||!other.Result)throw new Exception("one blocked window stalled its neighbor");
    release.Set();
    if(!Task.WaitAll(new Task[]{first,obsolete,correction},2500)||!correction.Result||discarded!=0||!Read(handles[0]).normal.Equals(m.original.normal))throw new Exception("late placement survived reversal");
    if(!lanes[1].Submit(()=>Restore(members[1],true)).Result)throw new Exception("neighbor restore");
   }
   Console.WriteLine("PASS three HWNDs on an isolated, never activated desktop; confirmed layout {0}ms, restore {1}ms; paused source queue did not block its neighbor, stale placement skipped, final geometry confirmed",outward,restoreMs);return 0;
  }finally{foreach(var f in windows)if(f!=null&&f.IsHandleCreated&&!f.IsDisposed)try{f.BeginInvoke(new Action(f.Close));}catch(InvalidOperationException){}foreach(var thread in threads)if(thread!=null)thread.Join(1000);CloseDesktop(desktop);}
 }
}
"@ | Set-Content -LiteralPath $cs
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll $cs
 if($LASTEXITCODE){throw 'Placement fixture compilation failed'}
 & $exe
 if($LASTEXITCODE -eq 2){return}
 if($LASTEXITCODE){throw 'Placement fixture failed'}
}finally{
 foreach($file in @($cs,$exe)){if($file -and (Test-Path -LiteralPath $file)){Remove-Item -LiteralPath $file}}
 [IO.Directory]::Delete($dir)
}
