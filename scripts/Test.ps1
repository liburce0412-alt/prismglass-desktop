$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
foreach($case in @(@('TestDesktopVisibility','DesktopVisibilityPolicy'),@('VerifyDockPolicy','DockVisibilityPolicy'),@('TestCursorPreferences','CursorPreferences'),@('TestDockLyrics','DockLyrics'))){
 [string[]]$extra=if($case[0] -eq 'TestDockLyrics'){@((Join-Path $repo 'src/LiquidDesktop/NetEaseSession.cs'))}else{@()}
 & $csc /nologo ("/out:"+(Join-Path $out ($case[0]+'.exe'))) /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Net.Http.dll (Join-Path $repo ('tests/'+$case[0]+'.cs')) (Join-Path $repo ('src/LiquidDesktop/'+$case[1]+'.cs')) @extra
 if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
 if($case[0] -eq 'TestDockLyrics'){
  $fixture=Join-Path $out ('lyrics-'+[guid]::NewGuid().ToString('N'))
  try{& (Join-Path $out ($case[0]+'.exe')) (Join-Path $fixture 'app');$result=$LASTEXITCODE}
  finally{if(Test-Path -LiteralPath $fixture){$resolved=(Resolve-Path -LiteralPath $fixture).Path;if(!$resolved.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped test directory'};Remove-Item -LiteralPath $resolved -Recurse -Force}}
  if($result -ne 0){throw 'Lyrics behavior test failed'}
 }else{& (Join-Path $out ($case[0]+'.exe'))}
 if($LASTEXITCODE -ne 0){throw 'Behavior test failed'}
}
foreach($js in Get-ChildItem "$repo/src/LiquidDesktop" -Filter '*.js'){& node --check $js.FullName;if($LASTEXITCODE -ne 0){throw "Invalid JavaScript: $($js.Name)"}}
foreach($ps in Get-ChildItem "$repo/scripts","$repo/src/LiquidDesktop" -Filter '*.ps1' -Recurse){$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile($ps.FullName,[ref]$tokens,[ref]$errors);if($errors.Count){throw $errors[0]}}
'All behavior and syntax checks passed.'
