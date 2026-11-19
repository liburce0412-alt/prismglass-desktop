$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$dir=Join-Path ([IO.Path]::GetTempPath()) ('PrismMask-'+[guid]::NewGuid().ToString('N'))
$bin=Join-Path $dir 'app';New-Item -Type Directory $bin -Force|Out-Null
try{
 $source=Get-Content (Join-Path $repo 'src/LiquidDesktop/StartupTransition.cs') -Raw
 $source=$source.Substring($source.IndexOf('public static class StartupTransition'),$source.IndexOf('internal sealed class StartupNotice')-$source.IndexOf('public static class StartupTransition'))
 $source=$source.Replace('Local\\PrismGlassLauncher','Local\\PrismGlassFixture'+[guid]::NewGuid().ToString('N')).Replace('[DllImport("dwmapi.dll")] static extern int DwmFlush();','static int DwmFlush(){return Fixture.Compose();}')
 $cs=Join-Path $bin 'Fixture.cs';$exe=Join-Path $bin 'Fixture.exe';$script=Join-Path $dir 'DesktopMode.ps1'
 @"
using System;using System.Linq;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Diagnostics;using System.IO;using System.Runtime.InteropServices;using System.ComponentModel;
$source
enum MessageBoxImage{Error}enum MessageBoxButton{OK}static class MessageBox{public static void Show(string a,string b,MessageBoxButton c,MessageBoxImage d){}}
enum ShutdownMode{OnExplicitShutdown}
class FixtureDispatcher:SynchronizationContext {
 readonly System.Collections.Concurrent.BlockingCollection<Action> queue=new System.Collections.Concurrent.BlockingCollection<Action>();
 public void BeginInvoke(Action action){queue.Add(action);}
 public override void Post(SendOrPostCallback work,object state){BeginInvoke(()=>work(state));}
 public void Pump(Func<bool> done){var deadline=Stopwatch.StartNew();while(!done()&&deadline.ElapsedMilliseconds<15000){Action next;if(queue.TryTake(out next,25))next();}if(!done())throw new Exception("launcher hung");}
}
class Application {public readonly FixtureDispatcher Dispatcher=new FixtureDispatcher();public ShutdownMode ShutdownMode;public event EventHandler Startup;bool done;public void Run(){SynchronizationContext.SetSynchronizationContext(Dispatcher);Startup(this,EventArgs.Empty);Dispatcher.Pump(()=>done);}public void Shutdown(){done=true;}}
class DesktopPreferences{public DesktopPreferences(string p){}public Display ForMonitor(string id,bool primary){return new Display{enabled=id!="d"};}}
class Display{public bool enabled=true,Primary;public string Id,Screen;}
static class MonitorCatalog {
 static volatile Display[] connected={new Display{Id="a",Screen="A",Primary=true},new Display{Id="b",Screen="B"}};
 public static List<Display> Read(){return connected.ToList();}
 public static void Change(params Display[] next){connected=next;SystemEvents.Raise();}
}
static class SystemEvents {
 public static event EventHandler DisplaySettingsChanged;
 public static bool Attached{get{return DisplaySettingsChanged!=null;}}
 public static void Raise(){DisplaySettingsChanged(null,EventArgs.Empty);}
}
static class DesktopSettingsFiles{public static string Read(string p){return "";}public static Dictionary<string,object> Map(string v){return new Dictionary<string,object>();}}
class StartupNotice{readonly string screen;string bounds;readonly Task<bool> result;public bool IsVisible;public event EventHandler ContentRendered,Closed;public event CancelEventHandler Closing;
 public StartupNotice(string p,Task<bool> t,DesktopPreferences d,string s){result=t;screen=bounds=s;result.ContinueWith(success=>{if(success.Result&&IsVisible)Close();},TaskScheduler.FromCurrentSynchronizationContext());}
 public bool UpdateDisplay(string updated,bool force){if(updated==bounds)return false;Fixture.Note("resize "+screen+" "+updated);bounds=updated;return true;}
 public void SetProgress(double fraction,string caption){Fixture.Note("progress "+screen+" "+fraction.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture));if(screen=="A"&&fraction==.52)Fixture.Change("running");}
 public async void Show(){IsVisible=true;var attempt=new CancelEventArgs();Closing(this,attempt);if(!attempt.Cancel)throw new Exception("premature close permitted");await Task.Delay(screen=="A"?30:screen=="B"&&Fixture.Scenario=="topology-before"?350:65);if(!IsVisible)return;Fixture.Note("frame "+screen);ContentRendered(this,EventArgs.Empty);if(screen=="A")Fixture.Change("before");}
 public void Close(){var attempt=new CancelEventArgs();Closing(this,attempt);if(attempt.Cancel)throw new Exception("close refused");Fixture.Note(result.IsCompleted?"close "+screen+" "+result.Result:"retire "+screen);IsVisible=false;Closed(this,EventArgs.Empty);}
}
class Fixture {
 static object gate=new object();static int topologyChanged;
 public static string Scenario{get{return Environment.GetEnvironmentVariable("MASK_CASE");}}
 public static void Note(string s){lock(gate)File.AppendAllText(Environment.GetEnvironmentVariable("MASK_LOG"),s+Environment.NewLine);}
 public static void Change(string phase){
  if(Scenario!="topology-"+phase||Interlocked.Exchange(ref topologyChanged,1)!=0)return;
  Note("display change "+phase);
  if(phase=="running")MonitorCatalog.Change(new Display{Id="d",Screen="D",Primary=true});
  else MonitorCatalog.Change(new Display{Id="A",Screen="A2",Primary=true},new Display{Id="c",Screen="C"});
 }
 public static int Compose(){Note("composed");Change("compose");return Scenario=="barrier-fail"?-1:0;}
 static int Main(){int result=StartupTransition.Run(new[]{"--login"});if(SystemEvents.Attached)throw new Exception("display handler leaked");return result;}
}
"@ | Set-Content -LiteralPath $cs
 @"
