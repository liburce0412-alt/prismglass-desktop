param([string]$CacheRoot='D:/Cache/PrismGlass')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/iteration6-20261112/stage-thumbnails/integration'
$app=Join-Path $out 'app'
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
New-Item -ItemType Directory -Path $app,$out -Force | Out-Null
try {
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath "$sdk/lib/net462/$name" -Destination $app -Force}
 Copy-Item -LiteralPath "$sdk/runtimes/win-x64/native/WebView2Loader.dll" -Destination $app -Force
 $sources=@('tests/TestStageThumbnails.cs','src/LiquidDesktop/NativeWindowMotion.cs','src/LiquidDesktop/StageManager.cs','src/LiquidDesktop/StageThumbnailCapture.cs','src/LiquidDesktop/StageStrip.cs','src/LiquidDesktop/ComponentPosition.cs','src/LiquidDesktop/DesktopVisibilityPolicy.cs','src/LiquidDesktop/LiveGlassBackdrop.cs','src/LiquidDesktop/OpticalBackdrop.cs','src/LiquidDesktop/SharedWindowCapture.cs','src/LiquidDesktop/WindowCapture.cs','src/LiquidDesktop/NativeGlassMaterial.cs','src/LiquidDesktop/MonitorWallpaper.cs','src/LiquidDesktop/GlassWindow.cs','src/LiquidDesktop/WebViewVisibility.cs','src/LiquidDesktop/WebViewPixelScale.cs','src/LiquidDesktop/DockBackend.cs','src/LiquidDesktop/IslandEvents.cs','src/LiquidDesktop/DockPreview.cs','src/LiquidDesktop/DesktopPreferences.cs','src/LiquidDesktop/RuntimePolicy.cs','src/LiquidDesktop/MonitorCatalog.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$app/TestStageThumbnails.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Numerics.dll /r:System.Numerics.Vectors.dll /r:System.Runtime.dll /r:System.Runtime.WindowsRuntime.dll /r:System.Runtime.InteropServices.WindowsRuntime.dll "/r:$env:WINDIR/System32/WinMetadata/Windows.Foundation.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Graphics.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Storage.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Security.winmd" /r:System.Xaml.dll "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/WindowsFormsIntegration.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/WindowsBase.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/PresentationCore.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/PresentationFramework.dll" /r:System.Web.Extensions.dll "/r:$app/Microsoft.Web.WebView2.Core.dll" "/r:$app/Microsoft.Web.WebView2.WinForms.dll" @sources
 if($LASTEXITCODE -ne 0){throw 'Stage thumbnail integration compilation failed'}
 $process=Start-Process -FilePath "$app/TestStageThumbnails.exe" -ArgumentList ('"'+$app+'" "'+$out+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(18000)){throw 'Stage thumbnail integration timed out'}
 Get-Content -LiteralPath "$out/checks.txt"
 if($process.ExitCode -ne 0){throw 'Stage thumbnail integration failed'}
} finally {
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 $owned=[IO.Path]::GetFullPath($app)+[IO.Path]::DirectorySeparatorChar
 Get-CimInstance Win32_Process -Filter "Name='TestStageThumbnails.exe'" | Where-Object {$_.ExecutablePath -and $_.ExecutablePath.StartsWith($owned,[StringComparison]::OrdinalIgnoreCase)} | ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue}
 if(Test-Path -LiteralPath $app){$resolved=(Resolve-Path -LiteralPath $app).Path;if(!$resolved.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Thumbnail integration cleanup escaped artifacts'};Remove-Item -LiteralPath $resolved -Recurse -Force;if(Test-Path -LiteralPath $resolved){throw 'Thumbnail integration cleanup failed'}}
}
