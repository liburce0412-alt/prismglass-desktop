$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/window-capture'
$app=Join-Path $out 'app'
$process=$null
New-Item -ItemType Directory -Path $app -Force | Out-Null
try {
 $sources=@('tests/TestWindowCapture.cs','src/LiquidDesktop/WindowCapture.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$app/TestWindowCapture.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.dll /r:System.Runtime.WindowsRuntime.dll /r:System.Runtime.InteropServices.WindowsRuntime.dll "/r:$env:WINDIR/System32/WinMetadata/Windows.Foundation.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Graphics.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Storage.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Security.winmd" @sources
 if($LASTEXITCODE -ne 0){throw 'Window capture test compilation failed'}
 $process=Start-Process -FilePath "$app/TestWindowCapture.exe" -ArgumentList ('"'+(Join-Path $out 'checks.txt')+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(30000)){throw 'Window capture test timed out'}
 Get-Content -LiteralPath (Join-Path $out 'checks.txt')
 if($process.ExitCode -ne 0){throw 'Window capture checks failed'}
} finally {
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if(Test-Path -LiteralPath $app){$path=(Resolve-Path -LiteralPath $app).Path;if(!$path.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup escaped test directory'};Remove-Item -LiteralPath $path -Recurse -Force;if(Test-Path -LiteralPath $path){throw 'Cleanup failed'}}
}
