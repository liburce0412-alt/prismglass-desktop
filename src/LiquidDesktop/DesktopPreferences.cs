using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal sealed class MonitorPreferences {
 public string id="";public bool enabled=true,top=true,left=true,right=true,dock=true;public int dockOffset=18;
}
internal sealed class DesktopPreferenceState {
 public int version=1;public long revision=1;public string theme="Sky",hotkey="Ctrl+Alt+Space";
 public int glassOpacity=100,textScale=100,gridColumns=6,dockIconSize=48;
 public bool manualPaused,reducedMotion,pauseOnBattery,pauseOnPowerSaver=true,pauseOnRemote=true,showLabels=true,dockAutoHide=true,welcomeShown;
 public List<MonitorPreferences> monitors=new List<MonitorPreferences>();
}

// All settings writers share this lock; unrelated shelf data is merged from its latest file.
internal static class DesktopSettingsFiles {
 public static void Locked(Action action){using(var gate=new Mutex(false,"Local\\PrismGlassDesktopSettings")){bool held=false;try{try{held=gate.WaitOne(3000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("设置正在由另一窗口保存，请稍后重试。");action();}finally{if(held)gate.ReleaseMutex();}}}
 public static string Read(string file){if(!File.Exists(file))return null;if(new FileInfo(file).Length>1048576)throw new IOException("设置文件过大，未覆盖原文件。");using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream,Encoding.UTF8,true))return reader.ReadToEnd();}
 public static Dictionary<string,object> Map(string text){if(String.IsNullOrWhiteSpace(text))return new Dictionary<string,object>();var value=new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string,object>;if(value==null)throw new IOException("设置格式无效，未覆盖原文件。");return value;}
 public static void Write(string file,string text){string pending=file+".pending-"+Guid.NewGuid().ToString("N");try{File.WriteAllText(pending,text,new UTF8Encoding(false));if(File.Exists(file))File.Replace(pending,file,null);else File.Move(pending,file);}finally{if(File.Exists(pending))File.Delete(pending);}}
 public static void Commit(Dictionary<string,string> next,Dictionary<string,string> before){
  foreach(var pair in before)if(Read(pair.Key)!=pair.Value)throw new IOException("设置已在另一个窗口更新，请重新载入后保存。");
  var applied=new List<string>();try{foreach(var pair in next){Write(pair.Key,pair.Value);applied.Add(pair.Key);}}
  catch{foreach(var file in applied){try{if(before[file]==null)File.Delete(file);else Write(file,before[file]);}catch{}}throw;}
 }
}

