$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$executable=Join-Path $out 'TestCompanionServer.exe'
& $csc /nologo ('/out:'+$executable) /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Web.Extensions.dll (Join-Path $repo 'src/LiquidDesktop/CompanionServer.cs') (Join-Path $repo 'tests/TestCompanionServer.cs')
if($LASTEXITCODE -ne 0){throw 'Companion transport test compilation failed'}
& $executable
if($LASTEXITCODE -ne 0){throw 'Companion transport checks failed'}
