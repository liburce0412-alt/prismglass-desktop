$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
foreach($case in @(@('TestDesktopVisibility','DesktopVisibilityPolicy'),@('VerifyDockPolicy','DockVisibilityPolicy'))){
 & $csc /nologo ("/out:"+(Join-Path $out ($case[0]+'.exe'))) /r:System.Drawing.dll (Join-Path $repo ('tests/'+$case[0]+'.cs')) (Join-Path $repo ('src/LiquidDesktop/'+$case[1]+'.cs'))
 if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
 & (Join-Path $out ($case[0]+'.exe'))
 if($LASTEXITCODE -ne 0){throw 'Behavior test failed'}
}
foreach($js in Get-ChildItem "$repo/src/LiquidDesktop" -Filter '*.js'){& node --check $js.FullName;if($LASTEXITCODE -ne 0){throw "Invalid JavaScript: $($js.Name)"}}
foreach($ps in Get-ChildItem "$repo/scripts" -Filter '*.ps1' -Recurse){$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile($ps.FullName,[ref]$tokens,[ref]$errors);if($errors.Count){throw $errors[0]}}
'All behavior and syntax checks passed.'
