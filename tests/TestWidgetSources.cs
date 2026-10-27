using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Web.Script.Serialization;
internal static class RuntimePaths{public static string Rainmeter{get{throw new Exception("Fixture must never launch Rainmeter");}}}
internal sealed class LyricsSettings{public bool enabled,online,holdGaps;public int offsetMs;public LyricsAppearance appearance;public static LyricsSettings Load(string root){return new LyricsSettings();}public void Save(string root){throw new Exception("Read-only fixture");}}
internal sealed class LyricsAppearance{public LyricsAppearance Normalize(){return this;}}
internal sealed class MediaSnapshot{public string title,artist,status;public static MediaSnapshot current;public static MediaSnapshot Read(string root,DateTime now){return current;}}
internal static class LyricBackgroundPalette{public static string path="";public static string CoverPath(string root,MediaSnapshot song){return path;}public static object Read(string root,MediaSnapshot song){return null;}}
internal static class TestWidgetSources{
 static readonly JavaScriptSerializer json=new JavaScriptSerializer();static int checks;static string root,ini,skins;static List<string> commands=new List<string>();
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static Dictionary<string,object> Map(object value){return json.Deserialize<Dictionary<string,object>>(json.Serialize(value));}
 static Dictionary<string,object> Child(Dictionary<string,object> value,string key){return (Dictionary<string,object>)value[key];}
 static WidgetBackend Backend(bool running=true){return new WidgetBackend(root,ini,s=>commands.Add(s),()=>running);}
 static void Install(int active=0){Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(skins,"PrismGlass","@Resources"));string settings="[Rainmeter]\nSkinPath="+skins+"\\\n";foreach(var name in new[]{"Weather","System","Music","Focus"}){string directory=Path.Combine(skins,"PrismGlass",name);Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"Main.ini"),"[Rainmeter]\nOnRefreshAction=[!Hide]\n[Variables]\nLiquidRoot="+root+"\\\n");settings+="[PrismGlass\\"+name+"]\nActive="+active+"\n";}File.WriteAllText(ini,settings);commands.Clear();}
 static void Write(string name,string content){File.WriteAllText(Path.Combine(root,"widget-"+name+".json"),content);}
 static void Main(string[] args){
  string fixture=Path.GetFullPath(args[0]);root=Path.Combine(fixture,"app");skins=Path.Combine(fixture,"Skins");ini=Path.Combine(fixture,"Rainmeter.ini");Install();
  File.WriteAllText(Path.Combine(root,"skin-path.txt"),Path.Combine(skins,"PrismGlass"));
  var backend=Backend();var first=Map(backend.EnsureCompanionSources());
  Check(commands.Count==4&&commands.All(s=>s.StartsWith("!ActivateConfig \"PrismGlass\\")&&s.EndsWith("\" \"Main.ini\"")),"explicit Start recovers only the four existing hidden source skins");
  Check(!(bool)Child(first,"weather")["available"]&&((string)Child(first,"weather")["reason"]).Contains("正在启动"),"activation reports pending instead of inventing source availability");
  backend.EnsureCompanionSources();backend.Read();backend.Read();Check(commands.Count==4,"duplicate Start and ordinary polling do not repeatedly activate sources");
  Check(WidgetBackend.IniValue(ini,"PrismGlass\\Weather","Active")=="0","source recovery does not rewrite Rainmeter or login configuration");
  Install();backend=Backend(false);var absent=Map(backend.EnsureCompanionSources());Check(commands.Count==0&&((string)Child(absent,"system")["reason"]).Contains("Rainmeter 未运行"),"stopped Rainmeter is diagnosed without starting other visible skins");
  Install();File.AppendAllText(Path.Combine(skins,"PrismGlass","Weather","Main.ini"),"ignored=fixture\n");File.WriteAllText(Path.Combine(skins,"PrismGlass","Weather","Main.ini"),"[Rainmeter]\nOnRefreshAction=[!Hide]\n[Variables]\nLiquidRoot="+Path.Combine(fixture,"wrong")+"\n");
  var mismatch=Map(Backend().EnsureCompanionSources());Check(commands.Count==3&&((string)Child(mismatch,"weather")["reason"]).Contains("目录"),"mismatched export destination is rejected without copying or moving user state");
  Install();File.WriteAllText(Path.Combine(skins,"PrismGlass","Music","Main.ini"),"[Rainmeter]\nOnRefreshAction=[!Show]\n[Variables]\nLiquidRoot="+root+"\n");var visible=Map(Backend().EnsureCompanionSources());Check(commands.Count==3&&((string)Child(visible,"volume")["reason"]).Contains("隐藏"),"source activation never opens a skin configured as visible");
  Install(1);backend=Backend();backend.EnsureCompanionSources();Check(commands.Count==4&&commands.All(s=>s.StartsWith("!UpdateMeasure"))&&commands.Any(s=>s.Contains("\"Timer\"")),"already active stale sources receive a measure update without resetting or reactivating skins");
  long weatherAt=(long)(DateTime.UtcNow.AddMinutes(-10)-new DateTime(1970,1,1)).TotalMilliseconds;
  Write("Weather","{\"ready\":true,\"updatedAt\":"+weatherAt+",\"temp\":23}");Write("System","{\"cpu\":35,\"ram\":50,\"up\":128,\"down\":512}");Write("Music","{\"value\":72}");Write("Focus","{\"available\":true}");
  commands.Clear();backend=Backend();backend.EnsureCompanionSources();Check(commands.Count==0,"healthy active sources are left untouched");
  var all=Map(backend.Read());var statuses=Child(all,"sourceStatus");var weather=Child(statuses,"weather");
  Check((bool)weather["available"]&&Convert.ToInt64(weather["updatedAt"])==weatherAt&&Convert.ToInt64(weather["heartbeatAt"])>weatherAt,"weather reports the fetch timestamp separately from the export heartbeat");
  Check(all["system"]!=null&&all["volume"]!=null&&(bool)Child(statuses,"system")["available"],"fresh CPU, memory and audio snapshots remain compatible with the desktop");
  Check(!(bool)Child(all,"focus")["available"]&&((string)Child(all,"focus")["reason"]).Contains("初始化"),"a missing timer state is distinguished from a stopped sampler");
  File.WriteAllText(Path.Combine(skins,"PrismGlass","@Resources","focus-state.txt"),"1500 1500 0");all=Map(backend.Read());Check((bool)Child(all,"focus")["available"],"first default timer state enables focus controls once the source heartbeat arrives");
  var disk=Child(all,"disk");Check((bool)disk["available"]&&Convert.ToInt64(disk["total"])>0&&Convert.ToInt64(disk["free"])>=0&&!disk.ContainsKey("path"),"system drive capacity is real and contains no filesystem paths");
  File.SetLastWriteTimeUtc(Path.Combine(root,"widget-System.json"),DateTime.UtcNow.AddHours(-12));all=Map(backend.Read());Check(all["system"]==null&&!(bool)Child(Child(all,"sourceStatus"),"system")["available"],"old system data is unavailable rather than displayed as current");
  File.SetLastWriteTimeUtc(Path.Combine(root,"widget-Focus.json"),DateTime.UtcNow.AddSeconds(-20));Check(!(bool)Child(Map(backend.Read()),"focus")["available"],"saved timer data cannot make a stopped focus sampler appear ready");
  Write("Weather","{\"ready\":true,\"updatedAt\":\"invalid\"}");Check(!(bool)Child(Child(Map(backend.Read()),"sourceStatus"),"weather")["available"],"invalid weather timestamp is unavailable without breaking other source reads");
  Write("Weather","{\"ready\":false,\"updatedAt\":"+weatherAt+"}");Check(!(bool)Child(Child(Map(backend.Read()),"sourceStatus"),"weather")["available"],"failed forecast refresh remains unavailable despite a fresh heartbeat");
  commands.Clear();backend.Action("volume-set:55");Check(commands.Count==1&&commands[0]=="!CommandMeasure Volume \"SetVolume 55\" \"PrismGlass\\Music\"","absolute volume builds only the fixed existing Music measure command");
  foreach(string invalid in new[]{"volume-set:","volume-set:-1","volume-set:101","volume-set:5.5","volume-set:50\" !Quit","volume-set: 5"}){bool rejected=false;try{backend.Action(invalid);}catch(ArgumentException){rejected=true;}Check(rejected&&commands.Count==1,"absolute volume rejects malformed or unsafe command suffix "+invalid);}
  MediaSnapshot.current=new MediaSnapshot{title="QQ music fixture",artist="QQ artist",status="playing"};
  LyricBackgroundPalette.path=Path.Combine(fixture,"session-cover-0123456789abcdef.bin");File.WriteAllBytes(LyricBackgroundPalette.path,new byte[]{1});
  var music=Child(Map(backend.Read()),"music");Check((string)music["title"]=="QQ music fixture"&&(bool)music["playing"]&&((string)music["cover"]).StartsWith("https://prism-media.local/session-cover-0123456789abcdef.bin?v="),"QQ and other SMTC sessions supply the desktop card cover without the NetEase bridge");
  LyricBackgroundPalette.path="";music=Child(Map(backend.Read()),"music");Check((string)music["cover"]=="","missing session artwork does not retain another player's cover");
  MediaSnapshot.current=null;
  Console.WriteLine("Completed "+checks+" widget source checks.");
 }
}
