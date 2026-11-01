param([Parameter(Mandatory=$true)][string]$BuildRoot,[Parameter(Mandatory=$true)][string]$FFmpeg,[Parameter(Mandatory=$true)][string]$MediaMtx,[switch]$AvSync)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo 'artifacts/companion-media-runtime'
$log=Join-Path $repo 'artifacts/experience-polish-20261031/audio/webrtc-checks.txt'
if($AvSync){$log=Join-Path $repo 'artifacts/experience-polish-20261031/audio/av-sync-checks.txt'}
$process=$null
if(Test-Path -LiteralPath $fixture){throw 'Media fixture already exists; check for an active probe before removing it.'}
if(Get-NetTCPConnection -LocalPort 31854,31889 -ErrorAction SilentlyContinue){throw 'Streaming ports are in use; stop the current stream before this opt-in probe.'}
try{
 New-Item -ItemType Directory -Path (Join-Path $fixture 'runtime-data/companion'),(Split-Path $log) -Force | Out-Null
 $app=Join-Path ([IO.Path]::GetFullPath($BuildRoot)) 'LiquidDesktop'
 foreach($name in @('PrismAudio.exe','NAudio.Core.dll','NAudio.Wasapi.dll','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){Copy-Item -LiteralPath (Join-Path $app $name) -Destination $fixture}
 if($AvSync){
  & $FFmpeg -hide_banner -loglevel error -f lavfi -i 'color=c=black:s=320x180:r=30:d=14' -f lavfi -i 'aevalsrc=0.03*sin(2*PI*997*t)*between(mod(t\,2)\,0.5\,0.7):s=48000:d=14' -vf "drawbox=color=white:t=fill:enable='between(mod(t,2),0.5,0.7)'" -c:v libx264 -preset ultrafast -pix_fmt yuv420p -c:a aac -movflags +faststart (Join-Path $fixture 'sync.mp4')
  if($LASTEXITCODE -ne 0){throw 'Synthetic flash and tone clip generation failed'}
 }
 @{enabled=$true;address='127.0.0.1';ffmpeg=[IO.Path]::GetFullPath($FFmpeg);mediamtx=[IO.Path]::GetFullPath($MediaMtx)} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'runtime-data/companion/screen-stream.json') -Encoding utf8
 $sources=@('tests/TestCompanionMedia.cs','src/LiquidDesktop/CompanionStream.cs','src/LiquidDesktop/CompanionVirtualDisplay.cs','src/LiquidDesktop/DesktopIcons.cs','src/LiquidDesktop/CompanionServer.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /debug /nowarn:0649 /platform:x64 "/out:$fixture/TestCompanionMedia.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll /r:System.Web.Extensions.dll "/r:$fixture/Microsoft.Web.WebView2.Core.dll" "/r:$fixture/Microsoft.Web.WebView2.WinForms.dll" @sources
 if($LASTEXITCODE -ne 0){throw 'Media probe compilation failed'}
 $arguments='"'+$fixture+'" "'+$log+'"';if($AvSync){$arguments+=' --av-sync'}
 $process=Start-Process -FilePath (Join-Path $fixture 'TestCompanionMedia.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(180000)){throw 'Media probe timed out'}
 if($process.ExitCode -ne 0){throw "Media probe failed: $log"}
 Get-Content -LiteralPath $log
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 $expected=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/companion-media-runtime'))
 if([IO.Path]::GetFullPath($fixture) -ne $expected){throw 'Unexpected fixture cleanup path'}
 if(Test-Path -LiteralPath $fixture){Start-Sleep -Milliseconds 500;Remove-Item -LiteralPath $fixture -Recurse -Force}
 if(Test-Path -LiteralPath $fixture){throw 'Media fixture cleanup incomplete'}
}
