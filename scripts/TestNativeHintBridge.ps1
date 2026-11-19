param([string]$CacheRoot='D:/Cache/PrismGlass',[string]$CaptureRoot='artifacts/iteration4-20261111/icons/native-bridge')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$runtime=Join-Path $repo ('artifacts/native-hint-ui-'+[guid]::NewGuid().ToString('N'))
$capture=[IO.Path]::GetFullPath((Join-Path $repo $CaptureRoot))
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
New-Item -ItemType Directory -Force -Path $runtime,$capture | Out-Null
try{
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath "$sdk/lib/net462/$name" -Destination $runtime}
 Copy-Item -LiteralPath "$sdk/runtimes/win-x64/native/WebView2Loader.dll" -Destination $runtime
 Copy-Item -LiteralPath (Join-Path $repo 'src/LiquidDesktop/prism-hint.js'),(Join-Path $repo 'src/LiquidDesktop/prism-hint.css') -Destination $runtime
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 "/out:$runtime/TestNativeHintBridge.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$runtime/Microsoft.Web.WebView2.Core.dll" "/r:$runtime/Microsoft.Web.WebView2.WinForms.dll" (Join-Path $repo 'src/LiquidDesktop/GlassWindow.cs') (Join-Path $repo 'src/LiquidDesktop/DesktopVisibilityPolicy.cs') (Join-Path $repo 'src/LiquidDesktop/NativeHintHost.cs') (Join-Path $repo 'tests/TestNativeHintBridge.cs')
 if($LASTEXITCODE -ne 0){throw 'Native hint bridge compilation failed'}
 $process=Start-Process -FilePath "$runtime/TestNativeHintBridge.exe" -ArgumentList @(('"'+$runtime+'"'),('"'+$capture+'"')) -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(20000)){throw 'Native hint bridge checks timed out'}
 Get-Content -LiteralPath (Join-Path $capture 'checks.txt')
 if($process.ExitCode -ne 0){throw 'Native hint bridge checks failed'}
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 $resolved=(Resolve-Path -LiteralPath $runtime).ProviderPath
 if(!$resolved.StartsWith(([IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)){throw 'Native hint cleanup escaped artifacts'}
 for($attempt=0;$attempt -lt 20;$attempt++){try{Remove-Item -LiteralPath $resolved -Recurse -Force;break}catch{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 200}}
 if(Test-Path -LiteralPath $resolved){throw 'Native hint fixture cleanup failed'}
}
