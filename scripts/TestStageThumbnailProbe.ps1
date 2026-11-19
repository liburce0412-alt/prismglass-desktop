$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/iteration6-20261112/stage-thumbnails/probe'
$app=Join-Path $out 'app'
New-Item -ItemType Directory -Path $out,$app -Force | Out-Null
$process=$null
try {
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$app/TestStageThumbnailProbe.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.dll /r:System.Runtime.WindowsRuntime.dll /r:System.Runtime.InteropServices.WindowsRuntime.dll "/r:$env:WINDIR/System32/WinMetadata/Windows.Foundation.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Graphics.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Storage.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Security.winmd" (Join-Path $repo 'tests/TestStageThumbnailProbe.cs') (Join-Path $repo 'src/LiquidDesktop/WindowCapture.cs')
 if($LASTEXITCODE -ne 0){throw 'Thumbnail probe did not compile'}
 $process=Start-Process -FilePath "$app/TestStageThumbnailProbe.exe" -ArgumentList ('"'+$out+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(12000)){throw 'Thumbnail probe timed out'}
 Get-Content -LiteralPath (Join-Path $out 'checks.txt')
 if($process.ExitCode -ne 0){throw 'Thumbnail probe failed; retain pixel evidence'}
} finally {
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 $owned=[IO.Path]::GetFullPath($app)+[IO.Path]::DirectorySeparatorChar
 Get-CimInstance Win32_Process -Filter "Name='TestStageThumbnailProbe.exe'" | Where-Object {$_.ExecutablePath -and $_.ExecutablePath.StartsWith($owned,[StringComparison]::OrdinalIgnoreCase)} | ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue}
 if(Test-Path -LiteralPath $app){$resolved=(Resolve-Path -LiteralPath $app).Path;if(!$resolved.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Probe cleanup escaped its directory'};Remove-Item -LiteralPath $resolved -Recurse -Force;if(Test-Path -LiteralPath $resolved){throw 'Probe cleanup failed'}}
}
