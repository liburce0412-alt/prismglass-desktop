using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using Microsoft.Win32;

internal sealed class CompanionPhysicalMonitor {public int Id;public string Name,DeviceName,Identity;public Rectangle Bounds;public bool Primary;}

// Only the explicitly supported MttVDD target is managed; physical paths are copied verbatim.
internal sealed class CompanionVirtualDisplay {
 readonly string journal;readonly CompanionVirtualDevice device;readonly object gate=new object();bool owned;CompanionDisplayTopology.DisplayPath extensionTarget;
 internal CompanionVirtualDisplay(string root){journal=Path.Combine(root,"runtime-data","companion","virtual-display-icons.json");device=new CompanionVirtualDevice(root);}
 internal bool Available {get{try{if(device.Managed&&device.Disabled)return true;var all=CompanionDisplayTopology.Read(1);CompanionDisplayTopology.FindVirtual(all);return true;}catch{return false;}}}
 internal string UnavailableReason {get{try{if(device.Managed&&device.Disabled)return "";CompanionDisplayTopology.FindVirtual(CompanionDisplayTopology.Read(1));return "";}catch(InvalidOperationException ex){return ex.Message;}catch{return "无法读取显示器状态，请在电脑检查虚拟显示驱动。";}}}
 internal List<CompanionPhysicalMonitor> PhysicalMonitors(){return CompanionDisplayTopology.PhysicalMonitors(CompanionDisplayTopology.Read(2));}
 internal Rectangle BeginExtension(bool portrait=false){lock(gate){
  if(owned)throw new InvalidOperationException("副屏已在使用，请先关闭原来的副屏。");
  RecoverIdle();string physical=CompanionDisplayTopology.Fingerprint(CompanionDisplayTopology.Read(2),CompanionDisplayTopology.IsVirtual);SaveIcons();owned=true;
  try{
   device.SetEnabled(true);
   var current=CompanionDisplayTopology.Read(1);
   if(device.Managed)for(int i=0;i<50&&!current.paths.Any(path=>path.target.available&&CompanionDisplayTopology.IsVirtual(path));i++){Thread.Sleep(100);current=CompanionDisplayTopology.Read(1);}
   if(CompanionDisplayTopology.Fingerprint(CompanionDisplayTopology.Read(2),CompanionDisplayTopology.IsVirtual)!=physical)throw new InvalidOperationException("实体屏幕配置已变化，副屏启动已取消。");
   var target=CompanionDisplayTopology.FindVirtual(current);extensionTarget=target;
   var preferred=CompanionDisplayTopology.Preferred(target.target);
   bool rotate=portrait!=(preferred.height>preferred.width)&&CompanionDisplayTopology.SupportsQuarterTurn(preferred,CompanionDisplayTopology.SupportedModes(target));
   var plan=CompanionDisplayTopology.EnableValidated(current,target,preferred,rotate);
   CompanionDisplayTopology.ApplyChecked(current,plan,false);RestoreIcons(false);return CompanionDisplayTopology.WaitVirtualBounds(target,true);
  }
  catch{try{StopExtension();}catch{throw new InvalidOperationException("副屏启动失败，自动恢复也未完成；请在电脑关闭副屏后重试。");}throw;}
 }}
 internal bool ExtensionMatches(Rectangle bounds){lock(gate){if(!owned)return false;var current=CompanionDisplayTopology.Read(2);var active=CompanionDisplayTopology.ActiveVirtual(current);return active.Count==1&&CompanionDisplayTopology.SameCaptureTarget(extensionTarget,bounds,current,active[0]);}}
 internal void StopExtension(){lock(gate){if(!owned&&!File.Exists(journal)&&!device.Managed)return;Disable();owned=false;}}
 internal void RecoverIdle(){lock(gate){Disable();owned=false;}}
 void Disable(){
  var current=CompanionDisplayTopology.Read(2);var selected=CompanionDisplayTopology.ActiveVirtual(current);
  if(selected.Count>1)throw new InvalidOperationException("检测到多个虚拟屏，请先在电脑保留一个 iPad 虚拟屏。");
  if(selected.Count==1){
   if(!File.Exists(journal))SaveIcons();
   var plan=CompanionDisplayTopology.Disable(current,selected[0]);
   CompanionDisplayTopology.ApplyChecked(current,plan,true);CompanionDisplayTopology.WaitVirtualBounds(selected[0],false);
  }
  if(device.Managed&&!device.Disabled){
   current=CompanionDisplayTopology.Read(2);
   if(CompanionDisplayTopology.PhysicalMonitors(current).Count==0)throw new InvalidOperationException("没有活动的实体屏幕，不能停用虚拟显示设备。");
   if(!File.Exists(journal))SaveIcons();
   string expected=CompanionDisplayTopology.Fingerprint(current,CompanionDisplayTopology.IsVirtual);
   device.SetEnabled(false);
   if(CompanionDisplayTopology.Fingerprint(CompanionDisplayTopology.Read(2),CompanionDisplayTopology.IsVirtual)!=expected)throw new InvalidOperationException("实体屏幕配置已变化，已保留桌面位置记录。");
  }
  RestoreIcons(true);
 }
 static T DesktopCall<T>(Func<T> action){
  if(Thread.CurrentThread.GetApartmentState()==ApartmentState.STA)return action();T result=default(T);Exception error=null;
  var thread=new Thread(()=>{try{result=action();}catch(Exception ex){error=ex;}}){IsBackground=true};thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)throw new InvalidOperationException("无法保存或恢复桌面图标位置。",error);return result;
 }
 void SaveIcons(){
  var positions=DesktopCall(()=>DesktopIcons.CapturePositions());
  Directory.CreateDirectory(Path.GetDirectoryName(journal));string text=new JavaScriptSerializer().Serialize(positions);
  if(Encoding.UTF8.GetByteCount(text)>1048576)throw new InvalidOperationException("桌面图标数量过多，副屏尚未开启。");
  using(var file=new FileStream(journal,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(file)){writer.Write(text);}
 }
 void RestoreIcons(bool remove){
  if(!File.Exists(journal))return;if(new FileInfo(journal).Length>1048576)throw new InvalidOperationException("桌面位置恢复记录过大，已保留供检查。");
  var positions=new JavaScriptSerializer().Deserialize<DesktopIcons.Position[]>(File.ReadAllText(journal));
  var bounds=PhysicalMonitors().Select(item=>item.Bounds).ToArray();
  positions=VisiblePositions(positions,bounds);
  // Explorer can apply its own layout after the display notification has returned.
  DesktopCall(()=>{Thread.Sleep(250);DesktopIcons.RestorePositions(positions);Thread.Sleep(250);DesktopIcons.RestorePositions(positions);
   if((DesktopIcons.CurrentFlags()&1)==0){var current=DesktopIcons.CapturePositions().ToDictionary(item=>item.path,StringComparer.OrdinalIgnoreCase);foreach(var position in positions){DesktopIcons.Position now;if(current.TryGetValue(position.path,out now)&&(now.x!=position.x||now.y!=position.y))throw new InvalidOperationException("桌面图标位置尚未恢复，已保留恢复记录。");}}return true;});
  if(remove)File.Delete(journal);
 }
 internal static DesktopIcons.Position[] VisiblePositions(DesktopIcons.Position[] values,Rectangle[] bounds){
  if(values==null||values.Length>10000)throw new InvalidOperationException("桌面位置恢复记录无效。");
  return values.Where(item=>item!=null&&!String.IsNullOrEmpty(item.path)&&item.path.Length<=32768&&bounds.Any(area=>area.Contains(item.x,item.y))).ToArray();
 }
}

