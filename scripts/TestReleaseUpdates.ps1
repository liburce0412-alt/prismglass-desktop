$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests/release-updates'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$program=Join-Path $out 'TestReleaseUpdates.exe'
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo ("/out:$program") /r:System.Net.Http.dll /r:System.Web.Extensions.dll (Join-Path $repo 'src/LiquidDesktop/ReleaseUpdates.cs') (Join-Path $repo 'tests/TestReleaseUpdates.cs')
if($LASTEXITCODE -ne 0){throw 'Release update test compilation failed'}
& $program
if($LASTEXITCODE -ne 0){throw 'Release update tests failed'}
