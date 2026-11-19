param([ValidateSet('Game','Beauty','Recovery','Login','CompanionCleanup')][string]$Mode='Beauty',[switch]$ShowErrors,[switch]$StopCompanion)
$ErrorActionPreference='Stop'
function Write-DesktopModeFailure([string]$Root,[string]$Action,[string]$Reason,[bool]$Notify){
 $title=if($Action -eq 'Beauty'){'PrismGlass 未能启动'}else{'PrismGlass 未能完成桌面切换'}
 $message=$title+"。`r`n`r`n"+$Reason+"`r`n`r`n请按提示处理后重试；仍无法使用时，可重新运行安装器修复程序。"
 try{
  $log=Join-Path $Root 'LiquidDesktop/startup-error.log'
  [IO.File]::WriteAllText($log,([datetime]::Now.ToString('o')+"`r`n模式："+$Action+"`r`n"+$Reason),[Text.UTF8Encoding]::new($false))
  $message+="`r`n`r`n本机错误记录："+$log
 }catch{$message+="`r`n`r`n错误记录无法保存，请保留此提示。"}
 if($Notify){try{Add-Type -AssemblyName System.Windows.Forms;[void][Windows.Forms.MessageBox]::Show($message,$title,[Windows.Forms.MessageBoxButtons]::OK,[Windows.Forms.MessageBoxIcon]::Error)}catch{Write-Warning $message}}
 return $message
}
function Test-StartupSnapshot($State,[int]$ProcessId,[datetime]$Started,[datetime]$Written,[datetime]$Now=[datetime]::UtcNow){
 return $null -ne $State -and $State.version -eq 1 -and $State.pid -eq $ProcessId -and $State.ready -is [bool] -and $Written -ge $Started -and ($Now-$Written).TotalSeconds -ge 0 -and ($Now-$Written).TotalSeconds -lt 6
}
function Test-StartupReadiness($State,[int]$ProcessId,[datetime]$Started,[datetime]$Written,[datetime]$Now=[datetime]::UtcNow){
 return (Test-StartupSnapshot $State $ProcessId $Started $Written $Now) -and $State.ready
}
function Test-StartupPanelProgress($State){
 foreach($monitor in $State.monitors){
  if($monitor.enabled -isnot [bool] -or !$monitor.enabled){continue}
  foreach($componentReady in @($monitor.rendererReady,$monitor.leftReady,$monitor.topReady,$monitor.rightReady)){
   if($componentReady -is [bool] -and $componentReady){return $true}
  }
 }
 return $false
}
function Read-DesktopHostState([string]$File,$Process){
 if(!$Process -or $Process.HasExited){return}
 try{
  $item=Get-Item -LiteralPath $File -ErrorAction Stop
  if($item.Length -gt 4096){return}
  $data=[IO.File]::ReadAllText($File)|ConvertFrom-Json -ErrorAction Stop
  if($data.pid -eq $Process.Id -and $data.startTicks -ceq [string]$Process.StartTime.ToUniversalTime().Ticks -and $data.state -in 'starting','running','stopping','detached' -and $data.companionRunning -is [bool]){return $data}
 }catch [IO.IOException]{}catch [Management.Automation.RuntimeException]{}catch [InvalidOperationException]{}
}
function Get-DesktopProcess([string]$Executable){
 $session=[Diagnostics.Process]::GetCurrentProcess().SessionId
 $candidates=@(Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $Executable -and $_.SessionId -eq $session})
 if(!$candidates.Count){return}
 $ownerFile=Join-Path (Split-Path -Parent $Executable) 'desktop-host.json'
 foreach($process in $candidates){if(Read-DesktopHostState $ownerFile $process){return $process}}
 # The owner can be starting or an older build with no status file.
 foreach($process in $candidates){
  $details=Get-CimInstance Win32_Process -Filter ('ProcessId='+$process.Id)
  if($details.CommandLine -match '\s+--renderer\s*$' -and !$process.HasExited){return $process}
 }
}
function Get-CompanionProcess([string]$Executable){
 $session=[Diagnostics.Process]::GetCurrentProcess().SessionId
 $ownerFile=Join-Path (Split-Path -Parent $Executable) 'companion-host.json'
 foreach($process in Get-Process LiquidDesktop -ErrorAction SilentlyContinue){
  if($process.Path -eq $Executable -and $process.SessionId -eq $session -and (Read-DesktopHostState $ownerFile $process)){return $process}
 }
}
function Test-DesktopCompanionRunning([string]$Executable){
 $process=Get-CompanionProcess $Executable
 if(!$process){return $false}
 $state=Read-DesktopHostState (Join-Path (Split-Path -Parent $Executable) 'companion-host.json') $process
 return $state.companionRunning -eq $true -or $state.state -eq 'starting'
}
function Stop-DesktopRenderer([string]$Executable,[string]$HostFile,$Process,[bool]$Maintenance=$false){
 if(!$Process -or $Process.HasExited){return $false}
 $state=Read-DesktopHostState $HostFile $Process
 if(!$Maintenance -and $state -and $state.state -eq 'detached'){return $state.companionRunning}
 $argument=if($Maintenance){'--maintenance-stop'}else{'--quit'}
 & $Executable $argument
 $deadline=[DateTime]::UtcNow.AddSeconds(8)
 do{
  if($Process.WaitForExit(70)){return $false}
  $state=Read-DesktopHostState $HostFile $Process
  if(!$Maintenance -and $state -and $state.state -eq 'detached'){return $state.companionRunning}
 }while([DateTime]::UtcNow -lt $deadline)
 throw 'PrismGlass 未确认退出。请关闭桌面或伴随屏管理窗口后重试。'
}
function Test-PrismGlassRainmeterProcess([string]$Executable){
 $processes=@(Get-Process Rainmeter -ErrorAction SilentlyContinue)
 try{foreach($process in $processes){
  $path=$process.Path
  if(!$path){throw '无法确认 Rainmeter 进程所属的安装位置；请检查进程权限后重试。'}
  if($path -eq $Executable){return $true}
 }}finally{foreach($process in $processes){$process.Dispose()}}
 return $false
}
function Start-PrismGlassRainmeter([string]$Executable,[int]$TimeoutMilliseconds=10000){
 if(!(Test-Path -LiteralPath $Executable -PathType Leaf)){throw '找不到已配置的 Rainmeter 程序，请重新运行 PrismGlass 安装器检查必需组件。'}
 $wasRunning=[RainmeterCommands]::Find($Executable) -ne [IntPtr]::Zero
 if(!$wasRunning){$wasRunning=Test-PrismGlassRainmeterProcess $Executable}
 $started=$null
 try{
  if(!$wasRunning){$started=Start-Process -FilePath $Executable -WindowStyle Hidden -PassThru;if(!$started){throw 'Rainmeter 进程未能启动。'}}
  $wait=[Diagnostics.Stopwatch]::StartNew()
  do{
   if([RainmeterCommands]::Probe($Executable,50)){return $wasRunning}
   if($started -and $started.HasExited){throw ('Rainmeter 在控制窗口就绪前退出，退出代码：'+$started.ExitCode+'。')}
   if($wait.ElapsedMilliseconds -ge $TimeoutMilliseconds){break}
   Start-Sleep -Milliseconds 100
  }while($true)
  throw 'Rainmeter 控制窗口未在规定时间内响应。已停止启动桌面，且没有继续创建命令进程；请恢复 Rainmeter 后重试。'
 }finally{if($started){$started.Dispose()}}
}
function Invoke-PrismGlassRainmeter([string]$Executable,[string]$Command){
 if($script:rainmeterCommandFailure){throw $script:rainmeterCommandFailure}
 try{[RainmeterCommands]::Send($Executable,$Command,250)}catch{
  $script:rainmeterCommandFailure='Rainmeter 控制失败，本次切换已停止发送后续命令：'+$_.Exception.GetBaseException().Message
  throw $script:rainmeterCommandFailure
 }
}
function Restore-PrismGlassShell {
 [DesktopTaskbar]::Set(2,$true)
 Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name HideIcons -Value 0
 if(![DesktopTaskbar]::ShellReady()){throw 'Windows 桌面尚未就绪，无法确认普通桌面已恢复。'}
 [DesktopIcons]::SetVisible($true)
 if(![DesktopTaskbar]::IconsVisible() -or ![DesktopTaskbar]::TaskbarsVisible() -or ([DesktopTaskbar]::State() -band 1) -ne 0){throw '普通桌面的图标或任务栏未能恢复，请检查资源管理器状态和进程权限。'}
}
function Update-PrismGlassSkinCatalog([string]$Executable,[string]$PendingFile,[bool]$WasRunning){
 if(!$WasRunning -or !(Test-Path -LiteralPath $PendingFile)){return}
 Invoke-PrismGlassRainmeter $Executable '!RefreshApp'
 # RefreshApp registers new directories asynchronously; do not combine it with activation.
 Start-Sleep -Milliseconds 1000
 if(![RainmeterCommands]::Probe($Executable,50)){$script:rainmeterCommandFailure='Rainmeter 刷新皮肤列表后未响应，已停止激活组件。';throw $script:rainmeterCommandFailure}
}
function Stop-PrismGlassSkins([string]$Executable,[int]$TimeoutMilliseconds=3000,[switch]$KeepCompanion,[switch]$CollectorsOnly){
 if($script:rainmeterCommandFailure){throw $script:rainmeterCommandFailure}
 if([RainmeterCommands]::Find($Executable) -eq [IntPtr]::Zero -and !(Test-PrismGlassRainmeterProcess $Executable)){return}
 $collectors=@('Focus','Music','System')
 # An upgrade also closes retired skins that the previous version may have left active.
 $configs=if($CollectorsOnly){$collectors}else{@('Dock','Weather','Clock','Calendar','Battery','Folders','Tools','BreathingOrb','DigitalClock','Moon','Hourglass','SunPath','YearDots','Tide','Orrery','Bonsai','Photo','Photo2','Photo3','Photo4','Photo5','Photo6','Topbar','Starlight','MoodNote')}
 if($KeepCompanion){
  foreach($config in $collectors){Invoke-PrismGlassRainmeter $Executable ('!Hide "PrismGlass\'+$config+'"')}
 }elseif(!$CollectorsOnly){$configs+=$collectors}
 $configured=Join-Path $PSScriptRoot 'LiquidDesktop/skin-path.txt'
 $skins=if([IO.File]::Exists($configured)){[IO.Path]::GetFullPath([IO.File]::ReadAllText($configured).Trim())}else{Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter/Skins/PrismGlass'}
 if(!$CollectorsOnly -and (Test-Path -LiteralPath $skins)){foreach($folder in Get-ChildItem -LiteralPath $skins -Directory -Filter 'Dock-monitor-*'){
  $file=Join-Path $folder.FullName 'Main.ini'
  if($folder.Name -match '^Dock-monitor-[0-9]+$' -and (Test-Path -LiteralPath $file) -and (Get-Content -LiteralPath $file -TotalCount 1) -eq '; PrismGlass generated monitor instance'){$configs+=$folder.Name}
 }}
 foreach($config in $configs){Invoke-PrismGlassRainmeter $Executable ('!DeactivateConfig "PrismGlass\'+$config+'"')}
 $wait=[Diagnostics.Stopwatch]::StartNew()
 do{
  $open=@($configs | Where-Object {[RainmeterCommands]::SkinOpen((Join-Path $skins ($_+'/Main.ini')))})
  if(!$open.Count){return}
  if($wait.ElapsedMilliseconds -ge $TimeoutMilliseconds){break}
  Start-Sleep -Milliseconds 100
 }while($true)
 $script:rainmeterCommandFailure='Rainmeter 尚未关闭 PrismGlass 皮肤：'+($open -join '、')+'。已停止继续发送命令，请恢复 Rainmeter 后重试。'
 throw $script:rainmeterCommandFailure
}
function Stop-PrismGlassRainmeter([string]$Executable,[int]$TimeoutMilliseconds=3000){
 if($script:rainmeterCommandFailure){throw $script:rainmeterCommandFailure}
 if([RainmeterCommands]::Find($Executable) -eq [IntPtr]::Zero -and !(Test-PrismGlassRainmeterProcess $Executable)){return}
 try{return [RainmeterCommands]::QuitIfIdle($Executable,250,$TimeoutMilliseconds)}catch{
  $script:rainmeterCommandFailure='Rainmeter 未能正常退出，本次切换已停止重试：'+$_.Exception.GetBaseException().Message
  throw $script:rainmeterCommandFailure
 }
}
$startedRenderer=$null;$requestedHost=$null;$rollbackRenderer=$null;$startedBridge=$null;$beautySkinsActivated=$false;$otherInstallationRunning=$false;$modeLockHeld=$false;$script:rainmeterCommandFailure=$null
$modeLock=[Threading.Mutex]::new($false,'Local\PrismGlassDesktopMode')
try {
if($Mode -eq 'Beauty'){'PRISMGLASS_STARTUP_STAGE SHELL'}
try{$modeLockHeld=$modeLock.WaitOne(45000)}catch [Threading.AbandonedMutexException]{$modeLockHeld=$true}
if(!$modeLockHeld){throw '另一次桌面切换仍在进行，请稍等片刻后重试。'}
$statePath=Join-Path $PSScriptRoot 'desktop-mode-state.json'
$readyPath=Join-Path $PSScriptRoot 'LiquidDesktop\desktop-ready.flag'
$glassPath=Join-Path $PSScriptRoot 'LiquidDesktop\LiquidDesktop.exe'
$hostFile=Join-Path $PSScriptRoot 'LiquidDesktop\desktop-host.json'
$bridge=Join-Path $PSScriptRoot 'MusicBridge.py'
$rainmeter=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LiquidDesktop/rainmeter-path.txt') -Encoding UTF8 -Raw).Trim()
$pythonCommand=Get-Command pythonw.exe -ErrorAction SilentlyContinue
$python=if($pythonCommand){$pythonCommand.Source}else{$null}
if(!('DesktopIcons' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'LiquidDesktop\DesktopIcons.cs')}
if($Mode -ne 'Login' -and !('RainmeterCommands' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'LiquidDesktop\RainmeterCommands.cs')}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DesktopTaskbar {
 [StructLayout(LayoutKind.Sequential)] public struct RECT {public int L,T,R,B;}
 [StructLayout(LayoutKind.Sequential)] public struct DATA {public int cbSize;public IntPtr hWnd;public uint message,edge;public RECT rect;public IntPtr param;}
 [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint m,ref DATA d);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string c,string t);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string c,string t);
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int n);
 [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
 public static bool RestoreKeysHeld(){foreach(int key in new[]{16,17,18,123})if((GetAsyncKeyState(key)&0x8000)!=0)return true;return false;}
 delegate bool EnumWindowProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback,IntPtr p);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr h,out uint key,out byte alpha,out uint flags);
 [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h,uint key,byte alpha,uint flags);
 [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr h,IntPtr rect,IntPtr region,uint flags);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out RECT rect);
 [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
 public static bool BottomDockReady(int pid){bool ready=false;EnumWindows((h,p)=>{uint owner;GetWindowThreadProcessId(h,out owner);RECT r;if(owner==(uint)pid&&IsWindowVisible(h)&&GetWindowRect(h,out r)&&r.R-r.L>400&&r.T>GetSystemMetrics(1)/2){ready=true;return false;}return true;},IntPtr.Zero);return ready;}
 static IntPtr DesktopView(){IntPtr found=IntPtr.Zero;EnumWindows((h,p)=>{var view=FindWindowEx(h,IntPtr.Zero,"SHELLDLL_DefView",null);if(view==IntPtr.Zero)return true;found=view;return false;},IntPtr.Zero);return found;}
 public static bool IconsVisible(){var view=DesktopView();var list=FindWindowEx(view,IntPtr.Zero,"SysListView32",null);return view!=IntPtr.Zero && list!=IntPtr.Zero && IsWindowVisible(view) && IsWindowVisible(list);}
 public static bool ShellReady(){var view=DesktopView();return view!=IntPtr.Zero && FindWindowEx(view,IntPtr.Zero,"SysListView32",null)!=IntPtr.Zero && FindWindow("Shell_TrayWnd",null)!=IntPtr.Zero;}
 public static bool TaskbarsVisible(){return TaskbarsMatch(true);}
 public static bool TaskbarsHidden(){return TaskbarsMatch(false);}
 static bool TaskbarsMatch(bool visible){
  var primary=FindWindow("Shell_TrayWnd",null);if(primary==IntPtr.Zero||IsWindowVisible(primary)!=visible)return false;
  IntPtr secondary=IntPtr.Zero;while((secondary=FindWindowEx(IntPtr.Zero,secondary,"Shell_SecondaryTrayWnd",null))!=IntPtr.Zero)if(IsWindowVisible(secondary)!=visible)return false;
  return true;
 }
 public static int State(){var d=new DATA();d.cbSize=Marshal.SizeOf(d);return (int)SHAppBarMessage(4,ref d).ToUInt64();}
 [StructLayout(LayoutKind.Sequential)] public struct MONITOR {public int size;public RECT monitor,work;public uint flags;}
 [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("user32.dll")]static extern bool GetMonitorInfo(IntPtr h,ref MONITOR info);
 [DllImport("user32.dll")]static extern bool SystemParametersInfo(uint action,uint param,ref RECT rect,uint flags);
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 public static void FullWorkArea(){var old=SetThreadDpiAwarenessContext(new IntPtr(-4));try{var m=new MONITOR{size=Marshal.SizeOf(typeof(MONITOR))};if(GetMonitorInfo(MonitorFromWindow(IntPtr.Zero,1),ref m)){var rect=m.monitor;if(!SystemParametersInfo(47,0,ref rect,2))throw new InvalidOperationException("Unable to release the taskbar work area");}}finally{if(old!=IntPtr.Zero)SetThreadDpiAwarenessContext(old);}}
 public static void Set(int state,bool visible){var d=new DATA();d.cbSize=Marshal.SizeOf(d);d.hWnd=FindWindow("Shell_TrayWnd",null);d.param=new IntPtr(state);SHAppBarMessage(10,ref d);{ShowWindow(d.hWnd,visible?5:0);IntPtr h=IntPtr.Zero;while((h=FindWindowEx(IntPtr.Zero,h,"Shell_SecondaryTrayWnd",null))!=IntPtr.Zero)ShowWindow(h,visible?5:0);}}
}
'@
$ownedBridge={Get-CimInstance Win32_Process -Filter "Name='pythonw.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($bridge)}}
if($Mode -eq 'CompanionCleanup'){
 if(Test-DesktopCompanionRunning $glassPath){return}
 $process=Get-DesktopProcess $glassPath
 $hostState=Read-DesktopHostState $hostFile $process
 if($process -and !$hostState -and $process.WaitForExit(1000)){
  $process=Get-DesktopProcess $glassPath
  $hostState=Read-DesktopHostState $hostFile $process
 }
 if($process -and (!$hostState -or $hostState.state -ne 'detached' -or $hostState.companionRunning)){return}
 & $ownedBridge | ForEach-Object {Stop-Process -Id $_.ProcessId -ErrorAction Stop}
 Stop-PrismGlassSkins $rainmeter -CollectorsOnly
 [void](Stop-PrismGlassRainmeter $rainmeter)
 return
}
$shellDeadline=(Get-Date).AddSeconds(30)
while(![DesktopTaskbar]::ShellReady() -and (Get-Date) -lt $shellDeadline){Start-Sleep -Milliseconds 250}
if(![DesktopTaskbar]::ShellReady()){throw 'Windows 桌面暂时没有响应，请等待资源管理器恢复后重试。'}
if($Mode -eq 'Login'){
 # Restore only the native shell at sign-in. Never start beauty or stop user apps.
 # A manual beauty launch that already completed wins over a delayed login action.
 $manualBeauty=Get-DesktopProcess $glassPath
 $hostState=Read-DesktopHostState $hostFile $manualBeauty
 if($manualBeauty -and (!$hostState -or $hostState.state -ne 'detached')){'LOGIN: Manual beauty session already running; native shell left unchanged.';return}
 & (Join-Path $PSScriptRoot 'CursorTheme.ps1') -Mode Restore
 Restore-PrismGlassShell
 @{Time=(Get-Date).ToString('o');Mode='Ordinary';IconsVisible=$true;TaskbarState=[DesktopTaskbar]::State();BeautyStarted=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'ordinary-login-status.json') -Encoding utf8
 'LOGIN: Ordinary desktop icons and Windows taskbar restored; no beauty components started.'
 return
}
if($Mode -eq 'Game' -or $Mode -eq 'Recovery'){
 & (Join-Path $PSScriptRoot 'Pegline.ps1') -Mode Stop
 & (Join-Path $PSScriptRoot 'CursorTheme.ps1') -Mode Restore
 if($Mode -eq 'Game' -and !(Test-Path $statePath)){
  @{TaskbarState=[DesktopTaskbar]::State();DesktopIconsVisible=[DesktopTaskbar]::IconsVisible();HideIcons=(Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced').HideIcons;Created=(Get-Date).ToString('o')} | ConvertTo-Json | Set-Content $statePath -Encoding utf8
 }
 # Stop only this desktop scheme; leave games, music and other user apps running.
 if($StopCompanion){
  $companion=Get-CompanionProcess $glassPath
  if($companion){& $glassPath '--stop-companion';if(!$companion.WaitForExit(8000)){throw '伴随屏未能正常关闭。'}}
 }
 if(Test-Path -LiteralPath $readyPath){Remove-Item -LiteralPath $readyPath}
 $keepCompanion=Stop-DesktopRenderer $glassPath $hostFile (Get-DesktopProcess $glassPath) $StopCompanion.IsPresent
 $keepCompanion=$keepCompanion -or (Test-DesktopCompanionRunning $glassPath)
 if(!$keepCompanion){& $ownedBridge | ForEach-Object {Stop-Process -Id $_.ProcessId -ErrorAction Stop}}
 Stop-PrismGlassSkins $rainmeter -KeepCompanion:$keepCompanion
 $remainingSkins=if(!$keepCompanion){@(Stop-PrismGlassRainmeter $rainmeter)}
 Restore-PrismGlassShell
 $exitMessage=if($keepCompanion){'PrismGlass 桌面已退出；iPad 伴随屏继续运行。'}elseif($remainingSkins.Count){'PrismGlass 已退出；Rainmeter 还承载以下其他皮肤，已保留该进程：'+($remainingSkins -join '、')}else{'PrismGlass 和配置的 Rainmeter 已退出。'}
 $exitStatus=Join-Path $PSScriptRoot 'LiquidDesktop/runtime-data/rainmeter-exit.json'
 try{[IO.Directory]::CreateDirectory((Split-Path $exitStatus)) | Out-Null;@{time=[datetime]::UtcNow.ToString('o');status=$(if($keepCompanion){'companion'}elseif($remainingSkins.Count){'preserved'}else{'exited'});executable=$rainmeter;remaining=$remainingSkins;message=$exitMessage} | ConvertTo-Json | Set-Content -LiteralPath $exitStatus -Encoding utf8}catch{Write-Warning ('Rainmeter 退出记录无法保存：'+$_.Exception.Message)}
 if($remainingSkins.Count){
  Write-Warning $exitMessage
  if($ShowErrors){try{Add-Type -AssemblyName System.Windows.Forms;[void][Windows.Forms.MessageBox]::Show($exitMessage,'PrismGlass 已退出',[Windows.Forms.MessageBoxButtons]::OK,[Windows.Forms.MessageBoxIcon]::Information)}catch{Write-Warning $_.Exception.Message}}
 }
 $exitMessage
}else{
 $otherGlass=Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -ne $glassPath}
 if($otherGlass){$otherInstallationRunning=$true;throw '另一处安装的 PrismGlass 正在运行，请从它的托盘菜单退出后重试。'}
 $readinessPath=Join-Path $PSScriptRoot 'LiquidDesktop\readiness.json'
 $running=Get-DesktopProcess $glassPath
 if($running -and (Test-Path -LiteralPath $readyPath) -and (Test-Path -LiteralPath $readinessPath)){
  try{$existing=Get-Content -LiteralPath $readinessPath -Raw -Encoding UTF8 | ConvertFrom-Json
   if((Test-StartupReadiness $existing $running.Id $running.StartTime.ToUniversalTime() (Get-Item -LiteralPath $readinessPath).LastWriteTimeUtc) -and [DesktopTaskbar]::IconsVisible() -eq !$existing.primaryEnabled -and (!$existing.taskbarHiddenExpected -or [DesktopTaskbar]::TaskbarsHidden()) -and !$running.HasExited){
    'PRISMGLASS_DESKTOP_READY'
    return
   }
  }catch{Write-Verbose $_.Exception.Message}
 }
 $rollbackRenderer=$running
 if(Test-Path -LiteralPath $readyPath){Remove-Item -LiteralPath $readyPath}
 # Keep the ordinary desktop usable until every required component has painted.
 [DesktopTaskbar]::Set(2,$true)
 [DesktopIcons]::SetVisible($true)
 # Let the restore shortcut fully release before starting either desktop app.
 $releaseDeadline=(Get-Date).AddSeconds(15)
 while([DesktopTaskbar]::RestoreKeysHeld() -and (Get-Date) -lt $releaseDeadline){Start-Sleep -Milliseconds 100}
 if([DesktopTaskbar]::RestoreKeysHeld()){throw '请松开 Ctrl、Alt、Shift 和 F12 快捷键后，再次打开 PrismGlass。'}
 'PRISMGLASS_STARTUP_STAGE SERVICES'
 $rainmeterWasRunning=Start-PrismGlassRainmeter $rainmeter
 $catalogPending=Join-Path $PSScriptRoot 'LiquidDesktop/skin-catalog-pending.flag'
 Update-PrismGlassSkinCatalog $rainmeter $catalogPending $rainmeterWasRunning
 $beautySkinsActivated=$true
 foreach($config in @('Focus','Music','System','Weather')){Invoke-PrismGlassRainmeter $rainmeter ('!ActivateConfig "PrismGlass\'+$config+'" "Main.ini"')}
 if($python -and (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'music-bridge.enabled')) -and !(& $ownedBridge)){$startedBridge=Start-Process $python -ArgumentList ('"'+$bridge+'"') -WindowStyle Hidden -PassThru}
 'PRISMGLASS_STARTUP_STAGE RENDERER'
 $running=Get-DesktopProcess $glassPath
 $rollbackRenderer=$running
 $requestedAt=[DateTime]::UtcNow
 if(!$running){$startedRenderer=Start-Process $glassPath -ArgumentList '--renderer' -WindowStyle Hidden -PassThru}
 else{
  $hostState=Read-DesktopHostState $hostFile $running
  if(!$hostState -or $hostState.state -eq 'detached'){$requestedHost=$running;& $glassPath '--renderer'}
 }
 $rendererDeadline=(Get-Date).AddSeconds(35)
 $renderReady=$false;$panelsReported=$false;$widgetsPending=$false
 while((Get-Date) -lt $rendererDeadline){
  if($startedRenderer -and $startedRenderer.HasExited){throw ('桌面渲染进程在就绪之前退出，退出代码：'+$startedRenderer.ExitCode+'。')}
  $glassProcess=if($requestedHost){$requestedHost}elseif($startedRenderer){$startedRenderer}else{Get-DesktopProcess $glassPath}
  if($requestedHost -and $requestedHost.HasExited){throw '伴随屏进程在重新打开桌面前退出。'}
  if($glassProcess -and (Test-Path -LiteralPath $readinessPath)){
   try{$readiness=Get-Content -LiteralPath $readinessPath -Encoding UTF8 -Raw | ConvertFrom-Json
    $rendererStarted=if($requestedHost){$requestedAt}else{$glassProcess.StartTime.ToUniversalTime()}
    if(Test-StartupSnapshot $readiness $glassProcess.Id $rendererStarted (Get-Item -LiteralPath $readinessPath).LastWriteTimeUtc){
     $widgetsPending=$readiness.widgetsReady -is [bool] -and !$readiness.widgetsReady
     if($readiness.ready){$renderReady=$true;break}
     if(!$panelsReported -and (Test-StartupPanelProgress $readiness)){'PRISMGLASS_STARTUP_STAGE PANELS';$panelsReported=$true}
    }
   }catch{Write-Verbose ('Readiness pending: '+$_.Exception.Message)}
  }
  Start-Sleep -Milliseconds 250
 }
 if(!$renderReady){
  if($widgetsPending){throw '已启用的桌面小组件未能完成显示。请检查 Rainmeter 后重试。'}
  throw '桌面组件未能在规定时间内完成启动。请检查 Rainmeter 与 WebView2 是否可用，再重新打开 PrismGlass。'
 }
 'PRISMGLASS_STARTUP_STAGE HANDOFF'
 if(Test-Path -LiteralPath $catalogPending){Remove-Item -LiteralPath $catalogPending}
 # Clear both Explorer's cached FWF_NOICONS and saved folder-view preference.
 # Only the current icon window is hidden; next login remains ordinary.
 Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name HideIcons -Value 0
 [DesktopIcons]::SetVisible(!$readiness.primaryEnabled)
 if([DesktopTaskbar]::IconsVisible() -ne !$readiness.primaryEnabled){throw 'Windows 未能确认桌面图标状态，已中止桌面交接。'}
 Set-Content -LiteralPath $readyPath -Value (Get-Date).ToString('o') -Encoding ascii
 if($readiness.taskbarHiddenExpected -is [bool] -and $readiness.taskbarHiddenExpected){
  $shellHandoffDeadline=[DateTime]::UtcNow.AddSeconds(4)
  while(![DesktopTaskbar]::TaskbarsHidden() -and [DateTime]::UtcNow -lt $shellHandoffDeadline){
   if($glassProcess.HasExited){throw '桌面渲染进程在交接期间退出。'}
   Start-Sleep -Milliseconds 60
  }
  if(![DesktopTaskbar]::TaskbarsHidden()){throw '任务栏未完成切换，已中止桌面交接。'}
 }
 if($glassProcess.HasExited){throw '桌面渲染进程在交接期间退出。'}
 'PRISMGLASS_DESKTOP_READY'
 # LiquidDesktop temporarily hides the taskbar and releases its work area after readiness.
 # Do not enable persistent auto-hide or preempt its work-area restore snapshot here.
 if(Test-Path $statePath){Remove-Item -LiteralPath $statePath}
 & (Join-Path $PSScriptRoot 'CursorTheme.ps1') -Mode Apply
 try { & (Join-Path $PSScriptRoot 'Pegline.ps1') -Mode Start } catch { Write-Warning $_.Exception.Message }
 'BEAUTY: Current desktop layout restored.'
}
} catch {
 $failure=$_
 $recoveryFailures=[Collections.Generic.List[string]]::new()
 if($modeLockHeld -and !$otherInstallationRunning -and $Mode -ne 'CompanionCleanup'){
  if($Mode -eq 'Beauty'){'PRISMGLASS_STARTUP_STAGE ROLLBACK'}
 $keepCompanion=$false
 if($Mode -eq 'Beauty'){
  $target=if($startedRenderer){$startedRenderer}else{$rollbackRenderer}
  $keepCompanion=(Read-DesktopHostState $hostFile (Get-DesktopProcess $glassPath)).companionRunning -eq $true
  if($target -and !$target.HasExited){try{$keepCompanion=Stop-DesktopRenderer $glassPath $hostFile $target}catch{$recoveryFailures.Add('关闭渲染进程：'+$_.Exception.Message)}}
  $keepCompanion=$keepCompanion -or (Test-DesktopCompanionRunning $glassPath)
 }
 if($beautySkinsActivated){try{Stop-PrismGlassSkins $rainmeter -KeepCompanion:$keepCompanion}catch{$recoveryFailures.Add('关闭 PrismGlass 皮肤：'+$_.Exception.Message)}}
 if(!$keepCompanion -and $Mode -eq 'Beauty' -and $rainmeterWasRunning -eq $false){try{[void](Stop-PrismGlassRainmeter $rainmeter)}catch{$recoveryFailures.Add('退出本次启动的 Rainmeter：'+$_.Exception.Message)}}
 if(!$keepCompanion -and $startedBridge -and !$startedBridge.HasExited){try{Stop-Process -Id $startedBridge.Id -ErrorAction Stop}catch{$recoveryFailures.Add('关闭音乐桥接：'+$_.Exception.Message)}}
 try { & (Join-Path $PSScriptRoot 'CursorTheme.ps1') -Mode Restore } catch {$recoveryFailures.Add('恢复光标：'+$_.Exception.Message)}
 # Also covers manual Beauty launches, not only the login wrapper.
 if('DesktopTaskbar' -as [type]){
  if(Test-Path -LiteralPath $readyPath){Remove-Item -LiteralPath $readyPath -ErrorAction SilentlyContinue}
  try{Restore-PrismGlassShell}catch{$recoveryFailures.Add('恢复普通桌面：'+$_.Exception.Message)}
 }
 }
 $failureReason=$failure.Exception.Message
 if($recoveryFailures.Count){$failureReason+="`r`n恢复过程中仍有问题："+($recoveryFailures -join '；')}
 [void](Write-DesktopModeFailure $PSScriptRoot $Mode $failureReason $ShowErrors.IsPresent)
 if($recoveryFailures.Count){throw [InvalidOperationException]::new($failureReason,$failure.Exception)}
 throw $failure
} finally {if($startedRenderer){$startedRenderer.Dispose()};if($startedBridge){$startedBridge.Dispose()};if($modeLockHeld){$modeLock.ReleaseMutex()};$modeLock.Dispose()}
