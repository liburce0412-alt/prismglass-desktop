using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class SideDockData {
 readonly string root;
 readonly JavaScriptSerializer json=new JavaScriptSerializer();
 readonly List<Sample> history=new List<Sample>();
 DateTime healthRead=DateTime.MinValue;
 double? health; long? cycles;
 internal sealed class Sample { public long time; public int percent; public bool charging; }
 public SideDockData(string directory){
  root=directory;
  try { var path=Path.Combine(root,"side-battery-history.json");if(File.Exists(path))history.AddRange(json.Deserialize<List<Sample>>(File.ReadAllText(path))); } catch { }
 }
 static long UnixNow(){return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;}
 static long? Capacity(string name,string property){
  try { using(var search=new ManagementObjectSearcher("root\\WMI","SELECT "+property+" FROM "+name))using(var rows=search.Get()){
   long value=0;bool found=false;foreach(ManagementObject row in rows)using(row){if(row[property]!=null){value+=Convert.ToInt64(row[property]);found=true;}}
   return found?(long?)value:null;
  }} catch {return null;}
 }
 public object Read(){
  var power=SystemInformation.PowerStatus;
  bool present=power.BatteryChargeStatus!=BatteryChargeStatus.Unknown&&(power.BatteryChargeStatus&BatteryChargeStatus.NoSystemBattery)==0;
  int? percent=present&&power.BatteryLifePercent>=0&&power.BatteryLifePercent<=1?(int?)Math.Round(power.BatteryLifePercent*100):null;
  bool plugged=power.PowerLineStatus==PowerLineStatus.Online;
  bool charging=present&&(power.BatteryChargeStatus&BatteryChargeStatus.Charging)!=0;
  long now=UnixNow();
  if((DateTime.UtcNow-healthRead).TotalMinutes>=5){
   healthRead=DateTime.UtcNow;long? design=Capacity("BatteryStaticData","DesignedCapacity"),full=Capacity("BatteryFullChargedCapacity","FullChargedCapacity");
   health=design>0&&full>0?(double?)Math.Round(100.0*full.Value/design.Value,1):null;cycles=Capacity("BatteryCycleCount","CycleCount");
  }
  history.RemoveAll(s=>s.time<now-86400);
  if(percent.HasValue&&(history.Count==0||now-history[history.Count-1].time>=300)){
   history.Add(new Sample{time=now,percent=percent.Value,charging=plugged});
   try {File.WriteAllText(Path.Combine(root,"side-battery-history.json"),json.Serialize(history));}catch { }
  }
  return new { type="data",time=now,battery=new {present=present,percent=percent,plugged=plugged,charging=charging,seconds=power.BatteryLifeRemaining,health=health,cycles=cycles,history=history.ToArray()},network=ReadNetwork() };
 }
 static object ReadNetwork(){
  try {
   var interfaces=NetworkInterface.GetAllNetworkInterfaces();
   var nic=interfaces.Where(n=>n.OperationalStatus==OperationalStatus.Up&&(n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211||n.NetworkInterfaceType==NetworkInterfaceType.Ethernet))
    .OrderByDescending(n=>n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211).FirstOrDefault();
   bool wifi=nic!=null&&nic.NetworkInterfaceType==NetworkInterfaceType.Wireless80211;
   string name=nic==null?"未连接":wifi?"Wi-Fi":"以太网";int? signal=null;double? receive=null,transmit=null;
   if(wifi){Guid id;if(Guid.TryParse(nic.Id,out id))ReadWifi(id,ref name,ref signal,ref receive,ref transmit);}
   return new {connected=nic!=null,wifi=wifi,name=name,adapter=nic==null?"没有活动的网络适配器":nic.Description,signal=signal,speed=nic==null?0:nic.Speed/1000000,receive=receive,transmit=transmit};
  }catch{return new {connected=false,wifi=false,name="状态暂不可用",adapter="稍后自动重试",signal=(int?)null,speed=0,receive=(double?)null,transmit=(double?)null};}
 }
 // Query only the current connection. No scans, profiles, passwords or permission changes.
 static void ReadWifi(Guid id,ref string name,ref int? signal,ref double? receive,ref double? transmit){
  IntPtr client=IntPtr.Zero,data=IntPtr.Zero;
  try {uint version,size,kind;if(WlanOpenHandle(2,IntPtr.Zero,out version,out client)!=0)return;
   if(WlanQueryInterface(client,ref id,7,IntPtr.Zero,out size,out data,out kind)!=0||size<588)return;
   int length=Marshal.ReadInt32(data,520);if(length>0&&length<=32){var ssid=new byte[length];Marshal.Copy(IntPtr.Add(data,524),ssid,0,length);name=Encoding.UTF8.GetString(ssid);}
   int quality=Marshal.ReadInt32(data,576);if(quality>=0&&quality<=100)signal=quality;
   receive=(uint)Marshal.ReadInt32(data,580)/1000.0;transmit=(uint)Marshal.ReadInt32(data,584)/1000.0;
  }catch { }finally{if(data!=IntPtr.Zero)WlanFreeMemory(data);if(client!=IntPtr.Zero)WlanCloseHandle(client,IntPtr.Zero);}
 }
 [DllImport("wlanapi.dll")]static extern uint WlanOpenHandle(uint version,IntPtr reserved,out uint negotiated,out IntPtr client);
 [DllImport("wlanapi.dll")]static extern uint WlanQueryInterface(IntPtr client,ref Guid id,uint opcode,IntPtr reserved,out uint size,out IntPtr data,out uint kind);
 [DllImport("wlanapi.dll")]static extern void WlanFreeMemory(IntPtr data);
 [DllImport("wlanapi.dll")]static extern uint WlanCloseHandle(IntPtr client,IntPtr reserved);
}
