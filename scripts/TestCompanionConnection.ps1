$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$binary=Join-Path $out 'TestCompanionConnection.exe'
& $csc /nologo /platform:x64 ('/out:'+$binary) /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Management.dll /r:System.Web.Extensions.dll (Join-Path $repo 'src/LiquidDesktop/CompanionServer.cs') (Join-Path $repo 'src/LiquidDesktop/CompanionConnection.cs') (Join-Path $repo 'tests/TestCompanionConnection.cs')
if($LASTEXITCODE -ne 0){throw 'Companion connection test compilation failed.'}
$certificateFile=Join-Path $out ('companion-public-fixture-'+[guid]::NewGuid().ToString('N')+'.cer')
$key=[Security.Cryptography.RSA]::Create(2048);$certificate=$null
try{
 $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=PrismGlass Companion 192.168.1.50',$key,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
 $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true,$true,0,$true))
 $certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1),[DateTimeOffset]::UtcNow.AddDays(1))
 [IO.File]::WriteAllBytes($certificateFile,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
 $shellName=if($PSVersionTable.PSVersion.Major -ge 7){'pwsh.exe'}else{'powershell.exe'}
 & $binary (Join-Path $PSHOME $shellName) $certificateFile
 if($LASTEXITCODE -ne 0){throw 'Companion connection checks failed.'}
}finally{if($certificate){$certificate.Dispose()};$key.Dispose();if([IO.File]::Exists($certificateFile)){[IO.File]::Delete($certificateFile)}}
