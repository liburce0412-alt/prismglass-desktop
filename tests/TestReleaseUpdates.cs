using System;
using System.IO;
using System.Net;
using System.Net.Http;
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
 static HttpResponseMessage Response(HttpStatusCode code,string body){return new HttpResponseMessage(code){Content=new StringContent(body)};}
 static void Reject(Action action,string name){try{action();}catch(IOException){Check(true,name);return;}throw new Exception(name);}
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
 }
 static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Release updates: "+checks+" checks passed (no network or settings changes).");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
