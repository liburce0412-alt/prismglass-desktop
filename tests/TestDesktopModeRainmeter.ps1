$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/tests/rainmeter-script-'+[guid]::NewGuid().ToString('N'))
$script:fixtureInstall=Join-Path $fixture '安装 空格'
$skin=Join-Path $fixture '皮肤/PrismGlass'
$executable=Join-Path $fixture 'Rainmeter.exe'
$passed=0
function Check([bool]$Condition,[string]$Name){if(!$Condition){throw "FAIL: $Name"};$script:passed++;"PASS: $Name"}
function Reject([scriptblock]$Action,[string]$Name){$message=$null;try{& $Action | Out-Null}catch{$message=$_.Exception.Message};Check ($null -ne $message) $Name;return $message}
Add-Type @'
using System;
using System.Collections.Generic;
public static class DesktopModeRainmeterFixture {
 public static long Window=1;public static bool Ready=true;public static int ReadyAfter=1,Probes,Sends,FailAt,OpenChecks,CloseAfter,QuitCalls;public static string Error="timeout",OpenSuffix,QuitError;public static string[] OtherSkins=new string[0];
 public static List<string> Commands=new List<string>();
 public static IntPtr Find(string executable){return new IntPtr(Window);}
 public static bool Probe(string executable,uint timeout){if(timeout!=50)throw new Exception("Probe timeout changed");Probes++;return Window!=0&&Ready&&Probes>=ReadyAfter;}
 public static void Send(string executable,string command,uint timeout){if(timeout!=250)throw new Exception("Send timeout changed");Sends++;Commands.Add(command);if(FailAt>0&&Sends>=FailAt){if(Error=="denied")throw new UnauthorizedAccessException("fixture access denied");throw new TimeoutException("fixture control timeout");}}
 public static bool SkinOpen(string file){if(OpenSuffix==null||!file.EndsWith(OpenSuffix,StringComparison.OrdinalIgnoreCase))return false;OpenChecks++;return CloseAfter==0||OpenChecks<=CloseAfter;}
 public static string[] QuitIfIdle(string executable,uint timeout,uint exitTimeout){if(timeout!=250||exitTimeout!=3000)throw new Exception("Quit deadlines changed");QuitCalls++;if(Window==0)throw new InvalidOperationException("fixture control missing");if(QuitError=="denied")throw new UnauthorizedAccessException("fixture exit denied");if(OtherSkins.Length>0)return OtherSkins;Commands.Add("!Quit");if(QuitError=="timeout")throw new TimeoutException("fixture exit timeout");Window=0;return new string[0];}
 public static void Reset(){Window=1;Ready=true;ReadyAfter=1;Probes=Sends=FailAt=OpenChecks=CloseAfter=QuitCalls=0;Error="timeout";OpenSuffix=QuitError=null;OtherSkins=new string[0];Commands.Clear();}
}
public static class DesktopModeShellFixture {
 public static bool Ready=true,Icons,Taskbar,SecondaryTaskbar,IgnoreIcons,IgnoreSecondaryTaskbar;public static int Sets,TaskbarState=1;
 public static void Set(int state,bool visible){Sets++;Taskbar=visible;if(!IgnoreSecondaryTaskbar)SecondaryTaskbar=visible;TaskbarState=state;}
 public static bool ShellReady(){return Ready;}public static bool IconsVisible(){return Icons;}public static bool TaskbarsVisible(){return Taskbar&&SecondaryTaskbar;}public static int State(){return TaskbarState;}
 public static void Reset(){Ready=true;Icons=Taskbar=SecondaryTaskbar=IgnoreIcons=IgnoreSecondaryTaskbar=false;Sets=0;TaskbarState=1;}
}
public static class DesktopModeIconsFixture {public static void SetVisible(bool visible){if(!DesktopModeShellFixture.IgnoreIcons)DesktopModeShellFixture.Icons=visible;}}
'@
function Fixture-Text([string]$Text){return $Text.Replace('[RainmeterCommands]','[DesktopModeRainmeterFixture]').Replace("'RainmeterCommands'","'DesktopModeRainmeterFixture'").Replace('[DesktopTaskbar]','[DesktopModeShellFixture]').Replace("'DesktopTaskbar'","'DesktopModeShellFixture'").Replace('[DesktopIcons]','[DesktopModeIconsFixture]').Replace('$PSScriptRoot','$script:fixtureInstall')}
$tokens=$null;$errors=$null
$desktopAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/runtime/DesktopMode.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw $errors[0]}
foreach($function in $desktopAst.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){Invoke-Expression (Fixture-Text $function.Extent.Text)}
$setupAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/SetupIntegration.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw $errors[0]}
foreach($name in @('Resolve-SetupPath','Read-SetupRecord','Stop-SetupRainmeterSkins')){
 $function=$setupAst.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
 Invoke-Expression (Fixture-Text $function.Extent.Text)
}
function New-FixtureProcess([string]$Path,[bool]$Exited=$false,[int]$Code=0){
 $result=[pscustomobject]@{Path=$Path;HasExited=$Exited;ExitCode=$Code;Disposed=$false}
 $result | Add-Member ScriptMethod Dispose {$this.Disposed=$true} -PassThru
}
function Get-Process {param($Name,$ErrorAction) if($Name -ne 'Rainmeter'){throw 'Unexpected process query'};return $script:existingProcesses}
function Start-Process {
 param($FilePath,$ArgumentList,$WindowStyle,[switch]$PassThru)
 if($FilePath -ne $executable -or $ArgumentList -or $WindowStyle -ne 'Hidden' -or !$PassThru){throw 'Unexpected process launch'}
 $script:launches++;$script:launched=New-FixtureProcess $FilePath $script:earlyExit 17
 if($script:publishWindow){[DesktopModeRainmeterFixture]::Window=1}
 return $script:launched
}
function Stop-Process {throw 'This fixture must not terminate a process'}
function Start-Sleep {param($Milliseconds) $script:waits.Add([int]$Milliseconds)}
function Set-ItemProperty {param($Path,$Name,$Value) if($script:registryDenied){throw [UnauthorizedAccessException]::new('fixture registry denied')};if($Name -ne 'HideIcons' -or $Value -ne 0){throw 'Unexpected registry change'};$script:registryWrites++}
function Reset-Fixture {
 [DesktopModeRainmeterFixture]::Reset();[DesktopModeShellFixture]::Reset()
 $script:existingProcesses=@();$script:launches=0;$script:launched=$null;$script:earlyExit=$false;$script:publishWindow=$true
 $script:waits=[Collections.Generic.List[int]]::new();$script:rainmeterCommandFailure=$null;$script:registryDenied=$false;$script:registryWrites=0
}
try{
 New-Item -ItemType Directory -Path (Join-Path $script:fixtureInstall 'LiquidDesktop'),$skin -Force | Out-Null
 [IO.File]::WriteAllText($executable,'inert fixture')
 [IO.File]::WriteAllText((Join-Path $script:fixtureInstall 'LiquidDesktop/skin-path.txt'),$skin)
 [IO.File]::WriteAllText((Join-Path $script:fixtureInstall 'LiquidDesktop/rainmeter-path.txt'),$executable)
 [IO.File]::WriteAllText((Join-Path $script:fixtureInstall 'CursorTheme.ps1'),'param($Mode)')
 foreach($name in @('Dock','Dock-monitor-2','Dock-monitor-3','Dock-monitor-unsafe','Unowned')){
  $folder=Join-Path $skin $name;New-Item -ItemType Directory -Path $folder -Force | Out-Null
  $content=if($name -eq 'Dock-monitor-2'){'; PrismGlass generated monitor instance'}else{'; user skin'}
  [IO.File]::WriteAllText((Join-Path $folder 'Main.ini'),$content)
 }
 [IO.File]::WriteAllText((Join-Path $skin '.prismglass-owner.json'),(@{product='PrismGlass.Desktop';root=$script:fixtureInstall;files=@('Dock\Main.ini')} | ConvertTo-Json))
 $pending=Join-Path $script:fixtureInstall 'LiquidDesktop/skin-catalog-pending.flag';[IO.File]::WriteAllText($pending,'pending')

 Reset-Fixture
 $wasRunning=Start-PrismGlassRainmeter $executable
 Check ($wasRunning -and $script:launches -eq 0) 'A responsive configured control window needs no process launch'
 Reset-Fixture;[DesktopModeRainmeterFixture]::Window=0
 $stale=New-FixtureProcess $executable;$script:existingProcesses=@($stale)
 [void](Reject {Start-PrismGlassRainmeter $executable 0} 'Residual command processes without a control window cannot satisfy startup readiness')
 Check ($script:launches -eq 0 -and $stale.Disposed) 'Existing configured processes prevent another Rainmeter launch and release inspection handles'
 Reset-Fixture;[DesktopModeRainmeterFixture]::Window=0;[DesktopModeRainmeterFixture]::ReadyAfter=3
 $other=New-FixtureProcess (Join-Path $fixture 'OtherRainmeter.exe');$script:existingProcesses=@($other)
 $wasRunning=Start-PrismGlassRainmeter $executable
 Check (!$wasRunning -and $script:launches -eq 1 -and [DesktopModeRainmeterFixture]::Probes -eq 3 -and $script:launched.Disposed -and $other.Disposed) 'Startup launches once, waits for the configured control window and preserves other installations'
 Reset-Fixture;[DesktopModeRainmeterFixture]::Window=0;$script:publishWindow=$false;$script:earlyExit=$true
 $errorText=Reject {Start-PrismGlassRainmeter $executable 0} 'A new Rainmeter process that exits early fails startup'
 Check ($errorText -match '17' -and $script:launches -eq 1 -and $script:launched.Disposed) 'Early exit reports its code without repeated starts'
 Reset-Fixture;[DesktopModeRainmeterFixture]::Window=0;$script:publishWindow=$false
 [void](Reject {Start-PrismGlassRainmeter $executable 0} 'Startup has a deadline even when the new process stays alive without a control window')
 Check ($script:launches -eq 1 -and $script:launched.Disposed) 'A startup timeout disposes its handle and does not start command processes'
 Reset-Fixture;[DesktopModeRainmeterFixture]::Window=0;$script:existingProcesses=@((New-FixtureProcess ''))
 [void](Reject {Start-PrismGlassRainmeter $executable 0} 'An unreadable process path is a permissions error instead of permission to launch again')
 Check ($script:launches -eq 0) 'Process ownership failure launches nothing'

 Reset-Fixture;[DesktopModeRainmeterFixture]::FailAt=1;[DesktopModeRainmeterFixture]::Error='denied'
 [void](Reject {Invoke-PrismGlassRainmeter $executable '!RefreshApp'} 'A denied command is reported as a failure')
 [void](Reject {Invoke-PrismGlassRainmeter $executable '!ActivateConfig "PrismGlass\Focus" "Main.ini"'} 'A later command cannot bypass the failed desktop-switch state')
 Check ([DesktopModeRainmeterFixture]::Sends -eq 1) 'Failed sends are not retried or replaced by command processes'
 Reset-Fixture
 Update-PrismGlassSkinCatalog $executable $pending $false
 Check ([DesktopModeRainmeterFixture]::Sends -eq 0) 'A newly started Rainmeter does not redundantly refresh its catalog'
 Update-PrismGlassSkinCatalog $executable $pending $true
 Check ([DesktopModeRainmeterFixture]::Commands[0] -eq '!RefreshApp' -and $script:waits.Contains(1000) -and (Test-Path -LiteralPath $pending)) 'Catalog refresh waits for registration and keeps its marker until full desktop readiness'
 Reset-Fixture;[DesktopModeRainmeterFixture]::Ready=$false
 [void](Reject {Update-PrismGlassSkinCatalog $executable $pending $true} 'Accepted catalog refresh still fails when the control window stops responding')
 [void](Reject {Invoke-PrismGlassRainmeter $executable '!ActivateConfig "PrismGlass\Focus" "Main.ini"'} 'A stalled catalog prevents subsequent activation')
 Check ([DesktopModeRainmeterFixture]::Sends -eq 1 -and (Test-Path -LiteralPath $pending)) 'A stalled catalog leaves one sent command and a retry marker'

 Reset-Fixture
 Stop-PrismGlassSkins $executable
 $sent=[DesktopModeRainmeterFixture]::Commands
 Check ($sent.Contains('!DeactivateConfig "PrismGlass\Dock"') -and $sent.Contains('!DeactivateConfig "PrismGlass\Topbar"') -and $sent.Contains('!DeactivateConfig "PrismGlass\Dock-monitor-2"') -and !$sent.Contains('!DeactivateConfig "PrismGlass\Dock-monitor-3"') -and !$sent.Contains('!DeactivateConfig "PrismGlass\Unowned"')) 'Recovery touches named PrismGlass skins and marked generated Docks only'
 Reset-Fixture
 Stop-PrismGlassSkins $executable -KeepCompanion
 $sent=[DesktopModeRainmeterFixture]::Commands
 Check ($sent.Contains('!Hide "PrismGlass\Focus"') -and $sent.Contains('!Hide "PrismGlass\Music"') -and $sent.Contains('!Hide "PrismGlass\System"') -and @($sent | Where-Object {$_ -match 'DeactivateConfig.*\\(Focus|Music|System)"$'}).Count -eq 0) 'Ordinary exit hides the companion collectors and keeps them active'
 Reset-Fixture
 Stop-PrismGlassSkins $executable -CollectorsOnly
 Check (([DesktopModeRainmeterFixture]::Commands -join '|') -ceq '!DeactivateConfig "PrismGlass\Focus"|!DeactivateConfig "PrismGlass\Music"|!DeactivateConfig "PrismGlass\System"') 'Detached companion cleanup touches only the retained collectors'
 Reset-Fixture;[DesktopModeRainmeterFixture]::FailAt=1
 [void](Reject {Stop-PrismGlassSkins $executable} 'A deactivation timeout aborts the remaining skin commands')
 [void](Reject {Stop-PrismGlassSkins $executable} 'Failure rollback cannot enqueue the same deactivations again')
 Check ([DesktopModeRainmeterFixture]::Sends -eq 1) 'Two cleanup attempts after a timeout still send only one command'
 Reset-Fixture;[DesktopModeRainmeterFixture]::OpenSuffix='\Dock\Main.ini';[DesktopModeRainmeterFixture]::CloseAfter=1
 Stop-PrismGlassSkins $executable
 Check ([DesktopModeRainmeterFixture]::OpenChecks -eq 2 -and $script:waits.Contains(100)) 'Delivered deactivation waits for the actual skin window to close'
 Reset-Fixture;[DesktopModeRainmeterFixture]::OpenSuffix='\Dock\Main.ini'
 [void](Reject {Stop-PrismGlassSkins $executable 0} 'Accepted commands cannot report success while a skin window remains open')
 $sends=[DesktopModeRainmeterFixture]::Sends
 [void](Reject {Stop-PrismGlassSkins $executable 0} 'A skin-close deadline also stops later cleanup retries')
 Check ([DesktopModeRainmeterFixture]::Sends -eq $sends) 'Skin-close failure does not enqueue another batch'

 Reset-Fixture;[DesktopModeRainmeterFixture]::Window=0
 $remaining=@(Stop-PrismGlassRainmeter $executable)
 Check (!$remaining.Count -and [DesktopModeRainmeterFixture]::QuitCalls -eq 0 -and $script:launches -eq 0) 'An already absent host exits without launching or sending a command'
 Reset-Fixture
 Stop-PrismGlassSkins $executable;$remaining=@(Stop-PrismGlassRainmeter $executable)
 Check (!$remaining.Count -and [DesktopModeRainmeterFixture]::Window -eq 0 -and [DesktopModeRainmeterFixture]::Commands[-1] -eq '!Quit' -and $script:launches -eq 0) 'A host used only by PrismGlass is quit after its skin closure even when this session did not launch it'
 Reset-Fixture;[DesktopModeRainmeterFixture]::OtherSkins=@('C:\user\illustro\Clock.ini')
 $remaining=@(Stop-PrismGlassRainmeter $executable)
 Check ($remaining.Count -eq 1 -and $remaining[0] -match 'illustro' -and [DesktopModeRainmeterFixture]::Window -eq 1 -and ![DesktopModeRainmeterFixture]::Commands.Contains('!Quit')) 'Other user skins retain their host and return the exact preservation reason'
 Reset-Fixture;[DesktopModeRainmeterFixture]::Window=0;$script:existingProcesses=@((New-FixtureProcess $executable))
 [void](Reject {Stop-PrismGlassRainmeter $executable} 'An existing process without its control window cannot be reported as exited')
 Reset-Fixture;[DesktopModeRainmeterFixture]::Window=0;$script:existingProcesses=@((New-FixtureProcess ''))
 [void](Reject {Stop-PrismGlassRainmeter $executable} 'Unknown process ownership prevents an unverified successful exit')
 Reset-Fixture;[DesktopModeRainmeterFixture]::QuitError='denied'
 [void](Reject {Stop-PrismGlassRainmeter $executable} 'A denied native quit is reported')
 [void](Reject {Stop-PrismGlassRainmeter $executable} 'Denied quit stops subsequent cleanup retries')
 Check ([DesktopModeRainmeterFixture]::QuitCalls -eq 1 -and $script:launches -eq 0) 'Denied quit never falls back to command processes or force termination'
 Reset-Fixture;[DesktopModeRainmeterFixture]::QuitError='timeout'
 [void](Reject {Stop-PrismGlassRainmeter $executable} 'Delivered quit still fails if the native process exit cannot be confirmed')
 [void](Reject {Stop-PrismGlassRainmeter $executable} 'An exit timeout is not retried in the same mode switch')
 Check ([DesktopModeRainmeterFixture]::QuitCalls -eq 1 -and [DesktopModeRainmeterFixture]::Window -eq 1) 'Timed-out host stays intact and receives only one quit attempt'

 Reset-Fixture
 Restore-PrismGlassShell
 Check ([DesktopModeShellFixture]::Icons -and [DesktopModeShellFixture]::TaskbarsVisible() -and [DesktopModeShellFixture]::TaskbarState -eq 2 -and $script:registryWrites -eq 1) 'Ordinary desktop restoration verifies icons and all taskbars'
 Reset-Fixture;[DesktopModeShellFixture]::IgnoreIcons=$true
 [void](Reject {Restore-PrismGlassShell} 'An unsuccessful native icon restore is not reported as success')
 Reset-Fixture;[DesktopModeShellFixture]::IgnoreSecondaryTaskbar=$true
 [void](Reject {Restore-PrismGlassShell} 'A visible primary taskbar cannot hide a failed secondary taskbar restore')
 Reset-Fixture;$script:registryDenied=$true
 [void](Reject {Restore-PrismGlassShell} 'Registry permissions failures remain visible during shell restoration')

 # Execute only the existing failure handler with inert dependencies, never the mode body.
 $outer=$desktopAst.EndBlock.Statements | Where-Object {$_ -is [Management.Automation.Language.TryStatementAst]} | Select-Object -First 1
 $catchText=$outer.CatchClauses[0].Body.Extent.Text
 $catchBlock=[ScriptBlock]::Create((Fixture-Text $catchText.Substring(1,$catchText.Length-2)))
 Reset-Fixture;$script:rainmeterCommandFailure='fixture original send failure'
 $modeLockHeld=$true;$otherInstallationRunning=$false;$startedRenderer=$null;$startedBridge=$null;$beautySkinsActivated=$true;$rainmeter=$executable;$Mode='Game';[switch]$ShowErrors=$false
 $readyPath=Join-Path $script:fixtureInstall 'LiquidDesktop/desktop-ready.flag'
 [void](Reject {try{throw 'fixture original send failure'}catch{& $catchBlock}} 'Desktop-switch failure remains failed after attempting ordinary desktop recovery')
 $log=Join-Path $script:fixtureInstall 'LiquidDesktop/startup-error.log'
 Check ([DesktopModeShellFixture]::Icons -and [DesktopModeRainmeterFixture]::Sends -eq 0 -and [IO.File]::ReadAllText($log).Contains('fixture original send failure')) 'Rollback restores the shell without resending failed Rainmeter commands and retains the reason'
 Reset-Fixture;$script:registryDenied=$true;$beautySkinsActivated=$false
 $errorText=Reject {try{throw 'fixture original failure'}catch{& $catchBlock}} 'Shell recovery failure does not replace the original failure with a success'
 Check ($errorText -match 'fixture original failure' -and $errorText -match 'fixture registry denied' -and [IO.File]::ReadAllText($log).Contains('fixture registry denied')) 'Both the original failure and failed recovery are retained in the error and local log'
 Reset-Fixture;[DesktopModeRainmeterFixture]::QuitError='timeout';$beautySkinsActivated=$false
 [void](Reject {try{Stop-PrismGlassRainmeter $executable}catch{& $catchBlock}} 'Quit timeout retains the failed result after ordinary desktop restoration')
 Check ([DesktopModeShellFixture]::Icons -and [DesktopModeShellFixture]::Taskbar -and [DesktopModeRainmeterFixture]::QuitCalls -eq 1 -and [IO.File]::ReadAllText($log).Contains('fixture exit timeout')) 'Exit failure still restores Windows icons and taskbar without repeating the native quit'
 & {
  Reset-Fixture;$Mode='Beauty';$rollbackRenderer=New-FixtureProcess (Join-Path $script:fixtureInstall 'LiquidDesktop/LiquidDesktop.exe')
  $glassPath=$rollbackRenderer.Path;$hostFile=Join-Path $script:fixtureInstall 'LiquidDesktop/desktop-host.json';$script:detachedRenderer=$null
  function Get-DesktopProcess($Executable){return $rollbackRenderer}
  function Read-DesktopHostState($File,$Process){return [pscustomobject]@{companionRunning=$true}}
  function Stop-DesktopRenderer($Executable,$HostFile,$Process){$script:detachedRenderer=$Process;return $true}
  [void](Reject {try{throw 'fixture reused renderer did not finish startup'}catch{& $catchBlock}} 'Reused renderer startup failure retains its original error')
  Check ($script:detachedRenderer -eq $rollbackRenderer -and [DesktopModeShellFixture]::Icons) 'Failed startup detaches its reused renderer before restoring the ordinary desktop'
 }
 Reset-Fixture;$Mode='CompanionCleanup';[IO.File]::WriteAllText($readyPath,'fixture')
 [void](Reject {try{throw 'fixture collector cleanup failure'}catch{& $catchBlock}} 'Companion cleanup failure stays visible in the local diagnostic')
 Check ($script:registryWrites -eq 0 -and [IO.File]::Exists($readyPath)) 'Collector cleanup failure leaves the Windows shell and readiness marker alone'
 $Mode='Game'
 $exitBranch=$desktopAst.Find({param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text -eq "$('$Mode') -eq 'Game' -or $('$Mode') -eq 'Recovery'"},$true)
 $exitText=$exitBranch.Clauses[0].Item2.Extent.Text
 Check ($exitText.IndexOf('Stop-PrismGlassSkins') -lt $exitText.IndexOf('Stop-PrismGlassRainmeter') -and $exitText.IndexOf('Stop-PrismGlassRainmeter') -lt $exitText.IndexOf('Restore-PrismGlassShell') -and $exitText.Contains('remainingSkins') -and $exitText.Contains('rainmeter-exit.json') -and !$exitText.Contains('Lively')) 'Game and Recovery both quit the configured host, report preserved skins and leave dynamic wallpaper independent'

 Reset-Fixture;$script:existingProcesses=@((New-FixtureProcess $executable))
 Stop-SetupRainmeterSkins $script:fixtureInstall $skin $executable
 Check ([DesktopModeRainmeterFixture]::Sends -eq 2 -and [DesktopModeRainmeterFixture]::Commands.Contains('!DeactivateConfig "PrismGlass\Dock-monitor-2"')) 'Uninstall deactivates only manifest-owned and marked generated skins'
 Reset-Fixture;$script:existingProcesses=@((New-FixtureProcess $executable));[DesktopModeRainmeterFixture]::FailAt=1
 [void](Reject {Stop-SetupRainmeterSkins $script:fixtureInstall $skin $executable} 'Uninstall stops at its first failed command')
 Check ([DesktopModeRainmeterFixture]::Sends -eq 1 -and (Test-Path -LiteralPath (Join-Path $skin 'Dock/Main.ini'))) 'Uninstall command failure preserves the owned skin file'
 Reset-Fixture;$script:existingProcesses=@((New-FixtureProcess $executable));[DesktopModeRainmeterFixture]::OpenSuffix='\Dock\Main.ini'
 [void](Reject {Stop-SetupRainmeterSkins $script:fixtureInstall $skin $executable 0} 'Uninstall cannot proceed while a delivered deactivation leaves the skin open')
 Check (Test-Path -LiteralPath (Join-Path $skin 'Dock/Main.ini')) 'Unconfirmed skin closure preserves files for recovery'

 # Run only the legacy sampler's skin-update tail; no desktop COM sampling occurs.
 $refreshPath=Join-Path $repo 'scripts/runtime/RefreshGlass.ps1'
 $refreshAst=[Management.Automation.Language.Parser]::ParseFile($refreshPath,[ref]$tokens,[ref]$errors)
 if($errors.Count){throw $errors[0]}
 $tailStart=$refreshAst.EndBlock.Statements | Where-Object {$_ -is [Management.Automation.Language.AssignmentStatementAst] -and $_.Left.Extent.Text -eq '$configured'} | Select-Object -First 1
 $refreshTail=[ScriptBlock]::Create((Fixture-Text ([IO.File]::ReadAllText($refreshPath).Substring($tailStart.Extent.StartOffset))))
 $source='fixture wallpaper';$target=Join-Path $script:fixtureInstall 'Glass-Wallpaper-Sample.png'
 Reset-Fixture;$script:existingProcesses=@((New-FixtureProcess (Join-Path $fixture 'OtherRainmeter.exe')))
 & $refreshTail | Out-Null
 Check ([DesktopModeRainmeterFixture]::Sends -eq 0 -and $script:launches -eq 0) 'Legacy wallpaper refresh never commands a different Rainmeter installation'
 Reset-Fixture;$script:existingProcesses=@((New-FixtureProcess $executable))
 & $refreshTail | Out-Null
 Check ([DesktopModeRainmeterFixture]::Sends -eq 5 -and $script:launches -eq 0) 'Legacy wallpaper refresh uses bounded control messages without command processes'
 Reset-Fixture;$script:existingProcesses=@((New-FixtureProcess $executable));[DesktopModeRainmeterFixture]::FailAt=1
 [void](Reject {& $refreshTail} 'Legacy wallpaper refresh stops after the first failed control message')
 Check ([DesktopModeRainmeterFixture]::Sends -eq 1 -and $script:launches -eq 0) 'A stalled legacy refresh cannot create a command-process queue'
}finally{
 $boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/tests'))+[IO.Path]::DirectorySeparatorChar
 $absolute=[IO.Path]::GetFullPath($fixture)
 if(!$absolute.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped its output directory'}
 if(Test-Path -LiteralPath $absolute){Remove-Item -LiteralPath $absolute -Recurse -Force}
}
"Desktop mode Rainmeter: $passed checks passed; all processes, windows and registry operations were fixtures."
