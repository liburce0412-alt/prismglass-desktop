param([string]$OutputRoot='artifacts/startup-progress-20261119/artwork')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=New-Item -ItemType Directory -Force (Join-Path $repo $OutputRoot)
$work=New-Item -ItemType Directory (Join-Path $out ('render-'+[guid]::NewGuid().ToString('N')))
try{
 $fw="$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319"
 $sources=@('tests/TestStartupArtwork.cs','src/LiquidDesktop/StartupArtwork.cs')|ForEach-Object{Join-Path $repo $_}
 $exe=Join-Path $work 'preview.exe'
 & "$fw/csc.exe" /nologo "/out:$exe" "/r:$fw/WPF/WindowsBase.dll" "/r:$fw/WPF/PresentationCore.dll" "/r:$fw/WPF/PresentationFramework.dll" "/r:$fw/WPF/UIAutomationTypes.dll" /r:System.Xaml.dll ('/resource:'+(Join-Path $repo 'assets/Brand/prismglass-logo-refined.png')+',PrismGlass.Startup.png') @sources
 if($LASTEXITCODE){throw 'Artwork fixture compilation failed'}
 $mp4=Join-Path $out 'startup-artwork.mp4'
 & $exe (Get-Command ffmpeg -ErrorAction Stop).Source $mp4 | Tee-Object -FilePath (Join-Path $out 'checks.txt')
 if($LASTEXITCODE){throw 'Artwork check failed'}
 foreach($frame in @{first=0;middle=36;complete=149}.GetEnumerator()){
  & ffmpeg -v error -y -i $mp4 -vf "select='eq(n,$($frame.Value))'" -frames:v 1 (Join-Path $out "startup-artwork-$($frame.Key).png")
  if($LASTEXITCODE){throw 'Preview frame export failed'}
 }
}finally{
 $path=(Resolve-Path -LiteralPath $work.FullName).Path
 if((Split-Path $path -Parent) -ne $out.FullName -or (Split-Path $path -Leaf) -notmatch '^render-[a-f0-9]{32}$'){throw 'Unexpected render directory'}
 Remove-Item -LiteralPath $path -Recurse -Force
}
