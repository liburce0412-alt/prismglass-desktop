param(
 [string]$BundleRoot=(Join-Path $PSScriptRoot '../dist/PrismGlass'),
 [string]$OutputRoot=(Join-Path $PSScriptRoot '../artifacts/release'),
 [ValidatePattern('^\d+\.\d+\.\d+(-[A-Za-z0-9.]+)?$')][string]$Version=(Get-Content -LiteralPath (Join-Path $PSScriptRoot '../VERSION') -Raw).Trim(),
 [Alias('CompilerPath')][string]$Compiler
)
$ErrorActionPreference='Stop'
function Test-PrivateBundlePath([string]$Path) {
 $Path=$Path.Replace('/','\')
 return $Path -match '(^|\\)(runtime-data|music-data|photo-cards|dock-icons|artifacts|outputs|companion-setup)(\\|$)' -or
  $Path -match '\.(pfx|p12|pem|key|cer|crt|log|request)$' -or
  $Path -match '(^|\\)wallpaper-monitor-[^\\]*\.png$' -or
  $Path -match '(^|\\)(devices\.bin|(?:prismglass-install|install-record|desktop-settings|desktop-host|companion-host|component-positions|widget-positions|stage-recovery|app-library|companion-settings|lyrics-settings|quota-cache|weather-cache|readiness|dock-click)\.json|installer-runtime\.ini|skin-catalog-pending\.flag|(?:skin-path|rainmeter-path|weather-location|widget-work-context)\.txt)$' -or
  $Path -match '(^|\\)[^\\]*-state(?:\.[^\\]+)?$' -or $Path -match '(^|\\)widget-[^\\]+\.state$'
}
$repo=Split-Path $PSScriptRoot
$BundleRoot=[IO.Path]::GetFullPath($BundleRoot);$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
foreach($required in @('LiquidDesktop/LiquidDesktop.exe','LiquidDesktop/PrismGlass.exe','LiquidDesktop/PrismGlass.ico','SetupIntegration.ps1','LICENSE','DesktopMode.ps1','ApplyUpdate.ps1','update-inventory.json','VERSION')){if(!(Test-Path -LiteralPath (Join-Path $BundleRoot $required))){throw "Build output is missing $required"}}
if((Get-Content -LiteralPath (Join-Path $BundleRoot 'VERSION') -Raw).Trim() -ne $Version){throw 'The bundle version does not match the requested installer version.'}
$build=Get-Content -LiteralPath (Join-Path $BundleRoot 'build-info.json') -Raw | ConvertFrom-Json
if($build.sourceSha256 -cnotmatch '^[0-9a-f]{64}$' -or $build.buildId -cne ($Version+'+'+$build.sourceSha256.Substring(0,16))){throw 'Invalid source identity in the bundle'}
foreach($name in 'LiquidDesktop','PrismGlass'){
 if([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $BundleRoot "LiquidDesktop/$name.exe")).ProductVersion -cne $build.buildId){throw "$name.exe does not match the build identity"}
}
if(!$Compiler){$command=Get-Command ISCC.exe -ErrorAction SilentlyContinue;if($command){$Compiler=$command.Source}}
if(!$Compiler){foreach($path in @("${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe","$env:ProgramFiles/Inno Setup 6/ISCC.exe")){if(Test-Path -LiteralPath $path){$Compiler=$path;break}}}
if(!$Compiler -or !(Test-Path -LiteralPath $Compiler -PathType Leaf)){throw 'Install Inno Setup 6 or pass -Compiler with its ISCC.exe path.'}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$entries=[Collections.Generic.List[string]]::new()
$preserve=@('LiquidDesktop\pins.json','LiquidDesktop\theme.txt','LiquidDesktop\paused.txt','LiquidDesktop\wallpaper.png','Glass-Wallpaper-Sample.png')
$retain=@('ConfigureCompanion.ps1','RepairCompanion.ps1','RepairCompanionVideo.ps1','COMPANION.md')
foreach($file in Get-ChildItem -LiteralPath $BundleRoot -File -Recurse){
 $relative=[IO.Path]::GetRelativePath($BundleRoot,$file.FullName)
 if(Test-PrivateBundlePath $relative){throw "Refusing runtime/private data in bundle: $relative"}
 if($relative -eq 'LiquidDesktop\pins.json' -and [IO.File]::ReadAllText($file.FullName).Trim([char]0xFEFF).Trim() -ne '[]'){throw 'The release bundle must contain only the empty default pins.json, not personal application pins.'}
 $destination=Split-Path $relative
 $flags='ignoreversion'
 if($relative -in $preserve){$flags+=' onlyifdoesntexist uninsneveruninstall'}
 elseif($relative -in $retain){$flags+=' uninsneveruninstall'}
 $entries.Add(('Source: "{0}"; DestDir: "{{app}}\{1}"; Flags: {2}' -f $file.FullName,$destination,$flags))
}
$manifest=Join-Path $OutputRoot 'installer-files.iss'
$skinList=Join-Path $OutputRoot 'installer-skins.txt'
try{
 [IO.File]::WriteAllLines($manifest,$entries,[Text.UTF8Encoding]::new($false))
 $skinSource=Join-Path $BundleRoot 'rainmeter/PrismGlass'
 [string[]]$skinFiles=Get-ChildItem -LiteralPath $skinSource -File -Recurse | ForEach-Object {[IO.Path]::GetRelativePath($skinSource,$_.FullName)}
 [IO.File]::WriteAllLines($skinList,$skinFiles,[Text.UTF8Encoding]::new($false))
 & $Compiler "/DBundleRoot=$BundleRoot" "/DOutputRoot=$OutputRoot" "/DAppVersion=$Version" "/DBuildId=$($build.buildId)" "/DFileList=$manifest" "/DSkinList=$skinList" (Join-Path $repo 'packaging/PrismGlass.iss')
 if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
 $installer=Join-Path $OutputRoot "PrismGlass-Setup-$Version-x64.exe"
 if(!(Test-Path -LiteralPath $installer)){throw 'Installer compiler did not produce its expected output.'}
 Write-Output $installer
}finally{foreach($generated in $manifest,$skinList){if(Test-Path -LiteralPath $generated){Remove-Item -LiteralPath $generated}}}
