param([ValidateSet('Toggle','Sky','Astro')][string]$Theme='Toggle')
$ErrorActionPreference='Stop'
$glass=Join-Path $PSScriptRoot 'LiquidDesktop'
$selected=if($Theme -eq 'Toggle'){if((Get-Content (Join-Path $glass 'theme.txt') -Raw).Trim() -eq 'Astro'){'Sky'}else{'Astro'}}else{$Theme}
[IO.File]::WriteAllText((Join-Path $glass 'theme.txt'),$selected)
if(!(Get-Process LiquidDesktop -ErrorAction SilentlyContinue) -and !(Test-Path (Join-Path $PSScriptRoot 'desktop-mode-state.json'))){Start-Process (Join-Path $glass 'LiquidDesktop.exe') -WindowStyle Hidden}
