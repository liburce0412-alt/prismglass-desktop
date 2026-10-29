param([Parameter(Mandatory=$true)][string]$AppRoot,[Parameter(Mandatory=$true)][string]$Address,[Parameter(Mandatory=$true)][string]$PeerAddress,[switch]$Apply)
$ErrorActionPreference='Stop'
$script:VideoScriptPath=$PSCommandPath

function Resolve-VideoPath([string]$Path){
 if($Path -notmatch '\A[A-Za-z]:[\\/]'){throw '请选择本机安装路径。'}
 $full=[IO.Path]::GetFullPath($Path).TrimEnd('\','/');$cursor=$full
 while($cursor){if((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw '投屏配置不能使用链接路径。'};$cursor=[IO.Path]::GetDirectoryName($cursor)}
 return $full
}
function Get-VideoPrivateAddress([string]$Value){
 $ip=$null;if(![Net.IPAddress]::TryParse($Value,[ref]$ip)){throw '投屏地址无效，请重新连接。'};$b=$ip.GetAddressBytes()
 if($b.Length -ne 4 -or !($b[0] -eq 10 -or ($b[0] -eq 192 -and $b[1] -eq 168) -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31)) -or $ip.ToString() -cne $Value){throw '投屏仅支持局域网 IPv4 地址。'}
 return $ip
}
function Test-VideoSubnet([string]$Local,[string]$Peer,[int]$Prefix){
 $a=(Get-VideoPrivateAddress $Local).GetAddressBytes();$b=(Get-VideoPrivateAddress $Peer).GetAddressBytes()
 if($Prefix -lt 1 -or $Prefix -gt 30 -or $Local -eq $Peer){return $false}
 for($i=0;$i -lt $Prefix;$i++){ $mask=1 -shl (7-($i%8));if(($a[[int][Math]::Floor($i/8)] -band $mask) -ne ($b[[int][Math]::Floor($i/8)] -band $mask)){return $false} }
 return $true
}
function Get-VideoExactAddress([string]$Value){
 $v=$Value -replace '/255\.255\.255\.255$','' -replace '/32$','';$parts=$v.Split('-')
 if($parts.Count -gt 2 -or ($parts.Count -eq 2 -and $parts[0] -cne $parts[1])){throw '现有投屏规则不是单设备范围，未修改。'}
 return (Get-VideoPrivateAddress $parts[0]).ToString()
}
function Get-VideoRule($Policy,[string]$Program,[string]$Owner){
 $rules=@($Policy.Rules|Where-Object {$_.Name -like 'PrismGlass iPad video *'})
 if($rules.Count -gt 1){throw '发现多条投屏规则，请先检查冲突；原规则未修改。'}
 if(!$rules.Count){return $null};$r=$rules[0]
 if(!$r.Enabled -or $r.Protocol -ne 17 -or $r.Profiles -ne 2 -or $r.Direction -ne 1 -or $r.Action -ne 1 -or $r.LocalPorts -ne '31890' -or $r.RemotePorts -ne '*' -or $r.ApplicationName -ne $Program -or @($r.Interfaces).Count -ne 1 -or [string]$r.Interfaces[0] -match '[*?\[\]]' -or $r.EdgeTraversal -or $r.ServiceName -or ($r.Description -and $r.Description -cne $Owner)){throw '现有投屏规则归属或范围不匹配，未修改。'}
 [void](Get-VideoExactAddress $r.LocalAddresses);[void](Get-VideoExactAddress $r.RemoteAddresses)
 return $r
}
function Get-VideoPlan([string]$Root,[string]$Local,[string]$Peer){
 $Root=Resolve-VideoPath $Root;[void](Get-VideoPrivateAddress $Local);[void](Get-VideoPrivateAddress $Peer)
 $file=Join-Path $Root 'runtime-data/companion/screen-stream.json'
 if(!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -gt 8192){throw '请先完成电脑端投屏工具配置。'}
 [void](Resolve-VideoPath $file);$config=[IO.File]::ReadAllText($file)|ConvertFrom-Json
 if($config.enabled -ne $true){throw '电脑端投屏尚未启用。'}
 $program=Resolve-VideoPath ([string]$config.mediamtx)
 if([IO.Path]::GetFileName($program) -ine 'mediamtx.exe' -or !(Test-Path -LiteralPath $program -PathType Leaf)){throw '找不到已配置的 MediaMTX 程序。'}
 $ip=@(Get-NetIPAddress -IPAddress $Local -AddressFamily IPv4 -ErrorAction Stop)
 if($ip.Count -ne 1 -or !(Test-VideoSubnet $Local $Peer $ip[0].PrefixLength)){throw '电脑和 iPad 不在所选局域网，请重新连接。'}
 $profile=@(Get-NetConnectionProfile -InterfaceIndex $ip[0].InterfaceIndex -ErrorAction Stop)
 if($profile.Count -ne 1 -or $profile[0].NetworkCategory -ne 'Private' -or [string]$ip[0].InterfaceAlias -match '[*?\[\]]'){throw '请在受信任的专用网络使用投屏。'}
 $policy=New-Object -ComObject HNetCfg.FwPolicy2;$owner='PrismGlass.Companion.Video.v1|'+$Root;$rule=Get-VideoRule $policy $program $owner
 $oldLocal='';$oldPeer='';$oldInterface='';if($rule){$oldLocal=Get-VideoExactAddress $rule.LocalAddresses;$oldPeer=Get-VideoExactAddress $rule.RemoteAddresses;$oldInterface=[string]$rule.Interfaces[0]}
 $configured=$null -ne $rule -and $oldLocal -ceq $Local -and $oldPeer -ceq $Peer -and $oldInterface -ceq [string]$ip[0].InterfaceAlias
 return [pscustomobject]@{ready=$true;configured=$configured;needsApproval=!$configured;issues=@();address=$Local;peer=$Peer;interfaceAlias=[string]$ip[0].InterfaceAlias;program=$program;oldAddress=$oldLocal;oldPeer=$oldPeer;oldInterface=$oldInterface;rule=$rule;policy=$policy;owner=$owner}
}
function Assert-VideoApply([string]$Root){
 if(!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw '更新投屏连接需要 Windows 管理员确认。'}
 $expected=Join-Path (Split-Path (Resolve-VideoPath $Root)) 'RepairCompanionVideo.ps1'
 if((Resolve-VideoPath $script:VideoScriptPath) -ine $expected){throw '请使用已安装的投屏修复工具。'}
}
function Invoke-VideoRepair([string]$Root,[string]$Local,[string]$Peer,[bool]$Change){
 $plan=Get-VideoPlan $Root $Local $Peer
 if(!$Change -or $plan.configured){return $plan|Select-Object ready,configured,needsApproval,issues,address,peer,interfaceAlias,program,oldAddress,oldPeer,oldInterface}
 Assert-VideoApply $Root
 $rule=$plan.rule;$old=$null;$added=$false
 if($rule){
  # Adopt only the exact legacy rule previously installed for this video channel.
  if(!$rule.Description){$native=Get-NetFirewallRule -Name 'PrismGlass-Screen-UDP-31890-iPad' -ErrorAction Stop;if($native.DisplayName -cne $rule.Name){throw '旧投屏规则身份不匹配，未修改。'}}
  $old=@{LocalAddresses=$rule.LocalAddresses;RemoteAddresses=$rule.RemoteAddresses;Interfaces=@($rule.Interfaces);Description=$rule.Description}
 }
 try{
  if(!$rule){$rule=New-Object -ComObject HNetCfg.FWRule;$rule.Name='PrismGlass iPad video managed';$rule.ApplicationName=$plan.program;$rule.Protocol=17;$rule.LocalPorts='31890';$rule.RemotePorts='*';$rule.Direction=1;$rule.Action=1;$rule.Profiles=2;$rule.Enabled=$false;$rule.EdgeTraversal=$false}
  else{$rule.Enabled=$false}
  $rule.LocalAddresses=$Local;$rule.RemoteAddresses=$Peer;$rule.Interfaces=@($plan.interfaceAlias);$rule.Description=$plan.owner
  if(!$old){$plan.policy.Rules.Add($rule);$added=$true}
  $rule.Enabled=$true
  $check=Get-VideoPlan $Root $Local $Peer
  if(!$check.configured){throw '投屏规则检查未通过。'}
  return $check|Select-Object ready,configured,needsApproval,issues,address,peer,interfaceAlias,program,oldAddress,oldPeer,oldInterface
 }catch{
  if($added){$plan.policy.Rules.Remove($rule.Name)}elseif($old){$rule.Enabled=$false;$rule.LocalAddresses=$old.LocalAddresses;$rule.RemoteAddresses=$old.RemoteAddresses;$rule.Interfaces=$old.Interfaces;$rule.Description=[string]$old.Description;$rule.Enabled=$true}
  throw
 }
}
try{Invoke-VideoRepair $AppRoot $Address $PeerAddress ([bool]$Apply)|ConvertTo-Json -Depth 4}catch{[ordered]@{ready=$false;configured=$false;needsApproval=$false;issues=@($_.Exception.Message)}|ConvertTo-Json;exit 1}
