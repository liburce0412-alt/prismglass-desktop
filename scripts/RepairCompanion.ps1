param(
 [Parameter(Mandatory=$true)][string]$AppRoot,
 [Parameter(Mandatory=$true)][string]$Address,
 [Parameter(Mandatory=$true)][ValidateRange(1,2147483647)][int]$InterfaceIndex,
 [Parameter(Mandatory=$true)][string]$HostName,
 [ValidateRange(1024,65535)][int]$Port=9443,
 [Parameter(Mandatory=$true)][string]$UserSid,
 [string]$ExistingSetupRecord,
 [switch]$Initialize,
 [switch]$TrustNetwork,
 [switch]$Apply
)
$ErrorActionPreference='Stop'
$script:CompanionScriptPath=$PSCommandPath

# Default: one JSON preflight result, no writes. ready means a valid plan;
# configured means its owned resources already match; needsApproval means Apply
# would change those resources. Apply writes the ownership record last.

function Assert-CompanionPath([string]$Path){
 if($Path -notmatch '\A[A-Za-z]:[\\/]'){throw 'Use a local absolute installation path.'}
 $full=[IO.Path]::GetFullPath($Path)
 if($full.StartsWith('\\') -or $full -notmatch '\A[A-Za-z]:[\\/]'){throw 'Use a local absolute installation path.'}
 $cursor=$full
 while($cursor){if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw ('Linked installation paths are not accepted: '+$cursor)};$cursor=[IO.Path]::GetDirectoryName($cursor)}
 return $full
}
function Read-CompanionRecord([string]$Path){
 $Path=Assert-CompanionPath $Path
 if(!(Test-Path -LiteralPath $Path)){return $null}
 if(!(Test-Path -LiteralPath $Path -PathType Leaf)){throw 'The companion record path is occupied by a directory.'}
 if((Get-Item -LiteralPath $Path).Length -gt 32768){throw 'Companion setup record exceeds the size limit.'}
 try{return ([IO.File]::ReadAllText($Path)|ConvertFrom-Json -ErrorAction Stop)}catch{throw 'The companion setup record is malformed; it was not replaced.'}
}
function Test-CompanionPrivateAddress([string]$Address){
 $ip=$null;if(![Net.IPAddress]::TryParse($Address,[ref]$ip)){return $false};$b=$ip.GetAddressBytes()
 return $b.Length -eq 4 -and ($b[0] -eq 10 -or ($b[0] -eq 192 -and $b[1] -eq 168) -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31))
}
function Assert-CompanionAlias([string]$Alias){
 if([string]::IsNullOrWhiteSpace($Alias) -or $Alias.Length -gt 256 -or $Alias -match '[*?\[\]\r\n\x00]'){throw 'Choose an interface with an exact, non-wildcard Windows name.'}
}
function Invoke-CompanionNetsh([string[]]$Arguments){
 $result=@(& netsh.exe @Arguments 2>&1)
 if($LASTEXITCODE -ne 0){throw ('HTTP.sys query/change failed; absence was not assumed: '+($result -join ' '))}
 return ($result -join "`n")
}
function Test-CompanionEndpointListed([string]$Text,[string]$Endpoint){return [regex]::IsMatch($Text,'(?im)^\s*[^\r\n]+?:\s*'+[regex]::Escape($Endpoint)+'\s*$')}
function Assert-CompanionSsl([string]$Text,[string]$Endpoint,[string]$Thumbprint){
 if(!(Test-CompanionEndpointListed $Text $Endpoint) -or $Text -notmatch ('(?im):\s*'+[regex]::Escape($Thumbprint)+'\s*$') -or $Text -notmatch '(?im):\s*\{2c370c70-7924-4eb8-a3c4-9c57a8c792bd\}\s*$' -or $Text -notmatch '(?im):\s*MY\s*$'){throw 'The SNI binding is not the recorded PrismGlass binding; it was not changed.'}
}
function Assert-CompanionAcl([string]$Text,[string]$Url,[string]$Sddl){
 $found=[regex]::Matches($Text,'(?im)^\s*[^:\r\n]+:\s*(D:[^\r\n]+)\s*$')
 if(!(Test-CompanionEndpointListed $Text $Url) -or $found.Count -ne 1 -or $found[0].Groups[1].Value.Trim() -cne $Sddl){throw 'The URL reservation does not match the recorded user-only permission; it was not changed.'}
}
function Assert-CompanionCertificate($Certificate,[string]$Subject,[bool]$Authority,[bool]$AllowExpired=$false){
 if(!$Certificate -or $Certificate.Subject -ne $Subject -or !$Certificate.HasPrivateKey){throw 'The recorded PrismGlass certificate or its private key is missing.'}
 if($Certificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or (!$AllowExpired -and $Certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow)){throw 'The recorded PrismGlass certificate is not currently valid.'}
 $raw=@($Certificate.Extensions|Where-Object {$_.Oid.Value -eq '2.5.29.19'})
 if($raw.Count -ne 1){throw 'Certificate basic constraints are missing.'}
 $constraints=[Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($raw[0],$true)
 if($constraints.CertificateAuthority -ne $Authority){throw 'Certificate authority constraints do not match the expected role.'}
 if($Authority){
  $usage=@($Certificate.Extensions|Where-Object {$_.Oid.Value -eq '2.5.29.15'})
  if($usage.Count -ne 1 -or !([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new($usage[0],$true).KeyUsages -band [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign)){throw 'The recorded CA cannot sign certificates.'}
 }else{
  $eku=@($Certificate.Extensions|Where-Object {$_.Oid.Value -eq '2.5.29.37'})
  if($eku.Count -ne 1 -or !([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($eku[0],$false).EnhancedKeyUsages.Value -contains '1.3.6.1.5.5.7.3.1')){throw 'The HTTPS certificate is missing server authentication usage.'}
 }
}
function Assert-CompanionDnsCertificate($Certificate,[string]$HostName,$Authority,[bool]$AllowExpired=$false){
 Assert-CompanionCertificate $Certificate ('CN='+$HostName) $false $AllowExpired
 $name=[Text.Encoding]::ASCII.GetBytes($HostName);$expected=[byte[]](@(0x30,($name.Length+2),0x82,$name.Length)+$name)
 $san=@($Certificate.Extensions|Where-Object {$_.Oid.Value -eq '2.5.29.17'})
 if($san.Count -ne 1 -or [Convert]::ToBase64String($expected) -cne [Convert]::ToBase64String($san[0].RawData)){throw 'The HTTPS certificate must contain exactly the selected fixed DNS name.'}
 $chain=[Security.Cryptography.X509Certificates.X509Chain]::new()
 try{
  $chain.ChainPolicy.RevocationMode=[Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
  if([Security.Cryptography.X509Certificates.X509ChainPolicy].GetProperty('CustomTrustStore')){
   $chain.ChainPolicy.TrustMode=[Security.Cryptography.X509Certificates.X509ChainTrustMode]::CustomRootTrust;[void]$chain.ChainPolicy.CustomTrustStore.Add($Authority);$chain.ChainPolicy.DisableCertificateDownloads=$true
  }else{
   # Framework 4.x permits only the missing root trust; the exact two-certificate chain is pinned below.
   if(@($Certificate.Extensions|Where-Object {$_.Oid.Value -eq '1.3.6.1.5.5.7.1.1'}).Count){throw 'The local certificate must not contain external issuer download locations.'}
   [void]$chain.ChainPolicy.ExtraStore.Add($Authority);$chain.ChainPolicy.VerificationFlags=[Security.Cryptography.X509Certificates.X509VerificationFlags]::AllowUnknownCertificateAuthority
  }
  if($AllowExpired){$chain.ChainPolicy.VerificationFlags=$chain.ChainPolicy.VerificationFlags -bor [Security.Cryptography.X509Certificates.X509VerificationFlags]::IgnoreNotTimeValid}
  if(!$chain.Build($Certificate) -or $chain.ChainElements.Count -ne 2 -or $chain.ChainElements[0].Certificate.Thumbprint -ne $Certificate.Thumbprint -or $chain.ChainElements[1].Certificate.Thumbprint -ne $Authority.Thumbprint){throw 'The HTTPS certificate does not verify against the recorded PrismGlass CA.'}
 }finally{$chain.Dispose()}
}
function Get-CompanionCertificate([string]$Thumbprint){
 # Enumerating public metadata distinguishes an absent certificate from an unreadable store.
 $matches=@(Get-ChildItem -LiteralPath Cert:\LocalMachine\My -ErrorAction Stop|Where-Object Thumbprint -EQ $Thumbprint)
 if($matches.Count -gt 1){throw 'Duplicate certificate thumbprints were returned by the machine store.'}
 if($matches.Count){return $matches[0]}
 return $null
}
function Test-CompanionAdministrator {
 $principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
 return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Assert-CompanionInstalledScript([string]$AppRoot){
 $actual=Assert-CompanionPath $script:CompanionScriptPath
 $expected=Join-Path ([IO.Path]::GetDirectoryName($AppRoot)) 'RepairCompanion.ps1'
 if(![string]::Equals($actual,$expected,[StringComparison]::OrdinalIgnoreCase)){throw 'Apply must run from RepairCompanion.ps1 beside the installed LiquidDesktop directory.'}
}
function Assert-CompanionNonExportableKey($Certificate){
 $key=[Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Certificate)
 try{
  if($key -is [Security.Cryptography.RSACng]){if($key.Key.ExportPolicy -ne [Security.Cryptography.CngExportPolicies]::None){throw 'The PrismGlass private key must be non-exportable.'}}
  elseif($key -is [Security.Cryptography.RSACryptoServiceProvider]){if($key.CspKeyContainerInfo.Exportable){throw 'The PrismGlass private key must be non-exportable.'}}
  else{throw 'The PrismGlass RSA private key provider could not be verified.'}
 }finally{if($key){$key.Dispose()}}
}
function Get-CompanionRuleScope($Rule){
 $p=$Rule|Get-NetFirewallPortFilter -ErrorAction Stop;$a=$Rule|Get-NetFirewallAddressFilter -ErrorAction Stop;$e=$Rule|Get-NetFirewallApplicationFilter -ErrorAction Stop;$i=$Rule|Get-NetFirewallInterfaceFilter -ErrorAction Stop
 return [pscustomobject]@{enabled=[string]$Rule.Enabled;profile=[string]$Rule.Profile;direction=[string]$Rule.Direction;action=[string]$Rule.Action;protocol=[string]$p.Protocol;localPort=@($p.LocalPort);remotePort=@($p.RemotePort);localAddress=@($a.LocalAddress);remoteAddress=@($a.RemoteAddress);program=[string]$e.Program;interfaceAlias=@($i.InterfaceAlias)}
}
function Test-CompanionRuleScope($Scope,[string]$Alias,[int]$Port,[string]$Protocol='TCP',[string]$Program='System'){
 $protocolNumber=if($Protocol -eq 'UDP'){'17'}else{'6'}
 return $Scope.enabled -eq 'True' -and $Scope.profile -eq 'Private' -and $Scope.direction -eq 'Inbound' -and $Scope.action -eq 'Allow' -and $Scope.protocol -in @($Protocol,$protocolNumber) -and ($Scope.localPort -join ',') -eq [string]$Port -and ($Scope.remotePort -join ',') -eq 'Any' -and ($Scope.localAddress -join ',') -eq 'Any' -and ($Scope.remoteAddress -join ',') -eq 'LocalSubnet' -and $Scope.program -eq $Program -and ($Scope.interfaceAlias -join ',') -eq $Alias
}
function Write-CompanionNetworkRecord([string]$Path,$Record){
 $Path=Assert-CompanionPath $Path;$directory=Assert-CompanionPath ([IO.Path]::GetDirectoryName($Path));[IO.Directory]::CreateDirectory($directory)|Out-Null
 $temporary=Join-Path $directory ('companion-network-'+[guid]::NewGuid().ToString('N')+'.tmp')
 try{[IO.File]::WriteAllText($temporary,($Record|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false));[void](Assert-CompanionPath $Path);if([IO.File]::Exists($Path)){[IO.File]::Replace($temporary,$Path,[NullString]::Value)}else{[IO.File]::Move($temporary,$Path)}}finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}}
}
function Invoke-CompanionRepair {
 param([string]$AppRoot,[string]$Address,[int]$InterfaceIndex,[string]$HostName,[int]$Port=9443,[string]$UserSid,[string]$ExistingSetupRecord,[switch]$Initialize,[switch]$TrustNetwork,[switch]$Apply)
 if($PSVersionTable.PSVersion -lt [version]'5.1'){throw 'Windows PowerShell 5.1 or newer is required.'}
 $shellName=if($PSVersionTable.PSVersion.Major -ge 7){'pwsh.exe'}else{'powershell.exe'}
 $powerShellPath=[IO.Path]::GetFullPath((Join-Path $PSHOME $shellName))
 if(!(Test-Path -LiteralPath $powerShellPath -PathType Leaf)){throw 'The current PowerShell executable could not be located.'}
 if($HostName -cnotmatch '\Aprismglass-[0-9a-f]{8}\.local\z' -or $Port -lt 1024 -or $Port -gt 65535){throw 'Choose the exact PrismGlass fixed name and a valid port.'}
 if(!(Test-CompanionPrivateAddress $Address) -or $InterfaceIndex -lt 1){throw 'Choose a local private IPv4 address and its interface.'}
 $Address=[Net.IPAddress]::Parse($Address).ToString();$sid=[Security.Principal.SecurityIdentifier]::new($UserSid)
 if($sid.Value -ne $UserSid -or $UserSid -notmatch '\AS-1-(5-21|12-1)-\d+-\d+-\d+-\d+\z'){throw 'Use the normal Windows user SID, not a group or service identity.'}
 $AppRoot=(Assert-CompanionPath $AppRoot).TrimEnd('\')
 if([IO.Path]::GetFileName($AppRoot) -ne 'LiquidDesktop' -or !(Test-Path -LiteralPath (Join-Path $AppRoot 'LiquidDesktop.exe') -PathType Leaf) -or !(Test-Path -LiteralPath (Join-Path $AppRoot 'CompanionServer.cs') -PathType Leaf)){throw 'AppRoot must be the existing PrismGlass LiquidDesktop installation.'}
 $file=Join-Path $AppRoot 'runtime-data/companion-network.json';$record=Read-CompanionRecord $file;$owner='PrismGlass.Companion.Network.v1';$appid='{2c370c70-7924-4eb8-a3c4-9c57a8c792bd}'
 if($Initialize -and ($record -or $ExistingSetupRecord)){throw 'First-time setup cannot replace an existing connection or its trusted CA. Use the existing configuration.'}
 $url="https://${HostName}:$Port/";$endpoint="${HostName}:$Port";$sddl="D:(A;;GX;;;$UserSid)";$ruleName="PrismGlass-Companion-$HostName-$Port";$description="PrismGlass Companion fixed name $endpoint; user $UserSid"
 $mdnsRuleName="PrismGlass-Companion-mDNS-$HostName-$Port";$mdnsDescription="PrismGlass Companion mDNS $endpoint; user $UserSid"
 $mdnsProgram=Assert-CompanionPath (Join-Path $AppRoot 'LiquidDesktop.exe')
 if($record -and ($record.schemaVersion -ne 1 -or $record.owner -ne $owner -or $record.appRoot -ne $AppRoot -or $record.hostName -cne $HostName -or $record.port -ne $Port -or $record.userSid -ne $UserSid -or $record.firewallRule -ne $ruleName -or $record.caThumbprint -notmatch '\A[0-9A-Fa-f]{40}\z' -or $record.serverThumbprint -notmatch '\A[0-9A-Fa-f]{40}\z' -or $record.caSubject -notmatch '\ACN=PrismGlass Companion [0-9.]+\z')){throw 'The existing network record belongs to a different configuration or is malformed.'}
 if($record){$recordId=[guid]::Empty;if(![guid]::TryParse([string]$record.interfaceId,[ref]$recordId)){throw 'The recorded interface identity is malformed.'};Assert-CompanionAlias ([string]$record.interfaceAlias)}
 if($record -and $record.PSObject.Properties.Name -contains 'mdnsFirewallRule' -and $record.mdnsFirewallRule -cne $mdnsRuleName){throw 'The recorded mDNS firewall identity is malformed.'}
 try{
  $adapter=@(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $Address -ErrorAction Stop|Where-Object InterfaceIndex -EQ $InterfaceIndex)
  $interface=@(Get-NetAdapter -InterfaceIndex $InterfaceIndex -Physical -ErrorAction Stop);$profiles=@(Get-NetConnectionProfile -InterfaceIndex $InterfaceIndex -ErrorAction Stop)
 }catch{throw ('Could not read the selected Windows network; configuration was not assumed: '+$_.Exception.Message)}
 if($adapter.Count -ne 1 -or $interface.Count -ne 1 -or $profiles.Count -ne 1 -or [string]$interface[0].Status -ne 'Up'){throw 'The selected IPv4 address must belong to one active network interface.'}
 $interfaceId=([guid]$interface[0].InterfaceGuid).ToString('D');$alias=[string]$interface[0].Name;$profile=[string]$profiles[0].NetworkCategory
 Assert-CompanionAlias $alias
 if($profile -notin @('Private','Public')){throw 'Domain-managed or unknown networks need administrator-managed configuration.'}
 if($profile -eq 'Public' -and !$TrustNetwork){throw 'This network is Public. Confirm that this exact network is trusted before using -TrustNetwork.'}
 $setupRecord=$null;$ca=$null;$caThumb='';$caSubject=''
 if($record){$caThumb=[string]$record.caThumbprint;$caSubject=[string]$record.caSubject;$setupRecord=[string]$record.setupRecord}
 elseif($Initialize){$caSubject='CN=PrismGlass Companion '+$Address}
 else{
  if(!$ExistingSetupRecord){throw 'Choose first-time setup or select the previous setup-record.json to reuse the trusted CA.'}
  $setupRecord=Assert-CompanionPath $ExistingSetupRecord;$legacy=Read-CompanionRecord $setupRecord
  if(!$legacy -or !(Test-CompanionPrivateAddress ([string]$legacy.address)) -or $legacy.caThumbprint -notmatch '\A[0-9A-Fa-f]{40}\z' -or $legacy.serverThumbprint -notmatch '\A[0-9A-Fa-f]{40}\z' -or $legacy.sid -ne $UserSid -or $legacy.url -ne "https://$($legacy.address):$($legacy.port)/"){throw 'The previous setup record does not identify this user and a PrismGlass LAN endpoint.'}
  $operation=[guid]::Empty;if(![guid]::TryParse([string]$legacy.operationId,[ref]$operation)){throw 'The previous setup operation identity is invalid.'}
  $caThumb=[string]$legacy.caThumbprint;$caSubject='CN=PrismGlass Companion '+$legacy.address
 }
 if(!$Initialize){
  $ca=Get-CompanionCertificate $caThumb
  Assert-CompanionCertificate $ca $caSubject $true
  if($ca.FriendlyName -ne $caSubject.Replace('CN=PrismGlass Companion ','PrismGlass Companion CA ') -or $ca.Subject -ne $ca.Issuer){throw 'The selected CA is not the recorded dedicated PrismGlass CA.'}
  if($ca.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow.AddDays(7)){throw 'The trusted CA expires within seven days. Renewing device trust needs a separate setup.'}
 }
 $sslAll=Invoke-CompanionNetsh @('http','show','sslcert');$aclAll=Invoke-CompanionNetsh @('http','show','urlacl')
 $hasSsl=Test-CompanionEndpointListed $sslAll $endpoint;$hasAcl=Test-CompanionEndpointListed $aclAll $url
 if(($hasSsl -or $hasAcl) -and !$record){throw 'An unrecorded SNI binding or URL reservation already exists; it will not be adopted or replaced.'}
 if($hasSsl){Assert-CompanionSsl (Invoke-CompanionNetsh @('http','show','sslcert',"hostnameport=$endpoint")) $endpoint $record.serverThumbprint}
 if($hasAcl){Assert-CompanionAcl (Invoke-CompanionNetsh @('http','show','urlacl',"url=$url")) $url $sddl}
 $allRules=@(Get-NetFirewallRule -PolicyStore PersistentStore -ErrorAction Stop);$rules=@($allRules|Where-Object Name -EQ $ruleName);$oldScope=$null
 if($rules.Count -gt 1){throw 'Duplicate firewall rule identities were found.'}
 if($rules.Count){
  if(!$record -or $rules[0].Description -cne $description){throw 'An unrecorded firewall rule already exists; it will not be changed.'}
  $oldScope=Get-CompanionRuleScope $rules[0]
  if(!(Test-CompanionRuleScope $oldScope ([string]$record.interfaceAlias) $Port)){throw 'The existing firewall rule differs from its recorded narrow scope; review it before repair.'}
 }
 $mdnsRules=@($allRules|Where-Object Name -EQ $mdnsRuleName);$oldMdnsScope=$null
 if($mdnsRules.Count -gt 1){throw 'Duplicate mDNS firewall rule identities were found.'}
 if($mdnsRules.Count){
  if(!$record -or $record.mdnsFirewallRule -cne $mdnsRuleName -or $mdnsRules[0].Description -cne $mdnsDescription){throw 'An unrecorded mDNS firewall rule already exists; it will not be changed.'}
  $oldMdnsScope=Get-CompanionRuleScope $mdnsRules[0]
  if(!(Test-CompanionRuleScope $oldMdnsScope ([string]$record.interfaceAlias) 5353 'UDP' $mdnsProgram)){throw 'The existing mDNS firewall rule differs from its recorded narrow scope; review it before repair.'}
 }
 $leaf=$null;$certificateAction='create'
 if($record){
  $leaf=Get-CompanionCertificate ([string]$record.serverThumbprint)
  $certificateAction='renew'
  if($leaf){
   if($leaf.FriendlyName -ne ('PrismGlass Companion HTTPS '+$endpoint)){throw 'The recorded HTTPS certificate is not owned by this fixed endpoint.'}
   Assert-CompanionDnsCertificate $leaf $HostName $ca $true
   if($leaf.NotAfter.ToUniversalTime() -gt [DateTime]::UtcNow.AddDays(7)){$certificateAction='reuse'}
  }
 }
 $ruleAction=if(!$rules.Count){'create'}elseif(($oldScope.interfaceAlias -join ',') -ne $alias){'move-interface'}else{'reuse'}
 $mdnsRuleAction=if(!$mdnsRules.Count){'create'}elseif(($oldMdnsScope.interfaceAlias -join ',') -ne $alias){'move-interface'}else{'reuse'}
 $mdns=[ordered]@{action=$mdnsRuleAction;rulesPresent=$mdnsRules.Count -gt 0;ruleName=$mdnsRuleName;program=$mdnsProgram;profile='Private';interfaceAlias=$alias;localAddress='Any';remoteAddress='LocalSubnet';protocol='UDP';localPort=5353;remotePort='Any';direction='Inbound';note='App-specific fixed-name responder. Existing Windows mDNS rules remain unchanged; verify discovery from the iPad.'}
 $configured=$record -and $hasSsl -and $hasAcl -and $certificateAction -eq 'reuse' -and $ruleAction -eq 'reuse' -and $mdnsRuleAction -eq 'reuse' -and $profile -eq 'Private' -and $record.interfaceId -eq $interfaceId
 $plan=[ordered]@{appRoot=$AppRoot;hostName=$HostName;url=$url;address=$Address;port=$Port;interfaceIndex=$InterfaceIndex;interfaceId=$interfaceId;interfaceAlias=$alias;networkName=[string]$profiles[0].Name;profile=$profile;changeNetworkToPrivate=($profile -eq 'Public');certificateAction=$certificateAction;caAction=if($Initialize){'create'}else{'reuse'};caThumbprint=$caThumb;sniAction=if($hasSsl){if($certificateAction -eq 'reuse'){'reuse'}else{'renew'}}else{'create'};urlAclAction=if($hasAcl){'reuse'}else{'create'};firewallAction=$ruleAction;firewall=[ordered]@{profile='Private';interfaceAlias=$alias;localAddress='Any';remoteAddress='LocalSubnet';protocol='TCP';localPort=$Port;program='System'};mdns=$mdns;windowsRootTrust='unchanged';oldIpBindings='preserved';pairing='preserved; first use of the fixed name needs pairing in Safari'}
 $issues=@()
 if(!$Apply){return [pscustomobject]@{schemaVersion=1;ready=($issues.Count -eq 0);needsApproval=!$configured;configured=[bool]$configured;issues=$issues;plan=$plan}}
 if($issues.Count){throw ($issues -join ' ')}
 Assert-CompanionInstalledScript $AppRoot
 if(!(Test-CompanionAdministrator)){throw 'Apply requires administrator confirmation.'}
 if($ca){Assert-CompanionNonExportableKey $ca}
 $mutex=[Threading.Mutex]::new($false,('Local\PrismGlass.Companion.Setup.'+$HostName+'.'+$Port));$locked=$false
 $newCa=$null;$newLeaf=$null;$sslAdded=$false;$sslUpdated=$false;$aclAdded=$false;$ruleAdded=$false;$ruleUpdated=$false;$mdnsRuleAdded=$false;$mdnsRuleUpdated=$false;$profileChanged=$false;$rollback=[Collections.Generic.List[string]]::new()
 try{
  $locked=$mutex.WaitOne(0);if(!$locked){throw 'Another companion setup is already running.'}
  if($profile -eq 'Public'){Set-NetConnectionProfile -InterfaceIndex $InterfaceIndex -NetworkCategory Private -ErrorAction Stop;$profileChanged=$true}
  if($Initialize){
   Import-Module PKI -ErrorAction Stop
   $newCa=New-SelfSignedCertificate -Type Custom -Subject $caSubject -FriendlyName ('PrismGlass Companion CA '+$Address) -CertStoreLocation Cert:\LocalMachine\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage CertSign,CRLSign -TextExtension @('2.5.29.19={critical}{text}ca=1&pathlength=0') -NotAfter ([DateTime]::UtcNow.AddYears(3))
   Assert-CompanionCertificate $newCa $caSubject $true;Assert-CompanionNonExportableKey $newCa;$ca=$newCa
  }
  if($certificateAction -ne 'reuse'){
   Import-Module PKI -ErrorAction Stop
   $expiry=[DateTime]::UtcNow.AddDays(365);if($expiry -gt $ca.NotAfter.ToUniversalTime()){$expiry=$ca.NotAfter.ToUniversalTime()}
   $newLeaf=New-SelfSignedCertificate -Type Custom -Subject ('CN='+$HostName) -FriendlyName ('PrismGlass Companion HTTPS '+$endpoint) -Signer $ca -CertStoreLocation Cert:\LocalMachine\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature,KeyEncipherment -TextExtension @("2.5.29.17={text}DNS=$HostName",'2.5.29.37={text}1.3.6.1.5.5.7.3.1','2.5.29.19={critical}{text}ca=0') -NotAfter $expiry
   Assert-CompanionDnsCertificate $newLeaf $HostName $ca;Assert-CompanionNonExportableKey $newLeaf;$leaf=$newLeaf
  }else{Assert-CompanionNonExportableKey $leaf}
  if(!$hasSsl){[void](Invoke-CompanionNetsh @('http','add','sslcert',"hostnameport=$endpoint","certhash=$($leaf.Thumbprint)","appid=$appid",'certstorename=MY'));$sslAdded=$true}
  elseif($certificateAction -ne 'reuse'){Assert-CompanionSsl (Invoke-CompanionNetsh @('http','show','sslcert',"hostnameport=$endpoint")) $endpoint $record.serverThumbprint;[void](Invoke-CompanionNetsh @('http','update','sslcert',"hostnameport=$endpoint","certhash=$($leaf.Thumbprint)","appid=$appid",'certstorename=MY'));$sslUpdated=$true}
  if(!$hasAcl){[void](Invoke-CompanionNetsh @('http','add','urlacl',"url=$url","sddl=$sddl"));$aclAdded=$true}
  if($ruleAction -eq 'create'){New-NetFirewallRule -Name $ruleName -DisplayName ('PrismGlass iPad '+$HostName) -Description $description -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -RemotePort Any -LocalAddress Any -RemoteAddress LocalSubnet -Profile Private -InterfaceAlias $alias -Program System -Enabled True -ErrorAction Stop|Out-Null;$ruleAdded=$true}
  elseif($ruleAction -eq 'move-interface'){
   $before=@(Get-NetFirewallRule -Name $ruleName -PolicyStore PersistentStore -ErrorAction Stop)
   if($before.Count -ne 1 -or $before[0].Description -cne $description -or !(Test-CompanionRuleScope (Get-CompanionRuleScope $before[0]) ([string]$record.interfaceAlias) $Port)){throw 'Firewall ownership changed during setup.'}
   Set-NetFirewallRule -Name $ruleName -PolicyStore PersistentStore -InterfaceAlias $alias -ErrorAction Stop|Out-Null;$ruleUpdated=$true
  }
  if($mdnsRuleAction -eq 'create'){
   New-NetFirewallRule -Name $mdnsRuleName -DisplayName ('PrismGlass iPad mDNS '+$HostName) -Description $mdnsDescription -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5353 -RemotePort Any -LocalAddress Any -RemoteAddress LocalSubnet -Profile Private -InterfaceAlias $alias -Program $mdnsProgram -Enabled True -ErrorAction Stop|Out-Null;$mdnsRuleAdded=$true
  }elseif($mdnsRuleAction -eq 'move-interface'){
   $before=@(Get-NetFirewallRule -Name $mdnsRuleName -PolicyStore PersistentStore -ErrorAction Stop)
   if($before.Count -ne 1 -or $before[0].Description -cne $mdnsDescription -or !(Test-CompanionRuleScope (Get-CompanionRuleScope $before[0]) ([string]$record.interfaceAlias) 5353 'UDP' $mdnsProgram)){throw 'mDNS firewall ownership changed during setup.'}
   Set-NetFirewallRule -Name $mdnsRuleName -PolicyStore PersistentStore -InterfaceAlias $alias -ErrorAction Stop|Out-Null;$mdnsRuleUpdated=$true
  }
  Assert-CompanionSsl (Invoke-CompanionNetsh @('http','show','sslcert',"hostnameport=$endpoint")) $endpoint $leaf.Thumbprint
  Assert-CompanionAcl (Invoke-CompanionNetsh @('http','show','urlacl',"url=$url")) $url $sddl
  $actualRule=@(Get-NetFirewallRule -Name $ruleName -PolicyStore PersistentStore -ErrorAction Stop)
  if($actualRule.Count -ne 1 -or $actualRule[0].Description -cne $description -or !(Test-CompanionRuleScope (Get-CompanionRuleScope $actualRule[0]) $alias $Port)){throw 'The firewall readback did not match the approved narrow scope.'}
  $actualMdns=@(Get-NetFirewallRule -Name $mdnsRuleName -PolicyStore PersistentStore -ErrorAction Stop)
  if($actualMdns.Count -ne 1 -or $actualMdns[0].Description -cne $mdnsDescription -or !(Test-CompanionRuleScope (Get-CompanionRuleScope $actualMdns[0]) $alias 5353 'UDP' $mdnsProgram)){throw 'The mDNS firewall readback did not match the approved narrow scope.'}
  $current=@(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $Address -ErrorAction Stop|Where-Object InterfaceIndex -EQ $InterfaceIndex);$currentProfile=@(Get-NetConnectionProfile -InterfaceIndex $InterfaceIndex -ErrorAction Stop)
  if($current.Count -ne 1 -or $currentProfile.Count -ne 1 -or [string]$currentProfile[0].NetworkCategory -ne 'Private'){throw 'The network changed during setup; retry after it settles.'}
  $saved=[ordered]@{schemaVersion=1;owner=$owner;appRoot=$AppRoot;powerShellPath=$powerShellPath;hostName=$HostName;port=$Port;interfaceId=$interfaceId;interfaceAlias=$alias;interfaceIndex=$InterfaceIndex;address=$Address;userSid=$UserSid;caThumbprint=$ca.Thumbprint;caSubject=$caSubject;serverThumbprint=$leaf.Thumbprint;firewallRule=$ruleName;mdnsFirewallRule=$mdnsRuleName;setupRecord=$setupRecord;createdUtc=if($record){$record.createdUtc}else{[DateTime]::UtcNow.ToString('o')};updatedUtc=[DateTime]::UtcNow.ToString('o')}
  Write-CompanionNetworkRecord $file $saved
  return [pscustomobject]@{schemaVersion=1;ready=$true;needsApproval=$false;configured=$true;issues=@();plan=$plan;recordPath=$file}
 }catch{
  $failure=$_
  if($mdnsRuleAdded -or $mdnsRuleUpdated){try{$r=@(Get-NetFirewallRule -Name $mdnsRuleName -PolicyStore PersistentStore -ErrorAction Stop);if($r.Count -ne 1 -or $r[0].Description -cne $mdnsDescription -or !(Test-CompanionRuleScope (Get-CompanionRuleScope $r[0]) $alias 5353 'UDP' $mdnsProgram)){throw 'mDNS firewall ownership or scope changed'};if($mdnsRuleAdded){$r[0]|Remove-NetFirewallRule -ErrorAction Stop}else{Set-NetFirewallRule -Name $mdnsRuleName -PolicyStore PersistentStore -InterfaceAlias $record.interfaceAlias -ErrorAction Stop|Out-Null}}catch{$rollback.Add('mDNS firewall: '+$_.Exception.Message)}}
  if($ruleAdded -or $ruleUpdated){try{$r=Get-NetFirewallRule -Name $ruleName -PolicyStore PersistentStore -ErrorAction Stop;if($r.Description -cne $description -or !(Test-CompanionRuleScope (Get-CompanionRuleScope $r) $alias $Port)){throw 'Firewall ownership or scope changed'};if($ruleAdded){$r|Remove-NetFirewallRule -ErrorAction Stop}else{Set-NetFirewallRule -Name $ruleName -PolicyStore PersistentStore -InterfaceAlias $record.interfaceAlias -ErrorAction Stop|Out-Null}}catch{$rollback.Add('Firewall: '+$_.Exception.Message)}}
  if($aclAdded){try{Assert-CompanionAcl (Invoke-CompanionNetsh @('http','show','urlacl',"url=$url")) $url $sddl;[void](Invoke-CompanionNetsh @('http','delete','urlacl',"url=$url"))}catch{$rollback.Add('URL ACL: '+$_.Exception.Message)}}
  $bindingRestored=$true
  if($sslAdded -or $sslUpdated){try{Assert-CompanionSsl (Invoke-CompanionNetsh @('http','show','sslcert',"hostnameport=$endpoint")) $endpoint $leaf.Thumbprint;if($sslAdded){[void](Invoke-CompanionNetsh @('http','delete','sslcert',"hostnameport=$endpoint"))}else{[void](Invoke-CompanionNetsh @('http','update','sslcert',"hostnameport=$endpoint","certhash=$($record.serverThumbprint)","appid=$appid",'certstorename=MY'))}}catch{$bindingRestored=$false;$rollback.Add('SNI binding: '+$_.Exception.Message)}}
  if($newLeaf -and $bindingRestored){try{Remove-Item -LiteralPath ("Cert:\LocalMachine\My\"+$newLeaf.Thumbprint) -DeleteKey -ErrorAction Stop}catch{$rollback.Add('Created leaf certificate: '+$_.Exception.Message)}}
  if($newCa -and $bindingRestored){try{Remove-Item -LiteralPath ("Cert:\LocalMachine\My\"+$newCa.Thumbprint) -DeleteKey -ErrorAction Stop}catch{$rollback.Add('Created CA certificate: '+$_.Exception.Message)}}
  if($profileChanged){try{$p=@(Get-NetConnectionProfile -InterfaceIndex $InterfaceIndex -ErrorAction Stop);if($p.Count -ne 1 -or $p[0].Name -ne $profiles[0].Name -or [string]$p[0].NetworkCategory -ne 'Private'){throw 'Network identity changed'};Set-NetConnectionProfile -InterfaceIndex $InterfaceIndex -NetworkCategory Public -ErrorAction Stop}catch{$rollback.Add('Network category: '+$_.Exception.Message)}}
  if($rollback.Count){throw ($failure.Exception.Message+' Rollback needs review: '+($rollback -join '; '))}
  throw $failure
 }finally{if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()}
}

try{Invoke-CompanionRepair @PSBoundParameters|ConvertTo-Json -Depth 7}
catch{[pscustomobject]@{schemaVersion=1;ready=$false;needsApproval=$false;configured=$false;issues=@($_.Exception.Message)}|ConvertTo-Json -Depth 3;exit 1}
