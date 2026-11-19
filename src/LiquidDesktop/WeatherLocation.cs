using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class WeatherPlace {
 public string name;public double latitude,longitude;
}
internal static class WeatherLocation {
 const int Limit=262144;
 static readonly HttpClient client=CreateClient(Limit);
 // Weather is public HTTPS: a stopped local proxy must not take the collector offline.
 // This is request-local; it neither changes Windows proxy settings nor bypasses a TUN.
 internal static HttpClient CreateClient(int limit){var value=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false,UseCookies=false,SslProtocols=System.Security.Authentication.SslProtocols.Tls12});value.Timeout=TimeSpan.FromSeconds(6);value.MaxResponseContentBufferSize=limit;return value;}
 internal static async Task<string> GetString(HttpClient transport,string url,int limit,CancellationToken cancel){return await WithDnsRecovery(url,limit,cancel,async()=>{using(var response=await transport.GetAsync(url,HttpCompletionOption.ResponseContentRead,cancel).ConfigureAwait(false)){CheckResponse(response);return await response.Content.ReadAsStringAsync().ConfigureAwait(false);}}).ConfigureAwait(false);}
 internal static async Task<string> WithDnsRecovery(string url,int limit,CancellationToken cancel,Func<Task<string>> primary){
  try{return await primary().ConfigureAwait(false);}catch(HttpRequestException){cancel.ThrowIfCancellationRequested();}catch(OperationCanceledException){cancel.ThrowIfCancellationRequested();}
  // A VPN can leave a cached fake IP after exiting. Recover only this request;
  // the resolver receives a public API hostname, never a city or coordinates.
  return await WeatherDnsRecovery.Get(url,limit,cancel).ConfigureAwait(false);
 }
 internal sealed class ResponseError:IOException {internal readonly int Status;internal ResponseError(int status):base("天气服务返回 HTTP "+status+"，请稍后重试。"){Status=status;}}
 internal static void CheckResponse(HttpResponseMessage response){if(!response.IsSuccessStatusCode)throw new ResponseError((int)response.StatusCode);}
 internal static bool RetryModel(Exception error){var response=error as ResponseError;if(response!=null)return response.Status==400||response.Status==422;return !(error is OperationCanceledException||error is HttpRequestException||error is System.Net.WebException);}
 internal static string Failure(Exception error){
  var response=error as ResponseError;if(response!=null)return response.Status==429?"天气服务请求受限，稍后自动重试。":"天气服务返回 HTTP "+response.Status+"。";
  if(error is OperationCanceledException)return "天气服务连接超时。";
  for(Exception cause=error;cause!=null;cause=cause.InnerException){
   var web=cause as System.Net.WebException;if(web!=null){if(web.Status==System.Net.WebExceptionStatus.NameResolutionFailure||web.Status==System.Net.WebExceptionStatus.ProxyNameResolutionFailure)return "无法解析天气服务地址，请检查电脑 DNS。";if(web.Status==System.Net.WebExceptionStatus.Timeout)return "天气服务连接超时。";if(web.Status==System.Net.WebExceptionStatus.TrustFailure||web.Status==System.Net.WebExceptionStatus.SecureChannelFailure)return "天气服务安全连接失败，请检查电脑时间与网络。";}
   var socket=cause as System.Net.Sockets.SocketException;if(socket!=null&&(socket.SocketErrorCode==System.Net.Sockets.SocketError.HostNotFound||socket.SocketErrorCode==System.Net.Sockets.SocketError.TryAgain||socket.SocketErrorCode==System.Net.Sockets.SocketError.NoData))return "无法解析天气服务地址，请检查电脑 DNS。";
  }
  return error is HttpRequestException||error is System.Net.WebException?"无法直连天气服务，请检查电脑网络。":"天气服务数据无效或已过期。";
 }
 static bool Coordinate(double value,double max){return !Double.IsNaN(value)&&!Double.IsInfinity(value)&&Math.Abs(value)<=max;}
 static bool Name(string value){if(String.IsNullOrWhiteSpace(value)||value.Length>200)return false;foreach(char c in value)if(Char.IsControl(c)||c=='['||c==']'||c=='#'||c=='"'||c=='\\')return false;return true;}
 public static string SearchUrl(string query){query=(query??"").Trim();if(query.Length<2||query.Length>80)throw new ArgumentException("请输入 2–80 个字的城市名称。");foreach(char c in query)if(Char.IsControl(c))throw new ArgumentException("城市名称不能包含换行或控制字符。");return "https://geocoding-api.open-meteo.com/v1/search?name="+Uri.EscapeDataString(query)+"&count=10&language=zh&format=json";}
 public static List<WeatherPlace> ParseResults(string text){
  if(text==null||Encoding.UTF8.GetByteCount(text)>Limit)throw new IOException("城市搜索返回的数据过大，请稍后重试。");
  var root=new JavaScriptSerializer{MaxJsonLength=Limit}.DeserializeObject(text) as Dictionary<string,object>;if(root==null)throw new IOException("城市搜索返回的数据无效。");
  object raw;if(root.TryGetValue("error",out raw)&&Object.Equals(raw,true))throw new IOException("城市服务暂时无法完成搜索，请换个名称或稍后重试。");
  var result=new List<WeatherPlace>();if(!root.TryGetValue("results",out raw))return result;var rows=raw as IEnumerable;if(rows==null||raw is string)throw new IOException("城市搜索结果无效。");
  foreach(var item in rows){var row=item as Dictionary<string,object>;object lat,lon,n;if(row==null||!row.TryGetValue("latitude",out lat)||!row.TryGetValue("longitude",out lon)||!row.TryGetValue("name",out n)||!(n is string))continue;double a,b;if(lat is bool||lon is bool||!Double.TryParse(Convert.ToString(lat,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out a)||!Double.TryParse(Convert.ToString(lon,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out b)||!Coordinate(a,90)||!Coordinate(b,180))continue;
   var names=new List<string>{(string)n};foreach(string key in new[]{"admin4","admin3","admin2","admin1","country"}){object part;if(row.TryGetValue(key,out part)&&part is string&&!String.IsNullOrWhiteSpace((string)part)&&!names.Contains((string)part))names.Add((string)part);}object feature;if(row.TryGetValue("feature_code",out feature)&&Object.Equals(feature,"PPL"))names[0]+="（居民点）";string label=String.Join(" · ",names);if(!Name(label))continue;result.Add(new WeatherPlace{name=label,latitude=a,longitude=b});if(result.Count==10)break;
  }return result;
 }
 public static async Task<List<WeatherPlace>> Search(string query,CancellationToken cancel){try{string text=await GetString(client,SearchUrl(query),Limit,cancel).ConfigureAwait(false);cancel.ThrowIfCancellationRequested();return ParseResults(text);}catch(OperationCanceledException){throw;}catch(Exception ex){throw new IOException("城市搜索失败："+Failure(ex),ex);}}
 public static string ReadText(string root){string file=Path.Combine(root,"weather-location.txt");if(!File.Exists(file))return null;if(new FileInfo(file).Length>4096)throw new IOException("天气城市设置文件过大，原文件已保留。");return DesktopSettingsFiles.Read(file);}
 // Existing installations may only have coordinates in their collector skin.
 // Read them without changing the saved city or choosing a new default city.
 internal static WeatherPlace ReadCollectorLocation(string file){
  if(!File.Exists(file))return null;if(new FileInfo(file).Length>65536)throw new IOException("天气采集配置过大。");string section="",url="",name="已配置位置";
  foreach(string raw in File.ReadAllLines(file)){string line=raw.Trim();if(line.StartsWith("[",StringComparison.Ordinal)&&line.EndsWith("]",StringComparison.Ordinal)){section=line.Substring(1,line.Length-2);continue;}int equals=line.IndexOf('=');if(equals<1)continue;string key=line.Substring(0,equals).Trim(),value=line.Substring(equals+1).Trim();if(section=="Feed"&&key=="URL")url=value;if(section=="City"&&key=="Text"&&value!="示例位置"&&Name(value))name=value;}
  Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="api.open-meteo.com")return null;double a=Double.NaN,b=Double.NaN;
  foreach(string pair in uri.Query.TrimStart('?').Split('&')){var parts=pair.Split('=');if(parts.Length!=2)continue;double coordinate;if(!Double.TryParse(Uri.UnescapeDataString(parts[1]),NumberStyles.Float,CultureInfo.InvariantCulture,out coordinate))continue;if(parts[0]=="latitude")a=coordinate;if(parts[0]=="longitude")b=coordinate;}
  return Coordinate(a,90)&&Coordinate(b,180)?new WeatherPlace{name=name,latitude=a,longitude=b}:null;
 }
 public static WeatherPlace ParseSaved(string text){if(text==null)return null;var lines=text.Replace("\r","").TrimEnd('\n').Split('\n');double a,b;if(lines.Length!=3||!Double.TryParse(lines[0],NumberStyles.Float,CultureInfo.InvariantCulture,out a)||!Double.TryParse(lines[1],NumberStyles.Float,CultureInfo.InvariantCulture,out b)||!Coordinate(a,90)||!Coordinate(b,180)||!Name(lines[2]))throw new IOException("天气城市设置无效，原文件已保留。");return new WeatherPlace{latitude=a,longitude=b,name=lines[2]};}
 public static void Save(string root,WeatherPlace place,string expected){if(place==null||!Coordinate(place.latitude,90)||!Coordinate(place.longitude,180)||!Name(place.name))throw new ArgumentException("所选城市无效，请重新搜索。");DesktopSettingsFiles.Locked(()=>{string before=ReadText(root);ParseSaved(before);if(before!=expected)throw new IOException("城市已在另一窗口更新，请重新打开天气设置。");DesktopSettingsFiles.Write(Path.Combine(root,"weather-location.txt"),place.latitude.ToString("R",CultureInfo.InvariantCulture)+"\n"+place.longitude.ToString("R",CultureInfo.InvariantCulture)+"\n"+place.name+"\n");});}
}

