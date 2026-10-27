$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$binary=Join-Path $out 'TestCompanionContext.exe'
& $csc /nologo /platform:x64 ("/out:"+$binary) /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll (Join-Path $repo 'src/LiquidDesktop/CompanionContext.cs') (Join-Path $repo 'tests/TestCompanionContext.cs')
if($LASTEXITCODE -ne 0){throw 'Companion context test compilation failed.'}
& $binary
if($LASTEXITCODE -ne 0){throw 'Companion context behavior checks failed.'}
