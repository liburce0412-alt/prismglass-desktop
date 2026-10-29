param([Parameter(Mandatory=$true)][string]$CacheRoot,[string]$CaptureRoot='artifacts/release-1.0.0/weather-ui',[switch]$CaptureOnly,[string]$WeatherFixture='')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/weather-ui-runtime'
$app=Join-Path $out 'app'
$companion=Join-Path $app 'companion-web'
$captures=[IO.Path]::GetFullPath((Join-Path $repo $CaptureRoot))
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
New-Item -ItemType Directory -Force -Path $app,$companion,$captures | Out-Null
try{
 Get-ChildItem -LiteralPath "$repo/src/LiquidDesktop" -File | Where-Object {$_.Extension -in '.js','.html','.css'} | Copy-Item -Destination $app
 Copy-Item "$repo/src/LiquidDesktop/companion-web/*" $companion -Force
 Copy-Item "$repo/src/LiquidDesktop/lyrics-style.js","$repo/src/LiquidDesktop/dock-lyrics.css","$repo/tests/CompanionUiFixture.js" $companion -Force
 if($WeatherFixture){Copy-Item -LiteralPath $WeatherFixture -Destination (Join-Path $app 'weather-source-evidence.json')}
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item "$sdk/lib/net462/$name" $out -Force}
 Copy-Item "$sdk/runtimes/win-x64/native/WebView2Loader.dll" $out -Force
 Set-Content -LiteralPath "$captures/ui-checks.txt" -Value '' -Encoding utf8
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 "/out:$out/TestWeatherUi.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$out/Microsoft.Web.WebView2.Core.dll" "/r:$out/Microsoft.Web.WebView2.WinForms.dll" (Join-Path $repo 'tests/TestWeatherUi.cs')
 if($LASTEXITCODE -ne 0){throw 'Weather UI test compilation failed'}
 $process=Start-Process -FilePath "$out/TestWeatherUi.exe" -ArgumentList ('"'+$app+'" "'+$captures+'" '+$CaptureOnly.IsPresent) -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(60000)){throw 'Weather UI checks timed out'}
 if($process.ExitCode -ne 0){throw "Weather UI checks failed; inspect $captures/ui-checks.txt"}
 Get-Content -LiteralPath "$captures/ui-checks.txt"
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if(Test-Path -LiteralPath $out){
  $resolved=(Resolve-Path -LiteralPath $out).Path
  $boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
  if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Weather test cleanup escaped artifacts'}
  for($attempt=0;$attempt -lt 40;$attempt++){
   try{Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop;break}catch{if($attempt -eq 39){throw};Start-Sleep -Milliseconds 250}
  }
  if(Test-Path -LiteralPath $resolved){throw 'Weather test cleanup failed'}
 }
}