// The elevated tasks run only Windows' protected PnPUtil, with a fixed MttVDD hardware ID.
internal sealed class CompanionVirtualDevice {
 const string HardwareId=@"Root\MttVDD";
 readonly string marker;readonly string sid=WindowsIdentity.GetCurrent().User.Value;
 internal CompanionVirtualDevice(string root){marker=Path.Combine(root,"runtime-data","companion","virtual-device-managed.json");}
 internal bool Managed {get{
  if(!File.Exists(marker))return false;
  if(new FileInfo(marker).Length>4096)throw new InvalidOperationException("虚拟屏开关配置无效。");
  var record=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(marker));
  object user,hardware;return record!=null&&record.TryGetValue("userSid",out user)&&String.Equals(user as string,sid,StringComparison.Ordinal)&&record.TryGetValue("hardwareId",out hardware)&&String.Equals(hardware as string,HardwareId,StringComparison.OrdinalIgnoreCase);
 }}
 [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)]static extern uint CM_Locate_DevNodeW(out uint node,string id,uint flags);
 [DllImport("cfgmgr32.dll")]static extern uint CM_Get_DevNode_Status(out uint status,out uint problem,uint node,uint flags);
 internal bool Disabled {get{
  var matches=new List<string>();using(var devices=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ROOT\DISPLAY")){
   if(devices!=null)foreach(string name in devices.GetSubKeyNames())using(var device=devices.OpenSubKey(name)){
    var ids=device==null?null:device.GetValue("HardwareID") as string[];
    if(ids!=null&&ids.Any(id=>String.Equals(id,HardwareId,StringComparison.OrdinalIgnoreCase)))matches.Add(@"ROOT\DISPLAY\"+name);
   }
  }
  if(matches.Count!=1)throw new InvalidOperationException("需要恰好一个 MttVDD 虚拟显示设备，未修改其他显示驱动。");
  uint node,status,problem;if(CM_Locate_DevNodeW(out node,matches[0],0)!=0||CM_Get_DevNode_Status(out status,out problem,node,0)!=0)throw new InvalidOperationException("虚拟显示设备正在变化，请稍后重试。");
  if(problem!=0&&problem!=22)throw new InvalidOperationException("虚拟显示设备报告错误："+problem);
  return problem==22;
 }}
 internal static string TaskName(string user,bool enabled){return "PrismGlass-Vdd-"+(enabled?"Enable-":"Disable-")+new SecurityIdentifier(user).Value;}
 internal static string Arguments(bool enabled){return (enabled?"/enable-device":"/disable-device")+" /deviceid \""+HardwareId+"\"";}
 internal static bool ValidTask(string xml,bool enabled){
  if(String.IsNullOrEmpty(xml))return false;
  try{var document=new XmlDocument{XmlResolver=null};using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=65536}))document.Load(reader);
   var namespaces=new XmlNamespaceManager(document.NameTable);namespaces.AddNamespace("t","http://schemas.microsoft.com/windows/2004/02/mit/task");
   var actions=document.SelectNodes("/t:Task/t:Actions/*",namespaces);var user=document.SelectSingleNode("/t:Task/t:Principals/t:Principal/t:UserId",namespaces);
   if(actions.Count!=1||actions[0].LocalName!="Exec"||user==null||user.InnerText!="S-1-5-18"||document.SelectNodes("/t:Task/t:Triggers/*",namespaces).Count!=0)return false;
   var command=actions[0].SelectSingleNode("t:Command",namespaces);var arguments=actions[0].SelectSingleNode("t:Arguments",namespaces);
   return command!=null&&arguments!=null&&String.Equals(command.InnerText,Path.Combine(Environment.SystemDirectory,"pnputil.exe"),StringComparison.OrdinalIgnoreCase)&&arguments.InnerText==Arguments(enabled);
  }catch(XmlException){return false;}
 }
 static object Call(object target,string name,params object[] args){return target.GetType().InvokeMember(name,BindingFlags.InvokeMethod,null,target,args);}
 static object Get(object target,string name){return target.GetType().InvokeMember(name,BindingFlags.GetProperty,null,target,null);}
 static void Release(object target){if(target!=null&&Marshal.IsComObject(target))Marshal.FinalReleaseComObject(target);}
 internal void SetEnabled(bool enabled){
  if(!Managed)return;
  object scheduler=null,folder=null,task=null,other=null,running=null;
  try{
   scheduler=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));Call(scheduler,"Connect");folder=Call(scheduler,"GetFolder",@"\");
   task=Call(folder,"GetTask",TaskName(sid,enabled));other=Call(folder,"GetTask",TaskName(sid,!enabled));
   if(!ValidTask((string)Get(task,"Xml"),enabled)||!ValidTask((string)Get(other,"Xml"),!enabled))throw new InvalidOperationException("虚拟屏管理任务不符合固定设备范围，请在电脑重新配置。");
   var deadline=DateTime.UtcNow.AddSeconds(20);
   while(Convert.ToInt32(Get(other,"State"))==2||Convert.ToInt32(Get(other,"State"))==4){if(DateTime.UtcNow>=deadline)throw new TimeoutException("上一次虚拟屏切换尚未完成。");Thread.Sleep(100);}
   if(Disabled==!enabled)return;
   running=Call(task,"Run",new object[]{null});
   while(Convert.ToInt32(Get(running,"State"))==2||Convert.ToInt32(Get(running,"State"))==4){if(DateTime.UtcNow>=deadline)throw new TimeoutException("虚拟屏开关执行超时，请在电脑检查。");Thread.Sleep(100);Call(running,"Refresh");}
   int result=Convert.ToInt32(Get(task,"LastTaskResult"));if(result!=0)throw new InvalidOperationException("Windows 未完成虚拟屏切换，错误码："+result);
   while(Disabled==enabled){if(DateTime.UtcNow>=deadline)throw new TimeoutException("虚拟显示设备尚未就绪。");Thread.Sleep(100);}
  }catch(TargetInvocationException ex){throw new InvalidOperationException("虚拟屏管理任务不可用，请在电脑重新配置。",ex.InnerException);}
  finally{Release(running);Release(other);Release(task);Release(folder);Release(scheduler);}
 }
}

