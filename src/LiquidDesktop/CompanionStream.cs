using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal interface ICompanionStreamRuntime : IDisposable {
 string Instance {get;}
 Rectangle CaptureBounds {get;}
 object Describe();
 string Start(string mode,int monitor,string quality,string offer,bool portrait=false);
 bool Alive {get;}
 void Stop();
}

internal interface ICompanionAudioRuntime {
 bool AudioAlive {get;}
 string StartAudio(string offer);
 void StopAudio();
}

// A published ID keeps its hardware identity even when displays reorder or disconnect.
internal sealed class CompanionMonitorSelection {
 internal const int MaximumId=4095;
 readonly List<string> identities=new List<string>();
 internal List<CompanionPhysicalMonitor> Publish(List<CompanionPhysicalMonitor> current){
  var result=new List<CompanionPhysicalMonitor>();
  foreach(var screen in current){
   if(result.Count==16)break;if(String.IsNullOrWhiteSpace(screen.Identity))continue;
   int id=identities.FindIndex(value=>String.Equals(value,screen.Identity,StringComparison.OrdinalIgnoreCase));
   if(id<0){if(identities.Count>MaximumId)throw new InvalidOperationException("显示器连接变化过多，请重启 PrismGlass 后重试。");id=identities.Count;identities.Add(screen.Identity);}
   screen.Id=id;result.Add(screen);
  }
  return result;
 }
 internal CompanionPhysicalMonitor Resolve(int id,List<CompanionPhysicalMonitor> current){
  if(id<0||id>=identities.Count)throw new ArgumentException("请重新选择投屏来源。");
  var screen=current.Find(value=>String.Equals(value.Identity,identities[id],StringComparison.OrdinalIgnoreCase));
  if(screen==null)throw new ArgumentException("所选显示器已断开或发生变化，请重新选择。");return screen;
 }
 internal static bool SameCapture(CompanionPhysicalMonitor expected,CompanionPhysicalMonitor current){return expected!=null&&current!=null&&!String.IsNullOrEmpty(expected.Identity)&&String.Equals(expected.Identity,current.Identity,StringComparison.OrdinalIgnoreCase)&&expected.Bounds==current.Bounds;}
}

