// Interactive Windows check. Run only with PrismGlass stopped; always restores the shell.
using System;using System.Diagnostics;using System.Drawing;using System.IO;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;using Microsoft.Win32;
internal static class TestTaskbarVisibility {
 [StructLayout(LayoutKind.Sequential)]struct RECT {public int L,T,R,B;public Rectangle Bounds{get{return Rectangle.FromLTRB(L,T,R,B);}}}
 [StructLayout(LayoutKind.Sequential)]struct APPBAR {public int cb;public IntPtr h;public uint message,edge;public RECT rect;public IntPtr param;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string c,string t);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]static extern bool SystemParametersInfo(uint action,uint param,ref RECT rect,uint flags);
 [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [DllImport("shell32.dll")]static extern UIntPtr SHAppBarMessage(uint message,ref APPBAR data);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out RECT bounds,int size);
 sealed class ProbeWindow:Form {protected override bool ShowWithoutActivation{get{return true;}}public ProbeWindow(){Text="PrismGlass work-area check";ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Bounds=new Rectangle(100,100,500,320);WindowState=FormWindowState.Maximized;}}
 static int State(){var data=new APPBAR{cb=Marshal.SizeOf(typeof(APPBAR))};return (int)SHAppBarMessage(4,ref data).ToUInt64();}
 static Rectangle Area(){var rect=new RECT();if(!SystemParametersInfo(48,0,ref rect,0))throw new Exception("Cannot read work area");return rect.Bounds;}
 static int SavedFlags(){using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3")){var value=key==null?null:key.GetValue("Settings") as byte[];return value!=null&&value.Length>=12?BitConverter.ToInt32(value,8):-1;}}
 static void Check(bool pass,string name){Console.WriteLine((pass?"PASS ":"FAIL ")+name);if(!pass)throw new Exception(name);}
 [STAThread]static void Main(){
  if(Process.GetProcessesByName("LiquidDesktop").Length>0){Console.WriteLine("Stop PrismGlass before running this interactive check");Environment.ExitCode=2;return;}
  SetProcessDPIAware();var screen=Screen.PrimaryScreen.Bounds;var before=Area();int flags=SavedFlags();var controller=new TaskbarVisibility();
  var taskbar=FindWindow("Shell_TrayWnd",null);
  Console.WriteLine("Primary="+screen+" ordinaryWorkArea="+before+" savedTaskbarFlags="+flags);
  Check(taskbar!=IntPtr.Zero&&IsWindowVisible(taskbar)&&(State()&1)==0,"ordinary desktop is the test baseline");
  try{
   for(int round=0;round<2;round++){
    using(var window=new ProbeWindow()){
    if(round==0){window.Show();Application.DoEvents();}
    var restoreBounds=window.RestoreBounds;
    controller.Set(false,screen);Console.WriteLine("Hidden work area immediately="+Area());Thread.Sleep(200);Console.WriteLine("Hidden work area after shell notifications="+Area());
    Check(!IsWindowVisible(taskbar),"temporary hide keeps the native taskbar out of view");
    Check((State()&1)==0&&SavedFlags()==flags,"hiding does not persist Windows auto-hide");
    Check(Area()==screen,"beauty work area covers the whole primary screen");
     if(round==1)window.Show();Application.DoEvents();Thread.Sleep(150);RECT bounds;
     Check(DwmGetWindowAttribute(window.Handle,9,out bounds,Marshal.SizeOf(typeof(RECT)))==0&&bounds.B>=screen.Bottom&&bounds.L<=screen.Left&&bounds.R>=screen.Right,(round==0?"already maximized":"newly maximized")+" window leaves no taskbar wallpaper gap");
    Check(window.RestoreBounds==restoreBounds,"temporary reflow preserves normal window position and size");
    controller.Set(true,screen);Application.DoEvents();Thread.Sleep(200);
    Check(IsWindowVisible(taskbar)&&Area()==before,"exit restores taskbar visibility and its work area");
    Check(DwmGetWindowAttribute(window.Handle,9,out bounds,Marshal.SizeOf(typeof(RECT)))==0&&Math.Abs(bounds.B-before.Bottom)<=2,"existing maximized window follows the restored work area");
    Check((State()&1)==0&&SavedFlags()==flags,"ordinary desktop remains the saved Windows state");
    window.Close();
    }
   }
  }catch(Exception ex){Console.WriteLine("FAIL "+ex.Message);Environment.ExitCode=1;}finally{controller.Set(true,screen);}
 }
}
