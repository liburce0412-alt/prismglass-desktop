param([string]$CacheRoot)
$ErrorActionPreference='Stop'
$installed='D:/DevTools/PrismGlass/lively/app'
$names=@('NAudio.Core','NAudio.Wasapi')
if(($names | Where-Object {$file=Join-Path $installed ($_+'.dll');!(Test-Path -LiteralPath $file) -or [Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString() -ne '2.2.1.0'}).Count -eq 0){
 foreach($name in $names){[IO.Path]::GetFullPath((Join-Path $installed ($name+'.dll')))}
 return
}
if(!$CacheRoot){$CacheRoot=Join-Path (Split-Path $PSScriptRoot) '.deps'}
foreach($name in $names){
 $package=Join-Path $CacheRoot ($name.ToLowerInvariant()+'-2.2.1')
 $file=Join-Path $package ('lib/netstandard2.0/'+$name+'.dll')
 if(!(Test-Path -LiteralPath $file)){
  New-Item -ItemType Directory -Force $package | Out-Null
  $archive=Join-Path $package 'package.zip'
  $id=$name.ToLowerInvariant()
  try{
   Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$id/2.2.1/$id.2.2.1.nupkg" -OutFile $archive
   Expand-Archive -LiteralPath $archive -DestinationPath $package -Force
  }finally{if(Test-Path -LiteralPath $archive){Remove-Item -LiteralPath $archive}}
 }
 if([Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString() -ne '2.2.1.0'){throw 'NAudio version mismatch'}
 [IO.Path]::GetFullPath($file)
}
