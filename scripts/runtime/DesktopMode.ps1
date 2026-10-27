param([ValidateSet('Game','Beauty','Recovery','Login')][string]$Mode='Beauty')
$ErrorActionPreference='Stop'
function Test-StartupReadiness($State,[int]$ProcessId,[datetime]$Started,[datetime]$Written,[datetime]$Now=[datetime]::UtcNow){
 return $null -ne $State -and $State.version -eq 1 -and $State.pid -eq $ProcessId -and $State.ready -is [bool] -and $State.ready -and $Written -ge $Started -and ($Now-$Written).TotalSeconds -ge 0 -and ($Now-$Written).TotalSeconds -lt 6
}
function Update-PrismGlassSkinCatalog([string]$Executable,[string]$PendingFile,[bool]$WasRunning){
 if(!$WasRunning -or !(Test-Path -LiteralPath $PendingFile)){return}
 & $Executable '!RefreshApp'
 if($LASTEXITCODE -ne 0){throw 'Rainmeter could not refresh its skin catalog. Retry after closing its settings window.'}
 # RefreshApp registers new directories asynchronously; do not combine it with activation.
 Start-Sleep -Milliseconds 1000
}
function Stop-PrismGlassSkins([string]$Executable){
 if(!(Get-Process Rainmeter -ErrorAction SilentlyContinue)){return}
 foreach($config in @('Dock','Focus','Music','System','Weather','Topbar')){& $Executable '!DeactivateConfig' ('PrismGlass\'+$config)}
 $configured=Join-Path $PSScriptRoot 'LiquidDesktop/skin-path.txt'
 $skins=if([IO.File]::Exists($configured)){[IO.Path]::GetFullPath([IO.File]::ReadAllText($configured).Trim())}else{Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter/Skins/PrismGlass'}
 if(Test-Path -LiteralPath $skins){foreach($folder in Get-ChildItem -LiteralPath $skins -Directory -Filter 'Dock-monitor-*'){
  $file=Join-Path $folder.FullName 'Main.ini'
  if($folder.Name -match '^Dock-monitor-[0-9]+$' -and (Test-Path -LiteralPath $file) -and (Get-Content -LiteralPath $file -TotalCount 1) -eq '; PrismGlass generated monitor instance'){& $Executable '!DeactivateConfig' ('PrismGlass\'+$folder.Name)}
 }}
}
$startedRenderer=$null;$startedBridge=$null;$beautySkinsActivated=$false;$otherInstallationRunning=$false
$modeLock=[Threading.Mutex]::new($false,'Local\PrismGlassDesktopMode')
if(!$modeLock.WaitOne(45000)){$modeLock.Dispose();throw 'Another desktop mode change is still running.'}
try {
$statePath=Join-Path $PSScriptRoot 'desktop-mode-state.json'
$readyPath=Join-Path $PSScriptRoot 'LiquidDesktop\desktop-ready.flag'
$bridge=Join-Path $PSScriptRoot 'MusicBridge.py'
$rainmeter=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LiquidDesktop/rainmeter-path.txt') -Raw).Trim()
$pythonCommand=Get-Command pythonw.exe -ErrorAction SilentlyContinue
$python=if($pythonCommand){$pythonCommand.Source}else{$null}
if(!('DesktopIcons' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'LiquidDesktop\DesktopIcons.cs')}
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
 public static bool TaskbarVisible(){return IsWindowVisible(FindWindow("Shell_TrayWnd",null));}
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
$shellDeadline=(Get-Date).AddSeconds(30)
while(![DesktopTaskbar]::ShellReady() -and (Get-Date) -lt $shellDeadline){Start-Sleep -Milliseconds 250}
if(![DesktopTaskbar]::ShellReady()){throw 'Explorer desktop was not ready after 30 seconds.'}
if($Mode -eq 'Login'){
 # Restore only the native shell at sign-in. Never start beauty or stop user apps.
 # A manual beauty launch that already completed wins over a delayed login action.
 $sessionId=(Get-Process -Id $PID).SessionId
 $glassPath=Join-Path $PSScriptRoot 'LiquidDesktop\LiquidDesktop.exe'
 $manualBeauty=Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.SessionId -eq $sessionId -and $_.Path -eq $glassPath}
 if($manualBeauty){'LOGIN: Manual beauty session already running; native shell left unchanged.';return}
 & (Join-Path $PSScriptRoot 'CursorTheme.ps1') -Mode Restore
 [DesktopTaskbar]::Set(2,$true)
 Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name HideIcons -Value 0
 [DesktopIcons]::SetVisible($true)
 if(![DesktopTaskbar]::IconsVisible() -or ![DesktopTaskbar]::TaskbarVisible() -or ([DesktopTaskbar]::State() -band 1) -ne 0){throw 'Native desktop login recovery did not reach the requested state.'}
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
 if(Test-Path -LiteralPath $readyPath){Remove-Item -LiteralPath $readyPath}
 $glassPath=Join-Path $PSScriptRoot 'LiquidDesktop\LiquidDesktop.exe'
 if(Get-Process LiquidDesktop -ErrorAction SilentlyContinue){
  & $glassPath '--quit'
  Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $glassPath} | ForEach-Object {
   if(!$_.WaitForExit(3000)){Stop-Process -Id $_.Id -ErrorAction SilentlyContinue}
  }
 }
 & $ownedBridge | ForEach-Object {Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue}

 Stop-PrismGlassSkins $rainmeter
 [DesktopTaskbar]::Set(2,$true)
 Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name HideIcons -Value 0
 if([DesktopTaskbar]::ShellReady()){[DesktopIcons]::SetVisible($true)}
 'GAME: Desktop renderer, Rainmeter and music bridge stopped; Windows taskbar and desktop icons shown.'
}else{
 $glassPath=Join-Path $PSScriptRoot 'LiquidDesktop\LiquidDesktop.exe'
 $otherGlass=Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -ne $glassPath}
 if($otherGlass){$otherInstallationRunning=$true;throw 'Another PrismGlass installation is running. Exit that installation before starting this one.'}
 if(Test-Path -LiteralPath $readyPath){Remove-Item -LiteralPath $readyPath}
 # Keep the ordinary desktop usable until every required component has painted.
 [DesktopTaskbar]::Set(2,$true)
 [DesktopIcons]::SetVisible($true)
 # Let the restore shortcut fully release before starting either desktop app.
 $releaseDeadline=(Get-Date).AddSeconds(15)
 while([DesktopTaskbar]::RestoreKeysHeld() -and (Get-Date) -lt $releaseDeadline){Start-Sleep -Milliseconds 100}
 if([DesktopTaskbar]::RestoreKeysHeld()){throw 'Release the shortcut keys and run Restore Desktop again.'}
 $iconsVisible=$false
 if(Test-Path $statePath){
  $saved=Get-Content $statePath -Raw|ConvertFrom-Json
  if($null -ne $saved.DesktopIconsVisible){$iconsVisible=[bool]$saved.DesktopIconsVisible}
 }
 $rainmeterWasRunning=[bool](Get-Process Rainmeter -ErrorAction SilentlyContinue)
 if(!$rainmeterWasRunning){Start-Process $rainmeter -WindowStyle Hidden}
 Start-Sleep -Milliseconds 500
 $catalogPending=Join-Path $PSScriptRoot 'LiquidDesktop/skin-catalog-pending.flag'
 Update-PrismGlassSkinCatalog $rainmeter $catalogPending $rainmeterWasRunning
 $beautySkinsActivated=$true
 foreach($config in @('Focus','Music','System','Weather')){& $rainmeter '!ActivateConfig' ('PrismGlass\'+$config) 'Main.ini'}
 if($python -and (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'music-bridge.enabled')) -and !(& $ownedBridge)){$startedBridge=Start-Process $python -ArgumentList ('"'+$bridge+'"') -WindowStyle Hidden -PassThru}
 if(!(Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $glassPath})){$startedRenderer=Start-Process $glassPath -WindowStyle Hidden -PassThru}
 $rendererDeadline=(Get-Date).AddSeconds(35)
 $renderReady=$false
 while((Get-Date) -lt $rendererDeadline){
  $glassProcess=Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $glassPath} | Select-Object -First 1
  $readinessPath=Join-Path $PSScriptRoot 'LiquidDesktop\readiness.json'
  if($glassProcess -and (Test-Path -LiteralPath $readinessPath)){
   try{$readiness=Get-Content -LiteralPath $readinessPath -Raw | ConvertFrom-Json
    if(Test-StartupReadiness $readiness $glassProcess.Id $glassProcess.StartTime.ToUniversalTime() (Get-Item -LiteralPath $readinessPath).LastWriteTimeUtc){$renderReady=$true;break}
   }catch{Write-Verbose ('Readiness pending: '+$_.Exception.Message)}
  }
  Start-Sleep -Milliseconds 250
 }
 if(!$renderReady){throw 'Enabled PrismGlass components did not become ready; ordinary desktop retained.'}
 if(Test-Path -LiteralPath $catalogPending){Remove-Item -LiteralPath $catalogPending}
 Set-Content -LiteralPath $readyPath -Value (Get-Date).ToString('o') -Encoding ascii
 # Clear both Explorer's cached FWF_NOICONS and saved folder-view preference.
 # Only the current icon window is hidden; next login remains ordinary.
 Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name HideIcons -Value 0
 [DesktopIcons]::SetVisible($(if($readiness.primaryEnabled){$iconsVisible}else{$true}))
 # LiquidDesktop temporarily hides the taskbar and releases its work area after readiness.
 # Do not enable persistent auto-hide or preempt its work-area restore snapshot here.
 if(Test-Path $statePath){Remove-Item -LiteralPath $statePath}
 & (Join-Path $PSScriptRoot 'CursorTheme.ps1') -Mode Apply
 try { & (Join-Path $PSScriptRoot 'Pegline.ps1') -Mode Start } catch { Write-Warning $_.Exception.Message }
 'BEAUTY: Current desktop layout restored.'
}
} catch {
 if($otherInstallationRunning){throw}
 if($startedRenderer -and !$startedRenderer.HasExited){
  try{& (Join-Path $PSScriptRoot 'LiquidDesktop\LiquidDesktop.exe') '--quit';if(!$startedRenderer.WaitForExit(3000)){Stop-Process -Id $startedRenderer.Id -ErrorAction SilentlyContinue}}catch{Write-Warning $_.Exception.Message}
 }
 if($beautySkinsActivated){try{Stop-PrismGlassSkins $rainmeter}catch{Write-Warning $_.Exception.Message}}
 if($startedBridge -and !$startedBridge.HasExited){Stop-Process -Id $startedBridge.Id -ErrorAction SilentlyContinue}
 try { & (Join-Path $PSScriptRoot 'CursorTheme.ps1') -Mode Restore } catch { Write-Warning $_.Exception.Message }
 # Also covers manual Beauty launches, not only the login wrapper.
 if('DesktopTaskbar' -as [type]){
  if(Test-Path -LiteralPath $readyPath){Remove-Item -LiteralPath $readyPath -ErrorAction SilentlyContinue}
  [DesktopTaskbar]::Set(2,$true)
  if([DesktopTaskbar]::ShellReady()){[DesktopIcons]::SetVisible($true)}
 }
 throw
} finally {if($startedRenderer){$startedRenderer.Dispose()};if($startedBridge){$startedBridge.Dispose()};$modeLock.ReleaseMutex();$modeLock.Dispose()}
