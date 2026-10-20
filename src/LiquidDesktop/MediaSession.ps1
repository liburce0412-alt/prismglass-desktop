# Windows PowerShell 5.1 only: WinRT projection is built into .NET Framework.
param([Parameter(Mandatory=$true)][int]$OwnerPid,[Parameter(Mandatory=$true)][string]$DataRoot,[int]$MaxSamples=0)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime]
$null=[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties,Windows.Media.Control,ContentType=WindowsRuntime]
$asTask=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'} | Select-Object -First 1
function Await-WinRT($Operation,[Type]$ResultType){
 $task=$asTask.MakeGenericMethod($ResultType).Invoke($null,@($Operation))
 if(!$task.Wait(2500)){throw 'Media request timed out'}
 $task.Result
}
New-Item -ItemType Directory -Path $DataRoot -Force | Out-Null
$DataRoot=(Resolve-Path -LiteralPath $DataRoot).Path
$owner=Get-Process -Id $OwnerPid
$ownerStart=$owner.StartTime.ToUniversalTime().Ticks
$epoch=[datetime]::SpecifyKind([datetime]'1970-01-01',[DateTimeKind]::Utc)
$manager=Await-WinRT ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
$lastSession=$null;$properties=$null;$nextMetadata=[datetime]::MinValue;$previousStatus='';$previousTimeline='';$anchor=0.0;$anchorAt=[datetime]::UtcNow;$count=0
try {
 while($true){
  $owner.Refresh()
  if($owner.HasExited -or $owner.StartTime.ToUniversalTime().Ticks -ne $ownerStart){break}
  $snapshot=@{source='';title='';artist='';album='';status='none';timeline=$false;position=0.0;duration=0.0;rate=1.0;sampledAt=0.0}
  try {
   $session=$manager.GetCurrentSession()
   if($null -ne $session){
    $info=$session.GetPlaybackInfo()
    # Prefer the active playing session; don't retain a paused player over another playing one.
    if($info.PlaybackStatus.ToString() -ne 'Playing'){
     foreach($candidate in $manager.GetSessions()){if($candidate.GetPlaybackInfo().PlaybackStatus.ToString() -eq 'Playing'){$session=$candidate;$info=$session.GetPlaybackInfo();break}}
    }
    $source=$session.SourceAppUserModelId
    if($source -ne $lastSession -or [datetime]::UtcNow -ge $nextMetadata){
     $properties=Await-WinRT ($session.TryGetMediaPropertiesAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties])
     $nextMetadata=[datetime]::UtcNow.AddMilliseconds(700)
    }
    $now=[datetime]::UtcNow
    $timeline=$session.GetTimelineProperties()
    $status=$info.PlaybackStatus.ToString().ToLowerInvariant()
    $rate=1.0;if($null -ne $info.PlaybackRate){$rate=[double]$info.PlaybackRate}
    $duration=($timeline.EndTime-$timeline.StartTime).TotalSeconds
    $position=($timeline.Position-$timeline.StartTime).TotalSeconds
    $updated=$timeline.LastUpdatedTime.UtcDateTime
    $key=$source+"`n"+$properties.Title+"`n"+$properties.Artist+"`n"+$timeline.Position.Ticks+"`n"+$updated.Ticks
    # Position is measured at LastUpdatedTime. Unchanged timeline on pause/resume
    # must carry the previous anchor forward, excluding time spent paused.
    if($key -ne $previousTimeline){
     $anchor=$position
     if($status -eq 'playing' -and $updated.Year -gt 2000){$anchor+=[Math]::Max(0,($now-$updated).TotalSeconds)*$rate}
     $anchorAt=$now;$previousTimeline=$key;$previousStatus=$status;$previousRate=$rate
    }elseif($status -ne $previousStatus -or $rate -ne $previousRate){
     if($previousStatus -eq 'playing'){$anchor+=[Math]::Max(0,($now-$anchorAt).TotalSeconds)*$previousRate}
     $anchorAt=$now;$previousStatus=$status;$previousRate=$rate
    }
    $elapsed=if($status -eq 'playing'){($now-$anchorAt).TotalSeconds*$rate}else{0}
    $snapshot=@{source=$source;title=$properties.Title;artist=$properties.Artist;album=$properties.AlbumTitle;status=$status;timeline=($duration -gt 0 -and $updated.Year -gt 2000);position=[Math]::Max(0,[Math]::Min($duration,$anchor+$elapsed));duration=$duration;rate=$rate;sampledAt=($now-$epoch).TotalSeconds}
    $lastSession=$source
   }else{$lastSession=$null;$properties=$null;$previousTimeline=''}
  }catch{$snapshot.status='unavailable';$lastSession=$null;$properties=$null}
  $snapshot.sampledAt=([datetime]::UtcNow-$epoch).TotalSeconds
  $path=Join-Path $DataRoot 'session.json';$temp=$path+'.tmp'
  [IO.File]::WriteAllText($temp,($snapshot | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
  if([IO.File]::Exists($path)){[IO.File]::Replace($temp,$path,[System.Management.Automation.Language.NullString]::Value)}else{[IO.File]::Move($temp,$path)}
  $count++;if($MaxSamples -gt 0 -and $count -ge $MaxSamples){break}
  Start-Sleep -Milliseconds 400
 }
}finally{$owner.Dispose()}
