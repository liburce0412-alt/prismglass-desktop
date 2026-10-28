using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal interface ICompanionStreamRuntime : IDisposable {
 object Describe();
 string Start(int monitor,string quality,string offer);
 bool Alive {get;}
 void Stop();
}

// One short-lived viewer owns capture; pairing alone never grants screen access.
internal sealed class CompanionStream : IDisposable {
 readonly object gate=new object();readonly Func<bool> locked;readonly Func<DateTime> clock;readonly ICompanionStreamRuntime runtime;readonly System.Threading.Timer timer;
 CompanionGrant owner;string lease="";DateTime expires;bool disposed;
 public CompanionStream(string root,Func<bool> isLocked):this(new CompanionStreamRuntime(root),isLocked,()=>DateTime.UtcNow,true){}
 internal CompanionStream(ICompanionStreamRuntime backend,Func<bool> isLocked,Func<DateTime> now,bool watch){runtime=backend;locked=isLocked;clock=now;if(watch)timer=new System.Threading.Timer(ignored=>Sweep(),null,1000,1000);}
 bool Allowed(CompanionGrant grant){try{return grant!=null&&grant.Active&&grant.screen&&!locked();}catch{return false;}}
 static string Text(Dictionary<string,object> body,string key){object value;return body.TryGetValue(key,out value)?value as string:null;}
 public CompanionReply Handle(string path,Dictionary<string,object> body,CompanionGrant grant){lock(gate){
  Sweep();if(disposed||!Allowed(grant))return CompanionReply.Error(403,"screen_denied","请在电脑为此设备允许屏幕画面共享。");
  try{
   if(path=="/api/screen")return CompanionReply.Json(200,runtime.Describe());
   if(path=="/api/screen/begin"){
    object value;string quality=Text(body,"quality"),offer=Text(body,"offer");
    if(body.Count!=3||!body.TryGetValue("monitor",out value)||!(value is int)||(int)value<0||(int)value>15||(quality!="720"&&quality!="1080")||offer==null||offer.Length>49152||!offer.StartsWith("v=0\r\n",StringComparison.Ordinal))return CompanionReply.Error(400,"invalid_stream","投屏参数无效。");
    if(owner!=null)return CompanionReply.Error(409,"screen_busy","已有投屏正在使用，请先停止原来的画面。");
    owner=grant;lease=Guid.NewGuid().ToString("N");expires=clock().AddSeconds(20);
    string answer=runtime.Start((int)value,quality,offer);
    if(!Allowed(grant)){Stop();return CompanionReply.Error(423,"screen_stopped","电脑锁定或授权已改变，投屏已停止。");}
    expires=clock().AddSeconds(15);return CompanionReply.Json(200,new{lease=lease,answer=answer});
   }
   if(body.Count!=1||Text(body,"lease")!=lease||owner!=grant||lease.Length==0)return CompanionReply.Error(409,"screen_expired","投屏已停止，请重新开始。");
   if(path=="/api/screen/end"){Stop();return CompanionReply.Json(200,new{ok=true});}
   if(path=="/api/screen/ping"){expires=clock().AddSeconds(15);return CompanionReply.Json(200,new{ok=true});}
   return CompanionReply.Error(404,"not_found","投屏接口不存在。");
  }catch(ArgumentException){if(owner==grant)Stop();return CompanionReply.Error(400,"invalid_stream","显示器或投屏设置无效，请重新选择。");}
   catch{if(owner==grant)Stop();return CompanionReply.Error(503,"screen_unavailable","投屏未能启动，请检查电脑的投屏配置、编码器和端口。");}
 }}
 internal void Sweep(){lock(gate){if(owner!=null&&(!Allowed(owner)||clock()>=expires||!runtime.Alive))Stop();}}
 public void Stop(){lock(gate){owner=null;lease="";runtime.Stop();}}
 public void Dispose(){lock(gate){disposed=true;if(timer!=null)timer.Dispose();Stop();runtime.Dispose();}}
}

