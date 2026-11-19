using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

// Device IO is serialized off the UI thread; a drag retains only its latest value.
internal sealed class DisplayBrightness : IDisposable {
 internal sealed class State {public bool available,busy;public string targetId="",reason="正在读取显示器亮度。";public int value;public object[] displays=new object[0];}
 internal sealed class Reading {public bool available;public int value;public string reason="";}
 readonly object gate=new object();readonly Func<string,int?,Func<bool>,Reading> access;readonly Func<string[]> targets;readonly Func<object[]> choices;
 string target="";int? pending;Func<bool> permission;bool running,disposed;long revision;DateTime readAt=DateTime.MinValue;Reading reading=new Reading{reason="正在读取显示器亮度。"};
 internal DisplayBrightness():this(NativeAccess,()=>MonitorCatalog.Read().OrderByDescending(m=>m.Primary).Select(m=>m.Id).ToArray(),()=>MonitorCatalog.Read().Select(m=>(object)new{id=m.Id,name=m.Name+" · "+m.Screen.DeviceName.Replace(@"\\.\","")+(m.Primary?" · 主屏":""),primary=m.Primary}).ToArray()){}
 internal DisplayBrightness(Func<string,int?,Func<bool>,Reading> io,Func<string[]> ids,Func<object[]> options){access=io;targets=ids;choices=options;}
 internal State Snapshot(){lock(gate){var ids=targets();if(target.Length==0&&ids.Length>0)target=ids[0];if(!running&&(DateTime.UtcNow-readAt).TotalSeconds>=3)Start();return new State{available=reading.available,busy=running,targetId=target,value=reading.value,reason=reading.reason,displays=choices()};}}
 internal void Select(string id){lock(gate){if(disposed)throw new ObjectDisposedException("DisplayBrightness");ValidateTarget(id);if(target==id)return;target=id;revision++;pending=null;permission=null;reading=new Reading{reason="正在读取目标屏幕亮度。"};readAt=DateTime.MinValue;if(!running)Start();}}
 void ValidateTarget(string id){if(String.IsNullOrEmpty(id)||id.Length>512||!targets().Contains(id))throw new ArgumentException("目标显示器未连接，请重新选择。");}
 internal void Set(string id,int value,Func<bool> allowed){if(value<0||value>100)throw new ArgumentException("亮度需要为 0 到 100 的整数。");lock(gate){if(disposed)throw new ObjectDisposedException("DisplayBrightness");ValidateTarget(id);if(id!=target)throw new InvalidOperationException("目标显示器已切换，请读取最新状态。");if(!reading.available)throw new InvalidOperationException(reading.reason);pending=value;permission=allowed;if(!running)Start();}}
 void Start(){if(disposed)return;running=true;Task.Run((Action)Run);}
 void Run(){while(true){string id;int? value;long version;Func<bool> allowed;lock(gate){if(disposed){running=false;return;}id=target;value=pending;pending=null;allowed=permission;permission=null;version=revision;}
   Reading next;try{next=access(id,value,()=>{lock(gate)return !disposed&&version==revision&&(allowed==null||allowed());});}catch(Exception ex){next=new Reading{reason="亮度暂不可用："+ex.Message};}
   lock(gate){if(version==revision){reading=next;readAt=DateTime.UtcNow;}if(disposed||version==revision&&!pending.HasValue){running=false;return;}}
  }}
 public void Dispose(){lock(gate){disposed=true;pending=null;permission=null;revision++;}}
 internal static string Identity(string value){if(value==null)return "";value=value.Replace('#','\\');if(value.StartsWith("\\\\?\\",StringComparison.Ordinal))value=value.Substring(4);int guid=value.IndexOf("\\{",StringComparison.Ordinal);if(guid>=0)value=value.Substring(0,guid);if(value.EndsWith("_0",StringComparison.Ordinal))value=value.Substring(0,value.Length-2);return value.ToUpperInvariant();}
 internal static int NearestLevel(int value,byte[] levels){if(levels==null||levels.Length==0)return value;return levels.OrderBy(v=>Math.Abs(v-value)).ThenBy(v=>v).First();}
 static Reading NativeAccess(string id,int? value,Func<bool> allowed){
  var display=MonitorCatalog.Read().FirstOrDefault(m=>m.Id==id);if(display==null)return new Reading{reason="目标显示器已断开。"};
  bool matchedWmi=false;try{using(var search=new ManagementObjectSearcher("root\\WMI","SELECT * FROM WmiMonitorBrightness"))using(var items=search.Get()){
   foreach(ManagementObject item in items)using(item){if(!Object.Equals(item["Active"],true)||Identity(Convert.ToString(item["InstanceName"]))!=Identity(id))continue;matchedWmi=true;
    if(value.HasValue){string instance=Convert.ToString(item["InstanceName"]);using(var methods=new ManagementObjectSearcher("root\\WMI","SELECT * FROM WmiMonitorBrightnessMethods"))using(var found=methods.Get()){bool written=false;foreach(ManagementObject method in found)using(method){if(Convert.ToString(method["InstanceName"])!=instance)continue;if(!allowed())throw new InvalidOperationException("控制已取消或权限已撤销。");var result=method.InvokeMethod("WmiSetBrightness",new object[]{(uint)0,(byte)NearestLevel(value.Value,item["Level"] as byte[])});if(Convert.ToUInt32(result)!=0)throw new InvalidOperationException("目标屏幕未确认亮度写入。");written=true;break;}if(!written)throw new InvalidOperationException("未找到目标屏幕的亮度写入接口。");}item.Get();}
    int currentValue=Convert.ToInt32(item["CurrentBrightness"]);if(currentValue<0||currentValue>100)throw new InvalidOperationException("目标屏幕返回了无效亮度。");return new Reading{available=true,value=currentValue};
   }
  }}catch(ManagementException){if(matchedWmi)throw;}
  var hMonitor=MonitorFromPoint(new System.Drawing.Point(display.Screen.Bounds.Left+display.Screen.Bounds.Width/2,display.Screen.Bounds.Top+display.Screen.Bounds.Height/2),0);uint count;
  if(hMonitor==IntPtr.Zero||!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor,out count)||count!=1)return new Reading{reason="此屏幕不支持独立硬件亮度控制（虚拟屏或映射不唯一）。"};
  var physical=new PhysicalMonitor[1];if(!GetPhysicalMonitorsFromHMONITOR(hMonitor,1,physical))return new Reading{reason="无法读取目标显示器。"};
  try{uint caps,color,min,current,max;if(!GetMonitorCapabilities(physical[0].handle,out caps,out color)||(caps&2)==0||!GetMonitorBrightness(physical[0].handle,out min,out current,out max)||max<=min||current<min||current>max)return new Reading{reason="此显示器未提供硬件亮度接口，请检查 DDC/CI 或使用显示器按键。"};
   if(value.HasValue){if(!allowed())throw new InvalidOperationException("控制已取消或权限已撤销。");uint raw=min+(uint)Math.Round((max-min)*value.Value/100.0);if(!SetMonitorBrightness(physical[0].handle,raw)||!GetMonitorBrightness(physical[0].handle,out min,out current,out max)||max<=min||current<min||current>max)throw new InvalidOperationException("显示器未确认亮度，未重试。 ");}
   return new Reading{available=true,value=(int)Math.Round((current-min)*100.0/(max-min))};
  }finally{DestroyPhysicalMonitors(1,physical);}
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct PhysicalMonitor {public IntPtr handle;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string description;}
 [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(System.Drawing.Point point,uint flags);
 [DllImport("dxva2.dll")]static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor,out uint count);
 [DllImport("dxva2.dll",CharSet=CharSet.Unicode)]static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor,uint count,[Out]PhysicalMonitor[] physical);
 [DllImport("dxva2.dll")]static extern bool DestroyPhysicalMonitors(uint count,PhysicalMonitor[] physical);
 [DllImport("dxva2.dll")]static extern bool GetMonitorCapabilities(IntPtr monitor,out uint capabilities,out uint colorTemperatures);
 [DllImport("dxva2.dll")]static extern bool GetMonitorBrightness(IntPtr monitor,out uint min,out uint current,out uint max);
 [DllImport("dxva2.dll")]static extern bool SetMonitorBrightness(IntPtr monitor,uint value);
}
