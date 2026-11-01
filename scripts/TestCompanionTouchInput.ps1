$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo 'artifacts/companion-touch-runtime'
$log=Join-Path $repo 'artifacts/experience-polish-20261031/native-touch-checks.txt'
if(Test-Path -LiteralPath $fixture){throw 'An existing touch fixture needs inspection before reuse.'}
$process=$null
try {
 New-Item -ItemType Directory -Path $fixture | Out-Null
 $sources=@('tests/TestCompanionTouchInput.cs','src/LiquidDesktop/CompanionControls.cs','src/LiquidDesktop/CompanionServer.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /nowarn:0649 /platform:x64 "/out:$fixture/TestCompanionTouchInput.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll /r:System.Web.Extensions.dll @sources
 if($LASTEXITCODE -ne 0){throw 'Touch fixture compilation failed'}
 $process=Start-Process -FilePath (Join-Path $fixture 'TestCompanionTouchInput.exe') -ArgumentList ('"'+$log+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(15000)){throw 'Touch fixture timed out'}
 if($process.ExitCode -ne 0){throw "Touch fixture failed: $log"}
 Get-Content -LiteralPath $log
} finally {
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if([IO.Path]::GetFullPath($fixture) -ne [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/companion-touch-runtime'))){throw 'Unexpected fixture cleanup path'}
 if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force}
 if(Test-Path -LiteralPath $fixture){throw 'Temporary touch fixture remains'}
}