internal sealed class CompanionStreamRuntime : ICompanionStreamRuntime {
 sealed class Config {public bool enabled;public string address,ffmpeg,mediamtx;}
 readonly string directory;Process router,encoder;IntPtr job;
 [StructLayout(LayoutKind.Sequential)]struct BASIC_LIMIT{public long processTime,jobTime;public uint flags;public UIntPtr minimum,maximum;public uint active;public UIntPtr affinity;public uint priority,scheduling;}
 [StructLayout(LayoutKind.Sequential)]struct IO_COUNTERS{public ulong readOps,writeOps,otherOps,readBytes,writeBytes,otherBytes;}
 [StructLayout(LayoutKind.Sequential)]struct EXTENDED_LIMIT{public BASIC_LIMIT basic;public IO_COUNTERS io;public UIntPtr processMemory,jobMemory,peakProcess,peakJob;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateJobObject(IntPtr attributes,string name);
 [DllImport("kernel32.dll")]static extern bool SetInformationJobObject(IntPtr job,int type,ref EXTENDED_LIMIT value,uint size);
 [DllImport("kernel32.dll")]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 public CompanionStreamRuntime(string root){directory=Path.Combine(root,"runtime-data","companion");}
 Config Read(){
  string file=Path.Combine(directory,"screen-stream.json");if(!File.Exists(file)||new FileInfo(file).Length>8192)throw new IOException();
  var config=new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(file));IPAddress address;
  if(config==null||!config.enabled||!IPAddress.TryParse(config.address,out address)||address.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork)throw new IOException();
  if(!IPAddress.IsLoopback(address))CompanionServer.Endpoint(config.address,31890);
  foreach(string path in new[]{config.ffmpeg,config.mediamtx})if(String.IsNullOrEmpty(path)||!Path.IsPathRooted(path)||Path.GetExtension(path).ToLowerInvariant()!=".exe"||!File.Exists(path))throw new IOException();
  config.address=address.ToString();return config;
 }
 public object Describe(){
  bool ready=true;try{Read();}catch{ready=false;}
  var monitors=new List<object>();var screens=Screen.AllScreens;for(int i=0;i<screens.Length&&i<16;i++){var screen=screens[i];monitors.Add(new{id=i,name=(screen.Primary?"主屏幕":"显示器 "+(i+1))+" · "+screen.Bounds.Width+" × "+screen.Bounds.Height});}
  return new{available=ready,monitors=monitors,message=ready?"局域网视频 · 30 帧目标 · 无音频":"请先在电脑配置局域网投屏。"};
 }
 public bool Alive {get{return router!=null&&encoder!=null&&!router.HasExited&&!encoder.HasExited;}}
 Process Launch(string file,string arguments){
  var process=new Process{StartInfo=new ProcessStartInfo{FileName=file,Arguments=arguments,WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true}};
  process.ErrorDataReceived+=(s,e)=>{};process.OutputDataReceived+=(s,e)=>{};
  try{process.Start();if(!AssignProcessToJobObject(job,process.Handle))throw new IOException();process.BeginErrorReadLine();process.BeginOutputReadLine();return process;}catch{try{if(!process.HasExited)process.Kill();}catch{}process.Dispose();throw;}
 }
 static string Quote(string path){if(path.IndexOfAny(new[]{'"','\r','\n','\0'})>=0)throw new ArgumentException();return "\""+path+"\"";}
 internal static string RouterConfig(string address){
  return "logLevel: error\nrtsp: true\nrtspAddress: 127.0.0.1:31854\nrtspTransports: [tcp]\nrtmp: false\nhls: false\nsrt: false\nmoq: false\napi: false\nmetrics: false\npprof: false\nplayback: false\nwebrtc: true\nwebrtcAddress: 127.0.0.1:31889\nwebrtcLocalUDPAddress: "+address+":31890\nwebrtcLocalTCPAddress: ''\nwebrtcIPsFromInterfaces: false\nwebrtcAdditionalHosts: ['"+address+"']\nwebrtcICEServers2: []\nauthInternalUsers:\n- user: any\n  ips: ['127.0.0.1']\n  permissions:\n  - action: publish\n    path: desktop\n  - action: read\n    path: desktop\npaths:\n  desktop:\n    source: publisher\n    maxReaders: 1\n    record: false\n";
 }
 public string Start(int monitor,string quality,string offer){
  Stop();try{
   var config=Read();var screens=Screen.AllScreens;if(monitor<0||monitor>=screens.Length)throw new ArgumentException();Rectangle bounds=screens[monitor].Bounds;
   if(bounds.Width<1||bounds.Height<1||(long)bounds.Width*bounds.Height>33554432)throw new ArgumentException();
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
    }catch(WebException error){if(error.Response!=null)error.Response.Dispose();Thread.Sleep(150);}
   }
   throw new IOException();
  }catch{Stop();throw;}
 }
 public void Stop(){
  if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}
  foreach(var process in new[]{encoder,router})if(process!=null){try{if(!process.HasExited){process.Kill();process.WaitForExit(1500);}}catch{}process.Dispose();}
  encoder=null;router=null;
 }
 public void Dispose(){Stop();}
}
