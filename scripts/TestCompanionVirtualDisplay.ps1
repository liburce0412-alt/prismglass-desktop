param([switch]$Describe)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/companion-virtual-display'
New-Item -ItemType Directory -Path $out -Force|Out-Null
$sources=@('tests/TestCompanionVirtualDisplay.cs','src/LiquidDesktop/CompanionVirtualDisplay.cs','src/LiquidDesktop/DesktopIcons.cs')|ForEach-Object {Join-Path $repo $_}
& "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo ('/out:'+(Join-Path $out 'TestCompanionVirtualDisplay.exe')) /r:System.Drawing.dll /r:System.Web.Extensions.dll @sources
if($LASTEXITCODE -ne 0){throw 'Virtual-display test compilation failed'}
if($Describe){& (Join-Path $out 'TestCompanionVirtualDisplay.exe') --describe}else{& (Join-Path $out 'TestCompanionVirtualDisplay.exe')}
if($LASTEXITCODE -ne 0){throw 'Virtual-display checks failed'}
