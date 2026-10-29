$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$source=Join-Path $repo 'scripts/SetupIntegration.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw $errors[0]}
foreach($function in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){Invoke-Expression $function.Extent.Text}
$packageAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/Package.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw $errors[0]}
$privatePathFunction=$packageAst.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-PrivateBundlePath'},$true)
Invoke-Expression $privatePathFunction.Extent.Text
$desktopAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/runtime/DesktopMode.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw $errors[0]}
$catalogFunction=$desktopAst.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Update-PrismGlassSkinCatalog'},$true)
Invoke-Expression $catalogFunction.Extent.Text
$passed=0
function Check([bool]$Condition,[string]$Name){if(!$Condition){throw "FAIL: $Name"};$script:passed++;"PASS: $Name"}
function Reject([scriptblock]$Action,[string]$Name){$failed=$false;try{& $Action | Out-Null}catch{$failed=$true};Check $failed $Name}
foreach($private in @('prismglass-install.json','install-record.json','installer-runtime.ini','LiquidDesktop/desktop-settings.json','LiquidDesktop/app-library.json','LiquidDesktop/companion-settings.json','LiquidDesktop/lyrics-settings.json','LiquidDesktop/devices.bin','LiquidDesktop/readiness.json','LiquidDesktop/weather-cache.json','LiquidDesktop/runtime-data/settings.json','companion-setup/setup-record.json','LiquidDesktop/desktop-state.json','companion-key.pfx')){Check (Test-PrivateBundlePath $private) ("Package rejects private file: $private")}
foreach($public in @('LiquidDesktop/desktop.js','LiquidDesktop/PrismGlass.ico','LiquidDesktop/companion-web/manifest.webmanifest','LiquidDesktop/pins.json','LICENSE','VERSION')){Check (!(Test-PrivateBundlePath $public)) ("Package accepts product file: $public")}
$fixture=Join-Path $repo ('artifacts/tests/setup-'+[guid]::NewGuid().ToString('N'))
try{
 $install=Join-Path $fixture '安装 空格🙂';$skinBase=Join-Path $fixture '自定义皮肤';$skin=Join-Path $skinBase 'PrismGlass';$rainmeter=Join-Path $fixture 'Rainmeter.exe';$settings=Join-Path $fixture 'Rainmeter.ini'
 New-Item -ItemType Directory -Path $fixture -Force | Out-Null
 [IO.File]::WriteAllText($rainmeter,'inert fixture')
 [IO.File]::WriteAllText($settings,"[Rainmeter]`r`nSkinPath=$skinBase`r`n",[Text.Encoding]::Unicode)
 Check ((Resolve-SetupSkin $rainmeter $settings) -eq $skin) 'Custom Rainmeter SkinPath with Unicode and spaces is resolved'
 Reject {Resolve-SetupPath 'C:\'} 'Drive-root installation is rejected'
 Reject {Resolve-SetupPath '..\relative'} 'Relative installation is rejected'
 [void](Assert-SetupOwnership $install $skin $true)
 New-Item -ItemType Directory -Path $install -Force | Out-Null
 [IO.File]::WriteAllText((Join-Path $install 'private.txt'),'keep')
 Reject {Assert-SetupOwnership $install $skin $true} 'Unmarked existing installation is not overwritten'
 Check ((Get-Content -LiteralPath (Join-Path $install 'private.txt') -Raw) -eq 'keep') 'Legacy user file remains intact'
 $pwsh=Join-Path $fixture 'powershell.exe';[IO.File]::WriteAllText($pwsh,'inert Windows PowerShell fixture')
 $record=[ordered]@{product='PrismGlass.Desktop';version=1;root=$install;skin=$skin;rainmeter=$rainmeter;powershell=$pwsh;latitude=31.2;longitude=121.4}
 Write-SetupRecord (Join-Path $install 'prismglass-install.json') $record
 $templates=Join-Path $install 'rainmeter/PrismGlass/Dock'
 New-Item -ItemType Directory -Path $templates -Force | Out-Null
 [IO.File]::WriteAllText((Join-Path $templates 'Main.ini'),'{{INSTALL_ROOT}}|{{POWERSHELL}}|{{LATITUDE}}|{{LONGITUDE}}')
 New-Item -ItemType Directory -Path (Join-Path $install 'LiquidDesktop') -Force | Out-Null
 [IO.File]::WriteAllText((Join-Path $install 'LiquidDesktop/LiquidDesktop.exe'),'inert fixture')
 & $source -Mode Configure -Root $install -RainmeterPath $rainmeter -PowerShellPath $pwsh -RainmeterIni $settings
 Check (([IO.File]::ReadAllText((Join-Path $install 'prismglass-install.json')) | ConvertFrom-Json).powershell -eq $pwsh) 'Installer configuration persists the system PowerShell 5.1 executable'
 $pending=Join-Path $install 'LiquidDesktop/skin-catalog-pending.flag'
 Check (Test-Path -LiteralPath $pending) 'First skin installation records a deferred catalog refresh without calling Rainmeter'
 & {
  $script:catalogCalls=[Collections.Generic.List[string]]::new()
  function Start-Process {
   param($FilePath,$ArgumentList,$WindowStyle,[switch]$Wait,[switch]$PassThru)
   if(!$Wait -or !$PassThru -or $WindowStyle -ne 'Hidden'){throw 'GUI commands must wait for their own exit code without opening a console'}
   $script:catalogCalls.Add($ArgumentList)
   $process=[pscustomobject]@{ExitCode=$(if($FilePath -eq 'rejected.exe'){1}else{0})}
   $process | Add-Member ScriptMethod Dispose {} -PassThru
  }
  function Start-Sleep {param($Milliseconds) $script:catalogCalls.Add('wait:'+ $Milliseconds)}
  Update-PrismGlassSkinCatalog 'fixture.exe' $pending $false
  Check ($script:catalogCalls.Count -eq 0) 'New Rainmeter process scans on startup without a redundant refresh'
  $global:LASTEXITCODE=$null
  Update-PrismGlassSkinCatalog 'fixture.exe' $pending $true
  Check (($script:catalogCalls -join '|') -eq '!RefreshApp|wait:1000') 'Existing Rainmeter refreshes then waits before subsequent activation'
  Check (Test-Path -LiteralPath $pending) 'Refresh marker stays until the whole desktop is ready'
  $global:LASTEXITCODE=0
  Reject {Update-PrismGlassSkinCatalog 'rejected.exe' $pending $true} 'Failed catalog refresh blocks activation even with a stale successful console exit code'
  Check (Test-Path -LiteralPath $pending) 'Failed refresh retains its marker for retry'
  $global:LASTEXITCODE=0
  $script:catalogCalls.Clear();Remove-Item -LiteralPath $pending
  Update-PrismGlassSkinCatalog 'fixture.exe' $pending $true
  Check ($script:catalogCalls.Count -eq 0) 'Ordinary launches do not repeatedly refresh all skins'
 }
 & $source -Mode Configure -Root $install -RainmeterPath $rainmeter -PowerShellPath $pwsh -RainmeterIni $settings
 Check (!(Test-Path -LiteralPath $pending)) 'Existing Dock skin upgrade does not request an unnecessary catalog refresh'
 $refreshAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/runtime/RefreshGlass.ps1'),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw $errors[0]}
 $pathAssignments=@($refreshAst.FindAll({param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -in @('$configured','$skin')},$true))
 & {
  $fixtureRoot=$install
  foreach($assignment in $pathAssignments){Invoke-Expression ($assignment.Extent.Text.Replace('$PSScriptRoot','$fixtureRoot'))}
  Check ($skin -eq (Join-Path $skinBase 'PrismGlass')) 'Wallpaper refresh uses the installed custom skin path'
  Remove-Item -LiteralPath (Join-Path $install 'LiquidDesktop/skin-path.txt')
  foreach($assignment in $pathAssignments){Invoke-Expression ($assignment.Extent.Text.Replace('$PSScriptRoot','$fixtureRoot'))}
  Check ($skin -eq (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter/Skins/PrismGlass')) 'Wallpaper refresh keeps the old-install fallback'
 }
 $main=Join-Path $skin 'Dock/Main.ini';$rendered=[IO.File]::ReadAllText($main)
 Check ($rendered.Contains($install) -and $rendered.Contains('31.2|121.4') -and !$rendered.Contains('{{')) 'Skin templates use current installation and invariant coordinates'
 $bytes=[IO.File]::ReadAllBytes($main);Check ($bytes[0] -eq 255 -and $bytes[1] -eq 254) 'Rainmeter text files use UTF-16'
 [void](Assert-SetupOwnership $install $skin $true)
 Check $true 'Installer-owned upgrade is accepted'
 [IO.File]::WriteAllText((Join-Path $skin 'user-note.txt'),'keep skin note')
 [IO.File]::WriteAllText((Join-Path $templates 'Main.ini'),'updated {{INSTALL_ROOT}}')
 Install-SetupSkin $install $skin $record.powershell $record.latitude $record.longitude
 Check ([IO.File]::ReadAllText($main).StartsWith('updated ')) 'Upgrade replaces owned skin code'
 Check ([IO.File]::ReadAllText((Join-Path $skin 'user-note.txt')) -eq 'keep skin note') 'Upgrade preserves unrelated skin data'
 $foreign=Join-Path $fixture 'foreign'
 Reject {Assert-SetupOwnership $foreign $skin $true} 'Another installation cannot take over owned skin'
 $ownerPath=Join-Path $skin '.prismglass-owner.json';$owner=Read-SetupRecord $ownerPath;$owner.files+=@('..\outside.txt');Write-SetupRecord $ownerPath $owner
 Reject {Remove-SetupSkin $install $skin} 'Escaping uninstall manifest is rejected before deletion'
 Check (Test-Path -LiteralPath $main) 'Failed uninstall validation preserves program skin'
 $owner.files=@('Dock\Main.ini');Write-SetupRecord $ownerPath $owner
 Remove-SetupSkin $install $skin
 Check (!(Test-Path -LiteralPath $main)) 'Uninstall removes the owned skin file'
 Check (Test-Path -LiteralPath (Join-Path $skin 'user-note.txt')) 'Uninstall preserves unowned user skin files'
 Check (Test-Path -LiteralPath (Join-Path $install 'private.txt')) 'Uninstall leaves personal application data intact'
 Check (Test-Path -LiteralPath (Join-Path $install 'prismglass-install.json')) 'Installation recovery record is retained'
 [void](Assert-SetupOwnership $install $skin $true)
 Check $true 'Reinstall can retain user files left in the owned skin directory'
 $absentSkin=Join-Path $fixture 'RemovedSkin'
 Remove-SetupSkin $install $absentSkin
 Check (!(Test-Path -LiteralPath $absentSkin)) 'Uninstall retry is idempotent when the skin directory is already absent'
 $desktop=Join-Path $fixture '桌面';New-Item -ItemType Directory -Path $desktop | Out-Null
 $shell=New-Object -ComObject WScript.Shell
 $oldShortcut=Join-Path $desktop '开启 PrismGlass.lnk'
 $otherShortcut=Join-Path $desktop '退出 PrismGlass.lnk'
 # WScript creates only an ASCII shell; Shell writes Unicode arguments before the filename is moved.
 $temporaryShortcut=Join-Path $fixture 'shortcut.lnk'
 $unicodeShell=New-Object -ComObject Shell.Application
 $legacyShell=Join-Path $fixture 'pwsh.exe';[IO.File]::WriteAllText($legacyShell,'inert legacy PowerShell 7 fixture')
 foreach($entry in @(@($oldShortcut,$install,'Beauty'),@($otherShortcut,$foreign,'Game'))){
  $link=$shell.CreateShortcut($temporaryShortcut);$link.TargetPath=$legacyShell;$link.Save()
  $expected='-File "'+(Join-Path $entry[1] 'DesktopMode.ps1')+'" -Mode '+$entry[2]
  $unicodeLink=$unicodeShell.Namespace($fixture).ParseName('shortcut.lnk').GetLink();$unicodeLink.Arguments=$expected;$unicodeLink.Save()
  Move-Item -LiteralPath $temporaryShortcut -Destination $entry[0]
  $readback=$unicodeShell.Namespace($desktop).ParseName((Split-Path $entry[0] -Leaf)).GetLink()
  Check ($readback.Arguments -ceq $expected) 'Real shortcut preserves Unicode arguments under a Unicode filename'
 }
 Remove-SetupLegacyDesktopShortcuts $install $desktop
 Check (!(Test-Path -LiteralPath $oldShortcut)) 'Upgrade removes the old desktop entry owned by this installation'
 Check (Test-Path -LiteralPath $otherShortcut) 'Upgrade preserves a same-name shortcut targeting another installation'
 $startup=Join-Path $fixture '启动目录';New-Item -ItemType Directory -Path $startup | Out-Null
 $startupLink=Join-Path $startup 'PrismGlass.lnk'
 $link=$shell.CreateShortcut($temporaryShortcut);$link.TargetPath=Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell/v1.0/powershell.exe';$link.Save()
 $startupArguments='-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+(Join-Path $install 'DesktopMode.ps1')+'" -Mode Beauty'
 $unicodeLink=$unicodeShell.Namespace($fixture).ParseName('shortcut.lnk').GetLink();$unicodeLink.Arguments=$startupArguments;$unicodeLink.Save()
 Move-Item -LiteralPath $temporaryShortcut -Destination $startupLink
 Remove-SetupLoginStartup $foreign $startup
 Check (Test-Path -LiteralPath $startupLink) 'Uninstall preserves another installation login shortcut'
 Remove-SetupLoginStartup $install $startup
 Check (!(Test-Path -LiteralPath $startupLink)) 'Uninstall removes the login shortcut owned by this installation'
 Remove-SetupLoginStartup $install $startup
 Check (!(Test-Path -LiteralPath $startupLink)) 'Login shortcut cleanup is idempotent'
 $unownedSkin=Join-Path $fixture 'UnownedSkin';New-Item -ItemType Directory -Path $unownedSkin | Out-Null
 Reject {Remove-SetupSkin $install $unownedSkin} 'Existing skin without an owner is still protected on uninstall retry'
 $iss=Get-Content -LiteralPath (Join-Path $repo 'packaging/PrismGlass.iss') -Encoding UTF8 -Raw
 Check ($iss.Contains('{group}\开启 PrismGlass') -and $iss.Contains('{group}\退出 PrismGlass') -and $iss.Contains('{group}\卸载 PrismGlass')) 'Start menu retains launch, exit and uninstall entries'
 $desktopEntries=@([regex]::Matches($iss,'(?m)^Name: "\{autodesktop\}\\[^\r\n]*') | ForEach-Object {$_.Value})
 Check ($desktopEntries.Count -eq 1 -and $desktopEntries[0].Contains('Name: "{autodesktop}\PrismGlass";') -and $desktopEntries[0].Contains('-Mode Beauty')) 'Desktop has one PrismGlass shortcut that launches Beauty mode'
 $desktopTask=[regex]::Match($iss,'(?m)^Name: desktopicon;[^\r\n]*').Value
 Check ($desktopTask -ne '' -and $desktopTask -notmatch '\bunchecked\b') 'Fresh installations select desktop shortcuts by default'
 Check ($iss.Contains("ExpandConstant('{param:RAINMETER|}')") -and $iss.Contains("ExpandConstant('{param:POWERSHELL|}')")) 'Command-line dependency paths populate the same validated wizard fields'
 Check ($iss -notmatch '(?<![A-Za-z])MsgBox\(') 'Silent installation and uninstall have no unsuppressible custom message boxes'
 Check ([regex]::Matches($iss,'SuppressibleMsgBox\([^;\r\n]+, mbError, MB_OK, IDOK\); Result := False;').Count -eq 5) 'Suppressing errors still rejects missing dependencies and pending component restart'
 Check ($iss.Contains('MessagesFile: "ChineseSimplified.isl"') -and (Test-Path -LiteralPath (Join-Path $repo 'packaging/ChineseSimplified.isl'))) 'Installer and uninstaller use the bundled Simplified Chinese messages'
 Check ($iss.Contains('LicenseFile={#BundleRoot}\LICENSE') -and $iss.Contains('WizardForm.LicenseAcceptedRadio.Checked := True;')) 'Readable license page remains present and is accepted by default'
 $launchEntry=[regex]::Match($iss,'(?m)^Filename: [^\r\n]*postinstall[^\r\n]*').Value
 Check ($launchEntry -match '-Mode Beauty' -and $launchEntry -match '\bskipifsilent\b' -and $launchEntry -match '\brunhidden\b' -and $launchEntry -notmatch '\bunchecked\b') 'Default finish action starts the desktop with a hidden shell and is skipped by silent installs'
}finally{
 $absolute=[IO.Path]::GetFullPath($fixture);$parent=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/tests'))+[IO.Path]::DirectorySeparatorChar
 if(!$absolute.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase)){throw 'Test fixture escaped its output directory'}
 if(Test-Path -LiteralPath $absolute){Remove-Item -LiteralPath $absolute -Recurse -Force}
}
"Setup integration: $passed checks passed; no real desktop, skin, registry or network settings changed."
