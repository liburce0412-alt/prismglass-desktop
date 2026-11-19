using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal static class TestReleaseUpdates {
 sealed class Fake : HttpMessageHandler {
  public int calls;public string uri,agent;public Func<CancellationToken,Task<HttpResponseMessage>> reply;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){calls++;uri=request.RequestUri.AbsoluteUri;agent=request.Headers.UserAgent.ToString();return reply(cancel);}
 }
 static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static string Release(string tag="v0.9.1",string url=null){return "{\"draft\":false,\"prerelease\":false,\"tag_name\":\""+tag+"\",\"html_url\":\""+(url??"https://github.com/liburce0412-alt/prismglass-desktop/releases/tag/"+tag)+"\"}";}
 const string FixtureName="PrismGlass-Setup-0.9.1-x64.exe",FixtureUrl="https://github.com/liburce0412-alt/prismglass-desktop/releases/download/v0.9.1/"+FixtureName;
 const string FixtureCdn="https://release-assets.githubusercontent.com/github-production-release-asset/123/00000000-0000-0000-0000-000000000000?sig=test";
 static string ReleaseWithAsset(string digest){return Release().TrimEnd('}')+",\"assets\":[{\"name\":\""+FixtureName+"\",\"browser_download_url\":\""+FixtureUrl+"\",\"digest\":\"sha256:"+digest+"\",\"state\":\"uploaded\",\"size\":3}]}";}
 static HttpResponseMessage Response(HttpStatusCode code,string body){return new HttpResponseMessage(code){Content=new StringContent(body)};}
 static void Reject(Action action,string name){try{action();}catch(IOException){Check(true,name);return;}throw new Exception(name);}
 static async Task DownloadChecks(){
  string sha256;using(var hash=SHA256.Create())sha256=BitConverter.ToString(hash.ComputeHash(Encoding.ASCII.GetBytes("abc"))).Replace("-","").ToLowerInvariant();
  string json=ReleaseWithAsset(sha256);var release=ReleaseUpdates.Parse(json,"0.9.0");
  Check(release.installer!=null&&release.installer.url==FixtureUrl&&release.installer.name==FixtureName&&release.installer.sha256==sha256&&release.installer.size==3,"exact x64 asset has official SHA-256 and bounded size");
  foreach(string invalid in new[]{json.Replace("liburce0412-alt/prismglass-desktop/releases/download","fork/prismglass-desktop/releases/download"),json.Replace("sha256:",""),json.Replace("\"size\":3","\"size\":268435457"),json.Replace("\"size\":3","\"size\":3.5"),json.Replace("uploaded","new"),json.Replace("x64.exe","arm64.exe")}){
   var result=ReleaseUpdates.Parse(invalid,"0.9.0");Check(result.state=="available"&&result.installer==null,"invalid or unverified installer retains the manual release link only");
  }
  Check(AutoUpdateDownload.IsAssetRedirect(new Uri(FixtureCdn)),"only GitHub release CDN asset path is eligible for redirect");
  foreach(string redirect in new[]{FixtureCdn.Replace("https:","http:"),FixtureCdn.Replace(".com/",".com.invalid/"),FixtureCdn.Replace(".com/",".com:444/"),FixtureCdn.Replace("https://","https://user@"),FixtureCdn.Replace("github-production-release-asset","unrelated"),FixtureCdn+"#fragment","/relative"})
   Check(!AutoUpdateDownload.IsAssetRedirect(new Uri(redirect,UriKind.RelativeOrAbsolute)),"unsafe redirect rejected: "+redirect);
  string root=Path.Combine(Path.GetTempPath(),"PrismGlassUpdateTest-"+Guid.NewGuid().ToString("N")),runtime=Path.Combine(root,"runtime-data"),directory=Path.Combine(runtime,"updates");
  var fake=new Fake();using(var client=new HttpClient(fake))try{
   fake.reply=cancel=>Task.FromResult(Response(HttpStatusCode.OK,"bad"));
   try{await AutoUpdateDownload.Stage(client,directory,release,p=>{},CancellationToken.None);throw new Exception("unexpected verification success");}catch(IOException){}
   Check(Directory.GetFiles(directory).Length==0,"failed hash leaves neither staged package nor partial file");
   fake.reply=cancel=>{var response=Response(HttpStatusCode.Redirect,"");response.Headers.Location=new Uri("https://example.com/asset.exe");return Task.FromResult(response);};
   int before=fake.calls;
   try{await AutoUpdateDownload.Stage(client,directory,release,p=>{},CancellationToken.None);throw new Exception("unexpected redirect success");}catch(IOException){}
   Check(Directory.GetFiles(directory).Length==0&&fake.calls==before+1,"download rejects a foreign redirect before contacting it");
   fake.reply=cancel=>{var response=Response(HttpStatusCode.Redirect,"");response.Headers.Location=new Uri(FixtureCdn);return Task.FromResult(fake.uri==FixtureUrl?response:Response(HttpStatusCode.OK,"abc"));};
   int percent=0;string manifest=await AutoUpdateDownload.Stage(client,directory,release,p=>percent=p,CancellationToken.None);
   Check(File.ReadAllText(Path.Combine(directory,FixtureName))=="abc"&&File.ReadAllText(manifest).Contains(sha256)&&percent==100&&fake.uri==FixtureCdn,"verified bytes and atomic manifest published after the allowlisted download");
   before=fake.calls;await AutoUpdateDownload.Stage(client,directory,release,p=>{},CancellationToken.None);
   Check(fake.calls==before&&Directory.GetFiles(directory).Length==2,"verified staged installer is reusable without downloading again");
   string payload=new string('a',256*1024),bulkDigest;
   using(var hash=SHA256.Create())bulkDigest=BitConverter.ToString(hash.ComputeHash(Encoding.ASCII.GetBytes(payload))).Replace("-","").ToLowerInvariant();
   var bulk=ReleaseUpdates.Parse(ReleaseWithAsset(bulkDigest).Replace("\"size\":3","\"size\":262144"),"0.9.0");
   fake.reply=cancel=>Task.FromResult(Response(HttpStatusCode.OK,payload));
   using(var cancel=new CancellationTokenSource()){
    try{await AutoUpdateDownload.Stage(client,directory,bulk,p=>cancel.Cancel(),cancel.Token);throw new Exception("unexpected staging success after cancellation");}catch(OperationCanceledException){}
   }
   Check(File.ReadAllText(Path.Combine(directory,FixtureName))=="abc"&&File.ReadAllText(manifest).Contains(sha256)&&Directory.GetFiles(directory).Length==2,"cancellation removes the partial download and preserves the previously verified stage");
   var rate=Stopwatch.StartNew();
   await AutoUpdateDownload.Stage(client,directory,bulk,p=>{},CancellationToken.None);
   Check(rate.ElapsedMilliseconds>=450,"download pacing leaves bandwidth for a live companion stream");
   release.state="ready";AutoUpdateDownload.WriteJson(Path.Combine(directory,"status.json"),release);
   int applies=0;using(var owner=new AutoUpdates(root,"0.9.0",()=>false,path=>{applies++;return true;})){
    Check(!owner.State.automaticEnabled&&owner.State.automaticSupported&&owner.State.state=="ready","auto-update is off by default and the shared staging status is visible");
    owner.SetEnabled(true);owner.Tick();Check(applies==0&&owner.State.automaticEnabled,"a pending update waits while restart is unsafe");
   }
   using(var viewer=new AutoUpdates(root,"0.9.0",()=>true,path=>{applies++;return true;})){viewer.Tick(false);Check(applies==0&&viewer.State.automaticEnabled,"desktop status viewer does not schedule while the companion owns it");}
   using(var busy=File.Open(Path.Combine(directory,"download.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
   using(var owner=new AutoUpdates(root,"0.9.0",()=>true,path=>{applies++;return true;})){owner.Tick();Check(applies==0,"an existing per-root staging lock blocks overlapping apply");}
   AutoUpdateDownload.WriteJson(Path.Combine(directory,"apply-result.json"),new{version="0.9.1",status="failed",message="test failed apply",atUtc=DateTime.UtcNow.ToString("o")});
   using(var owner=new AutoUpdates(root,"0.9.0",()=>true,path=>{applies++;return true;})){owner.Tick();Check(applies==0&&owner.State.state=="failed","failed update needs an explicit retry");}
  }finally{if(Directory.Exists(directory)){foreach(string file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);}if(Directory.Exists(runtime))Directory.Delete(runtime);if(Directory.Exists(root))Directory.Delete(root);}
 }
 static async Task Run(){
  string[] ordered={"1.0.0-alpha","1.0.0-alpha.1","1.0.0-alpha.beta","1.0.0-beta","1.0.0-beta.2","1.0.0-beta.11","1.0.0-rc.1","1.0.0"};for(int i=1;i<ordered.Length;i++)Check(ReleaseUpdates.CompareVersions(ordered[i],ordered[i-1])>0,"semantic version precedence "+ordered[i]);
  Check(ReleaseUpdates.CompareVersions("v0.9.1+build.7","0.9.1+build.9")==0,"build metadata does not change precedence");
  Check(ReleaseUpdates.CompareVersions("0.10.0","0.9.99")>0&&ReleaseUpdates.CompareVersions("999999999999999999999.0.0","2.0.0")>0,"numeric components do not compare lexically or overflow");
  Reject(()=>ReleaseUpdates.CompareVersions("01.0.0","1.0.0"),"reject leading zero core version");Reject(()=>ReleaseUpdates.CompareVersions("1.0.0-01","1.0.0"),"reject leading zero prerelease number");
  var newer=ReleaseUpdates.Parse(Release(),"0.9.0-beta.3");Check(newer.state=="available"&&newer.latest=="0.9.1"&&newer.url.Length>0,"beta installation receives the stable update");
  Check(ReleaseUpdates.Parse(Release(),"0.9.1").state=="current"&&ReleaseUpdates.Parse(Release(),"0.9.2-beta.1").url=="","equal and newer installed versions do not offer a downgrade");
  Reject(()=>ReleaseUpdates.Parse(Release().Replace("\"prerelease\":false","\"prerelease\":true"),"0.9.0"),"preview flagged release is not offered");Reject(()=>ReleaseUpdates.Parse(Release("v0.9.2-beta.1"),"0.9.0"),"preview tag is rejected even if incorrectly marked stable");Reject(()=>ReleaseUpdates.Parse(Release().Replace("\"draft\":false","\"draft\":true"),"0.9.0"),"draft is not offered");
  foreach(string url in new[]{"http://github.com/liburce0412-alt/prismglass-desktop/releases/tag/v0.9.1","https://github.com.evil.test/liburce0412-alt/prismglass-desktop/releases/tag/v0.9.1","https://github.com/other/repo/releases/tag/v0.9.1","https://user@github.com/liburce0412-alt/prismglass-desktop/releases/tag/v0.9.1","https://github.com/liburce0412-alt/prismglass-desktop/releases/tag/v0.9.1?redirect=evil","https://github.com/liburce0412-alt/prismglass-desktop/releases/tag/v0.9.2","https://github.com:444/liburce0412-alt/prismglass-desktop/releases/tag/v0.9.1"})Check(!ReleaseUpdates.IsReleaseUrl(url,"v0.9.1"),"reject noncanonical release link "+url);
  Reject(()=>ReleaseUpdates.Parse(Release("v0.9.1","https://example.com/download"),"0.9.0"),"remote arbitrary link never reaches launch state");Reject(()=>ReleaseUpdates.Parse(new string(' ',262145),"0.9.0"),"release response size is bounded");
  var pending=new TaskCompletionSource<HttpResponseMessage>();var fake=new Fake{reply=cancel=>pending.Task};using(var client=new HttpClient(fake)){
   client.MaxResponseContentBufferSize=262144;var task=ReleaseUpdates.Check(client,"0.9.0",CancellationToken.None);Check(!task.IsCompleted,"network wait does not block the caller");Check(fake.calls==1&&fake.uri==ReleaseUpdates.Endpoint&&fake.agent.Contains("PrismGlass"),"only the fixed official API is requested with a user agent");pending.SetResult(Response(HttpStatusCode.OK,Release()));Check((await task).state=="available","async response publishes the update");
   fake.reply=cancel=>Task.FromResult(Response(HttpStatusCode.ServiceUnavailable,""));Check((await ReleaseUpdates.Check(client,"0.9.0",CancellationToken.None)).state=="error","network failure is a retryable state");
   fake.reply=cancel=>Task.FromResult(Response(HttpStatusCode.OK,Release()));Check((await ReleaseUpdates.Check(client,"0.9.0",CancellationToken.None)).state=="available","retry succeeds without retaining an old error");
   fake.reply=cancel=>Task.FromResult(Response(HttpStatusCode.Redirect,""));Check((await ReleaseUpdates.Check(client,"0.9.0",CancellationToken.None)).state=="error","redirect response is never release metadata");
   int before=fake.calls;fake.reply=cancel=>{if(fake.calls==before+1)return Task.FromResult(Response(HttpStatusCode.Forbidden,""));var redirect=Response(HttpStatusCode.Redirect,"");redirect.Headers.Location=new Uri("https://github.com/liburce0412-alt/prismglass-desktop/releases/tag/v0.9.1");return Task.FromResult(redirect);};Check((await ReleaseUpdates.Check(client,"0.9.0",CancellationToken.None)).state=="available"&&fake.calls==before+2&&fake.uri==ReleaseUpdates.LatestPage,"API rate limit falls back only to the verified official latest-page redirect");
   Reject(()=>ReleaseUpdates.ParseRedirect(HttpStatusCode.Redirect,new Uri("https://example.com/liburce0412-alt/prismglass-desktop/releases/tag/v0.9.1"),"0.9.0"),"fallback rejects redirects outside the official repository");
   Reject(()=>ReleaseUpdates.ParseRedirect(HttpStatusCode.Redirect,new Uri("https://github.com/liburce0412-alt/prismglass-desktop/releases/tag/v0.9.2-beta.1"),"0.9.0"),"fallback cannot offer a prerelease as stable");
   fake.reply=cancel=>Task.FromResult(Response(HttpStatusCode.Forbidden,""));Check((await ReleaseUpdates.Check(client,"0.9.0",CancellationToken.None)).message.Contains("官方发布页"),"failed fallback keeps an actionable Chinese status");
   fake.reply=cancel=>Task.FromResult(Response(HttpStatusCode.OK,new string('x',262145)));Check((await ReleaseUpdates.Check(client,"0.9.0",CancellationToken.None)).state=="error","oversized HTTP body is rejected before parsing");
   fake.reply=cancel=>{throw new TaskCanceledException();};Check((await ReleaseUpdates.Check(client,"0.9.0",CancellationToken.None)).message.Contains("超时"),"transport timeout is shown in Chinese");
   using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool cancelled=false;try{await ReleaseUpdates.Check(client,"0.9.0",cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"closing the window cancels without an error result");}
  }
  await DownloadChecks();
 }
 static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Release updates: "+checks+" checks passed (no network or settings changes).");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
