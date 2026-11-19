param([Parameter(Mandatory=$true)][string]$AppAssembly,[string]$OutputRoot=(Join-Path $PSScriptRoot '../artifacts/wallpaper-startup-checks'))
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$assembly=(Resolve-Path -LiteralPath $AppAssembly).Path
$out=[IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$test=Join-Path $out 'TestWallpaperStartup.exe'
$process=$null
try {
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$test" /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $repo 'tests/TestWallpaperStartup.cs')
 if($LASTEXITCODE -ne 0){throw 'Startup wallpaper test compilation failed'}
 $process=Start-Process -FilePath $test -ArgumentList ('"'+$assembly+'"') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $out 'checks.txt') -RedirectStandardError (Join-Path $out 'errors.txt')
 if(!$process.WaitForExit(25000)){throw 'Startup wallpaper checks timed out'}
 Get-Content -LiteralPath (Join-Path $out 'checks.txt')
 if($process.ExitCode -ne 0){Get-Content -LiteralPath (Join-Path $out 'errors.txt');throw 'Startup wallpaper checks failed'}
} finally {
 if($process){if(!$process.HasExited){$process.Kill();[void]$process.WaitForExit(3000)};$process.Dispose()}
 if(Test-Path -LiteralPath $test){Remove-Item -LiteralPath $test -Force}
}
