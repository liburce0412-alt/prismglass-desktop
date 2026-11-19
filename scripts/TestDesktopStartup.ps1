param([string]$AppAssembly=(Join-Path $PSScriptRoot '../artifacts/tests/runtime-host/RuntimeCompile.dll'))
$ErrorActionPreference='Stop'
$script:desktopFixtureRoot=$PSScriptRoot
$passed=0
function Check([string]$Name,[bool]$Condition){if(!$Condition){throw "FAIL: $Name"};$script:passed++;"PASS: $Name"}
$assembly=[Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($AppAssembly))
$readinessType=$assembly.GetType('DesktopStartupReadiness',$true)
$displayReady=$readinessType.GetMethod('DisplayReady')
function Display-Ready([bool[]]$Values){return [bool]$displayReady.Invoke($null,[object[]]$Values)}
$allReady=[bool[]]@($true,$true,$true,$true,$true,$true,$true,$true,$true,$true)
Check 'Enabled display waits for all components' (Display-Ready $allReady)
foreach($component in @(@('left',2),@('top',4),@('right',6),@('dock',8))){
 $values=[bool[]]$allReady.Clone();$values[$component[1]+1]=$false
 Check ("Enabled {0} blocks incomplete startup" -f $component[0]) (!(Display-Ready $values))
 $values[$component[1]]=$false
 Check ("Disabled {0} does not block startup" -f $component[0]) (Display-Ready $values)
}
$primaryDisabled=[bool[]]@($false,$false,$true,$false,$true,$false,$true,$false,$true,$false)
Check 'Secondary-only setup accepts disabled primary and ready secondary' ((Display-Ready $primaryDisabled) -and (Display-Ready $allReady))
$secondaryMissing=[bool[]]$allReady.Clone();$secondaryMissing[1]=$false
Check 'Missing enabled secondary prevents aggregate readiness' (!((Display-Ready $primaryDisabled) -and (Display-Ready $secondaryMissing)))
Check 'Enabled display still requires its renderer with all features disabled' (!(Display-Ready ([bool[]]@($true,$false,$false,$false,$false,$false,$false,$false,$false,$false))))
Check 'All features disabled accepts painted renderer' (Display-Ready ([bool[]]@($true,$true,$false,$false,$false,$false,$false,$false,$false,$false)))
$publishDue=$readinessType.GetMethod('PublishDue')
Check 'Startup readiness refresh is throttled until 250 ms' (!$publishDue.Invoke($null,@($false,$false,[TimeSpan]::FromMilliseconds(249))))
Check 'Startup readiness can publish before the two-second heartbeat' ($publishDue.Invoke($null,@($false,$false,[TimeSpan]::FromMilliseconds(250))))
Check 'Once initial readiness has published, stable state only uses the heartbeat' (!$publishDue.Invoke($null,@($true,$false,[TimeSpan]::FromMilliseconds(250))) -and $publishDue.Invoke($null,@($true,$true,[TimeSpan]::Zero)))

$handoff=$readinessType.GetMethod('CanHandoff')
Check 'Ready host windows cannot reveal a desktop with unready widgets' (!$handoff.Invoke($null,@($true,$false,$true)))
Check 'Enabled Stage gets its first configuration pass before handoff' (!$handoff.Invoke($null,@($true,$true,$false)))
Check 'The configured usable desktop can hand off immediately' ($handoff.Invoke($null,@($true,$true,$true)))

