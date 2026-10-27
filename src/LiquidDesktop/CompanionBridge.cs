using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

// Network DTOs are deliberately projected here; desktop messages never cross this boundary.
internal sealed class CompanionBridge {
 readonly string root;readonly AppLibrary library;readonly DesktopPreferences preferences;readonly SharedDesktopData shared;readonly DockLyrics lyrics;readonly CompanionContent content;readonly LyricsStyleStore lyricsStyle;
 static readonly string[] SettingFields={"theme","manualPaused","glassOpacity","textScale","reducedMotion","dockIconSize","dockAutoHide","showLabels","gridColumns","pauseOnBattery","pauseOnPowerSaver","pauseOnRemote","hiddenCards","hiddenLeftCards"};
 static readonly string[] RightCards={"quota","desktop","notes","tasks","countdown","links","calculator"},LeftCards={"network","Weather","Music","Focus","Folders","System","Tools","Life"};
 readonly Func<Func<object>,object> onDesktop;readonly Func<bool> locked;readonly object gate=new object();readonly CompanionContext context;readonly CompanionControls controls;
 sealed class Assets {public Dictionary<string,byte[]> icons=new Dictionary<string,byte[]>();public string coverId="";public byte[] cover;}
 volatile Assets assetSnapshot=new Assets();
 readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=4194304};
 readonly Dictionary<string,string> remote=new Dictionary<string,string>(),allowed=new Dictionary<string,string>();
 readonly Dictionary<string,byte[]> icons=new Dictionary<string,byte[]>();string appsRevision=Guid.NewGuid().ToString("N"),appsSource="",coverId="",coverSource="";byte[] cover;
 public CompanionBridge(string path,AppLibrary apps,DesktopPreferences settings,SharedDesktopData data,DockLyrics dockLyrics,Func<Func<object>,object> dispatch,Func<bool> isLocked,CompanionContext foreground=null,CompanionControls remoteControls=null){root=path;library=apps;preferences=settings;shared=data;lyrics=dockLyrics;content=new CompanionContent(path);lyricsStyle=new LyricsStyleStore(path);onDesktop=dispatch;locked=isLocked;context=foreground??new CompanionContext();controls=remoteControls??new CompanionControls(isLocked);}
 public void StopControls(){controls.CancelAll();}
 public void Dispose(){controls.Dispose();}
 internal static string EntrySignature(AppLibraryEntry entry){return entry.kind+"\n"+entry.path+"\n"+entry.arguments+"\n"+entry.target;}
 public void Allow(Dictionary<string,string> entries){lock(gate){allowed.Clear();foreach(var p in entries)allowed[p.Key]=p.Value;appsSource="";remote.Clear();icons.Clear();PublishAssets();}}
 void PublishAssets(){assetSnapshot=new Assets{icons=new Dictionary<string,byte[]>(icons),coverId=coverId,cover=cover};}
 static long Now(){return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalMilliseconds;}
 Dictionary<string,object> Map(object value){return value==null?new Dictionary<string,object>():json.Deserialize<Dictionary<string,object>>(json.Serialize(value));}
 internal static string Text(Dictionary<string,object> data,string key){object v;return data.TryGetValue(key,out v)&&v is string?(string)v:"";}
 static object Value(Dictionary<string,object> data,string key){object v;return data.TryGetValue(key,out v)?v:null;}
 static object Pick(Dictionary<string,object> data,params string[] fields){var result=new Dictionary<string,object>();foreach(var field in fields)if(data.ContainsKey(field))result[field]=data[field];return result;}
 bool Permitted(AppLibraryEntry entry){string signature;return entry!=null&&entry.kind!="action"&&allowed.TryGetValue(entry.id,out signature)&&signature==EntrySignature(entry);}
 object Desktop(CompanionGrant grant,Func<object> operation){return onDesktop(()=>{if(!grant.Active||locked())throw new UnauthorizedAccessException();return operation();});}
 public CompanionReply Handle(string path,Dictionary<string,object> request,CompanionGrant grant){
  if(path=="/api/pointer"||path.StartsWith("/api/pointer/",StringComparison.Ordinal))return controls.Pointer(path,request,grant);
  if(path.StartsWith("/api/power/",StringComparison.Ordinal))return controls.Power(path,request,grant);
  if(path.StartsWith("/api/assets/",StringComparison.Ordinal)){if(!grant.Active||locked())return CompanionReply.Json(423,new{error="locked"});string id=path.Substring(12);byte[] bytes;var snapshot=assetSnapshot;if(grant.apps&&snapshot.icons.TryGetValue(id,out bytes))return CompanionReply.Asset(bytes,"image/png");if(grant.music&&id==snapshot.coverId&&snapshot.cover!=null)return CompanionReply.Asset(snapshot.cover,"image/jpeg");return CompanionReply.Json(404,new{error="not_found"});}
  lock(gate){
  if(!grant.Active||locked())return CompanionReply.Json(423,new{error="locked",message="电脑已锁定，请解锁后重连。"});
  try {
   if(path=="/api/state")return CompanionReply.Json(200,State(grant));
   if(path=="/api/apps"){if(!grant.apps)throw new UnauthorizedAccessException();return CompanionReply.Json(200,Apps());}
   if(path=="/api/tasks"){if(!grant.tasks)throw new UnauthorizedAccessException();return CompanionReply.Json(200,content.WriteTasks(request,()=>grant.Active&&!locked()));}
   if(path=="/api/notes"){if(!grant.notes)throw new UnauthorizedAccessException();return CompanionReply.Json(200,content.WriteNotes(request,()=>grant.Active&&!locked()));}
   if(path=="/api/lyrics-style"){
    if(!grant.music||!grant.control)throw new UnauthorizedAccessException();if(request.Keys.Any(k=>k!="expectedRevision"&&k!="appearance"&&k!="requestId"))throw new ArgumentException();
    return CompanionReply.Json(200,lyricsStyle.Write(Text(request,"expectedRevision"),Value(request,"appearance") as Dictionary<string,object>,()=>grant.Active&&grant.music&&grant.control&&!locked()));
   }
   if(path=="/api/settings"){
    if(!grant.control)throw new UnauthorizedAccessException();var patch=Value(request,"changes") as Dictionary<string,object>;if(patch==null||patch.Count==0||patch.Count>SettingFields.Length||patch.Keys.Any(k=>!SettingFields.Contains(k)))throw new ArgumentException();
    foreach(string key in new[]{"hiddenCards","hiddenLeftCards"})if(patch.ContainsKey(key))Cards(patch[key],key=="hiddenCards"?RightCards:LeftCards,true);
    return CompanionReply.Json(200,Desktop(grant,()=>{object saved=null;DesktopSettingsFiles.Locked(()=>{if(!grant.Active||locked())throw new UnauthorizedAccessException();preferences.Reload();if(Text(request,"expectedRevision")!=preferences.Revision.ToString())throw new CompanionContentConflictException();preferences.Apply(patch,preferences.Revision);saved=Settings();});return saved;}));
   }
   if(path=="/api/action")return CompanionReply.Json(200,Action(request,grant));
   return CompanionReply.Json(404,new{error="not_found"});
  }catch(CompanionContextChangedException){return CompanionReply.Json(409,new{error="context_changed",message="电脑已切换应用，请等待 Touch Bar 更新后再操作。"});}
   catch(CompanionContentConflictException){return CompanionReply.Json(409,new{error="conflict",message="内容已在另一端更新，请重新载入后合并。"});}
   catch(LyricsSettingsConflictException){return CompanionReply.Json(409,new{error="conflict",message="歌词样式已在另一端更新，请重新载入后重试。"});}
   catch(UnauthorizedAccessException){return CompanionReply.Json(403,new{error="forbidden",message="此设备未获授权，或电脑已锁定。"});}
   catch(ArgumentException){return CompanionReply.Json(400,new{error="invalid_request",message="请求参数无效，请刷新后重试。"});}
   catch(Exception){return CompanionReply.Json(503,new{error="unavailable",message="操作未能确认完成，请检查电脑当前状态。"});}
 }}
 object Section(Dictionary<string,object> widgets,string name,string file,bool ready,params string[] fields){var data=Value(widgets,name) as Dictionary<string,object>;var sources=Value(widgets,"sourceStatus") as Dictionary<string,object>;var status=sources==null?null:Value(sources,name) as Dictionary<string,object>;bool available=data!=null&&(!ready||Object.Equals(Value(data,"ready"),true))&&(status==null||Object.Equals(Value(status,"available"),true));return new{available=available,reason=status==null?"":Text(status,"reason"),updatedAt=available?(status==null?(object)(long)(File.GetLastWriteTimeUtc(Path.Combine(root,file))-new DateTime(1970,1,1)).TotalMilliseconds:Value(status,"updatedAt")):0,heartbeatAt=status==null?null:Value(status,"heartbeatAt"),data=available?Pick(data,fields):null};}
 static object ReadContent(Func<object> read){try{return read();}catch{return new{available=false,reason="电脑上的内容暂时无法读取，原文件已保留。"};}}
 static string[] Cards(object value,string[] allowed,bool strict){var items=value as System.Collections.IEnumerable;if(items==null||value is string){if(strict)throw new ArgumentException();return new string[0];}var result=new List<string>();foreach(var item in items){var card=item as string;if(card==null||!allowed.Contains(card)){if(strict)throw new ArgumentException();continue;}if(!result.Contains(card))result.Add(card);}return result.ToArray();}
 object Settings(){var snapshot=Map(preferences.Snapshot());if(Text(snapshot,"error")!="")return new{available=false,reason="桌面外观设置暂时无法读取，原文件已保留。"};var value=(Dictionary<string,object>)Pick(snapshot,SettingFields);value["available"]=true;value["reason"]="";value["revision"]=preferences.Revision.ToString();value["hiddenCards"]=Cards(Value(snapshot,"hiddenCards"),RightCards,false);value["hiddenLeftCards"]=Cards(Value(snapshot,"hiddenLeftCards"),LeftCards,false);return value;}
 object State(CompanionGrant grant){
  var widgets=Map(shared.ReadWidgets());var settings=Map(Desktop(grant,()=>ReadContent(Settings)));
  object music=new{available=false};if(grant.music){var state=Map(Desktop(grant,()=>lyrics.CompanionSnapshot(DateTime.UtcNow)));state["cover"]=Cover(state);music=state;}
  var focus=Value(widgets,"focus") as Dictionary<string,object>;if(focus==null||!Object.Equals(Value(focus,"available"),true)||!FocusAvailable())focus=new Dictionary<string,object>{{"available",false},{"reason",focus==null||Text(focus,"reason")==""?"专注采集尚未运行，请在电脑端重新开启伴随屏。":Text(focus,"reason")}};
  return new{protocol=1,serverTime=Now(),locked=false,theme=Value(settings,"theme"),settings=settings,lyricsSettings=grant.music?ReadContent(()=>lyricsStyle.Read(grant.control)):new{available=false},permissions=new{apps=grant.apps,tasks=grant.tasks,notes=grant.notes,music=grant.music,control=grant.control,pointer=grant.pointer,power=grant.power},controls=controls.Snapshot(grant),
   weather=Section(widgets,"weather","widget-Weather.json",true,"temp","code","high","low","sunrise","sunset"),system=Section(widgets,"system","widget-System.json",false,"cpu","ram","down","up"),volume=Section(widgets,"volume","widget-Music.json",false,"value"),music=music,
   disk=Pick(Map(Value(widgets,"disk")),"available","free","total","usedPercent","updatedAt","reason"),context=grant.control?context.Snapshot():new{available=false},focus=focus,tasks=grant.tasks?ReadContent(content.ReadTasks):new{available=false},notes=grant.notes?ReadContent(content.ReadNotes):new{available=false}};
 }
 object Apps(){
  var snapshot=library.GetSnapshot();var entries=snapshot.entries.Where(Permitted).OrderBy(e=>e.order).Take(500).ToList();
  string source=json.Serialize(entries.Select(e=>new{e.id,e.name,e.alias,e.category,e.folder,e.order,e.exists}));source+=json.Serialize(snapshot.categories)+json.Serialize(snapshot.folders);
  if(source!=appsSource){appsSource=source;appsRevision=Guid.NewGuid().ToString("N");remote.Clear();icons.Clear();foreach(var e in entries){string id=Guid.NewGuid().ToString("N");remote[id]=e.id;byte[] bytes=Icon(e.icon);if(bytes!=null)icons[id]=bytes;}}
  var items=new List<object>();foreach(var e in entries){string id=remote.First(p=>p.Value==e.id).Key;if(!icons.ContainsKey(id)){byte[] bytes=Icon(e.icon);if(bytes!=null)icons[id]=bytes;}items.Add(new{id=id,name=e.name,category=e.category,folder=e.folder,order=e.order,exists=e.exists,search=e.alias??"",icon=icons.ContainsKey(id)?"/api/assets/"+id:""});}
  var categories=snapshot.categories.Where(c=>entries.Any(e=>e.category==c.id)).Select(c=>new{c.id,c.name,c.order});var folders=snapshot.folders.Where(f=>entries.Any(e=>e.folder==f.id)).Select(f=>new{f.id,f.name,f.category,f.order});
  PublishAssets();return new{revision=appsRevision,items=items,categories=categories,folders=folders,limit=500};
 }
 static byte[] Icon(string uri){try{const string prefix="data:image/png;base64,";if(uri==null||!uri.StartsWith(prefix,StringComparison.Ordinal)||uri.Length>90000)return null;var bytes=Convert.FromBase64String(uri.Substring(prefix.Length));using(var stream=new MemoryStream(bytes))using(var source=Image.FromStream(stream))using(var small=new Bitmap(source,new Size(48,48)))using(var output=new MemoryStream()){small.Save(output,ImageFormat.Png);return output.ToArray();}}catch{return null;}}
 string NoCover(){cover=null;coverId="";coverSource="";PublishAssets();return "";}
 string Cover(Dictionary<string,object> music){
  try{if(!Object.Equals(Value(music,"available"),true)){return NoCover();}
   var song=MediaSnapshot.Read(root,DateTime.UtcNow);if(song==null||song.title!=Text(music,"title")||song.artist!=Text(music,"artist")||song.source!=Text(music,"source"))return NoCover();
   string directory=Path.GetFullPath(Path.Combine(root,"..","music-data")),file=LyricBackgroundPalette.CoverPath(root,song);if(String.IsNullOrEmpty(file))return NoCover();
   bool session=song.cover==Path.GetFileName(file)&&System.Text.RegularExpressions.Regex.IsMatch(song.cover??"",@"^session-cover-[a-f0-9]{16}\.bin$");if(!session&&(DateTime.UtcNow-File.GetLastWriteTimeUtc(Path.Combine(directory,"now.txt"))).TotalSeconds>12)return NoCover();
   if(!String.Equals(Path.GetDirectoryName(file),directory,StringComparison.OrdinalIgnoreCase)||!File.Exists(file)||new FileInfo(file).Length>4194304||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)return NoCover();
   string stamp=file+File.GetLastWriteTimeUtc(file).Ticks;if(stamp!=coverSource){using(var source=Image.FromFile(file)){if(source.Width>8192||source.Height>8192)return NoCover();double scale=Math.Min(1,256.0/Math.Max(source.Width,source.Height));using(var small=new Bitmap(source,new Size(Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale)))))using(var output=new MemoryStream()){small.Save(output,ImageFormat.Jpeg);cover=output.ToArray();}}coverSource=stamp;coverId=Guid.NewGuid().ToString("N");PublishAssets();}return "/api/assets/"+coverId;
  }catch{return NoCover();}
 }
 bool FocusAvailable(){return (DateTime.UtcNow-File.GetLastWriteTimeUtc(Path.Combine(root,"widget-Focus.json"))).TotalSeconds<8;}
 object Action(Dictionary<string,object> request,CompanionGrant grant){string name=Text(request,"action");
  if(name=="app.open"){
   if(!grant.apps)throw new UnauthorizedAccessException();if(Text(request,"expectedRevision")!=appsRevision)throw new CompanionContentConflictException();string desktopId;if(!remote.TryGetValue(Text(request,"targetId"),out desktopId))throw new ArgumentException();
   return Desktop(grant,()=>{var entry=library.Find(desktopId);if(!Permitted(entry)||!entry.exists)throw new ArgumentException();CompanionAppLaunch.Open(entry);return new{message="已在电脑提交打开请求，请查看电脑。"};});
  }
  if(name=="media.previous"||name=="media.toggle"||name=="media.next"){if(!grant.music)throw new UnauthorizedAccessException();return Desktop(grant,()=>{TopIsland.Media(name=="media.previous"?(byte)0xB1:name=="media.next"?(byte)0xB0:(byte)0xB3,root);return new{message="已提交媒体控制，请以播放器读回状态为准。"};});}
  if(!grant.control)throw new UnauthorizedAccessException();if(name=="context.execute")return Desktop(grant,()=>context.Execute(Text(request,"contextId"),Text(request,"command")));if(name=="desktop.execute")return Desktop(grant,()=>context.ExecuteGlobal(Text(request,"contextId"),Text(request,"command")));
  if(name=="volume.set"){object raw=Value(request,"value");if(!(raw is int)||(int)raw<0||(int)raw>100)throw new ArgumentException();return Desktop(grant,()=>{var current=Map(shared.ReadWidgets());var statuses=Value(current,"sourceStatus") as Dictionary<string,object>;var audio=statuses==null?null:Value(statuses,"volume") as Dictionary<string,object>;if(Value(current,"volume")==null||audio!=null&&!Object.Equals(Value(audio,"available"),true))throw new InvalidOperationException();shared.Widgets.Action("volume-set:"+((int)raw).ToString(System.Globalization.CultureInfo.InvariantCulture));shared.Invalidate();return new{message="已提交音量，请以最新读回状态为准。"};});}string command;
  switch(name){case "volume.up":command="volume-up";break;case "volume.down":command="volume-down";break;case "volume.mute":command="mute";break;case "focus.toggle":command="focus-toggle";break;case "focus.reset":command="focus-reset";break;case "focus.25":command="focus-25";break;case "focus.5":command="focus-5";break;default:throw new ArgumentException();}
  var widgets=Map(shared.ReadWidgets());if(name.StartsWith("focus.",StringComparison.Ordinal)){var focus=Value(widgets,"focus") as Dictionary<string,object>;if(focus==null||!Object.Equals(Value(focus,"available"),true)||!FocusAvailable())throw new InvalidOperationException();}else if(name!="volume.mute"&&Value(widgets,"volume")==null)throw new InvalidOperationException();
  return Desktop(grant,()=>{shared.Widgets.Action(command);shared.Invalidate();return new{message="已提交控制，请以最新读回状态为准。"};});
 }
}

internal static class CompanionAppLaunch {
 public static void Open(AppLibraryEntry entry){
  if(entry==null||!entry.exists||entry.kind=="action")throw new ArgumentException();
  if(entry.kind=="uwp"){if(entry.path==null||!entry.path.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase)||entry.path.Length<=17||entry.path.IndexOfAny(new[]{'"','\r','\n','\0'})>=0)throw new ArgumentException();DockBackend.OpenApplication(entry.path,"",entry.name);}
  else {if(!File.Exists(entry.path)&&!Directory.Exists(entry.path))throw new FileNotFoundException();DockBackend.OpenApplication(entry.path,entry.arguments??"",entry.name);}
 }
}