// WinHTTP supplies HTTP framing and normal Windows TLS validation on .NET 4.8.
// RESOLUTION_HOSTNAME (Windows 10 21H1+) overrides DNS only, retaining the URL's
// Host/SNI/certificate name. No hosts file, proxy, DNS cache or routes are changed.
internal static class WeatherDnsRecovery {
 internal sealed class Answer {internal string[] Addresses;internal DateTime Expires;}
 const int RecoveryBudgetMs=10000,PrimaryDeadlineMs=6000,ResolverBudgetMs=2000;
 static readonly Dictionary<string,Answer> cache=new Dictionary<string,Answer>();
 static readonly string[][] resolvers={new[]{"dns.alidns.com","223.5.5.5","/resolve"},new[]{"cloudflare-dns.com","1.1.1.1","/dns-query"}};
 internal static bool Allowed(Uri uri){return uri!=null&&uri.Scheme=="https"&&uri.Port==443&&uri.UserInfo.Length==0&&uri.Fragment.Length==0&&((uri.Host=="api.open-meteo.com"&&uri.AbsolutePath=="/v1/forecast")||(uri.Host=="geocoding-api.open-meteo.com"&&uri.AbsolutePath=="/v1/search"));}
 internal static bool PublicAddress(string text){IPAddress ip;if(!IPAddress.TryParse(text,out ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)return false;byte[] b=ip.GetAddressBytes();return b[0]!=0&&b[0]!=10&&b[0]!=127&&b[0]<224&&!(b[0]==100&&b[1]>=64&&b[1]<=127)&&!(b[0]==169&&b[1]==254)&&!(b[0]==172&&b[1]>=16&&b[1]<=31)&&!(b[0]==192&&(b[1]==168||b[1]==0||b[1]==2))&&!(b[0]==198&&(b[1]==18||b[1]==19||b[1]==51))&&!(b[0]==203&&b[1]==0&&b[2]==113);}
 internal static Answer Parse(string body,string host,DateTime now){
  var data=new JavaScriptSerializer{MaxJsonLength=16384}.DeserializeObject(body) as Dictionary<string,object>;object raw;if(data==null||!data.TryGetValue("Status",out raw)||!Object.Equals(raw,0))throw new HttpRequestException("天气备用解析没有有效结果。");
  Dictionary<string,object> question=null;if(data.TryGetValue("Question",out raw)){question=raw as Dictionary<string,object>;if(question==null&&raw is IList&&((IList)raw).Count==1)question=((IList)raw)[0] as Dictionary<string,object>;}
  object name,kind;if(question==null||!question.TryGetValue("name",out name)||!(name is string)||!String.Equals(((string)name).TrimEnd('.'),host,StringComparison.OrdinalIgnoreCase)||!question.TryGetValue("type",out kind)||!Object.Equals(kind,1)||!data.TryGetValue("Answer",out raw)||!(raw is IEnumerable))throw new HttpRequestException("天气备用解析返回了不匹配的域名。");
  var addresses=new List<string>();int ttl=300;
  foreach(var item in (IEnumerable)raw){var row=item as Dictionary<string,object>;object type,address,time;if(row==null||!row.TryGetValue("type",out type)||!Object.Equals(type,1)||!row.TryGetValue("data",out address)||!(address is string)||!PublicAddress((string)address)||!row.TryGetValue("TTL",out time)||!(time is int)||(int)time<0)continue;string ip=IPAddress.Parse((string)address).ToString();if(!addresses.Contains(ip)&&addresses.Count<2)addresses.Add(ip);ttl=Math.Min(ttl,(int)time);}
  if(addresses.Count==0)throw new HttpRequestException("天气备用解析没有有效公网地址。");return new Answer{Addresses=addresses.ToArray(),Expires=now.AddSeconds(ttl)};
 }
 internal static Task<string> Get(string url,int limit,CancellationToken cancel){
  var uri=new Uri(url);if(!Allowed(uri))throw new ArgumentException("不是已配置的天气服务。");
  return Task.Run(()=>Recover(uri,limit,cancel,Read,cache),cancel);
 }
 internal static string Recover(Uri uri,int limit,CancellationToken cancel,Func<Uri,string,int,CancellationToken,Stopwatch,int,string> read,Dictionary<string,Answer> answers){
  var watch=Stopwatch.StartNew();var attempted=new HashSet<string>();HttpRequestException routeError=null;
  for(int i=0;i<resolvers.Length;i++){
   cancel.ThrowIfCancellationRequested();
   // Reserve time for a second DNS answer and its route after the first paths fail.
   int deadline=i==0?PrimaryDeadlineMs:RecoveryBudgetMs;if(watch.ElapsedMilliseconds>=deadline)continue;
   var server=resolvers[i];string cacheKey=uri.Host+"\n"+server[0];
   Answer answer;lock(answers){answers.TryGetValue(cacheKey,out answer);}
   if(answer==null||answer.Expires<=DateTime.UtcNow){
    try{
     var resolver=new Uri("https://"+server[0]+server[2]+"?name="+Uri.EscapeDataString(uri.Host)+"&type=A");
     int resolveDeadline=Math.Min(deadline,(int)watch.ElapsedMilliseconds+ResolverBudgetMs);
     answer=Parse(read(resolver,server[1],16384,cancel,watch,resolveDeadline),uri.Host,DateTime.UtcNow);
     lock(answers)answers[cacheKey]=answer;
    }catch(HttpRequestException){continue;}catch(ArgumentException){continue;}catch(WeatherLocation.ResponseError){break;}
   }
   foreach(string address in answer.Addresses){
    cancel.ThrowIfCancellationRequested();if(watch.ElapsedMilliseconds>=deadline)break;if(!attempted.Add(address))continue;
    try{
     string body=read(uri,address,limit,cancel,watch,deadline);cancel.ThrowIfCancellationRequested();return body;
    }catch(HttpRequestException ex){routeError=ex;}
   }
   lock(answers){Answer cached;if(answers.TryGetValue(cacheKey,out cached)&&Object.ReferenceEquals(cached,answer))answers.Remove(cacheKey);}
  }
  cancel.ThrowIfCancellationRequested();
  throw routeError??new HttpRequestException("无法通过备用 DNS 解析天气服务。",new WebException("resolver unavailable",WebExceptionStatus.NameResolutionFailure));
 }
 static void Check(bool success){if(success)return;int code=Marshal.GetLastWin32Error();WebExceptionStatus status=code==12002?WebExceptionStatus.Timeout:code==12007?WebExceptionStatus.NameResolutionFailure:code==12175||code==12037||code==12038||code==12045?WebExceptionStatus.SecureChannelFailure:WebExceptionStatus.ConnectFailure;throw new HttpRequestException("天气直连失败。",new WebException("WinHTTP "+code,status));}
 static void Budget(IntPtr request,Stopwatch watch,CancellationToken cancel,int budget){cancel.ThrowIfCancellationRequested();int left=budget-(int)watch.ElapsedMilliseconds;if(left<=0)throw new HttpRequestException("天气直连超时。",new WebException("deadline",WebExceptionStatus.Timeout));int phase=Math.Min(2000,left);Check(WinHttpSetTimeouts(request,phase,phase,phase,phase));}
 static string Read(Uri uri,string address,int limit,CancellationToken cancel,Stopwatch watch,int budget){
  bool resolver=uri.Scheme=="https"&&uri.Port==443&&uri.UserInfo.Length==0&&uri.Fragment.Length==0&&((uri.Host=="dns.alidns.com"&&uri.AbsolutePath=="/resolve"&&address=="223.5.5.5")||(uri.Host=="cloudflare-dns.com"&&uri.AbsolutePath=="/dns-query"&&address=="1.1.1.1"));
  if((!Allowed(uri)&&!resolver)||!PublicAddress(address))throw new ArgumentException("天气直连地址无效。");IntPtr session=IntPtr.Zero,connection=IntPtr.Zero,request=IntPtr.Zero;
  try{
   session=WinHttpOpen("PrismGlass/1.1 weather",1,null,null,0);Check(session!=IntPtr.Zero);uint tls12=0x800;Check(WinHttpSetOptionNumber(session,84,ref tls12,4));connection=WinHttpConnect(session,uri.Host,443,0);Check(connection!=IntPtr.Zero);request=WinHttpOpenRequest(connection,"GET",uri.PathAndQuery,null,null,IntPtr.Zero,0x00800000);Check(request!=IntPtr.Zero);
   uint disabled=7;Check(WinHttpSetOptionNumber(request,63,ref disabled,4));uint retries=0;Check(WinHttpSetOptionNumber(request,4,ref retries,4));
   Check(WinHttpSetOption(request,165,address,(uint)((address.Length+1)*2)));Budget(request,watch,cancel,budget);Check(WinHttpSendRequest(request,resolver?"Accept: application/dns-json\r\n":"Accept: application/json\r\n",0xffffffff,IntPtr.Zero,0,0,IntPtr.Zero));Budget(request,watch,cancel,budget);Check(WinHttpReceiveResponse(request,IntPtr.Zero));
   uint status,size=4,index=0;Check(WinHttpQueryHeaders(request,19|0x20000000,null,out status,ref size,ref index));if(status<200||status>=300)throw new WeatherLocation.ResponseError((int)status);
   using(var content=new MemoryStream()){var buffer=new byte[8192];uint read;do{Budget(request,watch,cancel,budget);Check(WinHttpReadData(request,buffer,(uint)buffer.Length,out read));if(content.Length+read>limit)throw new IOException("天气响应过大。");content.Write(buffer,0,(int)read);}while(read!=0);cancel.ThrowIfCancellationRequested();return new UTF8Encoding(false,true).GetString(content.ToArray());}
  }finally{if(request!=IntPtr.Zero)WinHttpCloseHandle(request);if(connection!=IntPtr.Zero)WinHttpCloseHandle(connection);if(session!=IntPtr.Zero)WinHttpCloseHandle(session);}
 }
 [DllImport("winhttp.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr WinHttpOpen(string agent,uint access,string proxy,string bypass,uint flags);
 [DllImport("winhttp.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr WinHttpConnect(IntPtr session,string host,ushort port,uint reserved);
 [DllImport("winhttp.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr WinHttpOpenRequest(IntPtr connection,string verb,string path,string version,string referer,IntPtr accept,uint flags);
 [DllImport("winhttp.dll",SetLastError=true)] static extern bool WinHttpSetTimeouts(IntPtr handle,int resolve,int connect,int send,int receive);
 [DllImport("winhttp.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool WinHttpSetOption(IntPtr handle,uint option,string value,uint length);
 [DllImport("winhttp.dll",EntryPoint="WinHttpSetOption",SetLastError=true)] static extern bool WinHttpSetOptionNumber(IntPtr handle,uint option,ref uint value,uint length);
 [DllImport("winhttp.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool WinHttpSendRequest(IntPtr request,string headers,uint length,IntPtr data,uint count,uint total,IntPtr context);
 [DllImport("winhttp.dll",SetLastError=true)] static extern bool WinHttpReceiveResponse(IntPtr request,IntPtr reserved);
 [DllImport("winhttp.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool WinHttpQueryHeaders(IntPtr request,uint info,string name,out uint value,ref uint length,ref uint index);
 [DllImport("winhttp.dll",SetLastError=true)] static extern bool WinHttpReadData(IntPtr request,byte[] bytes,uint length,out uint read);
 [DllImport("winhttp.dll")] static extern bool WinHttpCloseHandle(IntPtr handle);
}
