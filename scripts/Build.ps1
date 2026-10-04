param([string]$WebViewVersion='1.0.4191.47')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$deps=Join-Path $repo '.deps'
$package=Join-Path $deps "webview2-$WebViewVersion"
if(!(Test-Path -LiteralPath "$package/lib/net462/Microsoft.Web.WebView2.Core.dll")){
 New-Item -ItemType Directory -Path $deps -Force | Out-Null
 $zip=Join-Path $deps "webview2-$WebViewVersion.zip"
 Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$WebViewVersion/microsoft.web.webview2.$WebViewVersion.nupkg" -OutFile $zip
 Expand-Archive -LiteralPath $zip -DestinationPath $package -Force
}
$out=Join-Path $repo 'dist/PrismGlass'
$app=Join-Path $out 'LiquidDesktop'
New-Item -ItemType Directory -Path $app -Force | Out-Null
Get-ChildItem "$repo/src/LiquidDesktop" -File | Copy-Item -Destination $app
foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item "$package/lib/net462/$name" $app}
Copy-Item "$package/runtimes/win-x64/native/WebView2Loader.dll" $app
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
Push-Location $app
try{
 $sources=@(Get-ChildItem -Filter '*.cs' | ForEach-Object {$_.FullName})
 & $csc /nologo /target:winexe /platform:x64 /optimize+ /out:LiquidDesktop.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Management.dll /r:Microsoft.Web.WebView2.Core.dll /r:Microsoft.Web.WebView2.WinForms.dll @sources
 if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
}finally{Pop-Location}
Copy-Item "$repo/scripts/runtime/*" $out
Copy-Item "$repo/scripts/Install.ps1" $out
Copy-Item "$repo/rainmeter" $out -Recurse -Force
Copy-Item "$repo/licenses" $out -Recurse -Force
Copy-Item "$repo/LICENSE","$repo/THIRD-PARTY-NOTICES.md","$repo/README.md" $out
Get-ChildItem $package -File | Where-Object {$_.Name -match 'license|notice'} | Copy-Item -Destination "$out/licenses"
# A neutral generated sample; the user's wallpaper and application icons are never bundled.
Add-Type -AssemblyName System.Drawing
$bitmap=[Drawing.Bitmap]::new(1920,1080)
$graphics=[Drawing.Graphics]::FromImage($bitmap)
$brush=[Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(0,0,1920,1080),[Drawing.Color]::FromArgb(42,56,91),[Drawing.Color]::FromArgb(173,181,219),35)
try{$graphics.FillRectangle($brush,0,0,1920,1080);$bitmap.Save("$out/Glass-Wallpaper-Sample.png",[Drawing.Imaging.ImageFormat]::Png)}finally{$brush.Dispose();$graphics.Dispose();$bitmap.Dispose()}
Copy-Item "$out/Glass-Wallpaper-Sample.png" "$app/wallpaper.png"
'[]' | Set-Content "$app/pins.json" -Encoding utf8
'Sky' | Set-Content "$app/theme.txt" -Encoding ascii
'false' | Set-Content "$app/paused.txt" -Encoding ascii
Write-Output "Built $out"
