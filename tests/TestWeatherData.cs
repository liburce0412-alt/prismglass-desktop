using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
internal static class TestWeatherData {
 static readonly JavaScriptSerializer json=new JavaScriptSerializer();static int checks;static DateTime now=new DateTime(2026,10,29,10,5,0,DateTimeKind.Utc);static WeatherPlace place=new WeatherPlace{name="南昌 · 南昌市 · 江西 · 中国",latitude=28.68396,longitude=115.85306};
 static long Epoch(DateTime value){return (long)(value-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;}
 static void Check(bool result,string name){if(!result)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static string Body(DateTime at){return json.Serialize(new{timezone="Asia/Shanghai",utc_offset_seconds=28800,current_units=new{time="unixtime",temperature_2m="°C",apparent_temperature="°C",weather_code="wmo code"},current=new{time=Epoch(at),temperature_2m=22.5,apparent_temperature=23.8,weather_code=2},daily_units=new{time="unixtime",temperature_2m_max="°C",temperature_2m_min="°C",sunrise="unixtime",sunset="unixtime"},daily=new{time=new[]{Epoch(at.Date.AddHours(-8))},temperature_2m_max=new[]{24.0},temperature_2m_min=new[]{17.0},sunrise=new[]{Epoch(at.Date.AddHours(-2))},sunset=new[]{Epoch(at.Date.AddHours(10))}}});}
 static Dictionary<string,object> Parse(string body){return WeatherData.Parse(body,place,now,true);}
 static void Reject(string body,string name){bool rejected=false;try{Parse(body);}catch{rejected=true;}Check(rejected,name);}
 static Dictionary<string,object> Child(Dictionary<string,object> value,string key){return (Dictionary<string,object>)value[key];}
 static string Change(string body,string section,string field,object value){var map=json.Deserialize<Dictionary<string,object>>(body);Child(map,section)[field]=value;return json.Serialize(map);}
 static void Save(string root,WeatherPlace city){File.WriteAllText(Path.Combine(root,"weather-location.txt"),city.latitude.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\n"+city.longitude.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"\n"+city.name+"\n");}
 static string DnsBody(params string[] addresses){return json.Serialize(new{Status=0,Question=new{name="api.open-meteo.com.",type=1},Answer=Array.ConvertAll(addresses,data=>new{name="api.open-meteo.com.",type=1,TTL=3600,data=data})});}
 static void CheckRecovery(string raw){
  var uri=new Uri(WeatherData.Url(place,true));var calls=new List<string>();var routeError=new HttpRequestException("fixture timeout",new System.Net.WebException("fixture timeout",System.Net.WebExceptionStatus.Timeout));
  var body=WeatherDnsRecovery.Recover(uri,65536,CancellationToken.None,(target,address,limit,cancel,watch,deadline)=>{
   calls.Add(address);
   if(target.Host=="dns.alidns.com")return DnsBody("188.40.99.226","167.114.211.86");
   if(target.Host=="cloudflare-dns.com")return DnsBody("0xbc.40.99.226","157.90.83.168");
   if(address=="157.90.83.168")return raw;
   throw routeError;
  },new Dictionary<string,WeatherDnsRecovery.Answer>());
  Check(body==raw&&String.Join(",",calls)=="223.5.5.5,188.40.99.226,167.114.211.86,1.1.1.1,157.90.83.168","secondary DNS supplies a new route after both first routes fail and equivalent addresses are skipped");
  calls.Clear();bool preserved=false;
  try{WeatherDnsRecovery.Recover(uri,65536,CancellationToken.None,(target,address,limit,cancel,watch,deadline)=>{
   calls.Add(address);if(target!=uri)return DnsBody("188.40.99.226");throw routeError;
  },new Dictionary<string,WeatherDnsRecovery.Answer>());}catch(HttpRequestException ex){preserved=Object.ReferenceEquals(ex,routeError);}
  Check(preserved&&String.Join(",",calls)=="223.5.5.5,188.40.99.226,1.1.1.1","a duplicate-only secondary result preserves the real provider timeout");
  calls.Clear();bool throttled=false;
  try{WeatherDnsRecovery.Recover(uri,65536,CancellationToken.None,(target,address,limit,cancel,watch,deadline)=>{
   calls.Add(address);if(target!=uri)return DnsBody("188.40.99.226","167.114.211.86");throw new WeatherLocation.ResponseError(429);
  },new Dictionary<string,WeatherDnsRecovery.Answer>());}catch(WeatherLocation.ResponseError ex){throttled=ex.Status==429;}
  Check(throttled&&String.Join(",",calls)=="223.5.5.5,188.40.99.226","a weather HTTP 429 stops recovery before any other address or resolver");
  calls.Clear();bool cancelled=false;
  using(var source=new CancellationTokenSource()){
   try{WeatherDnsRecovery.Recover(uri,65536,source.Token,(target,address,limit,cancel,watch,deadline)=>{
    calls.Add(address);if(target!=uri)return DnsBody("188.40.99.226","167.114.211.86");source.Cancel();throw routeError;
   },new Dictionary<string,WeatherDnsRecovery.Answer>());}catch(OperationCanceledException){cancelled=true;}
  }
  Check(cancelled&&String.Join(",",calls)=="223.5.5.5,188.40.99.226","cancelling an in-flight route starts no additional address or DNS request");
 }
 static int Main(string[] args){try{
  string root=Path.Combine(args[0],"data");Directory.CreateDirectory(root);string raw=Body(now.AddMinutes(-5));var parsed=Parse(raw);
  Check((double)parsed["temp"]==22.5&&(double)parsed["feelsLike"]==23.8&&(long)parsed["updatedAt"]==Epoch(now.AddMinutes(-5))*1000&&(long)parsed["fetchedAt"]==Epoch(now)*1000,"current temperature and source time are separate from the fetch clock");
  Check((string)parsed["kind"]=="model"&&((string)parsed["source"]).Contains("CMA GRAPES")&&(string)parsed["timezone"]=="Asia/Shanghai"&&Convert.ToInt32(parsed["utcOffsetSeconds"])==28800,"CMA via Open-Meteo is explicitly a forecast model with a full timezone");
  Check((string)parsed["sunrise"]=="2026-10-29T06:00"&&(string)parsed["sunset"]=="2026-10-29T18:00"&&(double)parsed["high"]==24,"daily values use the same source-local date including the UTC offset");
  Reject(Body(now.AddHours(-3)),"yesterday or old current data cannot masquerade as newly fetched weather");Reject(Body(now.AddMinutes(5)),"future source timestamps are rejected");Reject(Change(raw,"current","temperature_2m",null),"null current temperature is not zero");Reject(Change(raw,"current","temperature_2m","22.5"),"numeric strings are not accepted as measured numeric fields");Reject(Change(raw,"current_units","temperature_2m","°F"),"temperature units are validated before display");Reject("{\"error\":true,\"reason\":\"denied\"}","provider denial never becomes clear weather");Reject("<html>Access denied</html>","HTML errors never become weather");
  parsed=Parse(Change(raw,"current","apparent_temperature",null));Check(parsed["feelsLike"]==null,"missing feels-like temperature is not synthesized from temperature");
  parsed=Parse(Change(raw,"current","weather_code",null));Check(parsed["code"]==null,"missing condition is unknown rather than clear");
  parsed=Parse(Change(raw,"daily","time",new[]{Epoch(now.AddDays(-1).Date.AddHours(-8))}));Check(parsed["high"]==null&&parsed["low"]==null&&(string)parsed["sunrise"]=="","previous-day daily range is not attached to today's current weather");
  string ini=Path.Combine(root,"Main.ini");File.WriteAllText(ini,"[Feed]\nURL=https://api.open-meteo.com/v1/forecast?latitude=28.68&longitude=115.85\n[City]\nText=南昌\n");var oldPlace=WeatherLocation.ReadCollectorLocation(ini);Check(oldPlace.latitude==28.68&&oldPlace.longitude==115.85&&oldPlace.name=="南昌","installations without a saved city retain their collector coordinates");
  Save(root,place);int requests=0;var reply=new TaskCompletionSource<string>();var service=new WeatherData(root,()=>ini,url=>{requests++;return reply.Task;},()=>now);
  var initial=service.Read();Task pending=service.Pending;for(int i=0;i<20;i++)service.Read();reply.SetResult(raw);pending.GetAwaiter().GetResult();var current=service.Read();Check(requests==1&&(bool)current["ready"]&&!(bool)current["stale"]&&File.Exists(Path.Combine(root,"weather-cache.json")),"concurrent reads share one request and persist one successful city cache");
  string cache=File.ReadAllText(Path.Combine(root,"weather-cache.json"));Check(File.ReadAllText(Path.Combine(root,"weather-location.txt")).Contains(place.name),"fetching never rewrites the selected city");
  now=now.AddMinutes(16);int fallbackRequests=0;var fallback=new WeatherData(root,()=>ini,url=>{fallbackRequests++;return url.Contains("models=")?Task.FromException<string>(new IOException("fixture denial")):Task.FromResult(Body(now));},()=>now);fallback.Read();fallback.Pending.GetAwaiter().GetResult();current=fallback.Read();Check(fallbackRequests==2&&(bool)current["ready"]&&(string)current["source"]=="Open-Meteo"&&((string)current["reason"]).Contains("综合预报"),"a failed CMA request falls back to the original provider and reports its real source");
  long lastStamp=(long)current["updatedAt"];var offline=new WeatherData(root,()=>ini,url=>Task.FromException<string>(new IOException("fixture offline")),()=>now);offline.Read();offline.Pending.GetAwaiter().GetResult();current=offline.Read();Check((bool)current["ready"]&&(bool)current["stale"]&&(long)current["updatedAt"]==lastStamp&&((string)current["reason"]).Contains("更新失败"),"failed refresh retains the last good sample and marks it stale without changing its timestamp");
  var savedCache=File.ReadAllText(Path.Combine(root,"weather-cache.json"));int directAttempts=0;
  var dnsError=new System.Net.Http.HttpRequestException("private request details must not reach the UI",new System.Net.WebException("fixture DNS",System.Net.WebExceptionStatus.NameResolutionFailure));
  var dns=new WeatherData(root,()=>ini,url=>{directAttempts++;return directAttempts<3?Task.FromException<string>(dnsError):Task.FromResult(Body(now));},()=>now);dns.Read();dns.Pending.GetAwaiter().GetResult();current=dns.Read();
  Check(((string)current["reason"]).Contains("上次更新")&&((string)current["reason"]).Contains("自动重试"),"offline cache explains its age and scheduled recovery");
  Check(directAttempts==1&&((string)current["reason"]).Contains("DNS")&&!((string)current["reason"]).Contains("private"),"same-host DNS failure returns one specific safe reason without a redundant model request");
  Check((bool)current["stale"]&&(long)current["updatedAt"]==lastStamp&&File.ReadAllText(Path.Combine(root,"weather-cache.json"))==savedCache,"transport failure preserves the successful cache bytes and original source time");
  for(int i=0;i<20;i++)dns.Read();Check(directAttempts==1,"repeated UI reads respect the failed-request retry backoff");
  now=now.AddSeconds(61);dns.Read();dns.Pending.GetAwaiter().GetResult();current=dns.Read();Check(directAttempts==2&&(bool)current["stale"]&&(long)current["updatedAt"]==lastStamp&&File.ReadAllText(Path.Combine(root,"weather-cache.json"))==savedCache,"two consecutive failed DNS refreshes retain exactly the last valid source time and cache");
  now=now.AddSeconds(61);dns.Read();dns.Pending.GetAwaiter().GetResult();current=dns.Read();Check(directAttempts==3&&(bool)current["ready"]&&!(bool)current["stale"]&&(long)current["updatedAt"]>lastStamp,"a later successful refresh exits failure backoff and replaces stale data with the real response time");
  Check(!WeatherLocation.RetryModel(new TaskCanceledException())&&!WeatherLocation.RetryModel(new WeatherLocation.ResponseError(429))&&!WeatherLocation.RetryModel(new WeatherLocation.ResponseError(503)),"timeouts, throttling and host outage do not immediately retry the same host");
  Check(WeatherLocation.RetryModel(new WeatherLocation.ResponseError(400))&&WeatherLocation.RetryModel(new IOException("invalid model data")),"unsupported or invalid CMA data still permits a general forecast fallback");
  Check(WeatherLocation.Failure(new TaskCanceledException()).Contains("超时")&&WeatherLocation.Failure(new WeatherLocation.ResponseError(429)).Contains("受限")&&WeatherLocation.Failure(new WeatherLocation.ResponseError(503)).Contains("503"),"timeout, rate limit and HTTP service failures remain distinguishable");
  Check(!WeatherDnsRecovery.PublicAddress("198.18.0.121")&&!WeatherDnsRecovery.PublicAddress("198.19.255.255")&&!WeatherDnsRecovery.PublicAddress("127.0.0.1")&&!WeatherDnsRecovery.PublicAddress("192.168.1.1")&&WeatherDnsRecovery.PublicAddress("188.40.99.226"),"DNS recovery rejects tunnel fake IPs, loopback and private addresses");
  var dnsBody=DnsBody("188.40.99.226");
  var answer=WeatherDnsRecovery.Parse(dnsBody,"api.open-meteo.com",now);Check(answer.Addresses.Length==1&&answer.Expires==now.AddSeconds(300),"DNS recovery caches public answers for no longer than their TTL or five minutes");
  answer=WeatherDnsRecovery.Parse(dnsBody.Replace("3600","0"),"api.open-meteo.com",now);Check(answer.Expires<=now,"a zero-TTL DNS answer is immediately expired instead of reused");
  bool wrongDns=false;try{WeatherDnsRecovery.Parse(dnsBody,"geocoding-api.open-meteo.com",now);}catch(System.Net.Http.HttpRequestException){wrongDns=true;}Check(wrongDns,"DNS answers for a different public API hostname are rejected");
  Check(WeatherDnsRecovery.Allowed(new Uri(WeatherData.Url(place,true)))&&!WeatherDnsRecovery.Allowed(new Uri("http://api.open-meteo.com/v1/forecast"))&&!WeatherDnsRecovery.Allowed(new Uri("https://example.com/v1/forecast")),"DNS recovery is restricted to the existing two HTTPS weather API paths");
  bool cancelled=false;var cancelledToken=new System.Threading.CancellationToken(true);try{WeatherLocation.WithDnsRecovery(WeatherData.Url(place,true),65536,cancelledToken,()=>Task.FromException<string>(dnsError)).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"a cancelled city search never starts backup DNS requests");
  bool rateLimited=false;try{WeatherLocation.WithDnsRecovery(WeatherData.Url(place,true),65536,System.Threading.CancellationToken.None,()=>Task.FromException<string>(new WeatherLocation.ResponseError(429))).GetAwaiter().GetResult();}catch(WeatherLocation.ResponseError){rateLimited=true;}Check(rateLimited,"HTTP rate limiting does not invoke alternate DNS or evade provider throttling");
  CheckRecovery(raw);
  now=now.AddHours(3);current=offline.Read();offline.Pending.GetAwaiter().GetResult();Check(!(bool)current["ready"]&&(bool)current["stale"],"a retained sample older than two hours cannot be advertised as current");
  now=now.AddHours(-3);var raceReply=new TaskCompletionSource<string>();var race=new WeatherData(root,()=>ini,url=>raceReply.Task,()=>now);race.Read();var oldPending=race.Pending;var other=new WeatherPlace{name="杭州",latitude=30.27,longitude=120.15};Save(root,other);current=race.Read();Check(!(bool)current["ready"]&&current["temp"]==null&&(string)current["location"]=="杭州","changing cities clears the old city's temperature before the new response");raceReply.SetResult(Body(now));oldPending.GetAwaiter().GetResult();Check(File.ReadAllText(Path.Combine(root,"weather-cache.json")).Contains(place.name),"an old city's in-flight response cannot overwrite the new city's cache");
  File.WriteAllText(Path.Combine(root,"weather-location.txt"),"broken");current=race.Read();Check(!(bool)current["ready"]&&File.ReadAllText(Path.Combine(root,"weather-location.txt"))=="broken","invalid city files remain intact without silently switching locations");
  Check(Object.ReferenceEquals(WeatherData.ForRoot(root,()=>ini),WeatherData.ForRoot(root+Path.DirectorySeparatorChar,()=>ini)),"equivalent roots with or without a trailing slash share one weather collector");
  string blocked=Path.Combine(args[0],"blocked");Directory.CreateDirectory(blocked);var denied=new WeatherData(blocked,()=>{throw new UnauthorizedAccessException("fixture denied");},url=>{throw new Exception("must not fetch");},()=>now);current=denied.Read();Check(!(bool)current["ready"]&&((string)current["reason"]).Contains("原文件已保留"),"access-denied collector settings only disable weather and never escape to widget readers");
  if(args.Length>1&&args[1]=="--live"){
   string liveRoot=Path.Combine(args[0],"live");Directory.CreateDirectory(liveRoot);File.Copy(args[2],Path.Combine(liveRoot,"weather-location.txt"));string selected=File.ReadAllText(args[2]);var live=new WeatherData(liveRoot,()=>"",WeatherData.Fetch,()=>DateTime.UtcNow);live.Read();live.Pending.GetAwaiter().GetResult();var result=live.Read();Check((bool)result["ready"]&&result["temp"]!=null&&(long)result["updatedAt"]>Epoch(DateTime.UtcNow.AddHours(-2))*1000,"live selected-city weather is received by the production JSON collector");Check(File.ReadAllText(args[2])==selected,"live verification leaves installed city configuration unchanged");File.WriteAllText(args[3],json.Serialize(new{verifiedAt=DateTime.UtcNow.ToString("o"),weather=result,sourceStatus=new{weather=new{available=result["ready"],reason=result["reason"],updatedAt=result["updatedAt"],fetchedAt=result["fetchedAt"],stale=result["stale"]}}}));Console.WriteLine(json.Serialize(result));
  }
  if(args.Length>1&&args[1]=="--direct-proxy"){
   var oldProxy=System.Net.WebRequest.DefaultWebProxy;System.Net.WebRequest.DefaultWebProxy=new System.Net.WebProxy("http://127.0.0.1:1",false);
   try{
    bool blockedProxy=false;using(var inherited=new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler{Proxy=System.Net.WebRequest.DefaultWebProxy,UseProxy=true})){inherited.Timeout=TimeSpan.FromSeconds(2);try{inherited.GetStringAsync(WeatherData.Url(new WeatherPlace{latitude=28.68,longitude=115.85},true)).GetAwaiter().GetResult();}catch(System.Net.Http.HttpRequestException){blockedProxy=true;}catch(TaskCanceledException){blockedProxy=true;}}
    Check(blockedProxy,"an unavailable local proxy reproduces the previous transport dependency in this test process only");
    var city=new WeatherPlace{name="南昌（城市级直连样例）",latitude=28.68,longitude=115.85};
    using(var direct=WeatherLocation.CreateClient(65536)){var body=direct.GetStringAsync(WeatherData.Url(city,true)).GetAwaiter().GetResult();var snapshot=WeatherData.Parse(body,city,DateTime.UtcNow,true);Check((bool)snapshot["ready"]&&snapshot["temp"]!=null,"the production direct transport receives real CMA weather while the process default proxy is unavailable");}
    var places=WeatherLocation.Search("Nanchang",System.Threading.CancellationToken.None).GetAwaiter().GetResult();Check(places.Count>0,"city search uses the same direct transport despite the unavailable process proxy");
   }finally{System.Net.WebRequest.DefaultWebProxy=oldProxy;}
  }
  if(args.Length>1&&args[1]=="--dns-recovery"){
   var city=new WeatherPlace{name="南昌（城市级 DNS 恢复样例）",latitude=28.68,longitude=115.85};int attempts=0;var watch=System.Diagnostics.Stopwatch.StartNew();
   var body=WeatherLocation.WithDnsRecovery(WeatherData.Url(city,true),65536,System.Threading.CancellationToken.None,()=>{attempts++;return Task.FromException<string>(dnsError);}).GetAwaiter().GetResult();var snapshot=WeatherData.Parse(body,city,DateTime.UtcNow,true);
   Check(attempts==1&&(bool)snapshot["ready"]&&snapshot["temp"]!=null,"production DNS recovery receives real CMA weather after an injected failed system-DNS request");Console.WriteLine("DNS recovery real CMA elapsedMs="+watch.ElapsedMilliseconds+" sourceAt="+snapshot["updatedAt"]);
   body=WeatherLocation.WithDnsRecovery(WeatherLocation.SearchUrl("Nanchang"),262144,System.Threading.CancellationToken.None,()=>Task.FromException<string>(dnsError)).GetAwaiter().GetResult();Check(WeatherLocation.ParseResults(body).Count>0,"the same DNS recovery restores city search without sending the city name to the resolver");
  }
  Console.WriteLine("Completed "+checks+" weather data checks.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
