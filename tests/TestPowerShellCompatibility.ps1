$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -lt 1){throw 'Run this compatibility check with Windows PowerShell 5.1.'}
$repo=Split-Path $PSScriptRoot
$scripts=@(Get-ChildItem (Join-Path $repo 'scripts/runtime') -Filter '*.ps1')+@(Get-Item (Join-Path $repo 'scripts/SetupIntegration.ps1'),(Join-Path $repo 'scripts/Install.ps1'),(Join-Path $repo 'scripts/RepairCompanionVideo.ps1'),(Join-Path $repo 'src/LiquidDesktop/MediaSession.ps1'))
foreach($script in $scripts){
 $tokens=$null;$errors=$null
 [void][Management.Automation.Language.Parser]::ParseInput([IO.File]::ReadAllText($script.FullName),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw $errors[0]}
}
foreach($name in @('TopIsland.cs','LiquidDesktop.cs','FileShelf.cs','WidgetBackend.cs')){
 $text=[IO.File]::ReadAllText((Join-Path $repo ('src/LiquidDesktop/'+$name)))
 if($text.Contains('new ProcessStartInfo("pwsh.exe",')){throw "PowerShell 7 is still required by $name"}
}
& (Join-Path $PSScriptRoot 'TestDesktopModeFailure.ps1')
& (Join-Path $PSScriptRoot 'TestSetupIntegration.ps1')
& (Join-Path $PSScriptRoot 'TestCompanionVideoNetwork.ps1')
'Windows PowerShell 5.1 runtime syntax, failure reporting, installation and cleanup checks passed.'