// One short-lived viewer owns capture; pairing alone never grants screen access.
internal sealed class CompanionStream : IDisposable {
 readonly object gate=new object();readonly Func<bool> locked;readonly Func<DateTime> clock;readonly ICompanionStreamRuntime runtime;readonly System.Threading.Timer timer;
 CompanionGrant owner;string lease="";DateTime expires,audioExpires;int audioSequence;bool disposed,cleanupPending;
 internal Action<CompanionGrant> Stopped;
 internal bool IsActive {get {lock(gate)return owner!=null||cleanupPending;}}
 internal bool AudioActive {get {lock(gate)return owner!=null&&owner.Active&&owner.audio&&runtime is ICompanionAudioRuntime&&((ICompanionAudioRuntime)runtime).AudioAlive;}}
 public CompanionStream(string root,Func<bool> isLocked,Func<string> address=null):this(new CompanionStreamRuntime(root,address),isLocked,()=>DateTime.UtcNow,true){}
 internal CompanionStream(ICompanionStreamRuntime backend,Func<bool> isLocked,Func<DateTime> now,bool watch){runtime=backend;locked=isLocked;clock=now;if(watch)timer=new System.Threading.Timer(ignored=>{try{Sweep();}catch{}},null,1000,1000);}
 bool Allowed(CompanionGrant grant){try{return grant!=null&&grant.Active&&grant.screen&&!locked();}catch{return false;}}
 static string Text(Dictionary<string,object> body,string key){object value;return body.TryGetValue(key,out value)?value as string:null;}
 public CompanionReply Handle(string path,Dictionary<string,object> body,CompanionGrant grant){lock(gate){
  if(disposed||!Allowed(grant))return CompanionReply.Error(403,"screen_denied","请在电脑为此设备允许屏幕画面共享。");
  try{Sweep();}catch{return CompanionReply.Error(503,"screen_cleanup_pending","副屏尚未完成关闭，请稍后重试。");}
  try{
   if(path=="/api/screen")return CompanionReply.Json(200,runtime.Describe());
   if(path=="/api/screen/begin"){
    object value;string mode=body.ContainsKey("mode")?Text(body,"mode"):"mirror",quality=Text(body,"quality"),offer=Text(body,"offer");
    if(body.ContainsKey("instance")&&Text(body,"instance")!=runtime.Instance)return CompanionReply.Error(409,"screen_source_changed","电脑已重启，请重新选择投屏来源。");
    if(body.Count!=(body.ContainsKey("mode")?4:3)+(body.ContainsKey("instance")?1:0)+(body.ContainsKey("portrait")?1:0)||body.ContainsKey("portrait")&&!(body["portrait"] is bool)||!body.TryGetValue("monitor",out value)||!(value is int)||(mode!="mirror"&&mode!="extend")||(mode=="mirror"?((int)value<0||(int)value>CompanionMonitorSelection.MaximumId):(int)value!=-1)||(quality!="720"&&quality!="1080")||offer==null||offer.Length>49152||!offer.StartsWith("v=0\r\n",StringComparison.Ordinal))return CompanionReply.Error(400,"invalid_stream","投屏或副屏参数无效。");
    if(owner!=null)return CompanionReply.Error(409,"screen_busy","已有投屏正在使用，请先停止原来的画面。");
    owner=grant;lease=Guid.NewGuid().ToString("N");audioSequence=0;expires=clock().AddSeconds(20);
    string answer=runtime.Start(mode,(int)value,quality,offer,body.ContainsKey("portrait")&&(bool)body["portrait"]);
    if(!Allowed(grant)){Stop();return CompanionReply.Error(423,"screen_stopped","电脑锁定或授权已改变，投屏已停止。");}
    expires=clock().AddSeconds(15);return CompanionReply.Json(200,new{lease=lease,answer=answer});
   }
   if(path=="/api/screen/audio/begin"||path=="/api/screen/audio/end"){
    if(owner!=grant||lease.Length==0||Text(body,"lease")!=lease)return CompanionReply.Error(409,"screen_expired","请先开始投屏。");
    var audio=runtime as ICompanionAudioRuntime;if(audio==null)return CompanionReply.Error(503,"audio_unavailable","系统声音暂不可用。");
    object sequence;if(!body.TryGetValue("sequence",out sequence)||!(sequence is int)||(int)sequence<1)return CompanionReply.Error(400,"invalid_audio","声音参数无效。");
    if(path.EndsWith("/end",StringComparison.Ordinal)){if(body.Count!=2)return CompanionReply.Error(400,"invalid_audio","声音参数无效。");if((int)sequence>=audioSequence){audioSequence=(int)sequence;audio.StopAudio();}return CompanionReply.Json(200,new{ok=true});}
    string offer=Text(body,"offer");if(body.Count!=3||offer==null||offer.Length>49152||!offer.StartsWith("v=0\r\n",StringComparison.Ordinal))return CompanionReply.Error(400,"invalid_audio","声音参数无效。");
    if(!grant.audio)return CompanionReply.Error(403,"audio_denied","请在电脑为此设备允许系统声音。");
    if((int)sequence<=audioSequence)return CompanionReply.Error(409,"audio_expired","该声音请求已结束，请重新开启。");audioSequence=(int)sequence;
    try{string answer=audio.StartAudio(offer);if(!Allowed(grant)||!grant.audio){audio.StopAudio();return CompanionReply.Error(403,"audio_denied","声音授权已变化。");}audioExpires=clock().AddSeconds(15);return CompanionReply.Json(200,new{answer=answer});}
    catch{audio.StopAudio();return CompanionReply.Error(503,"audio_unavailable","系统声音未能开启，画面保持播放。");}
   }
   bool audioPing=path=="/api/screen/ping"&&body.Count==3&&body.ContainsKey("audioSequence")&&body["audioSequence"] is int&&(int)body["audioSequence"]>=0&&body.ContainsKey("audioEnabled")&&body["audioEnabled"] is bool;
   if((body.Count!=1&&!audioPing)||Text(body,"lease")!=lease||owner!=grant||lease.Length==0)return CompanionReply.Error(409,"screen_expired","投屏已停止，请重新开始。");
   if(path=="/api/screen/end"){Stop();return CompanionReply.Json(200,new{ok=true});}
   if(path=="/api/screen/ping"){expires=clock().AddSeconds(15);var audio=runtime as ICompanionAudioRuntime;if(audioPing&&audio!=null){int sequence=(int)body["audioSequence"];if((bool)body["audioEnabled"]){if(sequence==audioSequence)audioExpires=expires;}else if(sequence>=audioSequence){audioSequence=sequence;audio.StopAudio();}}return CompanionReply.Json(200,new{ok=true,audio=audio!=null&&audio.AudioAlive});}
   return CompanionReply.Error(404,"not_found","投屏接口不存在。");
  }catch(ArgumentException){if(owner==grant){try{Stop();}catch{}}return CompanionReply.Error(400,"invalid_stream","显示器或投屏设置无效，请重新选择。");}
   catch{if(owner==grant){try{Stop();}catch{}}return CompanionReply.Error(503,"screen_unavailable","画面未能完成开启或关闭，请检查电脑的投屏配置和副屏状态后重试。");}
 }}
 internal CompanionReply WithCapture(CompanionGrant grant,string id,Func<Rectangle,CompanionReply> action){lock(gate){
  try{Sweep();if(disposed||!Allowed(grant)||owner!=grant||String.IsNullOrEmpty(id)||id!=lease)return CompanionReply.Error(409,"screen_expired","画面已变化，请重新投屏后操作。");
   var bounds=runtime.CaptureBounds;if(bounds.Width<1||bounds.Height<1)return CompanionReply.Error(409,"screen_changed","屏幕位置已变化，请重新投屏。");return action(bounds);
  }catch{return CompanionReply.Error(503,"screen_unavailable","无法确认当前画面，未执行点选。");}
 }}
 internal void Sweep(){lock(gate){var audio=runtime as ICompanionAudioRuntime;if(owner!=null&&audio!=null&&(!owner.audio||clock()>=audioExpires||!audio.AudioAlive))audio.StopAudio();if(cleanupPending||owner!=null&&(!Allowed(owner)||clock()>=expires||!runtime.Alive))Stop();}}
 public void Stop(){lock(gate){var previous=owner;owner=null;lease="";cleanupPending=true;try{runtime.Stop();cleanupPending=false;}finally{if(previous!=null&&Stopped!=null)Stopped(previous);}}}
 public void Dispose(){lock(gate){disposed=true;if(timer!=null)timer.Dispose();try{Stop();}catch{}try{runtime.Dispose();}catch{}}}
}

