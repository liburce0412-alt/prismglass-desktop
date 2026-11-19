param([string]$WebViewVersion='1.0.4191.47',[string]$CacheRoot,[string]$OutputRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$version=(Get-Content -LiteralPath (Join-Path $repo 'VERSION') -Raw).Trim()
if($version -notmatch '^(\d+\.\d+\.\d+)(?:-[A-Za-z0-9.-]+)?$'){throw 'Invalid VERSION'}
$fileVersion=$Matches[1]+'.0'
# Include uncommitted production inputs so local installs of the same release are distinguishable.
$inputs=@('src/LiquidDesktop','rainmeter','scripts/runtime','tools/Startup','tools/CompanionAudio','tools/PrismNpu/main.py','assets/Cursors','assets/Brand/prismglass-icon-refined.ico','assets/Brand/prismglass-logo-refined.png','packaging/PrismGlass.iss','VERSION','licenses','LICENSE','THIRD-PARTY-NOTICES.md','README.md','docs/COMPANION.md','docs/COMPANION-SCREEN.md','docs/NPU-TOOLS.md','docs/REMOTE-DESKTOP-COMPARISON.md','docs/PRIVACY.md')
$inputs+=@(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Name -Match '^(Build|Package|Install|SetupIntegration|GetNAudio|GetQrCoder|ConfigureCompanion.*|RepairCompanion.*)\.ps1$' | ForEach-Object FullName)
$sourceFiles=@(foreach($entry in $inputs){$path=if([IO.Path]::IsPathRooted($entry)){$entry}else{Join-Path $repo $entry};Get-ChildItem -LiteralPath $path -Recurse -File})
$fingerprints=@($sourceFiles | ForEach-Object {([IO.Path]::GetRelativePath($repo,$_.FullName).Replace('\','/'))+' '+(Get-FileHash -LiteralPath $_.FullName).Hash})
[Array]::Sort($fingerprints,[StringComparer]::Ordinal)
$sourceDigest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($fingerprints -join "`n")))).ToLowerInvariant()
$buildId=$version+'+'+$sourceDigest.Substring(0,16)
$deps=if($CacheRoot){[IO.Path]::GetFullPath($CacheRoot)}else{Join-Path $repo '.deps'}
$qrAssembly=& (Join-Path $PSScriptRoot 'GetQrCoder.ps1') -CacheRoot $deps
$package=Join-Path $deps "webview2-$WebViewVersion"
if(!(Test-Path -LiteralPath "$package/lib/net462/Microsoft.Web.WebView2.Core.dll")){
 New-Item -ItemType Directory -Path $deps -Force | Out-Null
 $zip=Join-Path $deps "webview2-$WebViewVersion.zip"
 Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$WebViewVersion/microsoft.web.webview2.$WebViewVersion.nupkg" -OutFile $zip
 Expand-Archive -LiteralPath $zip -DestinationPath $package -Force
}
$out=if($OutputRoot){[IO.Path]::GetFullPath($OutputRoot)}else{Join-Path $repo 'dist/PrismGlass'}
$app=Join-Path $out 'LiquidDesktop'
if((Test-Path -LiteralPath (Join-Path $out 'prismglass-install.json')) -or (Test-Path -LiteralPath (Join-Path $app 'runtime-data')) -or (Test-Path -LiteralPath (Join-Path $app 'photo-cards')) -or (Test-Path -LiteralPath (Join-Path $app 'desktop-settings.json'))){throw 'Build output is a user installation or runtime directory. Choose a clean build directory.'}
New-Item -ItemType Directory -Path $app -Force | Out-Null
Get-ChildItem "$repo/src/LiquidDesktop" -File | Where-Object {$_.Extension -in '.cs','.ps1','.js','.html','.css','.svg','.png' -or $_.Name -in 'app-center-pinyin.txt','app-center-pinyin-LICENSE.txt'} | Copy-Item -Destination $app
Copy-Item "$repo/assets/Brand/prismglass-icon-refined.ico" "$app/PrismGlass.ico"
[IO.File]::WriteAllText((Join-Path $app 'AssemblyInfo.cs'),("using System.Reflection;`n[assembly: AssemblyTitle(""PrismGlass Desktop"")]`n[assembly: AssemblyProduct(""PrismGlass Desktop"")]`n[assembly: AssemblyVersion(""$fileVersion"")]`n[assembly: AssemblyFileVersion(""$fileVersion"")]`n[assembly: AssemblyInformationalVersion(""$buildId"")]`n"))
[IO.File]::WriteAllText((Join-Path $out 'build-info.json'),([ordered]@{version=$version;buildId=$buildId;sourceSha256=$sourceDigest} | ConvertTo-Json))
Copy-Item "$repo/src/LiquidDesktop/companion-web" $app -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $app 'PrismNpu') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'tools/PrismNpu/main.py') -Destination (Join-Path $app 'PrismNpu/main.py')
Copy-Item -LiteralPath $qrAssembly -Destination $app
foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item "$package/lib/net462/$name" $app}
Copy-Item "$package/runtimes/win-x64/native/WebView2Loader.dll" $app
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$uia=Join-Path (Split-Path $csc) 'WPF'
$audioAssemblies=@(& (Join-Path $PSScriptRoot 'GetNAudio.ps1') -CacheRoot $deps)
$audioAssemblies | ForEach-Object {Copy-Item -LiteralPath $_ -Destination $app -Force}
& $csc /nologo /target:exe /platform:x64 /optimize+ "/out:$app/PrismAudio.exe" "/r:$app/NAudio.Core.dll" "/r:$app/NAudio.Wasapi.dll" "/r:$(Split-Path $csc)/netstandard.dll" (Join-Path $repo 'tools/CompanionAudio/Program.cs')
if($LASTEXITCODE -ne 0){throw 'Audio capture helper compilation failed'}
Push-Location $app
try{
 $sources=@(Get-ChildItem "$repo/src/LiquidDesktop" -Filter '*.cs' | ForEach-Object {Join-Path $app $_.Name})+@(Join-Path $app 'AssemblyInfo.cs')
 & $csc /nologo /target:winexe /platform:x64 /optimize+ /win32icon:PrismGlass.ico /out:LiquidDesktop.exe "/resource:$repo/assets/Brand/prismglass-logo-refined.png,PrismGlass.Startup.png" /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Numerics.dll /r:System.Numerics.Vectors.dll /r:System.Runtime.dll /r:System.Runtime.WindowsRuntime.dll /r:System.Runtime.InteropServices.WindowsRuntime.dll "/r:$env:WINDIR/System32/WinMetadata/Windows.Foundation.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Graphics.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Storage.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Security.winmd" /r:System.Xaml.dll "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/WindowsFormsIntegration.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/PresentationCore.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/PresentationFramework.dll" /r:System.Web.Extensions.dll /r:System.Management.dll /r:System.Net.Http.dll /r:QRCoder.dll /r:NAudio.Core.dll /r:NAudio.Wasapi.dll "/r:$(Split-Path $csc)/netstandard.dll" /r:Microsoft.Web.WebView2.Core.dll /r:Microsoft.Web.WebView2.WinForms.dll "/r:$uia/UIAutomationClient.dll" "/r:$uia/UIAutomationTypes.dll" "/r:$uia/WindowsBase.dll" @sources
 if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
 & $csc /nologo /target:winexe /platform:x64 /optimize+ /win32icon:PrismGlass.ico /out:PrismGlass.exe /r:System.Windows.Forms.dll /r:LiquidDesktop.exe AssemblyInfo.cs (Join-Path $repo 'tools/Startup/Program.cs')
 if($LASTEXITCODE -ne 0){throw 'Startup launcher compilation failed'}
}finally{Pop-Location}
Copy-Item "$repo/scripts/runtime/*" $out
Copy-Item "$repo/scripts/Install.ps1" $out
Copy-Item "$repo/scripts/SetupIntegration.ps1" $out
Copy-Item "$repo/VERSION" $out
Copy-Item "$repo/rainmeter" $out -Recurse -Force
Copy-Item "$repo/licenses" $out -Recurse -Force
Copy-Item "$repo/assets/Cursors" $out -Recurse -Force
Copy-Item "$repo/LICENSE","$repo/THIRD-PARTY-NOTICES.md","$repo/README.md" $out
Copy-Item "$repo/docs/COMPANION.md","$repo/docs/COMPANION-SCREEN.md" $out
Copy-Item "$repo/docs/NPU-TOOLS.md" $out
Copy-Item "$repo/docs/REMOTE-DESKTOP-COMPARISON.md" $out
Copy-Item "$repo/docs/PRIVACY.md" $out
Copy-Item "$repo/scripts/ConfigureCompanion.ps1","$repo/scripts/RepairCompanion.ps1","$repo/scripts/RepairCompanionVideo.ps1","$repo/scripts/ConfigureCompanionVirtualDevice.ps1" $out
# Windows PowerShell 5.1 needs a BOM to read Chinese scripts independently of the system locale.
foreach($script in Get-ChildItem -LiteralPath $out -Filter '*.ps1' -File -Recurse){[IO.File]::WriteAllText($script.FullName,[IO.File]::ReadAllText($script.FullName),[Text.UTF8Encoding]::new($true))}
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
$preserved=@('LiquidDesktop/pins.json','LiquidDesktop/theme.txt','LiquidDesktop/paused.txt','LiquidDesktop/wallpaper.png','Glass-Wallpaper-Sample.png','update-inventory.json')
$managed=@(Get-ChildItem -LiteralPath $out -File -Recurse | ForEach-Object {[IO.Path]::GetRelativePath($out,$_.FullName).Replace('\','/')} | Where-Object {$_ -notin $preserved} | Sort-Object)
[IO.File]::WriteAllText((Join-Path $out 'update-inventory.json'),([ordered]@{format=1;files=$managed}|ConvertTo-Json -Compress))
Write-Output "Built $out"
