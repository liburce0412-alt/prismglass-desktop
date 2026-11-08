param([string]$CacheRoot='D:/Cache/PrismGlass',[switch]$PreviewOnly,[switch]$StageInteractionOnly,[switch]$StageLayoutOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo $(if($StageLayoutOnly){'artifacts/stage-layout'}elseif($StageInteractionOnly){'artifacts/stage-interaction'}else{'artifacts/native-desktop-effects'})
$app=Join-Path $out 'app'
$captures=Join-Path $out 'captures'
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
New-Item -ItemType Directory -Path $app,$captures -Force | Out-Null
try {
 foreach($frame in Get-ChildItem -LiteralPath $captures -Filter 'genie-frame-*.png' -File){if(!$frame.FullName.StartsWith([IO.Path]::GetFullPath($captures)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Recording escaped capture directory'};Remove-Item -LiteralPath $frame.FullName -Force}
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath "$sdk/lib/net462/$name" -Destination $app -Force}
 Copy-Item -LiteralPath "$sdk/runtimes/win-x64/native/WebView2Loader.dll" -Destination $app -Force
 foreach($name in @('glass-renderer.js','native-glass.html')){Copy-Item -LiteralPath (Join-Path $repo "src/LiquidDesktop/$name") -Destination $app -Force}
 $sources=@('tests/TestNativeDesktopEffects.cs','src/LiquidDesktop/NativeWindowMotion.cs','src/LiquidDesktop/StageManager.cs','src/LiquidDesktop/StageStrip.cs','src/LiquidDesktop/ComponentPosition.cs','src/LiquidDesktop/DesktopVisibilityPolicy.cs','src/LiquidDesktop/LiveGlassBackdrop.cs','src/LiquidDesktop/OpticalBackdrop.cs','src/LiquidDesktop/SharedWindowCapture.cs','src/LiquidDesktop/WindowCapture.cs','src/LiquidDesktop/NativeGlassMaterial.cs','src/LiquidDesktop/GlassWindow.cs','src/LiquidDesktop/WebViewVisibility.cs','src/LiquidDesktop/WebViewPixelScale.cs','src/LiquidDesktop/DockBackend.cs','src/LiquidDesktop/DockPreview.cs','src/LiquidDesktop/DesktopPreferences.cs','src/LiquidDesktop/RuntimePolicy.cs','src/LiquidDesktop/MonitorCatalog.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$app/TestNativeDesktopEffects.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Numerics.dll /r:System.Numerics.Vectors.dll /r:System.Runtime.dll /r:System.Runtime.WindowsRuntime.dll /r:System.Runtime.InteropServices.WindowsRuntime.dll "/r:$env:WINDIR/System32/WinMetadata/Windows.Foundation.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Graphics.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Storage.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Security.winmd" /r:System.Xaml.dll "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/WindowsFormsIntegration.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/WindowsBase.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/PresentationCore.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/PresentationFramework.dll" /r:System.Web.Extensions.dll "/r:$app/Microsoft.Web.WebView2.Core.dll" "/r:$app/Microsoft.Web.WebView2.WinForms.dll" @sources
 if($LASTEXITCODE -ne 0){throw 'Native effects test compilation failed'}
 $arguments='"'+$app+'" "'+$captures+'"';if($StageLayoutOnly){$arguments+=' --stage-layout-only'}elseif($StageInteractionOnly){$arguments+=' --stage-interaction-only'}elseif($PreviewOnly){$arguments+=' --preview-only'}
 $process=Start-Process -FilePath "$app/TestNativeDesktopEffects.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(60000)){throw 'Native effects short checks timed out'}
 Get-Content -LiteralPath "$captures/checks.txt"
 if($process.ExitCode -ne 0){throw 'Native effects checks failed'}
} finally {
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 $ownedRoot=[IO.Path]::GetFullPath($app)+[IO.Path]::DirectorySeparatorChar
 # This focused mode creates no WebViews; its two source processes are disposed by the harness.
 if(!$StageInteractionOnly -and !$StageLayoutOnly){Get-CimInstance Win32_Process -Filter "Name='TestNativeDesktopEffects.exe' OR Name='msedgewebview2.exe'" | Where-Object {($_.ExecutablePath -and $_.ExecutablePath.StartsWith($ownedRoot,[StringComparison]::OrdinalIgnoreCase)) -or ($_.CommandLine -and $_.CommandLine.Contains($ownedRoot))} | ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue}}
 if(Test-Path -LiteralPath $app){$path=(Resolve-Path -LiteralPath $app).Path;if(!$path.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup escaped test artifacts'};for($attempt=0;$attempt -lt 30;$attempt++){try{Remove-Item -LiteralPath $path -Recurse -Force;break}catch{if($attempt -eq 29){throw};Start-Sleep -Milliseconds 200}};if(Test-Path -LiteralPath $path){throw 'Cleanup failed'}}
}
