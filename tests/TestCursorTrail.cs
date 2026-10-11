using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Exercises native alpha rendering, hit testing, resource lifetime and idle behavior.
class TestCursorTrail : Form {
 readonly CursorTrail trail=new CursorTrail();readonly Timer timer=new Timer{Interval=16};
 readonly string output;int frame,baseline,peak,styleIndex;Point point;Bitmap preview;
 readonly string[] styles={"particles","stars","ribbon"};
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point p);
 [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr h,int index);
 [DllImport("user32.dll")]static extern int GetGuiResources(IntPtr process,int flag);
 [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 void Check(bool value,string name){File.AppendAllText(Path.Combine(output,"cursor-test.txt"),(value?"PASS ":"FAIL ")+name+Environment.NewLine);if(!value)throw new Exception(name);}
 public TestCursorTrail(string folder){output=folder;Text="PrismGlass Pointer Test";Size=new Size(840,400);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(27,32,44);DoubleBuffered=true;
  Controls.Add(new Label{Text="Native particle rendering / click-through / idle recovery",ForeColor=Color.White,AutoSize=true,Location=new Point(30,25)});
  var button=new Button{Text="Input still works",Bounds=new Rectangle(30,300,180,36)};button.Click+=delegate{button.Text="Click received";File.AppendAllText(Path.Combine(output,"cursor-test.txt"),"PASS pointer clicks underlying button"+Environment.NewLine);};Controls.Add(button);
  Shown+=delegate{baseline=GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle,0);timer.Start();};
  timer.Tick+=Step;FormClosed+=delegate{timer.Dispose();trail.Dispose();if(preview!=null)preview.Dispose();};
 }
 void Step(object sender,EventArgs args){try{
  frame++;
  if(frame<=180){point=PointToScreen(new Point(160+(frame*3)%460,160+(int)(45*Math.Sin(frame*.055))));trail.Update(point,true);
   peak=Math.Max(peak,GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle,0));
   if(frame==90){Check(trail.Visible,"native "+styles[styleIndex]+" visible while moving");Check((GetWindowLong(trail.Handle,-20)&0x080800A0)==0x080800A0,"alpha overlay is transparent and nonactivating");Check(WindowFromPoint(point)!=trail.Handle,"trail does not intercept pointer");Check(GetForegroundWindow()!=trail.Handle,"trail does not steal focus");
    var surface=(Bitmap)typeof(CursorTrail).GetField("surface",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(trail);if(preview!=null)preview.Dispose();preview=new Bitmap(surface);preview.Save(Path.Combine(output,"trail-"+styles[styleIndex]+".png"),ImageFormat.Png);Invalidate();}
  }else if(frame<=235)trail.Update(point,true);
  else {Check(!trail.Visible,"stationary "+styles[styleIndex]+" disappears");
   trail.Configure(new CursorPreferences{trailEnabled=false},100);trail.Update(new Point(point.X+20,point.Y),true);Check(!trail.Visible,"off switch prevents further rendering");
   styleIndex++;
   if(styleIndex<styles.Length){trail.Configure(new CursorPreferences{trailStyle=styles[styleIndex],trailColor=styleIndex==1?"#B29EEA":"#7BD6BB"},100);frame=0;return;}
   trail.Dispose();Check(peak-baseline<35,"GDI resources stay bounded during style switching");Check(GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle,0)<=baseline+12,"native rendering resources released");timer.Stop();Text="PrismGlass Pointer Test - passed";Close();}
 }catch(Exception ex){timer.Stop();File.AppendAllText(Path.Combine(output,"cursor-test.txt"),ex+Environment.NewLine);Environment.ExitCode=1;Close();}}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(preview!=null){e.Graphics.DrawImageUnscaled(preview,30,95);}}
 [STAThread]static void Main(string[] args){SetProcessDPIAware();Application.EnableVisualStyles();Application.Run(new TestCursorTrail(args.Length>0?args[0]:AppDomain.CurrentDomain.BaseDirectory));}
}
