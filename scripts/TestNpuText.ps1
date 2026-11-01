param([Parameter(Mandatory=$true)][string]$NpuRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/npu-text-runtime'))
if(Test-Path -LiteralPath $fixture){throw 'Inspect the existing NPU test runtime before retrying'}
try{
 New-Item -ItemType Directory -Path (Join-Path $fixture 'runtime-data'),(Join-Path $fixture 'PrismNpu') -Force | Out-Null
 Copy-Item -LiteralPath (Join-Path $repo 'tools/PrismNpu/main.py') -Destination (Join-Path $fixture 'PrismNpu/main.py')
 @{python=(Join-Path $NpuRoot 'venv/Scripts/python.exe');root=$NpuRoot} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'runtime-data/npu.json') -Encoding utf8
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /nowarn:0649 /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$fixture/TestNpuText.exe" (Join-Path $repo 'src/LiquidDesktop/NpuText.cs') (Join-Path $repo 'tests/TestNpuText.cs')
 if($LASTEXITCODE -ne 0){throw 'NPU host test compilation failed'}
 & (Join-Path $fixture 'TestNpuText.exe') $fixture (Join-Path $repo 'artifacts/experience-polish-20261031/npu/images/source.png') | Tee-Object -FilePath (Join-Path $repo 'artifacts/experience-polish-20261031/npu/host-checks.txt')
 if($LASTEXITCODE -ne 0){throw 'NPU host checks failed'}
}finally{
 if($fixture -ne [IO.Path]::GetFullPath((Join-Path $repo 'artifacts/npu-text-runtime'))){throw 'Unexpected fixture cleanup path'}
 if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force}
 if(Test-Path -LiteralPath $fixture){throw 'NPU fixture cleanup incomplete'}
}
