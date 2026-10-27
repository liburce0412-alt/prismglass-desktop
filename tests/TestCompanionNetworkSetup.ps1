$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot '../scripts/RepairCompanion.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ($errors.Message -join '; ')}
# Load functions only. Never execute the script entry point or its system commands.
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){Invoke-Expression $definition.Extent.Text}
$checks=0
function Check([bool]$Value,[string]$Message){if(!$Value){throw $Message};$script:checks++;Write-Output ('PASS '+$Message)}
function Reject([scriptblock]$Action,[string]$Message,[string]$Pattern='.'){
 $caught=$null;try{& $Action|Out-Null}catch{$caught=$_.Exception.Message}
 Check ($null -ne $caught -and $caught -match $Pattern) $Message
}
$fixture=Join-Path $PSScriptRoot ('fixtures-network-'+[guid]::NewGuid().ToString('N'))
$app=Join-Path $fixture 'LiquidDesktop';$recordPath=Join-Path $app 'runtime-data/companion-network.json';$legacyPath=Join-Path $fixture 'setup-record.json'
$fixedName='prismglass-a1b2c3d4.local';$endpoint=$fixedName+':9443';$url='https://'+$endpoint+'/'
$userSid='S-1-5-21-100-200-300-1001';$sddl='D:(A;;GX;;;'+$userSid+')';$ruleName='PrismGlass-Companion-'+$fixedName+'-9443'
$mdnsRuleName='PrismGlass-Companion-mDNS-'+$fixedName+'-9443';$mdnsProgram=Join-Path $app 'LiquidDesktop.exe'
$guid='12345678-1234-1234-1234-123456789012';$thumbCa='A'*40;$thumbLeaf='B'*40;$thumbNew='C'*40
$argsBase=@{AppRoot=$app;Address='192.168.1.50';InterfaceIndex=19;HostName=$fixedName;Port=9443;UserSid=$userSid;ExistingSetupRecord=$legacyPath}
$realWrite=(Get-Command Write-CompanionNetworkRecord).ScriptBlock
$resources=[Collections.Generic.List[IDisposable]]::new()
try{
 [IO.Directory]::CreateDirectory((Split-Path $recordPath))|Out-Null
 [IO.File]::WriteAllText((Join-Path $app 'LiquidDesktop.exe'),'fixture')
 [IO.File]::WriteAllText((Join-Path $app 'CompanionServer.cs'),'fixture')
 Check (Test-CompanionPrivateAddress '192.168.1.1') 'private LAN IPv4 is accepted'
 Reject {Assert-CompanionPath 'relative/LiquidDesktop'} 'relative destinations are rejected' 'absolute'
 Check (!(Test-CompanionPrivateAddress '127.0.0.1') -and !(Test-CompanionPrivateAddress '8.8.8.8')) 'loopback and public addresses are rejected'
 $ssl="主机名:端口 : $endpoint`n证书哈希 : $thumbLeaf`n应用程序 ID : {2c370c70-7924-4eb8-a3c4-9c57a8c792bd}`n存储 : MY"
 Assert-CompanionSsl $ssl $endpoint $thumbLeaf
 Check (!(Test-CompanionEndpointListed $ssl 'prismglass-a1b2c3d4.local:944')) 'HTTP.sys endpoint matching is exact across localized labels'
 Reject {Assert-CompanionSsl $ssl $endpoint $thumbNew} 'foreign certificate binding is not adopted'
 Assert-CompanionAcl ("保留 URL : $url`nSDDL : $sddl") $url $sddl
 Reject {Assert-CompanionAcl ("保留 URL : $url`nSDDL : $sddl(A;;GX;;;WD)") $url $sddl} 'URL ACL rejects an additional principal'
 [IO.File]::WriteAllText($recordPath,'{invalid')
 Reject {Read-CompanionRecord $recordPath} 'malformed records are preserved and rejected' 'malformed'
 Check ([IO.File]::ReadAllText($recordPath) -eq '{invalid') 'record parsing cannot overwrite an existing record'
 [IO.File]::WriteAllText($recordPath,('x'*32769))
 Reject {Read-CompanionRecord $recordPath} 'oversized records are rejected' 'size limit'
 [IO.File]::Delete($recordPath)
 $link=Join-Path $fixture 'linked';[IO.Directory]::CreateDirectory($link)|Out-Null
 function Get-Item {param([string]$LiteralPath,[switch]$Force) if($LiteralPath -eq $script:link){return [pscustomobject]@{Attributes=[IO.FileAttributes]::ReparsePoint}};return Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath -Force:$Force}
 Reject {Assert-CompanionPath (Join-Path $link 'runtime-data/companion-network.json')} 'reparse-point destinations are rejected' 'Linked'
 Remove-Item Function:\Get-Item
 Remove-Item -LiteralPath $link -Force

 # Real, memory-only certificate chains exercise DNS SAN and role validation.
 $key=[Security.Cryptography.RSA]::Create(2048);$resources.Add($key)
 $req=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=PrismGlass Companion 10.13.91.4',$key,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $req.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true,$true,0,$true))
 $req.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign,$true))
 $ca=$req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-10),[DateTimeOffset]::UtcNow.AddYears(1));$resources.Add($ca)
 $ca.FriendlyName='PrismGlass Companion CA 10.13.91.4'
 function New-MemoryLeaf([string]$Dns,[bool]$Expired=$false,$Authority=$script:ca){
  $rsa=[Security.Cryptography.RSA]::Create(2048);$script:resources.Add($rsa)
  $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new(('CN='+$script:fixedName),$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
  $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
  $oid=[Security.Cryptography.OidCollection]::new();[void]$oid.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1'))
  $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($oid,$false))
  $san=[Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new();$san.AddDnsName($Dns);$request.CertificateExtensions.Add($san.Build())
  $expires=if($Expired){[DateTimeOffset]::UtcNow.AddDays(-1)}else{[DateTimeOffset]::UtcNow.AddMonths(2)}
  $public=$request.Create($Authority,[DateTimeOffset]::UtcNow.AddDays(-2),$expires,[guid]::NewGuid().ToByteArray());$script:resources.Add($public)
  $certificate=[Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey($public,$rsa);$script:resources.Add($certificate)
  $certificate.FriendlyName='PrismGlass Companion HTTPS '+$script:endpoint
  return $certificate
 }
 $leaf=New-MemoryLeaf $fixedName;$newLeaf=New-MemoryLeaf $fixedName;$expired=New-MemoryLeaf $fixedName $true;$wrong=New-MemoryLeaf 'elsewhere.local'
 Assert-CompanionDnsCertificate $leaf $fixedName $ca
 Check $true 'DNS-only leaf verifies against an in-memory CA without changing root trust'
 $otherKey=[Security.Cryptography.RSA]::Create(2048);$resources.Add($otherKey)
 $otherRequest=[Security.Cryptography.X509Certificates.CertificateRequest]::new($ca.Subject,$otherKey,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $otherRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true,$true,0,$true))
 $otherRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign,$true))
 $otherCa=$otherRequest.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-10),[DateTimeOffset]::UtcNow.AddYears(1));$resources.Add($otherCa)
 Reject {Assert-CompanionDnsCertificate $leaf $fixedName $otherCa} 'a different CA with the same subject cannot validate the leaf' 'verify'
 Reject {Assert-CompanionDnsCertificate $wrong $fixedName $ca} 'another DNS SAN cannot be reused' 'exactly'
 Reject {Assert-CompanionDnsCertificate $expired $fixedName $ca} 'expired leaf fails normal validation' 'currently valid'
 Assert-CompanionDnsCertificate $expired $fixedName $ca $true
 Check $true 'owned expired leaf can be validated for renewal without accepting a different name'
 $thumbCa=$ca.Thumbprint;$thumbLeaf=$leaf.Thumbprint;$thumbNew=$newLeaf.Thumbprint
 $freshKey=[Security.Cryptography.RSA]::Create(2048);$resources.Add($freshKey)
 $freshRequest=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=PrismGlass Companion 192.168.1.50',$freshKey,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $freshRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true,$true,0,$true))
 $freshRequest.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyCertSign,$true))
 $freshCa=$freshRequest.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-10),[DateTimeOffset]::UtcNow.AddYears(3));$resources.Add($freshCa);$freshCa.FriendlyName='PrismGlass Companion CA 192.168.1.50'
 $freshLeaf=New-MemoryLeaf $fixedName $false $freshCa
 $legacy=@{operationId=[guid]::NewGuid().ToString('N');address='10.13.91.4';port=9443;url='https://10.13.91.4:9443/';sid=$userSid;caThumbprint=$thumbCa;serverThumbprint=$thumbLeaf}
 [IO.File]::WriteAllText($legacyPath,($legacy|ConvertTo-Json))
 function Scope([string]$Alias='WLAN'){return [pscustomobject]@{enabled='True';profile='Private';direction='Inbound';action='Allow';protocol='TCP';localPort=@('9443');remotePort=@('Any');localAddress=@('Any');remoteAddress=@('LocalSubnet');program='System';interfaceAlias=@($Alias)}}
 function MdnsScope([string]$Alias='WLAN'){$scope=Scope $Alias;$scope.protocol='UDP';$scope.localPort=@('5353');$scope.program=$script:mdnsProgram;return $scope}
 function Reset-Mock {
  $script:mock=@{ssl=$null;acl=$false;rule=$null;mdnsRule=$null;profile='Private';alias='WLAN';id=$script:guid;log=[Collections.Generic.List[string]]::new();failWrite=$false;failRestore=$false;failMdnsCreate=$false;failMdnsMove=$false;denyQuery=$false;denyCert=$false;leaf=$script:leaf}
  if([IO.File]::Exists($script:recordPath)){[IO.File]::Delete($script:recordPath)}
 }
 function Owned([switch]$Legacy) {
  $saved=@{schemaVersion=1;owner='PrismGlass.Companion.Network.v1';appRoot=$script:app;hostName=$script:fixedName;port=9443;interfaceId=$script:guid;interfaceAlias='WLAN';interfaceIndex=19;address='192.168.1.10';userSid=$script:userSid;caThumbprint=$script:thumbCa;caSubject=$script:ca.Subject;serverThumbprint=$script:mock.leaf.Thumbprint;firewallRule=$script:ruleName;setupRecord=$script:legacyPath;createdUtc=[DateTime]::UtcNow.ToString('o')}
  if(!$Legacy){$saved.mdnsFirewallRule=$script:mdnsRuleName;$script:mock.mdnsRule=[pscustomobject]@{Name=$script:mdnsRuleName;Description=('PrismGlass Companion mDNS '+$script:endpoint+'; user '+$script:userSid);Scope=(MdnsScope)}}
  [IO.File]::WriteAllText($script:recordPath,($saved|ConvertTo-Json))
  $script:mock.ssl=$script:mock.leaf.Thumbprint;$script:mock.acl=$true
  $script:mock.rule=[pscustomobject]@{Name=$script:ruleName;Description=('PrismGlass Companion fixed name '+$script:endpoint+'; user '+$script:userSid);Scope=(Scope)}
 }
 # All machine queries and mutations below are mocks; unexpected native operations fail.
 function Get-NetIPAddress {param($AddressFamily,$IPAddress,$ErrorAction) return [pscustomobject]@{InterfaceIndex=19}}
 function Get-NetAdapter {param($InterfaceIndex,[switch]$Physical,$ErrorAction) if(!$Physical){throw 'Physical interface check missing'};return [pscustomobject]@{Status='Up';InterfaceGuid=$script:mock.id;Name=$script:mock.alias}}
 function Get-NetConnectionProfile {param($InterfaceIndex,$ErrorAction) return [pscustomobject]@{NetworkCategory=$script:mock.profile;Name='Fixture Wi-Fi'}}
 function Set-NetConnectionProfile {param($InterfaceIndex,$NetworkCategory,$ErrorAction) $script:mock.log.Add('profile:'+ $NetworkCategory);$script:mock.profile=$NetworkCategory}
 function Get-CompanionCertificate([string]$Thumbprint){if($script:mock.denyCert){throw 'certificate store access denied'};if($Thumbprint -eq $script:freshCa.Thumbprint){return $script:freshCa};if($Thumbprint -eq $script:freshLeaf.Thumbprint){return $script:freshLeaf};if($Thumbprint -eq $script:thumbCa){return $script:ca};if($Thumbprint -eq $script:mock.leaf.Thumbprint){return $script:mock.leaf};return $null}
 function Test-CompanionAdministrator {return $true}
 function Assert-CompanionInstalledScript([string]$AppRoot) {if($AppRoot -ne $script:app){throw 'Unexpected installation'}}
 function Assert-CompanionNonExportableKey($Certificate) {if($Certificate.Thumbprint -notin @($script:thumbCa,$script:thumbLeaf,$script:thumbNew,$script:expired.Thumbprint,$script:freshCa.Thumbprint,$script:freshLeaf.Thumbprint)){throw 'Unexpected certificate'}}
 function Import-Module {param($Name,$ErrorAction) if($Name -ne 'PKI'){throw 'Unexpected module'}}
 function New-SelfSignedCertificate {
  param($Type,$Subject,$FriendlyName,$Signer,$CertStoreLocation,$KeyAlgorithm,$KeyLength,$HashAlgorithm,$KeyExportPolicy,$KeyUsage,$TextExtension,$NotAfter)
  if(!$Signer){if($Subject -ne $script:freshCa.Subject -or $FriendlyName -ne $script:freshCa.FriendlyName -or $KeyExportPolicy -ne 'NonExportable' -or $CertStoreLocation -ne 'Cert:\LocalMachine\My' -or $TextExtension[0] -ne '2.5.29.19={critical}{text}ca=1&pathlength=0'){throw 'Unsafe CA request'};$script:mock.log.Add('ca:create');return $script:freshCa}
  if($Signer.Thumbprint -notin @($script:thumbCa,$script:freshCa.Thumbprint) -or $KeyExportPolicy -ne 'NonExportable' -or $CertStoreLocation -ne 'Cert:\LocalMachine\My' -or $TextExtension[0] -ne ('2.5.29.17={text}DNS='+$script:fixedName)){throw 'Unsafe certificate request'}
  if($Signer.Thumbprint -eq $script:freshCa.Thumbprint){$script:mock.log.Add('leaf:create');return $script:freshLeaf}
  $script:mock.log.Add('leaf:create');return $script:newLeaf
 }
 function Invoke-CompanionNetsh([string[]]$Arguments){
  if($script:mock.denyQuery){throw 'HTTP.sys access denied'}
  $verb=$Arguments[1];$type=$Arguments[2]
  if($verb -eq 'show'){
   if($type -eq 'sslcert'){if($script:mock.ssl){return "Hostname:port : $script:endpoint`nHash : $($script:mock.ssl)`nApp : {2c370c70-7924-4eb8-a3c4-9c57a8c792bd}`nStore : MY"};return 'IP:port : 10.13.91.4:9443'}
   if($type -eq 'urlacl'){if($script:mock.acl){return "Reserved URL : $script:url`nSDDL : $script:sddl"};return 'Reserved URL : https://10.13.91.4:9443/'}
  }
  $script:mock.log.Add($verb+':'+$type)
  if($type -eq 'sslcert'){
   if($Arguments -notcontains ('hostnameport='+$script:endpoint)){throw 'Unexpected SSL target'}
   if($verb -eq 'delete'){if($script:mock.failRestore){throw 'simulated rollback failure'};$script:mock.ssl=$null;return ''}
   if($verb -in @('add','update')){$script:mock.ssl=($Arguments|Where-Object {$_ -like 'certhash=*'}).Substring(9);return ''}
  }
  if($type -eq 'urlacl' -and $Arguments -contains ('url='+$script:url)){if($verb -eq 'add'){$script:mock.acl=$true;return ''};if($verb -eq 'delete'){$script:mock.acl=$false;return ''}}
  throw ('Unexpected netsh operation '+($Arguments -join ' '))
 }
 function Get-NetFirewallRule {
  param($Name,$PolicyStore,$ErrorAction)
  if($Name){if($Name -eq $script:ruleName -and $script:mock.rule){return $script:mock.rule};if($Name -eq $script:mdnsRuleName -and $script:mock.mdnsRule){return $script:mock.mdnsRule};throw 'Rule is absent'}
  if($PolicyStore -ne 'PersistentStore'){throw 'Unexpected policy store query'}
  if($script:mock.rule){$script:mock.rule};if($script:mock.mdnsRule){$script:mock.mdnsRule}
 }
 function Get-CompanionRuleScope($Rule){return $Rule.Scope}
 function New-NetFirewallRule {
  param($Name,$DisplayName,$Description,$Direction,$Action,$Protocol,$LocalPort,$RemotePort,$LocalAddress,$RemoteAddress,$Profile,$InterfaceAlias,$Program,$Enabled,$ErrorAction)
  if($LocalAddress -ne 'Any' -or $RemoteAddress -ne 'LocalSubnet' -or $Profile -ne 'Private' -or $InterfaceAlias -ne $script:mock.alias -or $RemotePort -ne 'Any' -or $Direction -ne 'Inbound' -or $Action -ne 'Allow' -or $Enabled -ne 'True'){throw 'Unsafe firewall request'}
  if($Name -eq $script:mdnsRuleName){
   if($Program -ne $script:mdnsProgram -or $Protocol -ne 'UDP' -or $LocalPort -ne 5353){throw 'Unsafe mDNS request'}
   if($script:mock.failMdnsCreate){throw 'simulated mDNS rule creation failure'}
   $script:mock.log.Add('mdns:create');$script:mock.mdnsRule=[pscustomobject]@{Name=$Name;Description=$Description;Scope=(MdnsScope $InterfaceAlias)};return
  }
  if($Name -ne $script:ruleName -or $Program -ne 'System' -or $Protocol -ne 'TCP' -or $LocalPort -ne 9443){throw 'Unexpected TCP request'}
  $script:mock.log.Add('rule:create');$script:mock.rule=[pscustomobject]@{Name=$Name;Description=$Description;Scope=(Scope $InterfaceAlias)}
 }
 function Set-NetFirewallRule {param($Name,$PolicyStore,$InterfaceAlias,$ErrorAction) if($Name -eq $script:mdnsRuleName){if($script:mock.failMdnsMove){throw 'simulated mDNS interface failure'};$script:mock.log.Add('mdns:interface');$script:mock.mdnsRule.Scope.interfaceAlias=@($InterfaceAlias);return};if($Name -ne $script:ruleName){throw 'Unexpected rule'};$script:mock.log.Add('rule:interface');$script:mock.rule.Scope.interfaceAlias=@($InterfaceAlias)}
 function Remove-NetFirewallRule {param([Parameter(ValueFromPipeline)]$InputObject) process{if($InputObject.Name -eq $script:mdnsRuleName){$script:mock.log.Add('mdns:remove');$script:mock.mdnsRule=$null;return};if($InputObject.Name -ne $script:ruleName){throw 'Unexpected rule deletion'};$script:mock.log.Add('rule:remove');$script:mock.rule=$null}}
 function Remove-Item {
  param([string]$LiteralPath,[switch]$DeleteKey,[switch]$Force,$ErrorAction)
  if($LiteralPath -eq ('Cert:\LocalMachine\My\'+$script:freshCa.Thumbprint) -and $DeleteKey){$script:mock.log.Add('ca:remove');return}
  if($LiteralPath -notin @(('Cert:\LocalMachine\My\'+$script:thumbNew),('Cert:\LocalMachine\My\'+$script:freshLeaf.Thumbprint)) -or !$DeleteKey){throw 'Unexpected deletion in mocked apply'}
  $script:mock.log.Add('leaf:remove')
 }
 function Write-CompanionNetworkRecord([string]$Path,$Record){if($script:mock.failWrite){throw 'simulated record write failure'};& $script:realWrite $Path $Record}

 Reset-Mock
 $result=Invoke-CompanionRepair @argsBase
 Check ($result.ready -and !$result.configured -and $result.needsApproval -and $result.plan.certificateAction -eq 'create') 'first preview produces a reviewable DNS/SNI repair plan'
 Check ($mock.log.Count -eq 0 -and ![IO.File]::Exists($recordPath)) 'preview performs no mutations or record writes'
 Check ($result.plan.mdns.action -eq 'create' -and !$result.plan.mdns.rulesPresent -and $result.plan.mdns.program -eq $mdnsProgram -and $result.plan.mdns.interfaceAlias -eq 'WLAN' -and $result.plan.mdns.localPort -eq 5353 -and $result.plan.mdns.protocol -eq 'UDP' -and $result.plan.mdns.profile -eq 'Private' -and $result.plan.mdns.remoteAddress -eq 'LocalSubnet' -and $result.plan.appRoot -eq $app) 'plan describes the new app-specific Private/Subnet/interface UDP 5353 rule'
 $mock.denyQuery=$true
 Reject {Invoke-CompanionRepair @argsBase} 'denied HTTP.sys query never becomes absent binding' 'access denied'
 $mock.denyQuery=$false;$mock.denyCert=$true
 Reject {Invoke-CompanionRepair @argsBase} 'denied certificate query never becomes a renewal' 'access denied'
 $mock.denyCert=$false;$mock.ssl=$thumbLeaf
 Reject {Invoke-CompanionRepair @argsBase} 'unrecorded SNI endpoint is refused before any mutation' 'unrecorded'
 Reset-Mock;$mock.profile='Public'
 Reject {Invoke-CompanionRepair @argsBase} 'Public network requires explicit trust flag' 'TrustNetwork'
 $result=Invoke-CompanionRepair @argsBase -TrustNetwork
 Check ($result.plan.changeNetworkToPrivate -and $mock.log.Count -eq 0) 'trust preview describes a category change but does not apply it'
 Reset-Mock;Owned -Legacy;$result=Invoke-CompanionRepair @argsBase
 Check ($result.ready -and !$result.configured -and $result.needsApproval -and $result.plan.mdns.action -eq 'create' -and $result.plan.certificateAction -eq 'reuse' -and $result.plan.firewallAction -eq 'reuse' -and $mock.log.Count -eq 0) 'old record needs approval only for new mDNS rule and preserves existing HTTPS resources'
 Reset-Mock;Owned
 $result=Invoke-CompanionRepair @argsBase
 Check ($result.ready -and $result.configured -and !$result.needsApproval -and $result.plan.certificateAction -eq 'reuse' -and $mock.log.Count -eq 0) 'same interface with changed IPv4 is ready without certificate or firewall repair'
 $mock.rule.Scope.remoteAddress=@('Any')
 Reject {Invoke-CompanionRepair @argsBase} 'broadened existing firewall scope is not silently adopted' 'narrow scope'
 Reset-Mock;Owned;$mock.alias='Ethernet';$mock.id='87654321-1234-1234-1234-123456789012'
 $result=Invoke-CompanionRepair @argsBase
 Check (!$result.configured -and $result.plan.firewallAction -eq 'move-interface' -and $result.plan.mdns.action -eq 'move-interface' -and $result.plan.certificateAction -eq 'reuse') 'changing adapter retains certificate while planning both narrow rule moves'
 Reset-Mock;$mock.leaf=$expired;Owned
 $result=Invoke-CompanionRepair @argsBase
 Check ($result.plan.certificateAction -eq 'renew') 'expired owned certificate produces renewal plan'

 Reset-Mock
 $result=Invoke-CompanionRepair @argsBase -Apply
 $written=Read-CompanionRecord $recordPath
 Check ($result.configured -and !$result.needsApproval -and $written.appRoot -eq $app -and $written.serverThumbprint -eq $thumbNew) 'mocked apply records verified ownership only after successful readback'
 $expectedShell=if($PSVersionTable.PSVersion.Major -ge 7){'pwsh.exe'}else{'powershell.exe'}
 Check ($written.powerShellPath -eq [IO.Path]::GetFullPath((Join-Path $PSHOME $expectedShell)) -and [IO.File]::Exists($written.powerShellPath)) 'record preserves the current PowerShell executable, including 5.1 and portable 7 installs'
 Check ($written.mdnsFirewallRule -eq $mdnsRuleName -and ($mock.log -join ',') -eq 'leaf:create,add:sslcert,add:urlacl,rule:create,mdns:create') 'apply records and creates only owned HTTPS resources and the app-specific UDP rule'
 Reset-Mock;$mock.failWrite=$true;$mock.profile='Public'
 Reject {Invoke-CompanionRepair @argsBase -Apply -TrustNetwork} 'failed record write rolls back the mocked transaction' 'simulated record write failure'
 Check (!$mock.ssl -and !$mock.acl -and !$mock.rule -and !$mock.mdnsRule -and $mock.profile -eq 'Public' -and ![IO.File]::Exists($recordPath)) 'rollback restores only resources created during this run'
 Check (($mock.log -join ',') -eq 'profile:Private,leaf:create,add:sslcert,add:urlacl,rule:create,mdns:create,mdns:remove,rule:remove,delete:urlacl,delete:sslcert,leaf:remove,profile:Public') 'rollback order preserves CA, prior IP resources and user data'
 Reset-Mock;$mock.failWrite=$true;$mock.failRestore=$true
 Reject {Invoke-CompanionRepair @argsBase -Apply} 'partial rollback is explicitly reported' 'Rollback needs review'
 Check ($mock.ssl -eq $thumbNew -and $mock.log -notcontains 'leaf:remove') 'failed SNI rollback retains the certificate still in use'
 Reset-Mock;Owned;$mock.alias='Ethernet';$mock.id='87654321-1234-1234-1234-123456789012';$mock.failWrite=$true
 Reject {Invoke-CompanionRepair @argsBase -Apply} 'failed interface migration rolls back its rule change' 'simulated record write failure'
 Check (($mock.rule.Scope.interfaceAlias -join ',') -eq 'WLAN' -and ($mock.mdnsRule.Scope.interfaceAlias -join ',') -eq 'WLAN' -and $mock.ssl -eq $thumbLeaf -and $mock.acl -and ($mock.log -join ',') -eq 'rule:interface,mdns:interface,mdns:interface,rule:interface') 'migration rollback restores both interfaces and preserves existing HTTPS resources'

 Reset-Mock;Owned;$existingMdns=$mock.mdnsRule;Owned -Legacy;$mock.mdnsRule=$existingMdns
 Reject {Invoke-CompanionRepair @argsBase} 'mDNS rule without recorded ownership is not adopted' 'unrecorded mDNS'
 foreach($field in @('program','remoteAddress','profile','protocol')){
  Reset-Mock;Owned
  switch($field){'program'{$mock.mdnsRule.Scope.program='System'} 'remoteAddress'{$mock.mdnsRule.Scope.remoteAddress=@('Any')} 'profile'{$mock.mdnsRule.Scope.profile='Any'} 'protocol'{$mock.mdnsRule.Scope.protocol='TCP'}}
  Reject {Invoke-CompanionRepair @argsBase} ('mDNS rejects changed '+$field+' scope') 'mDNS firewall rule.*narrow scope'
 }
 Reset-Mock;Owned -Legacy;$result=Invoke-CompanionRepair @argsBase -Apply;$written=Read-CompanionRecord $recordPath
 Check ($result.configured -and $written.mdnsFirewallRule -eq $mdnsRuleName -and $written.serverThumbprint -eq $thumbLeaf -and ($mock.log -join ',') -eq 'mdns:create') 'legacy upgrade adds only app-specific UDP permission without reissuing a certificate'
 Reset-Mock;Owned -Legacy;$before=[IO.File]::ReadAllText($recordPath);$mock.failWrite=$true
 Reject {Invoke-CompanionRepair @argsBase -Apply} 'legacy upgrade rolls back when record saving fails' 'simulated record write failure'
 Check (!$mock.mdnsRule -and $mock.rule -and $mock.ssl -eq $thumbLeaf -and $mock.acl -and ($mock.log -join ',') -eq 'mdns:create,mdns:remove' -and [IO.File]::ReadAllText($recordPath) -eq $before) 'failed upgrade preserves byte-for-byte old record and every old HTTPS resource'
 Reset-Mock;Owned -Legacy;$mock.failMdnsCreate=$true
 Reject {Invoke-CompanionRepair @argsBase -Apply} 'failed UDP creation leaves existing configuration untouched' 'simulated mDNS rule creation failure'
 Check ($mock.log.Count -eq 0 -and $mock.rule -and $mock.ssl -eq $thumbLeaf -and $mock.acl -and !$mock.mdnsRule) 'failed UDP creation does not remove or recreate existing HTTPS resources'
 Reset-Mock;Owned;$mock.alias='Ethernet';$mock.id='87654321-1234-1234-1234-123456789012';$mock.failMdnsMove=$true
 Reject {Invoke-CompanionRepair @argsBase -Apply} 'failed UDP interface move rolls back preceding TCP interface move' 'simulated mDNS interface failure'
 Check (($mock.rule.Scope.interfaceAlias -join ',') -eq 'WLAN' -and ($mock.mdnsRule.Scope.interfaceAlias -join ',') -eq 'WLAN' -and ($mock.log -join ',') -eq 'rule:interface,rule:interface') 'both prior interface scopes survive partial migration failure'
 Reset-Mock;$firstArgs=$argsBase.Clone();$firstArgs.Remove('ExistingSetupRecord')
 Reject {Invoke-CompanionRepair @firstArgs} 'missing setup requires explicit first-use intent' 'first-time'
 $result=Invoke-CompanionRepair @firstArgs -Initialize
 Check ($result.ready -and $result.needsApproval -and !$result.configured -and $result.plan.caAction -eq 'create' -and $mock.log.Count -eq 0 -and ![IO.File]::Exists($recordPath)) 'first-use preview describes new CA without creating certificates, network rules or records'
 Reject {Invoke-CompanionRepair @argsBase -Initialize} 'first-use cannot replace an imported CA' 'cannot replace'
 $result=Invoke-CompanionRepair @firstArgs -Initialize -Apply;$written=Read-CompanionRecord $recordPath
 Check ($result.configured -and $written.caThumbprint -eq $freshCa.Thumbprint -and $written.serverThumbprint -eq $freshLeaf.Thumbprint -and !$written.setupRecord -and ($mock.log -join ',') -eq 'ca:create,leaf:create,add:sslcert,add:urlacl,rule:create,mdns:create') 'first-use applies a dedicated CA and fixed endpoint without a legacy record'
 $mock.log.Clear();$result=Invoke-CompanionRepair @firstArgs
 Check ($result.configured -and !$result.needsApproval -and $result.plan.caAction -eq 'reuse' -and $mock.log.Count -eq 0) 'second connection reuses the recorded first-use CA without new trust'
 Reject {Invoke-CompanionRepair @firstArgs -Initialize} 'first-use cannot replace an existing configured installation' 'cannot replace'
 Reset-Mock;$mock.failWrite=$true
 Reject {Invoke-CompanionRepair @firstArgs -Initialize -Apply} 'first-use failed record saving rolls back setup' 'simulated record write failure'
 Check (!$mock.ssl -and !$mock.acl -and !$mock.rule -and !$mock.mdnsRule -and ![IO.File]::Exists($recordPath) -and $mock.log -contains 'leaf:remove' -and $mock.log -contains 'ca:remove') 'first-use rollback removes only its newly created leaf and CA after unbinding'
 Reset-Mock;$mock.failWrite=$true;$mock.failRestore=$true
 Reject {Invoke-CompanionRepair @firstArgs -Initialize -Apply} 'first-use partial rollback reports manual review' 'Rollback needs review'
 Check ($mock.ssl -eq $freshLeaf.Thumbprint -and $mock.log -notcontains 'leaf:remove' -and $mock.log -notcontains 'ca:remove') 'unconfirmed binding rollback retains both new certificates still required by endpoint'
 Write-Output ("Passed $checks companion network setup checks. No real system configuration was changed.")
}finally{
 foreach($resource in $resources){$resource.Dispose()}
 $root=[IO.Path]::GetFullPath($fixture);$allowed=[IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')+'\'
 if(!$root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture cleanup escaped tests directory'}
 if([IO.Directory]::Exists($root)){Microsoft.PowerShell.Management\Remove-Item -LiteralPath $root -Recurse -Force}
}
