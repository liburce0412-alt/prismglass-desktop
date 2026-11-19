$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/iteration2-20261110/widgets'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$exe=Join-Path $out 'TestSharedWorkSync.exe'
try{
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$exe" (Join-Path $repo 'tests/TestSharedWorkSync.cs') (Join-Path $repo 'src/LiquidDesktop/SharedDesktopData.cs')
 if($LASTEXITCODE -ne 0){throw 'Shared work sync fixture did not compile'}
 & $exe | Tee-Object -FilePath (Join-Path $out 'shared-work-sync-checks.txt')
 if($LASTEXITCODE -ne 0){throw 'Shared work sync checks failed'}
}finally{if(Test-Path -LiteralPath $exe){Remove-Item -LiteralPath $exe}}
