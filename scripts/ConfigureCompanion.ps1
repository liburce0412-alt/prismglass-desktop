param(
 [Parameter(Mandatory=$true)][string]$Address,
 [ValidateRange(1024,65535)][int]$Port=9443,
 [Parameter(Mandatory=$true)][string]$User,
 [string]$OutputDirectory=(Join-Path $PSScriptRoot 'companion-setup'),
 [switch]$Apply
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion -lt [version]'5.1'){throw 'Windows PowerShell 5.1 or newer is required; no system changes were made.'}
$ip=[Net.IPAddress]::Parse($Address)
$bytes=$ip.GetAddressBytes()
if($bytes.Length -ne 4 -or !($bytes[0] -eq 10 -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168) -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31))){throw 'Choose a private LAN IPv4 address.'}
$Address=$ip.ToString()
$adapter=@(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $Address -ErrorAction Stop)
if($adapter.Count -ne 1){throw 'Address must identify one local network adapter.'}
$profiles=@(Get-NetConnectionProfile -InterfaceIndex $adapter[0].InterfaceIndex -ErrorAction Stop)
if($profiles.Count -ne 1 -or $profiles[0].NetworkCategory -ne 'Private'){throw 'The chosen adapter must already be a trusted Private network. This script does not change network profiles.'}
$profile=$profiles[0]
$sid=([Security.Principal.NTAccount]::new($User)).Translate([Security.Principal.SecurityIdentifier]).Value
$endpoint="${Address}:$Port"
$url="https://$endpoint/"
$rule="PrismGlass-Companion-$Address-$Port"
$appid='{2c370c70-7924-4eb8-a3c4-9c57a8c792bd}'
$sddl="D:(A;;GX;;;$sid)"
$operationId=[guid]::NewGuid().ToString('N')
$description="PrismGlass Companion setup $operationId"
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$plan=[ordered]@{url=$url;user=$User;sid=$sid;urlAcl=$sddl;firewall=[ordered]@{name=$rule;program='System (HTTP.sys)';protocol='TCP';localAddress=$Address;localPort=$Port;remoteAddress='LocalSubnet';profile='Private';interface=$profile.InterfaceAlias};certificates='New dedicated local CA and 365-day IP-SAN server certificate; non-exportable keys in LocalMachine/My';windowsTrust='No trusted-root changes';export=(Join-Path $OutputDirectory 'PrismGlass-Companion-CA.cer');startup='Unchanged; manual enable in PrismGlass settings';apply=[bool]$Apply}
$plan | ConvertTo-Json -Depth 5
function Invoke-CheckedNetsh([string[]]$NetshArguments){
 $output=@(& netsh.exe @NetshArguments 2>&1)
 if($LASTEXITCODE -ne 0){throw "netsh query or change failed (exit $LASTEXITCODE); absence was not assumed. $($output -join ' ')"}
 $output -join [Environment]::NewLine
}
function Assert-UnusedPort([string]$SslState,[string]$AclState){
 if($SslState -match "(?m)(?:\S+:|:\s*)$Port\s*$"){throw 'This port already has an HTTP.sys SSL binding. Choose an unused port; existing bindings will not be replaced.'}
 if($AclState -match "(?i)https?://[^\s/]+:$Port/"){throw 'This port already has an HTTP URL reservation. Choose an unused port; existing reservations will not be replaced.'}
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Choose a new output directory; existing setup records will not be overwritten.'}
if(@(Get-NetFirewallRule -PolicyStore PersistentStore -ErrorAction Stop | Where-Object Name -EQ $rule).Count){throw 'A rule already exists for this endpoint. Review it manually; it will not be replaced.'}
Assert-UnusedPort (Invoke-CheckedNetsh @('http','show','sslcert')) (Invoke-CheckedNetsh @('http','show','urlacl'))
if(@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -EQ $Port).Count){throw 'The selected port already has a listener. Choose an unused port; no process will be stopped.'}
if(!$Apply){Write-Output 'Read-only preflight passed. No certificates, bindings, firewall rules or output files were created.';return}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Apply requires an elevated PowerShell terminal.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$ca=$null;$leaf=$null;$sslAdded=$false;$aclAdded=$false;$firewallAdded=$false
try {
 Import-Module PKI -ErrorAction Stop
 $ca=New-SelfSignedCertificate -Type Custom -Subject "CN=PrismGlass Companion $Address" -FriendlyName "PrismGlass Companion CA $Address" -CertStoreLocation Cert:\LocalMachine\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage CertSign,CRLSign -TextExtension @('2.5.29.19={critical}{text}ca=1&pathlength=0') -NotAfter (Get-Date).AddYears(3)
 $leaf=New-SelfSignedCertificate -Type Custom -Subject "CN=$Address" -FriendlyName "PrismGlass Companion HTTPS $endpoint" -Signer $ca -CertStoreLocation Cert:\LocalMachine\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature,KeyEncipherment -TextExtension @("2.5.29.17={text}IPAddress=$Address",'2.5.29.37={text}1.3.6.1.5.5.7.3.1','2.5.29.19={critical}{text}ca=0') -NotAfter (Get-Date).AddDays(365)
 $san=@($leaf.Extensions | Where-Object {$_.Oid.Value -eq '2.5.29.17'})
 $expectedSan=[byte[]](@(0x30,0x06,0x87,0x04)+$bytes)
 if($san.Count -ne 1 -or [Convert]::ToBase64String($san[0].RawData) -ne [Convert]::ToBase64String($expectedSan)){throw 'The generated certificate does not contain exactly the selected IPv4 SAN.'}
 $eku=[Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new(($leaf.Extensions | Where-Object {$_.Oid.Value -eq '2.5.29.37'}),$false)
 $caConstraints=[Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new(($ca.Extensions | Where-Object {$_.Oid.Value -eq '2.5.29.19'}),$true)
 if(!($eku.EnhancedKeyUsages.Value -contains '1.3.6.1.5.5.7.3.1') -or !$caConstraints.CertificateAuthority -or !$leaf.HasPrivateKey -or !$ca.HasPrivateKey){throw 'The generated certificates are missing required TLS/CA properties.'}
 $chain=[Security.Cryptography.X509Certificates.X509Chain]::new()
 try{
  if([Security.Cryptography.X509Certificates.X509ChainPolicy].GetProperty('CustomTrustStore')){
   $chain.ChainPolicy.TrustMode=[Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust;[void]$chain.ChainPolicy.CustomTrustStore.Add($ca);$chain.ChainPolicy.DisableCertificateDownloads=$true
  }else{
   [void]$chain.ChainPolicy.ExtraStore.Add($ca);$chain.ChainPolicy.VerificationFlags=[Security.Cryptography.X509Certificates.X509VerificationFlags]::AllowUnknownCertificateAuthority
  }
  $chain.ChainPolicy.RevocationMode=[Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
  if(!$chain.Build($leaf) -or $chain.ChainElements.Count -ne 2 -or $chain.ChainElements[0].Certificate.Thumbprint -ne $leaf.Thumbprint -or $chain.ChainElements[1].Certificate.Thumbprint -ne $ca.Thumbprint){throw 'The generated leaf certificate does not verify against this setup CA.'}
 }finally{$chain.Dispose()}
 Export-Certificate -Cert $ca -FilePath (Join-Path $OutputDirectory 'PrismGlass-Companion-CA.cer') | Out-Null
 Invoke-CheckedNetsh @('http','add','sslcert',"ipport=$endpoint","certhash=$($leaf.Thumbprint)","appid=$appid",'certstorename=MY');$sslAdded=$true
 Invoke-CheckedNetsh @('http','add','urlacl',"url=$url","sddl=$sddl");$aclAdded=$true
 New-NetFirewallRule -Name $rule -DisplayName "PrismGlass iPad $endpoint" -Description $description -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -LocalAddress $Address -RemoteAddress LocalSubnet -Profile Private -InterfaceAlias $profile.InterfaceAlias -Program System | Out-Null;$firewallAdded=$true
 $record=[ordered]@{operationId=$operationId;address=$Address;port=$Port;url=$url;user=$User;sid=$sid;firewallRule=$rule;caThumbprint=$ca.Thumbprint;serverThumbprint=$leaf.Thumbprint;expires=$leaf.NotAfter.ToUniversalTime().ToString('o');createdUtc=[DateTime]::UtcNow.ToString('o')}
 $sslReadback=Invoke-CheckedNetsh @('http','show','sslcert',"ipport=$endpoint")
 $aclReadback=Invoke-CheckedNetsh @('http','show','urlacl',"url=$url")
 if(!$sslReadback.Contains($endpoint) -or $sslReadback -notmatch [regex]::Escape($leaf.Thumbprint) -or !$aclReadback.Contains($url) -or !$aclReadback.Contains($sddl)){throw 'HTTP.sys readback did not match the requested certificate and user-only URL ACL.'}
 $createdRule=Get-NetFirewallRule -Name $rule -PolicyStore PersistentStore -ErrorAction Stop
 $filterPort=$createdRule | Get-NetFirewallPortFilter
 $filterAddress=$createdRule | Get-NetFirewallAddressFilter
 $filterApp=$createdRule | Get-NetFirewallApplicationFilter
 $filterInterface=$createdRule | Get-NetFirewallInterfaceFilter
 if($createdRule.Description -ne $description -or $createdRule.Enabled -ne 'True' -or $createdRule.Profile -ne 'Private' -or $createdRule.Direction -ne 'Inbound' -or $createdRule.Action -ne 'Allow' -or $filterPort.Protocol -ne 'TCP' -or [string]$filterPort.LocalPort -ne [string]$Port -or [string]$filterAddress.LocalAddress -ne $Address -or [string]$filterAddress.RemoteAddress -ne 'LocalSubnet' -or $filterApp.Program -ne 'System' -or [string]$filterInterface.InterfaceAlias -ne [string]$profile.InterfaceAlias){throw 'Firewall readback did not match the exact requested scope.'}
 $sslReadback;$aclReadback
 $createdRule | Select-Object Name,Enabled,Profile,Direction,Action
 $record | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'setup-record.json') -Encoding utf8
 Write-Output 'Configured only. PrismGlass has not been started or enabled. Transfer the exported public CA certificate to the iPad and explicitly trust it before opening the HTTPS address.'
} catch {
 # Undo only objects this invocation successfully created; retain the setup folder as failure evidence.
 $failure=$_;$rollbackProblems=[Collections.Generic.List[string]]::new()
 if($firewallAdded){try{$existing=Get-NetFirewallRule -Name $rule -PolicyStore PersistentStore -ErrorAction Stop;if($existing.Description -ne $description){throw 'Rule ownership changed'};$existing | Remove-NetFirewallRule -ErrorAction Stop}catch{$rollbackProblems.Add("Firewall: $($_.Exception.Message)")}}
 if($aclAdded){try{$existing=Invoke-CheckedNetsh @('http','show','urlacl',"url=$url");if(!$existing.Contains($sddl)){throw 'URL ACL ownership changed'};Invoke-CheckedNetsh @('http','delete','urlacl',"url=$url")}catch{$rollbackProblems.Add("URL ACL: $($_.Exception.Message)")}}
 if($sslAdded){try{$existing=Invoke-CheckedNetsh @('http','show','sslcert',"ipport=$endpoint");if($existing -notmatch [regex]::Escape($leaf.Thumbprint)){throw 'SSL binding ownership changed'};Invoke-CheckedNetsh @('http','delete','sslcert',"ipport=$endpoint");$sslAdded=$false}catch{$rollbackProblems.Add("SSL: $($_.Exception.Message)")}}
 if(!$sslAdded){foreach($certificate in @($leaf,$ca)){if($certificate){try{Remove-Item -LiteralPath ("Cert:\LocalMachine\My\"+$certificate.Thumbprint) -DeleteKey -ErrorAction Stop}catch{$rollbackProblems.Add("Certificate $($certificate.Thumbprint): $($_.Exception.Message)")}}}}else{$rollbackProblems.Add('Created certificates retained because SSL binding cleanup was not confirmed.')}
 try{[ordered]@{operationId=$operationId;url=$url;sid=$sid;firewallRule=$rule;caThumbprint=$ca.Thumbprint;serverThumbprint=$leaf.Thumbprint;failure=$failure.Exception.Message;rollbackProblems=@($rollbackProblems)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'failure-record.json') -Encoding utf8}catch{Write-Warning 'Could not write the failure record; retain the console error and setup plan.'}
 foreach($problem in $rollbackProblems){Write-Warning $problem}
 throw $failure
}
