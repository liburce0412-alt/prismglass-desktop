param([Parameter(Mandatory=$true)][string]$CacheRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/webview-visibility'
$profile=Join-Path $out ('runtime-'+[guid]::NewGuid().ToString('N'))
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$log=Join-Path $out 'checks.txt'
$process=$null
New-Item -ItemType Directory -Path $out -Force | Out-Null
try{
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item "$sdk/lib/net462/$name" $out -Force}
 Copy-Item "$sdk/runtimes/win-x64/native/WebView2Loader.dll" $out -Force
 Set-Content -LiteralPath $log -Value '' -Encoding utf8
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 "/out:$out/TestWebViewVisibility.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$out/Microsoft.Web.WebView2.Core.dll" "/r:$out/Microsoft.Web.WebView2.WinForms.dll" (Join-Path $repo 'tests/TestWebViewVisibility.cs') (Join-Path $repo 'src/LiquidDesktop/WebViewVisibility.cs') (Join-Path $repo 'src/LiquidDesktop/WebViewPixelScale.cs')
 if($LASTEXITCODE -ne 0){throw 'Visibility test compilation failed'}
 $process=Start-Process -FilePath "$out/TestWebViewVisibility.exe" -ArgumentList ('"'+$profile+'" "'+$log+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(30000)){throw 'Visibility checks timed out'}
 Get-Content -LiteralPath $log
 if($process.ExitCode -ne 0){throw 'Visibility checks failed'}
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if(Test-Path -LiteralPath $profile){
  $resolved=(Resolve-Path -LiteralPath $profile).Path
  if(!$resolved.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Profile escaped fixture directory'}
  for($attempt=0;$attempt -lt 40;$attempt++){
   try{Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop;break}catch{if($attempt -eq 39){throw};Start-Sleep -Milliseconds 250}
  }
  if(Test-Path -LiteralPath $resolved){throw 'Profile cleanup failed'}
 }
 foreach($name in @('TestWebViewVisibility.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){Remove-Item -LiteralPath (Join-Path $out $name) -Force -ErrorAction SilentlyContinue;if(Test-Path -LiteralPath (Join-Path $out $name)){throw "Fixture cleanup failed: $name"}}
}
