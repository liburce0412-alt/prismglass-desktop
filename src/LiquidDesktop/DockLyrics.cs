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
 public static void Locked(Action action){using(var mutex=new Mutex(false,"Local\\PrismGlassLyricsSettings")){bool held=false;try{try{held=mutex.WaitOne(3000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("歌词设置正被另一端保存，请稍后重试。");action();}finally{if(held)mutex.ReleaseMutex();}}}
 public static string Read(string file){
  using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
  using(var reader=new StreamReader(stream,Encoding.UTF8,true))return reader.ReadToEnd();
 }
 public static void Write(string file,string value){
  string temp=file+".pending-"+Guid.NewGuid().ToString("N");try{File.WriteAllText(temp,value,new UTF8Encoding(false));
  if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
internal sealed class LyricsAppearance {
 public string font="sans",layout="duo",align="center",effect="float",texture="solid",widthMode="adaptive";
 public string color="#ffffff",secondary="#dceaf9",accent="#9edfff";
 public string backgroundTheme="transparent";public int backgroundOpacity=35;public bool backgroundMotion=false;
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
  backgroundTheme=Choice(backgroundTheme,"album|wallpaper|transparent|ocean|pine|sand|violet","transparent");backgroundOpacity=Bound(backgroundOpacity,0,100);
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
 internal string loadedSource;internal bool loaded;
 internal static LyricsSettings ReadStrict(string root,out string source,out Dictionary<string,object> document){
  string file=Path.Combine(root,"lyrics-settings.json");source=null;document=new Dictionary<string,object>();
  if(!File.Exists(file))return new LyricsSettings{loaded=true};
  if(new FileInfo(file).Length>=8192)throw new IOException("歌词设置文件过大，原文件已保留。");
  source=LyricsFiles.Read(file);var json=new JavaScriptSerializer();
  try{document=json.DeserializeObject(source) as Dictionary<string,object>;if(document==null)throw new ArgumentException();
   foreach(string key in new[]{"enabled","online","holdGaps"})if(document.ContainsKey(key)&&!(document[key] is bool))throw new ArgumentException();
   if(document.ContainsKey("offsetMs")&&!(document["offsetMs"] is int))throw new ArgumentException();
   object raw;if(document.TryGetValue("appearance",out raw)){var map=raw as Dictionary<string,object>;if(map==null)throw new ArgumentException();ValidateAppearance(map,false);}
   var value=json.Deserialize<LyricsSettings>(source);value.offsetMs=Math.Max(-5000,Math.Min(5000,value.offsetMs));value.appearance=(value.appearance??new LyricsAppearance()).Normalize();value.loaded=true;value.loadedSource=source;return value;
  }catch(ArgumentException){throw new IOException("歌词设置暂时无法读取，原文件已保留。");}catch(InvalidOperationException){throw new IOException("歌词设置暂时无法读取，原文件已保留。");}
 }
 internal static void ValidateAppearance(Dictionary<string,object> patch,bool rejectUnknown){
  foreach(var pair in patch){var field=typeof(LyricsAppearance).GetField(pair.Key);if(field==null){if(rejectUnknown)throw new ArgumentException();continue;}if(pair.Value==null||pair.Value.GetType()!=field.FieldType)throw new ArgumentException();}
 }
 public static LyricsSettings Load(string root,LyricsSettings previous=null){
  try{string source;Dictionary<string,object> document;return ReadStrict(root,out source,out document);}catch{}
  return previous??new LyricsSettings{enabled=false,online=false};
 }
 public void Save(string root){LyricsFiles.Locked(()=>{string source;Dictionary<string,object> document;ReadStrict(root,out source,out document);if(source!=loadedSource||(!loaded&&source!=null))throw new LyricsSettingsConflictException();
  appearance=(appearance??new LyricsAppearance()).Normalize();document["enabled"]=enabled;document["online"]=online;document["holdGaps"]=holdGaps;document["offsetMs"]=Math.Max(-5000,Math.Min(5000,offsetMs));document["appearance"]=appearance;
  string next=new JavaScriptSerializer().Serialize(document);LyricsFiles.Write(Path.Combine(root,"lyrics-settings.json"),next);loadedSource=next;loaded=true;});}
}
internal sealed class LyricsSettingsConflictException:IOException {public LyricsSettingsConflictException():base("歌词设置已在另一端更新，请重新载入后重试。"){} }
internal sealed class LyricsStyleStore {
 readonly string root;readonly JavaScriptSerializer json=new JavaScriptSerializer();string source,revision="";
 public LyricsStyleStore(string directory){root=directory;}
 void Observe(string value){if(revision==""||source!=value){source=value;revision=Guid.NewGuid().ToString("N");}}
 object Snapshot(LyricsSettings value,bool editable){return new{available=true,editable=editable,reason="",revision=revision,appearance=value.appearance,enabled=value.enabled,online=value.online,holdGaps=value.holdGaps,offsetMs=value.offsetMs};}
 public object Read(bool editable){object result=null;LyricsFiles.Locked(()=>{string actual;Dictionary<string,object> document;var value=LyricsSettings.ReadStrict(root,out actual,out document);Observe(actual);result=Snapshot(value,editable);});return result;}
 public object Write(string expected,Dictionary<string,object> patch,Func<bool> authorize){
  if(patch==null||patch.Count==0||patch.Count>22)throw new ArgumentException();LyricsSettings.ValidateAppearance(patch,true);object result=null;
  LyricsFiles.Locked(()=>{if(!authorize())throw new UnauthorizedAccessException();string actual;Dictionary<string,object> document;var value=LyricsSettings.ReadStrict(root,out actual,out document);Observe(actual);if(expected!=revision)throw new LyricsSettingsConflictException();
   foreach(var pair in patch)typeof(LyricsAppearance).GetField(pair.Key).SetValue(value.appearance,pair.Value);value.appearance.Normalize();
   var appearance=document.ContainsKey("appearance")?document["appearance"] as Dictionary<string,object>:new Dictionary<string,object>();foreach(var pair in json.Deserialize<Dictionary<string,object>>(json.Serialize(value.appearance)))appearance[pair.Key]=pair.Value;document["appearance"]=appearance;
   string next=json.Serialize(document);if(Encoding.UTF8.GetByteCount(next)>=8192)throw new IOException("歌词设置文件过大，原文件已保留。");
   string current;Dictionary<string,object> ignored;LyricsSettings.ReadStrict(root,out current,out ignored);if(current!=actual)throw new LyricsSettingsConflictException();if(!authorize())throw new UnauthorizedAccessException();
   LyricsFiles.Write(Path.Combine(root,"lyrics-settings.json"),next);Observe(next);result=Snapshot(value,true);
  });return result;
 }
}
internal sealed class MediaSnapshot {
 public string source="",title="",artist="",album="",status="",cover="";
 public double sampledAt=0,position=0,duration=0,rate=1;
 public bool timeline=false,smtc=false;
 public static double Unix(DateTime now){return (now-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;}
 public bool Fresh(DateTime now){double age=Unix(now)-sampledAt;return age>=-1&&age<4;}
 public string Key {get{return source+"\n"+title+"\n"+artist+"\n"+album+"\n"+Math.Round(duration).ToString(CultureInfo.InvariantCulture);}}
 public double Position(DateTime now){
  return Math.Max(0,Math.Min(duration,position+(status=="playing"?Math.Max(0,Unix(now)-sampledAt)*rate:0)));
 }
 internal System.Diagnostics.ProcessStartInfo ControlRequest(string root,byte code,DateTime now){
  if(!smtc||!Fresh(now))return null;
  string action=code==0xB1?"previous":code==0xB0?"next":code==0xB3?"toggle":"";
  if(action.Length==0||String.IsNullOrEmpty(source)||source.Length>1024)throw new ArgumentException("Invalid media control request.");
  string encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(source));
  string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
  return new System.Diagnostics.ProcessStartInfo(shell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+Path.Combine(root,"MediaSession.ps1")+"\" -Control "+action+" -Source64 "+encoded+" -RequestedAt "+Unix(now).ToString("R",CultureInfo.InvariantCulture)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden};
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
internal sealed class QQMusicLyricResult {public string lrc="",status="未确认 QQ 音乐唯一准确版本",mid="";}
internal static class QQMusicLyrics {
 const string SearchModule="music.search.SearchCgiService",Endpoint="https://u.y.qq.com/cgi-bin/musicu.fcg";
 public static bool IsSource(string source){return String.Equals(source,"QQMusic.exe",StringComparison.OrdinalIgnoreCase);}
 public static string CacheKey(MediaSnapshot song){return (IsSource(song.source)?"qq-exact-v1\n":"")+song.Key;}
 static Dictionary<string,object> Map(object value){return value as Dictionary<string,object>;}
 static object Value(Dictionary<string,object> data,string key){object value;return data!=null&&data.TryGetValue(key,out value)?value:null;}
 static string Text(Dictionary<string,object> data,string key){return Value(data,key) as string??"";}
 static Dictionary<string,object> Child(Dictionary<string,object> data,string key){return Map(Value(data,key));}
 static Dictionary<string,object> Parse(string body){if(String.IsNullOrEmpty(body)||body.Length>1048576)throw new InvalidDataException("Invalid QQ response size.");return new JavaScriptSerializer{MaxJsonLength=1048576,RecursionLimit=64}.DeserializeObject(body) as Dictionary<string,object>;}
 static bool Zero(Dictionary<string,object> data,string key){var value=Value(data,key);return value is int&&(int)value==0;}
 static string Normalize(string value){return Regex.Replace(System.Net.WebUtility.HtmlDecode(value??"").Normalize(NormalizationForm.FormKC).Trim(),@"\s+"," ").ToUpperInvariant();}
 static bool Equal(string left,string right){return !String.IsNullOrWhiteSpace(left)&&!String.IsNullOrWhiteSpace(right)&&Normalize(left)==Normalize(right);}
 static bool ValidMid(string mid){return Regex.IsMatch(mid??"",@"\A[A-Za-z0-9]{14}\z");}
 static IEnumerable<Dictionary<string,object>> Items(object value){var list=value as object[];if(list!=null)foreach(var item in list){var entry=Map(item);if(entry!=null)yield return entry;}}
 static IEnumerable<Dictionary<string,object>> Candidates(Dictionary<string,object> data,int depth){if(depth>4)yield break;yield return data;foreach(var grouped in Items(Value(data,"grp")))foreach(var nested in Candidates(grouped,depth+1))yield return nested;}
 static bool SameVersion(Dictionary<string,object> candidate,MediaSnapshot song){
  if(song==null||String.IsNullOrWhiteSpace(song.artist)||String.IsNullOrWhiteSpace(song.album)||song.duration<=0||Double.IsNaN(song.duration)||Double.IsInfinity(song.duration))return false;
  string title=Text(candidate,"title");if(title.Length==0)title=Text(candidate,"name");
  double duration;var raw=Value(candidate,"interval");if(raw==null||!Double.TryParse(Convert.ToString(raw,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out duration)||duration<=0||Double.IsNaN(duration)||Double.IsInfinity(duration)||Math.Abs(duration-song.duration)>1)return false;
  var album=Child(candidate,"album");return Equal(title,song.title)&&(Equal(Text(album,"name"),song.album)||Equal(Text(album,"title"),song.album));
 }
 static bool SameArtists(Dictionary<string,object> candidate,string artist){
  var singers=Items(Value(candidate,"singer")).ToArray();if(singers.Length==0||singers.Length>8)return false;
  var variants=new List<string[]>{new string[0]};
  foreach(var singer in singers){var names=new[]{Text(singer,"name"),Text(singer,"title")}.Where(s=>!String.IsNullOrWhiteSpace(s)).Distinct().ToArray();if(names.Length==0)return false;variants=variants.SelectMany(parts=>names.Select(name=>parts.Concat(new[]{name}).ToArray())).ToList();}
  foreach(var parts in variants)foreach(var separator in new[]{" / ","/","、",", "," & ","; ","；"})if(Equal(String.Join(separator,parts),artist))return true;
  return false;
 }
 static List<Dictionary<string,object>> SearchCandidates(string body,MediaSnapshot song){
  var reply=Parse(body);var result=Child(reply,SearchModule);if(!Zero(reply,"code")||!Zero(result,"code"))throw new InvalidDataException("QQ search unavailable.");
  var section=Child(Child(Child(result,"data"),"body"),"song");var matches=new List<Dictionary<string,object>>();
  foreach(var item in Items(Value(section,"list")))foreach(var candidate in Candidates(item,0))if(ValidMid(Text(candidate,"mid"))&&SameVersion(candidate,song))matches.Add(candidate);
  return matches;
 }
 public static bool MatchesDetail(string detailJson,string mid,MediaSnapshot song){var reply=Parse(detailJson);var result=Child(reply,"detail");var track=Child(Child(result,"data"),"track_info");return ValidMid(mid)&&Zero(reply,"code")&&Zero(result,"code")&&Text(track,"mid")==mid&&SameVersion(track,song)&&SameArtists(track,song.artist);}
 public static string DecodeLyric(string lyricJson,double duration){
  var reply=Parse(lyricJson);if(!Zero(reply,"code")||!Zero(reply,"retcode"))return "";
  string lrc=System.Net.WebUtility.HtmlDecode(Text(reply,"lyric"));if(lrc.Length>512000||duration<=0||Double.IsNaN(duration)||Double.IsInfinity(duration))return "";
  var lines=LyricsTimeline.Parse(lrc);return lines.Any(line=>line.text.Length>0)&&lines.All(line=>line.time<=duration+1)?lrc:"";
 }
 static async Task<string> Request(HttpClient http,HttpMethod method,string uri,object payload,CancellationToken token){
  using(var request=new HttpRequestMessage(method,uri)){request.Headers.Referrer=new Uri("https://y.qq.com/");if(payload!=null)request.Content=new StringContent(new JavaScriptSerializer().Serialize(payload),Encoding.UTF8,"application/json");
   using(var response=await http.SendAsync(request,token).ConfigureAwait(false)){response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>1048576)throw new InvalidDataException("QQ response exceeds limit.");string body=await response.Content.ReadAsStringAsync().ConfigureAwait(false);token.ThrowIfCancellationRequested();if(body.Length>1048576)throw new InvalidDataException("QQ response exceeds limit.");return body;}
  }
 }
 public static async Task<QQMusicLyricResult> FetchAsync(HttpClient http,MediaSnapshot song,CancellationToken token){
  if(!IsSource(song.source)||String.IsNullOrWhiteSpace(song.title)||String.IsNullOrWhiteSpace(song.artist)||String.IsNullOrWhiteSpace(song.album)||song.duration<=0)return new QQMusicLyricResult();
  var search=new Dictionary<string,object>{{SearchModule,new{module=SearchModule,method="DoSearchForQQMusicDesktop",param=new{search_type=0,query=song.title+" "+song.artist,page_num=1,num_per_page=50}}}};
  string body=await Request(http,HttpMethod.Post,Endpoint,search,token).ConfigureAwait(false);
  // Search may omit official singer aliases. Only full song details can confirm the final version.
  var mids=SearchCandidates(body,song).Select(c=>Text(c,"mid")).Distinct(StringComparer.Ordinal).ToArray();if(mids.Length==0||mids.Length>5)return new QQMusicLyricResult();
  var confirmed=new List<string>();
  foreach(string mid in mids){var detail=new{detail=new{module="music.pf_song_detail_svr",method="get_song_detail_yqq",param=new{song_mid=mid,song_type=0,song_id=""}}};string info=await Request(http,HttpMethod.Post,Endpoint,detail,token).ConfigureAwait(false);var reply=Parse(info);var result=Child(reply,"detail");var track=Child(Child(result,"data"),"track_info");if(!Zero(reply,"code")||!Zero(result,"code")||Text(track,"mid")!=mid)throw new InvalidDataException("QQ song details unavailable.");if(MatchesDetail(info,mid,song))confirmed.Add(mid);}
  if(confirmed.Count!=1)return new QQMusicLyricResult();string selected=confirmed[0];
  string lyrics=await Request(http,HttpMethod.Get,"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid="+selected+"&g_tk=5381&format=json&inCharset=utf8&outCharset=utf-8&nobase64=1",null,token).ConfigureAwait(false);
  string lrc=DecodeLyric(lyrics,song.duration);return new QQMusicLyricResult{mid=selected,lrc=lrc,status=lrc.Length>0?"QQ 音乐原曲歌词":"QQ 音乐此版本暂未提供同步歌词"};
 }
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
 int attempts;bool disposed,lastMotionPaused;MediaSnapshot media;LyricsSettings settings;
 public bool CompanionActive {get;set;}
 public bool DesktopMediaActive {get;set;}
 LyricPaletteState palette;DateTime paletteRead=DateTime.MinValue;string paletteTrack="";
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
 void StopHelper(){helperRetry=DateTime.MinValue;if(helper==null)return;try{if(!helper.HasExited)helper.Kill();}catch{}helper.Dispose();helper=null;}
 void CancelLookup(){if(cancellation!=null){cancellation.Cancel();cancellation.Dispose();cancellation=null;}if(lookup!=null)lookup.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);lookup=null;}
 void Status(string value){if(value==state)return;state=value;try{File.WriteAllText(Path.Combine(root,"lyrics-status.txt"),value);}catch(IOException){}}
 public void Tick(DateTime now,bool dockVisible,bool pointerNear,bool motionPaused){
  if(disposed)return;
  lastMotionPaused=motionPaused;
  if(pointerNear)approached=now;
  if((now-sample).TotalMilliseconds>=250){
   sample=now;var next=LyricsSettings.Load(root,settings);
   if(settings!=null&&settings.online!=next.online){CancelLookup();attempts=0;retry=DateTime.MinValue;}
   settings=next;
   if(netease!=null)netease.Enable(settings.enabled||CompanionActive||DesktopMediaActive);
   if(!settings.enabled){
    CancelLookup();track="";lines.Clear();Status("Dock 歌词已关闭");
    if(CompanionActive||DesktopMediaActive){StartHelper(now);var observed=MediaSnapshot.Read(root,now);media=observed??(media!=null&&media.Fresh(now)?media:null);}
    else{StopHelper();media=null;}
   }
   else {
    StartHelper(now);var observed=MediaSnapshot.Read(root,now);
    // A file replacement or missed sample is not a track change. Keep the last
    // valid sample only until its original heartbeat expires; never extend it.
    media=observed??(media!=null&&media.Fresh(now)?media:null);
    string key=media==null?"":QQMusicLyrics.CacheKey(media);
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
    else if(lines.Count>0)Status((QQMusicLyrics.IsSource(media.source)?"QQ 音乐原曲歌词显示中":"歌词显示中")+" · 鼠标靠近恢复应用");
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
 public object CompanionSnapshot(DateTime now){
  if(disposed||settings==null||(!settings.enabled&&!CompanionActive)||media==null||!media.Fresh(now))return new{available=false};
  var pair=settings.enabled&&media.timeline?LyricsTimeline.At(lines,media.Position(now)+settings.offsetMs/1000.0):new[]{"",""};
  bool gap=settings.enabled&&media.timeline&&pair[0].Length==0&&lines.Count>0&&settings.holdGaps&&media.Position(now)<media.duration;
  if(gap){pair[0]=media.title;pair[1]=(media.Position(now)<lines[0].time?"前奏":"间奏")+(String.IsNullOrWhiteSpace(media.artist)?"":" · "+media.artist);}
  ReadPalette(now);
  return new{available=true,updatedAt=(long)(media.sampledAt*1000),title=media.title,artist=media.artist,source=media.source,playing=media.status=="playing",position=media.Position(now),duration=media.duration,current=pair[0],next=pair[1],lyricsEnabled=settings.enabled,lyricsOnline=settings.online,appearance=settings.appearance,motionPaused=lastMotionPaused,gap=gap,palette=new{album=palette.album,wallpaper=palette.wallpaper,status=palette.status}};
 }
 void ReadPalette(DateTime now){string key=media==null?"":media.Key+"|"+media.cover;if(palette==null||paletteTrack!=key||(now-paletteRead).TotalMilliseconds>=750){palette=LyricBackgroundPalette.Read(root,media);paletteRead=now;paletteTrack=key;}}
 void Publish(DateTime now,string current,string next,bool motionPaused,bool gap=false){
  string value=(current.Length>0?"1":"0")+"|"+(motionPaused?"1":"0")+"\n"+Convert.ToBase64String(Encoding.UTF8.GetBytes(LyricsTimeline.DisplayText(current)))+"\n"+Convert.ToBase64String(Encoding.UTF8.GetBytes(LyricsTimeline.DisplayText(next)))+"\n";
  var appearance=settings==null?new LyricsAppearance():settings.appearance;
  ReadPalette(now);
  string signature=value+json.Serialize(appearance)+json.Serialize(palette)+gap;
  if(signature==lastOutput&&(now-published).TotalMilliseconds<750)return;
  // Send Unicode directly to the glass WebView; Rainmeter only controls icon visibility.
  var changed=VisualChanged;if(changed!=null)changed(new {type="lyrics",show=current.Length>0,current=current,next=next,motionPaused=motionPaused,gap=gap,appearance=appearance,palette=palette});
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
   if(QQMusicLyrics.IsSource(song.source)){var qq=await QQMusicLyrics.FetchAsync(http,song,token).ConfigureAwait(false);return new LookupResult{lrc=qq.lrc,status=qq.status};}
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
