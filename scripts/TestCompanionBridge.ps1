$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/companion-bridge'
$fixture=Join-Path $out ('fixture-'+[guid]::NewGuid().ToString('N'))
$helper=Join-Path $fixture 'DesktopSettingsFiles.cs'
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
 $binary=Join-Path $out 'TestCompanionBridge.exe'
 $sources=@('tests/TestCompanionBridge.cs','src/LiquidDesktop/CompanionBridge.cs','src/LiquidDesktop/CompanionContext.cs', 'src/LiquidDesktop/CompanionControls.cs','src/LiquidDesktop/CompanionStream.cs','src/LiquidDesktop/CompanionVirtualDisplay.cs','src/LiquidDesktop/DesktopIcons.cs','src/LiquidDesktop/CompanionContent.cs','src/LiquidDesktop/CompanionServer.cs','src/LiquidDesktop/LyricBackgroundPalette.cs')|ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /nowarn:0649 "/out:$binary" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Security.dll @sources $helper
 if($LASTEXITCODE -ne 0){throw 'Companion bridge tests did not compile.'}
 & $binary (Join-Path $fixture 'app')
 if($LASTEXITCODE -ne 0){throw 'Companion bridge behavior test failed.'}
}finally{
 if(Test-Path -LiteralPath $fixture){$resolved=(Resolve-Path -LiteralPath $fixture).Path;$boundary=[IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar;if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped test directory.'};Remove-Item -LiteralPath $resolved -Recurse -Force;if(Test-Path -LiteralPath $resolved){throw 'Fixture cleanup failed.'}}
}
