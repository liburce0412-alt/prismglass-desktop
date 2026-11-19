param([string]$CaptureRoot='artifacts/next-experience-20261109/glass')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=[IO.Path]::GetFullPath((Join-Path $repo $CaptureRoot))
$app=Join-Path $out 'gpu-check-runtime'
New-Item -ItemType Directory -Path $app -Force | Out-Null
try {
 $sources=@('tests/TestGpuDownsample.cs','src/LiquidDesktop/WindowCapture.cs','src/LiquidDesktop/SharedWindowCapture.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$app/TestGpuDownsample.exe" /r:System.Runtime.dll /r:System.Runtime.WindowsRuntime.dll /r:System.Runtime.InteropServices.WindowsRuntime.dll /r:System.Drawing.dll "/r:$env:WINDIR/System32/WinMetadata/Windows.Foundation.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Graphics.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Storage.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Security.winmd" @sources
 if($LASTEXITCODE -ne 0){throw 'GPU downsample test compilation failed'}
 & "$app/TestGpuDownsample.exe" | Tee-Object -FilePath (Join-Path $out 'gpu-downsample-checks.txt')
 if($LASTEXITCODE -ne 0){throw 'GPU downsample checks failed'}
}finally {
 if(Test-Path -LiteralPath $app){
  $resolved=(Resolve-Path -LiteralPath $app).Path
  if(!$resolved.StartsWith($out+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'GPU test cleanup escaped artifact directory'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
  if(Test-Path -LiteralPath $resolved){throw 'GPU test cleanup did not finish'}
 }
}
