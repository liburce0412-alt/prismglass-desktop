using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// Exercise the built runtime against only our own window; never toggle Explorer icons.
internal static class TestWallpaperStartup {
 delegate IntPtr WindowProc(IntPtr window,uint message,IntPtr w,IntPtr l);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct WindowClass {public uint size,style;public WindowProc procedure;public int classExtra,windowExtra;public IntPtr instance,icon,cursor,background;public string menu,name;public IntPtr smallIcon;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern ushort RegisterClassEx(ref WindowClass value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateWindowEx(int extended,string type,string title,int style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr data);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr DefWindowProc(IntPtr window,uint message,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr value);
 sealed class Fixture : Form {protected override bool ShowWithoutActivation {get{return true;}}}
 static readonly WindowProc procedure=DefWindowProc;static Assembly app;static int checks;
 const BindingFlags Members=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static object Call(Type type,object owner,string name,params object[] arguments){return type.GetMethod(name,Members).Invoke(owner,arguments);}
 static object Field(object owner,string name){return owner.GetType().GetField(name,Members).GetValue(owner);}
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static bool Wait(Func<bool> ready,int milliseconds){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){Application.DoEvents();if(ready())return true;Thread.Sleep(10);}return ready();}
 static long Now(){return (long)(Stopwatch.GetTimestamp()*(10000000.0/Stopwatch.Frequency));}
 [STAThread]static int Main(string[] args){try{
  SetProcessDpiAwarenessContext(new IntPtr(-4));app=Assembly.LoadFrom(Path.GetFullPath(args[0]));
  var glass=app.GetType("LiveGlassBackdrop",true);var capture=app.GetType("SharedWindowCapture",true);var windowCapture=app.GetType("WindowCapture",true);
  using(var form=new Fixture{Text="PrismGlass isolated capture check",ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Size=new Size(256,160),BackColor=Color.FromArgb(210,30,40)}){
   var work=Screen.PrimaryScreen.WorkingArea;form.Location=new Point(work.Right-form.Width-12,work.Bottom-form.Height-12);form.Show();ShowWindow(form.Handle,4);form.Refresh();
   var definition=new WindowClass{size=(uint)Marshal.SizeOf(typeof(WindowClass)),procedure=procedure,instance=Marshal.GetHINSTANCE(typeof(TestWallpaperStartup).Module),name="SHELLDLL_DefView"};
   Check(RegisterClassEx(ref definition)!=0,"isolated shell-view class registers");
   var view=CreateWindowEx(0,definition.name,"",0x50000000,0,0,200,120,form.Handle,IntPtr.Zero,definition.instance,IntPtr.Zero);Check(view!=IntPtr.Zero,"isolated shell-view child exists");
   using(var controls=new ListView()){var initialized=controls.Handle;}
   var list=CreateWindowEx(0,"SysListView32","",0x50000000,0,0,180,100,view,IntPtr.Zero,definition.instance,IntPtr.Zero);Check(list!=IntPtr.Zero,"native icon ListView exists with the Explorer class name");
   try{
    Check((bool)Call(glass,null,"HasVisibleDesktopIcons",form.Handle),"visible desktop icon list prevents a clean wallpaper root");
    ShowWindow(list,0);
    Check(!(bool)Call(glass,null,"HasVisibleDesktopIcons",form.Handle),"hidden icon list permits sampling even while DefView stays visible");
    ShowWindow(list,5);ShowWindow(view,0);
    Check(!(bool)Call(glass,null,"HasVisibleDesktopIcons",form.Handle),"an invisible ancestor makes the icon list non-visible");
   }finally{DestroyWindow(list);DestroyWindow(view);}
   form.Refresh();
   var permission=(Task<bool>)Call(windowCapture,null,"Borderless");Check(Wait(()=>permission.IsCompleted,5000)&&permission.GetAwaiter().GetResult(),"capture access is available for the isolated window");
   var subscription=Call(capture,null,"Acquire",form.Handle,(uint)Process.GetCurrentProcess().Id);
   using((IDisposable)subscription){
    object frame=null;Check(Wait(()=>{frame=Call(subscription.GetType(),subscription,"Latest",-1L,0L);return frame!=null;},5000),"shared capture publishes an actual first frame");
    long serial,time;using((IDisposable)frame){serial=(long)Field(frame,"Serial");time=(long)Field(frame,"CapturedAt");Check(time>0&&time<=Now(),"shared frame carries the compositor's QPC capture time");}
    Check(Call(subscription.GetType(),subscription,"Latest",-1L,Now()+TimeSpan.TicksPerSecond*10)==null,"a higher serial cannot bypass the fresh-frame time boundary");
    long after=Now();form.BackColor=Color.FromArgb(20,40,220);form.Refresh();
    Check(Wait(()=>{frame=Call(subscription.GetType(),subscription,"Latest",serial,after);return frame!=null;},5000),"a frame captured after the clean-state boundary becomes available");
    using((IDisposable)frame){Check((long)Field(frame,"CapturedAt")>after,"accepted replacement is newer than the retained old image");var bytes=(byte[])Field(frame,"Pixels");int width=(int)Field(frame,"Width"),height=(int)Field(frame,"Height"),center=(height/2*width+width/2)*4;Check(bytes[center]>bytes[center+2]+100,"replacement contains the new blue pixels, not the old red frame");}
   }
   form.Close();
  }
  Console.WriteLine("Completed "+checks+" startup capture checks; only fixture windows were touched.");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
