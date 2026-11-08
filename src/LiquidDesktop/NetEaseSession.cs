using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

// These NetEase versions do not publish SMTC. Read their playback-clock doubles,
// only for the verified module layout; never scan memory, inject or write to it.
internal sealed class NetEaseSession : IDisposable {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
 [DllImport("kernel32.dll")]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll")]static extern bool ReadProcessMemory(SafeProcessHandle process,IntPtr address,byte[] buffer,IntPtr size,out IntPtr read);
 readonly string data;readonly AutoResetEvent wake=new AutoResetEvent(false);readonly byte[] bytes=new byte[8];
 volatile bool enabled,disposed;Thread worker;SafeProcessHandle processHandle;
 IntPtr handle;long module;int positionOffset,durationOffset;string title="";MediaSnapshot track;volatile string status="";
 readonly PlaybackClock playback=new PlaybackClock();DateTime metadataAt=DateTime.MinValue;
 public NetEaseSession(string directory){data=directory;}
 public string Status {get{return status;}}
 public void Enable(bool value){
  if(enabled==value||disposed)return;
  enabled=value;if(worker==null&&value){worker=new Thread(Run){IsBackground=true,Name="PrismGlass NetEase playback"};worker.Start();}
  wake.Set();
 }
 static string Caption(IntPtr h){var text=new StringBuilder(1024);GetWindowText(h,text,text.Capacity);return text.ToString();}
 void Reset(){if(processHandle!=null)processHandle.Dispose();processHandle=null;handle=IntPtr.Zero;module=0;positionOffset=durationOffset=0;title="";track=null;playback.Reset();}
 internal static bool TryLayout(string version,int imageBytes,long fileBytes,out int position,out int duration){
  position=duration=0;
  // shortcut: Each new player build needs layout verification; unknown builds stay unread.
  if(version=="3.1.41.205529"&&imageBytes==41054208&&fileBytes==37747192){position=0x21A74D8;duration=0x2214778;return true;}
  // onPlayProgress's position was verified statically and against live advancing samples.
  // This build uses the already title/artist-matched queue duration, not an unverified RVA.
  if(version=="3.1.43.205627"&&imageBytes==41058304&&fileBytes==37749240){position=0x21A84D8;return true;}
  return false;
 }
 internal static string UnsupportedStatus(string version){return "网易云 "+version+" 的播放进度尚未适配";}
 void Locate(){
  if(handle!=IntPtr.Zero&&IsWindow(handle))return;
  Reset();status="";
  foreach(var process in Process.GetProcessesByName("cloudmusic")){
   using(process){try{
    var h=process.MainWindowHandle;if(h==IntPtr.Zero||!Caption(h).Contains(" - "))continue;
    string version=process.MainModule.FileVersionInfo.FileVersion;
    status=UnsupportedStatus(version);
    foreach(ProcessModule image in process.Modules){
     if(!String.Equals(image.ModuleName,"cloudmusic.dll",StringComparison.OrdinalIgnoreCase)||!TryLayout(version,image.ModuleMemorySize,new FileInfo(image.FileName).Length,out positionOffset,out durationOffset))continue;
     processHandle=OpenProcess(0x1010,false,process.Id);
     if(processHandle.IsInvalid){Reset();status="网易云播放进度暂时无法读取";break;}
     handle=h;module=image.BaseAddress.ToInt64();status="";return;
    }
   }catch{Reset();}}
  }
 }
 static string Text(Dictionary<string,object> value,string key){object item;return value!=null&&value.TryGetValue(key,out item)&&item!=null?Convert.ToString(item,CultureInfo.InvariantCulture):"";}
 static MediaSnapshot Metadata(string caption){
  int split=caption.LastIndexOf(" - ",StringComparison.Ordinal);if(split<=0)return null;
  var result=new MediaSnapshot{source="cloudmusic.exe",title=caption.Substring(0,split),artist=caption.Substring(split+3)};
  try{
   string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netease","CloudMusic","webdata","file","playingList");
   if(new FileInfo(file).Length>4194304)return result;
   var queue=new JavaScriptSerializer{MaxJsonLength=4194304}.DeserializeObject(File.ReadAllText(file)) as Dictionary<string,object>;object raw;
   if(queue==null||!queue.TryGetValue("list",out raw))return result;
   foreach(var entry in (System.Collections.IEnumerable)raw){
    var item=entry as Dictionary<string,object>;if(item==null||!item.TryGetValue("track",out raw))continue;
    var song=raw as Dictionary<string,object>;if(Text(song,"name")!=result.title||!song.TryGetValue("artists",out raw))continue;
    var artists=new List<string>();foreach(var artist in (System.Collections.IEnumerable)raw)artists.Add(Text(artist as Dictionary<string,object>,"name"));
    if(String.Join("/",artists)!=result.artist)continue;
    double duration;if(Double.TryParse(Text(song,"duration"),NumberStyles.Float,CultureInfo.InvariantCulture,out duration))result.duration=duration/1000;
    if(song.TryGetValue("album",out raw))result.album=Text(raw as Dictionary<string,object>,"name");break;
   }
  }catch{}
  return result;
 }
 double Clock(int offset){
  IntPtr read;if(!ReadProcessMemory(processHandle,new IntPtr(module+offset),bytes,new IntPtr(8),out read)||read.ToInt64()!=8)return Double.NaN;
  return BitConverter.ToDouble(bytes,0);
 }
 internal static bool ValidClock(double position,double duration,double expected){
  return !Double.IsNaN(position)&&!Double.IsNaN(duration)&&!Double.IsInfinity(position)&&!Double.IsInfinity(duration)&&expected>0&&Math.Abs(duration-expected)<2&&position>=0&&position<=duration;
 }
 internal static bool Advancing(double previous,double current,double elapsed){
  double delta=current-previous;return !Double.IsNaN(previous)&&elapsed>0&&elapsed<2&&delta>.001&&delta<Math.Max(1.5,elapsed*3);
 }
 internal sealed class PlaybackClock {
  double previous=Double.NaN;DateTime previousAt=DateTime.MinValue,advancedAt=DateTime.MinValue;
  public bool Ready,Advanced;
  public void Reset(){previous=Double.NaN;advancedAt=DateTime.MinValue;Ready=Advanced=false;}
  public bool Observe(double position,DateTime now){
   double elapsed=(now-previousAt).TotalSeconds;
   Ready=!Double.IsNaN(previous)&&elapsed>0&&elapsed<2;
   Advanced=Ready&&Advancing(previous,position,elapsed);
   if(!Ready)advancedAt=DateTime.MinValue;
   if(Advanced)advancedAt=now;
   previous=position;previousAt=now;
   // Native clocks may update in bursts. A single repeated reading isn't pause.
   return Ready&&(now-advancedAt).TotalSeconds<1.2;
  }
 }
 MediaSnapshot Sample(){
  Locate();if(handle==IntPtr.Zero)return null;
  string caption=Caption(handle);var now=DateTime.UtcNow;
  if(caption!=title){title=caption;track=Metadata(title);metadataAt=now;playback.Reset();}
  if(track!=null&&track.duration<=0&&(now-metadataAt).TotalSeconds>=2){track=Metadata(title);metadataAt=now;}
  if(track==null)return null;
  double position=Clock(positionOffset),duration=durationOffset==0?track.duration:Clock(durationOffset);
  if(Caption(handle)!=title||!ValidClock(position,duration,track.duration)){playback.Reset();return null;}
  bool playing=playback.Observe(position,now);
  return new MediaSnapshot{source=track.source,title=track.title,artist=track.artist,album=track.album,duration=duration,position=position,status=playing?"playing":"paused",timeline=playback.Ready,rate=playback.Advanced?1:0,sampledAt=MediaSnapshot.Unix(now)};
 }
 void Publish(MediaSnapshot value){
  string file=Path.Combine(data,"netease-session.json");
  if(value==null){if(File.Exists(file))File.Delete(file);return;}
  LyricsFiles.Write(file,new JavaScriptSerializer().Serialize(value));
 }
 void Run(){
  try{
   while(!disposed){
    try{if(enabled){var value=Sample();if(enabled&&!disposed)Publish(value);}else{Reset();Publish(null);}}
    catch(IOException){} // A busy reader must not reset the playback clock.
    catch{Reset();try{Publish(null);}catch{}}
    wake.WaitOne(enabled?400:2000);
   }
  }finally{Reset();try{Publish(null);}catch{}}
 }
 public void Dispose(){if(disposed)return;disposed=true;enabled=false;if(worker!=null){wake.Set();if(worker.Join(1500))wake.Dispose();}else wake.Dispose();}
}
