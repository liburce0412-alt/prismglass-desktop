$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/companion-controls'
$fixture=Join-Path $out ('fixture-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $fixture | Out-Null
try{
 $source=Get-Content -LiteralPath (Join-Path $repo 'src/LiquidDesktop/CompanionServer.cs') -Raw
 $start=$source.IndexOf('internal sealed class CompanionGrant {',[StringComparison]::Ordinal)
 $end=$source.IndexOf('internal sealed class CompanionRequest {',[StringComparison]::Ordinal)
 if($start -lt 0 -or $end -le $start){throw 'Companion contracts were not found'}
 $contract=Join-Path $fixture 'CompanionContracts.cs'
 [IO.File]::WriteAllText($contract,"using System;using System.Text;using System.Collections.Generic;using System.Web.Script.Serialization;`n"+$source.Substring($start,$end-$start))
 $binary=Join-Path $out 'TestCompanionControls.exe'
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$binary" /r:System.Web.Extensions.dll (Join-Path $repo 'src/LiquidDesktop/CompanionControls.cs') (Join-Path $repo 'tests/TestCompanionControls.cs') $contract
 if($LASTEXITCODE -ne 0){throw 'Companion controls compilation failed'}
 & $binary | Tee-Object -FilePath (Join-Path $out 'control-checks.txt')
 if($LASTEXITCODE -ne 0){throw 'Companion control behavior checks failed'}
}finally{
 if(Test-Path -LiteralPath $fixture){
  $resolved=(Resolve-Path -LiteralPath $fixture).Path
  $boundary=[IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar
  if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped artifacts directory'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
  if(Test-Path -LiteralPath $resolved){throw 'Fixture cleanup failed'}
 }
}
