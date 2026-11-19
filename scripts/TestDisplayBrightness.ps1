$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$sources=@('tests/TestDisplayBrightness.cs','src/LiquidDesktop/DisplayBrightness.cs','src/LiquidDesktop/MonitorCatalog.cs')|ForEach-Object {Join-Path $repo $_}
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$out/TestDisplayBrightness.exe" /r:System.Management.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll @sources
if($LASTEXITCODE -ne 0){throw 'Brightness tests did not compile'}
& (Join-Path $out 'TestDisplayBrightness.exe') | Tee-Object -FilePath (Join-Path $out 'brightness-checks.txt')
if($LASTEXITCODE -ne 0){throw 'Brightness checks failed'}
