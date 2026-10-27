# Windows PowerShell 5.1: target only a temporary silent SMTC publisher, never the user's player.
param([Parameter(Mandatory=$true)][string]$Helper,[Parameter(Mandatory=$true)][string]$OutputRoot)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Media.Playback.MediaPlayer,Windows.Media.Playback,ContentType=WindowsRuntime]
$null=[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime]
$null=[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties,Windows.Media.Control,ContentType=WindowsRuntime]
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$runtimeFacade=[Reflection.Assembly]::LoadWithPartialName('System.Runtime').Location
$interopFacade=[Reflection.Assembly]::LoadWithPartialName('System.Runtime.InteropServices.WindowsRuntime').Location
$observer=Join-Path $OutputRoot 'Observer.cs';$observerDll=Join-Path $OutputRoot 'Observer.dll'
[IO.File]::WriteAllText($observer,@'
public static class PrismMediaControlObserver {
 public static int Count;public static string Last;
 public static void Attach(Windows.Media.SystemMediaTransportControls controls){controls.ButtonPressed+=(sender,args)=>{Last=args.Button.ToString();System.Threading.Interlocked.Increment(ref Count);};}
}
'@)
& (Join-Path $framework 'csc.exe') /nologo /target:library ("/out:"+$observerDll) ("/r:"+(Join-Path $framework 'System.Runtime.WindowsRuntime.dll')) ("/r:"+$runtimeFacade) ("/r:"+$interopFacade) ("/r:"+(Join-Path $env:WINDIR 'System32/WinMetadata/Windows.Media.winmd')) ("/r:"+(Join-Path $env:WINDIR 'System32/WinMetadata/Windows.Foundation.winmd')) $observer
if($LASTEXITCODE -ne 0){throw 'Could not compile isolated media event observer'}
Add-Type -Path $observerDll
$asTask=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'} | Select-Object -First 1
function Await($operation,[Type]$result){$task=$asTask.MakeGenericMethod($result).Invoke($null,@($operation));if(!$task.Wait(2500)){throw 'Fixture media request timed out'};$task.Result}
function Check([bool]$condition,[string]$name){if(!$condition){throw $name};$script:checks++;"PASS $name"}
$player=[Windows.Media.Playback.MediaPlayer]::new();$checks=0;$eventId='PrismGlassControl-'+[guid]::NewGuid().ToString('N')
try{
 $player.CommandManager.IsEnabled=$false;$player.Volume=0
 $controls=$player.SystemMediaTransportControls;$controls.IsEnabled=$true;$controls.IsPlayEnabled=$true;$controls.IsPauseEnabled=$true;$controls.IsPreviousEnabled=$true;$controls.IsNextEnabled=$true
 $controls.DisplayUpdater.Type='Music';$controls.DisplayUpdater.MusicProperties.Title=$eventId;$controls.DisplayUpdater.Update();$controls.PlaybackStatus='Playing'
 [PrismMediaControlObserver]::Attach($controls)
 $manager=Await ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
 $target=$null;$deadline=[datetime]::UtcNow.AddSeconds(8)
 do{foreach($session in $manager.GetSessions()){$metadata=Await ($session.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties]);if($metadata.Title -eq $eventId){$target=$session;break}};if(!$target){Start-Sleep -Milliseconds 200}}while(!$target -and [datetime]::UtcNow -lt $deadline)
 if(!$target){throw 'Isolated media publisher was not registered'}
 $encoded=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($target.SourceAppUserModelId))
 foreach($case in @(@('toggle','Pause'),@('next','Next'),@('previous','Previous'))){
  $before=[PrismMediaControlObserver]::Count
  $stamp=([datetime]::UtcNow-[datetime]'1970-01-01').TotalSeconds.ToString('R',[Globalization.CultureInfo]::InvariantCulture)
  $arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$Helper+'" -Control '+$case[0]+' -Source64 '+$encoded+' -RequestedAt '+$stamp
  $process=Start-Process -FilePath "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait -RedirectStandardError (Join-Path $OutputRoot ($case[0]+'-error.txt'))
  Check ($process.ExitCode -eq 0) ('Targeted '+$case[0]+' request is accepted by Windows');$process.Dispose()
  $deadline=[datetime]::UtcNow.AddSeconds(4)
  while([PrismMediaControlObserver]::Count -eq $before -and [datetime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 50}
  Check ([PrismMediaControlObserver]::Count -eq $before+1 -and [PrismMediaControlObserver]::Last -eq $case[1]) ('Only the isolated publisher receives '+$case[1])
 }
 $before=[PrismMediaControlObserver]::Count
 $arguments='-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$Helper+'" -Control toggle -Source64 '+$encoded+' -RequestedAt 1'
 $process=Start-Process -FilePath "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait -RedirectStandardError (Join-Path $OutputRoot 'expired-error.txt')
 Check ($process.ExitCode -ne 0 -and [PrismMediaControlObserver]::Count -eq $before) 'Expired commands cannot control any player';$process.Dispose()
 "Completed $checks real SMTC control checks; no user playback was changed."
}finally{$controls.IsEnabled=$false;$player.Dispose()}
