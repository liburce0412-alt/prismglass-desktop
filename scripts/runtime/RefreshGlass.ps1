$ErrorActionPreference='Stop'
# Sample the current static Windows wallpaper, without capturing windows or icons.
Add-Type -AssemblyName System.Drawing,System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
[StructLayout(LayoutKind.Sequential)] public struct GlassWallpaperRect { public int Left,Top,Right,Bottom; }
[ComImport,Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IGlassDesktopWallpaper {
 void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor,[MarshalAs(UnmanagedType.LPWStr)] string path);
 [return:MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor);
 [return:MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
 uint GetMonitorDevicePathCount();
 GlassWallpaperRect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitor);
 void SetBackgroundColor(uint color);
 uint GetBackgroundColor();
 void SetPosition(int position);
 int GetPosition();
}
public sealed class GlassWallpaperState { public string Source; public uint Background; public int Position; }
public static class GlassWallpaperSource {
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 public static GlassWallpaperState Read(int left,int top,int width,int height){
  var wallpaper=(IGlassDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")));
  try{
   // A null monitor can return no image during a slideshow. Query the current
   // primary monitor explicitly, rather than an older registry/cache path.
   uint count=wallpaper.GetMonitorDevicePathCount();
   for(uint i=0;i<count;i++){
    string monitor=wallpaper.GetMonitorDevicePathAt(i);var rect=wallpaper.GetMonitorRECT(monitor);
    if(rect.Left==left&&rect.Top==top&&rect.Right-left==width&&rect.Bottom-top==height)
     return new GlassWallpaperState{Source=wallpaper.GetWallpaper(monitor),Background=wallpaper.GetBackgroundColor(),Position=wallpaper.GetPosition()};
   }
   throw new InvalidOperationException("The current primary monitor wallpaper could not be queried.");
  }finally{Marshal.ReleaseComObject(wallpaper);}
 }
}
'@
[GlassWallpaperSource]::SetProcessDPIAware() | Out-Null
function Get-GlassWallpaperRectangle([int]$ImageWidth,[int]$ImageHeight,[System.Drawing.Rectangle]$Bounds,[System.Drawing.Rectangle]$DesktopBounds,[int]$Position){
 $area=if($Position -eq 5){$DesktopBounds}else{$Bounds}
 $w=$area.Width;$h=$area.Height
 switch($Position){
  0 {$iw=$ImageWidth;$ih=$ImageHeight}
  2 {$iw=$w;$ih=$h}
  3 {$scale=[Math]::Min($w/$ImageWidth,$h/$ImageHeight);$iw=$ImageWidth*$scale;$ih=$ImageHeight*$scale}
  default {$scale=[Math]::Max($w/$ImageWidth,$h/$ImageHeight);$iw=$ImageWidth*$scale;$ih=$ImageHeight*$scale}
 }
 # Span fills the virtual desktop first, then crops the primary monitor.
 [System.Drawing.RectangleF]::new($area.Left-$Bounds.Left+($w-$iw)/2,$area.Top-$Bounds.Top+($h-$ih)/2,$iw,$ih)
}
$bounds=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$desktopBounds=[System.Windows.Forms.SystemInformation]::VirtualScreen
$state=[GlassWallpaperSource]::Read($bounds.X,$bounds.Y,$bounds.Width,$bounds.Height)
$source=[Environment]::ExpandEnvironmentVariables([string]$state.Source)
if($source -and !(Test-Path -LiteralPath $source -PathType Leaf)){throw 'Current Windows wallpaper file was not found.'}
$image=$null;$canvas=$null;$graphics=$null
$target=Join-Path $PSScriptRoot 'Glass-Wallpaper-Sample.png'
$temp=Join-Path $PSScriptRoot ('Glass-Wallpaper-Sample.'+[Guid]::NewGuid().ToString('N')+'.tmp.png')
try{
 if($source){$image=[System.Drawing.Image]::FromFile($source)}
 $canvas=[System.Drawing.Bitmap]::new($bounds.Width,$bounds.Height)
 $graphics=[System.Drawing.Graphics]::FromImage($canvas)
 $color=$state.Background
 $graphics.Clear([System.Drawing.Color]::FromArgb([int]($color-band255),[int](($color-shr8)-band255),[int](($color-shr16)-band255)))
 $graphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
 $w=$bounds.Width; $h=$bounds.Height
 if($image -and $state.Position -eq 1){
  $brush=[System.Drawing.TextureBrush]::new($image)
  try{$brush.TranslateTransform($desktopBounds.Left-$bounds.Left,$desktopBounds.Top-$bounds.Top);$graphics.FillRectangle($brush,0,0,$w,$h)}finally{$brush.Dispose()}
 }elseif($image){
  $rect=Get-GlassWallpaperRectangle $image.Width $image.Height $bounds $desktopBounds $state.Position
  $graphics.DrawImage($image,$rect)
 }
 $canvas.Save($temp,[System.Drawing.Imaging.ImageFormat]::Png)
 $graphics.Dispose();$graphics=$null;$canvas.Dispose();$canvas=$null
 if([IO.File]::Exists($target)){[IO.File]::Replace($temp,$target,[NullString]::Value)}else{[IO.File]::Move($temp,$target)}
}finally{
 if($graphics){$graphics.Dispose()};if($canvas){$canvas.Dispose()};if($image){$image.Dispose()}
 if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}
}
$configured=Join-Path $PSScriptRoot 'LiquidDesktop/skin-path.txt'
$skin=if([IO.File]::Exists($configured)){[IO.Path]::GetFullPath([IO.File]::ReadAllText($configured).Trim())}else{Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter/Skins/PrismGlass'}
foreach($file in Get-ChildItem -LiteralPath $skin -Filter Main.ini -Recurse){
 $text=[IO.File]::ReadAllText($file.FullName)
 $text=[regex]::Replace($text,'(?m)^ImagePath=.*$','ImagePath='+$target)
 [IO.File]::WriteAllText($file.FullName,$text,[Text.Encoding]::Unicode)
 $copy=Join-Path $PSScriptRoot ('PrismGlass\'+$file.Directory.Name+'\Main.ini')
 if(Test-Path -LiteralPath $copy){[IO.File]::WriteAllText($copy,$text,[Text.Encoding]::Unicode)}
 if(Get-Process Rainmeter -ErrorAction SilentlyContinue){& ((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LiquidDesktop/rainmeter-path.txt') -Encoding UTF8 -Raw).Trim()) '!Refresh' ('PrismGlass\'+$file.Directory.Name)}
}
'Glass wallpaper sampling updated: '+$source

# The shared renderer refreshes all cards including Dock from this sample.
