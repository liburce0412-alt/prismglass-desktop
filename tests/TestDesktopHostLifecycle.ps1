$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$file=Join-Path $repo 'scripts/runtime/DesktopMode.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$errors)
if($errors.Count){throw $errors[0]}
foreach($name in 'Read-DesktopHostState','Get-DesktopProcess','Stop-DesktopRenderer'){
 $function=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true)
 Invoke-Expression $function.Extent.Text
}
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('PrismHost-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
try{
 $stateFile=Join-Path $fixture 'desktop-host.json';$quitScript=Join-Path $fixture 'request.ps1'
 $start=[DateTime]::UtcNow
 $process=[pscustomobject]@{Id=1234;StartTime=$start;HasExited=$false;ExitFile=(Join-Path $fixture 'exited')}
 $process|Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {param($milliseconds) return [IO.File]::Exists($this.ExitFile)}
 $state=[ordered]@{pid=$process.Id;startTicks=[string]$start.Ticks;state='running';companionRunning=$true}
 function Save-State(){[IO.File]::WriteAllText($stateFile,($state|ConvertTo-Json -Compress))}
 Save-State
 if((Read-DesktopHostState $stateFile $process).state -ne 'running'){throw 'Matching owner not recognized'}
 $state.startTicks='1';Save-State
 if(Read-DesktopHostState $stateFile $process){throw 'PID reuse accepted'}
 $state.startTicks=[string]$start.Ticks;$state.companionRunning='true';Save-State
 if(Read-DesktopHostState $stateFile $process){throw 'Malformed companion field accepted'}
 $state.companionRunning=$true;Save-State
 $exe=Join-Path $fixture 'LiquidDesktop.exe'
 $session=[Diagnostics.Process]::GetCurrentProcess().SessionId
 $process|Add-Member -NotePropertyMembers @{Path=$exe;SessionId=$session}
 $menu=[pscustomobject]@{Id=5678;StartTime=$start;HasExited=$false;Path=$exe;SessionId=$session}
 function Get-Process($Name){$menu;$process}
 function Get-CimInstance($Class,$Filter){
  $arguments=if($Filter -eq 'ProcessId=5678'){' --dock-menu tile'}elseif($script:legacyHost){' --renderer'}else{' --side-preview'}
  [pscustomobject]@{CommandLine='"'+$exe+'"'+$arguments}
 }
 if((Get-DesktopProcess $exe).Id -ne $process.Id){throw 'An auxiliary process displaced the registered owner'}
 [IO.File]::Delete($stateFile);$script:legacyHost=$true
 if((Get-DesktopProcess $exe).Id -ne $process.Id){throw 'The older renderer was not selected'}
 $script:legacyHost=$false
 if(Get-DesktopProcess $exe){throw 'A preview or menu was mistaken for an owner'}
 Save-State
 @'
 $file=Join-Path $PSScriptRoot 'desktop-host.json';Add-Content (Join-Path $PSScriptRoot 'requests.txt') $args[0]
 if($args[0] -eq '--maintenance-stop'){[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'exited'),'')}
 else{$state=Get-Content $file -Raw|ConvertFrom-Json;$state.state='detached';[IO.File]::WriteAllText($file,($state|ConvertTo-Json -Compress))}
'@ | Set-Content -LiteralPath $quitScript
 if(!(Stop-DesktopRenderer $quitScript $stateFile $process)){throw 'Desktop exit did not retain the live companion'}
 if(!(Stop-DesktopRenderer $quitScript $stateFile $process)){throw 'Second desktop exit did not use the detached acknowledgement'}
 if(Stop-DesktopRenderer $quitScript $stateFile $process $true){throw 'Maintenance unexpectedly retained the host'}
 if((Get-Content (Join-Path $fixture 'requests.txt')) -join '|' -ne '--quit|--maintenance-stop'){throw 'Wrong stop request sequence'}
 'PASS owner selection, legacy renderer fallback, auxiliary process rejection, repeated desktop detach, and explicit full maintenance stop'
}finally{
 $path=(Resolve-Path -LiteralPath $fixture).Path
 if((Split-Path $path -Parent) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or (Split-Path $path -Leaf) -notmatch '^PrismHost-[0-9a-f]{32}$'){throw 'Unexpected lifecycle fixture directory'}
 Remove-Item -LiteralPath $path -Recurse -Force
}
