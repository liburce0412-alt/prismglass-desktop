param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [string]$OutputRoot
)
$ErrorActionPreference = 'Stop'
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
if (!$OutputRoot) { $OutputRoot = Join-Path $SourceRoot 'build\gravity-tests' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
[IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$physicsTest = Join-Path $OutputRoot 'TestGravity.exe'
& $compiler /nologo /target:exe /optimize+ "/out:$physicsTest" (Join-Path $SourceRoot 'src\RopePhysics.cs') (Join-Path $SourceRoot 'tools\TestGravity.cs')
if ($LASTEXITCODE) { throw 'Physics test compilation failed.' }
& $physicsTest
if ($LASTEXITCODE) { throw 'Physics checks failed.' }
$references = @(
    "/r:$wpf\PresentationCore.dll", "/r:$wpf\PresentationFramework.dll", "/r:$wpf\WindowsBase.dll",
    "/r:$framework\System.Xaml.dll", '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    '/r:Microsoft.VisualBasic.dll', '/r:System.dll', '/r:System.Core.dll'
)
$viewTest = Join-Path $OutputRoot 'TestGravityView.exe'
$sources = (Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'src') -Filter '*.cs').FullName
& $compiler /nologo /target:exe /main:TestGravityView "/out:$viewTest" $references $sources (Join-Path $SourceRoot 'tools\TestGravityView.cs')
if ($LASTEXITCODE) { throw 'WPF test compilation failed.' }
& $viewTest (Join-Path $OutputRoot 'fixtures')
if ($LASTEXITCODE) { throw 'WPF checks failed.' }
Write-Output "Gravity checks passed. Generated WPF renders: $OutputRoot\fixtures"
