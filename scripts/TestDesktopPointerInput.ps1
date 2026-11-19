$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$sources=@('tests/TestDesktopPointerInput.cs','src/LiquidDesktop/DesktopPointerInput.cs')|ForEach-Object {Join-Path $repo $_}
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$out/TestDesktopPointerInput.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll @sources
if($LASTEXITCODE -ne 0){throw 'Pointer test compilation failed'}
& (Join-Path $out 'TestDesktopPointerInput.exe') | Tee-Object -FilePath (Join-Path $out 'desktop-pointer-checks.txt')
if($LASTEXITCODE -ne 0){throw 'Pointer checks failed'}
