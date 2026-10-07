param([ValidateSet('Game','Beauty','Recovery','Login')][string]$Mode='Beauty')
$ErrorActionPreference='Stop'
$modeLock=[Threading.Mutex]::new($false,'Local\PrismGlassDesktopMode')
if(!$modeLock.WaitOne(45000)){$modeLock.Dispose();throw 'Another desktop mode change is still running.'}
try {
$statePath=Join-Path $PSScriptRoot 'desktop-mode-state.json'
$readyPath=Join-Path $PSScriptRoot 'LiquidDesktop\desktop-ready.flag'
$bridge=Join-Path $PSScriptRoot 'MusicBridge.py'
$rainmeter=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LiquidDesktop/rainmeter-path.txt') -Raw).Trim()
$pythonCommand=Get-Command pythonw.exe -ErrorAction SilentlyContinue
$python=if($pythonCommand){$pythonCommand.Source}else{$null}
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
 public static void SetIcons(bool visible){var view=DesktopView();if(view==IntPtr.Zero)throw new InvalidOperationException("Desktop icon view was not found.");var list=FindWindowEx(view,IntPtr.Zero,"SysListView32",null);if(list==IntPtr.Zero)throw new InvalidOperationException("Desktop icon list was not found.");if(visible){ShowWindow(view,5);uint key,flags;byte alpha;if(GetLayeredWindowAttributes(view,out key,out alpha,out flags)&&(flags&2)!=0)SetLayeredWindowAttributes(view,key,255,flags);}ShowWindow(list,visible?5:0);RedrawWindow(view,IntPtr.Zero,IntPtr.Zero,0x185);}
 public static bool TaskbarVisible(){return IsWindowVisible(FindWindow("Shell_TrayWnd",null));}
 public static int State(){var d=new DATA();d.cbSize=Marshal.SizeOf(d);return (int)SHAppBarMessage(4,ref d).ToUInt64();}
 [StructLayout(LayoutKind.Sequential)] public struct MONITOR {public int size;public RECT monitor,work;public uint flags;}
 [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
 [DllImport("user32.dll")]static extern bool GetMonitorInfo(IntPtr h,ref MONITOR info);
 [DllImport("user32.dll")]static extern bool SystemParametersInfo(uint action,uint param,ref RECT rect,uint flags);
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 public static void FullWorkArea(){var old=SetThreadDpiAwarenessContext(new IntPtr(-4));try{var m=new MONITOR{size=Marshal.SizeOf(typeof(MONITOR))};if(GetMonitorInfo(MonitorFromWindow(IntPtr.Zero,1),ref m)){var rect=m.monitor;if(!SystemParametersInfo(47,0,ref rect,2))throw new InvalidOperationException("Unable to release the taskbar work area");}}finally{if(old!=IntPtr.Zero)SetThreadDpiAwarenessContext(old);}}
 public static void Set(int state,bool visible){var d=new DATA();d.cbSize=Marshal.SizeOf(d);d.hWnd=FindWindow("Shell_TrayWnd",null);d.param=new IntPtr(state);SHAppBarMessage(10,ref d);{ShowWindow(d.hWnd,visible?5:0);IntPtr h=IntPtr.Zero;while((h=FindWindowEx(IntPtr.Zero,h,"Shell_SecondaryTrayWnd",null))!=IntPtr.Zero)ShowWindow(h,5);}}
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
 [DesktopTaskbar]::SetIcons($true)
 if(![DesktopTaskbar]::IconsVisible() -or ![DesktopTaskbar]::TaskbarVisible() -or ([DesktopTaskbar]::State() -band 1) -ne 0){throw 'Native desktop login recovery did not reach the requested state.'}
 @{Time=(Get-Date).ToString('o');Mode='Ordinary';IconsVisible=$true;TaskbarState=[DesktopTaskbar]::State();BeautyStarted=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'ordinary-login-status.json') -Encoding utf8
 'LOGIN: Ordinary desktop icons and Windows taskbar restored; no beauty components started.'
 return
}
if($Mode -eq 'Game' -or $Mode -eq 'Recovery'){
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

 if(Get-Process Rainmeter -ErrorAction SilentlyContinue){
  foreach($config in @('Dock','Focus','Music','System','Weather','Topbar')){& $rainmeter '!DeactivateConfig' ('PrismGlass\'+$config)}
 }
 [DesktopTaskbar]::Set(2,$true)
 Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name HideIcons -Value 0
 if([DesktopTaskbar]::ShellReady()){[DesktopTaskbar]::SetIcons($true)}
 'GAME: Desktop renderer, Rainmeter and music bridge stopped; Windows taskbar and desktop icons shown.'
}else{
 if(Test-Path -LiteralPath $readyPath){Remove-Item -LiteralPath $readyPath}
 # Keep the ordinary desktop usable until every required component has painted.
 [DesktopTaskbar]::Set(2,$true)
 [DesktopTaskbar]::SetIcons($true)
 # Let the restore shortcut fully release before starting either desktop app.
 $releaseDeadline=(Get-Date).AddSeconds(15)
 while([DesktopTaskbar]::RestoreKeysHeld() -and (Get-Date) -lt $releaseDeadline){Start-Sleep -Milliseconds 100}
 if([DesktopTaskbar]::RestoreKeysHeld()){throw 'Release the shortcut keys and run Restore Desktop again.'}
 $barState=3
 $iconsVisible=$false
 $hideIcons=0
 if(Test-Path $statePath){
  $saved=Get-Content $statePath -Raw|ConvertFrom-Json
  $barState=[int]$saved.TaskbarState
  if($null -ne $saved.DesktopIconsVisible){$iconsVisible=[bool]$saved.DesktopIconsVisible}
  if($null -ne $saved.HideIcons){$hideIcons=[int]$saved.HideIcons}
 }
 if(!(Get-Process Rainmeter -ErrorAction SilentlyContinue)){Start-Process $rainmeter -WindowStyle Hidden}
 Start-Sleep -Milliseconds 500
 foreach($config in @('Dock','Focus','Music','System','Weather')){& $rainmeter '!ActivateConfig' ('PrismGlass\'+$config) 'Main.ini'}
 $dockReady=$false
 for($check=0;$check -lt 40;$check++){
  Start-Sleep -Milliseconds 200
  $rp=Get-Process Rainmeter -ErrorAction SilentlyContinue | Select-Object -First 1
  if($rp -and [DesktopTaskbar]::BottomDockReady($rp.Id)){$dockReady=$true;break}
 }
 if(!$dockReady){[DesktopTaskbar]::Set(2,$true);throw 'Rainmeter Dock not ready; Windows taskbar retained.'}
 if($python -and (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'music-bridge.enabled')) -and !(& $ownedBridge)){Start-Process $python -ArgumentList ('"'+$bridge+'"') -WindowStyle Hidden}
 if(!(Get-Process LiquidDesktop -ErrorAction SilentlyContinue)){Start-Process (Join-Path $PSScriptRoot 'LiquidDesktop\LiquidDesktop.exe') -WindowStyle Hidden}
 $rendererDeadline=(Get-Date).AddSeconds(35)
 $renderReady=$false
 while((Get-Date) -lt $rendererDeadline){
  $glassProcess=Get-Process LiquidDesktop -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq (Join-Path $PSScriptRoot 'LiquidDesktop\LiquidDesktop.exe')}
  $heartbeat=Join-Path $PSScriptRoot 'LiquidDesktop\heartbeat.txt'
  $sideHeartbeat=Join-Path $PSScriptRoot 'LiquidDesktop\side-heartbeat.txt'
  $freshMain=$false;$freshSide=$false;$freshExtensions=$false
  if($glassProcess -and (Test-Path -LiteralPath $heartbeat)){$mainStamp=(Get-Item -LiteralPath $heartbeat).LastWriteTime;$freshMain=($mainStamp -ge $glassProcess.StartTime -and ((Get-Date)-$mainStamp).TotalSeconds -lt 6)}
  if($glassProcess -and (Test-Path -LiteralPath $sideHeartbeat)){$sideStamp=(Get-Item -LiteralPath $sideHeartbeat).LastWriteTime;$freshSide=($sideStamp -ge $glassProcess.StartTime -and ((Get-Date)-$sideStamp).TotalSeconds -lt 12)}
  $extensionBeat=Join-Path $PSScriptRoot 'LiquidDesktop\extensions-heartbeat.txt'; if($glassProcess -and (Test-Path -LiteralPath $extensionBeat)){$extensionStamp=(Get-Item -LiteralPath $extensionBeat).LastWriteTime;$freshExtensions=($extensionStamp -ge $glassProcess.StartTime -and ((Get-Date)-$extensionStamp).TotalSeconds -lt 6)}
  if($freshMain -and $freshSide -and $freshExtensions){$renderReady=$true;break}
  Start-Sleep -Milliseconds 250
 }
 if(!$renderReady){throw 'Glass renderer or side dock did not become ready; ordinary desktop retained.'}
 Set-Content -LiteralPath $readyPath -Value (Get-Date).ToString('o') -Encoding ascii
 Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name HideIcons -Value $hideIcons
 [DesktopTaskbar]::SetIcons($iconsVisible)
 [DesktopTaskbar]::Set(3,$false)
 [DesktopTaskbar]::FullWorkArea()
 if(Test-Path $statePath){Remove-Item -LiteralPath $statePath}
 & (Join-Path $PSScriptRoot 'CursorTheme.ps1') -Mode Apply
 'BEAUTY: Current desktop layout restored.'
}
} catch {
 try { & (Join-Path $PSScriptRoot 'CursorTheme.ps1') -Mode Restore } catch { Write-Warning $_.Exception.Message }
 # Also covers manual Beauty launches, not only the login wrapper.
 if('DesktopTaskbar' -as [type]){
  if(Test-Path -LiteralPath $readyPath){Remove-Item -LiteralPath $readyPath -ErrorAction SilentlyContinue}
  [DesktopTaskbar]::Set(2,$true)
  if([DesktopTaskbar]::ShellReady()){[DesktopTaskbar]::SetIcons($true)}
 }
 throw
} finally {$modeLock.ReleaseMutex();$modeLock.Dispose()}
