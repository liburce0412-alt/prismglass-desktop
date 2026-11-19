$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$audioAssemblies=@(& (Join-Path $PSScriptRoot 'GetNAudio.ps1'))
$audioAssemblies | ForEach-Object {Copy-Item -LiteralPath $_ -Destination $out -Force}
$audioRefs=@($audioAssemblies | ForEach-Object {'/r:'+$_})
$sources=@('tests/TestAudioOutputState.cs','src/LiquidDesktop/AudioOutputState.cs')|ForEach-Object {Join-Path $repo $_}
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$out/TestAudioOutputState.exe" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/netstandard.dll" @audioRefs @sources
if($LASTEXITCODE -ne 0){throw 'Audio endpoint test compilation failed'}
& (Join-Path $out 'TestAudioOutputState.exe') --read-only | Tee-Object -FilePath (Join-Path $out 'audio-output-checks.txt')
if($LASTEXITCODE -ne 0){throw 'Audio endpoint test failed'}
