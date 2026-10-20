using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class LyricsFiles {
 public static string Read(string file){
  using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
  using(var reader=new StreamReader(stream,Encoding.UTF8,true))return reader.ReadToEnd();
 }
 public static void Write(string file,string value){
  string temp=file+".tmp";File.WriteAllText(temp,value,new UTF8Encoding(false));
  if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
 }
}
internal sealed class LyricsAppearance {
 public string font="sans",layout="duo",align="center",effect="float",texture="solid",widthMode="adaptive";
 public string color="#ffffff",secondary="#dceaf9",accent="#9edfff";
 public int fontSize=26,weight=650,spacing=2,intensity=65,speed=100,glow=30,width=900,secondaryOpacity=80;
 static string Choice(string value,string choices,string fallback){return ("|"+choices+"|").Contains("|"+value+"|")&&!String.IsNullOrEmpty(value)?value:fallback;}
 static string Color(string value,string fallback){return Regex.IsMatch(value??"",@"^#[a-fA-F0-9]{6}$")?value.ToLowerInvariant():fallback;}
 static int Bound(int value,int min,int max){return Math.Max(min,Math.Min(max,value));}
 public LyricsAppearance Normalize(){
  font=Choice(font,"sans|humanist|hei|song|kai|round|mono|display","sans");
  layout=Choice(layout,"duo|single|split|stack","duo");align=Choice(align,"left|center|right","center");
  effect=Choice(effect,"float|spring|fade|flip|slide|zoom|blur|wave|ripple|cascade|stretch|none","float");
  texture=Choice(texture,"solid|gradient|shimmer|neon|outline","solid");widthMode=Choice(widthMode,"adaptive|fixed","adaptive");
  color=Color(color,"#ffffff");secondary=Color(secondary,"#dceaf9");accent=Color(accent,"#9edfff");
  fontSize=Bound(fontSize,18,32);weight=Bound(weight,400,900);spacing=Bound(spacing,-2,12);
  intensity=Bound(intensity,0,100);speed=Bound(speed,50,180);glow=Bound(glow,0,100);width=Bound(width,420,1200);secondaryOpacity=Bound(secondaryOpacity,30,100);
  return this;
 }
}
internal sealed class LyricsSettings {
 public bool enabled,online;
 public bool holdGaps=true;
 public int offsetMs;
 public LyricsAppearance appearance=new LyricsAppearance();
 public static LyricsSettings Load(string root,LyricsSettings previous=null){
  try{var file=Path.Combine(root,"lyrics-settings.json");if(File.Exists(file)&&new FileInfo(file).Length<8192){var s=new JavaScriptSerializer().Deserialize<LyricsSettings>(LyricsFiles.Read(file));if(s!=null){s.offsetMs=Math.Max(-5000,Math.Min(5000,s.offsetMs));s.appearance=(s.appearance??new LyricsAppearance()).Normalize();return s;}}}catch{}
  return previous??new LyricsSettings{enabled=false,online=false};
 }
 public void Save(string root){appearance=(appearance??new LyricsAppearance()).Normalize();LyricsFiles.Write(Path.Combine(root,"lyrics-settings.json"),new JavaScriptSerializer().Serialize(this));}
}
internal sealed class MediaSnapshot {
 public string source="",title="",artist="",album="",status="";
 public double sampledAt=0,position=0,duration=0,rate=1;
 public bool timeline=false;
 public static double Unix(DateTime now){return (now-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;}
 public bool Fresh(DateTime now){double age=Unix(now)-sampledAt;return age>=-1&&age<4;}
 public string Key {get{return source+"\n"+title+"\n"+artist+"\n"+album+"\n"+Math.Round(duration).ToString(CultureInfo.InvariantCulture);}}
 public double Position(DateTime now){
  return Math.Max(0,Math.Min(duration,position+(status=="playing"?Math.Max(0,Unix(now)-sampledAt)*rate:0)));
 }
 public static MediaSnapshot Read(string root,DateTime now){
  var system=ReadFile(root,"session.json",now);
  if(system!=null&&system.timeline&&system.status=="playing")return system;
  var netease=ReadFile(root,"netease-session.json",now);
  return netease!=null&&(netease.status=="playing"||system==null)?netease:system;
 }
 static MediaSnapshot ReadFile(string root,string name,DateTime now){
  try{var file=Path.Combine(root,"..","music-data",name);if(!File.Exists(file)||new FileInfo(file).Length>32768)return null;
   var s=new JavaScriptSerializer().Deserialize<MediaSnapshot>(LyricsFiles.Read(file));
   return s!=null&&s.Fresh(now)&&!String.IsNullOrWhiteSpace(s.title)&&s.duration>=0&&s.rate>=0&&s.rate<=16&&s.position>=0?s:null;
  }catch{return null;}
 }
}
internal sealed class LyricLine {public double time;public string text;}
internal static class LyricsTimeline {
 static readonly Regex stamp=new Regex(@"\[(\d{1,3}):([0-5]\d)(?:[.:](\d{1,3}))?\]",RegexOptions.Compiled);
 public static List<LyricLine> Parse(string lrc){
  var lines=new SortedDictionary<double,string>();double offset=0;
  if(String.IsNullOrEmpty(lrc)||lrc.Length>512000)return new List<LyricLine>();
  var adjustment=Regex.Match(lrc,@"(?im)\[offset:([+-]?\d+)\]");
  if(adjustment.Success){double value;if(double.TryParse(adjustment.Groups[1].Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out value))offset=Math.Max(-60000,Math.Min(60000,value))/1000;}
  foreach(var raw in lrc.Split('\n')){
   var matches=stamp.Matches(raw);if(matches.Count==0)continue;
   string text=Regex.Replace(raw,@"\[[^\]]*\]|<\d+:\d+(?:\.\d+)?>","").Trim();
   text=new string(text.Where(c=>!Char.IsControl(c)).Take(600).ToArray());
   foreach(Match m in matches){
    double seconds=int.Parse(m.Groups[1].Value)*60+int.Parse(m.Groups[2].Value);
    if(m.Groups[3].Success)seconds+=int.Parse(m.Groups[3].Value)/Math.Pow(10,m.Groups[3].Length);
    seconds=Math.Max(0,seconds-offset);
    string previous;if(lines.TryGetValue(seconds,out previous)&&previous.Length>0&&text.Length>0&&previous!=text)lines[seconds]=previous+" · "+text;else if(!lines.ContainsKey(seconds)||text.Length>0)lines[seconds]=text;
   }
  }
  return lines.Select(pair=>new LyricLine{time=pair.Key,text=pair.Value}).ToList();
 }
 public static string[] At(List<LyricLine> lines,double seconds){
  int lo=0,hi=lines.Count-1,found=-1;
  while(lo<=hi){int mid=(lo+hi)/2;if(lines[mid].time<=seconds){found=mid;lo=mid+1;}else hi=mid-1;}
  if(found<0||String.IsNullOrWhiteSpace(lines[found].text))return new[]{"",""};
  return new[]{lines[found].text,found+1<lines.Count?lines[found+1].text:""};
 }
 // Rainmeter expands #variables# and [measures] in meter options. Render these literally.
 public static string DisplayText(string text){return (text??"").Replace("#","＃").Replace("[","［").Replace("]","］").Replace("\r"," ").Replace("\n"," ");}
}
internal sealed class DockLyrics : IDisposable {
 public event Action<object> VisualChanged;
 readonly string root,data;
 readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=2097152};
 readonly HttpClient http;
 readonly bool readSystemMedia;
 readonly NetEaseSession netease;
 readonly Dictionary<string,string> cache=new Dictionary<string,string>();
 sealed class LookupResult {public string lrc="",status="未找到同步歌词";}
 Process helper;CancellationTokenSource cancellation;Task<LookupResult> lookup;
 string track="",requestTrack="",lastOutput="",state="",requestState="";
 List<LyricLine> lines=new List<LyricLine>();
 DateTime sample=DateTime.MinValue,published=DateTime.MinValue,retry=DateTime.MinValue,helperRetry=DateTime.MinValue,approached=DateTime.MinValue;
 int attempts;bool disposed;MediaSnapshot media;LyricsSettings settings;
 public DockLyrics(string directory,HttpMessageHandler transport=null,bool readSystemMedia=true){
  this.readSystemMedia=readSystemMedia;
  http=transport==null?new HttpClient():new HttpClient(transport);
  http.Timeout=TimeSpan.FromSeconds(8);http.MaxResponseContentBufferSize=1048576;
  root=directory;data=Path.GetFullPath(Path.Combine(root,"..","music-data"));Directory.CreateDirectory(data);
  if(readSystemMedia)netease=new NetEaseSession(data);
  http.DefaultRequestHeaders.UserAgent.ParseAdd("PrismGlass/1.0 (+https://github.com/liburce0412-alt/prismglass-desktop)");
  try{string file=Path.Combine(data,"lyrics-cache.json");if(File.Exists(file)&&new FileInfo(file).Length<2097152){var saved=json.Deserialize<Dictionary<string,string>>(File.ReadAllText(file));foreach(var pair in saved.Take(32))cache[pair.Key]=pair.Value;}}catch{}
 }
 void StartHelper(DateTime now){
  if(!readSystemMedia)return;
  if(helper!=null&&!helper.HasExited||now<helperRetry)return;
  if(helper!=null){helper.Dispose();helper=null;}helperRetry=now.AddSeconds(30);
  // Windows PowerShell 5.1 is required for the .NET Framework WinRT projection.
  string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
  try{File.Delete(Path.Combine(data,"session.json"));helper=Process.Start(new ProcessStartInfo(shell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+Path.Combine(root,"MediaSession.ps1")+"\" -OwnerPid "+Process.GetCurrentProcess().Id+" -DataRoot \""+data+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});}
  catch{Status("媒体会话读取暂不可用");}
 }
 void StopHelper(){if(helper==null)return;try{if(!helper.HasExited)helper.Kill();}catch{}helper.Dispose();helper=null;}
 void CancelLookup(){if(cancellation!=null){cancellation.Cancel();cancellation.Dispose();cancellation=null;}if(lookup!=null)lookup.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);lookup=null;}
 void Status(string value){if(value==state)return;state=value;try{File.WriteAllText(Path.Combine(root,"lyrics-status.txt"),value);}catch(IOException){}}
 public void Tick(DateTime now,bool dockVisible,bool pointerNear,bool motionPaused){
  if(disposed)return;
  if(pointerNear)approached=now;
  if((now-sample).TotalMilliseconds>=250){
   sample=now;var next=LyricsSettings.Load(root,settings);
   if(settings!=null&&settings.online!=next.online){CancelLookup();attempts=0;retry=DateTime.MinValue;}
   settings=next;
   if(netease!=null)netease.Enable(settings.enabled);
   if(!settings.enabled){StopHelper();CancelLookup();media=null;track="";lines.Clear();Status("Dock 歌词已关闭");}
   else {
    StartHelper(now);var observed=MediaSnapshot.Read(root,now);
    // A file replacement or missed sample is not a track change. Keep the last
    // valid sample only until its original heartbeat expires; never extend it.
    media=observed??(media!=null&&media.Fresh(now)?media:null);
    string key=media==null?"":media.Key;
    if(key!=track){CancelLookup();track=key;lines.Clear();attempts=0;retry=DateTime.MinValue;string saved;if(cache.TryGetValue(key,out saved))lines=LyricsTimeline.Parse(saved);}
    if(lookup!=null&&lookup.IsCompleted){
     if(lookup.Status==TaskStatus.RanToCompletion&&requestTrack==track){
      string lrc=lookup.Result.lrc;requestState=lookup.Result.status;lines=LyricsTimeline.Parse(lrc);
      if(lines.Count>0){
       cache[track]=lrc;
       while(cache.Count>32||cache.Sum(p=>p.Key.Length+p.Value.Length)>250000)cache.Remove(cache.Keys.First());
       try{File.WriteAllText(Path.Combine(data,"lyrics-cache.json"),json.Serialize(cache));}catch(IOException){}
      }
     }else if(lookup.IsFaulted){var ignored=lookup.Exception;}
     lookup=null;if(cancellation!=null){cancellation.Dispose();cancellation=null;}retry=now.AddSeconds(attempts<2?30:120);
    }
    if(media==null)Status("等待播放器提供歌曲信息");
    else if(!media.timeline)Status("播放器未提供可用进度");
    else if(media.status!="playing")Status("暂停时显示应用图标");
    else if(lines.Count>0)Status("歌词显示中 · 鼠标靠近恢复应用");
    else if(!settings.online)Status("开启在线匹配，或使用已缓存歌词");
    else if(lookup==null&&now>=retry&&attempts<3){
     attempts++;requestTrack=track;requestState="未找到同步歌词";cancellation=new CancellationTokenSource();
     var request=media;lookup=Fetch(request,cancellation.Token);Status("正在匹配歌词…");
    }else Status(lookup!=null?"正在匹配歌词…":requestState);
   }
  }
  string current="",nextLine="";bool gap=false;
  if(settings!=null&&settings.enabled&&dockVisible&&!pointerNear&&(now-approached).TotalMilliseconds>=650&&media!=null&&media.Fresh(now)&&media.timeline&&media.status=="playing"&&media.Position(now)<media.duration){
   var pair=LyricsTimeline.At(lines,media.Position(now)+settings.offsetMs/1000.0);
   current=pair[0];nextLine=pair[1];
   if(current.Length==0&&lines.Count>0&&settings.holdGaps){gap=true;current=media.title;nextLine=(media.Position(now)<lines[0].time?"前奏":"间奏")+(String.IsNullOrWhiteSpace(media.artist)?"":" · "+media.artist);}
  }
  Publish(now,current,nextLine,motionPaused,gap);
 }
 void Publish(DateTime now,string current,string next,bool motionPaused,bool gap=false){
  string value=(current.Length>0?"1":"0")+"|"+(motionPaused?"1":"0")+"\n"+Convert.ToBase64String(Encoding.UTF8.GetBytes(LyricsTimeline.DisplayText(current)))+"\n"+Convert.ToBase64String(Encoding.UTF8.GetBytes(LyricsTimeline.DisplayText(next)))+"\n";
  var appearance=settings==null?new LyricsAppearance():settings.appearance;
  string signature=value+json.Serialize(appearance)+gap;
  if(signature==lastOutput&&(now-published).TotalMilliseconds<750)return;
  // Send Unicode directly to the glass WebView; Rainmeter only controls icon visibility.
  var changed=VisualChanged;if(changed!=null)changed(new {type="lyrics",show=current.Length>0,current=current,next=next,motionPaused=motionPaused,gap=gap,appearance=appearance});
  try{LyricsFiles.Write(Path.Combine(root,"dock-lyrics.txt"),Math.Floor(MediaSnapshot.Unix(now)+3).ToString(CultureInfo.InvariantCulture)+"|"+value);lastOutput=signature;published=now;}catch(IOException){}
 }
 async Task<string> Get(string url,CancellationToken token){
  using(var response=await http.GetAsync(url,token).ConfigureAwait(false)){if(response.StatusCode==System.Net.HttpStatusCode.NotFound)return "";response.EnsureSuccessStatusCode();return await response.Content.ReadAsStringAsync().ConfigureAwait(false);}
 }
 string NetEaseId(MediaSnapshot song){
  if((song.source??"").IndexOf("cloudmusic",StringComparison.OrdinalIgnoreCase)<0)return "";
  try{
   string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Netease","CloudMusic","webdata","file","playingList");
   if(!File.Exists(path)||new FileInfo(path).Length>4194304)return "";
   var queue=new JavaScriptSerializer{MaxJsonLength=4194304}.DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;object raw;
   if(queue==null||!queue.TryGetValue("list",out raw))return "";
   foreach(var entry in (System.Collections.IEnumerable)raw){
    var item=entry as Dictionary<string,object>;if(item==null||!item.TryGetValue("track",out raw))continue;
    var t=raw as Dictionary<string,object>;if(t==null||Text(t,"name")!=song.title||!t.TryGetValue("artists",out raw))continue;
    var artists=new List<string>();foreach(var a in (System.Collections.IEnumerable)raw){var artist=a as Dictionary<string,object>;if(artist!=null)artists.Add(Text(artist,"name"));}
    if(Normalize(String.Join("/",artists))!=Normalize(song.artist))continue;
    long id;if(long.TryParse(Text(t,"id"),out id)&&id>0)return id.ToString(CultureInfo.InvariantCulture);
   }
  }catch{}return "";
 }
 static string Normalize(string s){return Regex.Replace((s??"").ToLowerInvariant(),@"[\s/、,&;·]+","");}
 static string Text(Dictionary<string,object> d,string key){object v;return d!=null&&d.TryGetValue(key,out v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):"";}
 async Task<LookupResult> Fetch(MediaSnapshot song,CancellationToken token){
  try{
   // Exact local queue match avoids guessing a song ID or accessing account credentials.
   string id=await Task.Run(()=>NetEaseId(song),token).ConfigureAwait(false);
   if(id.Length>0){
    try{string body=await Get("https://music.163.com/api/song/lyric?id="+id+"&lv=1&kv=0&tv=0",token).ConfigureAwait(false);
     var result=new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string,object>;object raw;
     if(result!=null&&result.TryGetValue("lrc",out raw)){string lrc=Text(raw as Dictionary<string,object>,"lyric");if(LyricsTimeline.Parse(lrc).Count>0)return new LookupResult{lrc=lrc};}
    }catch(OperationCanceledException){if(token.IsCancellationRequested)throw;}catch{}
   }
   string url="https://lrclib.net/api/get?track_name="+Uri.EscapeDataString(song.title)+"&artist_name="+Uri.EscapeDataString(song.artist)+"&album_name="+Uri.EscapeDataString(song.album??"")+"&duration="+Math.Round(song.duration).ToString(CultureInfo.InvariantCulture);
   string payload=await Get(url,token).ConfigureAwait(false);
   if(String.IsNullOrEmpty(payload))return new LookupResult();
   var record=new JavaScriptSerializer().DeserializeObject(payload) as Dictionary<string,object>;
   return new LookupResult{lrc=Text(record,"syncedLyrics")};
  }catch(OperationCanceledException){return new LookupResult{status="歌词服务超时，稍后重试"};}
  catch{return new LookupResult{status="歌词服务暂不可用，稍后重试"};}
 }
 public void Dispose(){disposed=true;CancelLookup();StopHelper();if(netease!=null)netease.Dispose();Publish(DateTime.UtcNow,"","",true);http.Dispose();}
}
