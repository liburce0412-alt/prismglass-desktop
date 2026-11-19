param([Parameter(Mandatory=$true)][string]$CacheRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/iteration4-20261111/display-recovery'
$app=Join-Path $out 'app'
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
New-Item -ItemType Directory -Path $app -Force | Out-Null
$process=$null
try {
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')) { Copy-Item -LiteralPath "$sdk/lib/net462/$name" -Destination $app -Force }
 $sources=@('tests/TestDisplayRecovery.cs','src/LiquidDesktop/GlassWindow.cs','src/LiquidDesktop/DesktopVisibilityPolicy.cs','src/LiquidDesktop/DockPlate.cs','src/LiquidDesktop/DockPreview.cs') | ForEach-Object { Join-Path $repo $_ }
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$app/TestDisplayRecovery.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$app/Microsoft.Web.WebView2.Core.dll" "/r:$app/Microsoft.Web.WebView2.WinForms.dll" @sources
 if($LASTEXITCODE -ne 0) { throw 'Display recovery fixture compilation failed' }
 $process=Start-Process -FilePath "$app/TestDisplayRecovery.exe" -ArgumentList ('"'+$out+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(20000)) { throw 'Display recovery fixture timed out' }
 Get-Content -LiteralPath "$out/checks.txt"
 if($process.ExitCode -ne 0) { throw 'Display recovery fixture failed' }
} finally {
 if($process) { if(!$process.HasExited){Stop-Process -Id $process.Id}; $process.Dispose() }
 if(Test-Path -LiteralPath $app) {
  $resolved=(Resolve-Path -LiteralPath $app).Path
  if(!$resolved.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture cleanup escaped its directory'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
 }
}
