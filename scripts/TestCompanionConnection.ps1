$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$binary=Join-Path $out 'TestCompanionConnection.exe'
& $csc /nologo /platform:x64 ('/out:'+$binary) /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Management.dll /r:System.Web.Extensions.dll (Join-Path $repo 'src/LiquidDesktop/CompanionServer.cs') (Join-Path $repo 'src/LiquidDesktop/CompanionConnection.cs') (Join-Path $repo 'tests/TestCompanionConnection.cs')
if($LASTEXITCODE -ne 0){throw 'Companion connection test compilation failed.'}
& $binary (Join-Path $PSHOME 'pwsh.exe')
if($LASTEXITCODE -ne 0){throw 'Companion connection checks failed.'}
