$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/companion-content'
$fixture=Join-Path $out ('fixture-'+[guid]::NewGuid().ToString('N'))
$helper=Join-Path $fixture 'DesktopSettingsFiles.cs'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try{
 # Compile the real shared file writer without dragging the desktop UI into a storage test.
 $source=Get-Content -LiteralPath (Join-Path $repo 'src/LiquidDesktop/DesktopPreferences.cs') -Raw
 $start=$source.IndexOf('internal static class DesktopSettingsFiles {',[StringComparison]::Ordinal)
 $end=$source.IndexOf('internal sealed class DesktopPreferences {',[StringComparison]::Ordinal)
 if($start -lt 0 -or $end -le $start){throw 'Shared desktop writer source was not found.'}
 [IO.File]::WriteAllText($helper,"using System;using System.IO;using System.Text;using System.Threading;using System.Collections.Generic;using System.Web.Script.Serialization;`n"+$source.Substring($start,$end-$start))
 $binary=Join-Path $out 'TestCompanionContent.exe'
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$binary" /r:System.Web.Extensions.dll (Join-Path $repo 'tests/TestCompanionContent.cs') (Join-Path $repo 'src/LiquidDesktop/CompanionContent.cs') $helper
 if($LASTEXITCODE -ne 0){throw 'Companion content tests did not compile.'}
 & $binary (Join-Path $fixture 'app')
 if($LASTEXITCODE -ne 0){throw 'Companion content behavior test failed.'}
}finally{
 if(Test-Path -LiteralPath $fixture){$resolved=(Resolve-Path -LiteralPath $fixture).Path;$boundary=[IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar;if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped test directory.'};Remove-Item -LiteralPath $resolved -Recurse -Force;if(Test-Path -LiteralPath $resolved){throw 'Fixture cleanup failed.'}}
}
