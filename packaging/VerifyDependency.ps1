param([Parameter(Mandatory=$true)][string]$Path)
$ErrorActionPreference='Stop'
try {
 if(!(Test-Path -LiteralPath $Path -PathType Leaf)){throw 'Installer file is missing.'}
 $signature=Get-AuthenticodeSignature -LiteralPath $Path
 if($signature.Status -ne 'Valid' -or !$signature.SignerCertificate){throw 'Installer signature is not valid.'}
 if($signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -cne 'Microsoft Corporation'){throw 'Installer publisher is not Microsoft Corporation.'}
 exit 0
}catch{Write-Error $_ -ErrorAction Continue;exit 1}
