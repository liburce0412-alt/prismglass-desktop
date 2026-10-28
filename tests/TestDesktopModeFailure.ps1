$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$tokens=$null;$errors=$null
$source=Join-Path $repo 'scripts/runtime/DesktopMode.ps1'
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw $errors[0]}
$function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Write-DesktopModeFailure'},$true)
Invoke-Expression $function.Extent.Text
$fixture=Join-Path $repo ('artifacts/tests/startup-failure-'+[guid]::NewGuid().ToString('N'))
$passed=0
function Check([bool]$Condition,[string]$Name){if(!$Condition){throw "FAIL: $Name"};$script:passed++;"PASS: $Name"}
try{
 New-Item -ItemType Directory -Path (Join-Path $fixture 'LiquidDesktop') -Force | Out-Null
 $message=Write-DesktopModeFailure $fixture 'Beauty' 'Rainmeter 无法启动，请重试。' $false
 $log=Join-Path $fixture 'LiquidDesktop/startup-error.log'
 Check ($message.Contains('未能启动') -and $message.Contains('Rainmeter 无法启动')) 'Actionable launch failure is preserved in the user message'
 Check ($message.Contains($log) -and [IO.File]::ReadAllText($log).Contains('Rainmeter 无法启动')) 'Local diagnostic preserves Unicode and its location'
 [void](Write-DesktopModeFailure $fixture 'Game' '退出失败' $false)
 Check (![IO.File]::ReadAllText($log).Contains('Rainmeter 无法启动')) 'Only the latest failure is retained, without an unbounded log'
 $message=Write-DesktopModeFailure (Join-Path $fixture 'missing') 'Game' 'original failure' $false
 Check ($message.Contains('original failure') -and $message.Contains('无法保存')) 'An unavailable log directory never hides the original failure'
 $icons=$ast.Find({param($node) $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Extent.Text -eq '[DesktopIcons]::SetVisible(!$readiness.primaryEnabled)'},$true)
 Check ($null -ne $icons) 'Beauty icon visibility follows primary desktop enablement, not a saved ordinary-desktop state'
 $visibleExpression=$icons.Arguments[0].Extent.Text
 foreach($enabled in @($true,$false)){$readiness=@{primaryEnabled=$enabled};Check ((Invoke-Expression $visibleExpression) -eq !$enabled) ('Primary enabled='+$enabled+' gives the correct native icon visibility')}
 Check ([IO.File]::ReadAllText($source).Contains("-ArgumentList '--renderer'")) 'Internal renderer launch cannot recurse through the public startup entry'
 Check ($ast.ParamBlock.Parameters.Name.VariablePath.UserPath -contains 'ShowErrors') 'Interactive launches can opt into visible errors; background callers stay nonmodal'
}finally{
 $boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/tests'))+[IO.Path]::DirectorySeparatorChar
 $resolved=[IO.Path]::GetFullPath($fixture);if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped test directory'}
 if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
"Desktop startup failure: $passed checks passed; no desktop state or dialogs changed."
