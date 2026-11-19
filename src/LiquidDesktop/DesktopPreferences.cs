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
internal sealed class GlassStylePreset {
 public string id="",name="",baseStyle="I";
 public Dictionary<string,double> parameters=new Dictionary<string,double>();
}
internal sealed class DesktopPreferenceState {
 public int version=1;public long revision=1;public string theme="Sky",hotkey="Ctrl+Alt+Space";
 public string glassStyle="";
 public Dictionary<string,Dictionary<string,double>> glassOverrides=new Dictionary<string,Dictionary<string,double>>();
 public List<GlassStylePreset> glassPresets=new List<GlassStylePreset>();
 public int glassOpacity=100,textScale=100,gridColumns=6,dockIconSize=48;
 public bool liveGlassBackground,manualPaused,reducedMotion,pauseOnBattery,pauseOnPowerSaver=true,pauseOnRemote=true,showLabels=true,dockAutoHide=true,welcomeShown;
 public bool topAutoHide=true,leftAutoHide=true,rightAutoHide=true,windowAnimations=true,stageManager,layoutLocked;
 public bool stageHideCovered=true;
 public string stageGroupLayout="auto",stageSingleSize="fit";
 public int stageMaxWidth=85,stageMaxHeight=85;
 public List<string> autoHideWidgets=new List<string>();
 public List<MonitorPreferences> monitors=new List<MonitorPreferences>();
 public List<string> desktopWidgets=new List<string>();
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
 static readonly string[] RightCards={"quota","notes","tasks","countdown","links","calculator"};
 static readonly string[] LeftCards={"network","Weather","Music","Focus","Folders","System","Tools","Life"};
 static readonly string[] DesktopCards={"Weather","System","Clock","Calendar","Battery","Folders","Music","Focus","Tools","BreathingOrb","DigitalClock","Moon","Hourglass","SunPath","YearDots","Tide","Orrery","Bonsai","Photo","Photo2","Photo3","Photo4","Photo5","Photo6"};
 static readonly Dictionary<string,double[]> GlassRanges=new Dictionary<string,double[]>{
  {"refraction",new[]{0.0,2.5}},{"bevel",new[]{2.0,50.0}},{"ior",new[]{1.05,2.0}},{"dispersion",new[]{0.0,.3}},
  {"blur",new[]{0.0,30.0}},{"diffusion",new[]{0.0,1.0}},{"saturation",new[]{0.0,4.0}},{"brightness",new[]{.25,2.5}},{"contrast",new[]{.25,2.5}},
  {"tintOpacity",new[]{0.0,.5}},{"tintR",new[]{0.0,1.0}},{"tintG",new[]{0.0,1.0}},{"tintB",new[]{0.0,1.0}},
  {"reflection",new[]{0.0,.6}},{"highlight",new[]{0.0,.4}},{"shadow",new[]{0.0,.3}},{"grain",new[]{0.0,.06}},{"edge",new[]{0.0,2.5}},{"power",new[]{1.0,6.0}},
  {"opacity",new[]{0.0,1.0}},{"adaptive",new[]{0.0,1.0}},{"density",new[]{0.0,1.0}},{"tintMin",new[]{0.0,.5}},{"tintMax",new[]{0.0,1.0}}
 };
 static bool GlassKey(string key){return key!=null&&key.Length==1&&key[0]>='A'&&key[0]<='J';}
 static bool CustomGlassKey(string key){return key!=null&&System.Text.RegularExpressions.Regex.IsMatch(key,"^user-[a-f0-9]{32}$");}
 static Dictionary<string,double> GlassParameters(object value){
  var fields=value as IDictionary;if(fields==null||fields.Count>GlassRanges.Count)throw new ArgumentException("未知玻璃参数。");var next=new Dictionary<string,double>();
  foreach(DictionaryEntry field in fields){double[] range;string name=field.Key as string;var raw=field.Value;if(name==null||!GlassRanges.TryGetValue(name,out range)||!(raw is int||raw is long||raw is double||raw is decimal||raw is float))throw new ArgumentException("玻璃参数必须是有效数值。");double number=Convert.ToDouble(raw);if(Double.IsNaN(number)||Double.IsInfinity(number)||number<range[0]||number>range[1])throw new ArgumentException("玻璃参数超出支持范围："+name);next.Add(name,number);}return next;
 }
 static Dictionary<string,Dictionary<string,double>> GlassOverrides(object value){
  var map=value as IDictionary;if(map==null||map.Count>10)throw new ArgumentException("玻璃参数必须是按材质保存的列表。");
  var result=new Dictionary<string,Dictionary<string,double>>();
  foreach(DictionaryEntry entry in map){string key=entry.Key as string;if(!GlassKey(key))throw new ArgumentException("未知玻璃材质。");var next=GlassParameters(entry.Value);
   if(next.Count>0)result.Add(key,next);
  }return result;
 }
 static List<GlassStylePreset> GlassPresets(object value){
  var items=value as IEnumerable;if(items==null||value is string||value is IDictionary)throw new ArgumentException("自定义风格列表无效。");var result=new List<GlassStylePreset>();var ids=new HashSet<string>();
  foreach(var raw in items){if(result.Count>=64)throw new ArgumentException("最多保存 64 个自定义风格。");var fields=raw as IDictionary;if(fields==null||fields.Count!=4||!fields.Contains("id")||!fields.Contains("name")||!fields.Contains("baseStyle")||!fields.Contains("parameters"))throw new ArgumentException("自定义风格内容不完整。");string id=fields["id"] as string,name=fields["name"] as string,basis=fields["baseStyle"] as string;if(!CustomGlassKey(id)||!ids.Add(id)||!GlassKey(basis)||String.IsNullOrWhiteSpace(name)||name.Length>48||Array.Exists(name.ToCharArray(),Char.IsControl))throw new ArgumentException("自定义风格名称或标识无效。");result.Add(new GlassStylePreset{id=id,name=name.Trim(),baseStyle=basis,parameters=GlassParameters(fields["parameters"])});
  }return result;
 }
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
  try{string signature=Source();if(signature==observed&&Revision!=0&&loadError.Length==0)return;var text=DesktopSettingsFiles.Read(file);var next=text==null?new DesktopPreferenceState():json.Deserialize<DesktopPreferenceState>(text);if(next==null||next.version!=1)throw new IOException("桌面设置版本或格式不受支持，原文件已保留。");if(text!=null){object overrides;var savedMap=DesktopSettingsFiles.Map(text);if(savedMap.TryGetValue("glassOverrides",out overrides))next.glassOverrides=GlassOverrides(overrides);if(savedMap.TryGetValue("glassPresets",out overrides))next.glassPresets=GlassPresets(overrides);}Validate(next);ReadSidecar("shelf-state.json");ReadSidecar("sidebar-visibility.json");
   string theme=DesktopSettingsFiles.Read(Path.Combine(root,"theme.txt")),motion=DesktopSettingsFiles.Read(Path.Combine(root,"paused.txt"));if(theme!=null)next.theme=theme.Trim()=="Astro"?"Astro":"Sky";if(motion!=null)next.manualPaused=motion.Trim()=="true";
   Current=next;Revision=Math.Max(Revision+1,next.revision);observed=signature;loadError="";
  }catch(Exception ex){loadError=ex.Message;if(Revision==0)Revision=1;}
 }
 static void Validate(DesktopPreferenceState value){
  if(Array.IndexOf(new[]{"auto","columns","grid","preserve"},value.stageGroupLayout)<0||Array.IndexOf(new[]{"fit","remember"},value.stageSingleSize)<0)throw new ArgumentException("未知台前调度布局。");Number(value.stageMaxWidth,50,100);Number(value.stageMaxHeight,50,100);
  // Retired decorative cards are ignored on read without rewriting saved user files.
  foreach(var cards in new[]{value.desktopWidgets,value.autoHideWidgets})if(cards!=null)cards.RemoveAll(name=>name=="Starlight"||name=="MoodNote");
  value.desktopWidgets=Cards(value.desktopWidgets??new List<string>(),DesktopCards);
  value.autoHideWidgets=Cards(value.autoHideWidgets??new List<string>(),DesktopCards);
  if(value.glassPresets==null||value.glassPresets.Count>64)throw new ArgumentException("自定义风格列表无效。");var presetIds=new HashSet<string>();
  foreach(var preset in value.glassPresets){if(preset==null||!CustomGlassKey(preset.id)||!presetIds.Add(preset.id)||!GlassKey(preset.baseStyle)||String.IsNullOrWhiteSpace(preset.name)||preset.name.Length>48||Array.Exists(preset.name.ToCharArray(),Char.IsControl))throw new ArgumentException("自定义风格无效。");preset.name=preset.name.Trim();preset.parameters=GlassParameters(preset.parameters);}
  if(value.glassStyle==null||value.glassStyle!=""&&!GlassKey(value.glassStyle)&&!presetIds.Contains(value.glassStyle))throw new ArgumentException("未知玻璃材质。");
  value.glassOverrides=GlassOverrides(value.glassOverrides);
  if(value.theme!="Sky"&&value.theme!="Astro")throw new ArgumentException("未知主题。");Number(value.glassOpacity,60,100);Number(value.textScale,90,130);Number(value.gridColumns,4,10);Number(value.dockIconSize,32,72);HotkeyBinding.Parse(value.hotkey);
  if(value.monitors==null)value.monitors=new List<MonitorPreferences>();if(value.monitors.Count>32)throw new ArgumentException("显示器配置过多。");var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var monitor in value.monitors){if(monitor==null||String.IsNullOrWhiteSpace(monitor.id)||monitor.id.Length>512||!seen.Add(monitor.id))throw new ArgumentException("显示器标识无效或重复。");Number(monitor.dockOffset,0,200);}
 }
 internal static Dictionary<string,object> ReadShelfState(string text){
  var map=DesktopSettingsFiles.Map(text);foreach(string key in new[]{"hiddenCards","cardOrder"}){object raw;if(!map.TryGetValue(key,out raw)||raw is string||!(raw is IEnumerable))continue;var items=new List<object>();foreach(var item in (IEnumerable)raw)if(!Object.Equals(item,"desktop"))items.Add(item);
   // Retiring the only visible legacy card must not leave an empty rail; reading never rewrites user data.
   if(key=="hiddenCards"&&Array.TrueForAll(RightCards,name=>items.Contains(name)))items.Remove("quota");map[key]=items;
  }return map;
 }
 Dictionary<string,object> ReadSidecar(string name){bool right=name=="shelf-state.json";string text=DesktopSettingsFiles.Read(Path.Combine(root,name));var map=right?ReadShelfState(text):DesktopSettingsFiles.Map(text);string hidden=right?"hiddenCards":"hiddenLeftCards",order=right?"cardOrder":"leftCardOrder";object raw;if(map.TryGetValue(hidden,out raw)&&raw!=null)Cards(raw,right?RightCards:LeftCards);if(map.TryGetValue(order,out raw)&&raw!=null)Order(raw,right?RightCards:LeftCards);return map;}
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
    case "glassStyle":next.glassStyle=pair.Value as string;break;
    case "glassOverrides":next.glassOverrides=GlassOverrides(pair.Value);break;
    case "glassPresets":next.glassPresets=GlassPresets(pair.Value);break;
    case "layoutLocked":next.layoutLocked=Flag(pair.Value);break;
    case "liveGlassBackground":next.liveGlassBackground=Flag(pair.Value);break;
    case "topAutoHide":next.topAutoHide=Flag(pair.Value);break;case "leftAutoHide":next.leftAutoHide=Flag(pair.Value);break;case "rightAutoHide":next.rightAutoHide=Flag(pair.Value);break;
    case "windowAnimations":next.windowAnimations=Flag(pair.Value);break;case "stageManager":next.stageManager=Flag(pair.Value);break;
    case "stageHideCovered":next.stageHideCovered=Flag(pair.Value);break;
    case "stageGroupLayout":next.stageGroupLayout=pair.Value as string;break;case "stageSingleSize":next.stageSingleSize=pair.Value as string;break;
    case "stageMaxWidth":next.stageMaxWidth=Number(pair.Value,50,100);break;case "stageMaxHeight":next.stageMaxHeight=Number(pair.Value,50,100);break;
    case "autoHideWidgets":next.autoHideWidgets=Cards(pair.Value,DesktopCards);break;
    case "glassOpacity":next.glassOpacity=Number(pair.Value,60,100);break;case "textScale":next.textScale=Number(pair.Value,90,130);break;case "gridColumns":next.gridColumns=Number(pair.Value,4,10);break;case "dockIconSize":next.dockIconSize=Number(pair.Value,32,72);break;
    case "manualPaused":next.manualPaused=Flag(pair.Value);break;case "reducedMotion":next.reducedMotion=Flag(pair.Value);break;case "pauseOnBattery":next.pauseOnBattery=Flag(pair.Value);break;case "pauseOnPowerSaver":next.pauseOnPowerSaver=Flag(pair.Value);break;case "pauseOnRemote":next.pauseOnRemote=Flag(pair.Value);break;case "showLabels":next.showLabels=Flag(pair.Value);break;case "dockAutoHide":next.dockAutoHide=Flag(pair.Value);break;
    case "monitors":next.monitors=json.Deserialize<List<MonitorPreferences>>(json.Serialize(pair.Value));break;
    case "welcomeShown":next.welcomeShown=Flag(pair.Value);break;
    case "desktopWidgets":next.desktopWidgets=Cards(pair.Value,DesktopCards);break;
    case "hiddenCards":case "hiddenLeftCards":case "cardOrder":case "leftCardOrder":bool right=pair.Key=="hiddenCards"||pair.Key=="cardOrder";string path=Path.Combine(root,right?"shelf-state.json":"sidebar-visibility.json");string staged;var map=DesktopSettingsFiles.Map(writes.TryGetValue(path,out staged)?staged:DesktopSettingsFiles.Read(path));map[pair.Key]=pair.Key.EndsWith("Order",StringComparison.Ordinal)?Order(pair.Value,right?RightCards:LeftCards):Cards(pair.Value,right?RightCards:LeftCards);if(pair.Key=="hiddenCards"&&((List<string>)map[pair.Key]).Count==RightCards.Length)throw new ArgumentException("右侧至少保留一个入口。");writes[path]=json.Serialize(map);break;
    default:throw new ArgumentException("未知设置项："+pair.Key);
   }}Validate(next);
   Action rollback=null;if(next.hotkey!=Current.hotkey&&PrepareHotkey!=null)rollback=PrepareHotkey(next.hotkey);
   try{next.revision=Revision+1;writes[file]=json.Serialize(next);if(changes.ContainsKey("theme"))writes[Path.Combine(root,"theme.txt")]=next.theme;if(changes.ContainsKey("manualPaused"))writes[Path.Combine(root,"paused.txt")]=next.manualPaused?"true":"false";foreach(var path in writes.Keys)previous[path]=DesktopSettingsFiles.Read(path);if(Source()!=observed)throw new IOException("设置刚刚更新，请重新载入后保存。");DesktopSettingsFiles.Commit(writes,previous);Current=next;Revision=next.revision;observed=Source();}
   catch{if(rollback!=null)rollback();throw;}
  });var changed=Changed;if(changed!=null)changed();
 }
}
