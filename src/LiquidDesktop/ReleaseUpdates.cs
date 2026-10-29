using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class ReleaseUpdateState {
 public string current,latest="",tag="",url="",state="idle",message="点击检查 GitHub 上的最新正式版本。";
}
internal static class ReleaseUpdates {
 internal const string Endpoint="https://api.github.com/repos/liburce0412-alt/prismglass-desktop/releases/latest";
 internal const string LatestPage="https://github.com/liburce0412-alt/prismglass-desktop/releases/latest";
 const string ReleasePath="/liburce0412-alt/prismglass-desktop/releases/tag/";
 const int Limit=262144;
 static readonly HttpClient client=CreateClient();
 static readonly Regex versionPattern=new Regex(@"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z",RegexOptions.CultureInvariant);
 static HttpClient CreateClient(){var value=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,SslProtocols=System.Security.Authentication.SslProtocols.Tls12});value.Timeout=TimeSpan.FromSeconds(10);value.MaxResponseContentBufferSize=Limit;return value;}
 static bool Numeric(string value){foreach(char c in value)if(c<'0'||c>'9')return false;return value.Length>0;}
 static Match Version(string value){
  if(value==null||value.Length>128)throw new IOException("版本信息无效，请稍后重试。");
  var match=versionPattern.Match(value);if(!match.Success)throw new IOException("版本信息无效，请稍后重试。");
  foreach(string part in match.Groups[4].Value.Split('.'))if(Numeric(part)&&part.Length>1&&part[0]=='0')throw new IOException("版本信息无效，请稍后重试。");return match;
 }
 static int CompareNumber(string a,string b){return a.Length==b.Length?String.CompareOrdinal(a,b):a.Length.CompareTo(b.Length);}
 internal static int CompareVersions(string left,string right){
  var a=Version(left);var b=Version(right);
  for(int i=1;i<=3;i++){int order=CompareNumber(a.Groups[i].Value,b.Groups[i].Value);if(order!=0)return order;}
  string ap=a.Groups[4].Value,bp=b.Groups[4].Value;if(ap==bp)return 0;if(ap.Length==0)return 1;if(bp.Length==0)return -1;
  string[] av=ap.Split('.'),bv=bp.Split('.');for(int i=0;i<Math.Min(av.Length,bv.Length);i++){bool an=Numeric(av[i]),bn=Numeric(bv[i]);int order=an&&bn?CompareNumber(av[i],bv[i]):an!=bn?(an?-1:1):String.CompareOrdinal(av[i],bv[i]);if(order!=0)return order;}return av.Length.CompareTo(bv.Length);
 }
 internal static bool IsReleaseUrl(string value,string tag){
  try{Version(tag);if(value==null||value.Length>512||value.IndexOf('\\')>=0)return false;foreach(char c in value)if(Char.IsWhiteSpace(c)||Char.IsControl(c))return false;Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)&&uri.Port==443&&uri.UserInfo.Length==0&&uri.Query.Length==0&&uri.Fragment.Length==0&&Uri.UnescapeDataString(uri.AbsolutePath)==ReleasePath+tag;}catch(IOException){return false;}
 }
 static ReleaseUpdateState FromTag(string name,string url,string current){
  Version(current);if(Version(name).Groups[4].Length>0||!IsReleaseUrl(url,name))throw new IOException("更新信息验证失败，请稍后重试。");
  int order=CompareVersions(name,current);bool newer=order>0;return new ReleaseUpdateState{current=current,latest=name.TrimStart('v'),tag=name,url=newer?"https://github.com"+ReleasePath+Uri.EscapeDataString(name):"",state=newer?"available":"current",message=newer?"发现新版本，可打开官方发布页下载安装。":order==0?"当前已是最新正式版本。":"当前版本高于最新正式版本，无需降级。"};
 }
 internal static ReleaseUpdateState Parse(string text,string current){
  Version(current);if(text==null||Encoding.UTF8.GetByteCount(text)>Limit)throw new IOException("更新服务返回的数据过大，请稍后重试。");
  var data=new JavaScriptSerializer{MaxJsonLength=Limit}.DeserializeObject(text) as Dictionary<string,object>;object draft,preview,tag,url;
  if(data==null||!data.TryGetValue("draft",out draft)||!Object.Equals(draft,false)||!data.TryGetValue("prerelease",out preview)||!Object.Equals(preview,false)||!data.TryGetValue("tag_name",out tag)||!(tag is string)||!data.TryGetValue("html_url",out url)||!(url is string))throw new IOException("没有取得有效的正式版本信息，请稍后重试。");
  return FromTag((string)tag,(string)url,current);
 }
 internal static ReleaseUpdateState ParseRedirect(HttpStatusCode status,Uri location,string current){
  if(status!=HttpStatusCode.Redirect||location==null||!location.IsAbsoluteUri||!location.AbsolutePath.StartsWith(ReleasePath,StringComparison.Ordinal))throw new IOException("GitHub 暂时无法提供更新信息，请重试或打开官方发布页。");
  return FromTag(Uri.UnescapeDataString(location.AbsolutePath.Substring(ReleasePath.Length)),location.AbsoluteUri,current);
 }
 static async Task<ReleaseUpdateState> CheckPage(HttpClient transport,string current,CancellationToken cancel){using(var request=new HttpRequestMessage(HttpMethod.Get,LatestPage)){
  request.Headers.UserAgent.ParseAdd("PrismGlass-UpdateCheck/1.0");
  using(var response=await transport.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel).ConfigureAwait(false))return ParseRedirect(response.StatusCode,response.Headers.Location,current);
 }}
 public static Task<ReleaseUpdateState> Check(string current,CancellationToken cancel){return Check(client,current,cancel);}
 internal static async Task<ReleaseUpdateState> Check(HttpClient transport,string current,CancellationToken cancel){
  try{using(var request=new HttpRequestMessage(HttpMethod.Get,Endpoint)){
   request.Headers.UserAgent.ParseAdd("PrismGlass-UpdateCheck/1.0");request.Headers.Accept.ParseAdd("application/vnd.github+json");
   using(var response=await transport.SendAsync(request,HttpCompletionOption.ResponseContentRead,cancel).ConfigureAwait(false)){
    if(response.StatusCode==HttpStatusCode.Forbidden||(int)response.StatusCode==429)return await CheckPage(transport,current,cancel).ConfigureAwait(false);
    if(!response.IsSuccessStatusCode)throw new IOException("暂时无法获取更新信息，请检查网络后重试。");
    string text=await response.Content.ReadAsStringAsync().ConfigureAwait(false);cancel.ThrowIfCancellationRequested();return Parse(text,current);
   }
  }}catch(OperationCanceledException){if(cancel.IsCancellationRequested)throw;return new ReleaseUpdateState{current=current,state="error",message="检查更新超时，请检查网络后重试。"};}
   catch(Exception ex){return new ReleaseUpdateState{current=current,state="error",message=ex is IOException?ex.Message:"无法检查更新，请检查网络后重试。"};}
 }
}
