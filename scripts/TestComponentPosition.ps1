param()
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/component-position-check'
New-Item -ItemType Directory -Path $out -Force | Out-Null
try {
 $sources=@('tests/TestComponentPosition.cs','src/LiquidDesktop/ComponentPosition.cs','src/LiquidDesktop/DesktopPreferences.cs','src/LiquidDesktop/MonitorCatalog.cs','src/LiquidDesktop/RuntimePolicy.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$out/TestComponentPosition.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll @sources
 if($LASTEXITCODE -ne 0){throw 'Component position compilation failed'}
 & "$out/TestComponentPosition.exe" $out
 if($LASTEXITCODE -ne 0){throw 'Component position checks failed'}
} finally {
 $resolved=[IO.Path]::GetFullPath($out)
 if($resolved -ne [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/component-position-check'))){throw 'Unexpected component test cleanup path'}
 if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
 if(Test-Path -LiteralPath $resolved){throw 'Component test cleanup failed'}
}
