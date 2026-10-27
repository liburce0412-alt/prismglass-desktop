using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class CompanionGrant {
 public bool apps,tasks,notes,music,control,pointer,power;
 internal volatile bool Active;
}
internal sealed class CompanionReply {
 public int Status;public byte[] Body;public string ContentType="application/json; charset=utf-8";
 public readonly Dictionary<string,string> Headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 public static CompanionReply Json(int status,object value){return new CompanionReply{Status=status,Body=Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=1048576}.Serialize(value))};}
 public static CompanionReply Asset(byte[] data,string type){if(data==null||data.Length>262144||(type!="image/png"&&type!="image/jpeg"))throw new ArgumentException("Invalid asset");return new CompanionReply{Status=200,Body=data,ContentType=type};}
 public static CompanionReply Error(int status,string code,string message){return Json(status,new{error=code,message=message});}
}
internal sealed class CompanionRequest {
 public string Method,Path,Origin,ContentType,Body,Cookie,Csrf,RemoteAddress;
}
internal sealed class CompanionServer : IDisposable {
 const int MaxBody=131072,ControlBody=1024,MaxDevices=8,MaxCommandReply=16384,MaxCachedReplies=4194304,CoreConcurrency=8,AssetConcurrency=4,PointerConcurrency=2,PowerConcurrency=2;const string CookieName="PrismCompanion";
 const int PointerDevicePerSecond=90,PointerDevicePerMinute=5400,PointerIpPerSecond=180,PointerIpPerMinute=10800;
 internal const string ContentSecurityPolicy="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; media-src 'self' blob:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
 internal const string PermissionsPolicy="camera=(self), microphone=(), geolocation=()";
 readonly string webRoot,store;readonly Func<string,Dictionary<string,object>,CompanionGrant,CompanionReply> handler;readonly Func<bool> isLocked;
 readonly object gate=new object();readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=1048576,RecursionLimit=24};
 readonly List<Device> devices=new List<Device>();readonly Dictionary<string,Pending> pending=new Dictionary<string,Pending>();
 readonly Dictionary<string,Rate> rates=new Dictionary<string,Rate>(),assetRates=new Dictionary<string,Rate>();readonly SemaphoreSlim requests=new SemaphoreSlim(CoreConcurrency,CoreConcurrency),assetRequests=new SemaphoreSlim(AssetConcurrency,AssetConcurrency);
 readonly Dictionary<string,Rate> pointerRates=new Dictionary<string,Rate>(),powerRates=new Dictionary<string,Rate>(),cancelRates=new Dictionary<string,Rate>();readonly SemaphoreSlim pointerRequests=new SemaphoreSlim(PointerConcurrency,PointerConcurrency),powerRequests=new SemaphoreSlim(PowerConcurrency,PowerConcurrency),cancelRequests=new SemaphoreSlim(1,1);
 readonly Dictionary<string,Rejection> rejections=new Dictionary<string,Rejection>();
 HttpListener listener;string pairingCode="",loadError="";DateTime pairingExpires=DateTime.MinValue;int pairingAttempts,generation;bool disposed;
 public bool IsRunning {get {lock(gate)return listener!=null;}}
 public string Address {get;private set;}
 public sealed class Device {public string id,name,tokenHash,csrf;public DateTime expiresAt;public CompanionGrant grants;[ScriptIgnore]public Dictionary<string,Command> commands=new Dictionary<string,Command>();[ScriptIgnore]public int cachedBytes;}
 public sealed class Command {public string signature;public CompanionReply reply;}
 sealed class Pending {public string id,ticket,name,token,deviceId;public DateTime expiresAt;}
 sealed class Rate {public DateTime until;public int count;}
 sealed class Rejection {public string error,route,scope;public long count;public DateTime lastAt;}
 public CompanionServer(string root,Func<string,Dictionary<string,object>,CompanionGrant,CompanionReply> action,Func<bool> locked){
  webRoot=Path.Combine(root,"companion-web");store=Path.Combine(root,"runtime-data","companion","devices.bin");handler=action;isLocked=locked??(()=>true);Address="";Load();
 }
 static DateTime Now {get{return DateTime.UtcNow;}}
 static string Token(){var bytes=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
 static string Hash(string value){using(var hash=SHA256.Create())return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value??"")));}
 static bool Equal(string left,string right){if(left==null||right==null)return false;byte[] a=Encoding.UTF8.GetBytes(left),b=Encoding.UTF8.GetBytes(right);int mismatch=a.Length^b.Length;for(int i=0;i<Math.Min(a.Length,b.Length);i++)mismatch|=a[i]^b[i];return mismatch==0;}
 static bool Opaque(string value){return value!=null&&Regex.IsMatch(value,"\\A[a-zA-Z0-9_-]{16,96}\\z");}
 static string Text(Dictionary<string,object> body,string key){object value;return body.TryGetValue(key,out value)?value as string:null;}
 static CompanionGrant Copy(CompanionGrant value){return new CompanionGrant{apps=value.apps,tasks=value.tasks,notes=value.notes,music=value.music,control=value.control,pointer=value.pointer,power=value.power,Active=true};}
 public static string Endpoint(string address,int port){
  IPAddress ip;if(!IPAddress.TryParse(address,out ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||port<1024||port>65535)throw new ArgumentException("请选择本机的局域网 IPv4 地址和 1024–65535 端口。");
  var bytes=ip.GetAddressBytes();if(!(bytes[0]==10||(bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31)||(bytes[0]==192&&bytes[1]==168)))throw new ArgumentException("伴随屏仅支持专用局域网 IPv4 地址。");
  return "https://"+ip.ToString()+":"+port.ToString(CultureInfo.InvariantCulture);
 }
 public static string NormalizeHostName(string hostName){string value=hostName==null?"":hostName.ToLowerInvariant();if(!Regex.IsMatch(value,"\\Aprismglass-[0-9a-f]{8}\\.local\\z",RegexOptions.CultureInvariant))throw new ArgumentException("伴随屏名称必须为 prismglass-加八位十六进制标识.local。");return value;}
 public static string Endpoint(string address,int port,string hostName){Endpoint(address,port);return "https://"+NormalizeHostName(hostName)+":"+port.ToString(CultureInfo.InvariantCulture);}
 internal static bool AcceptEndpoint(bool secure,Uri url,string hostHeader,IPEndPoint local,string origin,string address,int port){
  return secure&&url!=null&&url.IsAbsoluteUri&&url.Query.Length==0&&url.UserInfo.Length==0&&url.Fragment.Length==0&&url.GetLeftPart(UriPartial.Authority)==origin&&String.Equals(hostHeader,new Uri(origin).Authority,StringComparison.OrdinalIgnoreCase)&&local!=null&&local.Port==port&&local.Address.Equals(IPAddress.Parse(address));
 }
 public void Start(string address,int port){StartCore(address,port,null);}
 public void Start(string address,int port,string hostName){StartCore(address,port,NormalizeHostName(hostName));}
 void StartCore(string address,int port,string hostName){
  string ipOrigin=Endpoint(address,port);address=new Uri(ipOrigin).Host;string origin=hostName==null?ipOrigin:Endpoint(address,port,hostName);bool local=false;foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())if(adapter.OperationalStatus==OperationalStatus.Up)foreach(var ip in adapter.GetIPProperties().UnicastAddresses)if(ip.Address.ToString()==address)local=true;
  if(!local)throw new ArgumentException("该地址不属于当前电脑，请重新选择网络地址。");
  lock(gate){if(disposed)throw new ObjectDisposedException("CompanionServer");if(listener!=null)throw new InvalidOperationException("伴随屏已开启。");if(loadError.Length>0)throw new InvalidOperationException(loadError);
   var next=new HttpListener();next.Prefixes.Add(origin+"/");try{next.TimeoutManager.EntityBody=TimeSpan.FromSeconds(8);next.TimeoutManager.HeaderWait=TimeSpan.FromSeconds(8);next.TimeoutManager.IdleConnection=TimeSpan.FromSeconds(15);next.Start();RenewSessionCsrf();}catch{next.Close();throw new InvalidOperationException("无法开启 HTTPS。请检查证书绑定、URL ACL、端口、所选网络地址和运行目录权限。");}
   Address=origin;listener=next;generation++;foreach(var device in devices){device.grants=Copy(device.grants);device.grants.Active=device.expiresAt>Now;}int current=generation;Task.Run(()=>Listen(next,current,origin,address,port));
  }
 }
 public void Stop(){HttpListener previous;lock(gate){previous=listener;listener=null;generation++;pairingCode="";pairingExpires=DateTime.MinValue;pending.Clear();foreach(var device in devices)device.grants.Active=false;}if(previous!=null){previous.Stop();previous.Close();}}
 async Task Listen(HttpListener active,int current,string origin,string address,int port){
  while(active.IsListening){HttpListenerContext context;try{context=await active.GetContextAsync().ConfigureAwait(false);}catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}
   SemaphoreSlim slots;var rejected=Enter(context.Request.RawUrl,out slots);if(rejected!=null){Send(context,rejected);continue;}
   ThreadPool.QueueUserWorkItem(async ignored=>{try{await Serve(context,current,origin,address,port).ConfigureAwait(false);}finally{slots.Release();}});
  }
 }
 async Task Serve(HttpListenerContext context,int current,string origin,string address,int port){
  CompanionReply result;try{
   var incoming=context.Request;int maximum=BodyLimit(incoming.RawUrl);if(!AcceptEndpoint(incoming.IsSecureConnection,incoming.Url,incoming.Headers["Host"],incoming.LocalEndPoint,origin,address,port))result=CompanionReply.Error(400,"bad_request","请求地址无效。");
   else if(incoming.ContentLength64>maximum)result=CompanionReply.Error(413,"body_too_large","请求内容过大。");
   else {
    byte[] bytes=new byte[maximum+1];int length=0;using(var deadline=new CancellationTokenSource(8000)){
     while(length<bytes.Length){var read=incoming.InputStream.ReadAsync(bytes,length,bytes.Length-length,deadline.Token);var finished=await Task.WhenAny(read,Task.Delay(8000,deadline.Token)).ConfigureAwait(false);if(finished!=read)throw new TimeoutException();int count=await read.ConfigureAwait(false);if(count==0)break;length+=count;}
    }
    if(length>maximum)result=CompanionReply.Error(413,"body_too_large","请求内容过大。");
    else {string body=new UTF8Encoding(false,true).GetString(bytes,0,length);var request=new CompanionRequest{Method=incoming.HttpMethod,Path=incoming.RawUrl,Origin=incoming.Headers["Origin"],ContentType=incoming.ContentType,Body=body,Cookie=incoming.Cookies[CookieName]==null?null:incoming.Cookies[CookieName].Value,Csrf=incoming.Headers["X-CSRF-Token"],RemoteAddress=incoming.RemoteEndPoint==null?"":incoming.RemoteEndPoint.Address.ToString()};
     lock(gate){if(current!=generation||listener==null){Send(context,CompanionReply.Error(503,"stopped","伴随屏已关闭。"));return;}}
     result=DispatchCore(request,origin,current);
    }
   }
  }catch(TimeoutException){result=CompanionReply.Error(408,"request_timeout","请求超时。请重新连接。 ");}catch(OperationCanceledException){result=CompanionReply.Error(408,"request_timeout","请求超时。请重新连接。");}catch{result=CompanionReply.Error(400,"bad_request","请求无效。");}
  Send(context,result);
 }
 static void Send(HttpListenerContext context,CompanionReply reply){
  try{using(var deadline=new Timer(ignored=>{try{context.Response.Abort();}catch{}},null,8000,Timeout.Infinite)){var response=context.Response;response.StatusCode=reply.Status;response.ContentType=reply.ContentType;response.Headers["Cache-Control"]="no-store";response.Headers["X-Content-Type-Options"]="nosniff";response.Headers["Referrer-Policy"]="no-referrer";response.Headers["Content-Security-Policy"]=ContentSecurityPolicy;response.Headers["X-Frame-Options"]="DENY";response.Headers["Permissions-Policy"]=PermissionsPolicy;foreach(var header in reply.Headers)response.Headers[header.Key]=header.Value;response.ContentLength64=reply.Body.Length;response.OutputStream.Write(reply.Body,0,reply.Body.Length);}}catch{}finally{try{context.Response.Close();}catch{}}
 }
 bool Locked(){bool locked=true;try{locked=isLocked();}catch{}if(locked){pairingCode="";pairingExpires=DateTime.MinValue;pending.Clear();}return locked;}
 static bool AssetPath(string path){return path!=null&&path.StartsWith("/api/assets/",StringComparison.Ordinal);}
 static bool PointerPath(string path){return path=="/api/pointer"||path=="/api/pointer/begin"||path=="/api/pointer/end";}
 static bool PowerPath(string path){return path=="/api/power/prepare"||path=="/api/power/commit"||path=="/api/power/cancel";}
 static int BodyLimit(string path){return PointerPath(path)||PowerPath(path)?ControlBody:MaxBody;}
 static string RouteCategory(string path){return AssetPath(path)?"asset":PointerPath(path)?"pointer":path=="/api/power/cancel"?"power_cancel":PowerPath(path)?"power":path=="/api/pair"?"pair":path!=null&&path.StartsWith("/api/",StringComparison.Ordinal)?"core":"static";}
 CompanionReply Enter(string path,out SemaphoreSlim slots){slots=AssetPath(path)?assetRequests:PointerPath(path)?pointerRequests:path=="/api/power/cancel"?cancelRequests:PowerPath(path)?powerRequests:requests;return slots.Wait(0)?null:Reject(429,"busy","服务暂时繁忙，请稍后重试。",RouteCategory(path),"concurrency",1);}
 CompanionReply Reject(int status,string code,string message,string route,string scope,int retryAfter){
  // Only fixed server-defined categories enter this bounded in-memory summary. Never retain a URL, address, device ID, cookie or body.
  lock(gate){string key=code+":"+route+":"+scope;Rejection value;if(!rejections.TryGetValue(key,out value)){value=new Rejection{error=code,route=route,scope=scope};rejections.Add(key,value);}if(value.count<long.MaxValue)value.count++;value.lastAt=Now;}
  var reply=retryAfter>0?CompanionReply.Json(status,new{error=code,message=message,retryAfter=retryAfter}):CompanionReply.Error(status,code,message);if(retryAfter>0)reply.Headers["Retry-After"]=retryAfter.ToString(CultureInfo.InvariantCulture);return reply;
 }
 static int Remaining(DateTime until,DateTime now){return Math.Max(1,(int)Math.Ceiling((until-now).TotalSeconds));}
 int RateWait(Dictionary<string,Rate> bucket,string key,int maximum,int seconds){
  DateTime now=Now;Rate rate;if(!bucket.TryGetValue(key,out rate)||rate.until<=now){
   if(bucket.Count>=128){var expired=new List<string>();DateTime first=DateTime.MaxValue;foreach(var entry in bucket){if(entry.Value.until<=now)expired.Add(entry.Key);else if(entry.Value.until<first)first=entry.Value.until;}foreach(var id in expired)bucket.Remove(id);if(bucket.Count>=128)return Remaining(first,now);}
   rate=new Rate{until=now.AddSeconds(seconds)};bucket[key]=rate;
  }
  if(rate.count>=maximum)return Remaining(rate.until,now);rate.count++;return 0;
 }
 object Diagnostics(){var values=new List<object>();foreach(var value in rejections.Values)values.Add(new{error=value.error,route=value.route,scope=value.scope,count=value.count,lastAt=value.lastAt.ToString("o")});return new{coreActive=CoreConcurrency-requests.CurrentCount,assetActive=AssetConcurrency-assetRequests.CurrentCount,pointerActive=PointerConcurrency-pointerRequests.CurrentCount,powerActive=PowerConcurrency-powerRequests.CurrentCount,cancelActive=1-cancelRequests.CurrentCount,rejections=values};}
 void Prune(){foreach(var device in devices)if(device.expiresAt<=Now)device.grants.Active=false;var expired=new List<string>();foreach(var entry in pending)if(entry.Value.expiresAt<=Now)expired.Add(entry.Key);foreach(var id in expired)pending.Remove(id);}
 public string OpenPairing(){lock(gate){if(listener==null)throw new InvalidOperationException("请先开启伴随屏 HTTPS 服务。");return CreatePairingWindow();}}
 string CreatePairingWindow(){lock(gate){if(disposed)throw new ObjectDisposedException("CompanionServer");if(Locked())throw new InvalidOperationException("请先解锁电脑。");if(loadError.Length!=0)throw new InvalidOperationException(loadError);Prune();var bytes=new byte[4];string next;using(var rng=RandomNumberGenerator.Create()){do{rng.GetBytes(bytes);next=(BitConverter.ToUInt32(bytes,0)%1000000).ToString("D6",CultureInfo.InvariantCulture);}while(next==pairingCode);}pairingCode=next;pairingExpires=Now.AddMinutes(2);pairingAttempts=0;return pairingCode;}}
 public void Approve(string id,CompanionGrant grant){lock(gate){if(Locked())throw new InvalidOperationException("请先解锁电脑。");Prune();Pending request;if(!pending.TryGetValue(id,out request)||request.deviceId!=null)throw new InvalidOperationException("配对请求已过期。");if(grant==null)throw new ArgumentNullException("grant");devices.RemoveAll(d=>d.expiresAt<=Now);if(devices.Count>=MaxDevices)throw new InvalidOperationException("最多配对 8 台设备，请先撤销旧设备。");string token=Token();var device=new Device{id=Guid.NewGuid().ToString("N"),name=request.name,tokenHash=Hash(token),csrf=Token(),expiresAt=Now.AddDays(30),grants=Copy(grant)};devices.Add(device);try{Save();}catch{devices.Remove(device);device.grants.Active=false;throw;}request.token=token;request.deviceId=device.id;DateTime claimUntil=Now.AddSeconds(30);if(request.expiresAt<claimUntil)request.expiresAt=claimUntil;}}
 public void Reject(string id){lock(gate)pending.Remove(id);}
 public void UpdateGrant(string id,CompanionGrant grant){lock(gate){if(disposed)throw new ObjectDisposedException("CompanionServer");if(Locked())throw new InvalidOperationException("请先解锁电脑。");if(grant==null)throw new ArgumentNullException("grant");var device=devices.Find(d=>d.id==id&&d.expiresAt>Now);if(device==null)throw new InvalidOperationException("设备授权已失效，请重新选择。");var previous=device.grants;var next=Copy(grant);next.Active=previous.Active;device.grants=next;try{Save();}catch{device.grants=previous;next.Active=false;throw;}previous.Active=false;}}
 public void Revoke(string id){lock(gate){var device=devices.Find(d=>d.id==id);if(device==null)return;device.grants.Active=false;devices.Remove(device);try{Save();}catch{loadError="撤销未能持久保存，服务已停用。请检查运行目录权限。";Stop();throw;}var removed=new List<string>();foreach(var entry in pending)if(entry.Value.deviceId==id)removed.Add(entry.Key);foreach(var key in removed)pending.Remove(key);}}
 public object Status(){lock(gate){Locked();Prune();var waiting=new List<object>();foreach(var request in pending.Values)if(request.deviceId==null)waiting.Add(new{id=request.id,name=request.name,expiresAt=request.expiresAt.ToString("o")});var known=new List<object>();foreach(var device in devices)if(device.expiresAt>Now)known.Add(new{id=device.id,name=device.name,grants=device.grants,expiresAt=device.expiresAt.ToString("o")});return new{enabled=listener!=null,address=Address,pairingExpiresAt=pairingExpires>Now?pairingExpires.ToString("o"):null,pending=waiting,devices=known,error=loadError,diagnostics=Diagnostics()};}}
 internal CompanionReply Dispatch(CompanionRequest request,string origin,int expectedGeneration=-1){
  SemaphoreSlim slots;var rejected=Enter(request==null?null:request.Path,out slots);if(rejected!=null)return rejected;try{return DispatchCore(request,origin,expectedGeneration);}finally{slots.Release();}
 }
 CompanionReply DispatchCore(CompanionRequest request,string origin,int expectedGeneration){
  CompanionGrant grant=null;Device device=null;Dictionary<string,object> body=null;Command command=null;
  try{
   if(request==null||request.Path==null||request.Path.Length>128||request.Path.IndexOfAny(new[]{'%','\\','?','#'})>=0)return CompanionReply.Error(400,"bad_request","请求路径无效。");
   bool write=request.Method=="POST";if(request.Method!="GET"&&!write)return CompanionReply.Error(405,"method_not_allowed","请求方式不受支持。");
   int maximum=BodyLimit(request.Path);if(Encoding.UTF8.GetByteCount(request.Body??"")>maximum)return CompanionReply.Error(413,"body_too_large","请求内容过大。");
   if(write){if(request.Origin!=origin)return CompanionReply.Error(403,"origin_denied","请求来源不受允许。");if(request.ContentType==null||!Regex.IsMatch(request.ContentType,"\\Aapplication/json(?:\\s*;\\s*charset=utf-8)?\\z",RegexOptions.IgnoreCase))return CompanionReply.Error(415,"json_required","请使用 JSON 请求。");try{body=new JavaScriptSerializer{MaxJsonLength=maximum,RecursionLimit=24}.DeserializeObject(request.Body??"") as Dictionary<string,object>;}catch{}if(body==null)return CompanionReply.Error(400,"bad_json","请求内容无效。");}
   else {if(!String.IsNullOrEmpty(request.Origin)&&request.Origin!=origin)return CompanionReply.Error(403,"origin_denied","请求来源不受允许。");body=new Dictionary<string,object>();}
   if(!request.Path.StartsWith("/api/",StringComparison.Ordinal)){if(write)return CompanionReply.Error(405,"method_not_allowed","请求方式不受支持。");return Static(request.Path);}
   lock(gate){
    if(disposed||(expectedGeneration>=0&&expectedGeneration!=generation))return CompanionReply.Error(503,"stopped","伴随屏已关闭。");Prune();bool asset=AssetPath(request.Path),pointer=PointerPath(request.Path),power=PowerPath(request.Path),cancel=request.Path=="/api/power/cancel";string category=RouteCategory(request.Path);var bucket=asset?assetRates:pointer?pointerRates:cancel?cancelRates:power?powerRates:rates;int wait=RateWait(bucket,"ip:"+(request.RemoteAddress??""),pointer?PointerIpPerMinute:cancel?60:power?30:720,60);if(wait==0&&pointer)wait=RateWait(bucket,"ip-second:"+(request.RemoteAddress??""),PointerIpPerSecond,1);if(wait>0)return Reject(429,"rate_limited","请求过于频繁，请稍后重试。",category,"ip",wait);if(Locked())return CompanionReply.Error(423,"locked","电脑已锁定，私人内容和控制已暂停。");
    if(request.Path=="/api/pair"){if(!write)return CompanionReply.Error(405,"method_not_allowed","请使用配对表单。");return Pair(body,request.RemoteAddress??"");}
    if(!Opaque(request.Cookie))return CompanionReply.Error(401,"unpaired","请在电脑确认配对。");string digest=Hash(request.Cookie);device=devices.Find(d=>d.expiresAt>Now&&d.grants.Active&&Equal(d.tokenHash,digest));if(device==null)return CompanionReply.Error(401,"unpaired","设备配对已失效，请重新配对。");grant=device.grants;
    // One library may contain 500 icons. Allow one complete load plus covers and shortcuts; repeated loads remain bounded independently of state/control.
    wait=RateWait(bucket,"device:"+device.id,asset?600:pointer?PointerDevicePerMinute:cancel?30:power?12:180,60);if(wait==0&&pointer)wait=RateWait(bucket,"device-second:"+device.id,PointerDevicePerSecond,1);if(wait>0)return Reject(429,"rate_limited","请求过于频繁，请稍后重试。",category,"device",wait);
    if(request.Path=="/api/session"){
     if(write)return CompanionReply.Error(405,"method_not_allowed","请求方式不受支持。");
     DateTime previous=device.expiresAt;
     // Remember active devices, with at most one protected-store update per day.
     if(previous<Now.AddDays(29)){device.expiresAt=Now.AddDays(30);try{Save();}catch{device.expiresAt=previous;throw;}}
     var session=CompanionReply.Json(200,new{csrf=device.csrf,grants=grant,expiresAt=device.expiresAt.ToString("o"),protocol=1});session.Headers["Set-Cookie"]=SessionCookie(request.Cookie,device.expiresAt);return session;
    }
    if(!Route(request.Path,write,grant))return CompanionReply.Error(403,"not_allowed","该操作未获此设备的共享授权。");
    if(write&&!Equal(request.Csrf,device.csrf))return CompanionReply.Error(403,"csrf_denied","会话校验失败，请重新载入页面。");
    if(write&&!pointer&&!cancel){string id=Text(body,"requestId");if(!Opaque(id))return CompanionReply.Error(400,"request_id_required","请提供有效的唯一请求编号。");string signature=Hash(request.Path+"\n"+request.Body);
     if(device.commands.TryGetValue(id,out command)){if(command.signature!=signature)return CompanionReply.Error(409,"request_id_reused","同一个请求编号不能用于不同操作。");return command.reply??CompanionReply.Error(409,"request_pending","操作仍在执行，结果待确认。请勿重复操作。");}
     // shortcut: retain every accepted ID for this process session; re-pair after 4096 writes instead of forgetting and replaying an old command.
     if(device.commands.Count>=4096||device.cachedBytes>MaxCachedReplies-MaxCommandReply)return Reject(409,"session_budget","本次会话操作已达上限，请在电脑撤销后重新配对。",category,"session",0);command=new Command{signature=signature};device.commands.Add(id,command);device.cachedBytes+=MaxCommandReply;
     if(request.Path=="/api/logout"){Revoke(device.id);var loggedOut=CompanionReply.Json(200,new{ok=true});loggedOut.Headers["Set-Cookie"]=CookieName+"=; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=0";return loggedOut;}
    }
   }
   CompanionReply response;try{response=handler(request.Path,body,grant)??CompanionReply.Error(500,"unavailable","操作未能完成。");}catch{response=CompanionReply.Error(500,"operation_failed","操作未能确认完成，请查看电脑端。不会自动重复执行。");}
   lock(gate){if(command!=null)response=CacheCommand(device,command,response);if(Locked()||!grant.Active)return CompanionReply.Error(423,"unavailable","电脑已锁定或设备授权已撤销。");}return response;
  }catch{var failure=CompanionReply.Error(500,"operation_failed","操作未能确认完成，请查看电脑端。不会自动重复执行。");lock(gate)if(command!=null)failure=CacheCommand(device,command,failure);return failure;}
 }
 static bool Route(string path,bool write,CompanionGrant grant){
  if(!write)return path=="/api/state"||(path=="/api/apps"&&grant.apps)||(Regex.IsMatch(path,"\\A/api/assets/[a-zA-Z0-9_-]{16,96}\\z")&&(grant.apps||grant.music));
  if(PointerPath(path))return grant.pointer;if(PowerPath(path))return grant.power;
  return path=="/api/logout"||path=="/api/action"||path=="/api/tasks"&&grant.tasks||path=="/api/notes"&&grant.notes||path=="/api/settings"&&grant.control||path=="/api/lyrics-style"&&grant.music&&grant.control;
 }
 static CompanionReply CacheCommand(Device device,Command command,CompanionReply response){if(response.Body.Length>MaxCommandReply)response=CompanionReply.Json(response.Status,new{ok=response.Status<400,result="refresh_required",message="操作结果已记录，请刷新状态查看详情。请勿重复提交。"});if(command.reply==null)device.cachedBytes-=MaxCommandReply;else device.cachedBytes-=command.reply.Body.Length;command.reply=response;device.cachedBytes+=response.Body.Length;return response;}
 string SessionCookie(string token,DateTime expires){return CookieName+"="+token+"; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age="+Math.Max(0,(int)(expires-Now).TotalSeconds).ToString(CultureInfo.InvariantCulture)+"; Expires="+expires.ToUniversalTime().ToString("R",CultureInfo.InvariantCulture);}
 CompanionReply Pair(Dictionary<string,object> body,string remote){
  string ticket=Text(body,"ticket");if(ticket!=null){if(!Opaque(ticket)||body.Count!=1)return CompanionReply.Error(400,"bad_request","配对请求无效。");Pending found=null;foreach(var entry in pending.Values)if(Equal(entry.ticket,ticket))found=entry;if(found==null)return CompanionReply.Error(401,"pair_expired","配对请求已过期，请重新输入电脑上的配对码。");if(found.deviceId==null)return CompanionReply.Json(202,new{status="pending"});var device=devices.Find(d=>d.id==found.deviceId&&d.grants.Active);if(device==null)return CompanionReply.Error(401,"pair_expired","配对已被撤销。");var paired=CompanionReply.Json(200,new{status="paired"});paired.Headers["Set-Cookie"]=SessionCookie(found.token,device.expiresAt);pending.Remove(found.id);return paired;}
  int wait=RateWait(rates,"pair:"+remote,6,60);if(wait>0)return Reject(429,"pair_limited","配对尝试过多，请稍后重试。","pair","pair",wait);if(pairingExpires<=Now||pairingCode.Length==0||pairingAttempts>=12)return CompanionReply.Error(401,"pair_closed","请在电脑端开启新的配对窗口。");pairingAttempts++;if(!Equal(Text(body,"code"),pairingCode))return CompanionReply.Error(401,"pair_code","配对码不正确或已过期。");string name=Text(body,"name");if(body.Count!=2||String.IsNullOrWhiteSpace(name)||name.Length>40||Regex.IsMatch(name,"[\\x00-\\x1f\\x7f]"))return CompanionReply.Error(400,"bad_name","设备名称应为 1–40 个可见字符。");if(pending.Count>=4||devices.FindAll(d=>d.expiresAt>Now).Count>=MaxDevices)return Reject(409,"device_limit","配对设备已达上限，请先在电脑清理旧请求或设备。","pair","devices",0);var next=new Pending{id=Guid.NewGuid().ToString("N"),ticket=Token(),name=name.Trim(),expiresAt=Now.AddMinutes(2)};pending.Add(next.id,next);return CompanionReply.Json(202,new{status="pending",ticket=next.ticket});
 }
 CompanionReply Static(string path){
  string name,type;bool shared=false;if(path=="/"||path=="/index.html"){name="index.html";type="text/html; charset=utf-8";}else if(path=="/companion.css"||path=="/companion-appearance.css"||path=="/dock-lyrics.css"){name=path.Substring(1);type="text/css; charset=utf-8";shared=path=="/dock-lyrics.css";}else if(path=="/companion.js"||path=="/companion-scan.js"||path=="/jsqr.js"||path=="/companion-glass.js"||path=="/companion-lyrics.js"||path=="/companion-settings.js"||path=="/companion-widgets.js"||path=="/lyrics-style.js"){name=path.Substring(1);type="application/javascript; charset=utf-8";shared=path=="/lyrics-style.js";}else if(path=="/companion-scene.jpg"){name="companion-scene.jpg";type="image/jpeg";}else if(path=="/manifest.webmanifest"){name="manifest.webmanifest";type="application/manifest+json; charset=utf-8";}else if(path=="/companion-icon.png"){name="companion-icon.png";type="image/png";}else return CompanionReply.Error(404,"not_found","页面不存在。");
  try{string file=Path.Combine(shared?Path.GetDirectoryName(webRoot.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)):webRoot,name);if(!File.Exists(file)||new FileInfo(file).Length>524288||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)return CompanionReply.Error(503,"assets_missing","伴随屏网页资源未正确安装。");return new CompanionReply{Status=200,ContentType=type,Body=File.ReadAllBytes(file)};}catch{return CompanionReply.Error(503,"assets_missing","伴随屏网页资源暂不可用。");}
 }
 void Load(){try{if(!File.Exists(store))return;if(new FileInfo(store).Length>65536)throw new IOException();byte[] clear=ProtectedData.Unprotect(File.ReadAllBytes(store),null,DataProtectionScope.CurrentUser);var saved=json.Deserialize<List<Device>>(Encoding.UTF8.GetString(clear));if(saved==null||saved.Count>MaxDevices)throw new IOException();var seen=new HashSet<string>();foreach(var device in saved){if(device==null||!Opaque(device.id)||String.IsNullOrEmpty(device.name)||device.name.Length>40||device.tokenHash==null||device.tokenHash.Length!=44||!Opaque(device.csrf)||device.grants==null||!seen.Add(device.id))throw new IOException();device.commands=new Dictionary<string,Command>();device.grants.Active=device.expiresAt>Now;devices.Add(device);}}catch{devices.Clear();loadError="无法读取已配对设备，原文件已保留。请检查用户账户和运行目录。";}}
 void RenewSessionCsrf(){var previous=new List<string>();foreach(var device in devices){previous.Add(device.csrf);device.csrf=Token();}try{Save();}catch{for(int index=0;index<devices.Count;index++)devices[index].csrf=previous[index];throw;}}
 void Save(){if(loadError.Length>0)throw new IOException(loadError);string directory=Path.GetDirectoryName(store);Directory.CreateDirectory(directory);string temporary=store+".pending-"+Guid.NewGuid().ToString("N");try{File.WriteAllBytes(temporary,ProtectedData.Protect(Encoding.UTF8.GetBytes(json.Serialize(devices)),null,DataProtectionScope.CurrentUser));if(File.Exists(store))File.Replace(temporary,store,null);else File.Move(temporary,store);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
 public void Dispose(){Stop();lock(gate)disposed=true;}
}
