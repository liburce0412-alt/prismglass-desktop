$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo 'artifacts/companion-point-runtime'
$log=Join-Path $repo 'artifacts/experience-polish-20261031/native-point-checks.txt'
if(Test-Path -LiteralPath $fixture){throw 'An existing point fixture needs inspection before reuse.'}
$process=$null
try {
 New-Item -ItemType Directory -Path $fixture | Out-Null
 $sources=@('tests/TestCompanionPointInput.cs','src/LiquidDesktop/CompanionControls.cs','src/LiquidDesktop/CompanionServer.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /nowarn:0649 /platform:x64 "/out:$fixture/TestCompanionPointInput.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll /r:System.Web.Extensions.dll @sources
 if($LASTEXITCODE -ne 0){throw 'Point fixture compilation failed'}
 $process=Start-Process -FilePath (Join-Path $fixture 'TestCompanionPointInput.exe') -ArgumentList ('"'+$log+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(15000)){throw 'Point fixture timed out'}
 if($process.ExitCode -ne 0){throw "Point fixture failed: $log"}
 Get-Content -LiteralPath $log
} finally {
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if([IO.Path]::GetFullPath($fixture) -ne [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/companion-point-runtime'))){throw 'Unexpected fixture cleanup path'}
 if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force}
 if(Test-Path -LiteralPath $fixture){throw 'Temporary point fixture remains'}
}
