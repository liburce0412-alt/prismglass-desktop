param([string]$CacheRoot)
$ErrorActionPreference='Stop'
if(!$CacheRoot){
 $CacheRoot=Join-Path (Split-Path $PSScriptRoot) '.deps'
 if(!(Test-Path -LiteralPath (Join-Path $CacheRoot 'qrcoder-1.7.0/lib/net40/QRCoder.dll')) -and (Test-Path -LiteralPath 'D:/Cache/PrismGlass/qrcoder-1.7.0/lib/net40/QRCoder.dll')){$CacheRoot='D:/Cache/PrismGlass'}
}
$package=Join-Path $CacheRoot 'qrcoder-1.7.0'
$assembly=Join-Path $package 'lib/net40/QRCoder.dll'
if(!(Test-Path -LiteralPath $assembly)){
 New-Item -ItemType Directory -Path $CacheRoot -Force | Out-Null
 $archive=Join-Path $CacheRoot 'qrcoder-1.7.0.nupkg'
 if(!(Test-Path -LiteralPath $archive)){Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/qrcoder/1.7.0/qrcoder.1.7.0.nupkg' -OutFile $archive}
 if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne 'B2CB124014C77F571458D418144109F0F44BB38F3A7220EC5EDAF02FEA336485'){throw 'QRCoder package integrity check failed.'}
 Expand-Archive -LiteralPath $archive -DestinationPath $package -Force
}
if(!(Test-Path -LiteralPath $assembly)){throw 'QRCoder net40 assembly is missing.'}
[IO.Path]::GetFullPath($assembly)
