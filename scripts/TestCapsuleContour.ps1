param([string]$ComparisonImagePath,[string]$CacheRoot="D:/Cache/PrismGlass",[string]$Browser="$env:ProgramFiles\Google\Chrome\Application\chrome.exe")
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$profile=Join-Path ([IO.Path]::GetTempPath()) ('prism-capsule-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $profile | Out-Null
try {
 $page=[Uri]::new((Resolve-Path (Join-Path $repo 'tests/TestCapsuleContour.html')).Path)
 $info=[Diagnostics.ProcessStartInfo]::new()
 $info.FileName=$Browser
 $info.UseShellExecute=$false
 $info.CreateNoWindow=$true
 $info.RedirectStandardOutput=$true
 foreach($argument in @('--headless','--disable-background-networking','--no-first-run',"--user-data-dir=$profile",'--virtual-time-budget=5000','--dump-dom',$page.AbsoluteUri)){$info.ArgumentList.Add($argument)}
 $process=[Diagnostics.Process]::Start($info)
 try {
  $output=$process.StandardOutput.ReadToEndAsync()
  if(!$process.WaitForExit(20000)){$process.Kill($true);throw 'Capsule pixel fixture timed out'}
  $match=[regex]::Match($output.GetAwaiter().GetResult(),'<pre id="result">([^<]+)')
  if(!$match.Success){throw "Browser returned no capsule result (exit $($process.ExitCode))"}
  $result=[Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
  if(!$result.StartsWith('PASS ')){throw $result}
  $evidence=$result.Substring(5) | ConvertFrom-Json
  $inputPath=Join-Path $profile 'rasters.json'
  ConvertTo-Json -InputObject $evidence.nativeCases -Depth 8 -Compress | Set-Content -LiteralPath $inputPath -Encoding utf8
  $sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47/lib/net462'
  foreach($dll in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath "$sdk/$dll" -Destination $profile}
  $sources=@('tests/TestCapsuleNative.cs','src/LiquidDesktop/GlassWindow.cs','src/LiquidDesktop/DesktopVisibilityPolicy.cs') | ForEach-Object {Join-Path $repo $_}
  & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /nowarn:0649 /target:exe "/out:$profile/native.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$profile/Microsoft.Web.WebView2.Core.dll" "/r:$profile/Microsoft.Web.WebView2.WinForms.dll" @sources
  if($LASTEXITCODE){throw 'Native region fixture compilation failed'}
  & "$profile/native.exe" $inputPath
  if($LASTEXITCODE){throw 'Native region diverged from rendered pixels'}
  if($ComparisonImagePath){[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($ComparisonImagePath),[Convert]::FromBase64String($evidence.comparison))}
  $evidence | Select-Object metrics,edge,upload,samples,renderer | ConvertTo-Json -Depth 5 -Compress
 }finally{$process.Dispose()}
}finally{
 $full=(Resolve-Path -LiteralPath $profile).Path
 if([IO.Path]::GetDirectoryName($full) -ne [IO.Path]::GetTempPath().TrimEnd('\') -or [IO.Path]::GetFileName($full) -notmatch '^prism-capsule-[a-f0-9]{32}$'){throw 'Unexpected capsule fixture path'}
 Remove-Item -LiteralPath $full -Recurse -Force
}
