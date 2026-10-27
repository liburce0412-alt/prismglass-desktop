# Windows PowerShell 5.1: exercises the real WinRT helper using an isolated silent SMTC publisher.
param([Parameter(Mandatory=$true)][string]$DataRoot,[Parameter(Mandatory=$true)][string]$Helper)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Media.Playback.MediaPlayer,Windows.Media.Playback,ContentType=WindowsRuntime]
$null=[Windows.Media.SystemMediaTransportControlsTimelineProperties,Windows.Media,ContentType=WindowsRuntime]
$player=[Windows.Media.Playback.MediaPlayer]::new()
$reader=$null;$checks=0
function Check($condition,$name){if(!$condition){throw $name};$script:checks++;"PASS $name"}
function Read-TestSession($status){
 $deadline=[datetime]::UtcNow.AddSeconds(8)
 do{
  Start-Sleep -Milliseconds 200
  try{$sample=Get-Content -LiteralPath (Join-Path $DataRoot 'session.json') -Raw | ConvertFrom-Json}catch{$sample=$null}
  if($sample.title -eq 'PrismGlass synthetic media test' -and $sample.status -eq $status){return $sample}
 }while([datetime]::UtcNow -lt $deadline)
 throw "Test publisher was not observed with status $status"
}
try{
 $player.CommandManager.IsEnabled=$false
 $player.Volume=0
 $controls=$player.SystemMediaTransportControls
 $controls.IsEnabled=$true;$controls.IsPlayEnabled=$true;$controls.IsPauseEnabled=$true
 $controls.DisplayUpdater.Type='Music'
 $controls.DisplayUpdater.MusicProperties.Title='PrismGlass synthetic media test'
 $controls.DisplayUpdater.MusicProperties.Artist='Test fixture'
 $controls.DisplayUpdater.MusicProperties.AlbumTitle='Synthetic session'
 $controls.DisplayUpdater.Update()
 $timeline=[Windows.Media.SystemMediaTransportControlsTimelineProperties]::new()
 $timeline.StartTime=[TimeSpan]::Zero;$timeline.EndTime=[TimeSpan]::FromSeconds(120)
 $timeline.MinSeekTime=[TimeSpan]::Zero;$timeline.MaxSeekTime=[TimeSpan]::FromSeconds(120)
 $timeline.Position=[TimeSpan]::FromSeconds(10)
 $controls.UpdateTimelineProperties($timeline)
 $controls.PlaybackStatus='Playing'
 $reader=Start-Process -FilePath "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "'+$Helper+'" -OwnerPid '+$PID+' -DataRoot "'+$DataRoot+'"') -WindowStyle Hidden -PassThru
 $first=Read-TestSession 'playing'
 Check ($first.timeline -and $first.position -ge 10 -and $first.position -lt 18 -and $first.duration -eq 120) 'real SMTC title and time axis reach the helper'
 Check ($first.smtc -and ![string]::IsNullOrWhiteSpace($first.source)) 'real media snapshots retain the session identity for targeted controls'
 $controls.PlaybackStatus='Paused'
 $paused=Read-TestSession 'paused'
 Start-Sleep -Milliseconds 1600
 $still=Read-TestSession 'paused'
 Check ([Math]::Abs($still.position-$paused.position) -lt .1) 'unchanged timeline stays frozen while paused'
 $controls.PlaybackStatus='Playing'
 $resumed=Read-TestSession 'playing'
 Check ($resumed.position -ge $paused.position -and $resumed.position-$paused.position -lt 1.5) 'resume excludes time spent paused'
 $timeline.Position=[TimeSpan]::FromSeconds(40);$controls.UpdateTimelineProperties($timeline)
 Start-Sleep -Milliseconds 600
 $seek=Read-TestSession 'playing'
 Check ($seek.position -ge 40 -and $seek.position -lt 43) 'seek updates the live anchor'
 $controls.DisplayUpdater.MusicProperties.Title='PrismGlass replacement fixture';$controls.DisplayUpdater.Update()
 Start-Sleep -Milliseconds 1300
 $changed=Get-Content -LiteralPath (Join-Path $DataRoot 'session.json') -Raw | ConvertFrom-Json
 Check ($changed.title -eq 'PrismGlass replacement fixture') 'same-player track metadata refreshes'
 "Completed $checks real SMTC helper checks. Publisher sets metadata only; no audio is played."
}finally{
 $controls.IsEnabled=$false
 $player.Dispose()
 if($reader){if(!$reader.HasExited){Stop-Process -Id $reader.Id};$reader.Dispose()}
}
