param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $SourceRoot).Path
$repo=Split-Path (Split-Path $PSScriptRoot)
$out=Join-Path $repo 'artifacts\tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$fw=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs=@("/r:$fw\WPF\PresentationCore.dll","/r:$fw\WPF\PresentationFramework.dll","/r:$fw\WPF\WindowsBase.dll","/r:$fw\System.Xaml.dll",'/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:Microsoft.VisualBasic.dll','/r:System.dll','/r:System.Core.dll')
$sources=@(Get-ChildItem -LiteralPath (Join-Path $source 'src') -Filter '*.cs' | ForEach-Object FullName)
$exe=Join-Path $out 'TestDiscard.exe'
& (Join-Path $fw 'csc.exe') /nologo /target:exe /main:TestDiscard ("/out:$exe") @refs @sources (Join-Path $source 'tools\TestDiscard.cs')
if($LASTEXITCODE){throw 'Discard test compilation failed'}
& $exe
if($LASTEXITCODE){throw 'Discard test failed'}
