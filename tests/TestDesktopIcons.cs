// Interactive Windows test. Requires beauty stopped; leaves native icons visible.
// Compile with src/LiquidDesktop/DesktopIcons.cs and run in the user's desktop session.
using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;
class TestDesktopIcons {
 static int count;
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 static bool EqualBytes(byte[] a,byte[] b){if(a==null||b==null)return a==b;if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
 [STAThread]static int Main(){
  if(Process.GetProcessesByName("LiquidDesktop").Length>0){Console.WriteLine("Stop beauty before this interactive test.");return 2;}
  try{
   byte[] layout;int size;
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Bags\1\Desktop")){layout=key==null?null:key.GetValue("IconLayouts") as byte[];size=key==null?0:Convert.ToInt32(key.GetValue("IconSize",0));}
   Console.WriteLine("Before current=0x{0:X8} saved=0x{1:X8} visible={2}",DesktopIcons.CurrentFlags(),DesktopIcons.SavedFlags(),DesktopIcons.Visible());
   DesktopIcons.SetVisible(true);
   Check((DesktopIcons.CurrentFlags()&0x1000)==0,"Explorer's live hide-icons flag is clear");
   Check((DesktopIcons.SavedFlags()&0x1000)==0,"next-login desktop flags show icons");
   Check(DesktopIcons.Visible(),"native desktop icons are visible without beauty");
   DesktopIcons.SetVisible(false);Thread.Sleep(250);
   Console.WriteLine("Hidden current=0x{0:X8} saved=0x{1:X8} visible={2}",DesktopIcons.CurrentFlags(),DesktopIcons.SavedFlags(),DesktopIcons.Visible());
   Check(!DesktopIcons.Visible(),"beauty temporarily hides icons");
   Check((DesktopIcons.CurrentFlags()&0x1000)==0,"temporary hiding does not change Explorer's cached flag");
   Check((DesktopIcons.SavedFlags()&0x1000)==0,"temporary hiding does not persist into next login");
   DesktopIcons.SetVisible(true);Thread.Sleep(250);
   Check(DesktopIcons.Visible()&&(DesktopIcons.CurrentFlags()&0x1000)==0,"exit restores ordinary icons and cached state");
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Bags\1\Desktop")){
    Check(key==null?layout==null:EqualBytes(layout,key.GetValue("IconLayouts") as byte[]),"saved icon positions are preserved");
    Check(key==null?size==0:size==Convert.ToInt32(key.GetValue("IconSize",0)),"icon size is preserved");
   }
   Console.WriteLine(count+" desktop-icon checks passed.");return 0;
  }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
  finally{DesktopIcons.SetVisible(true);}
 }
}
