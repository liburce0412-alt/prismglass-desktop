param([string]$SignedMicrosoftFile=(Get-Command pwsh.exe -ErrorAction Stop).Source)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$verifier=Join-Path $repo 'packaging/VerifyDependency.ps1'
$windowsPowerShell=Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
$fixture=Join-Path $repo ('artifacts/tests/dependencies-'+[guid]::NewGuid().ToString('N'))
$passed=0
function Check([bool]$Result,[string]$Message){if(!$Result){throw $Message};$script:passed++}
function Verify([string]$File){$ErrorActionPreference='Continue';& $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $verifier -Path $File *> $null;return $LASTEXITCODE -eq 0}
New-Item -ItemType Directory -Path $fixture -Force|Out-Null
try {
 Check (Verify $SignedMicrosoftFile) 'A real valid Microsoft Corporation signature passes in Windows PowerShell 5.1'
 Check (!(Verify $windowsPowerShell)) 'A different valid signer is rejected even though Windows trusts it'
 $unsigned=Join-Path $fixture 'unsigned.exe';[IO.File]::WriteAllText($unsigned,'not a signed installer')
 Check (!(Verify $unsigned)) 'An unsigned or malformed installer is never accepted'
 Check (!(Verify (Join-Path $fixture 'missing.exe'))) 'A missing installer is rejected'
 $tampered=Join-Path $fixture 'tampered.exe';$bytes=[IO.File]::ReadAllBytes($SignedMicrosoftFile);$bytes[512]=$bytes[512] -bxor 1;[IO.File]::WriteAllBytes($tampered,$bytes)
 Check (!(Verify $tampered)) 'A modified signed executable fails the trust check'
 $iss=Get-Content -LiteralPath (Join-Path $repo 'packaging/PrismGlass.iss') -Encoding UTF8 -Raw
 Check ($iss.Contains('if WizardSilent then Exit;')) 'Silent installs cannot enter the download action'
 Check ($iss.Contains('if MissingDependencies <> '''' then begin Result :=')) 'The final installation gate rejects missing dependencies even when wizard pages are skipped'
 Check ($iss.Contains('DependencyRestartRequired := (Code = 3010) or (Code = 1641);') -and $iss.Contains('/passive /norestart')) 'Restart-required results stop continuation without requesting an automatic restart'
 Check ($iss.Contains('/AUTOSTARTUP=0 /RESTART=0 /VERSION=64 /LANGUAGE=2052')) 'Rainmeter bootstrap keeps startup and launch disabled'
 Check ($iss.Contains("'de6e010f8c28882bc28dcd8f319c899f34dfddec51e1e4dd4e7a23f68ebfe14d'")) 'Rainmeter release is pinned to its verified official SHA-256'
 Check ($iss.Contains('if MicrosoftSigned and not VerifyMicrosoftInstaller(Path) then begin')) 'Microsoft installers are verified before execution'
 Check ($iss.Contains('DependencyDownload.AbortedByUser') -and $iss.Contains('DependencyDownload.Hide;') -and $iss.Contains('DeleteFile(Path);')) 'Cancellation returns from the progress page and removes the downloaded installer'
 Check ($iss.Contains("ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe')") -and $iss.Contains("GetIniString('Runtime', 'PowerShell', '', AddBackslash(Root) + 'installer-runtime.ini')")) 'System PowerShell is available without downloading 7 and valid upgrade runtime is reused'
 Check ($iss.Contains('LoadStringsFromFile') -and $iss.Contains('GetArrayLength(Stored) > 0')) 'Rainmeter saved paths are read as UTF-8 including Unicode without a BOM'
}finally{
 $absolute=[IO.Path]::GetFullPath($fixture);$parent=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/tests'))+[IO.Path]::DirectorySeparatorChar
 if(!$absolute.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase)){throw 'Test fixture escaped its output directory'}
 if(Test-Path -LiteralPath $absolute){Remove-Item -LiteralPath $absolute -Recurse -Force}
}
"Installer dependencies: $passed checks passed; no downloads, installs or system settings changed."
