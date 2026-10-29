param([switch]$Live,[string]$LocationFile)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/release-1.0.0'
$fixture=Join-Path $out ('weather-test-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $out,$fixture | Out-Null
try{
 $sources=@('tests/TestWeatherData.cs','src/LiquidDesktop/WeatherData.cs','src/LiquidDesktop/WeatherLocation.cs','src/LiquidDesktop/DesktopPreferences.cs','src/LiquidDesktop/RuntimePolicy.cs','src/LiquidDesktop/MonitorCatalog.cs')|ForEach-Object {Join-Path $repo $_}
 $exe=Join-Path $fixture 'TestWeatherData.exe'
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$exe" /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll @sources
 if($LASTEXITCODE -ne 0){throw 'Weather data test compilation failed'}
 $arguments=@($fixture)
 if($Live){if(!(Test-Path -LiteralPath $LocationFile -PathType Leaf)){throw 'Live verification requires the existing weather-location.txt path.'};$arguments+=@('--live',$LocationFile,(Join-Path $out 'weather-live.json'))}
 & $exe @arguments | Tee-Object -FilePath (Join-Path $out $(if($Live){'weather-live.log'}else{'weather-data.log'}))
 if($LASTEXITCODE -ne 0){throw 'Weather data checks failed'}
}finally{
 if(Test-Path -LiteralPath $fixture){$resolved=(Resolve-Path -LiteralPath $fixture).Path;$boundary=[IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar;if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped evidence directory'};Remove-Item -LiteralPath $resolved -Recurse -Force;if(Test-Path -LiteralPath $resolved){throw 'Weather fixture cleanup failed'}}
}