param([string]`$Mode)
Add-Content -LiteralPath `$env:MASK_LOG 'child'
if(`$env:MASK_CASE -ne 'repeat'){
 'PRISMGLASS_STARTUP_STAGE SHELL'
 'PRISMGLASS_STARTUP_STAGE SERVICES'
 'PRISMGLASS_STARTUP_STAGE RENDERER'
 if(`$env:MASK_CASE -eq 'topology-running'){Start-Sleep -Milliseconds 240}
 'PRISMGLASS_STARTUP_STAGE PANELS'
 'PRISMGLASS_STARTUP_STAGE RENDERER'
 'PRISMGLASS_STARTUP_STAGE HANDOFF'
 'PRISMGLASS_STARTUP_STAGE UNEXPECTED'
}
if(`$env:MASK_CASE -ne 'missing'){ 'PRISMGLASS_DESKTOP_READY' }
Start-Sleep -Milliseconds 50
Add-Content -LiteralPath `$env:MASK_LOG 'cleanup'
if(`$env:MASK_CASE -eq 'rollback'){ 'PRISMGLASS_STARTUP_STAGE ROLLBACK'; exit 3 }
"@ | Set-Content -LiteralPath $script
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$exe" $cs
 if($LASTEXITCODE){throw 'fixture compilation failed'}
 foreach($scenario in 'success','rollback','missing','barrier-fail','repeat','topology-before','topology-compose','topology-running'){
  $log=Join-Path $dir "$scenario.txt";$si=[Diagnostics.ProcessStartInfo]::new($exe);$si.UseShellExecute=$false;$si.CreateNoWindow=$true;$si.EnvironmentVariables['MASK_CASE']=$scenario;$si.EnvironmentVariables['MASK_LOG']=$log
  $child=[Diagnostics.Process]::Start($si);try{if(!$child.WaitForExit(16000)){throw 'fixture timeout'};$status=$child.ExitCode}finally{$child.Dispose()}
  $expectedCode=@{success=0;repeat=0;rollback=3;missing=1;'barrier-fail'=1;'topology-before'=0;'topology-compose'=0;'topology-running'=0}[$scenario]
  $expected=@('frame A','frame B','composed');if($scenario -ne 'barrier-fail'){$expected+=@('child','cleanup')}
  $ok=$expectedCode -eq 0;$expected+=@("close A $ok","close B $ok")
  $screens=@('A','B')
  switch($scenario){
   'topology-before' {$expected=@('frame A','display change before','resize A A2','retire B','frame C','composed','child','cleanup','close A True','close C True');$screens=@('A','C')}
   'topology-compose' {$expected=@('frame A','frame B','composed','display change compose','resize A A2','retire B','frame C','composed','child','cleanup','close A True','close C True');$screens=@('A','C')}
   'topology-running' {$expected=@('frame A','frame B','composed','child','display change running','retire A','retire B','frame D','cleanup','close D True');$screens=@('D')}
  }
  $lines=Get-Content $log
  $events=@($lines | Where-Object {$_ -notlike 'progress *'})
  if($status -ne $expectedCode -or ($events -join '|') -ne ($expected -join '|')){throw "$scenario code=$status trace=$lines"}
  $fractions=if($scenario -eq 'barrier-fail'){@()}elseif($scenario -eq 'repeat'){@('1.00')}else{@('0.12','0.33','0.52','0.73','0.91')+$(if($ok){@('1.00')}elseif($scenario -eq 'rollback'){@('0.91')})}
  if($scenario -eq 'topology-running'){$fractions=@('0.52','0.73','0.91','1.00')}
  foreach($screen in $screens){
   $actual=@($lines | Where-Object {$_ -like "progress $screen *"} | ForEach-Object {($_ -split ' ')[2]})
   if(($actual -join '|') -ne ($fractions -join '|')){throw "$scenario bad phase progress for screen $screen : $actual"}
  }
  if($ok -and [Array]::IndexOf($lines,"progress $($screens[0]) 1.00") -le [Array]::IndexOf($lines,'cleanup')){throw '100% displayed before script exit'}
  "PASS $scenario : $($lines -join ' -> ')"
 }
}finally{
 $resolved=(Resolve-Path $dir).Path
 if([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or (Split-Path $resolved -Leaf) -notmatch '^PrismMask-[0-9a-f]{32}$'){throw 'unexpected fixture path'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
