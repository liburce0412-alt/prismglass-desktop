param([Parameter(Mandatory=$true)][string]$CacheRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$stage=Join-Path $repo 'artifacts/lyrics-showcase'
$app=Join-Path $stage 'app'
$captures=Join-Path $stage 'capture'
$media=Join-Path $repo 'docs/media'
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
$ffmpeg=(Get-Command ffmpeg -ErrorAction Stop).Source
New-Item -ItemType Directory -Force -Path $app,$captures | Out-Null
try{
 Copy-Item "$repo/src/LiquidDesktop/*" $app -Force
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item "$sdk/lib/net462/$name" $stage -Force}
 Copy-Item "$sdk/runtimes/win-x64/native/WebView2Loader.dll" $stage -Force
 $csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 & $csc /nologo /target:exe /platform:x64 "/out:$stage/CaptureLyricsShowcase.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$stage/Microsoft.Web.WebView2.Core.dll" "/r:$stage/Microsoft.Web.WebView2.WinForms.dll" (Join-Path $repo 'tools/CaptureLyricsShowcase.cs')
 if($LASTEXITCODE -ne 0){throw 'Showcase compilation failed'}
 $process=Start-Process -FilePath "$stage/CaptureLyricsShowcase.exe" -ArgumentList ('"'+$app+'" "'+$captures+'"') -WindowStyle Hidden -RedirectStandardOutput "$stage/capture.log" -RedirectStandardError "$stage/capture-error.log" -PassThru
 if(!$process.WaitForExit(55000)){throw 'Showcase capture timed out'}
 if($process.ExitCode -ne 0){Get-Content "$stage/capture.log";throw 'Showcase capture failed'}
 & $ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i "$captures/frames/frames.txt" -vf 'fps=30,format=yuv420p' -c:v libx264 -crf 19 -preset medium -movflags +faststart -an "$media/dock-lyrics.mp4"
 if($LASTEXITCODE -ne 0){throw 'MP4 encoding failed'}
 & $ffmpeg -hide_banner -loglevel error -y -i "$media/dock-lyrics.mp4" -filter_complex '[0:v]fps=16,scale=960:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a:diff_mode=rectangle' -loop 0 "$media/dock-lyrics.gif"
 if($LASTEXITCODE -ne 0){throw 'GIF encoding failed'}
 Get-ChildItem -LiteralPath $captures -Filter '*.png' | Copy-Item -Destination $media -Force
 Get-Content "$stage/capture.log"
 Get-ChildItem "$media/dock-lyrics.*","$media/lyrics-*.png" | Select-Object Name,Length
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 # All files in this dedicated directory are generated and disposable.
 if(Test-Path -LiteralPath $stage){
  $resolved=(Resolve-Path -LiteralPath $stage).Path
  $boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
  if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Showcase cleanup escaped artifacts'}
  if((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Unexpected showcase reparse point'}
  if(Get-ChildItem -LiteralPath $resolved -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}){throw 'Unexpected nested showcase reparse point'}
  for($attempt=0;$attempt -lt 40;$attempt++){
   try{Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop;break}catch{if($attempt -eq 39){throw};Start-Sleep -Milliseconds 250}
  }
  if(Test-Path -LiteralPath $resolved){throw 'Showcase cleanup failed'}
 }
}
