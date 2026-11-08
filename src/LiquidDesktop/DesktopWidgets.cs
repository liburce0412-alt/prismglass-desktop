using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Reuse the installed data-source skins; removing a card keeps its data source alive.
internal sealed class DesktopWidgets : IDisposable {
 internal static readonly string[] Names={"Weather","System","Clock","Calendar","Battery","Folders","Music","Focus","Tools","BreathingOrb","DigitalClock","Moon","Photo","Photo2","Photo3","Photo4","Photo5","Photo6"};
 readonly string skinRoot;readonly Dictionary<string,DateTime> retries=new Dictionary<string,DateTime>();
 readonly HashSet<IntPtr> styled=new HashSet<IntPtr>();readonly HashSet<string> shown=new HashSet<string>();
 readonly Dictionary<IntPtr,bool> ambientPaused=new Dictionary<IntPtr,bool>();
 bool dragging,catalogRefreshed,layoutLocked;
 internal void LockLayout(bool value){if(layoutLocked==value)return;layoutLocked=value;dragging=false;styled.Clear();}
 [StructLayout(LayoutKind.Sequential)]struct Rect {public int left,top,right,bottom;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string title);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect bounds);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 internal DesktopWidgets(string root){var path=Path.Combine(root,"skin-path.txt");skinRoot=Path.GetFullPath(File.Exists(path)?File.ReadAllText(path).Trim():Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Rainmeter","Skins","PrismGlass"));}
 IntPtr Window(string name){return FindWindow("RainmeterMeterWindow",Path.Combine(skinRoot,name,"Main.ini"));}
 internal bool Owns(string path){foreach(var name in shown)if(String.Equals(Path.Combine(skinRoot,name,"Main.ini"),path,StringComparison.OrdinalIgnoreCase))return true;return false;}
 internal bool Dragging {get{if(layoutLocked||GetAsyncKeyState(1)>=0)return dragging=false;if(dragging)return true;var at=Cursor.Position;foreach(var name in shown){Rect r;if(GetWindowRect(Window(name),out r)&&at.X>=r.left&&at.X<r.right&&at.Y>=r.top&&at.Y<r.bottom)return dragging=true;}return false;}}
 internal static Rectangle Place(Rectangle card,Rectangle area,ICollection<Rectangle> rails){
  if(card.Width>area.Width||card.Height>area.Height)return Rectangle.Empty;
  var xs=new List<int>{card.X};var ys=new List<int>{card.Y};
  foreach(var rail in rails){xs.Add(rail.Left-card.Width-8);xs.Add(rail.Right+8);ys.Add(rail.Top-card.Height-8);ys.Add(rail.Bottom+8);}
  Rectangle best=Rectangle.Empty;double distance=Double.MaxValue;
  foreach(int x in xs)foreach(int y in ys){var candidate=new Rectangle(Math.Max(area.Left,Math.Min(x,area.Right-card.Width)),Math.Max(area.Top,Math.Min(y,area.Bottom-card.Height)),card.Width,card.Height);bool overlaps=false;foreach(var rail in rails)if(candidate.IntersectsWith(Rectangle.Inflate(rail,8,8))){overlaps=true;break;}if(overlaps)continue;double dx=(double)candidate.X-card.X,dy=(double)candidate.Y-card.Y,d=dx*dx+dy*dy;if(d<distance){best=candidate;distance=d;}}
  return best;
 }
 internal void Arrange(ICollection<Rectangle> rails){
  if(layoutLocked)return;
  foreach(var name in shown){var window=Window(name);Rect r;if(!GetWindowRect(window,out r))continue;var bounds=Rectangle.FromLTRB(r.left,r.top,r.right,r.bottom);var next=Place(bounds,Screen.FromRectangle(bounds).WorkingArea,rails);string config="\"PrismGlass\\"+name+"\"";
   if(next.IsEmpty){MonitorDock.SendSkinCommand(window,"!Hide "+config);throw new IOException("屏幕没有足够空间放置桌面卡片，请先隐藏侧栏或移到更大的显示器。");}
   if(next!=bounds&&!MonitorDock.SendSkinCommand(window,"!Move "+next.X+" "+next.Y+" "+config))throw new IOException("桌面卡片未能避开侧栏，请重试。");
  }
 }
 internal bool Sync(ICollection<string> selected,bool enabled,ICollection<string> autoHide=null,Func<Rectangle,bool> covered=null,Func<Rectangle,bool> screenEnabled=null,Func<Rectangle,bool> motionPaused=null){
  if(!MonitorDock.SkinCatalogReady)return false;
  bool changed=false;
  foreach(var name in Names){
   bool visible=enabled&&selected.Contains(name);var window=Window(name);string config="\"PrismGlass\\"+name+"\"";
   Rect bounds;var area=Rectangle.Empty;if(window!=IntPtr.Zero&&GetWindowRect(window,out bounds)){area=Rectangle.FromLTRB(bounds.left,bounds.top,bounds.right,bounds.bottom);if(visible)visible=(screenEnabled==null||screenEnabled(area))&&!(covered!=null&&autoHide!=null&&autoHide.Contains(name)&&covered(area));}
   if(window==IntPtr.Zero){
    changed|=shown.Remove(name);DateTime retry;
    if(visible&&(!retries.TryGetValue(name,out retry)||DateTime.UtcNow>=retry)){
     if(!File.Exists(Path.Combine(skinRoot,name,"Main.ini")))throw new IOException("桌面小组件 "+name+" 的皮肤尚未安装，请重新安装 PrismGlass。");
     // Newly installed directories may not be in the running Rainmeter catalog yet.
     if(retries.ContainsKey(name)&&!catalogRefreshed){MonitorDock.RefreshSkinCatalog();catalogRefreshed=true;styled.Clear();ambientPaused.Clear();return changed;}
     retries[name]=DateTime.UtcNow.AddSeconds(3);
     using(var process=Process.Start(new ProcessStartInfo(RuntimePaths.Rainmeter,"!ActivateConfig "+config+" \"Main.ini\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){if(process!=null)process.WaitForExit(500);}
    }continue;
   }
   bool restyle=visible&&(!styled.Contains(window)||!IsWindowVisible(window));
   if(restyle){
    string command="[!ZPos -2 "+config+"][!Draggable "+(layoutLocked?"0":"1")+" "+config+"][!ClickThrough 0 "+config+"][!KeepOnScreen 1 "+config+"][!SetVariable Ink 245,248,255 "+config+"][!SetVariable Muted 218,230,245 "+config+"][!UpdateMeter * "+config+"][!Redraw "+config+"]";
    if(!MonitorDock.SendSkinCommand(window,command))throw new IOException("桌面小组件未响应，请稍后重试。");styled.Add(window);
   }
   if(name=="BreathingOrb"){
    bool stop=!visible||(motionPaused!=null&&motionPaused(area)),previous;
    if(restyle||!ambientPaused.TryGetValue(window,out previous)||previous!=stop){
     if(!MonitorDock.SendSkinCommand(window,"[!SetVariable AmbientPaused "+(stop?"1":"0")+" "+config+"][!CommandMeasure Ambient \"MotionPolicy()\" "+config+"]"))throw new IOException("装饰小组件未能同步动效设置。");ambientPaused[window]=stop;
    }
   }
   if(IsWindowVisible(window)!=visible&&!MonitorDock.SendSkinCommand(window,(visible?"!Show ":"!Hide ")+config))throw new IOException("桌面小组件显示状态未能更新。");
   if(visible)changed|=shown.Add(name);else changed|=shown.Remove(name);
  }
  return changed;
 }
 public void Dispose(){foreach(var name in shown){var window=Window(name);string config="\"PrismGlass\\"+name+"\"";if(name=="BreathingOrb")MonitorDock.SendSkinCommand(window,"[!SetVariable AmbientPaused 1 "+config+"][!CommandMeasure Ambient \"MotionPolicy()\" "+config+"]");MonitorDock.SendSkinCommand(window,"!Hide "+config);}shown.Clear();styled.Clear();ambientPaused.Clear();}
}
