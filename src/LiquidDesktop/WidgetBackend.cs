using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
internal sealed class WidgetBackend {
 readonly string root,rainmeterIni;readonly Action<string> sendBang;readonly Func<bool> rainmeterRunning;readonly object sourcesGate=new object();readonly Dictionary<string,string> sourceReasons=new Dictionary<string,string>();readonly JavaScriptSerializer json=new JavaScriptSerializer();DateTime ensuredAt=DateTime.MinValue,diskAt=DateTime.MinValue;object disk;
 public WidgetBackend(string path):this(path,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Rainmeter","Rainmeter.ini"),SendBang,RainmeterRunning){}
 internal WidgetBackend(string path,string settings,Action<string> send,Func<bool> running){root=path;rainmeterIni=settings;sendBang=send;rainmeterRunning=running;}
 static bool RainmeterRunning(){var processes=Process.GetProcessesByName("Rainmeter");try{return processes.Length>0;}finally{foreach(var process in processes)process.Dispose();}}
 internal static string IniValue(string path,string section,string key){if(!File.Exists(path)||new FileInfo(path).Length>524288)return "";string current="";foreach(var raw in File.ReadAllLines(path)){string line=raw.Trim();if(line.StartsWith("[",StringComparison.Ordinal)&&line.EndsWith("]",StringComparison.Ordinal)){current=line.Substring(1,line.Length-2);continue;}int equals=line.IndexOf('=');if(equals>0&&current.Equals(section,StringComparison.OrdinalIgnoreCase)&&line.Substring(0,equals).Trim().Equals(key,StringComparison.OrdinalIgnoreCase))return line.Substring(equals+1).Trim();}return "";}
 string SkinRoot(){var configured=Path.Combine(root,"skin-path.txt");if(File.Exists(configured))return Path.GetDirectoryName(Path.GetFullPath(File.ReadAllText(configured).Trim()));string path=IniValue(rainmeterIni,"Rainmeter","SkinPath");return String.IsNullOrEmpty(path)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Rainmeter","Skins"):Environment.ExpandEnvironmentVariables(path);}
 Dictionary<string,object> Snapshot(string name){try{var file=Path.Combine(root,"widget-"+name+".json");double age=(DateTime.UtcNow-File.GetLastWriteTimeUtc(file)).TotalSeconds;if(age< -1||age>=8||new FileInfo(file).Length>32768)return null;return json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));}catch{return null;}}
 static long Milliseconds(DateTime value){return (long)(value-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
 object SourceStatus(string name,Dictionary<string,object> snapshot){
  bool available=snapshot!=null;long updated=0,heartbeat=0;string reason="";object value;
  if(available){heartbeat=Milliseconds(File.GetLastWriteTimeUtc(Path.Combine(root,"widget-"+name+".json")));updated=heartbeat;if(name=="Weather"){updated=0;if(snapshot.TryGetValue("updatedAt",out value))long.TryParse(Convert.ToString(value),out updated);available=snapshot.TryGetValue("ready",out value)&&Object.Equals(value,true)&&updated>0&&updated<=Milliseconds(DateTime.UtcNow.AddSeconds(1));if(!available)reason="天气尚未成功更新，请稍候或检查电脑网络。";}}
  if(!available&&reason.Length==0){lock(sourcesGate){sourceReasons.TryGetValue(name,out reason);if(reason=="正在启动隐藏采集。"&&(DateTime.UtcNow-ensuredAt).TotalSeconds>=8)reason="采集未恢复，请在电脑端重新开启伴随屏。";}if(String.IsNullOrEmpty(reason))reason="采集尚未运行，请在电脑端开启伴随屏。";}
  return new{available=available,reason=reason,updatedAt=available?updated:0,heartbeatAt=heartbeat};
 }
 object SourceStatuses(){return new{weather=SourceStatus("Weather",Snapshot("Weather")),system=SourceStatus("System",Snapshot("System")),volume=SourceStatus("Music",Snapshot("Music")),focus=SourceStatus("Focus",Snapshot("Focus"))};}
 // Called only by an explicit companion Start. Never activates visible cards or changes login startup.
 public object EnsureCompanionSources(){lock(sourcesGate){
  if((DateTime.UtcNow-ensuredAt).TotalSeconds<8)return SourceStatuses();ensuredAt=DateTime.UtcNow;
  foreach(string name in new[]{"Weather","System","Music","Focus"})try{
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
  return new{type="widgets",weather=Snapshot("Weather"),system=Snapshot("System"),volume=Snapshot("Music"),sourceStatus=SourceStatuses(),disk=ReadDisk(),music=new{title=title,artist=artist,cover=cover,playing=playing},lyrics=new{enabled=lyrics.enabled,online=lyrics.online,offsetMs=lyrics.offsetMs,status=lyricStatus,holdGaps=lyrics.holdGaps,appearance=lyrics.appearance,palette=LyricBackgroundPalette.Read(root,session)},focus=new{available=focusReady,reason=focusReady?"":Snapshot("Focus")==null?"专注采集尚未运行。":"专注计时状态尚未初始化。",total=total,remaining=remaining,deadline=deadline,completed=completed,lastCompleted=lastCompleted}};
 }
 [DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 static void Brightness(int delta){using(var query=new ManagementObjectSearcher("root\\WMI","SELECT * FROM WmiMonitorBrightness")){foreach(ManagementObject item in query.Get()){int level=Math.Max(0,Math.Min(100,Convert.ToInt32(item["CurrentBrightness"])+delta));using(var methods=new ManagementObjectSearcher("root\\WMI","SELECT * FROM WmiMonitorBrightnessMethods")){foreach(ManagementObject method in methods.Get())method.InvokeMethod("WmiSetBrightness",new object[]{(uint)1,(byte)level});}return;}}throw new InvalidOperationException("此显示器不支持系统亮度控制，请使用显示器按键。");}
 static void Launch(string file,string args){Process.Start(new ProcessStartInfo(file,args){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Normal});}
 static void SendBang(string args){using(var process=Process.Start(new ProcessStartInfo(RuntimePaths.Rainmeter,args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){} }
 void Bang(string args){sendBang(args);}
 public void Action(string id){
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
   case "brightness-up":Brightness(10);break;
   case "brightness-down":Brightness(-10);break;
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
   case "weather":Launch("https://www.msn.com/weather","");break;
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
