$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'scripts/Package.ps1'),[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Package script parsing failed'}
$function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-PrivateBundlePath'},$true)
. ([scriptblock]::Create($function.Extent.Text))
foreach($name in @('component-positions','widget-positions','stage-recovery')){if(!(Test-PrivateBundlePath "LiquidDesktop/$name.json")){throw "$name leaked into distributable assets"}}
foreach($name in @('Hourglass','Bonsai')){if(!(Test-PrivateBundlePath "LiquidDesktop/widget-$name.state")){throw "$name state leaked into distributable assets"}}
foreach($name in @('WidgetPositions.cs','DesktopWidgets.cs','functional-icons.js')){if(Test-PrivateBundlePath "LiquidDesktop/$name"){throw "$name was incorrectly excluded"}}
'PASS saved component positions and window recovery remain private; implementation assets remain distributable'

if(!(Test-PrivateBundlePath 'LiquidDesktop/widget-work-context.txt')){throw 'Private widget work context leaked into bundle'}