# Load only pure checks and the cleanup function; never execute desktop mode actions.
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'runtime/DesktopMode.ps1'),[ref]$tokens,[ref]$parseErrors)
Check 'DesktopMode parses' ($parseErrors.Count -eq 0)
if(!('StartupRainmeterProbe' -as [type])){Add-Type 'public static class StartupRainmeterProbe { public static System.IntPtr Find(string executable){return new System.IntPtr(1);} public static bool SkinOpen(string file){return false;} }'}
foreach($name in @('Test-StartupSnapshot','Test-StartupReadiness','Test-StartupPanelProgress','Stop-PrismGlassSkins')){
 $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
 if(!$function){throw "Missing function: $name"}
 Invoke-Expression ($function.Extent.Text.Replace('[RainmeterCommands]','[StartupRainmeterProbe]').Replace('$PSScriptRoot','$script:desktopFixtureRoot'))
}
$now=[datetime]::UtcNow;$started=$now.AddSeconds(-20);$written=$now.AddSeconds(-1)
$state=[pscustomobject]@{version=1;pid=12345;ready=$true}
Check 'Fresh matching process can commit Beauty' (Test-StartupReadiness $state 12345 $started $written $now)
Check 'Different process cannot reuse readiness' (!(Test-StartupReadiness $state 67890 $started $written $now))
Check 'Previous launch readiness cannot commit Beauty' (!(Test-StartupReadiness $state 12345 $started $started.AddSeconds(-1) $now))
Check 'Stalled renderer cannot commit Beauty' (!(Test-StartupReadiness $state 12345 $started $now.AddSeconds(-7) $now))
Check 'Future timestamp cannot commit Beauty' (!(Test-StartupReadiness $state 12345 $started $now.AddSeconds(1) $now))
$state.ready=$false
Check 'Incomplete components keep ordinary desktop' (!(Test-StartupReadiness $state 12345 $started $written $now))
Check 'A fresh incomplete snapshot can report startup progress' (Test-StartupSnapshot $state 12345 $started $written $now)
Check 'A different renderer cannot report startup progress' (!(Test-StartupSnapshot $state 67890 $started $written $now))
$state | Add-Member -NotePropertyName monitors -NotePropertyValue @([pscustomobject]@{enabled=$false;rendererReady=$true;leftReady=$true},[pscustomobject]@{enabled=$true;leftReady='true';topReady=$false})
Check 'A disabled display and a string flag cannot advance startup' (!(Test-StartupPanelProgress $state))
$state.monitors[1].topReady=$true
Check 'A painted component on another display is real progress' (Test-StartupPanelProgress $state)
$state.ready='true'
Check 'Invalid readiness type cannot commit Beauty' (!(Test-StartupReadiness $state 12345 $started $written $now))
$state.ready=$true;$state.version=2
Check 'Unknown readiness schema cannot commit Beauty' (!(Test-StartupReadiness $state 12345 $started $written $now))

# Mock process, skin directory and Rainmeter commands so failure/exit cleanup is inert.
& {
 $script:stoppedSkins=[Collections.Generic.List[string]]::new()
 $script:rainmeterCommandFailure=$null
 function Test-Path {param($LiteralPath) return $true}
 function Get-ChildItem {param($LiteralPath,[switch]$Directory,$Filter) foreach($name in @('Dock-monitor-2','Dock-monitor-3','Dock-monitor-unsafe')){[pscustomobject]@{Name=$name;FullName=(Join-Path $script:desktopFixtureRoot $name)}}}
 function Get-Content {param($LiteralPath,$TotalCount) if($LiteralPath -match 'Dock-monitor-3'){return '; user skin'};return '; PrismGlass generated monitor instance'}
 function Invoke-PrismGlassRainmeter {param($Executable,$Command) if($Command -notmatch '^!DeactivateConfig "([^"]+)"$'){throw 'Unexpected Rainmeter command'};$script:stoppedSkins.Add($Matches[1])}
 Stop-PrismGlassSkins 'fixture.exe'
 Check 'Recovery deactivates all six original skins including Topbar' ((@('Dock','Focus','Music','System','Weather','Topbar') | Where-Object {-not $script:stoppedSkins.Contains('PrismGlass\'+$_)}).Count -eq 0)
 Check 'Recovery deactivates marked secondary Dock' ($script:stoppedSkins.Contains('PrismGlass\Dock-monitor-2'))
 Check 'Recovery preserves unmarked same-name skin' (!$script:stoppedSkins.Contains('PrismGlass\Dock-monitor-3'))
 Check 'Recovery rejects invalid generated instance names' (!$script:stoppedSkins.Contains('PrismGlass\Dock-monitor-unsafe'))
}
"Desktop startup checks: $passed passed (no desktop or user configuration changes)."
