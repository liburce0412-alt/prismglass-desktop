param([string]$CacheRoot='D:\Cache\PrismGlass')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$fixture=Join-Path $repo ('artifacts/panel-motion-'+[guid]::NewGuid().ToString('N'))
$evidence=Join-Path $repo 'artifacts/tests/panel-motion.jsonl'
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
New-Item -ItemType Directory -Force -Path $fixture,(Split-Path $evidence)|Out-Null
try{
 Get-ChildItem -LiteralPath "$repo/src/LiquidDesktop" -File | Where-Object Extension -in '.js','.css','.html','.svg' | Copy-Item -Destination $fixture
 Add-Type -AssemblyName System.Drawing
 $bitmap=[Drawing.Bitmap]::new(1600,900);$graphics=[Drawing.Graphics]::FromImage($bitmap)
 try{$graphics.Clear([Drawing.Color]::CornflowerBlue);$graphics.FillEllipse([Drawing.Brushes]::LightSkyBlue,100,50,900,700);$bitmap.Save((Join-Path $fixture 'wallpaper.png'))}finally{$graphics.Dispose();$bitmap.Dispose()}
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item "$sdk/lib/net462/$name" $fixture}
 Copy-Item "$sdk/runtimes/win-x64/native/WebView2Loader.dll" $fixture
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /target:exe /platform:x64 "/out:$fixture/TestPanelMotion.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$fixture/Microsoft.Web.WebView2.Core.dll" "/r:$fixture/Microsoft.Web.WebView2.WinForms.dll" (Join-Path $repo 'tests/TestPanelMotion.cs')
 if($LASTEXITCODE -ne 0){throw 'Panel motion check did not compile'}
 [IO.File]::WriteAllText($evidence,'')
 $process=Start-Process -FilePath "$fixture/TestPanelMotion.exe" -ArgumentList ('"'+$fixture+'" "'+$evidence+'"') -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(20000)){throw 'Panel motion check timed out'}
 if($process.ExitCode -ne 0){throw "Panel motion failed: $evidence"}
 Get-Content -LiteralPath $evidence | ConvertFrom-Json | Select-Object page,samples,spanMs,medianMs,p95Ms,maxMs,selectionReads,shapeMessages,endpoint
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 $resolved=[IO.Path]::GetFullPath($fixture);$boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))+[IO.Path]::DirectorySeparatorChar
 if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped artifacts'}
 for($attempt=0;$attempt -lt 20;$attempt++){try{Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop;break}catch{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 250}}
 if(Test-Path -LiteralPath $resolved){throw 'Fixture cleanup failed'}
}
