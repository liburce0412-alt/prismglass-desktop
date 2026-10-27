$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
$fixture=Join-Path $out ('widget-sources-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $out,$fixture | Out-Null
try{
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$out/TestWidgetSources.exe" /r:System.Management.dll /r:System.Web.Extensions.dll (Join-Path $repo 'tests/TestWidgetSources.cs') (Join-Path $repo 'src/LiquidDesktop/WidgetBackend.cs')
 if($LASTEXITCODE -ne 0){throw 'Widget source compilation failed'}
 & (Join-Path $out 'TestWidgetSources.exe') $fixture | Tee-Object -FilePath (Join-Path $out 'widget-source-checks.txt')
 if($LASTEXITCODE -ne 0){throw 'Widget source checks failed'}
}finally{
 if(Test-Path -LiteralPath $fixture){
  $resolved=(Resolve-Path -LiteralPath $fixture).Path
  $boundary=[IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar
  if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped artifacts directory'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
  if(Test-Path -LiteralPath $resolved){throw 'Fixture cleanup failed'}
 }
}
