using System;using System.Diagnostics;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Runtime.InteropServices;using System.Threading.Tasks;using System.Windows.Forms;
internal sealed class StageThumbnailSource:Form {
 protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
 internal StageThumbnailSource(){Text="PrismGlass owned thumbnail source";Bounds=new Rectangle(120,120,480,340);StartPosition=FormStartPosition.Manual;BackColor=Color.FromArgb(25,195,95);}
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.FillRectangle(Brushes.Crimson,40,40,80,55);e.Graphics.DrawString("Owned DWM source",SystemFonts.CaptionFont,Brushes.White,35,120);}
 [DllImport("user32.dll")]static extern bool SetWindowDisplayAffinity(IntPtr window,uint affinity);
 protected override void WndProc(ref Message m){if(m.Msg==0x8003){m.Result=new IntPtr(SetWindowDisplayAffinity(Handle,(uint)m.WParam.ToInt32())?1:0);return;}base.WndProc(ref m);}
}
internal sealed class StageThumbnailHost:Form {
 [StructLayout(LayoutKind.Sequential)]struct RECT {internal int l,t,r,b;internal RECT(int width,int height){l=t=0;r=width;b=height;}}
 [StructLayout(LayoutKind.Sequential)]struct SIZE {internal int x,y;}
 [StructLayout(LayoutKind.Sequential)]struct PROPS {internal uint flags;internal RECT dest,source;internal byte opacity;[MarshalAs(UnmanagedType.Bool)]internal bool visible;[MarshalAs(UnmanagedType.Bool)]internal bool client;}
 [DllImport("dwmapi.dll")]static extern int DwmRegisterThumbnail(IntPtr target,IntPtr source,out IntPtr thumbnail);
 [DllImport("dwmapi.dll")]static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail,out SIZE size);
 [DllImport("dwmapi.dll")]static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,ref PROPS props);
 [DllImport("dwmapi.dll")]static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
 [DllImport("user32.dll")]static extern bool GetWindowDisplayAffinity(IntPtr source,out uint affinity);
 IntPtr thumbnail;
 internal StageThumbnailHost(Point at){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Bounds=new Rectangle(at,new Size(320,240));BackColor=Color.Magenta;TopMost=true;}
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
 internal bool Attach(IntPtr source){uint affinity;if(GetWindowDisplayAffinity(source,out affinity)&&affinity!=0)return false;if(DwmRegisterThumbnail(Handle,source,out thumbnail)!=0)return false;SIZE size;if(DwmQueryThumbnailSourceSize(thumbnail,out size)!=0||size.x<1||size.y<1)return false;var props=new PROPS{flags=31,dest=new RECT(ClientSize.Width,ClientSize.Height),source=new RECT(size.x,size.y),opacity=255,visible=true,client=false};return DwmUpdateThumbnailProperties(thumbnail,ref props)==0;}
 protected override void Dispose(bool disposing){if(thumbnail!=IntPtr.Zero){DwmUnregisterThumbnail(thumbnail);thumbnail=IntPtr.Zero;}base.Dispose(disposing);}
}
internal static class TestStageThumbnailProbe {
 [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr source,int command);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr source);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr source,uint message,IntPtr w,IntPtr l);
 static string output;static int checks,exit;static Process sourceProcess;static Form owner;
 static void Check(bool ok,string message){File.AppendAllText(Path.Combine(output,"checks.txt"),(ok?"PASS ":"FAIL ")+message+Environment.NewLine);if(!ok)throw new Exception(message);checks++;}
 static async Task Wait(Func<bool> ready,int maximum=1400){var clock=Stopwatch.StartNew();while(!ready()&&clock.ElapsedMilliseconds<maximum)await Task.Delay(20);if(!ready())throw new TimeoutException("Owned thumbnail fixture did not settle.");}
 static async Task<Bitmap> Read(IntPtr window,uint pid){using(var capture=new WindowCapture(window,pid,false)){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<1200){using(var pixels=await capture.Next(640,640*480)){if(pixels!=null){var bitmap=new Bitmap(pixels.PixelWidth,pixels.PixelHeight,PixelFormat.Format32bppArgb);var data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.WriteOnly,bitmap.PixelFormat);try{WindowCapture.Copy(pixels,data.Scan0,data.Stride);}finally{bitmap.UnlockBits(data);}return bitmap;}}await Task.Delay(20);}return null;}}
 static bool Green(Bitmap image){if(image==null)return false;int green=0,red=0;for(int y=1;y<12;y++)for(int x=1;x<12;x++){var p=image.GetPixel(image.Width*x/12,image.Height*y/12);if(p.G>130&&p.G>p.R+45&&p.G>p.B+30)green++;if(p.R>140&&p.R>p.G+60)red++;}return green>50&&red>1;}
 static async Task Run(){
  IntPtr foreground=GetForegroundWindow();sourceProcess=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--source"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});await Wait(()=>{sourceProcess.Refresh();return sourceProcess.MainWindowHandle!=IntPtr.Zero;});var source=sourceProcess.MainWindowHandle;
  using(var initial=await Read(source,(uint)sourceProcess.Id)){Check(Green(initial),"independent source really paints green content and a red marker");initial.Save(Path.Combine(output,"source.png"));}
  ShowWindowAsync(source,6);await Wait(()=>IsIconic(source));Check(IsIconic(source),"source is minimized before any DWM relationship is registered");
  using(var host=new StageThumbnailHost(new Point(180,180))){host.Show();Check(host.Attach(source),"DWM accepts the already minimized source");await Task.Delay(120);using(var frame=await Read(host.Handle,(uint)Process.GetCurrentProcess().Id)){if(frame!=null)frame.Save(Path.Combine(output,"onscreen-host.png"));Check(Green(frame),"WGC reads real retained content from the onscreen DWM host");}Check(IsIconic(source),"onscreen sampling does not restore the source");}
  var area=SystemInformation.VirtualScreen;
  using(var host=new StageThumbnailHost(new Point(area.Right+160,area.Top+40))){host.Show();Check(!host.Bounds.IntersectsWith(area),"new DWM host starts entirely outside every display");Check(host.Attach(source),"fresh offscreen host accepts the minimized source");using(var frame=await Read(host.Handle,(uint)Process.GetCurrentProcess().Id)){if(frame!=null)frame.Save(Path.Combine(output,"offscreen-host.png"));Check(Green(frame),"fresh offscreen DWM host yields the actual retained source pixels");}Check(IsIconic(source),"offscreen sampling keeps the source minimized");}
  Check(SendMessage(source,0x8003,new IntPtr(0x11),IntPtr.Zero)!=IntPtr.Zero,"owned source enables capture exclusion");using(var protectedHost=new StageThumbnailHost(new Point(area.Right+160,area.Top+40))){protectedHost.Show();Check(!protectedHost.Attach(source),"capture-excluded source is rejected before DWM registration");}Check(IsIconic(source),"protected-source check preserves minimized state");Check(GetForegroundWindow()==foreground,"probe never changes foreground ownership");
  File.AppendAllText(Path.Combine(output,"checks.txt"),"Completed "+checks+" owned DWM to WGC pixel checks."+Environment.NewLine);
 }
 [STAThread]static int Main(string[] args){SetProcessDpiAwarenessContext(new IntPtr(-4));Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();if(args[0]=="--source"){Application.Run(new StageThumbnailSource());return 0;}output=args[0];File.WriteAllText(Path.Combine(output,"checks.txt"),"");owner=new StageThumbnailHost(new Point(SystemInformation.VirtualScreen.Right+520,SystemInformation.VirtualScreen.Top+40)){Opacity=0};owner.Shown+=async(s,e)=>{try{await Run();}catch(Exception e1){File.AppendAllText(Path.Combine(output,"checks.txt"),e1+Environment.NewLine);exit=1;}finally{if(sourceProcess!=null){if(!sourceProcess.HasExited){sourceProcess.CloseMainWindow();if(!sourceProcess.WaitForExit(500))sourceProcess.Kill();}sourceProcess.Dispose();}owner.Close();}};Application.Run(owner);owner.Dispose();return exit;}
}