internal sealed class DesktopPreferences {
 readonly string root,file;readonly JavaScriptSerializer json=new JavaScriptSerializer();string observed="",loadError="";
 static readonly string[] RightCards={"quota","desktop","notes","tasks","countdown","links","calculator"};
 static readonly string[] LeftCards={"network","Weather","Music","Focus","Folders","System","Tools","Life"};
 public DesktopPreferenceState Current {get;private set;}
 public long Revision {get;private set;}
 public bool IsFirstRun {get;private set;}
 public event Action Changed;
 // Register a new binding before persistence; returning an action lets a failed save restore the old one.
 public Func<string,Action> PrepareHotkey;
 public DesktopPreferences(string directory){root=directory;file=Path.Combine(root,"desktop-settings.json");IsFirstRun=!File.Exists(file)&&!File.Exists(Path.Combine(root,"app-library.json"))&&!File.Exists(Path.Combine(root,"shelf-state.json"))&&!File.Exists(Path.Combine(root,"lyrics-settings.json"));Current=new DesktopPreferenceState();Reload();}
 static int Number(object value,int min,int max){int result;if(value is bool||!Int32.TryParse(Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture),out result)||result<min||result>max)throw new ArgumentException("数值必须介于 "+min+" 与 "+max+" 之间。");return result;}
 static bool Flag(object value){if(!(value is bool))throw new ArgumentException("开关值无效。");return (bool)value;}
 static List<string> Cards(object value,string[] allowed){var values=value as IEnumerable;if(values==null||value is string)throw new ArgumentException("组件列表无效。");var result=new List<string>();foreach(var raw in values){string name=raw as string;if(name==null||Array.IndexOf(allowed,name)<0)throw new ArgumentException("未知组件。");if(!result.Contains(name))result.Add(name);}return result;}
 static List<string> Order(object value,string[] allowed){var items=value as IEnumerable;if(items==null||value is string)throw new ArgumentException("组件顺序无效。");var result=new List<string>();foreach(var raw in items){var name=raw as string;if(name==null||Array.IndexOf(allowed,name)<0||result.Contains(name))throw new ArgumentException("组件顺序必须完整且不能重复。");result.Add(name);}if(result.Count==0)return new List<string>(allowed);if(result.Count!=allowed.Length)throw new ArgumentException("组件顺序必须包含全部组件。");return result;}
 static object Value(Dictionary<string,object> map,string key,object fallback){object value;return map.TryGetValue(key,out value)?value:fallback;}
 string Source(){return (DesktopSettingsFiles.Read(file)??"")+"\n"+(DesktopSettingsFiles.Read(Path.Combine(root,"theme.txt"))??"")+"\n"+(DesktopSettingsFiles.Read(Path.Combine(root,"paused.txt"))??"")+"\n"+(DesktopSettingsFiles.Read(Path.Combine(root,"shelf-state.json"))??"")+"\n"+(DesktopSettingsFiles.Read(Path.Combine(root,"sidebar-visibility.json"))??"");}
 public void Reload(){
  try{string signature=Source();if(signature==observed&&Revision!=0&&loadError.Length==0)return;var text=DesktopSettingsFiles.Read(file);var next=text==null?new DesktopPreferenceState():json.Deserialize<DesktopPreferenceState>(text);if(next==null||next.version!=1)throw new IOException("桌面设置版本或格式不受支持，原文件已保留。");Validate(next);ReadSidecar("shelf-state.json");ReadSidecar("sidebar-visibility.json");
   string theme=DesktopSettingsFiles.Read(Path.Combine(root,"theme.txt")),motion=DesktopSettingsFiles.Read(Path.Combine(root,"paused.txt"));if(theme!=null)next.theme=theme.Trim()=="Astro"?"Astro":"Sky";if(motion!=null)next.manualPaused=motion.Trim()=="true";
   Current=next;Revision=Math.Max(Revision+1,next.revision);observed=signature;loadError="";
  }catch(Exception ex){loadError=ex.Message;if(Revision==0)Revision=1;}
 }
 static void Validate(DesktopPreferenceState value){
  if(value.theme!="Sky"&&value.theme!="Astro")throw new ArgumentException("未知主题。");Number(value.glassOpacity,60,100);Number(value.textScale,90,130);Number(value.gridColumns,4,10);Number(value.dockIconSize,32,72);HotkeyBinding.Parse(value.hotkey);
  if(value.monitors==null)value.monitors=new List<MonitorPreferences>();if(value.monitors.Count>32)throw new ArgumentException("显示器配置过多。");var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var monitor in value.monitors){if(monitor==null||String.IsNullOrWhiteSpace(monitor.id)||monitor.id.Length>512||!seen.Add(monitor.id))throw new ArgumentException("显示器标识无效或重复。");Number(monitor.dockOffset,0,200);}
 }
 Dictionary<string,object> ReadSidecar(string name){var map=DesktopSettingsFiles.Map(DesktopSettingsFiles.Read(Path.Combine(root,name)));bool right=name=="shelf-state.json";string hidden=right?"hiddenCards":"hiddenLeftCards",order=right?"cardOrder":"leftCardOrder";object raw;if(map.TryGetValue(hidden,out raw)&&raw!=null)Cards(raw,right?RightCards:LeftCards);if(map.TryGetValue(order,out raw)&&raw!=null)Order(raw,right?RightCards:LeftCards);return map;}
 Dictionary<string,object> SnapshotSidecar(string name){try{return ReadSidecar(name);}catch(Exception ex){loadError=ex.Message;return new Dictionary<string,object>();}}
 public MonitorPreferences ForMonitor(string id,bool primary){var match=Current.monitors.Find(item=>String.Equals(item.id,id,StringComparison.OrdinalIgnoreCase));return match??new MonitorPreferences{id=id,enabled=primary};}
 public object Snapshot(){Reload();var result=DesktopSettingsFiles.Map(json.Serialize(Current));result["revision"]=Revision;var shelf=SnapshotSidecar("shelf-state.json");var left=SnapshotSidecar("sidebar-visibility.json");result["error"]=loadError;result["hiddenCards"]=Value(shelf,"hiddenCards",new string[0]);result["hiddenLeftCards"]=Value(left,"hiddenLeftCards",new string[0]);result["cardOrder"]=Value(shelf,"cardOrder",RightCards);result["leftCardOrder"]=Value(left,"leftCardOrder",LeftCards);
  var displays=new List<object>();var connected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var monitor in MonitorCatalog.Read()){connected.Add(monitor.Id);var entry=DesktopSettingsFiles.Map(json.Serialize(ForMonitor(monitor.Id,monitor.Primary)));entry["name"]=monitor.Name;entry["connected"]=true;entry["primary"]=monitor.Primary;entry["bounds"]=new{x=monitor.Screen.Bounds.X,y=monitor.Screen.Bounds.Y,width=monitor.Screen.Bounds.Width,height=monitor.Screen.Bounds.Height};displays.Add(entry);}foreach(var monitor in Current.monitors)if(!connected.Contains(monitor.id)){var entry=DesktopSettingsFiles.Map(json.Serialize(monitor));entry["name"]="未连接的显示器";entry["connected"]=false;entry["primary"]=false;displays.Add(entry);}result["monitors"]=displays;return result;
 }
 public void Apply(Dictionary<string,object> changes,long expectedRevision){
  if(changes==null||changes.Count==0)return;DesktopSettingsFiles.Locked(()=>{
   Reload();if(loadError.Length>0)throw new IOException(loadError);if(expectedRevision!=Revision)throw new IOException("设置已在其他入口更新，请重新载入；当前编辑尚未覆盖。");
   var next=json.Deserialize<DesktopPreferenceState>(json.Serialize(Current));var writes=new Dictionary<string,string>();var previous=new Dictionary<string,string>();
   foreach(var pair in changes){switch(pair.Key){
    case "theme":next.theme=pair.Value as string;break;case "hotkey":next.hotkey=pair.Value as string;break;
    case "glassOpacity":next.glassOpacity=Number(pair.Value,60,100);break;case "textScale":next.textScale=Number(pair.Value,90,130);break;case "gridColumns":next.gridColumns=Number(pair.Value,4,10);break;case "dockIconSize":next.dockIconSize=Number(pair.Value,32,72);break;
    case "manualPaused":next.manualPaused=Flag(pair.Value);break;case "reducedMotion":next.reducedMotion=Flag(pair.Value);break;case "pauseOnBattery":next.pauseOnBattery=Flag(pair.Value);break;case "pauseOnPowerSaver":next.pauseOnPowerSaver=Flag(pair.Value);break;case "pauseOnRemote":next.pauseOnRemote=Flag(pair.Value);break;case "showLabels":next.showLabels=Flag(pair.Value);break;case "dockAutoHide":next.dockAutoHide=Flag(pair.Value);break;
    case "monitors":next.monitors=json.Deserialize<List<MonitorPreferences>>(json.Serialize(pair.Value));break;
    case "welcomeShown":next.welcomeShown=Flag(pair.Value);break;
    case "hiddenCards":case "hiddenLeftCards":case "cardOrder":case "leftCardOrder":bool right=pair.Key=="hiddenCards"||pair.Key=="cardOrder";string path=Path.Combine(root,right?"shelf-state.json":"sidebar-visibility.json");string staged;var map=DesktopSettingsFiles.Map(writes.TryGetValue(path,out staged)?staged:DesktopSettingsFiles.Read(path));map[pair.Key]=pair.Key.EndsWith("Order",StringComparison.Ordinal)?Order(pair.Value,right?RightCards:LeftCards):Cards(pair.Value,right?RightCards:LeftCards);if(pair.Key=="hiddenCards"&&((List<string>)map[pair.Key]).Count==RightCards.Length)throw new ArgumentException("右侧至少保留一个入口。");writes[path]=json.Serialize(map);break;
    default:throw new ArgumentException("未知设置项："+pair.Key);
   }}Validate(next);
   Action rollback=null;if(next.hotkey!=Current.hotkey&&PrepareHotkey!=null)rollback=PrepareHotkey(next.hotkey);
   try{next.revision=Revision+1;writes[file]=json.Serialize(next);if(changes.ContainsKey("theme"))writes[Path.Combine(root,"theme.txt")]=next.theme;if(changes.ContainsKey("manualPaused"))writes[Path.Combine(root,"paused.txt")]=next.manualPaused?"true":"false";foreach(var path in writes.Keys)previous[path]=DesktopSettingsFiles.Read(path);if(Source()!=observed)throw new IOException("设置刚刚更新，请重新载入后保存。");DesktopSettingsFiles.Commit(writes,previous);Current=next;Revision=next.revision;observed=Source();}
   catch{if(rollback!=null)rollback();throw;}
  });var changed=Changed;if(changed!=null)changed();
 }
}
