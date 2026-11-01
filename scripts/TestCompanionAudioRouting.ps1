param([Parameter(Mandatory=$true)][string]$BuildRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/audio-routing-runtime'))
$app=Join-Path ([IO.Path]::GetFullPath($BuildRoot)) 'LiquidDesktop'
$log=Join-Path $repo 'artifacts/experience-polish-20261031/audio/routing-checks.txt'
if(Test-Path -LiteralPath $fixture){throw 'Inspect the existing audio routing probe before continuing.'}
try{
 New-Item -ItemType Directory -Path $fixture -Force | Out-Null
 foreach($name in @('NAudio.Core.dll','NAudio.Wasapi.dll')){Copy-Item -LiteralPath (Join-Path $app $name) -Destination $fixture}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /platform:x64 "/out:$fixture/TestCompanionAudioRouting.exe" "/r:$fixture/NAudio.Core.dll" "/r:$fixture/NAudio.Wasapi.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/netstandard.dll" (Join-Path $repo 'tests/TestCompanionAudioRouting.cs')
 if($LASTEXITCODE -ne 0){throw 'Routing probe compilation failed'}
 & (Join-Path $fixture 'TestCompanionAudioRouting.exe') (Join-Path $app 'PrismAudio.exe') 2>&1 | Tee-Object -FilePath $log
 if($LASTEXITCODE -ne 0){throw 'Routing probe failed; inspect its log'}
}finally{
 if($fixture -ne [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/audio-routing-runtime'))){throw 'Unexpected cleanup path'}
 if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force}
 if(Test-Path -LiteralPath $fixture){throw 'Temporary routing probe remains'}
}
