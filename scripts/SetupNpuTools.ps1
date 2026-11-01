param([Parameter(Mandatory=$true)][string]$Python,[Parameter(Mandatory=$true)][string]$Root,[Parameter(Mandatory=$true)][string]$CacheRoot)
$ErrorActionPreference='Stop'
$Root=[IO.Path]::GetFullPath($Root)
$runtime=Join-Path $Root 'venv/Scripts/python.exe'
if(!(Test-Path -LiteralPath $runtime)){
 & $Python -m venv (Join-Path $Root 'venv')
 if($LASTEXITCODE -ne 0){throw 'NPU environment creation failed'}
}
& $runtime -m pip install --disable-pip-version-check --cache-dir $CacheRoot openvino==2026.4.1 opencv-python-headless==5.0.0.93 pillow==13.0.0
if($LASTEXITCODE -ne 0){throw 'NPU runtime installation failed'}
$models=Join-Path $Root 'models'
New-Item -ItemType Directory -Path $models -Force | Out-Null
$files=@(
 @{name='ocr-det.onnx';url='https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv5/det/ch_PP-OCRv5_det_mobile.onnx';algorithm='SHA256';digest='4d97c44a20d30a81aad087d6a396b08f786c4635742afc391f6621f5c6ae78ae'},
 @{name='ocr-rec.onnx';url='https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx';algorithm='SHA256';digest='5825fc7ebf84ae7a412be049820b4d86d77620f204a041697b0494669b1742c5'},
 @{name='super-resolution.xml';url='https://storage.openvinotoolkit.org/repositories/open_model_zoo/2023.0/models_bin/1/single-image-super-resolution-1032/FP16/single-image-super-resolution-1032.xml';algorithm='SHA384';digest='7b9f3995adf3a713295de26ff2a77b4375dc27975d44abf45e88d2bd2fede1cdcbdd04ee108e5845ed776cb0fd72d37d'},
 @{name='super-resolution.bin';url='https://storage.openvinotoolkit.org/repositories/open_model_zoo/2023.0/models_bin/1/single-image-super-resolution-1032/FP16/single-image-super-resolution-1032.bin';algorithm='SHA384';digest='4cee28140fe9e171c023690dec8376eff8937d7cae39998ebcad317b96268fa8c9e8ec226af088ea45a7664a920a7d92'}
)
foreach($file in $files){
 $path=Join-Path $models $file.name
 if(!(Test-Path -LiteralPath $path)){
  $partial=$path+'.partial'
  try{Invoke-WebRequest -Uri $file.url -OutFile $partial
   if((Get-FileHash -LiteralPath $partial -Algorithm $file.algorithm).Hash -ne $file.digest){throw ('Model checksum mismatch: '+$file.name)}
   Move-Item -LiteralPath $partial -Destination $path
  }finally{if(Test-Path -LiteralPath $partial){Remove-Item -LiteralPath $partial}}
 }elseif((Get-FileHash -LiteralPath $path -Algorithm $file.algorithm).Hash -ne $file.digest){throw ('Existing model differs from upstream: '+$file.name)}
}
$files | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $models 'sources.json') -Encoding utf8
Write-Output ('NPU tools prepared at '+$Root+'; application settings and drivers were not changed.')
