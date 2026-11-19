using System;using System.Diagnostics;using System.Drawing;using System.IO;using System.Reflection;using System.Threading.Tasks;using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
internal sealed class PreviewSource:Form {
 readonly Panel child=new Panel{Bounds=new Rectangle(10,10,20,20)};
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetWindowDisplayAffinity(IntPtr window,uint affinity);
 internal PreviewSource(){Text="ChatGPT";Bounds=new Rectangle(100,100,900,700);StartPosition=FormStartPosition.Manual;TopMost=true;BackColor=Color.FromArgb(25,195,95);Controls.Add(child);}
 protected override void WndProc(ref Message m){if(m.Msg==0x8002){m.Result=child.Handle;return;}if(m.Msg==0x8003){m.Result=new IntPtr(SetWindowDisplayAffinity(Handle,(uint)m.WParam.ToInt32())?1:0);return;}base.WndProc(ref m);}
}
internal static class TestDockPreviewRendering {
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 static string root,output;static Form owner;static Process sourceProcess;static DockWindow source;static int checks;static readonly BindingFlags Fields=BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static void Check(bool ok,string message){File.AppendAllText(Path.Combine(output,"checks.txt"),(ok?"PASS ":"FAIL ")+message+Environment.NewLine);if(!ok)throw new Exception(message);checks++;}
 static async Task Wait(Func<bool> ready,int milliseconds){var watch=Stopwatch.StartNew();while(!ready()&&watch.ElapsedMilliseconds<milliseconds)await Task.Delay(25);}
 static async Task Inspect(DockPreview preview,string name){
  using(var bitmap=new Bitmap(preview.Width,preview.Height))using(var g=Graphics.FromImage(bitmap)){g.CopyFromScreen(preview.Location,Point.Empty,bitmap.Size);bitmap.Save(Path.Combine(output,name+".png"));}
  var host=typeof(NativeGlassMaterial).GetField("host",Fields).GetValue(null) as Form;if(host==null)return;
  var task=(Task)host.GetType().GetProperty("Ready").GetValue(host,null);var view=(WebView2)host.Controls[0];
  string state="not initialized";if(view.CoreWebView2!=null)state=await view.CoreWebView2.ExecuteScriptAsync("JSON.stringify({visibility:document.visibilityState,size:[innerWidth,innerHeight],canvas:[document.querySelector('canvas').width,document.querySelector('canvas').height],resources:performance.getEntriesByType('resource').map(r=>[r.name,r.duration])})");
  File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO "+name+" ready="+(task==null?"null":task.Status.ToString())+" image="+(preview.BackgroundImage!=null)+" state="+state+Environment.NewLine);
 }
 static DockEntry Entry(){return new DockEntry{id="codex-fixture",name="Codex",windows=new System.Collections.Generic.List<DockWindow>{source}};}
 static async Task Run(){
  using(var wallpaper=new Bitmap(900,700))using(var g=Graphics.FromImage(wallpaper)){g.Clear(Color.FromArgb(90,125,175));for(int x=0;x<900;x+=90)g.FillRectangle(Brushes.CornflowerBlue,x,0,35,700);wallpaper.Save(Path.Combine(root,"wallpaper.png"));}
  sourceProcess=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--source"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});await Wait(()=>{sourceProcess.Refresh();return sourceProcess.MainWindowHandle!=IntPtr.Zero;},2000);await Task.Delay(250);sourceProcess.Refresh();source=new DockWindow{handle=sourceProcess.MainWindowHandle.ToInt64(),pid=(uint)sourceProcess.Id,path=Application.ExecutablePath,title="ChatGPT"};
  Check(source.handle!=0,"an independently painted source HWND is available");DockPreview.MotionAllowed=()=>false;NativeGlassMaterial.Register(root);var watch=Stopwatch.StartNew();
  await Wait(()=>{var warmed=typeof(NativeGlassMaterial).GetField("host",Fields).GetValue(null) as Form;if(warmed==null)return false;var task=(Task)warmed.GetType().GetProperty("Ready").GetValue(warmed,null);return task!=null&&task.Status==TaskStatus.RanToCompletion&&!warmed.Controls[0].Visible;},8500);
  var prepared=typeof(NativeGlassMaterial).GetField("host",Fields).GetValue(null) as Form;Check(prepared!=null&&((Task)prepared.GetType().GetProperty("Ready").GetValue(prepared,null)).Status==TaskStatus.RanToCompletion&&!prepared.Controls[0].Visible,"one idle-prewarmed material host is ready and hidden before any popup opens");File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO startup prewarm milliseconds="+watch.ElapsedMilliseconds+Environment.NewLine);watch.Restart();
  using(var preview=new DockPreview(Entry(),new Rectangle(100,700,800,80),false)){
   preview.PlaceAt(new Point(180,180));preview.Show();await Wait(()=>preview.BackgroundImage!=null,2000);await Inspect(preview,"first-hover");
   File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO first-hover material milliseconds="+watch.ElapsedMilliseconds+Environment.NewLine);
   Check(preview.BackgroundImage!=null,"first preview receives an actual material bitmap");
   Check(Object.ReferenceEquals(prepared,typeof(NativeGlassMaterial).GetField("host",Fields).GetValue(null))&&watch.ElapsedMilliseconds<1000,"first hover reuses the prewarmed host without a cold renderer or five-second penalty");
   using(var image=new Bitmap(preview.Width,preview.Height))using(var g=Graphics.FromImage(image)){g.CopyFromScreen(preview.Location,Point.Empty,image.Size);var pixel=image.GetPixel(128,85);Check(pixel.G>120&&pixel.G>pixel.R+30,"DWM paints the exact capturable source above its glass background");}
   ShowWindow(new IntPtr(source.handle),6);await Wait(()=>preview.PreviewState(0)==DockPreview.ThumbnailState.Minimized,1000);Check(preview.PreviewState(0)==DockPreview.ThumbnailState.Minimized,"minimized source is marked as retained content rather than waiting for a new frame");await Inspect(preview,"minimized");
   ShowWindow(new IntPtr(source.handle),9);await Wait(()=>preview.PreviewState(0)==DockPreview.ThumbnailState.Submitted,1000);Check(preview.PreviewState(0)==DockPreview.ThumbnailState.Submitted,"restoring the same source updates its existing DWM relationship");
   Check(SendMessage(new IntPtr(source.handle),0x8003,new IntPtr(0x11),IntPtr.Zero)!=IntPtr.Zero,"fixture source explicitly opts out of capture");await Wait(()=>preview.PreviewState(0)==DockPreview.ThumbnailState.Protected,1000);Check(preview.PreviewState(0)==DockPreview.ThumbnailState.Protected,"capture-protected source releases its thumbnail and explains the unavailable preview");await Inspect(preview,"protected");
   SendMessage(new IntPtr(source.handle),0x8003,IntPtr.Zero,IntPtr.Zero);await Wait(()=>preview.PreviewState(0)==DockPreview.ThumbnailState.Submitted,1000);Check(preview.PreviewState(0)==DockPreview.ThumbnailState.Submitted,"removing fixture capture protection recreates its permitted preview");
   preview.Close();Check(preview.BackgroundImage==null,"closing preview releases its native material image");
  }
  var unavailable=Entry();unavailable.windows=new System.Collections.Generic.List<DockWindow>{new DockWindow{handle=SendMessage(new IntPtr(source.handle),0x8002,IntPtr.Zero,IntPtr.Zero).ToInt64(),pid=source.pid,path=source.path,title="Unsupported child HWND"}};
  using(var preview=new DockPreview(unavailable,new Rectangle(100,700,800,80),false)){
   preview.PlaceAt(new Point(180,180));preview.Show();await Task.Delay(50);File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO rejected-source state="+preview.PreviewState(0)+Environment.NewLine);Check(preview.PreviewState(0)==DockPreview.ThumbnailState.Pending,"a real rejected non-top-level DWM source enters a visible bounded pending state");await Wait(()=>preview.PreviewState(0)==DockPreview.ThumbnailState.Unavailable,1800);Check(preview.PreviewState(0)==DockPreview.ThumbnailState.Unavailable,"DWM registration failure leaves pending after three attempts instead of an empty card");await Inspect(preview,"unavailable");preview.Close();
  }
  using(var preview=new DockPreview(Entry(),new Rectangle(100,700,800,80),false)){
   preview.PlaceAt(new Point(180,180));watch.Restart();preview.Show();await Wait(()=>preview.BackgroundImage!=null,2000);await Inspect(preview,"reopened");
   Check(preview.BackgroundImage!=null,"reopening a preview resumes the shared material renderer");File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO warm material milliseconds="+watch.ElapsedMilliseconds+Environment.NewLine);preview.Close();
  }
  File.AppendAllText(Path.Combine(output,"checks.txt"),"Completed "+checks+" native preview rendering checks."+Environment.NewLine);
 }
 [STAThread]static int Main(string[] args){SetProcessDpiAwarenessContext(new IntPtr(-4));Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);if(args[0]=="--source"){Application.Run(new PreviewSource());return 0;}root=args[0];output=args[1];int exit=0;owner=new Form{ShowInTaskbar=false,Opacity=0};owner.Shown+=async(s,e)=>{try{await Run();}catch(Exception error){File.AppendAllText(Path.Combine(output,"checks.txt"),error+Environment.NewLine);exit=1;}finally{File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO stopping source"+Environment.NewLine);if(sourceProcess!=null){if(!sourceProcess.HasExited){sourceProcess.CloseMainWindow();if(!sourceProcess.WaitForExit(500))sourceProcess.Kill();}sourceProcess.Dispose();}File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO stopping controller"+Environment.NewLine);owner.Close();File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO controller closed"+Environment.NewLine);}};Application.Run(owner);owner.Dispose();File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO message loop exited"+Environment.NewLine);return exit;}
}
