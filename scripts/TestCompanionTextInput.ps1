param([Parameter(Mandatory=$true)][string]$BuildRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo 'artifacts/companion-text-runtime'
$log=Join-Path $repo 'artifacts/experience-polish-20261031/native-text-checks.txt'
$process=$null
if(Test-Path -LiteralPath $fixture){throw 'Text fixture already exists; check for an active probe before removing it.'}
try{
 New-Item -ItemType Directory -Path $fixture -Force | Out-Null
 $app=Join-Path ([IO.Path]::GetFullPath($BuildRoot)) 'LiquidDesktop'
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){Copy-Item -LiteralPath (Join-Path $app $name) -Destination $fixture}
 $sources=@('tests/TestCompanionTextInput.cs','src/LiquidDesktop/CompanionControls.cs','src/LiquidDesktop/CompanionServer.cs') | ForEach-Object {Join-Path $repo $_}
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /nowarn:0649 /platform:x64 "/out:$fixture/TestCompanionTextInput.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Security.dll /r:System.Web.Extensions.dll "/r:$fixture/Microsoft.Web.WebView2.Core.dll" "/r:$fixture/Microsoft.Web.WebView2.WinForms.dll" @sources
 if($LASTEXITCODE -ne 0){throw 'Text probe compilation failed'}
 $process=Start-Process -FilePath (Join-Path $fixture 'TestCompanionTextInput.exe') -ArgumentList ('"'+(Join-Path $fixture 'profile')+'" "'+$log+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(30000)){throw 'Text probe timed out'}
 if($process.ExitCode -ne 0){throw "Text probe failed: $log"}
 Get-Content -LiteralPath $log
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 $expected=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/companion-text-runtime'))
 if([IO.Path]::GetFullPath($fixture) -ne $expected){throw 'Unexpected fixture cleanup path'}
 if(Test-Path -LiteralPath $fixture){Start-Sleep -Milliseconds 500;Remove-Item -LiteralPath $fixture -Recurse -Force}
 if(Test-Path -LiteralPath $fixture){throw 'Text fixture cleanup incomplete'}
}
