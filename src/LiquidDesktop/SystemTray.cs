using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;

// Temporarily expose Explorer's own notification area. Never change its saved settings.
internal static class SystemTray {
 [StructLayout(LayoutKind.Sequential)]struct Rect {public int L,T,R,B;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string name,string title);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")]static extern bool SystemParametersInfo(uint action,uint parameter,ref Rect rect,uint flags);
 static bool engaged,opening;static int generation;static IntPtr taskbar;static DateTime grace;static Rect area;static bool areaSaved;
 public static bool OverflowVisible {
  get {
   foreach(string name in new[]{"TopLevelWindowForOverflowXamlIsland","NotifyIconOverflowWindow"}){
    var window=FindWindow(name,null);if(window!=IntPtr.Zero&&IsWindowVisible(window))return true;
   }
   return false;
  }
 }
 public static bool KeepVisible {
  get {
   if(!engaged)return false;
   if(DateTime.UtcNow<grace||OverflowVisible||GetForegroundWindow()==taskbar)return true;
   var menu=FindWindow("#32768",null);uint menuPid,barPid;
   if(menu!=IntPtr.Zero&&IsWindowVisible(menu)){GetWindowThreadProcessId(menu,out menuPid);GetWindowThreadProcessId(taskbar,out barPid);if(menuPid==barPid)return true;}
   engaged=false;return false;
  }
 }
 public static void Show(){
  if(taskbar==IntPtr.Zero)return;ShowWindow(taskbar,8);
  // Explorer can reclaim a taskbar inset when it becomes visible. Keep the
  // current desktop work area while the temporary notification popup is open.
  if(areaSaved){var current=new Rect();if(SystemParametersInfo(48,0,ref current,0)&&(current.L!=area.L||current.T!=area.T||current.R!=area.R||current.B!=area.B)){var restore=area;SystemParametersInfo(47,0,ref restore,0);}}
 }
 public static void Cancel(){generation++;engaged=false;opening=false;}
 public static async Task<bool> OpenAsync(){
  if(opening)return false;
  if(KeepVisible&&OverflowVisible)return true;
  taskbar=FindWindow("Shell_TrayWnd",null);if(taskbar==IntPtr.Zero)return false;
  areaSaved=SystemParametersInfo(48,0,ref area,0);
  engaged=true;opening=true;grace=DateTime.UtcNow.AddSeconds(5);int request=++generation;
  Show();SetForegroundWindow(taskbar);
  // Explorer's accessibility provider can stall; keep it off the rendering thread.
  var invoke=Task.Run(()=>{
   try{
    var bar=AutomationElement.FromHandle(taskbar);
    var buttons=bar.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
    foreach(AutomationElement button in buttons){
     string id=button.Current.AutomationId,name=button.Current.Name??"",kind=button.Current.ClassName;
     bool chevron=id=="SystemTrayChevron"||kind=="SystemTray.ChevronIconView"||
      (name.Contains("隐藏")&&name.Contains("图标"))||
      (name.IndexOf("hidden",StringComparison.OrdinalIgnoreCase)>=0&&name.IndexOf("icon",StringComparison.OrdinalIgnoreCase)>=0);
     if(!chevron)continue;
     if(request!=generation||DateTime.UtcNow>=grace)return false;
     object pattern;if(button.TryGetCurrentPattern(InvokePattern.Pattern,out pattern)){((InvokePattern)pattern).Invoke();return true;}
    }
   }catch{}
   return false;
  });
  bool invoked=await Task.WhenAny(invoke,Task.Delay(4500))==invoke&&await invoke;
  if(request!=generation)return false;
  opening=false;generation++;grace=DateTime.UtcNow.AddMilliseconds(900);
  if(invoked){for(int i=0;i<8&&!OverflowVisible;i++)await Task.Delay(100);}
  return OverflowVisible;
 }
}
