$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$file=Join-Path $repo 'scripts/runtime/ApplyUpdate.ps1'
$source=[IO.File]::ReadAllText($file)
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseInput($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw $errors[0]}
function Check($condition,$label){if(!$condition){throw "FAIL: $label"};"PASS: $label"}
function Replace-WorkerFunction($name,$replacement){
 $definition=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 if(!$definition){throw "Missing updater function $name"}
 $script:fixtureSource=$script:fixtureSource.Replace($definition.Extent.Text,$replacement)
}
$id=[guid]::NewGuid().ToString('N')
$base=[IO.Path]::GetTempPath().TrimEnd('\')
$fixture=Join-Path $base ('PrismUpdateFixture-'+$id)
$script:fixtureRoot=Join-Path $fixture 'app';$app=Join-Path $script:fixtureRoot 'LiquidDesktop'
$cache=Join-Path $app 'runtime-data/updates';$skin=Join-Path $fixture 'skins/PrismGlass';$exe=Join-Path $app 'LiquidDesktop.exe'
$launcher=Join-Path $app 'PrismGlass.exe';$userData=Join-Path $app 'runtime-data/devices.bin';$skinFile=Join-Path $skin 'Dock/Main.ini'
$asset='PrismGlass-Setup-2.1.0-x64.exe';$download=Join-Path $cache $asset
$result=Join-Path $cache 'apply-result.json';$staged=Join-Path $cache 'staged.json'
$installRecord=Join-Path $script:fixtureRoot 'prismglass-install.json'
$script:fixtureSource=$source.Replace('$PSScriptRoot','$script:fixtureRoot').Replace('Local\PrismGlassApplyUpdate','Local\PrismUpdateFixture-'+$id).Replace('Local\PrismGlassDesktopMode','Local\PrismUpdateFixtureMode-'+$id)
Replace-WorkerFunction 'Read-OfficialUpdateRelease' 'function Read-OfficialUpdateRelease {$script:events.Add("official");if($script:networkFailed){throw "fixture offline"};if($script:becameBusy){$script:companion.active=$true};if($script:desktopClosed){$script:desktop=$null};return $script:official}'
Replace-WorkerFunction 'Read-ActiveUpdateOwner' @'
function Read-ActiveUpdateOwner($File,$Exe){
 if($File -like '*companion-host.json'){return $script:companion}
 if($File -like '*desktop-host.json'){return $script:desktop}
}
'@
Replace-WorkerFunction 'Wait-UpdateReopen' 'function Wait-UpdateReopen($File,$Executable,$Companion,$Requested,$ListenerExpected){return $true}'
function Get-Item($LiteralPath,[switch]$Force){
 $item=Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force:$Force
 if($LiteralPath -eq $exe){return [pscustomobject]@{Attributes=$item.Attributes;VersionInfo=[pscustomobject]@{ProductVersion=$script:productVersion}}}
 return $item
}
function New-ProcessStub([int]$Code){
 $proc=[pscustomobject]@{ExitCode=$Code}
 $proc|Add-Member ScriptMethod WaitForExit {param($ms) $true}
 $proc|Add-Member ScriptMethod Dispose {}
 return $proc
}
function Get-Process($Id,$Name){if($Id -eq 1111){return New-ProcessStub 0}}
function Start-Process($FilePath,$ArgumentList,$WindowStyle,[switch]$PassThru){
 if($FilePath -eq $exe -and $ArgumentList -contains '--update-stop-companion'){$script:events.Add('stop-companion');$script:companion=$null;return New-ProcessStub 0}
 if($FilePath -like '*powershell.exe' -and $ArgumentList -contains 'Game'){$script:events.Add('stop-desktop');$script:desktop=$null;return New-ProcessStub 0}
 if($FilePath -eq $download){
  if($ArgumentList -notcontains ('/DIR="'+$script:fixtureRoot+'"') -or $ArgumentList -notcontains '/NORESTART'){throw 'Fixture expected an in-place install without a Windows reboot'}
  $script:events.Add('install')
  [IO.File]::WriteAllText($exe,'new executable');[IO.File]::WriteAllText($skinFile,'new skin')
  if(!$script:failInstall){$script:productVersion='2.1.0+verified';$script:installation.buildId=$script:productVersion;Save-Json $installRecord $script:installation}
  else{[IO.File]::Delete($skinFile);[IO.Directory]::Delete((Split-Path -Parent $skinFile))}
  return New-ProcessStub ([int]$script:failInstall)
 }
 if($FilePath -eq $exe -and $ArgumentList -contains '--ensure-companion'){$script:events.Add('reopen-companion');return}
 if($FilePath -eq $exe -and $ArgumentList -contains '--companion'){$script:events.Add('reopen-manager');return}
 if($FilePath -eq $launcher){$script:events.Add('reopen-desktop');return}
 throw "Fixture rejected unexpected process launch: $FilePath"
}
function Save-Json($Path,$Data){[IO.File]::WriteAllText($Path,($Data|ConvertTo-Json -Depth 6 -Compress))}
function Setup-FakeInstall($busy,$badAsset,$badBytes,$fail,[bool]$becomesBusy=$false){
 $script:events=[Collections.Generic.List[string]]::new();$script:productVersion='2.0.0+old';$script:failInstall=$fail;$script:becameBusy=$becomesBusy;$script:networkFailed=$false;$script:desktopClosed=$false
 $script:installation=@{product='PrismGlass.Desktop';version=1;root=$script:fixtureRoot;skin=$skin;rainmeter=(Join-Path $fixture 'Rainmeter.exe');powershell=(Join-Path $fixture 'powershell.exe');buildId=$script:productVersion}
 Save-Json $installRecord $script:installation
 Save-Json (Join-Path $script:fixtureRoot 'update-inventory.json') @{format=1;files=@('LiquidDesktop/LiquidDesktop.exe','LiquidDesktop/PrismGlass.exe','DesktopMode.ps1')}
 Save-Json (Join-Path $skin '.prismglass-owner.json') @{product='PrismGlass.Desktop';root=$script:fixtureRoot;files=@('Dock/Main.ini')}
 [IO.File]::WriteAllText($exe,'old executable');[IO.File]::WriteAllText($launcher,'fixture launcher')
 [IO.File]::WriteAllText($skinFile,'old skin');[IO.File]::WriteAllText($userData,'private state')
 [IO.File]::WriteAllText((Join-Path $script:fixtureRoot 'DesktopMode.ps1'),'inert fixture')
 [IO.File]::WriteAllText((Join-Path $cache 'enabled'),'')
 [IO.File]::WriteAllText($download,'verified installer bytes')
 $hash=(Get-FileHash -LiteralPath $download).Hash.ToLowerInvariant();$size=(Get-Item -LiteralPath $download).Length
 Save-Json $staged @{version='2.1.0';tag='v2.1.0';name=$asset;sha256=$hash;size=$size}
 [IO.File]::Delete($result)
 $url='https://github.com/liburce0412-alt/prismglass-desktop/releases/download/v2.1.0/'+$asset
 if($badAsset){$url=$url.Replace('liburce0412-alt','untrusted-fork')}
 $script:official=@{tag_name='v2.1.0';draft=$false;prerelease=$false;assets=@(@{name=$asset;state='uploaded';size=$size;browser_download_url=$url;digest=('sha256:'+$hash)})}
 if($badBytes){[IO.File]::WriteAllText($download,'tampered')}
 $script:companion=[pscustomobject]@{state='running';active=$busy;pid=1111;companionRunning=$true}
 $script:desktop=[pscustomobject]@{state='running';pid=2222;companionRunning=$false}
}
try{
 New-Item -ItemType Directory -Path $cache,(Split-Path -Parent $skinFile) -Force|Out-Null
 $worker=[scriptblock]::Create($script:fixtureSource)
 Setup-FakeInstall $true $false $false $false
 & $worker
 Check (($script:events.Count -eq 0) -and (([IO.File]::ReadAllText($result)|ConvertFrom-Json).status -eq 'deferred') -and (Test-Path $download)) 'Active projection defers without contacting GitHub or stopping either role'
 Setup-FakeInstall $false $false $false $false $true
 & $worker
 Check ((($script:events -join '|') -eq 'official') -and (([IO.File]::ReadAllText($result)|ConvertFrom-Json).status -eq 'deferred')) 'Starting a new projection during verification prevents shutdown'
 Setup-FakeInstall $false $false $false $false
 $script:desktopClosed=$true
 & $worker
 Check ((($script:events -join '|') -eq 'official') -and (([IO.File]::ReadAllText($result)|ConvertFrom-Json).status -eq 'deferred')) 'A user exiting the desktop during verification takes priority over reopening it'
 Setup-FakeInstall $false $false $false $false
 $script:networkFailed=$true
 & $worker
 Check ((($script:events -join '|') -eq 'official') -and (([IO.File]::ReadAllText($result)|ConvertFrom-Json).status -eq 'deferred')) 'Temporary release lookup failure keeps the staged update and running components'
 Setup-FakeInstall $false $true $false $false
 & $worker
 Check (($script:events -join '|') -eq 'official') 'A substituted release URL cannot reach shell shutdown'
 Setup-FakeInstall $false $false $true $false
 & $worker
 Check ((($script:events -join '|') -eq 'official') -and (([IO.File]::ReadAllText($result)|ConvertFrom-Json).status -eq 'failed')) 'A modified staged payload cannot stop either component'
 Setup-FakeInstall $false $false $false $true
 & $worker
 Check ((($script:events -join '|') -eq 'official|stop-companion|stop-desktop|install|reopen-companion|reopen-desktop') -and ([IO.File]::ReadAllText($exe) -eq 'old executable') -and ([IO.File]::ReadAllText($skinFile) -eq 'old skin')) 'Failure restores managed files and retired skin directories and reopens both previous components'
 Setup-FakeInstall $false $false $false $false
 & $worker
 Check ((($script:events -join '|') -eq 'official|stop-companion|stop-desktop|install|reopen-companion|reopen-desktop') -and (([IO.File]::ReadAllText($result)|ConvertFrom-Json).status -eq 'succeeded') -and !(Test-Path $download) -and !(Test-Path $staged) -and ([IO.File]::ReadAllText($userData) -eq 'private state')) 'Successful in-place update reopens both roles, clears staging and preserves personal data'
 Setup-FakeInstall $false $false $false $false
 $script:companion.companionRunning=$false;$script:desktop=$null
 & $worker
 Check (($script:events -join '|') -eq 'official|stop-companion|install|reopen-manager') 'Previously open independent management window returns without starting its service or the desktop'
}finally{
 $path=(Resolve-Path -LiteralPath $fixture).Path
 if((Split-Path -Parent $path) -ne $base -or (Split-Path -Leaf $path) -cne ('PrismUpdateFixture-'+$id)){throw 'Invalid fixture cleanup target'}
 Remove-Item -LiteralPath $path -Recurse -Force
}
