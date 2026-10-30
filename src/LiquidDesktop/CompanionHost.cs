using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using QRCoder;

internal sealed class CompanionConfiguration {
 public string address="";public int port=9443;
 public Dictionary<string,string> apps=new Dictionary<string,string>();
}

internal sealed class CompanionHost : IDisposable {
 readonly Control owner;readonly string root;readonly AppLibrary library;readonly CompanionServer server;readonly CompanionBridge bridge;readonly string file;readonly SharedDesktopData shared;readonly DockLyrics lyrics;
 readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly CompanionDiscovery discovery=new CompanionDiscovery();CompanionConfiguration config=new CompanionConfiguration();CompanionManagement management;volatile bool closed,sessionLocked;CompanionNetworkChoice activeNetwork;internal Func<CompanionNetworkChoice[]> ReadNetworks=CompanionConnection.Networks;
 readonly object videoGate=new object();Task<CompanionReply> videoCheck;string videoKey="",videoPeer="",videoOffer="";CompanionGrant videoGrant;DateTime videoCheckedAt;int videoRevision,islandRevision;volatile bool videoApplying;bool stoppingRemote;
 internal Func<string,string,string,Task<Dictionary<string,object>>> ReadVideoPlan=CompanionConnection.CheckVideo;
 internal Func<string,string,string,Task> ApplyVideoPlan=CompanionConnection.ApplyVideo;
 internal Func<string,bool> ConfirmVideo;
 [DllImport("user32.dll",SetLastError=true)]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
 [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetUserObjectInformation(IntPtr handle,int index,StringBuilder value,int bytes,out int needed);
 public CompanionHost(Control desktop,string directory,AppLibrary apps,DesktopPreferences settings,SharedDesktopData data,DockLyrics lyrics){
  owner=desktop;root=directory;library=apps;shared=data;this.lyrics=lyrics;file=Path.Combine(root,"runtime-data","companion-settings.json");
  ConfirmVideo=text=>{Show();return MessageBox.Show(DialogOwner??owner as Form,text,"修复投屏连接",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;};
  if(File.Exists(file))try{if(new FileInfo(file).Length<1048576){var saved=json.Deserialize<CompanionConfiguration>(File.ReadAllText(file));if(saved!=null&&saved.apps!=null&&saved.apps.Count<=500)config=saved;}}catch{}
  bridge=new CompanionBridge(root,apps,settings,data,lyrics,Dispatch,IsLocked,streamAddress:()=>server.LocalAddress);bridge.Allow(config.apps);server=new CompanionServer(root,bridge.Handle,IsLocked);server.BeforeScreen=PrepareVideo;
  server.VideoApprovalPending=()=>videoApplying&&!sessionLocked&&!closed;
  SystemEvents.SessionSwitch+=SessionChanged;NetworkChange.NetworkAddressChanged+=NetworkChanged;
 }
 void NetworkChanged(object sender,EventArgs e){if(closed||activeNetwork==null||!owner.IsHandleCreated)return;try{owner.BeginInvoke(new Action(()=>{if(!closed&&management!=null)management.RefreshNetwork();}));}catch(InvalidOperationException){}}
 void SessionChanged(object sender,SessionSwitchEventArgs e){if(e.Reason==SessionSwitchReason.SessionLock||e.Reason==SessionSwitchReason.SessionLogoff){sessionLocked=true;Interlocked.Increment(ref islandRevision);bridge.StopControls();}else if(e.Reason==SessionSwitchReason.SessionUnlock){sessionLocked=false;Interlocked.Increment(ref islandRevision);}}
 bool IsLocked(){if(closed||sessionLocked)return true;IntPtr input=OpenInputDesktop(0,false,1);if(input==IntPtr.Zero)return true;try{int needed;var name=new StringBuilder(256);return !GetUserObjectInformation(input,2,name,512,out needed)||!String.Equals(name.ToString(),"Default",StringComparison.OrdinalIgnoreCase);}finally{CloseDesktop(input);}}
 object Dispatch(Func<object> operation){
  if(closed||owner.IsDisposed||!owner.IsHandleCreated)throw new InvalidOperationException();if(!owner.InvokeRequired)return operation();
  var result=new TaskCompletionSource<object>();int cancelled=0;
  owner.BeginInvoke(new Action(()=>{if(Volatile.Read(ref cancelled)!=0||closed){result.TrySetCanceled();return;}try{result.TrySetResult(operation());}catch(Exception ex){result.TrySetException(ex);}}));
  if(!((IAsyncResult)result.Task).AsyncWaitHandle.WaitOne(5000)){Interlocked.Exchange(ref cancelled,1);throw new TimeoutException();}return result.Task.GetAwaiter().GetResult();
 }
 public event Action ManagementRequested;
 public event Action ManagementChanged;
 public Form DialogOwner {get;set;}
 public void Show(Form parent=null){if(closed)return;if(parent!=null)DialogOwner=parent;var handler=ManagementRequested;if(handler!=null)handler();}
 CompanionManagement Management {get{if(management==null)management=new CompanionManagement(this);return management;}}
 public object ManagementSnapshot(){return Management.Snapshot();}
 internal int IslandRevision {get{return Volatile.Read(ref islandRevision);}}
 internal object IslandSnapshot(){bool running=!closed&&server.IsRunning&&!IsLocked();return new{running=running,connected=running?server.ActiveDeviceCount:0,screen=running&&bridge.ScreenActive,pointer=running&&bridge.PointerActive};}
 internal async Task StopRemoteControls(){
  if(closed||stoppingRemote)return;stoppingRemote=true;ResetVideo();int revision=videoRevision;Interlocked.Increment(ref islandRevision);
  try{await Task.Run(()=>{bridge.StopControls();if(bridge.PointerActive)throw new InvalidOperationException();});if(!closed&&revision==videoRevision)Management.VideoNotice("远程控制已停止，伴随屏连接和设备授权已保留。",false);}
  catch{if(!closed&&revision==videoRevision){Show();Management.VideoNotice("远程操作未能完全停止，请在电脑检查鼠标和投屏状态后重试。",true);}}
  finally{stoppingRemote=false;Interlocked.Increment(ref islandRevision);}
 }
 public void Manage(Dictionary<string,object> message){if(!closed)Management.Handle(message);}
 void NotifyManagement(){var handler=ManagementChanged;if(!closed&&handler!=null)handler();}

 bool VideoBusy {get{lock(videoGate)return videoKey.Length>0&&videoCheck!=null&&!videoCheck.IsCompleted;}}
 void VideoNotice(string message,bool error,int revision){if(!closed&&revision==videoRevision)Management.VideoNotice(message,error);}
 CompanionReply VideoError(int status,string code,string message,int revision){VideoNotice(message,true,revision);return CompanionReply.Error(status,code,message);}
 CompanionReply PrepareVideo(string peer,CompanionGrant grant,string offer){
  string local=server.LocalAddress;IPAddress ip;if(!grant.Active||!grant.screen||IsLocked()||String.IsNullOrEmpty(local)||!IPAddress.TryParse(peer,out ip)||!CompanionConnection.Lan(ip))return CompanionReply.Error(403,"screen_network_denied","请在同一局域网重新连接已授权的 iPad。");
  if(offer==null||offer.Length>49152||!offer.StartsWith("v=0\r\n",StringComparison.Ordinal))return CompanionReply.Error(400,"invalid_stream","投屏连接请求无效，请重新开始。");
  lock(videoGate){
   if(videoGrant!=null&&(videoGrant!=grant||videoKey!=local+"|"+peer)&&bridge.ScreenActive)return CompanionReply.Error(409,"screen_busy","已有投屏正在使用，请先停止原来的画面。");
   if(videoCheck!=null&&!videoCheck.IsCompleted)return CompanionReply.Error(409,"screen_network_pending","正在检查投屏连接，请留意电脑上的确认窗口…");
   // Polls reuse one offer; a fresh user attempt must not inherit a failed repair.
   if(videoCheck!=null&&videoKey==local+"|"+peer&&videoGrant==grant&&DateTime.UtcNow-videoCheckedAt<TimeSpan.FromMinutes(2)&&(videoCheck.Result==null||videoOffer==offer))return videoCheck.Result;
   videoKey=local+"|"+peer;videoPeer=peer;videoGrant=grant;videoOffer=offer;videoCheckedAt=DateTime.UtcNow;int revision=videoRevision;
   var completion=new TaskCompletionSource<CompanionReply>();videoCheck=completion.Task;
   try{owner.BeginInvoke(new Action(async()=>{try{completion.TrySetResult(await CheckVideo(local,peer,grant,revision));}catch{completion.TrySetResult(VideoError(503,"screen_network_failed","投屏连接检查失败，请在电脑点“检查连接”后重试。",revision));}finally{if(revision==videoRevision)NotifyManagement();}}));}
   catch{completion.TrySetResult(CompanionReply.Error(503,"screen_network_stopped","伴随屏已关闭。"));}
   return CompanionReply.Error(409,"screen_network_pending","正在检查投屏连接，请留意电脑上的确认窗口…");
  }
 }
 bool VideoCurrent(string local,CompanionGrant grant,int revision){return revision==videoRevision&&grant.Active&&grant.screen&&!IsLocked()&&server.LocalAddress==local;}
 async Task<CompanionReply> CheckVideo(string local,string peer,CompanionGrant grant,int revision){
  try{
   VideoNotice("正在检查这台 iPad 的投屏连接…",false,revision);
   var plan=await ReadVideoPlan(root,local,peer);
   if(!VideoCurrent(local,grant,revision))throw new InvalidOperationException("连接或设备权限已变化，请重新开始投屏。");
   if(CompanionConnection.Flag(plan,"needsApproval")){
    string text="为当前已授权的 iPad 修复投屏连接：\n电脑 "+local+" → iPad "+peer+"\n网络："+CompanionConnection.Text(plan,"interfaceAlias")+"（专用）\n\n仅允许 MediaMTX 的 UDP 31890 视频通道访问这台 iPad。\n"+(CompanionConnection.Text(plan,"oldAddress").Length>0?"将替换原来的地址 "+CompanionConnection.Text(plan,"oldAddress")+" → "+CompanionConnection.Text(plan,"oldPeer")+"。\n":"将创建此设备专用的视频规则。\n")+"\n是否更新？确认后可能出现 Windows 管理员提示，iPad 会继续连接。";
    VideoNotice("等待电脑确认投屏连接，请核对弹窗中的网络和设备。",false,revision);
    if(!ConfirmVideo(text))return VideoError(409,"screen_network_cancelled","电脑已取消投屏连接修复。需要时在 iPad 重新开始投屏。",revision);
    if(!VideoCurrent(local,grant,revision))throw new InvalidOperationException("连接或设备权限已变化，请重新开始投屏。");
    VideoNotice("等待 Windows 管理员确认并更新投屏连接…",false,revision);
    videoApplying=true;try{await ApplyVideoPlan(root,local,peer);}finally{videoApplying=false;}
    VideoNotice("正在核对投屏连接配置…",false,revision);plan=await ReadVideoPlan(root,local,peer);
   }
   if(!VideoCurrent(local,grant,revision)||!CompanionConnection.Flag(plan,"configured"))throw new InvalidOperationException("投屏连接尚未准备好，请在电脑重新检查连接。");
   VideoNotice("投屏连接已准备好。请在 iPad 等待画面；若已停止，请重新开始投屏。",false,revision);
   return null;
  }catch(System.ComponentModel.Win32Exception ex){return ex.NativeErrorCode==1223?VideoError(409,"screen_network_cancelled","管理员确认已取消，请在 iPad 重新开始投屏。",revision):VideoError(503,"screen_network_failed","无法启动系统配置（Windows 错误 "+ex.NativeErrorCode+"），请在电脑重新检查连接。",revision);}
   catch(Exception ex){return VideoError(503,"screen_network_failed",ex is InvalidOperationException||ex is TimeoutException?ex.Message:"投屏连接检查失败，请在电脑点“检查连接”后重试。",revision);}
 }
 void ResetVideo(){lock(videoGate){videoRevision++;videoKey="";videoPeer="";videoOffer="";videoGrant=null;}}
 void StartSources(){Interlocked.Increment(ref islandRevision);lyrics.CompanionActive=true;shared.Widgets.EnsureCompanionSources();shared.Invalidate();}
 void Stop(){try{ResetVideo();activeNetwork=null;lyrics.CompanionActive=false;discovery.Stop();server.Stop();bridge.StopControls();}finally{Interlocked.Increment(ref islandRevision);}}
 public void Dispose(){if(closed)return;closed=true;SystemEvents.SessionSwitch-=SessionChanged;NetworkChange.NetworkAddressChanged-=NetworkChanged;Stop();if(management!=null)management.Dispose();discovery.Dispose();server.Dispose();bridge.Dispose();}
 static bool Private(IPAddress address){byte[] a=address.GetAddressBytes();return a.Length==4&&(a[0]==10||a[0]==192&&a[1]==168||a[0]==172&&a[1]>=16&&a[1]<=31);}
 public static string[] Addresses(){return NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).Where(Private).Select(a=>a.ToString()).Distinct().ToArray();}
 void Save(){Directory.CreateDirectory(Path.GetDirectoryName(file));DesktopSettingsFiles.Write(file,json.Serialize(config));}
 sealed class CompanionManagement : IDisposable {
  readonly CompanionHost host;readonly System.Windows.Forms.Timer refresh=new System.Windows.Forms.Timer{Interval=5000};
  CompanionNetworkChoice[] networks=new CompanionNetworkChoice[0];List<AppLibraryEntry> entries=new List<AppLibraryEntry>();
  bool readingNetwork,connecting,working,entryQr,appsLoaded,noticeError;int connectionRevision,operationRevision;string selectedAddress="",notice="",code="",qrImage="";int selectedPort;
  string legacySetup="",proposedHost="prismglass-"+Guid.NewGuid().ToString("N").Substring(0,8)+".local";object result;
  bool IsDisposed {get{return host.closed;}}
  internal CompanionManagement(CompanionHost parent){host=parent;selectedAddress=host.config.address;selectedPort=Math.Max(1024,Math.Min(65535,host.config.port));refresh.Tick+=(s,e)=>{if(host.activeNetwork!=null)RefreshNetwork();};refresh.Start();}
  void Notify(){if(!IsDisposed)host.NotifyManagement();}
  internal void VideoNotice(string message,bool error){notice=message;noticeError=error;Notify();}
  public object Snapshot(){
   var state=host.json.Deserialize<Dictionary<string,object>>(host.json.Serialize(host.server.Status()));DateTime expires;
   if(!host.server.IsRunning||host.IsLocked()||host.activeNetwork!=null&&host.discovery.Status=="error"||!entryQr&&(!DateTime.TryParseExact(CompanionBridge.Text(state,"pairingExpiresAt"),"o",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out expires)||expires.ToUniversalTime()<=DateTime.UtcNow))ClearPairing();
   string status=host.server.IsRunning?(host.IsLocked()?"电脑已锁定 · 控制已暂停":host.discovery.Status=="error"?"名称发现失败："+host.discovery.Error:host.activeNetwork!=null&&!host.discovery.IsRegistered?"正在发布固定名称…":"伴随屏已开启"):"伴随屏已关闭";
   return new {status=status,service=state,notice=notice,error=noticeError,busy=working||connecting||host.VideoBusy,readingNetwork=readingNetwork,address=selectedAddress,port=selectedPort,networks=networks.Select(n=>new{address=n.address,label=n.Description}).ToArray(),qr=qrImage,code=code,entryQr=entryQr,result=result,appsLoaded=appsLoaded,apps=entries.Select(e=>new{id=e.id,name=e.name,exists=e.exists,allowed=Allowed(e)}).ToArray()};
  }
  bool Allowed(AppLibraryEntry entry){string signature;return host.config.apps.TryGetValue(entry.id,out signature)&&signature==CompanionBridge.EntrySignature(entry);}
  static CompanionGrant Grants(Dictionary<string,object> message){object raw;if(!message.TryGetValue("grants",out raw)||!(raw is Dictionary<string,object>))throw new ArgumentException("权限草稿无效，请重新选择设备。");var values=(Dictionary<string,object>)raw;var keys=new[]{"apps","music","control","tasks","notes","pointer","power","screen","keyboard"};if(values.Count!=keys.Length||keys.Any(k=>!values.ContainsKey(k)||!(values[k] is bool)))throw new ArgumentException("权限草稿无效，请重新选择设备。");return new CompanionGrant{apps=(bool)values["apps"],music=(bool)values["music"],control=(bool)values["control"],tasks=(bool)values["tasks"],notes=(bool)values["notes"],pointer=(bool)values["pointer"],power=(bool)values["power"],screen=(bool)values["screen"],keyboard=(bool)values["keyboard"]};}
  string DeviceName(string id,bool pending){var snapshot=host.json.Deserialize<Dictionary<string,object>>(host.json.Serialize(host.server.Status()));var list=snapshot[pending?"pending":"devices"] as System.Collections.IEnumerable;if(list!=null)foreach(Dictionary<string,object> item in list)if(CompanionBridge.Text(item,"id")==id)return CompanionBridge.Text(item,"name");throw new InvalidOperationException("设备或请求已失效，请重新选择。");}
  public async void Handle(Dictionary<string,object> message){
   string command=CompanionBridge.Text(message,"command"),request=CompanionBridge.Text(message,"request");
   if(command=="read"){RefreshNetwork();if(!appsLoaded&&!working){working=true;try{await LoadApps();}catch(Exception ex){ConnectionError(ex);}finally{working=false;Notify();}}else Notify();return;}
   if(command=="stop"){operationRevision++;connectionRevision++;host.Stop();ClearPairing();notice="伴随屏已关闭，配对与共享设置已保留。";noticeError=false;result=new{request=request,command=command,ok=true};Notify();return;}
   if(working||connecting||host.VideoBusy)return;
   int operation=++operationRevision;working=true;notice="";noticeError=false;Notify();bool ok=false;
   try{
    switch(command){
     case "connect":case "pair":
      object rawPort;if(!message.TryGetValue("port",out rawPort)||!(rawPort is int)||(int)rawPort<1024||(int)rawPort>65535)throw new ArgumentException("端口须在 1024 到 65535 之间。");selectedPort=(int)rawPort;selectedAddress=CompanionBridge.Text(message,"address");
      if(command=="pair"){if(!await EnsureConnected())return;ClearPairing();string pairing=host.server.OpenPairing();DrawQr(host.server.Address+"/#pair="+pairing,false);code="配对码："+pairing+" · 2 分钟有效";}else if(!await Connect())return;break;
     case "import":if(!await ImportPreviousSetup())return;break;
     case "export":if(!ExportCertificate())return;break;
     case "apps-refresh":await LoadApps();break;
     case "apps-save":await SaveApps(message);break;
     case "approve":case "grant":case "revoke":case "reject":
      string id=CompanionBridge.Text(message,"id"),name=DeviceName(id,command=="approve"||command=="reject");
      if(command=="approve"){host.server.Approve(id,Grants(message));notice="已批准 "+name+"，请在 iPad 等待连接完成。";}
      else if(command=="grant"){host.server.UpdateGrant(id,Grants(message));notice="已更新 "+name+" 的共享权限。";}
      else if(command=="revoke"){host.server.Revoke(id);notice="已撤销 "+name+" 的共享授权。";}
      else{host.server.Reject(id);notice="已拒绝 "+name+" 的请求。";}break;
     default:throw new ArgumentException("无法识别的伴随屏操作。");
    }ok=true;
   }catch(Exception ex){if(operation==operationRevision)ConnectionError(ex);}finally{working=false;if(operation==operationRevision){if(!ok&&!noticeError)notice="操作已取消。";result=new{request=request,command=command,ok=ok};}Notify();}
  }
  void DrawQr(string url,bool entry){ClearPairing();using(var generator=new QRCodeGenerator())using(var data=generator.CreateQrCode(url,QRCodeGenerator.ECCLevel.M))using(var qr=new QRCode(data))using(var bitmap=qr.GetGraphic(Math.Max(1,222/data.ModuleMatrix.Count),Color.Black,Color.White,true))using(var bytes=new MemoryStream()){bitmap.Save(bytes,System.Drawing.Imaging.ImageFormat.Png);qrImage="data:image/png;base64,"+Convert.ToBase64String(bytes.ToArray());}entryQr=entry;}
  void ClearPairing(){entryQr=false;qrImage="";code="";}
  internal async void RefreshNetwork(){if(readingNetwork||connecting||IsDisposed)return;readingNetwork=true;int revision=connectionRevision;try{var next=await Task.Run(()=>host.ReadNetworks());if(IsDisposed||connecting||revision!=connectionRevision)return;networks=next;if(host.activeNetwork!=null&&!next.Any(n=>CompanionConnection.SameNetwork(n,host.activeNetwork))){host.Stop();ClearPairing();notice="网络已变化，连接已暂停。请点“检查并连接”，iPad 固定入口保持不变。";Notify();}RefreshAddresses();}catch{if(!IsDisposed&&revision==connectionRevision)notice="暂时无法读取当前网络，请点“检查并连接”重试。";}finally{readingNetwork=false;Notify();}}
  void RefreshAddresses(){Dictionary<string,object> record=null;try{record=CompanionConnection.ReadRecord(host.root);}catch{}var choice=CompanionConnection.Select(networks,selectedAddress.Length>0?selectedAddress:host.config.address,CompanionConnection.Text(record,"interfaceId"));selectedAddress=choice==null?"":choice.address;}
  void ConnectionError(Exception ex){if(IsDisposed)return;noticeError=true;notice=ex is IOException?"连接设置未能保存，请检查磁盘空间和目录权限。":ex is UnauthorizedAccessException?"连接设置没有写入权限，请使用原来的电脑用户重试。":ex is System.ComponentModel.Win32Exception?"系统配置已取消。可再次点“检查并连接”。":ex is InvalidOperationException||ex is ArgumentException||ex is TimeoutException?ex.Message:"连接未完成，请检查当前网络和电脑端配置。";Notify();}
  async Task<bool> Connect(){if(!await EnsureConnected())return false;string peer,offer;CompanionGrant grant;lock(host.videoGate){peer=host.videoPeer;grant=host.videoGrant;offer=host.videoOffer;host.videoKey="";}if(grant!=null&&grant.Active&&peer.Length>0)host.PrepareVideo(peer,grant,offer);DrawQr(host.server.Address+"/",true);code="固定入口 · 已配对设备扫码直接打开";Notify();return true;}
  async Task<bool> ImportPreviousSetup(){if(connecting){notice="连接检查正在进行，请稍后重试。";return false;}if(CompanionConnection.ReadRecord(host.root)!=null){notice="此安装已有连接配置，将继续沿用；无需再次导入。";return true;}int revision=operationRevision;using(var choose=new OpenFileDialog{Title="选择旧版 setup-record.json，沿用 iPad 已信任的证书",Filter="连接配置记录|setup-record.json",CheckFileExists=true}){if(choose.ShowDialog(host.DialogOwner??host.owner as Form)!=DialogResult.OK)return false;if(IsDisposed||revision!=operationRevision)return false;legacySetup=choose.FileName;}return await Connect();}
  bool ExportCertificate(){byte[] certificate=CompanionConnection.PublicCertificate(host.root);using(var save=new SaveFileDialog{Title="导出公开证书到你选择的位置，再发送给 iPad",FileName="PrismGlass-Companion-CA.cer",Filter="公开证书|*.cer",DefaultExt="cer",AddExtension=true,OverwritePrompt=true}){if(save.ShowDialog(host.DialogOwner??host.owner as Form)!=DialogResult.OK)return false;File.WriteAllBytes(save.FileName,certificate);}notice="公开证书已导出（不包含私钥）。请按连接步骤在 iPad 安装并启用信任，再扫码配对。";return true;}
  async Task<bool> EnsureConnected(){if(connecting)return false;if(host.server.IsRunning&&host.activeNetwork==null){host.StartSources();Notify();return true;}int attempt=++connectionRevision;connecting=true;Notify();
   try{notice="正在检查当前网络与固定入口…";Notify();networks=await Task.Run(()=>host.ReadNetworks());if(IsDisposed||attempt!=connectionRevision)return false;if(host.server.IsRunning){if(networks.Any(n=>CompanionConnection.SameNetwork(n,host.activeNetwork))){if(host.discovery.Status=="error"||host.discovery.Status=="stopped"){host.discovery.Stop();var network=host.activeNetwork;host.discovery.Start(network.address,host.config.port,new Uri(host.server.Address).Host,network.index);}host.StartSources();if(!await WaitDiscovery(attempt))return false;notice="";Notify();return true;}host.Stop();ClearPairing();}RefreshAddresses();var choice=networks.FirstOrDefault(n=>n.address==(selectedAddress));if(choice==null)throw new InvalidOperationException("没有可用的局域网连接。请先让电脑连接 Wi-Fi 或有线网络。");
    bool trust=false;if(!choice.IsPrivate){if(choice.category!=0)throw new InvalidOperationException("无法确认网络类别，请先在 Windows 网络设置中检查此连接。");if(MessageBox.Show(host.DialogOwner??host.owner as Form,"当前网络："+choice.Description+"\n\n只有你信任的家庭 Wi-Fi 或个人热点才适合开启。是否将此网络设为专用，并继续检查？","确认当前网络",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return false;if(IsDisposed||attempt!=connectionRevision)return false;trust=true;}
    var record=CompanionConnection.ReadRecord(host.root);string name=record==null?proposedHost:CompanionConnection.Text(record,"hostName"),setup=record==null?legacySetup:CompanionConnection.Text(record,"setupRecord");int requestedPort=selectedPort;bool initialize=record==null&&String.IsNullOrEmpty(setup);
    var check=await CompanionConnection.Check(host.root,choice,name,requestedPort,setup,trust,initialize);if(IsDisposed||attempt!=connectionRevision)return false;if(!CompanionConnection.Flag(check,"ready"))throw new InvalidOperationException(CompanionConnection.Issues(check));
    if(CompanionConnection.Flag(check,"needsApproval")){string changes="固定入口：https://"+name+":"+requestedPort+"\n网络："+choice.Description+"\n\n"+(initialize?"首次连接：创建专用 CA（3 年）与 HTTPS 证书（1 年），私钥不可导出；配置后需把公开证书安装并信任到 iPad。若以前配置过，请取消并使用“导入旧连接配置”。\n":"沿用 iPad 已信任的 CA。\n")+"为固定名称配置 HTTPS 与当前用户访问权限；只允许此网卡、专用网络、本地子网访问 TCP "+requestedPort+"；UDP 5353 仅允许本安装的 PrismGlass 回答固定名称。\n"+(trust?"将当前网络改为专用。\n":"")+"不修改 Windows 根证书信任。是否应用？";if(MessageBox.Show(host.DialogOwner??host.owner as Form,changes,"配置伴随屏连接",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return false;if(IsDisposed||attempt!=connectionRevision)return false;notice="等待 Windows 管理员确认并配置连接…";Notify();await CompanionConnection.Apply(host.root,choice,name,requestedPort,setup,trust,initialize);check=await CompanionConnection.Check(host.root,choice,name,requestedPort,setup,false);if(IsDisposed||attempt!=connectionRevision)return false;if(!CompanionConnection.Flag(check,"configured"))throw new InvalidOperationException("系统配置未通过检查："+CompanionConnection.Issues(check));}
    var current=await Task.Run(()=>host.ReadNetworks());if(IsDisposed||attempt!=connectionRevision)return false;choice=current.FirstOrDefault(n=>n.id==choice.id&&n.address==choice.address&&n.IsPrivate);if(choice==null)throw new InvalidOperationException("网络在检查过程中变化，请再次连接。");Start(choice,name,requestedPort);if(!await WaitDiscovery(attempt))return false;notice=initialize?"电脑配置完成。请在“首次连接指南”导出证书，到 iPad 安装并信任后再配对。":"已开启固定入口。首次使用此入口，请生成配对二维码。";return true;
   }finally{connecting=false;Notify();}
  }
  async Task<bool> WaitDiscovery(int attempt){for(int i=0;i<100;i++){if(IsDisposed||attempt!=connectionRevision||!host.server.IsRunning)return false;if(host.discovery.IsRegistered)return true;if(host.discovery.Status=="error"){string error=host.discovery.Error;host.Stop();throw new InvalidOperationException(error);}await Task.Delay(100);}host.Stop();throw new TimeoutException("固定名称发布超时，请重新检查连接。");}
  void Start(CompanionNetworkChoice choice,string name,int requestedPort){string oldAddress=host.config.address;int oldPort=host.config.port;host.server.Start(choice.address,requestedPort,name);try{host.discovery.Start(choice.address,requestedPort,name,choice.index);host.config.address=choice.address;host.config.port=requestedPort;host.Save();host.activeNetwork=choice;host.StartSources();}catch{host.config.address=oldAddress;host.config.port=oldPort;host.Stop();throw;}Notify();}
  async Task LoadApps(){var snapshot=await Task.Run(()=>host.library.GetSnapshot());if(IsDisposed)return;entries=snapshot.entries.Where(e=>e.kind!="action").OrderBy(e=>e.name).ToList();appsLoaded=true;}
  async Task SaveApps(Dictionary<string,object> message){
   object raw;if(!message.TryGetValue("ids",out raw)||!(raw is System.Collections.IEnumerable)||raw is string)throw new ArgumentException("应用共享范围无效。");var next=new Dictionary<string,string>();foreach(object item in (System.Collections.IEnumerable)raw){var id=item as string;var entry=entries.Find(e=>e.id==id);if(entry==null)throw new InvalidOperationException("应用列表已更新，请刷新后重新选择。");if(!entry.exists&&!Allowed(entry))throw new InvalidOperationException("不能共享已失效的入口。");next[id]=CompanionBridge.EntrySignature(entry);}if(next.Count>500)throw new InvalidOperationException("最多共享 500 个入口。");
   int revision=operationRevision;var previous=host.config.apps;host.config.apps=next;try{host.Save();}catch{host.config.apps=previous;throw;}await Task.Run(()=>host.bridge.Allow(next));if(!IsDisposed&&revision==operationRevision)notice="应用共享范围已保存。";
  }
  public void Dispose(){refresh.Dispose();ClearPairing();}
 }
}
