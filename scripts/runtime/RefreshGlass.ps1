$ErrorActionPreference='Stop'
# Sample the current static Windows wallpaper, without capturing windows or icons.
$desktop=Get-ItemProperty 'HKCU:\Control Panel\Desktop'
$source=[Environment]::ExpandEnvironmentVariables($desktop.WallPaper)
if(!(Test-Path -LiteralPath $source -PathType Leaf)){throw 'Current Windows wallpaper file was not found.'}
Add-Type -AssemblyName System.Drawing,System.Windows.Forms
Add-Type 'using System.Runtime.InteropServices; public static class GlassSamplingDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
[GlassSamplingDpi]::SetProcessDPIAware() | Out-Null
$bounds=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$image=[System.Drawing.Image]::FromFile($source)
$canvas=[System.Drawing.Bitmap]::new($bounds.Width,$bounds.Height)
$graphics=[System.Drawing.Graphics]::FromImage($canvas)
$target=Join-Path $PSScriptRoot 'Glass-Wallpaper-Sample.png'
$temp=Join-Path $PSScriptRoot 'Glass-Wallpaper-Sample.tmp.png'
try{
 $rgb=((Get-ItemProperty 'HKCU:\Control Panel\Colors').Background -split ' ')
 $graphics.Clear([System.Drawing.Color]::FromArgb([int]$rgb[0],[int]$rgb[1],[int]$rgb[2]))
 $graphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
 $w=$bounds.Width; $h=$bounds.Height
 if($desktop.TileWallpaper -eq '1'){
  $brush=[System.Drawing.TextureBrush]::new($image)
  try{$graphics.FillRectangle($brush,0,0,$w,$h)}finally{$brush.Dispose()}
 }else{
  switch([string]$desktop.WallpaperStyle){
   '0' {$iw=$image.Width;$ih=$image.Height}
   '2' {$iw=$w;$ih=$h}
   '6' {$scale=[Math]::Min($w/$image.Width,$h/$image.Height);$iw=$image.Width*$scale;$ih=$image.Height*$scale}
   default {$scale=[Math]::Max($w/$image.Width,$h/$image.Height);$iw=$image.Width*$scale;$ih=$image.Height*$scale}
  }
  $rect=[System.Drawing.RectangleF]::new(($w-$iw)/2,($h-$ih)/2,$iw,$ih)
  $graphics.DrawImage($image,$rect)
 }
 $canvas.Save($temp,[System.Drawing.Imaging.ImageFormat]::Png)
}finally{$graphics.Dispose();$canvas.Dispose();$image.Dispose()}
Move-Item -LiteralPath $temp -Destination $target -Force
$skin=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter\Skins\PrismGlass'
foreach($file in Get-ChildItem -LiteralPath $skin -Filter Main.ini -Recurse){
 $text=[IO.File]::ReadAllText($file.FullName)
 $text=[regex]::Replace($text,'(?m)^ImagePath=.*$','ImagePath='+$target)
 [IO.File]::WriteAllText($file.FullName,$text,[Text.Encoding]::Unicode)
 $copy=Join-Path $PSScriptRoot ('PrismGlass\'+$file.Directory.Name+'\Main.ini')
 if(Test-Path -LiteralPath $copy){[IO.File]::WriteAllText($copy,$text,[Text.Encoding]::Unicode)}
 if(Get-Process Rainmeter -ErrorAction SilentlyContinue){& ((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LiquidDesktop/rainmeter-path.txt') -Raw).Trim()) '!Refresh' ('PrismGlass\'+$file.Directory.Name)}
}
'Glass wallpaper sampling updated: '+$source

# The shared renderer refreshes all cards including Dock from this sample.
