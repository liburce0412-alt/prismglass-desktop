param([Parameter(Mandatory)][string]$BundleRoot)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$program=Join-Path $BundleRoot 'LiquidDesktop/LiquidDesktop.exe'
$type=[Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $program)).GetType('DockBackend',$true)
$method=$type.GetMethod('SaveResourceIcon',[Reflection.BindingFlags]'Static, NonPublic')
$sample=Join-Path ([IO.Path]::GetTempPath()) ('PrismGlass-icon-'+[guid]::NewGuid().ToString('N')+'.png')
try{
 $arguments=[object[]]::new(3)
 $arguments[0]=[IO.Path]::GetFullPath((Join-Path $BundleRoot 'LiquidDesktop/PrismGlass.ico'))
 $arguments[1]=[int]0
 $arguments[2]=[string]$sample
 $method.Invoke($null,$arguments)
 $image=[Drawing.Bitmap]::new($sample)
 try{
  if($image.Width -ne 256 -or $image.Height -ne 256){throw "Expected a 256px resource, got $($image.Size)"}
  if($image.GetPixel(0,0).A -gt 30 -or $image.GetPixel(128,128).A -lt 220){throw 'Original silhouette was lost during extraction'}
 }finally{$image.Dispose()}
 'PASS 256px original resource with transparent margins and opaque center'
}finally{if(Test-Path -LiteralPath $sample){Remove-Item -LiteralPath $sample}}
