param([Parameter(Mandatory=$true)][string]$CacheRoot,[switch]$BodyOnly,[switch]$DockOnly,[switch]$MultiHostOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo $(if($MultiHostOnly){'artifacts/live-glass-multi-host'}elseif($DockOnly){'artifacts/live-glass-dock'}else{'artifacts/live-glass-ui'})
$app=Join-Path $out 'app'
$captures=Join-Path $out 'captures'
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
New-Item -ItemType Directory -Path $app,$captures -Force | Out-Null
try{
 foreach($name in @('glass-materials.js','glass-renderer.js','native-glass.html')){Copy-Item -LiteralPath (Join-Path $repo "src/LiquidDesktop/$name") -Destination $app -Force}
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath "$sdk/lib/net462/$name" -Destination $app -Force}
 Copy-Item -LiteralPath "$sdk/runtimes/win-x64/native/WebView2Loader.dll" -Destination $app -Force
 Set-Content -LiteralPath (Join-Path $captures 'checks.txt') -Value '' -Encoding utf8
 $sources=@('tests/TestLiveGlassBackdrop.cs','tests/TestLiveGlassMultiHost.cs','src/LiquidDesktop/SharedWindowCapture.cs','src/LiquidDesktop/LiveGlassBackdrop.cs','src/LiquidDesktop/OpticalBackdrop.cs','src/LiquidDesktop/WindowCapture.cs','src/LiquidDesktop/GlassWindow.cs','src/LiquidDesktop/DesktopVisibilityPolicy.cs','src/LiquidDesktop/NativeGlassMaterial.cs','src/LiquidDesktop/MonitorWallpaper.cs','src/LiquidDesktop/WebViewPixelScale.cs','src/LiquidDesktop/WebViewVisibility.cs','src/LiquidDesktop/DockBackend.cs','src/LiquidDesktop/IslandEvents.cs','src/LiquidDesktop/DockPreview.cs','src/LiquidDesktop/DesktopPreferences.cs','src/LiquidDesktop/MonitorCatalog.cs','src/LiquidDesktop/RuntimePolicy.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$app/TestLiveGlassBackdrop.exe" /r:System.Numerics.dll /r:System.Numerics.Vectors.dll /r:System.Runtime.dll /r:System.Runtime.WindowsRuntime.dll /r:System.Runtime.InteropServices.WindowsRuntime.dll "/r:$env:WINDIR/System32/WinMetadata/Windows.Foundation.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Graphics.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Storage.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Security.winmd" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$app/Microsoft.Web.WebView2.Core.dll" "/r:$app/Microsoft.Web.WebView2.WinForms.dll" @sources
 if($LASTEXITCODE -ne 0){throw 'Live background test compilation failed'}
 $process=Start-Process -FilePath "$app/TestLiveGlassBackdrop.exe" -ArgumentList ('"'+$app+'" "'+$captures+'"'+$(if($MultiHostOnly){' --multi-host-only'}elseif($DockOnly){' --dock-only'}elseif($BodyOnly){' --body-only'}else{''})) -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(45000)){throw 'Live background checks timed out'}
 if($process.ExitCode -ne 0){throw 'Live background checks failed; inspect captures/checks.txt'}
 Get-Content -LiteralPath (Join-Path $captures 'checks.txt')
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if($MultiHostOnly){
  $ownedExe=[IO.Path]::GetFullPath((Join-Path $app 'TestLiveGlassBackdrop.exe'))
  Get-CimInstance Win32_Process -Filter "Name='TestLiveGlassBackdrop.exe'" | Where-Object {$_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath) -eq $ownedExe} | ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue}
 }
 if(Test-Path -LiteralPath $app){
  $resolved=(Resolve-Path -LiteralPath $app).Path
  if(!$resolved.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Test cleanup escaped its artifact directory'}
  for($attempt=0;$attempt -lt 40;$attempt++){try{Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop;break}catch{if($attempt -eq 39){throw};Start-Sleep -Milliseconds 250}}
  if(Test-Path -LiteralPath $resolved){throw 'Test cleanup did not finish'}
 }
}
