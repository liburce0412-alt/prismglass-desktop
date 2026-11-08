using System;using System.Diagnostics;using System.Drawing;using System.IO;using System.Runtime.InteropServices;using System.Threading.Tasks;using System.Windows.Forms;using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.WinForms;
internal class GlassCaptureHost : Form {
 internal Action<long> Probe;
 protected override CreateParams CreateParams{get{var value=base.CreateParams;value.ExStyle|=0x80;return value;}}
 protected override void WndProc(ref Message message){if(message.Msg==0x8050){var action=Probe;if(action!=null)action(message.WParam.ToInt64());return;}base.WndProc(ref message);}
}
internal sealed class LiveBackdropFixture : GlassCaptureHost {
 readonly Timer animation=new Timer{Interval=33};int frame;
 public LiveBackdropFixture(){DoubleBuffered=true;animation.Tick+=(s,e)=>{frame++;BackColor=Color.FromArgb(50+(int)(35+35*Math.Sin(frame*.07)),75+(int)(50+50*Math.Sin(frame*.09)),145+(int)(40+40*Math.Cos(frame*.05)));Invalidate();};}
 public void ResetHandle(){RecreateHandle();}
 protected override bool ShowWithoutActivation{get{return true;}}
 protected override void WndProc(ref Message message){if(message.Msg==0x8004){Bounds=Screen.PrimaryScreen.Bounds;Opacity=1;TopMost=true;animation.Start();return;}if(message.Msg==0x8005){animation.Stop();return;}if(message.Msg==0x8001){BackColor=Color.FromArgb(message.WParam.ToInt32());Invalidate();return;}if(message.Msg==0x8002){Opacity=0;TopMost=true;Location=new Point(message.WParam.ToInt32(),message.LParam.ToInt32());return;}if(message.Msg==0x8003){Opacity=1;TopMost=false;Location=new Point(message.WParam.ToInt32(),message.LParam.ToInt32());return;}base.WndProc(ref message);}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var pen=new Pen(Color.FromArgb(160,255,255,255),3))for(int x=25+frame%75;x<Width;x+=75)e.Graphics.DrawLine(pen,x,0,x,Height);}
 protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
}
internal static partial class TestLiveGlassBackdrop {
 static Form form;static WebView2 view;static string root,output;static Process sourceProcess;static int checks,requests;static bool bodyOnly,dockOnly,multiHostOnly;static OpticalBackdrop optical;
 [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string title);
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT bounds);
 [DllImport("user32.dll")]static extern bool GetWindowDisplayAffinity(IntPtr window,out uint affinity);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [StructLayout(LayoutKind.Sequential)]struct RECT {public int left,top,right,bottom;public Rectangle Rectangle{get{return Rectangle.FromLTRB(left,top,right,bottom);}}}
 static Task<string> Eval(string script){return view.CoreWebView2.ExecuteScriptAsync(script);}
 static void Check(bool value,string name){File.AppendAllText(Path.Combine(output,"checks.txt"),(value?"PASS ":"FAIL ")+name+Environment.NewLine);if(!value)throw new Exception(name);checks++;}
 static async Task Wait(string expression,string name){for(int i=0;i<70;i++){if(await Eval(expression)=="true"){Check(true,name);return;}await Task.Delay(100);}throw new Exception("Timeout: "+name+"; native="+(optical==null?"":optical.Error)+"; renderer="+await Eval("JSON.stringify(stats())"));}
 static async Task Save(string name){using(var stream=File.Create(Path.Combine(output,name+".png")))await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}
 static async Task NativeWait(Func<bool> condition,string name){for(int i=0;i<100;i++){if(condition()){Check(true,name);return;}await Task.Delay(100);}throw new Exception("Timeout: "+name);}
 static string Pixels(Image image){if(image==null)return "";using(var stream=new MemoryStream()){image.Save(stream,System.Drawing.Imaging.ImageFormat.Png);return Convert.ToBase64String(stream.ToArray());}}
 static Color CompositedPixel(string name){using(var bitmap=new Bitmap(form.ClientSize.Width,form.ClientSize.Height))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(form.PointToScreen(Point.Empty),Point.Empty,bitmap.Size);bitmap.Save(Path.Combine(output,name+".png"));return bitmap.GetPixel(330,330);}}
 static Color ScreenPixel(int x,int y){using(var bitmap=new Bitmap(1,1))using(var graphics=Graphics.FromImage(bitmap)){graphics.CopyFromScreen(form.PointToScreen(new Point(x,y)),Point.Empty,bitmap.Size);return bitmap.GetPixel(0,0);}}
 static double Linear(byte value){double channel=value/255.0;return channel<=.04045?channel/12.92:Math.Pow((channel+.055)/1.055,2.4);}
 static void Clip(Region region){var old=form.Region;form.Region=region;if(old!=null)old.Dispose();}
 static async Task DockLayers(IntPtr front){
  using(var back=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--source"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}))using(var plate=new GlassCaptureHost{ClientSize=new Size(800,120),StartPosition=FormStartPosition.Manual,FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,TopMost=true,Opacity=.01}){
   try{
    IntPtr behind=IntPtr.Zero;for(int i=0;i<50;i++){behind=FindWindow(null,"PrismGlass material test source "+back.Id);if(behind!=IntPtr.Zero)break;await Task.Delay(100);}Check(behind!=IntPtr.Zero,"isolated rear wallpaper-color fixture is available");
    var origin=new Point(180,140);form.Location=origin;form.Opacity=1;
    SendMessage(behind,0x8001,new IntPtr(Color.FromArgb(30,55,190).ToArgb()),IntPtr.Zero);SendMessage(behind,0x8003,new IntPtr(origin.X),new IntPtr(origin.Y));SendMessage(front,0x8003,new IntPtr(origin.X),new IntPtr(origin.Y));
    SetWindowPos(behind,new IntPtr(-1),0,0,0,0,0x13);SetWindowPos(form.Handle,new IntPtr(-1),0,0,0,0,0x13);SetWindowPos(front,new IntPtr(-1),0,0,0,0,0x13);
    var dock=new Rectangle(origin.X+100,origin.Y+500,800,120);plate.Bounds=dock;plate.Show();SetWindowPos(plate.Handle,new IntPtr(-1),0,0,0,0,0x13);
    Check(!LiveGlassBackdrop.Sources(form.Handle,form.Bounds).Exists(s=>s.handle==front)&&LiveGlassBackdrop.Sources(plate.Handle,dock).Exists(s=>s.handle==front),"test places the application above the widget host but behind the Dock plate");
    await Eval("receive({type:'layout',width:innerWidth,height:innerHeight,sceneWidth:innerWidth,sceneHeight:innerHeight,theme:'Sky',paused:false,cards:[{name:'Widget',x:80,y:60,w:240,h:180,r:24,fixedShape:true},{name:'Dock',x:100,y:500,w:800,h:120,r:30,fixedShape:true}]});window.sample=(x,y)=>{const g=document.querySelector('canvas').getContext('webgl'),p=new Uint8Array(4);g.readPixels(x,g.drawingBufferHeight-y,1,1,g.RGBA,g.UNSIGNED_BYTE,p);return Array.from(p);};window.dominant=(x,y,c)=>{const p=sample(x,y);return p[c]>p[(c+1)%3]+25&&p[c]>p[(c+2)%3]+25;};enable(true)");LiveGlassBackdrop.Configure(root,true,false);
    await Wait("stats().liveBackground==='optical'&&dominant(200,140,2)&&dominant(500,560,2)","without a raised reference both glass cards sample the blue rear window");
    optical.SetOverlay(plate.Handle,dock);await Wait("dominant(200,140,2)&&dominant(500,560,1)","the raised Dock samples green while the widget keeps its blue Z-order background");
    SendMessage(front,0x8001,new IntPtr(Color.FromArgb(205,36,46).ToArgb()),IntPtr.Zero);await Wait("dominant(200,140,2)&&dominant(500,560,0)","live foreground changes remain assigned to the Dock while widgets stay unchanged");
    await Wait("dominant(500,507,0)&&dominant(110,560,0)&&dominant(890,560,0)&&dominant(500,613,0)","Dock bevel samples the foreground at every edge without a rear blue fringe");await Save("dock-red-widget-blue");
    optical.SetOverlay(IntPtr.Zero,Rectangle.Empty);await Wait("dominant(200,140,2)&&dominant(500,560,2)","removing the raised Dock reference restores the original source without stale pixels");
    File.AppendAllText(Path.Combine(output,"checks.txt"),"Completed "+checks+" focused native Dock-layer checks."+Environment.NewLine);
   }finally{optical.SetOverlay(IntPtr.Zero,Rectangle.Empty);if(!back.HasExited){back.CloseMainWindow();if(!back.WaitForExit(1000))back.Kill();}}
  }
 }
 static async Task NativeComposition(IntPtr source){
  form.Location=new Point(180,140);SendMessage(source,0x8003,new IntPtr(180),new IntPtr(140));SetWindowPos(source,new IntPtr(-1),0,0,0,0,0x13);form.Opacity=1;SetWindowPos(form.Handle,new IntPtr(-1),0,0,0,0,0x13);
  Clip(new Region(new Rectangle(250,0,750,720)));LiveGlassBackdrop.Configure(root,false,false);await Task.Delay(200);var outside=ScreenPixel(100,330);
  LiveGlassBackdrop.Configure(root,true,false);await Eval("layout(false);enable(true)");
  await Wait("stats().liveBackground==='optical'","WebView glass refracts native window pixels through the shared texture bridge");int before=requests;
  await Task.Delay(200);Check(ScreenPixel(100,330).ToArgb()==outside.ToArgb(),"optical glass never paints outside the window's visible region");
  SendMessage(source,0x8001,new IntPtr(Color.FromArgb(30,210,65).ToArgb()),IntPtr.Zero);await Task.Delay(650);var green=CompositedPixel("compositor-green");
  SendMessage(source,0x8001,new IntPtr(Color.FromArgb(35,65,230).ToArgb()),IntPtr.Zero);await Task.Delay(650);var blue=CompositedPixel("compositor-blue");
  File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO native pixels: "+green+" -> "+blue+Environment.NewLine);
  Check(Math.Abs(green.G-blue.G)+Math.Abs(green.B-blue.B)>8,"actual refracted pixels follow a background app without JPEG frames");
  using(var grid=new Bitmap(Path.Combine(output,"compositor-blue.png"))){int direct=900,bent=900;for(int x=901;x<936;x++){if(grid.GetPixel(x,30).G>grid.GetPixel(direct,30).G)direct=x;if(grid.GetPixel(x,330).G>grid.GetPixel(bent,330).G)bent=x;}Check(Math.Abs(direct-bent)>=3,"the real background grid bends at the lens edge, rather than only changing tint");}
  await Eval("receive({type:'pointer',x:250,y:330,down:false})");await Task.Delay(400);CompositedPixel("body-pointer-left");
  await Eval("receive({type:'pointer',x:750,y:330,down:false})");await Task.Delay(400);CompositedPixel("body-pointer-right");
  using(var left=new Bitmap(Path.Combine(output,"body-pointer-left.png")))using(var right=new Bitmap(Path.Combine(output,"body-pointer-right.png"))){int a=450,b=450;for(int x=451;x<515;x++){if(left.GetPixel(x,330).G>left.GetPixel(a,330).G)a=x;if(right.GetPixel(x,330).G>right.GetPixel(b,330).G)b=x;}Check(Math.Abs(a-b)<=2,"fixed glass body keeps the real background grid anchored while the pointer crosses it");}
  await Eval("receive({type:'pointer',x:-999,y:-999,down:false})");
  SendMessage(source,0x8001,new IntPtr(Color.White.ToArgb()),IntPtr.Zero);await Task.Delay(650);var white=CompositedPixel("compositor-white");double contrast=1.05/(.2126*Linear(white.R)+.7152*Linear(white.G)+.0722*Linear(white.B)+.05);
  Check((white.R+white.G+white.B)/3.0>=180,"clear glass transmits a bright background without the former gray contrast veil");
  File.AppendAllText(Path.Combine(output,"checks.txt"),"INFO clear-glass white-text contrast: "+contrast.ToString("F2")+":1; reduced contrast is the selected visual tradeoff."+Environment.NewLine);
  Clip(new Region(new Rectangle(400,0,600,720)));await Task.Delay(200);Check(ScreenPixel(330,330).ToArgb()==Color.White.ToArgb(),"shrinking a live region leaves no stale backdrop behind");
  var islands=new Region(new Rectangle(250,0,200,720));islands.Union(new Rectangle(750,0,250,720));Clip(islands);await Task.Delay(200);
  Check(ScreenPixel(580,330).ToArgb()==Color.White.ToArgb()&&ScreenPixel(330,330).R<white.R+3,"disconnected glass regions preserve their clear gap");
  var empty=new Region();empty.MakeEmpty();Clip(empty);await Task.Delay(200);Check(ScreenPixel(330,330).ToArgb()==Color.White.ToArgb(),"an empty region releases all visible backdrop strips");
  Clip(new Region(new Rectangle(250,0,750,720)));
  Check(before==requests,"optical background sends no per-frame PrintWindow or JPEG requests");
  LiveGlassBackdrop.Configure(root,true,true);await Eval("layout(true)");await Wait("stats().liveBackground==='wallpaper'","suspending native material restores the retained wallpaper shader");
  LiveGlassBackdrop.Configure(root,false,false);await Eval("layout(false);enable(false)");await Wait("stats().liveBackground==='wallpaper'&&stats().theme==='Sky'&&!stats().errors.length","native opt-out retains Sky wallpaper after pointer interaction");
  form.Opacity=0;
 }
 static async Task NativeMaterial(IntPtr source){
  NativeGlassMaterial.Register(root);var settings=new DesktopPreferences(root);var entry=new DockEntry{id="fixture",name="PrismGlass · 材质检查"};entry.windows.Add(new DockWindow{handle=source.ToInt64(),pid=(uint)sourceProcess.Id,title="独立测试窗口",path=Application.ExecutablePath});
  SendMessage(source,0x8002,new IntPtr(100),new IntPtr(100));
  using(var preview=new DockPreview(entry,new Rectangle(100,500,400,60),false)){
   preview.PlaceAt(new Point(100,100));preview.Opacity=0;preview.Show();
   await NativeWait(()=>preview.BackgroundImage!=null,"native Dock preview receives the shared WebGL material");
   Check(!preview.KeyboardMode&&preview.AccessibleName.Contains("窗口预览"),"material preserves preview activation mode and accessible name");
   var thumbs=(System.Collections.ICollection)typeof(DockPreview).GetField("thumbs",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(preview);Check(thumbs.Count==1,"native DWM thumbnail remains registered beneath preview controls");
   using(var image=new Bitmap(preview.Width,preview.Height)){preview.DrawToBitmap(image,preview.ClientRectangle);image.Save(Path.Combine(output,"native-preview-sky.png"));}
   string sky=Pixels(preview.BackgroundImage);settings.Apply(new System.Collections.Generic.Dictionary<string,object>{{"theme","Astro"}},settings.Revision);
   await NativeWait(()=>Pixels(preview.BackgroundImage)!=sky,"open native preview follows the Astro material setting");
   preview.BackgroundImage.Save(Path.Combine(output,"native-material-astro.png"));
   settings.Apply(new System.Collections.Generic.Dictionary<string,object>{{"theme","Sky"},{"liveGlassBackground",true}},settings.Revision);
   SendMessage(source,0x8003,new IntPtr(100),new IntPtr(100));preview.Opacity=1;
   SetWindowPos(source,new IntPtr(-1),0,0,0,0,0x13);SetWindowPos(preview.Handle,new IntPtr(-1),0,0,0,0,0x13);
   await Task.Delay(800);string blue=Pixels(preview.BackgroundImage);SendMessage(source,0x8001,new IntPtr(Color.FromArgb(42,160,95).ToArgb()),IntPtr.Zero);
   await NativeWait(()=>Pixels(preview.BackgroundImage)!=blue,"native preview refracts changing background pixels without a compositor layer covering its foreground");
   using(var shot=new Bitmap(preview.Width,preview.Height))using(var graphics=Graphics.FromImage(shot)){graphics.CopyFromScreen(preview.Location,Point.Empty,shot.Size);shot.Save(Path.Combine(output,"native-preview-optical.png"));int ink=0;for(int y=8;y<26;y++)for(int x=12;x<preview.Width-12;x++){var pixel=shot.GetPixel(x,y);if(pixel.R>225&&pixel.G>225&&pixel.B>225)ink++;}Check(ink>20,"the real glass preview retains its painted title above the refracted background");}
   settings.Apply(new System.Collections.Generic.Dictionary<string,object>{{"liveGlassBackground",false}},settings.Revision);
   await NativeWait(()=>Pixels(preview.BackgroundImage)==sky,"native preview restores the original material after opt-out");
   WebView2 materialView=null;foreach(Form window in Application.OpenForms)if(window.GetType().Name=="MaterialHost")materialView=(WebView2)window.Controls[0];
   await materialView.CoreWebView2.ExecuteScriptAsync("chrome.webview.postMessage('error:simulated graphics failure')");
   await NativeWait(()=>preview.BackgroundImage==null,"renderer failure leaves a readable native fallback");
   await NativeWait(()=>preview.BackgroundImage!=null,"shared native material recovers after a renderer failure");
   using(var bitmap=new Bitmap(1000,720))using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.FromArgb(160,90,30));bitmap.Save(Path.Combine(root,"wallpaper.png"));}
   await NativeWait(()=>Pixels(preview.BackgroundImage)!=sky,"an open native preview refreshes when its wallpaper sample changes");
   using(var bitmap=new Bitmap(1000,720))using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.FromArgb(80,105,145));bitmap.Save(Path.Combine(root,"wallpaper.png"));}
   await NativeWait(()=>Pixels(preview.BackgroundImage)==sky,"wallpaper refresh waits for the matching shader image before caching the native background");
   preview.Close();Check(preview.BackgroundImage==null,"closed preview releases its material image");
  }
  using(var menu=DockBackend.CreateMenu()){
   int clicks=0;menu.Opacity=0;menu.Items.Add("独立测试应用").Enabled=false;menu.Items.Add("打开",null,(s,e)=>clicks++);menu.Items.Add(new ToolStripSeparator());menu.Items.Add("关闭");menu.Show(new Point(100,100));
   await NativeWait(()=>menu.BackgroundImage!=null&&menu.Region!=null,"Dock context menu receives the same rounded glass material");
   using(var image=new Bitmap(menu.Width,menu.Height)){menu.DrawToBitmap(image,menu.ClientRectangle);image.Save(Path.Combine(output,"native-menu-sky.png"));}
   menu.Items[1].PerformClick();Check(clicks==1&&!menu.Items[0].Enabled,"material leaves menu actions and disabled states intact");menu.Close();Check(menu.BackgroundImage==null,"closed context menu releases its material image");
  }
  int hosts=0;bool visible=false;foreach(Form window in Application.OpenForms)if(window.GetType().Name=="MaterialHost"){hosts++;visible=window.Controls[0].Visible;}
  Check(hosts==1&&!visible,"native surfaces share one renderer and hide it when idle");
 }
 static async Task Run(){
  using(var bitmap=new Bitmap(1000,720))using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.FromArgb(80,105,145));bitmap.Save(Path.Combine(root,"wallpaper.png"));}
  File.WriteAllText(Path.Combine(root,"fixture.html"),"<!doctype html><meta charset='utf-8'><style>html,body{margin:0;width:100%;height:100%;overflow:hidden}canvas{position:absolute;inset:0;width:100%;height:100%}p{position:absolute;left:100px;top:110px;color:white;font:24px 'Segoe UI'}</style><canvas id='glass'></canvas><p>PrismGlass · Live background fixture</p><script src='glass-renderer.js'></script><script>window.messages=[];window.errors=[];onerror=m=>errors.push(m);PrismGlassRenderer(document.querySelector('canvas'),m=>{messages.push(m);if(m==='painted'||m.startsWith('error:'))chrome.webview.postMessage(m)},r=>window.receive=r);window.layout=paused=>receive({type:'layout',width:innerWidth,height:innerHeight,sceneWidth:innerWidth,sceneHeight:innerHeight,originX:0,originY:0,theme:'Sky',paused,cards:[{name:'Test',x:60,y:60,w:innerWidth-120,h:innerHeight-120,r:32,fixedShape:true}]});window.enable=value=>dispatchEvent(new CustomEvent('prism-preferences',{detail:{liveGlassBackground:value}}));window.stats=()=>{receive({type:'stats'});return JSON.parse(messages.filter(m=>m.startsWith('stats:')).pop().slice(6))};layout(false);</script>");
  await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"runtime")));view.ZoomFactor=96.0/GetDpiForWindow(form.Handle);
  view.CoreWebView2.SetVirtualHostNameToFolderMapping("prism-test.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
  view.CoreWebView2.WebResourceRequested+=(s,e)=>{if(e.Request.Uri.StartsWith("https://prism-backdrop.invalid/"))requests++;};
  view.Source=new Uri("https://prism-test.local/fixture.html");await Wait("!!window.stats&&stats().ready","shared renderer starts with the original wallpaper");await Task.Delay(250);Check(requests==0,"default-off mode never requests native capture");
  await Eval("window.original=document.querySelector('canvas').toDataURL()");
  sourceProcess=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--source"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
  IntPtr source=IntPtr.Zero;for(int i=0;i<50;i++){source=FindWindow(null,"PrismGlass material test source "+sourceProcess.Id);if(source!=IntPtr.Zero)break;await Task.Delay(100);}Check(source!=IntPtr.Zero,"isolated native source window is available");ShowWindow(source,4);
  if(dockOnly){await DockLayers(source);return;}
  if(multiHostOnly){await MultiHost(source);return;}
  RECT rect;GetWindowRect(source,out rect);var item=new LiveGlassBackdrop.Source{handle=source,pid=(uint)sourceProcess.Id,bounds=rect.Rectangle,visible=rect.Rectangle};
  using(var captured=await Task.Run(()=>LiveGlassBackdrop.CaptureWindow(item)))Check(captured!=null&&captured.GetPixel(110,110).G>captured.GetPixel(110,110).R,"native capture reads the fixture's actual green window content");
  item.pid++;Check(LiveGlassBackdrop.CaptureWindow(item)==null,"reused or mismatched window identity is rejected");item.pid--;
  form.Location=new Point(180,140);form.Opacity=1;SendMessage(source,0x8003,new IntPtr(180),new IntPtr(140));SetWindowPos(source,new IntPtr(-1),0,0,0,0,0x13);SetWindowPos(form.Handle,new IntPtr(-1),0,0,0,0,0x13);
  LiveGlassBackdrop.Configure(root,true,false);await Eval("enable(true)");await Wait("stats().liveBackground==='optical'&&document.querySelector('canvas').toDataURL()!==original","explicit opt-in refracts the actual background window through WebGL");await Save("live-green");
  await Eval("window.green=document.querySelector('canvas').toDataURL()");SendMessage(source,0x8001,new IntPtr(Color.FromArgb(52,86,205).ToArgb()),IntPtr.Zero);
  await Wait("document.querySelector('canvas').toDataURL()!==green&&stats().liveBackground==='optical'","changing native window pixels updates the glass");await Save("live-blue");
  uint affinity;Check(GetWindowDisplayAffinity(form.Handle,out affinity)&&affinity==0,"live capture leaves screenshot and screen-sharing affinity unchanged");
  LiveGlassBackdrop.Configure(root,true,true);await Eval("layout(true)");await Wait("stats().liveBackground==='wallpaper'","pausing returns to wallpaper");int pausedRequests=requests;await Task.Delay(400);Check(requests==pausedRequests,"paused mode stops requesting frames");
  LiveGlassBackdrop.Configure(root,true,false);await Eval("layout(false)");await Wait("stats().liveBackground==='optical'","resume restarts native background updates");
  LiveGlassBackdrop.Configure(root,false,false);await Eval("enable(false)");await Wait("stats().liveBackground==='wallpaper'&&document.querySelector('canvas').toDataURL()===original","turning the option off restores the exact original material image");int disabledRequests=requests;await Task.Delay(500);Check(requests==disabledRequests,"late captures cannot restart a disabled option");
  if(!bodyOnly){form.Opacity=0;await NativeMaterial(source);}
  await NativeComposition(source);
  SendMessage(source,0x10,IntPtr.Zero,IntPtr.Zero);await Task.Delay(250);LiveGlassBackdrop.Configure(root,true,false);await Eval("enable(true)");await Task.Delay(400);Check(await Eval("stats().liveBackground==='wallpaper'&&errors.length===0&&!messages.some(m=>m.startsWith('error:'))")=="true","closing the background app releases its texture and retains readable wallpaper");
  File.AppendAllText(Path.Combine(output,"checks.txt"),"Completed "+checks+" native live-background checks."+Environment.NewLine);
 }
 [STAThread]static void Main(string[] args){SetProcessDPIAware();Application.EnableVisualStyles();
  if(args.Length>0&&args[0]=="--source"){var background=new LiveBackdropFixture{Text="PrismGlass material test source "+Process.GetCurrentProcess().Id,BackColor=Color.FromArgb(42,160,95),ClientSize=new Size(1000,720),StartPosition=FormStartPosition.Manual,Location=new Point(-24000,-24000),FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false};background.Shown+=(s,e)=>ShowWindow(background.Handle,4);Application.Run(background);return;}
  bodyOnly=Array.IndexOf(args,"--body-only")>=0;dockOnly=Array.IndexOf(args,"--dock-only")>=0;multiHostOnly=Array.IndexOf(args,"--multi-host-only")>=0;root=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);form=new GlassCaptureHost{ClientSize=new Size(1000,720),StartPosition=FormStartPosition.Manual,Location=new Point(-24000,-24000),FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,TopMost=true,Opacity=0};view=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=Color.Transparent};form.Controls.Add(view);optical=GlassWindow.Configure(form,view);
  form.Shown+=async(s,e)=>{try{await Run();}catch(Exception error){File.AppendAllText(Path.Combine(output,"checks.txt"),error.ToString()+Environment.NewLine);Environment.ExitCode=1;}finally{LiveGlassBackdrop.Configure(root,false,false);if(sourceProcess!=null){if(!sourceProcess.HasExited){sourceProcess.CloseMainWindow();if(!sourceProcess.WaitForExit(1000))sourceProcess.Kill();}sourceProcess.Dispose();}view.Dispose();form.Close();}};Application.Run(form);
 }
}
