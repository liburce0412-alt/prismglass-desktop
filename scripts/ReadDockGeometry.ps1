param([string]$OutputPath)
$ErrorActionPreference='Stop'
if(!('DockGeometryAudit' -as [type])){Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class DockGeometryAudit {
 delegate bool EnumProc(IntPtr h,IntPtr p);
 delegate bool MonitorProc(IntPtr h,IntPtr dc,ref RECT r,IntPtr p);
 [StructLayout(LayoutKind.Sequential)]public struct RECT {public int left,top,right,bottom;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct MONITOR {public int size;public RECT monitor,work;public uint flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string device;}
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc p,IntPtr x);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")]static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
 [DllImport("user32.dll")]static extern int GetAwarenessFromDpiAwarenessContext(IntPtr h);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorProc fn,IntPtr p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr h,ref MONITOR info);
 [DllImport("shcore.dll")]static extern int GetDpiForMonitor(IntPtr h,int kind,out uint x,out uint y);
 public static object Read(){
  var old=SetThreadDpiAwarenessContext(new IntPtr(-4));var windows=new List<object>();var monitors=new List<object>();
  try{
   EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr h,IntPtr dc,ref RECT rect,IntPtr p)=>{var m=new MONITOR{size=Marshal.SizeOf(typeof(MONITOR))};GetMonitorInfo(h,ref m);uint x,y;GetDpiForMonitor(h,0,out x,out y);monitors.Add(new{device=m.device,primary=(m.flags&1)!=0,bounds=m.monitor,work=m.work,dpi=x});return true;},IntPtr.Zero);
   EnumWindows((h,p)=>{var cl=new StringBuilder(255);GetClassName(h,cl,255);var title=new StringBuilder(2048);GetWindowText(h,title,2048);if(cl.ToString()=="RainmeterMeterWindow"&&title.ToString().Contains("PrismGlass\\Dock")){RECT r;GetWindowRect(h,out r);uint pid;GetWindowThreadProcessId(h,out pid);windows.Add(new{title=title.ToString(),visible=IsWindowVisible(h),rect=r,pid=pid,dpi=GetDpiForWindow(h),awareness=GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(h))});}return true;},IntPtr.Zero);
  }finally{SetThreadDpiAwarenessContext(old);}
  return new{captured=DateTime.UtcNow.ToString("o"),monitors=monitors,windows=windows};
 }
}
'@}
$result=[DockGeometryAudit]::Read() | ConvertTo-Json -Depth 8
if($OutputPath){$result | Set-Content -LiteralPath $OutputPath -Encoding utf8}
$result
