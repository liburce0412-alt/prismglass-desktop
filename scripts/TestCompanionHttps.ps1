param(
 [Parameter(Mandatory=$true)][string]$SetupDirectory
)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$record=Get-Content -LiteralPath (Join-Path $SetupDirectory 'setup-record.json') -Raw | ConvertFrom-Json
$out=Join-Path $repo 'artifacts/companion-deploy'
$fixture=Join-Path $out ('https-'+[guid]::NewGuid().ToString('N'))
$process=$null
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try{
 Copy-Item -LiteralPath (Join-Path $repo 'src/LiquidDesktop/companion-web') -Destination $fixture -Recurse
 @'
using System;using System.IO;using System.Threading;
internal static class HttpsProbe {
 static int Main(string[] args){try{using(var server=new CompanionServer(args[0],(path,body,grant)=>CompanionReply.Json(503,new{error="probe_only"}),()=>false)){
  server.Start(args[1],Int32.Parse(args[2]));File.WriteAllText(Path.Combine(args[0],"ready"),"ready");
  var deadline=DateTime.UtcNow.AddSeconds(30);while(!File.Exists(Path.Combine(args[0],"stop"))&&DateTime.UtcNow<deadline)Thread.Sleep(100);
  server.Stop();return 0;
 }}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
'@ | Set-Content -LiteralPath (Join-Path $fixture 'HttpsProbe.cs') -Encoding utf8
 @'
'use strict';
const https=require('node:https'),fs=require('node:fs'),assert=require('node:assert/strict'),{X509Certificate}=require('node:crypto');
const [setup,output]=process.argv.slice(2),path=require('node:path');
const record=JSON.parse(fs.readFileSync(path.join(setup,'setup-record.json'),'utf8').replace(/^\uFEFF/,''));
const ca=new X509Certificate(fs.readFileSync(path.join(setup,'PrismGlass-Companion-CA.cer'))).toString();
function read(route){return new Promise((resolve,reject)=>{
 const request=https.get(new URL(route,record.url),{ca,agent:false,minVersion:'TLSv1.2',maxVersion:'TLSv1.2',rejectUnauthorized:true},response=>{
  try{assert(response.socket.authorized);assert.equal(response.socket.getPeerCertificate().fingerprint.replaceAll(':',''),record.serverThumbprint);}
  catch(error){response.destroy();reject(error);return;}
  const protocol=response.socket.getProtocol(),chunks=[];
  response.on('data',chunk=>chunks.push(chunk));response.on('end',()=>resolve({status:response.statusCode,headers:response.headers,body:Buffer.concat(chunks).toString('utf8'),protocol}));response.on('error',reject);
 });request.setTimeout(5000,()=>request.destroy(new Error('HTTPS probe timeout')));request.on('error',reject);
});}
(async()=>{
 const page=await read('/');assert.equal(page.status,200);assert.match(page.body,/PrismGlass/);assert.match(page.headers['cache-control'],/no-store/);assert.match(page.headers['content-security-policy'],/default-src 'self'/);
 const session=await read('/api/session');assert.equal(session.status,401);JSON.parse(session.body);
 const state=await read('/api/state');assert.equal(state.status,401);assert(!state.body.includes('tasks'));
 const report={success:true,url:record.url,protocol:page.protocol,certificateChain:'dedicated CA validated; IP identity and server fingerprint checked',pageStatus:page.status,unpairedSession:session.status,unpairedState:state.status,utc:new Date().toISOString()};
 fs.writeFileSync(output,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
})().catch(error=>{console.error(error);process.exitCode=1;});
'@ | Set-Content -LiteralPath (Join-Path $fixture 'client.cjs') -Encoding utf8
 $exe=Join-Path $fixture 'HttpsProbe.exe'
 $csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 & $csc /nologo /nowarn:0649 ("/out:$exe") /r:System.dll /r:System.Core.dll /r:System.Security.dll /r:System.Web.Extensions.dll (Join-Path $repo 'src/LiquidDesktop/CompanionServer.cs') (Join-Path $fixture 'HttpsProbe.cs')
 if($LASTEXITCODE -ne 0){throw 'HTTPS probe compilation failed.'}
 $stderr=Join-Path $out 'https-probe-errors.txt'
 $process=Start-Process -FilePath $exe -ArgumentList @(('"'+$fixture+'"'),$record.address,[string]$record.port) -WindowStyle Hidden -RedirectStandardError $stderr -PassThru
 $deadline=[DateTime]::UtcNow.AddSeconds(8)
 while(!(Test-Path -LiteralPath (Join-Path $fixture 'ready')) -and !$process.HasExited -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 100}
 if(!(Test-Path -LiteralPath (Join-Path $fixture 'ready'))){Get-Content -LiteralPath $stderr;throw 'HTTPS listener did not become ready.'}
 & node (Join-Path $fixture 'client.cjs') $SetupDirectory (Join-Path $out 'https-checks.json')
 if($LASTEXITCODE -ne 0){throw 'HTTPS behavior check failed.'}
 New-Item -ItemType File -Path (Join-Path $fixture 'stop') | Out-Null
 if(!$process.WaitForExit(5000) -or $process.ExitCode -ne 0){throw 'HTTPS probe did not exit cleanly.'}
 if(@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -EQ $record.port).Count){throw 'HTTPS port still has a listener after probe exit.'}
 'Production HTTPS listener stopped; port released.'
 if((Get-Item -LiteralPath $stderr).Length -eq 0){Remove-Item -LiteralPath $stderr}
}finally{
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 if(Test-Path -LiteralPath $fixture){$resolved=(Resolve-Path -LiteralPath $fixture).Path;$boundary=[IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar;if(!$resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'HTTPS fixture escaped boundary.'};Remove-Item -LiteralPath $resolved -Recurse -Force;if(Test-Path -LiteralPath $resolved){throw 'HTTPS fixture cleanup failed.'}}
}