internal static class CompanionDisplayTopology {
 [StructLayout(LayoutKind.Sequential)]internal struct Luid {public uint low;public int high;public override string ToString(){return high+":"+low;}}
 [StructLayout(LayoutKind.Sequential)]internal struct Rational {public uint numerator,denominator;}
 [StructLayout(LayoutKind.Sequential)]internal struct Source {public Luid adapter;public uint id,mode,status;}
 [StructLayout(LayoutKind.Sequential)]internal struct Target {public Luid adapter;public uint id,mode,technology,rotation,scaling;public Rational refresh;public uint scanline;[MarshalAs(UnmanagedType.Bool)]public bool available;public uint status;}
 [StructLayout(LayoutKind.Sequential)]internal struct DisplayPath {public Source source;public Target target;public uint flags;}
 [StructLayout(LayoutKind.Sequential)]internal struct Point {public int x,y;}
 [StructLayout(LayoutKind.Sequential)]internal struct SourceMode {public uint width,height,format;public Point position;}
 [StructLayout(LayoutKind.Explicit,Size=48)]internal struct Signal {[FieldOffset(0)]public ulong a;[FieldOffset(8)]public ulong b;[FieldOffset(16)]public ulong c;[FieldOffset(24)]public ulong d;[FieldOffset(32)]public ulong e;[FieldOffset(40)]public ulong f;}
 [StructLayout(LayoutKind.Explicit,Size=64)]internal struct Mode {[FieldOffset(0)]public uint type;[FieldOffset(4)]public uint id;[FieldOffset(8)]public Luid adapter;[FieldOffset(16)]public SourceMode source;[FieldOffset(16)]public Signal signal;}
 [StructLayout(LayoutKind.Sequential)]internal struct Header {public uint type,size;public Luid adapter;public uint id;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct SourceName {public Header header;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct TargetName {public Header header;public uint flags,technology;public ushort manufacturer,product;public uint connector;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string devicePath;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct AdapterName {public Header header;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string path;}
 [StructLayout(LayoutKind.Explicit,Size=80)]internal struct PreferredMode {[FieldOffset(0)]public Header header;[FieldOffset(20)]public uint width;[FieldOffset(24)]public uint height;[FieldOffset(32)]public Signal signal;}
 internal sealed class Snapshot {public DisplayPath[] paths;public Mode[] modes;}
 [StructLayout(LayoutKind.Explicit,Size=220)]internal struct DisplayMode {
  [FieldOffset(68)]public ushort size;[FieldOffset(72)]public uint fields;[FieldOffset(84)]public uint orientation;
  [FieldOffset(168)]public uint bits;[FieldOffset(172)]public uint width;[FieldOffset(176)]public uint height;
  [FieldOffset(180)]public uint flags;[FieldOffset(184)]public uint frequency;
 }
 [DllImport("user32.dll",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]static extern bool EnumDisplaySettingsEx(string device,uint index,ref DisplayMode mode,uint flags);
 internal static List<DisplayMode> SupportedModes(DisplayPath target){
  if(!IsVirtual(target))throw new InvalidOperationException("仅枚举受管理副屏的模式。");
  string device=SourceDevice(target.source);if(String.IsNullOrEmpty(device))throw new InvalidOperationException("副屏显示源不可用。");
  var result=new List<DisplayMode>();for(uint index=0;index<4096;index++){
   var mode=new DisplayMode{size=220};if(!EnumDisplaySettingsEx(device,index,ref mode,4))return result;
   if((mode.fields&0x5C0000)==0x5C0000&&mode.bits==32&&mode.width>=320&&mode.height>=320&&mode.width<=8192&&mode.height<=8192&&mode.frequency>=24&&mode.frequency<=240)result.Add(mode);
  }throw new InvalidOperationException("副屏模式数量异常。");
 }
 [DllImport("user32.dll")]static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
 [DllImport("user32.dll")]static extern int QueryDisplayConfig(uint flags,ref uint pathCount,[Out]DisplayPath[] paths,ref uint modeCount,[Out]Mode[] modes,IntPtr topology);
 [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")]static extern int SourceInfo(ref SourceName name);
 [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")]static extern int TargetInfo(ref TargetName name);
 [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")]static extern int AdapterInfo(ref AdapterName name);
 [DllImport("user32.dll",EntryPoint="DisplayConfigGetDeviceInfo")]static extern int PreferredInfo(ref PreferredMode mode);
 [DllImport("user32.dll")]static extern int SetDisplayConfig(uint count,DisplayPath[] paths,uint modeCount,Mode[] modes,uint flags);
 static void Check(int error,string message){if(error!=0)throw new Win32Exception(error,message);}
 internal static void CheckSizes(){if(Marshal.SizeOf(typeof(DisplayPath))!=72||Marshal.SizeOf(typeof(Mode))!=64||Marshal.SizeOf(typeof(TargetName))!=420||Marshal.SizeOf(typeof(PreferredMode))!=80)throw new InvalidOperationException("显示器接口结构不匹配。");}
 internal static Snapshot Read(uint flags){
  CheckSizes();for(int attempt=0;attempt<3;attempt++){uint pc,mc;Check(GetDisplayConfigBufferSizes(flags,out pc,out mc),"无法读取显示器数量。");if(pc>512||mc>1024)throw new InvalidOperationException("显示器数量异常。");var paths=new DisplayPath[pc];var modes=new Mode[mc];int error=QueryDisplayConfig(flags,ref pc,paths,ref mc,modes,IntPtr.Zero);if(error==122)continue;Check(error,"无法读取显示器配置。");Array.Resize(ref paths,(int)pc);Array.Resize(ref modes,(int)mc);return new Snapshot{paths=paths,modes=modes};}throw new InvalidOperationException("屏幕正在切换，请稍后重试。");
 }
 static TargetName DescribeTarget(Target target){var value=new TargetName{header=new Header{type=2,size=(uint)Marshal.SizeOf(typeof(TargetName)),adapter=target.adapter,id=target.id}};Check(TargetInfo(ref value),"无法读取屏幕标识。");return value;}
 static string SourceDevice(Source source){var value=new SourceName{header=new Header{type=1,size=(uint)Marshal.SizeOf(typeof(SourceName)),adapter=source.adapter,id=source.id}};Check(SourceInfo(ref value),"无法读取屏幕名称。");return value.name;}
 internal static bool MatchesVirtualIdentity(string targetPath,string adapterPath,string[] hardwareIds){
  if(String.IsNullOrEmpty(targetPath)||!targetPath.StartsWith(@"\\?\DISPLAY#MTT1337#",StringComparison.OrdinalIgnoreCase)||String.IsNullOrEmpty(adapterPath)||!adapterPath.StartsWith(@"\\?\ROOT#DISPLAY#",StringComparison.OrdinalIgnoreCase))return false;
  return hardwareIds!=null&&hardwareIds.Any(id=>String.Equals(id,@"Root\MttVDD",StringComparison.OrdinalIgnoreCase));
 }
 internal static bool IsVirtual(DisplayPath path){
  var target=DescribeTarget(path.target);if(String.IsNullOrEmpty(target.devicePath)||!target.devicePath.StartsWith(@"\\?\DISPLAY#MTT1337#",StringComparison.OrdinalIgnoreCase))return false;
  var adapter=new AdapterName{header=new Header{type=4,size=(uint)Marshal.SizeOf(typeof(AdapterName)),adapter=path.target.adapter}};Check(AdapterInfo(ref adapter),"无法读取虚拟屏驱动标识。");
  if(String.IsNullOrEmpty(adapter.path)||!adapter.path.StartsWith(@"\\?\ROOT#DISPLAY#",StringComparison.OrdinalIgnoreCase))return false;
  var parts=adapter.path.Substring(4).Split('#');if(parts.Length!=4||parts[2].Length==0||parts[2].Any(ch=>!Char.IsLetterOrDigit(ch)))return false;
  using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ROOT\DISPLAY\"+parts[2]))return MatchesVirtualIdentity(target.devicePath,adapter.path,key==null?null:key.GetValue("HardwareID") as string[]);
 }
 static string TargetKey(DisplayPath path){return path.target.adapter+"/"+path.target.id;}
  internal static bool SameCaptureTarget(DisplayPath expected,Rectangle bounds,Snapshot current,DisplayPath actual){return (actual.flags&1)!=0&&TargetKey(expected)==TargetKey(actual)&&Bounds(current,actual)==bounds;}
 static string SourceKey(DisplayPath path){return path.source.adapter+"/"+path.source.id;}
 internal static DisplayPath FindVirtual(Snapshot state){
  var paths=state.paths.Where(path=>path.target.available&&IsVirtual(path)).ToArray();var targets=paths.GroupBy(TargetKey).ToArray();
  if(targets.Length==0)throw new InvalidOperationException("请先在电脑安装支持的 Virtual Display Driver，再打开副屏。");
  if(targets.Length!=1)throw new InvalidOperationException("检测到多个虚拟屏，请先在电脑保留一个 iPad 虚拟屏。");
  foreach(var path in targets[0])if(!state.paths.Any(other=>(other.flags&1)!=0&&SourceKey(other)==SourceKey(path)&&TargetKey(other)!=TargetKey(path)))return path;
  throw new InvalidOperationException("虚拟屏没有独立显示源，请在电脑检查显示器配置。");
 }
 internal static List<DisplayPath> ActiveVirtual(Snapshot state){return state.paths.Where(path=>(path.flags&1)!=0&&IsVirtual(path)).ToList();}
 internal static Rectangle Bounds(Snapshot state,DisplayPath path){
  if(path.source.mode>=state.modes.Length)throw new InvalidOperationException("屏幕模式不可用。");var mode=state.modes[path.source.mode];if(mode.type!=1||mode.id!=path.source.id||mode.adapter.ToString()!=path.source.adapter.ToString()||mode.source.width==0||mode.source.height==0||mode.source.width>32768||mode.source.height>32768)throw new InvalidOperationException("屏幕模式无效。");
  return new Rectangle(mode.source.position.x,mode.source.position.y,(int)mode.source.width,(int)mode.source.height);
 }
 internal static List<CompanionPhysicalMonitor> PhysicalMonitors(Snapshot state){
  var result=new List<CompanionPhysicalMonitor>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var path in state.paths){if((path.flags&1)==0||path.target.technology==15||path.target.technology==17||IsVirtual(path))continue;string source=SourceDevice(path.source);if(!seen.Add(source))continue;var bounds=Bounds(state,path);bool primary=bounds.X==0&&bounds.Y==0;var target=DescribeTarget(path.target);result.Add(new CompanionPhysicalMonitor{DeviceName=source,Identity=String.IsNullOrWhiteSpace(target.devicePath)?null:target.devicePath+"|"+source+"|"+SourceKey(path),Name=primary?"主屏幕":String.IsNullOrEmpty(target.name)?"有线屏幕":target.name,Bounds=bounds,Primary=primary});}
  result.Sort((a,b)=>a.Primary!=b.Primary?(a.Primary?-1:1):StringComparer.OrdinalIgnoreCase.Compare(a.DeviceName,b.DeviceName));for(int i=0;i<result.Count;i++)result[i].Id=i;return result;
 }
 internal static PreferredMode Preferred(Target target){var mode=new PreferredMode{header=new Header{type=3,size=(uint)Marshal.SizeOf(typeof(PreferredMode)),adapter=target.adapter,id=target.id}};Check(PreferredInfo(ref mode),"无法读取副屏分辨率。");return mode;}
 internal static Snapshot Disable(Snapshot current,DisplayPath target){
  var paths=current.paths.Where(path=>(path.flags&1)!=0&&TargetKey(path)!=TargetKey(target)).ToArray();if(paths.Length==0)throw new InvalidOperationException("不能关闭唯一的显示器。");return new Snapshot{paths=paths,modes=(Mode[])current.modes.Clone()};
 }
 internal static bool SupportsQuarterTurn(PreferredMode preferred,IEnumerable<DisplayMode> modes){return modes.Any(mode=>mode.orientation==1&&mode.width==preferred.height&&mode.height==preferred.width&&mode.frequency==(uint)preferred.signal.c/Math.Max(1u,(uint)(preferred.signal.c>>32)));}
 internal static Snapshot EnableValidated(Snapshot current,DisplayPath target,PreferredMode preferred,bool rotate){
  var plan=Enable(current,target,preferred,rotate);
  // Some drivers enumerate rotations that CCD rejects; fall back to the native orientation.
  try{Validate(plan);}catch(Win32Exception){if(!rotate)throw;plan=Enable(current,target,preferred);Validate(plan);}return plan;
 }
 internal static Snapshot Enable(Snapshot current,DisplayPath target,PreferredMode preferred,bool rotate=false){
  if(preferred.width==0||preferred.height==0||preferred.width>8192||preferred.height>8192||(ulong)preferred.width*preferred.height>33554432)throw new InvalidOperationException("副屏分辨率无效。");
  var paths=current.paths.Where(path=>(path.flags&1)!=0&&TargetKey(path)!=TargetKey(target)).ToList();if(paths.Count==0)throw new InvalidOperationException("请先连接主屏幕。");
  if(paths.Any(path=>SourceKey(path)==SourceKey(target)))throw new InvalidOperationException("副屏与其他屏幕共用显示源。");
  uint width=rotate?preferred.height:preferred.width,height=rotate?preferred.width:preferred.height;
  int right=paths.Max(path=>Bounds(current,path).Right);if(right>32768-(int)width)throw new InvalidOperationException("桌面范围过大。");
  var modes=new List<Mode>(current.modes);target.source.mode=(uint)modes.Count;modes.Add(new Mode{type=1,id=target.source.id,adapter=target.source.adapter,source=new SourceMode{width=width,height=height,format=4,position=new Point{x=right,y=0}}});
  target.target.mode=(uint)modes.Count;modes.Add(new Mode{type=2,id=target.target.id,adapter=target.target.adapter,signal=preferred.signal});
  target.flags=1;target.target.rotation=rotate?2u:1u;target.target.scaling=1;target.target.refresh=new Rational{numerator=(uint)preferred.signal.c,denominator=(uint)(preferred.signal.c>>32)};target.target.scanline=(uint)(preferred.signal.f>>32);paths.Add(target);return new Snapshot{paths=paths.ToArray(),modes=modes.ToArray()};
 }
 internal static string Fingerprint(Snapshot state,Func<DisplayPath,bool> ignore){
  var rows=new List<string>();foreach(var path in state.paths){if((path.flags&1)==0||ignore(path))continue;var bounds=Bounds(state,path);if(path.target.mode>=state.modes.Length)throw new InvalidOperationException("屏幕目标模式不可用。");var mode=state.modes[path.target.mode];var s=mode.signal;
   rows.Add(TargetKey(path)+"/"+SourceKey(path)+"/"+bounds+"/"+state.modes[path.source.mode].source.format+"/"+path.target.rotation+"/"+path.target.scaling+"/"+path.target.refresh.numerator+":"+path.target.refresh.denominator+"/"+path.target.scanline+"/"+s.a+":"+s.b+":"+s.c+":"+s.d+":"+s.e+":"+s.f);
  }rows.Sort(StringComparer.Ordinal);return String.Join("|",rows);
 }
 internal static void ApplyChecked(Snapshot before,Snapshot plan,bool persist){
  string expected=Fingerprint(before,IsVirtual);if(Fingerprint(plan,IsVirtual)!=expected)throw new InvalidOperationException("拒绝修改实体屏幕布局。");
  if(Fingerprint(Read(2),IsVirtual)!=expected)throw new InvalidOperationException("实体屏幕配置已变化，请重试。");
  Validate(plan);
  Check(SetDisplayConfig((uint)plan.paths.Length,plan.paths,(uint)plan.modes.Length,plan.modes,0x20|0x80|(persist?0x200u:0u)),"无法切换 iPad 副屏。");
  if(Fingerprint(Read(2),IsVirtual)!=expected)throw new InvalidOperationException("实体屏幕布局检查未通过，请在电脑检查显示设置。");
 }
 internal static void Validate(Snapshot plan){Check(SetDisplayConfig((uint)plan.paths.Length,plan.paths,(uint)plan.modes.Length,plan.modes,0x20|0x40),"显示器配置校验失败。");}
 internal static Rectangle WaitVirtualBounds(DisplayPath target,bool active){
  for(int i=0;i<20;i++){var state=Read(2);var found=state.paths.Where(path=>TargetKey(path)==TargetKey(target)).ToArray();if(!active&&found.Length==0)return Rectangle.Empty;if(active&&found.Length==1)return Bounds(state,found[0]);Thread.Sleep(100);}
  throw new InvalidOperationException(active?"副屏尚未就绪，请重试。":"副屏未能关闭，请在电脑检查显示设置。");
 }
}
