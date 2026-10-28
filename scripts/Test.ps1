$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
foreach($case in @(@('TestDesktopVisibility','DesktopVisibilityPolicy'),@('VerifyDockPolicy','DockVisibilityPolicy'),@('TestCursorPreferences','CursorPreferences'),@('TestDockLyrics','DockLyrics'),@('TestQQMusicLyrics','DockLyrics'),@('TestAppLibrary','AppLibrary'),@('TestDockInteractions','DockBackend'),@('TestDockPathIdentity','DockBackend'),@('TestDesktopPreferences','DesktopPreferences'),@('TestLoginStartup','LoginStartup'),@('TestWeatherLocation','WeatherLocation'),@('TestLyricBackground','DockLyrics'),@('TestMonitorDock','MonitorDock'),@('TestTaskbarVisibility','TaskbarVisibility'))){
 [string[]]$extra=switch($case[0]){ {$_ -in @('TestDockLyrics','TestQQMusicLyrics','TestLyricBackground')}{@((Join-Path $repo 'src/LiquidDesktop/NetEaseSession.cs'),(Join-Path $repo 'src/LiquidDesktop/LyricBackgroundPalette.cs'))};{$_ -in @('TestDockInteractions','TestDockPathIdentity')}{@((Join-Path $repo 'src/LiquidDesktop/DockPreview.cs'))};'TestDesktopPreferences'{@((Join-Path $repo 'src/LiquidDesktop/RuntimePolicy.cs'),(Join-Path $repo 'src/LiquidDesktop/MonitorCatalog.cs'))};'TestWeatherLocation'{@((Join-Path $repo 'src/LiquidDesktop/DesktopPreferences.cs'),(Join-Path $repo 'src/LiquidDesktop/RuntimePolicy.cs'),(Join-Path $repo 'src/LiquidDesktop/MonitorCatalog.cs'))};'TestMonitorDock'{@((Join-Path $repo 'src/LiquidDesktop/MonitorCatalog.cs'))};default{@()}}
 & $csc /nologo ("/out:"+(Join-Path $out ($case[0]+'.exe'))) /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll (Join-Path $repo ('tests/'+$case[0]+'.cs')) (Join-Path $repo ('src/LiquidDesktop/'+$case[1]+'.cs')) @extra
 if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
 if($case[0] -in @('TestDockLyrics','TestQQMusicLyrics','TestDockInteractions','TestDockPathIdentity')){
  $fixture=Join-Path $out ($case[0]+'-'+[guid]::NewGuid().ToString('N'))
  try{& (Join-Path $out ($case[0]+'.exe')) (Join-Path $fixture 'app');$result=$LASTEXITCODE}
  finally{if(Test-Path -LiteralPath $fixture){$resolved=(Resolve-Path -LiteralPath $fixture).Path;if(!$resolved.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped test directory'};Remove-Item -LiteralPath $resolved -Recurse -Force}}
  if($result -ne 0){throw "$($case[0]) behavior test failed"}
 }elseif($case[0] -eq 'TestTaskbarVisibility'){& (Join-Path $out ($case[0]+'.exe')) --policy}
 else{& (Join-Path $out ($case[0]+'.exe'))}
 if($LASTEXITCODE -ne 0){throw 'Behavior test failed'}
}
foreach($script in @('TestCompanionServer.ps1','TestCompanionDiscovery.ps1','TestCompanionAddressDiscovery.ps1','TestCompanionConnection.ps1','TestCompanionContent.ps1','TestCompanionBridge.ps1','TestCompanionHost.ps1','TestCompanionContext.ps1','TestCompanionControls.ps1','TestWidgetSources.ps1')){& (Join-Path $PSScriptRoot $script)}
& (Join-Path $repo 'tests/TestCompanionNetworkSetup.ps1')
& (Join-Path $repo 'tests/TestSetupIntegration.ps1')
& (Join-Path $repo 'tests/TestDesktopModeFailure.ps1')
& $csc /nologo ("/out:"+(Join-Path $out 'TestWallpaperRefresh.exe')) (Join-Path $repo 'tests/TestWallpaperRefresh.cs') (Join-Path $repo 'src/LiquidDesktop/WallpaperRefresh.cs')
if($LASTEXITCODE -ne 0){throw 'Wallpaper test compilation failed'}
& (Join-Path $out 'TestWallpaperRefresh.exe')
if($LASTEXITCODE -ne 0){throw 'Wallpaper sampling checks failed'}
& (Join-Path $repo 'tests/TestInstallerDependencies.ps1')
& (Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell/v1.0/powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $repo 'tests/TestPowerShellCompatibility.ps1')
if($LASTEXITCODE -ne 0){throw 'Windows PowerShell 5.1 compatibility checks failed'}
& node (Join-Path $repo 'tests/TestCompanionWeb.js');if($LASTEXITCODE -ne 0){throw 'Companion web behavior checks failed'}
& node (Join-Path $repo 'tests/TestCompanionGlass.js');if($LASTEXITCODE -ne 0){throw 'Companion optical glass checks failed'}
& (Join-Path $PSScriptRoot 'TestCompanionStream.ps1')
& node (Join-Path $repo 'tests/TestCompanionScreen.js');if($LASTEXITCODE -ne 0){throw 'Companion screen checks failed'}
foreach($test in @('TestCompanionInstall.js','TestCompanionScan.js','TestCompanionLyrics.js','TestCompanionSettings.js','TestCompanionWidgets.js')){& node (Join-Path $repo ('tests/'+$test));if($LASTEXITCODE -ne 0){throw "$test failed"}}
& node (Join-Path $repo 'tests/TestGlassOutlineMessages.js');if($LASTEXITCODE -ne 0){throw 'Glass outline message checks failed'}
& node (Join-Path $repo 'tests/TestDockTransition.js');if($LASTEXITCODE -ne 0){throw 'Dock transition checks failed'}
& node (Join-Path $repo 'tests/TestWallpaperFeedback.js');if($LASTEXITCODE -ne 0){throw 'Wallpaper feedback checks failed'}
& node (Join-Path $repo 'tests/TestWeatherPresentation.js');if($LASTEXITCODE -ne 0){throw 'Weather presentation checks failed'}
& (Join-Path $repo 'tests/TestDockPreviewLifecycle.ps1')
foreach($js in Get-ChildItem "$repo/src/LiquidDesktop" -Filter '*.js' -Recurse){& node --check $js.FullName;if($LASTEXITCODE -ne 0){throw "Invalid JavaScript: $($js.Name)"}}
foreach($ps in Get-ChildItem "$repo/scripts","$repo/src/LiquidDesktop" -Filter '*.ps1' -Recurse){$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile($ps.FullName,[ref]$tokens,[ref]$errors);if($errors.Count){throw $errors[0]}}
'All behavior and syntax checks passed.'
