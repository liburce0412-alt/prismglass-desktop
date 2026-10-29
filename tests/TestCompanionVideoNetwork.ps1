$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot '../scripts/RepairCompanionVideo.ps1'
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw ($errors.Message -join '; ')}
foreach($definition in $ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst]},$false)){Invoke-Expression $definition.Extent.Text}
$checks=0
function Check([bool]$Value,[string]$Message){if(!$Value){throw $Message};$script:checks++;Write-Output ('PASS '+$Message)}
function Reject([scriptblock]$Action,[string]$Message){$failed=$false;try{& $Action|Out-Null}catch{$failed=$true};Check $failed $Message}
$fixture=Join-Path $PSScriptRoot ('fixtures-video-network-'+[guid]::NewGuid().ToString('N'))
$app=Join-Path $fixture 'LiquidDesktop';$configFile=Join-Path $app 'runtime-data/companion/screen-stream.json';$program=Join-Path $fixture 'mediamtx.exe'
$script:rules=[Collections.ArrayList]::new();$script:policy=[pscustomobject]@{Rules=$script:rules}
Add-Member -InputObject $script:rules -MemberType ScriptMethod -Name Remove -Force -Value {param($Name)for($i=$this.Count-1;$i -ge 0;$i--){if($this[$i].Name -eq $Name){$this.RemoveAt($i)}}}
$script:category='Private';$script:applyChecks=0;$script:failReadback=$false;$script:readCount=0
function Get-NetIPAddress {param($IPAddress,$AddressFamily,$ErrorAction)if($IPAddress -ne '192.168.1.50'){throw 'not local'};[pscustomobject]@{InterfaceIndex=19;InterfaceAlias='WLAN';PrefixLength=24}}
function Get-NetConnectionProfile {param($InterfaceIndex,$ErrorAction)[pscustomobject]@{NetworkCategory=$script:category}}
function Get-NetFirewallRule {param($Name,$ErrorAction)if($Name -ne 'PrismGlass-Screen-UDP-31890-iPad'){throw 'wrong identity'};[pscustomobject]@{DisplayName='PrismGlass iPad video 10.0.0.20'}}
function Assert-VideoApply {param($Root)$script:applyChecks++}
function New-Object {param($ComObject)
 if($ComObject -eq 'HNetCfg.FwPolicy2'){$script:readCount++;if($script:failReadback -and $script:readCount -eq 2){throw 'fixture readback failure'};return $script:policy}
 if($ComObject -eq 'HNetCfg.FWRule'){return [pscustomobject]@{Name='';Description='';ApplicationName='';Protocol=0;LocalPorts='';RemotePorts='*';LocalAddresses='';RemoteAddresses='';Interfaces=@();Direction=0;Action=0;Profiles=0;Enabled=$false;EdgeTraversal=$false;ServiceName=''}}
 throw 'Unexpected COM dependency'
}
try{
 [IO.Directory]::CreateDirectory((Split-Path $configFile))|Out-Null;[IO.File]::WriteAllText($program,'test fixture')
 [IO.File]::WriteAllText($configFile,(@{enabled=$true;address='10.0.0.10';mediamtx=$program}|ConvertTo-Json));$original=[IO.File]::ReadAllText($configFile)
 Check (Test-VideoSubnet '192.168.1.50' '192.168.1.90' 24) 'same-subnet authenticated peer is accepted'
 Check (!(Test-VideoSubnet '192.168.1.50' '192.168.2.90' 24)) 'different subnet cannot widen the video rule'
 Check (!(Test-VideoSubnet '192.168.1.50' '192.168.1.50' 24)) 'local address cannot masquerade as a remote iPad'
 foreach($value in @('8.8.8.8','127.0.0.1','::1','192.168.1.2,192.168.1.3','192.168.1.0/24','192.168.1.2-192.168.1.9')){Reject {Get-VideoExactAddress $value} ('rejects non-single-device address '+$value)}
 Check ((Get-VideoExactAddress '192.168.1.90-192.168.1.90') -eq '192.168.1.90') 'Windows exact-IP ranges normalize correctly'
 $plan=Invoke-VideoRepair $app '192.168.1.50' '192.168.1.90' $false
 Check ($plan.needsApproval -and !$plan.configured -and $script:rules.Count -eq 0 -and $script:applyChecks -eq 0) 'missing-rule preflight creates nothing and requests approval'
 $script:category='Public';Reject {Invoke-VideoRepair $app '192.168.1.50' '192.168.1.90' $true} 'public network is never silently changed or enabled';$script:category='Private'
 Reject {Invoke-VideoRepair $app '10.0.0.10' '10.0.0.20' $true} 'stale local address is rejected before mutation'
 $created=Invoke-VideoRepair $app '192.168.1.50' '192.168.1.90' $true
 Check ($created.configured -and $script:rules.Count -eq 1 -and $script:applyChecks -eq 1) 'approved setup creates one device-scoped rule'
 $r=$script:rules[0];Check ($r.ApplicationName -eq $program -and $r.Profiles -eq 2 -and $r.Protocol -eq 17 -and $r.LocalPorts -eq '31890' -and $r.Interfaces[0] -eq 'WLAN' -and $r.RemoteAddresses -eq '192.168.1.90') 'created rule keeps program, private interface, port and exact peer'
 [void](Invoke-VideoRepair $app '192.168.1.50' '192.168.1.90' $true)
 Check ($script:rules.Count -eq 1 -and $script:applyChecks -eq 1) 'already configured connection is a no-op without elevation'
 $r.Name='PrismGlass iPad video 10.0.0.20';$r.Description='';$r.LocalAddresses='10.0.0.10';$r.RemoteAddresses='10.0.0.20'
 $plan=Invoke-VideoRepair $app '192.168.1.50' '192.168.1.90' $false
 Check ($plan.oldAddress -eq '10.0.0.10' -and $plan.oldPeer -eq '10.0.0.20' -and $r.LocalAddresses -eq '10.0.0.10') 'stale legacy rule is reported without mutation'
 [void](Invoke-VideoRepair $app '192.168.1.50' '192.168.1.90' $true)
 Check ($script:rules.Count -eq 1 -and $r.LocalAddresses -eq '192.168.1.50' -and $r.RemoteAddresses -eq '192.168.1.90' -and $r.Description.Contains($app)) 'approved migration adopts the exact legacy rule instead of adding another'
 $r.RemoteAddresses='192.168.1.91';$script:readCount=0;$script:failReadback=$true
 Reject {Invoke-VideoRepair $app '192.168.1.50' '192.168.1.90' $true} 'failed readback reports failure'
 Check ($r.Enabled -and $r.RemoteAddresses -eq '192.168.1.91' -and $r.LocalAddresses -eq '192.168.1.50') 'failed update restores the prior rule scope';$script:failReadback=$false
 $r.RemoteAddresses='*';Reject {Invoke-VideoRepair $app '192.168.1.50' '192.168.1.90' $true} 'unexpected broad rule is not adopted';$r.RemoteAddresses='192.168.1.90'
 $r.Description='another installation';Reject {Invoke-VideoRepair $app '192.168.1.50' '192.168.1.90' $true} 'another installation rule is preserved'
 $script:rules.Clear();$script:readCount=0;$script:failReadback=$true
 Reject {Invoke-VideoRepair $app '192.168.1.50' '192.168.1.90' $true} 'new-rule readback failure reports failure'
 Check ($script:rules.Count -eq 0) 'failed creation removes only its newly added rule'
 Check ([IO.File]::ReadAllText($configFile) -ceq $original) 'repair never rewrites stream settings or the legacy address'
 Write-Output ('Video network: '+$checks+' checks passed; all system adapters were isolated.')
}finally{
 if(Test-Path -LiteralPath $fixture){$resolved=(Resolve-Path -LiteralPath $fixture).Path;if(!$resolved.StartsWith([IO.Path]::GetFullPath($PSScriptRoot)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped test directory'};Remove-Item -LiteralPath $resolved -Recurse -Force}
}
