# Uses Windows PowerShell 5.1 solely for the same .NET Framework WinRT projection as the media helper.
param([Parameter(Mandatory=$true)][string]$FixtureRoot)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $FixtureRoot){throw 'Use a new disposable fixture directory.'}
$repo=Split-Path $PSScriptRoot
$full=[IO.Path]::GetFullPath($FixtureRoot)
$boundary=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/tests'))+[IO.Path]::DirectorySeparatorChar
if(!$full.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture must remain under artifacts/tests.'}
New-Item -ItemType Directory -Path $full | Out-Null
try{
 Add-Type -AssemblyName System.Runtime.WindowsRuntime
 Add-Type -AssemblyName System.Drawing
 Add-Type -Path (Join-Path $repo 'src/LiquidDesktop/MediaCoverReader.cs')
 $null=[Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
 $null=[Windows.Storage.Streams.IRandomAccessStreamWithContentType,Windows.Storage.Streams,ContentType=WindowsRuntime]
 $null=[Windows.Storage.Streams.IRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
 $null=[Windows.Storage.Streams.RandomAccessStreamReference,Windows.Storage.Streams,ContentType=WindowsRuntime]
 $asTask=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {$_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'} | Select-Object -First 1
 $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'src/LiquidDesktop/MediaSession.ps1'),[ref]$tokens,[ref]$errors)
 foreach($name in @('Await-WinRT','Save-MediaCover')){$function=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true);. ([scriptblock]::Create($function.Extent.Text.Replace("}catch{return ''}finally{","}catch{throw}finally{")))}
 $DataRoot=Join-Path $full 'cache';New-Item -ItemType Directory -Path $DataRoot | Out-Null
 $file=Join-Path $full 'sample.png';$bitmap=New-Object Drawing.Bitmap 32,32;$graphics=[Drawing.Graphics]::FromImage($bitmap)
 try{$graphics.Clear([Drawing.Color]::CornflowerBlue);$bitmap.Save($file,[Drawing.Imaging.ImageFormat]::Png)}finally{$graphics.Dispose();$bitmap.Dispose()}
 $memory=[Windows.Storage.Streams.InMemoryRandomAccessStream]::new();$output=$memory.GetOutputStreamAt(0);$writer=[System.IO.WindowsRuntimeStreamExtensions]::AsStreamForWrite($output);$bytes=[IO.File]::ReadAllBytes($file);$writer.Write($bytes,0,$bytes.Length);$writer.Flush();$memory.Seek(0)
 $thumbnail=[Windows.Storage.Streams.RandomAccessStreamReference]::CreateFromStream($memory)
 $name=Save-MediaCover $thumbnail
 if($name -notmatch '^session-cover-[a-f0-9]{16}\.bin$' -or !(Test-Path -LiteralPath (Join-Path $DataRoot $name))){throw 'Projected thumbnail did not produce a cached image.'}
 if((Get-Item -LiteralPath (Join-Path $DataRoot $name)).Length -ne (Get-Item -LiteralPath $file).Length){throw 'Thumbnail bytes changed.'}
 'PASS real WinRT thumbnail stream is copied without altering source'
 1..9 | ForEach-Object {$null=Save-MediaCover $thumbnail}
 if(@(Get-ChildItem -LiteralPath $DataRoot -Filter 'session-cover-*.bin' -File).Count -ne 8){throw 'Cover cache is not bounded.'}
 'PASS generated media thumbnail cache retains at most eight images'
 if((Save-MediaCover $null) -ne ''){throw 'Missing thumbnail should return empty.'}
 'PASS missing thumbnail permits wallpaper fallback'
}catch{Write-Output $_.Exception.ToString();throw}finally{if(Test-Path -LiteralPath $full){Remove-Item -LiteralPath $full -Recurse -Force;if(Test-Path -LiteralPath $full){throw 'Fixture cleanup failed.'}}}
