// --policy is inert. The default interactive check requires PrismGlass stopped
// and explicit desktop-test authorization; it always restores the shell.
using System;using System.Collections.Generic;using System.Diagnostics;using System.Drawing;using System.IO;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;using Microsoft.Win32;
internal static class TestTaskbarVisibility {
 [StructLayout(LayoutKind.Sequential)]struct RECT {public int L,T,R,B;public Rectangle Bounds{get{return Rectangle.FromLTRB(L,T,R,B);}}}
 [StructLayout(LayoutKind.Sequential)]struct APPBAR {public int cb;public IntPtr h;public uint message,edge;public RECT rect;public IntPtr param;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string c,string t);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string c,string t);
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
 static bool SecondaryVisibility(bool visible){IntPtr window=IntPtr.Zero;while((window=FindWindowEx(IntPtr.Zero,window,"Shell_SecondaryTrayWnd",null))!=IntPtr.Zero)if(IsWindowVisible(window)!=visible)return false;return true;}
 static void Policy(){
  var primary=new IntPtr(1);var secondary=new List<IntPtr>{new IntPtr(2),new IntPtr(3)};var states=new Dictionary<IntPtr,bool>{{primary,true},{secondary[0],true},{secondary[1],true}};var calls=new List<IntPtr>();
  Func<IntPtr,IntPtr> next=after=>{int index=after==IntPtr.Zero?0:secondary.IndexOf(after)+1;return index<secondary.Count?secondary[index]:IntPtr.Zero;};
  Func<IntPtr,bool> visible=window=>states[window];Action<IntPtr,int> show=(window,command)=>{if(command!=0&&command!=5)throw new Exception("Unexpected taskbar command");states[window]=command==5;calls.Add(window);};
  TaskbarVisibility.SyncWindows(false,primary,next,visible,show);Check(!states[primary]&&!states[secondary[0]]&&!states[secondary[1]]&&calls.Count==3,"beauty hides primary and every secondary taskbar");
  calls.Clear();TaskbarVisibility.SyncWindows(false,primary,next,visible,show);Check(calls.Count==0,"unchanged hidden state does not issue duplicate window commands");
  states[secondary[0]]=true;var added=new IntPtr(4);secondary.Add(added);states[added]=true;TaskbarVisibility.SyncWindows(false,primary,next,visible,show);Check(calls.Count==2&&calls.Contains(secondary[0])&&calls.Contains(added)&&!states[added],"reappearing and newly enumerated secondary taskbars are hidden");
  calls.Clear();TaskbarVisibility.SyncWindows(true,primary,next,visible,show);Check(states[primary]&&states[secondary[0]]&&states[secondary[1]]&&states[added]&&calls.Count==4,"exit or failure recovery restores every taskbar");
  calls.Clear();TaskbarVisibility.SyncWindows(true,primary,next,visible,show);Check(calls.Count==0,"unchanged visible state does not issue duplicate window commands");
  states[primary]=false;states[added]=false;TaskbarVisibility.SyncWindows(true,IntPtr.Zero,next,visible,show);Check(!states[primary]&&states[added]&&calls.Count==1,"secondary recovery still works while the primary handle is unavailable");
  calls.Clear();secondary.Clear();TaskbarVisibility.SyncWindows(true,primary,next,visible,show);Check(states[primary]&&calls.Count==1,"single-monitor recovery remains valid");
  Console.WriteLine("Completed 7 taskbar policy checks without calling Windows APIs.");
 }
 [STAThread]static void Main(string[] args){
  if(args.Length==1&&args[0]=="--policy"){try{Policy();}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}return;}
  if(args.Length!=0){Console.Error.WriteLine("Use --policy for inert checks, or no arguments for the interactive desktop check.");Environment.ExitCode=2;return;}
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
    Check(SecondaryVisibility(false),"beauty also hides every secondary taskbar");
    Check((State()&1)==0&&SavedFlags()==flags,"hiding does not persist Windows auto-hide");
    Check(Area()==screen,"beauty work area covers the whole primary screen");
     if(round==1)window.Show();Application.DoEvents();Thread.Sleep(150);RECT bounds;
     Check(DwmGetWindowAttribute(window.Handle,9,out bounds,Marshal.SizeOf(typeof(RECT)))==0&&bounds.B>=screen.Bottom&&bounds.L<=screen.Left&&bounds.R>=screen.Right,(round==0?"already maximized":"newly maximized")+" window leaves no taskbar wallpaper gap");
    Check(window.RestoreBounds==restoreBounds,"temporary reflow preserves normal window position and size");
    controller.Set(true,screen);Application.DoEvents();Thread.Sleep(200);
    Check(IsWindowVisible(taskbar)&&Area()==before,"exit restores taskbar visibility and its work area");
    Check(SecondaryVisibility(true),"exit restores every secondary taskbar");
    Check(DwmGetWindowAttribute(window.Handle,9,out bounds,Marshal.SizeOf(typeof(RECT)))==0&&Math.Abs(bounds.B-before.Bottom)<=2,"existing maximized window follows the restored work area");
    Check((State()&1)==0&&SavedFlags()==flags,"ordinary desktop remains the saved Windows state");
    window.Close();
    }
   }
  }catch(Exception ex){Console.WriteLine("FAIL "+ex.Message);Environment.ExitCode=1;}finally{controller.Set(true,screen);}
 }
}
