using System;
using System.Drawing;
using System.Runtime.InteropServices;

// One coordinator controls all Explorer taskbars without persisting auto-hide.
internal sealed class TaskbarVisibility {
 [StructLayout(LayoutKind.Sequential)]struct RECT {public int L,T,R,B;}
 [StructLayout(LayoutKind.Sequential)]struct APPBAR {public int cb;public IntPtr h;public uint message,edge;public RECT rect;public IntPtr param;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string c,string t);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string c,string t);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr h,int command);
 delegate bool EnumProc(IntPtr window,IntPtr parameter);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
 [DllImport("user32.dll")]static extern bool IsZoomed(IntPtr window);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("shell32.dll")]static extern UIntPtr SHAppBarMessage(uint message,ref APPBAR data);
 [DllImport("user32.dll",SetLastError=true)]static extern bool SystemParametersInfo(uint action,uint param,ref RECT rect,uint flags);
 RECT? savedArea;Rectangle savedScreen;
 internal static void SyncWindows(bool visible,IntPtr primary,Func<IntPtr,IntPtr> nextSecondary,Func<IntPtr,bool> isVisible,Action<IntPtr,int> show){
  int command=visible?5:0;if(primary!=IntPtr.Zero&&isVisible(primary)!=visible)show(primary,command);
  IntPtr secondary=IntPtr.Zero;while((secondary=nextSecondary(secondary))!=IntPtr.Zero)if(isVisible(secondary)!=visible)show(secondary,command);
 }
 static void SetWindows(bool visible,IntPtr primary){SyncWindows(visible,primary,after=>FindWindowEx(IntPtr.Zero,after,"Shell_SecondaryTrayWnd",null),IsWindowVisible,(window,command)=>{ShowWindow(window,command);});}
 static bool BottomInset(RECT area,Rectangle screen){return area.L==screen.Left&&area.T==screen.Top&&area.R==screen.Right&&area.B<screen.Bottom&&screen.Bottom-area.B<=128;}
 static bool FullArea(RECT area,Rectangle screen){return area.L==screen.Left&&area.T==screen.Top&&area.R==screen.Right&&area.B==screen.Bottom;}
 static void ResizeMaximized(RECT before,RECT after,IntPtr taskbar){
  // Reflow only windows already maximized to this work area. Preserve their
  // nonclient borders, restore bounds, focus and stacking order.
  EnumWindows((window,p)=>{RECT bounds;if(window==taskbar||!IsWindowVisible(window)||!IsZoomed(window)||!GetWindowRect(window,out bounds))return true;
   if(Math.Abs(bounds.L-before.L)>32||Math.Abs(bounds.T-before.T)>32||Math.Abs(bounds.R-before.R)>32||Math.Abs(bounds.B-before.B)>32)return true;
   SetWindowPos(window,IntPtr.Zero,bounds.L+after.L-before.L,bounds.T+after.T-before.T,bounds.R-bounds.L+after.R-after.L-before.R+before.L,bounds.B-bounds.T+after.B-after.T-before.B+before.T,0x4214);
   return true;},IntPtr.Zero);
 }
 public void Set(bool visible,Rectangle screen){
  var taskbar=FindWindow("Shell_TrayWnd",null);if(taskbar==IntPtr.Zero){if(visible)SetWindows(true,IntPtr.Zero);return;}
  var data=new APPBAR{cb=Marshal.SizeOf(typeof(APPBAR)),h=taskbar};
  if((SHAppBarMessage(4,ref data).ToUInt64()&1)!=0){data.param=IntPtr.Zero;SHAppBarMessage(10,ref data);}
  var area=new RECT();
  if(!visible&&SystemParametersInfo(48,0,ref area,0)&&BottomInset(area,screen)){savedArea=area;savedScreen=screen;}
  SetWindows(visible,taskbar);
  if(!SystemParametersInfo(48,0,ref area,0))return;
  if(!visible&&BottomInset(area,screen)){
   var previous=area;
   area.B=screen.Bottom;if(!SystemParametersInfo(47,0,ref area,0)){int error=Marshal.GetLastWin32Error();SetWindows(true,taskbar);throw new System.ComponentModel.Win32Exception(error,"Cannot expand the temporary desktop work area");} // current session only; never SPIF_UPDATEINIFILE
   ResizeMaximized(previous,area,taskbar);
  }else if(visible&&savedArea.HasValue){
   if(savedScreen==screen&&FullArea(area,screen)){var restore=savedArea.Value;if(SystemParametersInfo(47,0,ref restore,2))ResizeMaximized(area,restore,taskbar);}
   savedArea=null;
  }
 }
}
