param([Parameter(Mandatory=$true)][string]$CacheRoot,[string]$CaptureRoot='artifacts/release-1.0.0/companion-ui')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/companion-ui-runtime'
$app=Join-Path $out 'app'
$captures=[IO.Path]::GetFullPath((Join-Path $repo $CaptureRoot))
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
New-Item -ItemType Directory -Force -Path $app,$captures | Out-Null
try{
 Copy-Item "$repo/src/LiquidDesktop/companion-web/*" $app -Force
 Copy-Item "$repo/src/LiquidDesktop/lyrics-style.js","$repo/src/LiquidDesktop/dock-lyrics.css","$repo/tests/CompanionUiFixture.js" $app -Force
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item "$sdk/lib/net462/$name" $out -Force}
 Copy-Item "$sdk/runtimes/win-x64/native/WebView2Loader.dll" $out -Force
 Set-Content -LiteralPath "$captures/ui-checks.txt" -Value '' -Encoding utf8
 Set-Content -LiteralPath "$captures/layout-metrics.jsonl" -Value '' -Encoding utf8
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 "/out:$out/TestCompanionUi.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$out/Microsoft.Web.WebView2.Core.dll" "/r:$out/Microsoft.Web.WebView2.WinForms.dll" (Join-Path $repo 'tests/TestCompanionUi.cs')
 if($LASTEXITCODE -ne 0){throw 'Companion UI test compilation failed'}
 $process=Start-Process -FilePath "$out/TestCompanionUi.exe" -ArgumentList ('"'+$app+'" "'+$captures+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(60000)){throw 'Companion UI checks timed out'}
 if($process.ExitCode -ne 0){throw "Companion UI checks failed; inspect $captures/ui-checks.txt"}
 Get-Content -LiteralPath "$captures/ui-checks.txt"
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if(Test-Path -LiteralPath $out){
  $resolved=(Resolve-Path -LiteralPath $out).Path
  $boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
  if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Companion test cleanup escaped artifacts'}
  for($attempt=0;$attempt -lt 40;$attempt++){
   try{Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop;break}catch{if($attempt -eq 39){throw};Start-Sleep -Milliseconds 250}
  }
  if(Test-Path -LiteralPath $resolved){throw 'Companion test cleanup failed'}
 }
}
