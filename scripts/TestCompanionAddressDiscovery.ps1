$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$binary=Join-Path $out 'TestCompanionAddressDiscovery.exe'
& $csc /nologo /platform:x64 ('/out:'+$binary) /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Web.Extensions.dll (Join-Path $repo 'src/LiquidDesktop/CompanionServer.cs') (Join-Path $repo 'src/LiquidDesktop/CompanionAddressDiscovery.cs') (Join-Path $repo 'tests/TestCompanionAddressDiscovery.cs')
if($LASTEXITCODE -ne 0){throw 'Address discovery test compilation failed.'}
& $binary
if($LASTEXITCODE -ne 0){throw 'Address discovery checks failed.'}
