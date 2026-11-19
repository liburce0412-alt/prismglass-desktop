using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
internal sealed class WidgetBackend : IDisposable {
 readonly string root,rainmeterIni;readonly Action<string> sendBang;readonly Func<bool> rainmeterRunning;readonly object sourcesGate=new object();readonly Dictionary<string,string> sourceReasons=new Dictionary<string,string>();readonly JavaScriptSerializer json=new JavaScriptSerializer();DateTime ensuredAt=DateTime.MinValue,diskAt=DateTime.MinValue;object disk;
 readonly WeatherData weather;
 readonly DisplayBrightness brightness;
 readonly AudioOutputState audioOutput;
 DateTime workAt=DateTime.MinValue,workWritten=DateTime.MinValue;string workText="",pendingWorkAction="";DateTime pendingWorkUntil;
 static object WorkValue(Dictionary<string,object> map,string key){object value;return map!=null&&map.TryGetValue(key,out value)?value:null;}
 static string WorkText(object value){string text=Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture).Replace('\r',' ').Replace('\n',' ');return text.Length>120?text.Substring(0,120):text;}
 // Called once per second by the primary host, using SharedDesktopData's cached snapshot.
 internal void SyncWorkWidgets(object snapshot){
  var now=DateTime.UtcNow;if((now-workAt).TotalMilliseconds<900)return;workAt=now;
  string request=Path.Combine(root,"widget-action.request");
  try{if(File.Exists(request)){string action=new FileInfo(request).Length<=64?File.ReadAllText(request).Trim():"";File.Delete(request);if(action=="focus-toggle"||action=="focus-reset"||action=="focus-25"||action=="focus-5"){
    if(Snapshot("Focus")!=null)Action(action);else if(EnsureWorkFocus()){pendingWorkAction=action;pendingWorkUntil=now.AddSeconds(5);}
  }}if(pendingWorkAction.Length>0){if(Snapshot("Focus")!=null){string action=pendingWorkAction;pendingWorkAction="";Action(action);}else if(now>=pendingWorkUntil)pendingWorkAction="";}}catch{pendingWorkAction="";}
  try{
   var widgets=json.Deserialize<Dictionary<string,object>>(json.Serialize(snapshot));var focus=WorkValue(widgets,"focus") as Dictionary<string,object>;var current=WorkValue(widgets,"weather") as Dictionary<string,object>;
   var lines=new List<string>{"version=1"};Action<string,object> add=(key,value)=>lines.Add(key+"="+WorkText(value));
   try{var content=new CompanionContent(root);var tasks=json.Deserialize<Dictionary<string,object>>(json.Serialize(content.ReadTasks()));var entries=WorkValue(tasks,"items") as System.Collections.IEnumerable;int total=0,pending=0;string first="";
    if(entries!=null)foreach(var item in entries){var task=item as Dictionary<string,object>;total++;if(!Object.Equals(WorkValue(task,"done"),true)){pending++;if(first.Length==0)first=WorkText(WorkValue(task,"text"));}}
    add("tasksAvailable",1);add("taskPending",pending);add("taskTotal",total);add("taskFirst",first);add("countdownTitle",WorkValue(tasks,"countdownTitle"));add("countdownDate",WorkValue(tasks,"countdownDate"));
   }catch{add("tasksAvailable",0);}
   add("focusAvailable",Object.Equals(WorkValue(focus,"available"),true)?1:0);foreach(var key in new[]{"total","remaining","deadline","completed"})add("focus"+Char.ToUpperInvariant(key[0])+key.Substring(1),WorkValue(focus,key));
   bool weatherReady=Object.Equals(WorkValue(current,"ready"),true)&&!Object.Equals(WorkValue(current,"stale"),true);add("weatherTemp",weatherReady?WorkValue(current,"temp"):null);add("weatherCode",weatherReady?WorkValue(current,"code"):null);add("weatherLocation",weatherReady?WorkValue(current,"location"):null);
   // The standalone Rainmeter card consumes this same sample; it must not keep
   // a second public HTTP collector with different proxy, cache or failure rules.
   add("weatherReady",Object.Equals(WorkValue(current,"ready"),true)?1:0);add("weatherStale",Object.Equals(WorkValue(current,"stale"),true)?1:0);
   foreach(var key in new[]{"temp","code","high","low","sunrise","sunset","utcOffsetSeconds","updatedAt","reason","source"})add("weatherShared"+Char.ToUpperInvariant(key[0])+key.Substring(1),WorkValue(current,key));
   lines.Add("weatherSharedLocation="+Convert.ToString(WorkValue(current,"location"),System.Globalization.CultureInfo.InvariantCulture).Replace('\r',' ').Replace('\n',' '));
   string text=String.Join("\n",lines);if(text!=workText||(now-workWritten).TotalSeconds>=5){File.WriteAllText(Path.Combine(root,"widget-work-context.txt"),"stamp="+(Milliseconds(now)/1000)+"\n"+text,new System.Text.UTF8Encoding(false));workText=text;workWritten=now;}
  }catch{}
 }
 bool EnsureWorkFocus(){
  if(!rainmeterRunning())return false;string skin=Path.Combine(SkinRoot(),"PrismGlass","Focus","Main.ini"),destination=IniValue(skin,"Variables","LiquidRoot");
  if(!File.Exists(skin)||!Path.IsPathRooted(destination)||!String.Equals(Path.GetFullPath(destination).TrimEnd('\\','/'),Path.GetFullPath(root).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)||!IniValue(skin,"Rainmeter","OnRefreshAction").StartsWith("[!Hide]",StringComparison.OrdinalIgnoreCase))return false;
  if(IniValue(rainmeterIni,"PrismGlass\\Focus","Active")!="1")sendBang("!ActivateConfig \"PrismGlass\\Focus\" \"Main.ini\"");else sendBang("!UpdateMeasure \"Timer\" \"PrismGlass\\Focus\"");return true;
 }
 public WidgetBackend(string path):this(path,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Rainmeter","Rainmeter.ini"),SendBang,RainmeterRunning){weather=WeatherData.ForRoot(root,()=>Path.Combine(SkinRoot(),"PrismGlass","Weather","Main.ini"));brightness=new DisplayBrightness();audioOutput=new AudioOutputState();}
 internal WidgetBackend(string path,string settings,Action<string> send,Func<bool> running,WeatherData weather=null){root=path;rainmeterIni=settings;sendBang=send;rainmeterRunning=running;this.weather=weather;}
 static bool RainmeterRunning(){return RainmeterCommands.Find(RuntimePaths.Rainmeter)!=IntPtr.Zero;}
 internal static string IniValue(string path,string section,string key){if(!File.Exists(path)||new FileInfo(path).Length>524288)return "";string current="";foreach(var raw in File.ReadAllLines(path)){string line=raw.Trim();if(line.StartsWith("[",StringComparison.Ordinal)&&line.EndsWith("]",StringComparison.Ordinal)){current=line.Substring(1,line.Length-2);continue;}int equals=line.IndexOf('=');if(equals>0&&current.Equals(section,StringComparison.OrdinalIgnoreCase)&&line.Substring(0,equals).Trim().Equals(key,StringComparison.OrdinalIgnoreCase))return line.Substring(equals+1).Trim();}return "";}
 string SkinRoot(){var configured=Path.Combine(root,"skin-path.txt");if(File.Exists(configured))return Path.GetDirectoryName(Path.GetFullPath(File.ReadAllText(configured).Trim()));string path=IniValue(rainmeterIni,"Rainmeter","SkinPath");return String.IsNullOrEmpty(path)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Rainmeter","Skins"):Environment.ExpandEnvironmentVariables(path);}
 Dictionary<string,object> Snapshot(string name){try{var file=Path.Combine(root,"widget-"+name+".json");double age=(DateTime.UtcNow-File.GetLastWriteTimeUtc(file)).TotalSeconds;if(age< -1||age>=8||new FileInfo(file).Length>32768)return null;return json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));}catch{return null;}}
 static long Milliseconds(DateTime value){return (long)(value-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
 object SourceStatus(string name,Dictionary<string,object> snapshot){
  if(name=="Weather"&&weather!=null){object ready,stamp,received,stale,weatherReason;return new{available=snapshot!=null&&snapshot.TryGetValue("ready",out ready)&&Object.Equals(ready,true),updatedAt=snapshot!=null&&snapshot.TryGetValue("updatedAt",out stamp)?stamp:0L,fetchedAt=snapshot!=null&&snapshot.TryGetValue("fetchedAt",out received)?received:0L,stale=snapshot!=null&&snapshot.TryGetValue("stale",out stale)&&Object.Equals(stale,true),heartbeatAt=0L,reason=snapshot!=null&&snapshot.TryGetValue("reason",out weatherReason)?Convert.ToString(weatherReason):"天气尚未成功更新。"};}
  bool available=snapshot!=null;long updated=0,heartbeat=0;string reason="";object value;
  if(available){heartbeat=Milliseconds(File.GetLastWriteTimeUtc(Path.Combine(root,"widget-"+name+".json")));updated=heartbeat;if(name=="Weather"){updated=0;if(snapshot.TryGetValue("updatedAt",out value))long.TryParse(Convert.ToString(value),out updated);available=snapshot.TryGetValue("ready",out value)&&Object.Equals(value,true)&&updated>0&&updated<=Milliseconds(DateTime.UtcNow.AddSeconds(1));if(!available)reason="天气尚未成功更新，请稍候或检查电脑网络。";}}
  if(!available&&reason.Length==0){lock(sourcesGate){sourceReasons.TryGetValue(name,out reason);if(reason=="正在启动隐藏采集。"&&(DateTime.UtcNow-ensuredAt).TotalSeconds>=8)reason="采集未恢复，请在电脑端重新开启伴随屏。";}if(String.IsNullOrEmpty(reason))reason="采集尚未运行，请在电脑端开启伴随屏。";}
  return new{available=available,reason=reason,updatedAt=available?updated:0,heartbeatAt=heartbeat};
 }
 Dictionary<string,object> WeatherSnapshot(){return weather==null?Snapshot("Weather"):weather.Read();}
 object SourceStatuses(){return SourceStatuses(WeatherSnapshot());}
 object SourceStatuses(Dictionary<string,object> currentWeather){return new{weather=SourceStatus("Weather",currentWeather),system=SourceStatus("System",Snapshot("System")),volume=SourceStatus("Music",Snapshot("Music")),focus=SourceStatus("Focus",Snapshot("Focus"))};}
 // Called only by an explicit companion Start. Never activates visible cards or changes login startup.
 public object EnsureCompanionSources(){lock(sourcesGate){
  if((DateTime.UtcNow-ensuredAt).TotalSeconds<8)return SourceStatuses();ensuredAt=DateTime.UtcNow;
  foreach(string name in weather==null?new[]{"Weather","System","Music","Focus"}:new[]{"System","Music","Focus"})try{
   if(!rainmeterRunning()){sourceReasons[name]="Rainmeter 未运行，请先在电脑启动 PrismGlass。";continue;}
   string config="PrismGlass\\"+name,skin=Path.Combine(SkinRoot(),"PrismGlass",name,"Main.ini");
   if(!File.Exists(skin)){sourceReasons[name]="缺少已安装的采集皮肤，请重新安装 PrismGlass 组件。";continue;}
   string destination=IniValue(skin,"Variables","LiquidRoot"),refresh=IniValue(skin,"Rainmeter","OnRefreshAction");
   if(!Path.IsPathRooted(destination)||!String.Equals(Path.GetFullPath(destination).TrimEnd('\\','/'),Path.GetFullPath(root).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)){sourceReasons[name]="采集输出目录与当前程序不一致，请修复安装。";continue;}
   if(!refresh.StartsWith("[!Hide]",StringComparison.OrdinalIgnoreCase)){sourceReasons[name]="采集皮肤未配置为隐藏启动，请更新组件。";continue;}
   if(IniValue(rainmeterIni,config,"Active")!="1")sendBang("!ActivateConfig \""+config+"\" \"Main.ini\"");
   else if(Snapshot(name)==null)sendBang("!UpdateMeasure \""+(name=="Focus"?"Timer":"WidgetExport")+"\" \""+config+"\"");
   sourceReasons[name]="正在启动隐藏采集。";
  }catch{sourceReasons[name]="无法恢复采集，请检查 Rainmeter 与组件安装。";}
  return SourceStatuses();
 }}
 object ReadDisk(){if(disk!=null&&(DateTime.UtcNow-diskAt).TotalSeconds<30)return disk;diskAt=DateTime.UtcNow;try{var drive=new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));long total=drive.TotalSize,free=drive.AvailableFreeSpace;disk=new{available=total>0,free=free,total=total,usedPercent=total>0?100.0*(total-free)/total:0,updatedAt=Milliseconds(diskAt)};}catch{disk=new{available=false,reason="系统磁盘信息暂不可用。"};}return disk;}
 public object Read(){
  long total=1500,remaining=1500,deadline=0;bool focusReady=false;
  try{var fields=File.ReadAllText(Path.Combine(SkinRoot(),"PrismGlass","@Resources","focus-state.txt")).Trim().Split(' ');total=long.Parse(fields[0]);remaining=long.Parse(fields[1]);deadline=long.Parse(fields[2]);focusReady=total>0&&total<=86400&&remaining>=0&&remaining<=total&&deadline>=0&&Snapshot("Focus")!=null;}catch{}
  long completed=0,lastCompleted=0;try{var record=File.ReadAllText(Path.Combine(SkinRoot(),"PrismGlass","@Resources","focus-completed.txt")).Split(' ');completed=long.Parse(record[0]);lastCompleted=long.Parse(record[1]);}catch{}
  string title="等待播放",artist="",cover="";
  try{var lines=File.ReadAllLines(Path.Combine(root,"..","music-data","now.txt"));if(lines.Length>0)title=lines[0];if(lines.Length>1)artist=lines[1];if(lines.Length>2&&File.Exists(lines[2])){string media=Path.GetFullPath(Path.Combine(root,"..","music-data"))+Path.DirectorySeparatorChar;string full=Path.GetFullPath(lines[2]);if(full.StartsWith(media,StringComparison.OrdinalIgnoreCase))cover="https://prism-media.local/"+Uri.EscapeDataString(Path.GetFileName(full))+"?v="+File.GetLastWriteTimeUtc(full).Ticks;}}catch{}
  bool? playing=null;try{var status=Path.Combine(root,"..","music-data","playback.txt");if((DateTime.UtcNow-File.GetLastWriteTimeUtc(status)).TotalSeconds<12){var state=File.ReadAllText(status).Trim();if(state=="playing"||state=="paused")playing=state=="playing";}}catch{}
  var lyrics=LyricsSettings.Load(root);var session=MediaSnapshot.Read(root,DateTime.UtcNow);
  if(session!=null){if(title!=session.title||artist!=session.artist)cover="";title=session.title;artist=session.artist;playing=session.status=="playing";string image=LyricBackgroundPalette.CoverPath(root,session);if(image.Length>0)cover="https://prism-media.local/"+Uri.EscapeDataString(Path.GetFileName(image))+"?v="+File.GetLastWriteTimeUtc(image).Ticks;}
  string lyricStatus=lyrics.enabled?"等待播放器提供歌曲信息":"Dock 歌词已关闭";
  try{lyricStatus=File.ReadAllText(Path.Combine(root,"lyrics-status.txt"));}catch{}
  var currentWeather=WeatherSnapshot();return new{type="widgets",weather=currentWeather,system=Snapshot("System"),volume=Snapshot("Music"),brightness=BrightnessState(),audioOutput=audioOutput==null?new AudioOutputState.State{reason="声音输出状态未启用。"}:audioOutput.Snapshot(),sourceStatus=SourceStatuses(currentWeather),disk=ReadDisk(),music=new{title=title,artist=artist,cover=cover,playing=playing},lyrics=new{enabled=lyrics.enabled,online=lyrics.online,offsetMs=lyrics.offsetMs,status=lyricStatus,holdGaps=lyrics.holdGaps,appearance=lyrics.appearance,palette=LyricBackgroundPalette.Read(root,session)},focus=new{available=focusReady,reason=focusReady?"":Snapshot("Focus")==null?"专注采集尚未运行。":"专注计时状态尚未初始化。",total=total,remaining=remaining,deadline=deadline,completed=completed,lastCompleted=lastCompleted}};
 }
 [DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 public object BrightnessState(){return brightness==null?(object)new DisplayBrightness.State{reason="亮度服务未启用。"}:brightness.Snapshot();}
 public void Dispose(){if(brightness!=null)brightness.Dispose();if(audioOutput!=null)audioOutput.Dispose();}
 static void Launch(string file,string args){Process.Start(new ProcessStartInfo(file,args){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Normal});}
 static void SendBang(string args){RainmeterCommands.Send(RuntimePaths.Rainmeter,args,50);}
 void Bang(string args){sendBang(args);}
 public void Action(string id,Func<bool> allowed=null){
  if(id.StartsWith("brightness-target:",StringComparison.Ordinal)){if(brightness==null)throw new InvalidOperationException("亮度服务未启用。");brightness.Select(Uri.UnescapeDataString(id.Substring(18)));return;}
  if(id.StartsWith("brightness-set:",StringComparison.Ordinal)){int split=id.IndexOf(':',15),value;if(split<0||!Int32.TryParse(id.Substring(15,split-15),out value)||value<0||value>100)throw new ArgumentException("亮度需要为 0 到 100 的整数。");if(brightness==null)throw new InvalidOperationException("亮度服务未启用。");brightness.Set(Uri.UnescapeDataString(id.Substring(split+1)),value,allowed);return;}
  if(id.StartsWith("volume-set:",StringComparison.Ordinal)){int value;string raw=id.Substring(11);if(raw.Length==0||raw.Length>3||!System.Linq.Enumerable.All(raw,c=>c>='0'&&c<='9')||!Int32.TryParse(raw,out value)||value<0||value>100)throw new ArgumentException("音量需要为 0 到 100 的整数。");Bang("!CommandMeasure Volume \"SetVolume "+value.ToString(System.Globalization.CultureInfo.InvariantCulture)+"\" \"PrismGlass\\Music\"");return;}
  if(id.StartsWith("lyrics-style:",StringComparison.Ordinal)&&id.Length<4096){
   var settings=LyricsSettings.Load(root);settings.appearance=(json.Deserialize<LyricsAppearance>(id.Substring(13))??new LyricsAppearance()).Normalize();settings.Save(root);return;
  }
  if(id=="lyrics-style-reset"){var settings=LyricsSettings.Load(root);settings.appearance=new LyricsAppearance();settings.Save(root);return;}
  if(id=="lyrics-toggle"||id=="lyrics-online"||id=="lyrics-earlier"||id=="lyrics-later"||id=="lyrics-reset"||id=="lyrics-gaps"){
   var settings=LyricsSettings.Load(root);
   if(id=="lyrics-toggle")settings.enabled=!settings.enabled;
   if(id=="lyrics-online")settings.online=!settings.online;
   if(id=="lyrics-earlier")settings.offsetMs=Math.Min(5000,settings.offsetMs+200);
   if(id=="lyrics-later")settings.offsetMs=Math.Max(-5000,settings.offsetMs-200);
   if(id=="lyrics-reset")settings.offsetMs=0;
   if(id=="lyrics-gaps")settings.holdGaps=!settings.holdGaps;
   settings.Save(root);return;
  }
  switch(id){
   case "theme-sky":File.WriteAllText(Path.Combine(root,"theme.txt"),"Sky");break;
   case "theme-astro":File.WriteAllText(Path.Combine(root,"theme.txt"),"Astro");break;
   case "mute":keybd_event(0xAD,0,0,UIntPtr.Zero);keybd_event(0xAD,0,2,UIntPtr.Zero);break;
   case "brightness-up":case "brightness-down":if(brightness==null)throw new InvalidOperationException("亮度服务未启用。");var current=brightness.Snapshot();brightness.Set(current.targetId,Math.Max(0,Math.Min(100,current.value+(id=="brightness-up"?1:-1))),allowed);break;
   case "display":Launch("ms-settings:display","");break;
   case "focus-break":Bang("!CommandMeasure Timer \"Preset(5);Toggle()\" \"PrismGlass\\Focus\"");break;
   case "focus-again":Bang("!CommandMeasure Timer \"Preset(25);Toggle()\" \"PrismGlass\\Focus\"");break;
   case "focus-toggle":Bang("!CommandMeasure Timer \"Toggle()\" \"PrismGlass\\Focus\"");break;
   case "focus-reset":Bang("!CommandMeasure Timer \"Reset()\" \"PrismGlass\\Focus\"");break;
   case "focus-25":Bang("!CommandMeasure Timer \"Preset(25)\" \"PrismGlass\\Focus\"");break;
   case "focus-5":Bang("!CommandMeasure Timer \"Preset(5)\" \"PrismGlass\\Focus\"");break;
   case "volume-up":Bang("!CommandMeasure Volume \"ChangeVolume 5\" \"PrismGlass\\Music\"");break;
   case "volume-down":Bang("!CommandMeasure Volume \"ChangeVolume -5\" \"PrismGlass\\Music\"");break;
   case "sound":Launch("ms-settings:sound","");break;
   case "music":Launch("mswindowsmusic:","");break;
   case "weather":
   case "weather-settings":File.WriteAllText(Path.Combine(root,"workspace.request"),"weather");break;
   case "task-manager":Launch("taskmgr.exe","");break;
   case "desktop":Launch("explorer.exe","\""+Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)+"\"");break;
   case "documents":Launch("explorer.exe","\""+Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"\"");break;
   case "downloads":Launch("explorer.exe","\""+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")+"\"");break;
   case "apps":File.WriteAllText(Path.Combine(root,"workspace.request"),"apps");break;
   case "capture":Launch("ms-screenclip:","");break;
   case "recycle":Launch("explorer.exe","shell:RecycleBinFolder");break;
   case "notes":Launch("notepad.exe","\""+Path.GetFullPath(Path.Combine(root,"..","随手记.txt"))+"\"");break;
   case "clipboard":Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+Path.GetFullPath(Path.Combine(root,"..","OpenClipboardHistory.ps1"))+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});break;
   default:throw new ArgumentException("Unknown widget action: "+id);
  }
 }
}
