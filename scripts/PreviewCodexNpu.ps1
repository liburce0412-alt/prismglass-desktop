param([string]$Root='D:/DevTools/PrismGlass/npu')
$ErrorActionPreference='Stop'
$Root=(Resolve-Path -LiteralPath $Root).Path
$python=Join-Path $Root 'venv/Scripts/pythonw.exe'
if(!(Test-Path -LiteralPath $python)){throw '请先按 docs/NPU-TOOLS.md 准备 NPU 运行环境。'}
$preview=Join-Path (Split-Path $PSScriptRoot) 'tools/PrismNpu/window_preview.py'
Start-Process -FilePath $python -ArgumentList @('-B','-X','utf8',('"'+$preview+'"'),'--root',('"'+$Root+'"')) -WindowStyle Normal
