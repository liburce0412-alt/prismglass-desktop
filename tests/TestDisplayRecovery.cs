using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

// Only compositor/input collaborators are stubbed; all tested windows and DWM surfaces are real.
internal sealed class OpticalBackdrop {}
internal static class LiveGlassBackdrop {internal static OpticalBackdrop Attach(Func<Form> form,WebView2 view){return null;}}
internal sealed class DockWindow {public long handle;public string title="Fixture";}
internal sealed class DockEntry {public string id="fixture",name="Fixture";public List<DockWindow> windows=new List<DockWindow>();}
internal static class DockBackend {
 internal static bool Valid(DockWindow window){return Control.FromHandle(new IntPtr(window.handle))!=null;}
 internal static void HoldActivation(){} internal static void Activate(DockWindow window){}
}
internal static class TestDisplayRecovery {
 [StructLayout(LayoutKind.Sequential)]struct Margins {public int left,right,top,bottom;}
 [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(IntPtr window,ref Margins margins);
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 static string output;static int checks;static Form owner;
 static void Check(bool value,string label){File.AppendAllText(Path.Combine(output,"checks.txt"),(value?"PASS ":"FAIL ")+label+Environment.NewLine);if(!value)throw new Exception(label);checks++;}
 static Color Capture(Form form,string name){using(var bitmap=new Bitmap(form.Width,form.Height)){using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(form.Location,Point.Empty,form.Size);bitmap.Save(Path.Combine(output,name+".png"));return bitmap.GetPixel(bitmap.Width/2,bitmap.Height/2);}}
 static bool Near(Color a,Color b){return Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B)<45;}
 static Form Window(Rectangle bounds,Color color){return new Form{FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Bounds=bounds,BackColor=color,ShowInTaskbar=false,TopMost=true};}
 static async Task Run(){
  var area=Screen.PrimaryScreen.WorkingArea;var bounds=new Rectangle(area.Left+120,area.Top+120,240,140);
  using(var glass=Window(bounds,Color.Black)){
   GlassWindow.Configure(glass);glass.Show(owner);await Task.Delay(350);
   Check(Near(Capture(glass,"transparent-before"),owner.BackColor),"full-client glass shows the owned background instead of black");
   var disabled=new Margins();DwmExtendFrameIntoClientArea(glass.Handle,ref disabled);glass.Invalidate(true);await Task.Delay(180);
   Check(Near(Capture(glass,"composition-invalidated"),Color.Black),"fixture reproduces a lost transparent frame");
   SendMessage(glass.Handle,0x7E,new IntPtr(32),IntPtr.Zero);await Task.Delay(400);
   Check(Near(Capture(glass,"display-restored"),owner.BackColor),"display-change message restores transparency without recreating the HWND");
   DwmExtendFrameIntoClientArea(glass.Handle,ref disabled);glass.Invalidate(true);SendMessage(glass.Handle,0x31E,IntPtr.Zero,IntPtr.Zero);await Task.Delay(400);
   Check(Near(Capture(glass,"dwm-restored"),owner.BackColor)&&GlassWindow.RecoveryError=="","DWM composition change also restores the transparent client area");
  }
  var destination=new Rectangle(bounds.Left+300,bounds.Top,200,120);
  using(var a=Window(new Rectangle(bounds.Left,bounds.Top+210,200,120),Color.FromArgb(36,154,224)))
  using(var b=Window(new Rectangle(bounds.Left+230,bounds.Top+210,200,120),Color.FromArgb(226,96,54)))
  using(var plate=new DockPlate()){
   a.Show(owner);b.Show(owner);await Task.Delay(180);
   plate.Sync(a.Handle,owner.Handle,destination,destination,true);await Task.Delay(180);
   Check(Near(Capture(plate,"thumbnail-a"),a.BackColor),"Dock plate initially presents its registered source");
   SendMessage(plate.Handle,0x7E,new IntPtr(32),IntPtr.Zero);plate.Sync(b.Handle,owner.Handle,destination,destination,true);await Task.Delay(220);
   Check(Near(Capture(plate,"thumbnail-b-after-display"),b.BackColor),"display recovery binds a fresh source instead of retaining the old thumbnail");
   plate.Sync(a.Handle,owner.Handle,destination,destination,true);await Task.Delay(180);
   Check(Near(Capture(plate,"thumbnail-a-rebound"),a.BackColor),"source replacement at unchanged geometry updates the actual displayed pixels");plate.Hide();
   var entry=new DockEntry{windows=new List<DockWindow>{new DockWindow{handle=a.Handle.ToInt64()},new DockWindow{handle=b.Handle.ToInt64()}}};
   DockPreview.MotionAllowed=()=>false;
   using(var preview=new DockPreview(entry,new Rectangle(area.Left+20,area.Bottom-90,Math.Min(800,area.Width-40),70),false)){
    preview.Show(owner);await Task.Delay(80);var position=preview.Location;var handle=preview.Handle;
    Check(DockPreview.TryPromote(entry)&&preview.KeyboardMode&&preview.Handle==handle&&preview.Location==position,"hover preview becomes the keyboard picker in place without a second entrance or HWND");
    Check(Application.OpenForms.Cast<Form>().Count(f=>f is DockPreview&&f.Visible)==1,"promotion keeps exactly one visible preview");
    Check(DockPreview.TryPromote(new DockEntry{windows=new List<DockWindow>{entry.windows[0]}})&&preview.Location==position,"removing a source window rebuilds the preview in place instead of recentering under the pointer");
    DockPreview.MotionAllowed=()=>true;preview.Dismiss();preview.Revive();await Task.Delay(100);Check(!preview.IsDisposed&&preview.Visible,"reversing preview dismissal keeps the same live preview");
    DockPreview.CloseActive();Check(preview.IsDisposed&&!DockPreview.PickerOpen,"display or workspace teardown releases promoted picker ownership");
   }
   using(var anonymous=new DockPreview(new DockEntry{id=null,windows=entry.windows},new Rectangle(area.Left+20,area.Bottom-90,800,70),false)){
    anonymous.Show(owner);Check(!DockPreview.TryPromote(new DockEntry{id=null,windows=entry.windows}),"application-center entries without a Dock identity cannot promote another application's picker");
   }
  }
 }
 [STAThread]static int Main(string[] args){
  SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);output=args[0];Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"checks.txt"),"");int result=0;
  var area=Screen.PrimaryScreen.WorkingArea;owner=Window(new Rectangle(area.Left+80,area.Top+80,820,520),Color.FromArgb(175,58,179));owner.Text="PrismGlass display recovery fixture";
  owner.Shown+=async(s,e)=>{try{await Run();File.AppendAllText(Path.Combine(output,"checks.txt"),"Completed "+checks+" native recovery checks."+Environment.NewLine);}catch(Exception ex){result=1;File.AppendAllText(Path.Combine(output,"checks.txt"),ex+Environment.NewLine);}finally{owner.Close();}};
  Application.Run(owner);owner.Dispose();return result;
 }
}
