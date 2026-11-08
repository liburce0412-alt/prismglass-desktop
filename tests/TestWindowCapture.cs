using System;using System.Collections.Generic;using System.Diagnostics;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Linq;using System.Runtime.InteropServices;using System.Threading.Tasks;using System.Windows.Forms;
internal static class TestWindowCapture {
 [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")]static extern bool SetWindowDisplayAffinity(IntPtr window,uint affinity);
 static string output;static int checks;
 static void Check(bool value,string name){File.AppendAllText(output,(value?"PASS ":"FAIL ")+name+Environment.NewLine);if(!value)throw new Exception(name);checks++;}
 static async Task<Bitmap> Frame(WindowCapture capture){for(int i=0;i<80;i++){using(var pixels=await capture.Next()){if(pixels!=null){var image=new Bitmap(pixels.PixelWidth,pixels.PixelHeight,PixelFormat.Format32bppPArgb);var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.WriteOnly,image.PixelFormat);try{WindowCapture.Copy(pixels,bits.Scan0,bits.Stride);}finally{image.UnlockBits(bits);}return image;}}await Task.Delay(15);}throw new TimeoutException("No Windows Graphics Capture frame. "+capture.Diagnostic);}
 [STAThread]static void Main(string[] args){SetProcessDPIAware();Application.EnableVisualStyles();output=args[0];File.WriteAllText(output,"");var source=new Form{Text="PrismGlass isolated GPU capture",Size=new Size(1100,700),StartPosition=FormStartPosition.Manual,Location=new Point(220,170),BackColor=Color.FromArgb(30,190,60)};
  source.Shown+=async(s,e)=>{try{ShowWindow(source.Handle,4);await Task.Delay(150);bool borderless=await WindowCapture.Borderless();Check(borderless,"desktop capture receives the system's borderless permission");using(var capture=new WindowCapture(source.Handle,(uint)Process.GetCurrentProcess().Id,borderless)){
   using(var green=await Frame(capture))Check(green.GetPixel(450,350).G>150,"Windows Graphics Capture reads a real native window");
   source.BackColor=Color.FromArgb(30,60,210);source.Refresh();await Task.Delay(120);using(var blue=await Frame(capture))Check(blue.GetPixel(450,350).B>150,"captured pixels follow a changing background");
   var watch=Stopwatch.StartNew();int frames=0,draws=0;var costs=new List<double>();var ready=new TaskCompletionSource<bool>();capture.Available=()=>ready.TrySetResult(true);using(var producer=new Timer{Interval=1}){producer.Tick+=(a,b)=>{draws++;source.BackColor=Color.FromArgb(30,60+draws%120,210-draws%90);source.Refresh();};producer.Start();while(watch.ElapsedMilliseconds<2000){ready=new TaskCompletionSource<bool>();var start=Stopwatch.GetTimestamp();using(var pixels=await capture.Next()){if(pixels!=null){frames++;costs.Add((Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency);}else await Task.WhenAny(ready.Task,Task.Delay(100));}}producer.Stop();}
   costs.Sort();Check(frames>5,"continuous window updates produce fresh capture frames");File.AppendAllText(output,"INFO continuous producer="+draws+", captured="+frames+", seconds="+watch.Elapsed.TotalSeconds.ToString("F2")+", readback mean="+costs.Average().ToString("F2")+" ms, p95="+costs[(int)(costs.Count*.95)].ToString("F2")+" ms"+Environment.NewLine);
   source.Size=new Size(900,540);await Task.Delay(100);using(var resized=await Frame(capture))Check(resized.Width<1000,"capture pool follows a resized window");
   try{Check(SetWindowDisplayAffinity(source.Handle,0x11)&&await capture.Next()==null,"a newly protected source stops publishing capture frames");}finally{SetWindowDisplayAffinity(source.Handle,0);}
   capture.Dispose();Check(await capture.Next()==null,"closed capture cannot publish late frames");
  }Check(checks==7,"all capture checks completed");}catch(Exception ex){File.AppendAllText(output,ex.ToString()+Environment.NewLine);Environment.ExitCode=1;}finally{source.Close();}};Application.Run(source);
 }
}
