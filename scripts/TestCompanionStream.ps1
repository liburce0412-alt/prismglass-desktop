$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/companion-stream'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$sources=@('tests/TestCompanionStream.cs','src/LiquidDesktop/CompanionStream.cs','src/LiquidDesktop/CompanionServer.cs') | ForEach-Object {Join-Path $repo $_}
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /nowarn:0649 ('/out:'+(Join-Path $out 'TestCompanionStream.exe')) /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll /r:System.Web.Extensions.dll @sources
if($LASTEXITCODE -ne 0){throw 'Stream test compilation failed'}
& (Join-Path $out 'TestCompanionStream.exe')
if($LASTEXITCODE -ne 0){throw 'Stream tests failed'}