internal sealed class CompanionStreamRuntime : ICompanionStreamRuntime,ICompanionAudioRuntime {
 sealed class Config {public bool enabled;public string address,ffmpeg,mediamtx;}
 readonly string instance=Guid.NewGuid().ToString("N");readonly string directory;readonly Func<string> connectionAddress;readonly CompanionVirtualDisplay displays;readonly CompanionMonitorSelection selections=new CompanionMonitorSelection();Process router,encoder,audioCapture,audioEncoder;Task audioPump;IntPtr job;
 CompanionPhysicalMonitor captureSource;Rectangle captureBounds;string captureMode;DateTime nextDisplayCheck;bool captureValid;
 internal Action<string> Diagnostic {get;set;}
 [StructLayout(LayoutKind.Sequential)]struct BASIC_LIMIT{public long processTime,jobTime;public uint flags;public UIntPtr minimum,maximum;public uint active;public UIntPtr affinity;public uint priority,scheduling;}
 [StructLayout(LayoutKind.Sequential)]struct IO_COUNTERS{public ulong readOps,writeOps,otherOps,readBytes,writeBytes,otherBytes;}
 [StructLayout(LayoutKind.Sequential)]struct EXTENDED_LIMIT{public BASIC_LIMIT basic;public IO_COUNTERS io;public UIntPtr processMemory,jobMemory,peakProcess,peakJob;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateJobObject(IntPtr attributes,string name);
 [DllImport("kernel32.dll")]static extern bool SetInformationJobObject(IntPtr job,int type,ref EXTENDED_LIMIT value,uint size);
 [DllImport("kernel32.dll")]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 public CompanionStreamRuntime(string root,Func<string> address=null){directory=Path.Combine(root,"runtime-data","companion");connectionAddress=address;displays=new CompanionVirtualDisplay(root);}
 Config Read(){
  string file=Path.Combine(directory,"screen-stream.json");if(!File.Exists(file)||new FileInfo(file).Length>8192)throw new IOException();
  var config=new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(file));IPAddress address;
   if(config==null||!config.enabled)throw new IOException();
   // Use the running HTTPS connection, never an old saved IP or an arbitrary adapter.
   if(connectionAddress!=null)config.address=connectionAddress();
   if(!IPAddress.TryParse(config.address,out address)||address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)throw new IOException();
  if(!IPAddress.IsLoopback(address))CompanionServer.Endpoint(config.address,31890);
   bool local=false;foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())if(adapter.OperationalStatus==OperationalStatus.Up)foreach(var ip in adapter.GetIPProperties().UnicastAddresses)if(ip.Address.Equals(address))local=true;
   if(!local)throw new IOException("投屏地址已不属于当前电脑，请重新检查伴随屏连接。");
  foreach(string path in new[]{config.ffmpeg,config.mediamtx})if(String.IsNullOrEmpty(path)||!Path.IsPathRooted(path)||Path.GetExtension(path).ToLowerInvariant()!=".exe"||!File.Exists(path))throw new IOException();
  config.address=address.ToString();return config;
 }
 public string Instance {get{return instance;}}
 public Rectangle CaptureBounds {get{if(!Alive)return Rectangle.Empty;bool same=captureMode=="extend"?displays.ExtensionMatches(captureBounds):displays.PhysicalMonitors().Exists(screen=>CompanionMonitorSelection.SameCapture(captureSource,screen));return same?captureBounds:Rectangle.Empty;}}
 public object Describe(){
  bool ready=true;try{Read();}catch{ready=false;}
  var monitors=new List<object>();foreach(var screen in selections.Publish(displays.PhysicalMonitors()))monitors.Add(new{id=screen.Id,name=(screen.Primary?"主屏幕":"有线屏幕 · "+screen.Name)+" · "+screen.Bounds.Width+" × "+screen.Bounds.Height});
  bool extensionAvailable=displays.Available;
  return new{instance=instance,available=ready,monitors=monitors,extensionAvailable=extensionAvailable,extensionMessage=extensionAvailable?"打开时启用 iPad 副屏，停止或断线后关闭。":displays.UnavailableReason,message=ready?"局域网视频 · 30 帧目标 · 声音需单独开启":"请先在电脑配置局域网投屏。"};
 }
 public bool Alive {get{
  if(router==null||encoder==null||router.HasExited||encoder.HasExited)return false;
  if(DateTime.UtcNow>=nextDisplayCheck){nextDisplayCheck=DateTime.UtcNow.AddSeconds(1);try{captureValid=captureMode=="extend"?displays.ExtensionMatches(captureBounds):displays.PhysicalMonitors().Exists(screen=>CompanionMonitorSelection.SameCapture(captureSource,screen));}catch{captureValid=false;}}
  return captureValid;
 }}
 Process Launch(string file,string arguments,bool pipeInput=false,bool pipeOutput=false){
  var process=new Process{StartInfo=new ProcessStartInfo{FileName=file,Arguments=arguments,WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,RedirectStandardInput=pipeInput}};
  process.ErrorDataReceived+=(s,e)=>{if(Diagnostic!=null&&e.Data!=null)Diagnostic(Path.GetFileName(file)+": "+e.Data);};process.OutputDataReceived+=(s,e)=>{if(Diagnostic!=null&&!pipeOutput&&e.Data!=null)Diagnostic(Path.GetFileName(file)+": "+e.Data);};
  try{process.Start();if(!AssignProcessToJobObject(job,process.Handle))throw new IOException();process.BeginErrorReadLine();if(!pipeOutput)process.BeginOutputReadLine();return process;}catch{try{if(!process.HasExited)process.Kill();}catch{}process.Dispose();throw;}
 }
 static string Quote(string path){if(path.IndexOfAny(new[]{'"','\r','\n','\0'})>=0)throw new ArgumentException();return "\""+path+"\"";}
 internal static string RouterConfig(string address){
  return "logLevel: error\nrtsp: true\nrtspAddress: 127.0.0.1:31854\nrtspTransports: [tcp]\nrtmp: false\nhls: false\nsrt: false\nmoq: false\napi: false\nmetrics: false\npprof: false\nplayback: false\nwebrtc: true\nwebrtcAddress: 127.0.0.1:31889\nwebrtcLocalUDPAddress: "+address+":31890\nwebrtcLocalTCPAddress: ''\nwebrtcIPsFromInterfaces: false\nwebrtcAdditionalHosts: ['"+address+"']\nwebrtcICEServers2: []\nauthInternalUsers:\n- user: any\n  ips: ['127.0.0.1']\n  permissions:\n  - action: publish\n    path: desktop\n  - action: read\n    path: desktop\n  - action: publish\n    path: audio\n  - action: read\n    path: audio\npaths:\n  desktop:\n    source: publisher\n    maxReaders: 1\n    record: false\n  audio:\n    source: publisher\n    maxReaders: 1\n    record: false\n";
 }
 public string Start(string mode,int monitor,string quality,string offer,bool portrait=false){
  Stop();try{
   var config=Read();Rectangle bounds;
   if(mode=="extend"&&monitor==-1)bounds=displays.BeginExtension(portrait);
   else if(mode=="mirror"){captureSource=selections.Resolve(monitor,displays.PhysicalMonitors());bounds=captureSource.Bounds;}
   else throw new ArgumentException();
   if(bounds.Width<1||bounds.Height<1||(long)bounds.Width*bounds.Height>33554432)throw new ArgumentException();
   captureMode=mode;captureBounds=bounds;captureValid=true;nextDisplayCheck=DateTime.MinValue;
   job=CreateJobObject(IntPtr.Zero,null);var limit=new EXTENDED_LIMIT();limit.basic.flags=0x2000;if(job==IntPtr.Zero||!SetInformationJobObject(job,9,ref limit,(uint)Marshal.SizeOf(typeof(EXTENDED_LIMIT))))throw new IOException();
   string file=Path.Combine(directory,"screen-session.yml");File.WriteAllText(file,RouterConfig(config.address),new UTF8Encoding(false));router=Launch(config.mediamtx,Quote(file));
   Thread.Sleep(250);if(router.HasExited)throw new IOException();
   string bitrate=quality=="1080"?"10M":"6M";
   string arguments=String.Format(CultureInfo.InvariantCulture,"-hide_banner -loglevel error -nostdin -thread_queue_size 64 -f gdigrab -framerate 30 -draw_mouse 1 -offset_x {0} -offset_y {1} -video_size {2}x{3} -i desktop -vf scale=-2:{4}:flags=fast_bilinear,format=yuv420p -an -c:v h264_nvenc -profile:v baseline -bf 0 -preset p4 -tune ull -rc cbr -b:v {5} -maxrate {5} -bufsize 1M -g 30 -f rtsp -rtsp_transport tcp rtsp://127.0.0.1:31854/desktop",bounds.Left,bounds.Top,bounds.Width,bounds.Height,quality,bitrate);
   encoder=Launch(config.ffmpeg,arguments);var deadline=DateTime.UtcNow.AddSeconds(10);
   while(DateTime.UtcNow<deadline&&Alive){
    try{
     var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:31889/desktop/whep");request.Proxy=null;request.AllowAutoRedirect=false;request.Method="POST";request.ContentType="application/sdp";request.Timeout=2000;request.ReadWriteTimeout=2000;
     byte[] bytes=Encoding.UTF8.GetBytes(offer);request.ContentLength=bytes.Length;using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
     using(var response=(HttpWebResponse)request.GetResponse())using(var stream=response.GetResponseStream())using(var output=new MemoryStream()){
      if((int)response.StatusCode!=201)throw new IOException();var buffer=new byte[4096];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){output.Write(buffer,0,count);if(output.Length>49152)throw new IOException();}
      string answer=Encoding.UTF8.GetString(output.ToArray());if(!answer.StartsWith("v=0\r\n",StringComparison.Ordinal))throw new IOException();return answer;
     }
    }catch(WebException error){if(Diagnostic!=null)Diagnostic("Video signaling: "+error.Status+" "+error.Message);if(error.Response!=null)error.Response.Dispose();Thread.Sleep(150);}
   }
   throw new IOException();
  }catch{Stop();throw;}
 }
 public bool AudioAlive {get{return audioCapture!=null&&audioEncoder!=null&&!audioCapture.HasExited&&!audioEncoder.HasExited&&audioPump!=null&&!audioPump.IsCompleted;}}
 public string StartAudio(string offer){
  StopAudio();if(!Alive)throw new InvalidOperationException("No active video");try{
   var config=Read();string helper=Path.GetFullPath(Path.Combine(directory,"..","..","PrismAudio.exe"));if(!File.Exists(helper))throw new IOException();
   audioEncoder=Launch(config.ffmpeg,"-hide_banner -loglevel error -nostdin -fflags +nobuffer -probesize 32 -analyzeduration 0 -f wav -max_size 3840 -ignore_length 1 -blocksize 3840 -i pipe:0 -c:a libopus -b:a 128k -application lowdelay -frame_duration 20 -flush_packets 1 -muxdelay 0 -f rtsp -rtsp_transport tcp -tcp_nodelay 1 rtsp://127.0.0.1:31854/audio",true);
   audioCapture=Launch(helper,"--loopback",false,true);
   var input=audioCapture.StandardOutput.BaseStream;var output=audioEncoder.StandardInput.BaseStream;
   audioPump=Task.Run(async()=>{try{
    // WAV permits 10 ms input packets; raw PCM batches about 85 ms and inflates the receiver jitter buffer.
    using(var header=new BinaryWriter(output,Encoding.ASCII,true)){header.Write(Encoding.ASCII.GetBytes("RIFF"));header.Write(UInt32.MaxValue);header.Write(Encoding.ASCII.GetBytes("WAVEfmt "));header.Write(16);header.Write((short)3);header.Write((short)2);header.Write(48000);header.Write(384000);header.Write((short)8);header.Write((short)32);header.Write(Encoding.ASCII.GetBytes("data"));header.Write(UInt32.MaxValue);header.Flush();}
    await input.CopyToAsync(output,3840);
   }catch{}});
   var deadline=DateTime.UtcNow.AddSeconds(6);
   while(DateTime.UtcNow<deadline&&AudioAlive){
    try{
     var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:31889/audio/whep");request.Proxy=null;request.AllowAutoRedirect=false;request.Method="POST";request.ContentType="application/sdp";request.Timeout=1500;request.ReadWriteTimeout=1500;
     byte[] bytes=Encoding.UTF8.GetBytes(offer);request.ContentLength=bytes.Length;using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
     using(var response=(HttpWebResponse)request.GetResponse())using(var stream=response.GetResponseStream())using(var result=new MemoryStream()){
      if((int)response.StatusCode!=201)throw new IOException();var buffer=new byte[4096];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){result.Write(buffer,0,count);if(result.Length>49152)throw new IOException();}
      string answer=Encoding.UTF8.GetString(result.ToArray());if(!answer.StartsWith("v=0\r\n",StringComparison.Ordinal))throw new IOException();return answer;
     }
    }catch(WebException error){if(Diagnostic!=null)Diagnostic("Audio signaling: "+error.Status+" "+error.Message);if(error.Response!=null)error.Response.Dispose();Thread.Sleep(100);}
   }throw new IOException();
  }catch{StopAudio();throw;}
 }
 public void StopAudio(){
  foreach(var process in new[]{audioCapture,audioEncoder})if(process!=null){try{if(!process.HasExited){process.Kill();process.WaitForExit(1500);}}catch{}process.Dispose();}
  audioCapture=null;audioEncoder=null;if(audioPump!=null){try{audioPump.Wait(500);}catch{}audioPump=null;}
 }
 public void Stop(){
  StopAudio();captureMode=null;captureSource=null;captureValid=false;nextDisplayCheck=DateTime.MinValue;
  if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}
  foreach(var process in new[]{encoder,router})if(process!=null){try{if(!process.HasExited){process.Kill();process.WaitForExit(1500);}}catch{}process.Dispose();}
  encoder=null;router=null;
  try{displays.StopExtension();string failure=Path.Combine(directory,"display-recovery-error.txt");if(File.Exists(failure))File.Delete(failure);}catch(Exception error){try{Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"display-recovery-error.txt"),error.Message);}catch{}throw;}
 }
 public void Dispose(){Stop();}
}
