param(
 [Parameter(Mandatory=$true)][string]$AppRoot,
 [string]$UserSid=([Security.Principal.WindowsIdentity]::GetCurrent().User.Value),
 [switch]$Apply
)
$ErrorActionPreference='Stop'
$AppRoot=[IO.Path]::GetFullPath($AppRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if(!(Test-Path -LiteralPath (Join-Path $AppRoot 'LiquidDesktop.exe') -PathType Leaf)){throw 'Choose the installed LiquidDesktop directory.'}
$UserSid=([Security.Principal.SecurityIdentifier]::new($UserSid)).Value
if($UserSid -notmatch '^S-1-5-21-(\d+-){3}\d+$'){throw 'Choose a specific local desktop user SID.'}
$hardware='Root\MttVDD'
$devices=@(Get-PnpDevice -Class Display -PresentOnly | Where-Object {
 $ids=@((Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_HardwareIds).Data)
 $ids -contains $hardware
})
if($devices.Count -ne 1){throw 'Expected exactly one installed MttVDD adapter; no other driver will be managed.'}
$program=Join-Path ([Environment]::SystemDirectory) 'pnputil.exe'
$owner="PrismGlass virtual display switch v1 ($UserSid)"
$tasks=@('Enable','Disable') | ForEach-Object {[pscustomobject]@{name="PrismGlass-Vdd-$_-$UserSid";command=$program;arguments=('/'+$_.ToLowerInvariant()+'-device /deviceid "'+$hardware+'"')}}
$plan=[ordered]@{userSid=$UserSid;hardwareId=$hardware;deviceInstance=$devices[0].InstanceId;tasks=$tasks;principal='LocalSystem';scope='Enable or disable only Root\MttVDD using Windows PnPUtil; no script or user-writable executable runs elevated.';automaticTriggers=$false;apply=[bool]$Apply}
if(!$Apply){$plan | ConvertTo-Json -Depth 4;return}
if(!([Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator approval is required to create the two fixed-device tasks.'}
$service=New-Object -ComObject Schedule.Service
$folder=$null;$created=@()
$marker=Join-Path $AppRoot 'runtime-data/companion/virtual-device-managed.json'
try{
 $service.Connect();$folder=$service.GetFolder('\')
 foreach($item in $tasks){
  $existing=$null
  try{$existing=$folder.GetTask($item.name)}catch{if($_.Exception.HResult -ne -2147024894){throw}}
  if($existing){
   try{
    $definition=$existing.Definition
    if($definition.RegistrationInfo.Description -ne $owner -or ([xml]$existing.Xml).Task.Principals.Principal.UserId -ne 'S-1-5-18' -or $definition.Triggers.Count -ne 0 -or $definition.Actions.Count -ne 1 -or $definition.Actions.Item(1).Path -ne $program -or $definition.Actions.Item(1).Arguments -cne $item.arguments){throw 'A conflicting task exists; it was not replaced.'}
   }finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($existing)}
   continue
  }
  $definition=$service.NewTask(0)
  try{
   $definition.RegistrationInfo.Description=$owner
   $definition.Principal.UserId='S-1-5-18';$definition.Principal.LogonType=5;$definition.Principal.RunLevel=1
   $definition.Settings.Enabled=$true;$definition.Settings.Hidden=$true;$definition.Settings.AllowDemandStart=$true
   $definition.Settings.DisallowStartIfOnBatteries=$false;$definition.Settings.StopIfGoingOnBatteries=$false
   $definition.Settings.MultipleInstances=2;$definition.Settings.ExecutionTimeLimit='PT30S'
   $action=$definition.Actions.Create(0);$action.Path=$program;$action.Arguments=$item.arguments
   $registered=$folder.RegisterTaskDefinition($item.name,$definition,2,'SYSTEM',$null,5,"O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;$UserSid)")
   $created+=$item.name
   try{if($registered.Definition.Actions.Item(1).Arguments -cne $item.arguments -or ([xml]$registered.Xml).Task.Principals.Principal.UserId -ne 'S-1-5-18'){throw 'Task verification failed.'}}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($registered)}
  }finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($definition)}
 }
 $record=@{version=1;userSid=$UserSid;hardwareId=$hardware}|ConvertTo-Json
 if(Test-Path -LiteralPath $marker){if((Get-Content -LiteralPath $marker -Raw -Encoding UTF8|ConvertFrom-Json).userSid -ne $UserSid){throw 'A different user already owns virtual-display management.'}}
 $directory=Split-Path $marker
 New-Item -ItemType Directory -Path $directory -Force|Out-Null
 $temp=Join-Path $directory ('virtual-device-'+[guid]::NewGuid().ToString('N')+'.tmp')
 try{[IO.File]::WriteAllText($temp,$record,[Text.UTF8Encoding]::new($false));if(Test-Path -LiteralPath $marker){[IO.File]::Replace($temp,$marker,$null)}else{[IO.File]::Move($temp,$marker)}}finally{if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp}}
 $plan | ConvertTo-Json -Depth 4
}catch{
 foreach($name in $created){try{$folder.DeleteTask($name,0)}catch{Write-Warning "Could not roll back task $name"}}
 throw
}finally{if($folder){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($folder)};[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($service)}
