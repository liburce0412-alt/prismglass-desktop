param([ValidateSet('Start','Stop')][string]$Mode='Start')
$ErrorActionPreference='Stop'
$config=Join-Path $PSScriptRoot 'pegline-path.txt'
$marker=Join-Path $PSScriptRoot 'pegline-session.json'
if($Mode -eq 'Stop'){
 if(!(Test-Path -LiteralPath $marker)){return}
 $saved=Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
 $process=Get-Process -Id $saved.Id -ErrorAction SilentlyContinue
 if($process -and $process.Path -eq $saved.Path -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq $saved.StartTicks){
  [void]$process.CloseMainWindow()
  if(!$process.WaitForExit(1500)){Stop-Process -Id $process.Id}
 }
 Remove-Item -LiteralPath $marker
 return
}
if(!(Test-Path -LiteralPath $config)){return}
$exe=(Get-Content -LiteralPath $config -Raw).Trim()
if(!(Test-Path -LiteralPath $exe -PathType Leaf)){Write-Warning 'Pegline executable is missing; desktop continues without it.';return}
if(Get-Process Pegline -ErrorAction SilentlyContinue){return}
$process=Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru
@{Id=$process.Id;Path=$exe;StartTicks=$process.StartTime.ToUniversalTime().Ticks.ToString()} | ConvertTo-Json | Set-Content -LiteralPath $marker
'Pegline started with the beauty session.'
