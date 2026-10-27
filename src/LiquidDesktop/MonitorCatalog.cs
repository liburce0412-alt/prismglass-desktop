using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class MonitorRecord {public string Id,Name,Instance;public Screen Screen;public bool Primary {get{return Screen.Primary;}}}
internal static class MonitorCatalog {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct DISPLAY_DEVICE {public int size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string description;public int flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string key;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool EnumDisplayDevices(string device,uint index,ref DISPLAY_DEVICE display,uint flags);
 public static List<MonitorRecord> Read(){var result=new List<MonitorRecord>();foreach(var screen in Screen.AllScreens){var device=new DISPLAY_DEVICE{size=Marshal.SizeOf(typeof(DISPLAY_DEVICE))};bool found=EnumDisplayDevices(screen.DeviceName,0,ref device,1);string id=found&&!String.IsNullOrEmpty(device.id)?device.id:screen.DeviceName;result.Add(new MonitorRecord{Id=id,Name=found&&!String.IsNullOrEmpty(device.description)?device.description:screen.DeviceName,Screen=screen});}result.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.Id,b.Id));int ordinal=0;foreach(var item in result)item.Instance=item.Primary?"primary":"monitor-"+(++ordinal);return result;}
}
