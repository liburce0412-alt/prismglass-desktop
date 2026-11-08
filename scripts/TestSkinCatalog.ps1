$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/skin-catalog-'+[guid]::NewGuid().ToString('N'))
$Root=Join-Path $fixture 'installed'
$skin=Join-Path $fixture 'Skins/PrismGlass'
$app=Join-Path $Root 'LiquidDesktop'
$pending=Join-Path $app 'skin-catalog-pending.flag'
try{
 $errors=$null;$tokens=$null
 $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'SetupIntegration.ps1'),[ref]$tokens,[ref]$errors)
 if($errors.Count){throw 'SetupIntegration.ps1 does not parse'}
 $loop=$ast.Find({param($node) $node -is [Management.Automation.Language.ForEachStatementAst] -and $node.Variable.VariablePath.UserPath -eq 'config' -and $node.Extent.Text.Contains('skin-catalog-pending.flag')},$true)
 if(!$loop){throw 'The installed-skin catalog check is missing'}
 $check=[scriptblock]::Create($loop.Extent.Text)
 New-Item -ItemType Directory -Force -Path $app,(Join-Path $Root 'rainmeter/PrismGlass/Dock'),(Join-Path $Root 'rainmeter/PrismGlass/BreathingOrb'),(Join-Path $skin 'Dock') | Out-Null
 foreach($path in @((Join-Path $Root 'rainmeter/PrismGlass/Dock/Main.ini'),(Join-Path $Root 'rainmeter/PrismGlass/BreathingOrb/Main.ini'),(Join-Path $skin 'Dock/Main.ini'))){[IO.File]::WriteAllText($path,'[Rainmeter]')}
 & $check
 if(!(Test-Path -LiteralPath $pending)){throw 'Adding a new card to an existing Dock must mark catalog refresh pending'}
 'PASS an upgrade adding a card marks the existing Rainmeter catalog for refresh'
 Remove-Item -LiteralPath $pending
 New-Item -ItemType Directory -Path (Join-Path $skin 'BreathingOrb'),(Join-Path $Root 'rainmeter/PrismGlass/@Resources') | Out-Null
 [IO.File]::WriteAllText((Join-Path $skin 'BreathingOrb/Main.ini'),'[Rainmeter]')
 & $check
 if(Test-Path -LiteralPath $pending){throw 'Already registered directories and resource-only folders must not force refresh'}
 'PASS unchanged configurations and resource-only folders do not force global refresh'
 # Exercise the real installer functions against an owned local fixture, never the user's skin tree.
 foreach($node in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$true)){
  . ([scriptblock]::Create($node.Extent.Text))
 }
 $owned=@('Starlight\Main.ini','MoodNote\Main.ini','@Resources\AmbientArt\starlight-nebula.png','@Resources\AmbientArt\sunlit-garden.png','BreathingOrb\Main.ini')
 foreach($relative in $owned){$file=Join-Path $skin $relative;New-Item -ItemType Directory -Force -Path (Split-Path $file)|Out-Null;[IO.File]::WriteAllText($file,'owned')}
 [IO.File]::WriteAllText((Join-Path $skin 'MoodNote/personal-note.txt'),'keep')
 Write-SetupRecord (Join-Path $skin '.prismglass-owner.json') @{product='PrismGlass.Desktop';root=$Root;files=$owned}
 Install-SetupSkin $Root $skin 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' 0 0
 foreach($relative in $owned[0..3]){if(Test-Path -LiteralPath (Join-Path $skin $relative)){throw ('Retired owned file survived: '+$relative)}}
 if(!(Test-Path -LiteralPath (Join-Path $skin 'BreathingOrb/Main.ini')) -or [IO.File]::ReadAllText((Join-Path $skin 'MoodNote/personal-note.txt')) -ne 'keep'){throw 'Retirement modified retained files'}
 'PASS owned retired skins and art are removed while the orb and unrelated files survive'
 $unowned=Join-Path $skin 'Starlight/Main.ini';New-Item -ItemType Directory -Force -Path (Split-Path $unowned)|Out-Null;[IO.File]::WriteAllText($unowned,'unowned')
 Install-SetupSkin $Root $skin 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' 0 0
 if([IO.File]::ReadAllText($unowned) -ne 'unowned'){throw 'Retirement deleted a file outside the ownership manifest'}
 'PASS unowned same-name files are left untouched'
}finally{
 $absolute=[IO.Path]::GetFullPath($fixture);$boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
 if(!$absolute.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped artifacts'}
 if(Test-Path -LiteralPath $absolute){Remove-Item -LiteralPath $absolute -Recurse -Force}
 if(Test-Path -LiteralPath $absolute){throw 'Fixture cleanup failed'}
}
