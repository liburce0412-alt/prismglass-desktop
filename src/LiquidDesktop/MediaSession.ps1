# Windows PowerShell 5.1 only: WinRT projection is built into .NET Framework.
param([int]$OwnerPid,[string]$DataRoot,[int]$MaxSamples=0,[ValidateSet('','toggle','previous','next')][string]$Control='',[string]$Source64='',[double]$RequestedAt=0)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime]
$null=[Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties,Windows.Media.Control,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.IRandomAccessStreamWithContentType,Windows.Storage.Streams,ContentType=WindowsRuntime]
$asTask=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'} | Select-Object -First 1
function Await-WinRT($Operation,[Type]$ResultType){
 $task=$asTask.MakeGenericMethod($ResultType).Invoke($null,@($Operation))
 if(!$task.Wait(2500)){throw 'Media request timed out'}
 $task.Result
}
if($Control){
 $age=([datetime]::UtcNow-[datetime]'1970-01-01').TotalSeconds-$RequestedAt
 if($Source64.Length -gt 4096 -or $age -lt -1 -or $age -gt 5){throw 'Media control request expired. Please retry.'}
 $source=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Source64))
 $manager=Await-WinRT ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
 $targets=@($manager.GetSessions() | Where-Object {$_.SourceAppUserModelId -ceq $source})
 if($targets.Count -ne 1){throw 'The requested player session changed. Please retry.'}
 $target=$targets[0];$capabilities=$target.GetPlaybackInfo().Controls
 $operation=switch($Control){
  'toggle' {if(!$capabilities.IsPlayPauseToggleEnabled){throw 'Player does not support play/pause.'};$target.TryTogglePlayPauseAsync()}
  'previous' {if(!$capabilities.IsPreviousEnabled){throw 'Player does not support previous track.'};$target.TrySkipPreviousAsync()}
  'next' {if(!$capabilities.IsNextEnabled){throw 'Player does not support next track.'};$target.TrySkipNextAsync()}
 }
 if(!(Await-WinRT $operation ([bool]))){throw 'Player did not accept the control request.'}
 return
}
if($OwnerPid -le 0 -or [string]::IsNullOrWhiteSpace($DataRoot)){throw 'Media sampling requires an owner process and data directory.'}
Add-Type -Path (Join-Path $PSScriptRoot 'MediaCoverReader.cs')
function Save-MediaCover($Thumbnail){
 if($null -eq $Thumbnail){return ''}
 $random=$null;$target=$null;$pending=$null
 try{
  $random=Await-WinRT ($Thumbnail.OpenReadAsync()) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType])
  $bytes=[MediaCoverReader]::Read($random,2097152);if(!$bytes.Length){return ''}
  $name='session-cover-'+[guid]::NewGuid().ToString('N').Substring(0,16)+'.bin';$target=Join-Path $DataRoot $name;$pending=$target+'.pending'
  [IO.File]::WriteAllBytes($pending,$bytes)
  [IO.File]::Move($pending,$target);$pending=$null
  # This cache owns only generated session-cover names, never the player's artwork.
  Get-ChildItem -LiteralPath $DataRoot -Filter 'session-cover-*.bin' -File | Where-Object {$_.Name -ne $name -and $_.Name -match '^session-cover-[a-f0-9]{16}\.bin$'} | Sort-Object LastWriteTimeUtc -Descending | Select-Object -Skip 7 | ForEach-Object {Remove-Item -LiteralPath $_.FullName -Force}
  return $name
 }catch{return ''}finally{if($random -and [Runtime.InteropServices.Marshal]::IsComObject($random)){try{[void][Runtime.InteropServices.Marshal]::ReleaseComObject($random)}catch{}};if($pending -and [IO.File]::Exists($pending)){[IO.File]::Delete($pending)}}
}
New-Item -ItemType Directory -Path $DataRoot -Force | Out-Null
$DataRoot=(Resolve-Path -LiteralPath $DataRoot).Path
$owner=Get-Process -Id $OwnerPid
$ownerStart=$owner.StartTime.ToUniversalTime().Ticks
$epoch=[datetime]::SpecifyKind([datetime]'1970-01-01',[DateTimeKind]::Utc)
$manager=Await-WinRT ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
$lastSession=$null;$properties=$null;$nextMetadata=[datetime]::MinValue;$previousStatus='';$previousTimeline='';$anchor=0.0;$anchorAt=[datetime]::UtcNow;$count=0
$coverTrack='';$coverName='';$nextCover=[datetime]::MinValue
try {
 while($true){
  $owner.Refresh()
  if($owner.HasExited -or $owner.StartTime.ToUniversalTime().Ticks -ne $ownerStart){break}
  $snapshot=@{source='';title='';artist='';album='';status='none';cover='';timeline=$false;position=0.0;duration=0.0;rate=1.0;sampledAt=0.0}
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
     $nextCoverTrack=$source+"`n"+$properties.Title+"`n"+$properties.Artist+"`n"+$properties.AlbumTitle
     if($nextCoverTrack -ne $coverTrack -or (!$coverName -and [datetime]::UtcNow -ge $nextCover)){$coverTrack=$nextCoverTrack;$coverName='';$coverName=Save-MediaCover $properties.Thumbnail;$nextCover=[datetime]::UtcNow.AddSeconds(5)}
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
    $snapshot=@{source=$source;title=$properties.Title;artist=$properties.Artist;album=$properties.AlbumTitle;status=$status;cover=$coverName;smtc=$true;timeline=($duration -gt 0 -and $updated.Year -gt 2000);position=[Math]::Max(0,[Math]::Min($duration,$anchor+$elapsed));duration=$duration;rate=$rate;sampledAt=($now-$epoch).TotalSeconds}
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
