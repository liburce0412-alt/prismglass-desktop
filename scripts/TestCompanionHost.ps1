param([string]$CacheRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/companion-host'
$fixture=Join-Path $out ('fixture-'+[guid]::NewGuid().ToString('N'))
$helper=Join-Path $fixture 'DesktopSettingsFiles.cs'
$process=$null
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try{
 $source=Get-Content -LiteralPath (Join-Path $repo 'src/LiquidDesktop/DesktopPreferences.cs') -Raw
 $start=$source.IndexOf('internal static class DesktopSettingsFiles {',[StringComparison]::Ordinal)
 $end=$source.IndexOf('internal sealed class DesktopPreferences {',[StringComparison]::Ordinal)
 if($start -lt 0 -or $end -le $start){throw 'Shared desktop writer source was not found.'}
 [IO.File]::WriteAllText($helper,"using System;using System.IO;using System.Text;using System.Threading;using System.Collections.Generic;using System.Globalization;using System.Text.RegularExpressions;using System.Web.Script.Serialization;`n"+$source.Substring($start,$end-$start))
 $lyrics=Get-Content -LiteralPath (Join-Path $repo 'src/LiquidDesktop/DockLyrics.cs') -Raw
 foreach($section in @(@('internal static class LyricsFiles {','internal sealed class MediaSnapshot {'),@('internal sealed class MediaSnapshot {','internal sealed class LyricLine {'))){
  $first=$lyrics.IndexOf($section[0],[StringComparison]::Ordinal);$last=$lyrics.IndexOf($section[1],[StringComparison]::Ordinal)
  if($first -lt 0 -or $last -le $first){throw 'Shared media source was not found.'}
  [IO.File]::AppendAllText($helper,"`n"+$lyrics.Substring($first,$last-$first))
 }
 $binary=Join-Path $out 'TestCompanionHost.exe'
 $qrAssembly=& (Join-Path $PSScriptRoot 'GetQrCoder.ps1') -CacheRoot $CacheRoot
 Copy-Item -LiteralPath $qrAssembly -Destination $out
 # Reuse the bridge test's in-memory adapters; execute only the hidden native host lifecycle harness.
 $sources=@('tests/TestCompanionHost.cs','tests/TestCompanionBridge.cs','src/LiquidDesktop/CompanionHost.cs','src/LiquidDesktop/CompanionConnection.cs','src/LiquidDesktop/CompanionDiscovery.cs','src/LiquidDesktop/CompanionAddressDiscovery.cs','src/LiquidDesktop/CompanionBridge.cs','src/LiquidDesktop/CompanionContext.cs', 'src/LiquidDesktop/CompanionControls.cs','src/LiquidDesktop/CompanionStream.cs','src/LiquidDesktop/CompanionContent.cs','src/LiquidDesktop/CompanionServer.cs','src/LiquidDesktop/LyricBackgroundPalette.cs')|ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /nowarn:0649 /main:TestCompanionHost "/out:$binary" /r:System.Management.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll "/r:$qrAssembly" @sources $helper
 if($LASTEXITCODE -ne 0){throw 'Companion host tests did not compile.'}
 $stdout=Join-Path $out 'lifecycle-checks.txt';$stderr=Join-Path $out 'lifecycle-errors.txt'
 $process=Start-Process -FilePath $binary -ArgumentList ('"'+(Join-Path $fixture 'app')+'"') -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
 if(!$process.WaitForExit(20000)){throw 'Companion host lifecycle checks timed out.'}
 Get-Content -LiteralPath $stdout
 if($process.ExitCode -ne 0){Get-Content -LiteralPath $stderr;throw 'Companion host lifecycle checks failed.'}
 if((Get-Item -LiteralPath $stderr).Length -eq 0){Remove-Item -LiteralPath $stderr}
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if(Test-Path -LiteralPath $fixture){$resolved=(Resolve-Path -LiteralPath $fixture).Path;$boundary=[IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar;if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped test directory.'};Remove-Item -LiteralPath $resolved -Recurse -Force;if(Test-Path -LiteralPath $resolved){throw 'Fixture cleanup failed.'}}
}
