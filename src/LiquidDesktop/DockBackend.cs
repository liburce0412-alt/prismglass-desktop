using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal sealed class DockPin {public string id,name,path,arguments,icon;public bool exists;}
internal sealed class DockWindow {public long handle;public uint pid;public string title,path;}
internal sealed class DockEntry {public string id,name,icon,path,arguments;public bool pinned,active,attention;public List<DockWindow> windows=new List<DockWindow>();}
internal sealed class DockBackend {
 const string OverflowId="r2147483647";
 readonly string root;DockPin[] pins;DateTime pinsStamp;readonly JavaScriptSerializer json=new JavaScriptSerializer();
 readonly Dictionary<string,int> order=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
 readonly Dictionary<string,string> icons=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 string previous="";
 readonly HashSet<long> attention=new HashSet<long>();public void Attention(long h){attention.Add(h);}
 delegate bool EnumProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc fn,IntPtr p);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h,uint cmd);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h,int index);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int size);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder text,int size);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h,int cmd);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h,uint flags,StringBuilder path,ref uint size);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attr,out int value,int size);
 [StructLayout(LayoutKind.Sequential)] struct SIZE {public int x,y;}
 [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IImageFactory {[PreserveSig]int GetImage(SIZE size,int flags,out IntPtr bitmap);}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)] static extern void SHCreateItemFromParsingName(string path,IntPtr context,ref Guid guid,[MarshalAs(UnmanagedType.Interface)]out IImageFactory item);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
 [StructLayout(LayoutKind.Sequential)] struct BITMAPINFO {public uint size;public int width,height;public ushort planes,bits;public uint compression,imageSize;public int x,y;public uint used,important;}
 [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr h);
 [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr h);
 [DllImport("gdi32.dll")]static extern int GetDIBits(IntPtr dc,IntPtr bitmap,uint start,uint rows,IntPtr data,ref BITMAPINFO info,uint usage);
 static void SaveBitmap(IntPtr h,string path){using(var source=Image.FromHbitmap(h)){int w=source.Width,ht=source.Height;using(var output=new Bitmap(w,ht,PixelFormat.Format32bppArgb)){var data=output.LockBits(new Rectangle(0,0,w,ht),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);var info=new BITMAPINFO{size=40,width=w,height=-ht,planes=1,bits=32};var dc=CreateCompatibleDC(IntPtr.Zero);try{if(GetDIBits(dc,h,0,(uint)ht,data.Scan0,ref info,0)==0)throw new Exception("icon pixels");}finally{DeleteDC(dc);output.UnlockBits(data);}output.Save(path,ImageFormat.Png);}}}
 static string ProcessPath(uint pid){var h=OpenProcess(0x1000,false,pid);if(h==IntPtr.Zero)return "";try{var b=new StringBuilder(32768);uint n=32768;return QueryFullProcessImageName(h,0,b,ref n)?b.ToString():"";}finally{CloseHandle(h);}}
 public DockBackend(string folder){root=folder;Directory.CreateDirectory(Path.Combine(root,"dock-icons"));pins=json.Deserialize<DockPin[]>(File.ReadAllText(Path.Combine(root,"pins.json")));
  foreach(var p in pins)p.icon=IconFor(p.path,p.icon,"pin-"+p.id);
 }
 string IconFor(string path,string custom,string key){
  string saved;string cacheKey=path+"|"+custom;if(icons.TryGetValue(cacheKey,out saved))return saved;
  saved=Path.Combine(root,"dock-icons",key+".png");
  try{if(!String.IsNullOrEmpty(custom)&&File.Exists(custom)){using(var input=Image.FromFile(custom))input.Save(saved,ImageFormat.Png);}
   else {IImageFactory factory=null;IntPtr bitmap=IntPtr.Zero;try{var iid=typeof(IImageFactory).GUID;SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out factory);if(factory.GetImage(new SIZE{x=64,y=64},5,out bitmap)!=0)throw new Exception("icon");SaveBitmap(bitmap,saved);}finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);if(factory!=null)Marshal.ReleaseComObject(factory);}}
  }catch{try{using(var ico=Icon.ExtractAssociatedIcon(path))using(var img=ico.ToBitmap())img.Save(saved,ImageFormat.Png);}catch{using(var img=SystemIcons.Application.ToBitmap())img.Save(saved,ImageFormat.Png);}}
  icons[cacheKey]=saved;return saved;
 }
 static bool Matches(DockPin pin,string path){string file=Path.GetFileName(path).ToLowerInvariant();
  return String.Equals(Path.GetFileName(pin.path),file,StringComparison.OrdinalIgnoreCase);
 }
 public void Refresh(){
  var stamp=File.GetLastWriteTimeUtc(Path.Combine(root,"pins.json"));if(stamp!=pinsStamp){try{var updated=json.Deserialize<DockPin[]>(File.ReadAllText(Path.Combine(root,"pins.json")));foreach(var pin in updated)pin.icon=IconFor(pin.path,pin.icon,"pin-"+pin.id);pins=updated;pinsStamp=stamp;}catch{}}

  var foreground=GetForegroundWindow();var all=new List<DockWindow>();var paths=new Dictionary<uint,string>();
  EnumWindows((h,p)=>{
   if(!IsWindowVisible(h))return true;long style=GetWindowLongPtr(h,-20).ToInt64();if((style&0x80)!=0||(style&0x08000000)!=0)return true;
   if(GetWindow(h,4)!=IntPtr.Zero&&(style&0x40000)==0)return true;
   int cloaked;if(DwmGetWindowAttribute(h,14,out cloaked,4)==0&&cloaked!=0)return true;
   var cl=new StringBuilder(256);GetClassName(h,cl,256);if(new[]{"Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd","RainmeterMeterWindow"}.Contains(cl.ToString()))return true;
   var title=new StringBuilder(1024);GetWindowText(h,title,1024);if(title.Length==0)return true;
   uint pid;GetWindowThreadProcessId(h,out pid);string path;if(!paths.TryGetValue(pid,out path)){path=ProcessPath(pid);paths[pid]=path;}
   if(String.IsNullOrEmpty(path)||new[]{"liquiddesktop.exe","rainmeter.exe","nexus.exe"}.Contains(Path.GetFileName(path).ToLowerInvariant()))return true;
   all.Add(new DockWindow{handle=h.ToInt64(),pid=pid,title=title.ToString(),path=path});return true;
  },IntPtr.Zero);
  var entries=new List<DockEntry>();var used=new HashSet<long>();
  foreach(var pin in pins){var e=new DockEntry{id="p"+pin.id,name=pin.name,icon=pin.icon,path=pin.path,arguments=pin.arguments,pinned=true};
   e.windows=all.Where(w=>Matches(pin,w.path)).ToList();foreach(var w in e.windows)used.Add(w.handle);e.active=e.windows.Any(w=>w.handle==foreground.ToInt64());entries.Add(e);
  }
  foreach(var group in all.Where(w=>!used.Contains(w.handle)).GroupBy(w=>w.path,StringComparer.OrdinalIgnoreCase)){
   if(!order.ContainsKey(group.Key))order[group.Key]=order.Count;var sequence=order[group.Key];
   var name=Path.GetFileNameWithoutExtension(group.Key);if(name.Equals("explorer",StringComparison.OrdinalIgnoreCase))name="文件资源管理器";
   entries.Add(new DockEntry{id="r"+sequence,name=name,path=group.Key,arguments="",pinned=false,icon=IconFor(group.Key,"","run-"+sequence),windows=group.ToList(),active=group.Any(w=>w.handle==foreground.ToInt64())});
  }
  entries=entries.Where(e=>e.pinned).Concat(entries.Where(e=>!e.pinned).OrderBy(e=>Int32.Parse(e.id.Substring(1)))).ToList();
  attention.RemoveWhere(h=>!all.Any(w=>w.handle==h));foreach(var e in entries){if(e.active)foreach(var w in e.windows)attention.Remove(w.handle);e.attention=e.windows.Any(w=>attention.Contains(w.handle));}
  var displayed=DisplayEntries(entries,OverflowIcon());
  var overflow=displayed.FirstOrDefault(e=>e.id==OverflowId);if(overflow!=null)entries.Add(overflow);
  string data=json.Serialize(entries);if(data==previous)return;previous=data;
  File.WriteAllText(Path.Combine(root,"dock-state.json"),data);
  var lines=new StringBuilder();foreach(var e in displayed)lines.AppendLine(e.id+"\t"+B64(e.name)+"\t"+B64(e.icon)+"\t"+e.windows.Count+"\t"+(e.active?"1":"0")+"\t"+(e.pinned?"1":"0")+"\t"+(e.attention?"1":"0"));
  string temp=Path.Combine(root,"dock-state.tmp");File.WriteAllText(temp,lines.ToString());File.Copy(temp,Path.Combine(root,"dock-state.txt"),true);
 }
 internal static List<DockEntry> DisplayEntries(List<DockEntry> entries,string icon){
  if(entries.Count<=32)return new List<DockEntry>(entries);
  var visible=entries.Take(31).ToList();var remainder=entries.Skip(31).ToList();
  visible.Add(new DockEntry{id=OverflowId,name="更多应用 · "+remainder.Count,icon=icon,path="",arguments="",windows=remainder.SelectMany(e=>e.windows).ToList(),active=remainder.Any(e=>e.active),attention=remainder.Any(e=>e.attention)});return visible;
 }
 string OverflowIcon(){var path=Path.Combine(root,"dock-icons","more-apps.png");if(!File.Exists(path)){using(var bitmap=new Bitmap(64,64))using(var g=Graphics.FromImage(bitmap))using(var brush=new SolidBrush(Color.FromArgb(224,235,248)))using(var ink=new SolidBrush(Color.FromArgb(39,59,83))){g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.FillEllipse(brush,2,2,60,60);for(int i=0;i<3;i++)g.FillEllipse(ink,14+i*14,28,8,8);bitmap.Save(path,ImageFormat.Png);}}return path;}
 static void MoreMenu(string root){
  var entries=new JavaScriptSerializer().Deserialize<List<DockEntry>>(File.ReadAllText(Path.Combine(root,"dock-state.json"))).Where(e=>e.id!=OverflowId).Skip(31).ToList();
  using(var owner=new Form())using(var menu=new ContextMenuStrip()){
   owner.FormBorderStyle=FormBorderStyle.None;owner.ShowInTaskbar=false;owner.StartPosition=FormStartPosition.Manual;owner.Bounds=new Rectangle(Cursor.Position,new Size(1,1));owner.Opacity=0;
   foreach(var entry in entries){var id=entry.id;menu.Items.Add(entry.name,null,(s,e)=>Click(root,id,false));}
   menu.Closed+=(s,e)=>owner.Close();owner.Shown+=(s,e)=>menu.Show(Cursor.Position);Application.Run(owner);
  }
 }
 static string B64(string s){return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));}
 static bool Valid(DockWindow w){uint pid;GetWindowThreadProcessId(new IntPtr(w.handle),out pid);return pid==w.pid&&String.Equals(ProcessPath(pid),w.path,StringComparison.OrdinalIgnoreCase);}
 public static void Activate(DockWindow w){if(!Valid(w))return;var h=new IntPtr(w.handle);if(IsIconic(h))ShowWindowAsync(h,9);SetForegroundWindow(h);}
 public DockEntry Entry(string id){if(previous.Length==0)return null;var e=json.Deserialize<List<DockEntry>>(previous).FirstOrDefault(x=>x.id==id);if(e!=null)e.windows=e.windows.Where(Valid).ToList();return e;}
 public static void Click(string root,string id,bool forceNew){
  if(id==OverflowId){MoreMenu(root);return;}
  try{var entries=new JavaScriptSerializer().Deserialize<List<DockEntry>>(File.ReadAllText(Path.Combine(root,"dock-state.json")));var entry=entries.FirstOrDefault(e=>e.id==id);if(entry==null)return;
   var windows=entry.windows.Where(Valid).ToList();if(forceNew||windows.Count==0){if(entry.pinned&&File.Exists(entry.path))Process.Start(new ProcessStartInfo(entry.path,entry.arguments??""){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(entry.path)});return;}
   if(windows.Count==1){Activate(windows[0]);return;}
   DockWindow chosen=null;using(var picker=new Form()){
    picker.Text=entry.name+" · 选择窗口";picker.FormBorderStyle=FormBorderStyle.FixedToolWindow;picker.ShowInTaskbar=false;picker.TopMost=true;picker.StartPosition=FormStartPosition.Manual;picker.BackColor=Color.FromArgb(239,245,252);picker.ClientSize=new Size(390,Math.Min(460,windows.Count*47+12));
    var area=Screen.FromPoint(Cursor.Position).WorkingArea;picker.Location=new Point(Math.Max(area.Left,Math.Min(area.Right-picker.Width,Cursor.Position.X-picker.Width/2)),Math.Max(area.Top,area.Bottom-picker.Height-90));
    var list=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(6)};picker.Controls.Add(list);
    foreach(var w in windows){var selected=w;var button=new Button{Text=w.title,Width=366,Height=39,TextAlign=ContentAlignment.MiddleLeft,FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Color.FromArgb(27,42,66)};button.Click+=(s,e)=>{chosen=selected;picker.Close();};list.Controls.Add(button);}
    picker.Deactivate+=(s,e)=>picker.Close();picker.ShowDialog();
   }if(chosen!=null)Activate(chosen);
  }catch(Exception ex){File.WriteAllText(Path.Combine(root,"dock-error.txt"),ex.Message);}
 }

 public static void Menu(string root,string id){
  if(id==OverflowId){MoreMenu(root);return;}
  try{
   var serializer=new JavaScriptSerializer();var entry=serializer.Deserialize<List<DockEntry>>(File.ReadAllText(Path.Combine(root,"dock-state.json"))).FirstOrDefault(e=>e.id==id);if(entry==null)return;
   using(var owner=new Form())using(var menu=new ContextMenuStrip()){
    owner.FormBorderStyle=FormBorderStyle.None;owner.ShowInTaskbar=false;owner.StartPosition=FormStartPosition.Manual;owner.Bounds=new Rectangle(Cursor.Position,new Size(1,1));owner.Opacity=0;
    owner.ContextMenuStrip=menu;menu.Font=new Font("Microsoft YaHei UI",10);menu.Items.Add(entry.name).Enabled=false;
    menu.Items.Add("打开 / 切换窗口",null,(s,e)=>Click(root,id,false));
    menu.Items.Add("新建窗口",null,(s,e)=>Click(root,id,true));
    menu.Items.Add("打开文件位置",null,(s,e)=>Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+entry.path+"\""){UseShellExecute=true}));
    menu.Items.Add(new ToolStripSeparator());
    Action<int> change=direction=>{
     var path=Path.Combine(root,"pins.json");var list=serializer.Deserialize<List<DockPin>>(File.ReadAllText(path));int index=list.FindIndex(p=>"p"+p.id==id);
     if(direction==0){if(index>=0)list.RemoveAt(index);else{int max=list.Select(p=>{int n;return Int32.TryParse(p.id,out n)?n:0;}).DefaultIfEmpty(0).Max();list.Add(new DockPin{id=(max+1).ToString(),name=entry.name,path=entry.path,arguments=entry.arguments,icon=entry.icon,exists=File.Exists(entry.path)});}}
     else if(index>=0){int next=Math.Max(0,Math.Min(list.Count-1,index+direction));var pin=list[index];list.RemoveAt(index);list.Insert(next,pin);}
     var temp=path+".tmp";File.WriteAllText(temp,serializer.Serialize(list));File.Replace(temp,path,path+".previous");
    };
    menu.Items.Add(entry.pinned?"从 Dock 取消固定":"固定到 Dock",null,(s,e)=>change(0));
    if(entry.pinned){menu.Items.Add("向左移动",null,(s,e)=>change(-1));menu.Items.Add("向右移动",null,(s,e)=>change(1));}
    menu.Closed+=(s,e)=>owner.Close();owner.Shown+=(s,e)=>menu.Show(Cursor.Position);Application.Run(owner);
   }
  }catch(Exception ex){File.WriteAllText(Path.Combine(root,"dock-error.txt"),ex.ToString());}
 }
}
