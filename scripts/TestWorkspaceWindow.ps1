param([Parameter(Mandatory=$true)][string]$AppRoot,[switch]$GeometryOnly,[switch]$RecoveryOnly,[switch]$WallpaperOnly,[switch]$PreferencesOnly)
$ErrorActionPreference='Stop'
if(([int][bool]$GeometryOnly+[int][bool]$RecoveryOnly+[int][bool]$WallpaperOnly+[int][bool]$PreferencesOnly) -gt 1){throw 'Choose one native check mode'}
$repo=Split-Path $PSScriptRoot
$source=(Resolve-Path -LiteralPath $AppRoot).Path
$out=Join-Path $repo 'artifacts/workspace-window'
$fixture=Join-Path $out ('fixture-'+[Guid]::NewGuid().ToString('N'))
$media=Join-Path $out 'music-data';$mediaExisted=Test-Path -LiteralPath $media
$process=$null
New-Item -ItemType Directory -Force -Path $out | Out-Null
try {
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){Copy-Item -LiteralPath (Join-Path $source $name) -Destination $out -Force}
 $test=Join-Path $out 'TestWorkspaceWindow.exe'
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 "/out:$test" /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$out/Microsoft.Web.WebView2.Core.dll" "/r:$out/Microsoft.Web.WebView2.WinForms.dll" (Join-Path $repo 'tests/TestWorkspaceWindow.cs')
 if($LASTEXITCODE -ne 0){throw 'Native workspace harness compilation failed'}
 $log=Join-Path $out $(if($GeometryOnly){'native-alignment-checks.txt'}elseif($RecoveryOnly){'native-recovery-checks.txt'}elseif($WallpaperOnly){'native-wallpaper-checks.txt'}elseif($PreferencesOnly){'native-preferences-checks.txt'}else{'native-checks.txt'})
 $arguments='"'+(Join-Path $source 'LiquidDesktop.exe')+'" "'+$fixture+'" "'+$log+'"'
 if($GeometryOnly){$arguments+=' --geometry'}
 if($RecoveryOnly){$arguments+=' --recovery'}
 if($WallpaperOnly){$arguments+=' --wallpaper'}
 if($PreferencesOnly){$arguments+=' --preferences'}
 $process=Start-Process -FilePath $test -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(60000)){throw 'Native workspace checks timed out'}
 Get-Content -LiteralPath $log
 if($process.ExitCode -ne 0){throw "Native workspace checks failed; inspect $log"}
} finally {
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 $resolved=[IO.Path]::GetFullPath($fixture)
 if(!$resolved.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped the test output directory'}
 for($attempt=0;Test-Path -LiteralPath $resolved;$attempt++){
  try{Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop}catch{if($attempt -ge 39){throw};Start-Sleep -Milliseconds 250}
 }
 if(Test-Path -LiteralPath $resolved){throw 'Native workspace fixture cleanup failed'}
 if($PreferencesOnly -and !$mediaExisted -and (Test-Path -LiteralPath $media) -and @(Get-ChildItem -LiteralPath $media -Force).Count -eq 0){Remove-Item -LiteralPath $media}
 foreach($name in @('TestWorkspaceWindow.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){
  $temporary=Join-Path $out $name
  if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}
  if(Test-Path -LiteralPath $temporary){throw 'Native workspace test executable cleanup failed'}
 }
}
