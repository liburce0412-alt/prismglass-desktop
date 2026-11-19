param([string]$CacheRoot='D:/Cache/PrismGlass',[string]$CaptureRoot='artifacts/iteration2-20261110/icons-dock',[string]$DockLayerTrace)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$runtime=Join-Path $repo 'artifacts/functional-icons-runtime'
$capture=[IO.Path]::GetFullPath((Join-Path $repo $CaptureRoot))
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
New-Item -ItemType Directory -Force -Path $runtime,$capture | Out-Null
try{
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath "$sdk/lib/net462/$name" -Destination $runtime -Force}
 Copy-Item -LiteralPath "$sdk/runtimes/win-x64/native/WebView2Loader.dll" -Destination $runtime -Force
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 "/out:$runtime/TestFunctionalIcons.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$runtime/Microsoft.Web.WebView2.Core.dll" "/r:$runtime/Microsoft.Web.WebView2.WinForms.dll" (Join-Path $repo 'tests/TestFunctionalIcons.cs')
 if($LASTEXITCODE -ne 0){throw 'Functional icon test compilation failed'}
 $arguments=@(('"'+(Join-Path $repo 'src/LiquidDesktop')+'"'),('"'+$capture+'"'))
 if($DockLayerTrace){
  Copy-Item -LiteralPath (Join-Path $repo 'tests/DockIconLayer.html'),(Join-Path $repo 'assets/Brand/prismglass-icon-refined-256.png') -Destination $runtime
  $arguments+=('"'+(Resolve-Path -LiteralPath $DockLayerTrace).ProviderPath+'"')
 }
 $process=Start-Process -FilePath "$runtime/TestFunctionalIcons.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(45000)){throw 'Functional icon UI checks timed out'}
 Get-Content -LiteralPath (Join-Path $capture 'checks.txt')
 if($process.ExitCode -ne 0){throw 'Functional icon UI checks failed'}
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 foreach($path in @($runtime,(Join-Path $capture 'profile'))){
  if(!(Test-Path -LiteralPath $path)){continue}
  $resolved=(Resolve-Path -LiteralPath $path).ProviderPath
  if(!$resolved.StartsWith(([IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture cleanup escaped artifacts'}
  for($attempt=0;$attempt -lt 20;$attempt++){try{Remove-Item -LiteralPath $resolved -Recurse -Force;break}catch{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 250}}
  if(Test-Path -LiteralPath $resolved){throw 'Fixture cleanup failed'}
 }
}
