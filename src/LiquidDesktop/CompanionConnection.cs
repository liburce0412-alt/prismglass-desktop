using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class CompanionNetworkChoice {
 public string address,id,alias,name;public int index,category=-1;
 public bool IsPrivate {get{return category==1;}}
 public string Description {get{return (String.IsNullOrEmpty(name)?alias:name)+" · "+address+" · "+(IsPrivate?"专用网络":category==0?"公共网络":"无法确认网络类别");}}
}

internal static class CompanionConnection {
 public static bool Lan(IPAddress address){var bytes=address.GetAddressBytes();return bytes.Length==4&&(bytes[0]==10||bytes[0]==192&&bytes[1]==168||bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31);}
 public static CompanionNetworkChoice[] Networks(){
  var profiles=new Dictionary<int,Tuple<string,int>>();
  try{using(var query=new ManagementObjectSearcher(@"root\StandardCimv2","SELECT InterfaceIndex,Name,NetworkCategory FROM MSFT_NetConnectionProfile"))using(var results=query.Get())foreach(ManagementObject profile in results)using(profile){profiles[Convert.ToInt32(profile["InterfaceIndex"])]=Tuple.Create(Convert.ToString(profile["Name"]),Convert.ToInt32(profile["NetworkCategory"]));}}catch{}
  var choices=new List<CompanionNetworkChoice>();
  foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces())try{
   if(adapter.OperationalStatus!=OperationalStatus.Up||(adapter.NetworkInterfaceType!=NetworkInterfaceType.Wireless80211&&adapter.NetworkInterfaceType!=NetworkInterfaceType.Ethernet))continue;
   var properties=adapter.GetIPProperties();if(!properties.GatewayAddresses.Any(g=>Lan(g.Address)))continue;
   var ipv4=properties.GetIPv4Properties();Tuple<string,int> profile;profiles.TryGetValue(ipv4.Index,out profile);
   foreach(var entry in properties.UnicastAddresses)if(Lan(entry.Address))choices.Add(new CompanionNetworkChoice{address=entry.Address.ToString(),id=InterfaceId(adapter.Id),alias=adapter.Name,index=ipv4.Index,name=profile==null?adapter.Name:profile.Item1,category=profile==null?-1:profile.Item2});
  }catch{}
  return choices.OrderByDescending(c=>c.IsPrivate).ThenBy(c=>c.index).ThenBy(c=>c.address,StringComparer.Ordinal).ToArray();
 }
 internal static string InterfaceId(string value){Guid id;return Guid.TryParse(value,out id)?id.ToString("D"):value;}
 public static bool SameNetwork(CompanionNetworkChoice a,CompanionNetworkChoice b){return a!=null&&b!=null&&String.Equals(InterfaceId(a.id),InterfaceId(b.id),StringComparison.OrdinalIgnoreCase)&&a.address==b.address&&a.index==b.index&&a.name==b.name&&a.IsPrivate&&b.IsPrivate;}
 public static CompanionNetworkChoice Select(CompanionNetworkChoice[] choices,string address,string interfaceId){return choices.FirstOrDefault(c=>c.address==address)??choices.FirstOrDefault(c=>String.Equals(InterfaceId(c.id),InterfaceId(interfaceId),StringComparison.OrdinalIgnoreCase))??choices.FirstOrDefault(c=>c.IsPrivate)??choices.FirstOrDefault();}
 public static Dictionary<string,object> ReadRecord(string root){
  string file=Path.Combine(root,"runtime-data","companion-network.json");if(!File.Exists(file))return null;
  if(new FileInfo(file).Length>65536)throw new InvalidDataException("连接配置过大，请重新检查连接。");
  var value=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));
  string savedRoot=Text(value,"appRoot");if(value==null||Number(value,"schemaVersion")!=1||Text(value,"owner")!="PrismGlass.Companion.Network.v1"||savedRoot.Length<3||savedRoot[1]!=':'||savedRoot[2]!='\\'||!String.Equals(Path.GetFullPath(savedRoot).TrimEnd('\\'),Path.GetFullPath(root).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("连接配置不属于此安装，请重新检查连接。");
  CompanionServer.NormalizeHostName(Text(value,"hostName"));return value;
 }
 public static string Text(Dictionary<string,object> value,string key){object result;return value!=null&&value.TryGetValue(key,out result)&&result is string?(string)result:"";}
 public static bool Flag(Dictionary<string,object> value,string key){object result;return value!=null&&value.TryGetValue(key,out result)&&result is bool&&(bool)result;}
 public static int Number(Dictionary<string,object> value,string key){object result;return value!=null&&value.TryGetValue(key,out result)&&result is int?(int)result:0;}
 public static string Issues(Dictionary<string,object> value){object result;if(value==null||!value.TryGetValue("issues",out result))return "";var items=result as System.Collections.IEnumerable;return items==null?"":String.Join("\n",items.Cast<object>().Select(item=>Convert.ToString(item)));}
 internal static string Quote(string value){if(value==null||value.IndexOfAny(new[]{'\r','\n','\0','"'})>=0)throw new ArgumentException("连接参数无效。");return "\""+value+new string('\\',value.Length-value.TrimEnd('\\').Length)+"\"";}
 internal static string PowerShell(string root){var candidates=new List<string>{Text(ReadRecord(root),"powerShellPath"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"PowerShell","7","pwsh.exe")};foreach(string directory in (Environment.GetEnvironmentVariable("PATH")??"").Split(';'))if(Path.IsPathRooted(directory))candidates.Add(Path.Combine(directory,"pwsh.exe"));foreach(string path in candidates)try{if(path.Length<4||!Char.IsLetter(path[0])||path[1]!=':'||(path[2]!='\\'&&path[2]!='/')||!String.Equals(Path.GetFileName(path),"pwsh.exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(path))continue;var version=FileVersionInfo.GetVersionInfo(path);if(version.FileMajorPart>=7&&version.ProductName=="PowerShell")return Path.GetFullPath(path);}catch(ArgumentException){}string windowsShell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");if(File.Exists(windowsShell))return windowsShell;throw new InvalidOperationException("找不到可用的 PowerShell，请检查 Windows PowerShell 5.1 或 PowerShell 7。");}
 public static string Arguments(string root,CompanionNetworkChoice choice,string hostName,int port,string existingRecord,bool trust,bool apply){
  if(choice==null||choice.index<=0)throw new ArgumentException("没有可用的局域网连接。");CompanionServer.Endpoint(choice.address,port,hostName);
  root=Path.GetFullPath(root).TrimEnd('\\','/');string script=Path.Combine(Path.GetDirectoryName(root),"RepairCompanion.ps1");if(!File.Exists(script))throw new InvalidOperationException("连接修复工具尚未安装，请更新 PrismGlass。");
  string args="-NoProfile -NonInteractive -File "+Quote(script)+" -AppRoot "+Quote(Path.GetFullPath(root))+" -Address "+Quote(choice.address)+" -InterfaceIndex "+choice.index+" -HostName "+Quote(hostName)+" -Port "+port+" -UserSid "+Quote(WindowsIdentity.GetCurrent().User.Value);
  if(!String.IsNullOrEmpty(existingRecord))args+=" -ExistingSetupRecord "+Quote(Path.GetFullPath(existingRecord));if(trust)args+=" -TrustNetwork";if(apply)args+=" -Apply";return args;
 }
 public static async Task<Dictionary<string,object>> Check(string root,CompanionNetworkChoice choice,string hostName,int port,string existingRecord,bool trust){
  var start=new ProcessStartInfo(PowerShell(root),Arguments(root,choice,hostName,port,existingRecord,trust,false)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  using(var process=Process.Start(start)){
   Task<string> output=process.StandardOutput.ReadToEndAsync(),error=process.StandardError.ReadToEndAsync();var wait=Task.Run(()=>process.WaitForExit(30000));
   if(!await wait){try{process.Kill();}catch{}throw new TimeoutException("连接检查超时，请稍后重试。");}string text=await output,details=await error;
   if(text.Length>262144)throw new InvalidDataException("连接检查结果过大。");var result=String.IsNullOrWhiteSpace(text)?null:new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text);
   if(process.ExitCode!=0)throw new InvalidOperationException(result!=null?Issues(result):String.IsNullOrWhiteSpace(details)?"连接检查未完成。":details.Trim());return result;
  }
 }
 public static async Task Apply(string root,CompanionNetworkChoice choice,string hostName,int port,string existingRecord,bool trust){
  var start=new ProcessStartInfo(PowerShell(root),Arguments(root,choice,hostName,port,existingRecord,trust,true)){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
  using(var process=Process.Start(start)){await Task.Run(()=>process.WaitForExit());if(process.ExitCode!=0)throw new InvalidOperationException("系统配置未完成或已取消。原连接配置保留，请再次检查。");}
 }
}
