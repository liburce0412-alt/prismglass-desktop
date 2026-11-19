param([string]$CacheRoot)
$ErrorActionPreference='Stop'
$version='2.4.0'
$names=@('NAudio.Core','NAudio.Wasapi')
$hashes=@{'NAudio.Core'='63EC0488354353F035764E2CAE470B04AFB790B0E5A9706ED1987B9AF3CFE02F';'NAudio.Wasapi'='AEC112D586ADF06C467F444AEC745BDE4932D7B44D77B393634FE5EA09E20B12'}
if(!$CacheRoot){
 $CacheRoot=Join-Path (Split-Path $PSScriptRoot) '.deps'
 if(($names | Where-Object {!(Test-Path -LiteralPath (Join-Path $CacheRoot ($_.ToLowerInvariant()+'-'+$version+'/lib/netstandard2.0/'+$_+'.dll')))}).Count -gt 0 -and ($names | Where-Object {!(Test-Path -LiteralPath ('D:/Cache/PrismGlass/'+$_.ToLowerInvariant()+'-'+$version+'/lib/netstandard2.0/'+$_+'.dll'))}).Count -eq 0){$CacheRoot='D:/Cache/PrismGlass'}
}
foreach($name in $names){
 $package=Join-Path $CacheRoot ($name.ToLowerInvariant()+'-'+$version)
 $file=Join-Path $package ('lib/netstandard2.0/'+$name+'.dll')
 if(!(Test-Path -LiteralPath $file)){
  New-Item -ItemType Directory -Force $package | Out-Null
  $archive=Join-Path $package 'package.zip'
  $id=$name.ToLowerInvariant()
  try{
   Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile $archive
   if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $hashes[$name]){throw 'NAudio package integrity check failed'}
   Expand-Archive -LiteralPath $archive -DestinationPath $package -Force
  }finally{if(Test-Path -LiteralPath $archive){Remove-Item -LiteralPath $archive}}
 }
 if([Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString() -ne ($version+'.0')){throw 'NAudio version mismatch'}
 [IO.Path]::GetFullPath($file)
}
