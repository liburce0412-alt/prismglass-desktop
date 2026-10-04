using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Enumerates only an explicitly opened directory. No recursive indexing or file-content reads.
internal sealed class ShelfEntry {public string name,path,icon;public bool directory;public long size;public string modified;}
internal sealed class ShelfTask {public string id,text;public bool done;}
internal sealed class ShelfLink {public string id,name,url;}
internal sealed class ShelfState {public List<ShelfTask> tasks=new List<ShelfTask>();public string countdownTitle="",countdownDate="";public List<ShelfLink> links=new List<ShelfLink>();public ShelfLink removedLink;public List<string> hiddenCards=new List<string>(),hiddenLeftCards=new List<string>();}
internal sealed class ShelfPage {public string type="page",path,parent,error;public List<ShelfEntry> entries=new List<ShelfEntry>();public bool more;public int scanned;}
internal sealed class ShelfReader : IDisposable {
 readonly object gate=new object();IEnumerator<string> iterator;string directory="",filter="";bool disposed;
 public const int PageSize=80,ScanLimit=1200;
 public static string Normalize(string path){
  if(String.IsNullOrWhiteSpace(path)||path.Length>4096||!Path.IsPathRooted(path)||path.StartsWith(@"\\.\",StringComparison.Ordinal)||path.StartsWith(@"\\?\",StringComparison.Ordinal))throw new ArgumentException("请选择有效的文件夹。");
  var full=Path.GetFullPath(path);var current=new DirectoryInfo(full);
  if(!current.Exists)throw new DirectoryNotFoundException("文件夹不存在或设备尚未就绪。");
  for(var item=current;item!=null&&item.Parent!=null;item=item.Parent)if((item.Attributes&(FileAttributes.Hidden|FileAttributes.System|FileAttributes.ReparsePoint))!=0)throw new UnauthorizedAccessException("隐藏、系统或链接目录请在资源管理器中查看。");
  return full;
 }
 public ShelfPage Read(string path,string query,bool append){lock(gate){
  var result=new ShelfPage();if(disposed)return result;
  try{
   path=Normalize(path);query=(query??"").Trim();if(query.Length>100)query=query.Substring(0,100);
   if(!append||iterator==null||!String.Equals(directory,path,StringComparison.OrdinalIgnoreCase)||filter!=query){Reset();directory=path;filter=query;iterator=Directory.EnumerateFileSystemEntries(path).GetEnumerator();}
   result.path=directory;var parent=Directory.GetParent(directory);result.parent=parent==null?"":parent.FullName;
   bool exhausted=false;
   while(result.entries.Count<PageSize&&result.scanned<ScanLimit){
    if(!iterator.MoveNext()){exhausted=true;break;}result.scanned++;
    var full=iterator.Current;
    try{var attr=File.GetAttributes(full);if((attr&(FileAttributes.Hidden|FileAttributes.System|FileAttributes.ReparsePoint))!=0)continue;
     var name=Path.GetFileName(full);if(name.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)<0)continue;
     bool folder=(attr&FileAttributes.Directory)!=0;var file=folder?null:new FileInfo(full);
     result.entries.Add(new ShelfEntry{name=name,path=full,directory=folder,size=file==null?0:file.Length,modified=File.GetLastWriteTime(full).ToString("yyyy/MM/dd HH:mm")});
    }catch(UnauthorizedAccessException){}catch(IOException){}
   }
   result.more=!exhausted;if(exhausted)Reset();
  }catch(UnauthorizedAccessException){result.error="没有权限读取此文件夹，请在资源管理器中查看。";Reset();}
   catch(DirectoryNotFoundException){result.error="文件夹不存在或设备尚未就绪。";Reset();}
   catch(Exception ex){result.error=ex is ArgumentException?ex.Message:"无法读取此文件夹，请重试或在资源管理器中打开。";Reset();}
  return result;
 }}
 void Reset(){if(iterator!=null){iterator.Dispose();iterator=null;}}
 public void Dispose(){lock(gate){disposed=true;Reset();}}
}

internal sealed class FileShelf : Form {
 public event Action Ready;
 static readonly string[] CardNames={"quota","desktop","notes","tasks","countdown","links","calculator"};
 static readonly string[] LeftCardNames={"network","Weather","Music","Focus","Folders","System","Tools","Life"};
 readonly string root;readonly bool preview;readonly WebView2 view=new WebView2();readonly JavaScriptSerializer json=new JavaScriptSerializer();readonly ShelfReader reader=new ShelfReader();
 readonly List<string> history=new List<string>();readonly Dictionary<string,ShelfEntry> offered=new Dictionary<string,ShelfEntry>(StringComparer.OrdinalIgnoreCase);
 readonly QuotaReader quotaReader;QuotaSnapshot quotaSnapshot;bool quotaBusy;DateTime nextQuota=DateTime.MinValue;
 Rectangle screen;bool ready,closed,expanded,paused,reading,notesLoaded,painted,pendingFilter;string theme="Sky",layout="",path="",query="",mode="desktop";int generation;float scale=1;ShelfState state=new ShelfState();long noteRevision;readonly Dictionary<string,string> iconCache=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 string StatePath{get{return Path.Combine(root,preview?"shelf-preview-state.json":"shelf-state.json");}}string NotesPath{get{return Path.GetFullPath(Path.Combine(root,preview?"shelf-preview-notes.txt":"..\\随手记.txt"));}}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct SHFILEINFO{public IntPtr icon;public int index;public uint attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string display;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string type;}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SHGetFileInfo(string path,uint attributes,out SHFILEINFO info,uint size,uint flags);
 [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
 [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 protected override bool ShowWithoutActivation{get{return !preview;}}
 protected override CreateParams CreateParams{get{var value=base.CreateParams;if(!preview){value.ExStyle|=0x80;value.ExStyle&=~0x40000;}return value;}}
 public FileShelf(string directory,bool isPreview){
  root=directory;preview=isPreview;quotaReader=new QuotaReader(root);quotaSnapshot=quotaReader.Cached();screen=Screen.PrimaryScreen.Bounds;Text="PrismGlass 桌面空间";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=preview;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;BackColor=Color.FromArgb(27,39,55);view.Dock=DockStyle.Fill;Controls.Add(view);try{if(File.Exists(StatePath))state=json.Deserialize<ShelfState>(File.ReadAllText(StatePath));if(state==null)state=new ShelfState();if(state.tasks==null)state.tasks=new List<ShelfTask>();if(state.links==null)state.links=new List<ShelfLink>();if(state.hiddenCards==null)state.hiddenCards=new List<string>();state.hiddenCards.RemoveAll(name=>Array.IndexOf(CardNames,name)<0);if(VisibleCards().Count==0)state.hiddenCards.Remove("desktop");}catch{state=new ShelfState();}Place();
  ReadLeftVisibility();Shown+=async(s,e)=>{try{
   var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,preview?"shelf-preview-runtime":"runtime-data"));await view.EnsureCoreWebView2Async(env);SyncZoom();
   var core=view.CoreWebView2;core.Settings.IsZoomControlEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;
   core.SetVirtualHostNameToFolderMapping("prism-shelf.local",root,CoreWebView2HostResourceAccessKind.DenyCors);
   core.NavigationStarting+=(sender,a)=>{if(!String.Equals(a.Uri,"https://prism-shelf.local/shelf.html",StringComparison.Ordinal))a.Cancel=true;};
   core.NewWindowRequested+=(sender,a)=>a.Handled=true;
   core.WebMessageReceived+=(sender,a)=>{if(String.Equals(a.Source,"https://prism-shelf.local/shelf.html",StringComparison.Ordinal))Receive(a.TryGetWebMessageAsString());};
   core.ProcessFailed+=(sender,a)=>{try{File.WriteAllText(Path.Combine(root,"shelf-error.txt"),"WebView2: "+a.ProcessFailedKind);}catch{}Close();};
   view.Source=new Uri("https://prism-shelf.local/shelf.html");
  }catch(Exception ex){try{File.WriteAllText(Path.Combine(root,"shelf-error.txt"),ex.Message);}catch{}Close();}};
  Deactivate+=(s,e)=>{if(expanded&&!preview)SetExpanded(false);};
  FormClosed+=(s,e)=>{closed=true;generation++;quotaReader.Dispose();reader.Dispose();view.Dispose();};
 }
 void SyncZoom(){if(view.CoreWebView2!=null){uint dpi=GetDpiForWindow(Handle);view.ZoomFactor=96.0/(dpi==0?96:dpi);}}
 void Place(){
  scale=Math.Max(.65f,Math.Min(1.25f,Math.Min((screen.Height-250)/620f,(screen.Width-180)/500f)));
  int width=(int)Math.Ceiling(500*scale),height=(int)Math.Ceiling(620*scale);Bounds=new Rectangle(screen.Right-width-20,screen.Top+Math.Max(82,(screen.Height-height)/2),width,height);Shape(expanded?1:0);
 }
 void Shape(float amount){using(var shape=new GraphicsPath()){
  int count=VisibleCards().Count;float top=(620-(count*74+36))/2f;Round(shape,417*scale,(top-22)*scale,82*scale,(count*74+58)*scale,26*scale);if(amount>.001f)Round(shape,(404-400*amount)*scale,4*scale,400*amount*scale,612*scale,26*scale);
  var old=Region;Region=new Region(shape);if(old!=null)old.Dispose();
 }}
 static void Round(GraphicsPath p,float x,float y,float w,float h,float r){float d=Math.Min(r*2,Math.Min(w,h));if(w<=0||h<=0)return;p.StartFigure();p.AddArc(x,y,d,d,180,90);p.AddArc(x+w-d,y,d,d,270,90);p.AddArc(x+w-d,y+h-d,d,d,0,90);p.AddArc(x,y+h-d,d,d,90,90);p.CloseFigure();}
 public void Sync(string nextTheme,Rectangle bounds,IntPtr anchor,bool motionPaused){
  if(closed)return;if(screen!=bounds){screen=bounds;Place();layout="";}SyncZoom();theme=nextTheme=="Astro"?"Astro":"Sky";paused=motionPaused;Publish();if(ready)RefreshQuota(false);
  if(!preview&&anchor!=IntPtr.Zero&&GetForegroundWindow()!=Handle){var before=GetWindow(anchor,3);if(before!=Handle)SetWindowPos(Handle,before,0,0,0,0,0x13);}
 }
 void Publish(){if(!ready)return;string stamp=File.GetLastWriteTimeUtc(Path.Combine(root,"wallpaper.png")).Ticks.ToString();string key=Bounds+theme+paused+stamp;if(key==layout)return;layout=key;Send(new{type="layout",theme=theme,paused=paused,scale=scale,width=Width,height=Height,originX=Left-screen.Left,originY=Top-screen.Top,sceneWidth=screen.Width,sceneHeight=screen.Height,wallpaper=stamp});}
 void Send(object value){if(!closed&&ready&&view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson(json.Serialize(value));}
 void SetExpanded(bool value,bool activate=true){generation++;pendingFilter=false;expanded=value;Send(new{type="expanded",value=value,mode=mode});if(value){if(activate){Activate();view.Focus();}LoadMode();}else{query="";}}
 void LoadMode(){if(mode=="desktop")Desktop();else if(mode=="notes")Notes();else if(mode=="quota")Send(new{type="quota",snapshot=quotaSnapshot,busy=quotaBusy});else Send(new{type="local-state",state=state});}
 async void RefreshQuota(bool manual){if(closed||preview||quotaBusy||DateTime.UtcNow<nextQuota&&!manual)return;if(manual&&DateTime.UtcNow<nextQuota.AddMinutes(-3).AddSeconds(15))return;quotaBusy=true;nextQuota=DateTime.UtcNow.AddMinutes(3);Send(new{type="quota",snapshot=quotaSnapshot,busy=true});try{var snapshot=await quotaReader.ReadAsync();if(!closed){quotaSnapshot=snapshot;Send(new{type="quota",snapshot=quotaSnapshot,busy=false});}}finally{quotaBusy=false;}}
 List<string> VisibleCards(){var result=new List<string>();foreach(var name in CardNames)if(!state.hiddenCards.Contains(name))result.Add(name);return result;}
 string LeftVisibilityPath{get{return Path.Combine(root,preview?"sidebar-preview-visibility.json":"sidebar-visibility.json");}}
 void ReadLeftVisibility(){state.hiddenLeftCards=new List<string>();try{if(File.Exists(LeftVisibilityPath)){var data=json.Deserialize<Dictionary<string,string[]>>(File.ReadAllText(LeftVisibilityPath));string[] names;if(data.TryGetValue("hiddenLeftCards",out names)&&names!=null)foreach(var name in names)if(Array.IndexOf(LeftCardNames,name)>=0&&!state.hiddenLeftCards.Contains(name))state.hiddenLeftCards.Add(name);}}catch{}}
 void SaveLeftVisibility(){var temp=LeftVisibilityPath+".tmp";File.WriteAllText(temp,json.Serialize(new{hiddenLeftCards=state.hiddenLeftCards}));if(File.Exists(LeftVisibilityPath))File.Replace(temp,LeftVisibilityPath,null);else File.Move(temp,LeftVisibilityPath);Send(new{type="local-state",state=state});}
 static bool SafeUrl(string value,out string normalized){normalized=null;Uri uri;if(value==null||value.Length>2048||!Uri.TryCreate(value.Trim(),UriKind.Absolute,out uri)||(uri.Scheme!=Uri.UriSchemeHttp&&uri.Scheme!=Uri.UriSchemeHttps)||String.IsNullOrEmpty(uri.Host)||!String.IsNullOrEmpty(uri.UserInfo))return false;normalized=uri.AbsoluteUri;return true;}
 void SaveState(){if(preview)return;var temp=StatePath+".tmp";File.WriteAllText(temp,json.Serialize(state));if(File.Exists(StatePath))File.Replace(temp,StatePath,null);else File.Move(temp,StatePath);Send(new{type="local-state",state=state});}
 void Notes(){if(notesLoaded)return;try{var file=new FileInfo(NotesPath);noteRevision=file.Exists?file.LastWriteTimeUtc.Ticks:0;if(file.Exists&&file.Length>131072){Send(new{type="error",message="随手记超过 128 KB，请在记事本中编辑。"});return;}Send(new{type="notes",text=file.Exists?File.ReadAllText(NotesPath):""});notesLoaded=true;}catch{Send(new{type="error",message="无法读取随手记，请在记事本中打开。"});}}
 bool NoteUnchanged(){return (File.Exists(NotesPath)?File.GetLastWriteTimeUtc(NotesPath).Ticks:0)==noteRevision;}
 void SaveNote(string value){
  if(value==null||value.Length>32768)throw new ArgumentException();
  const string conflict="随手记已在其他应用中更新。请先复制当前草稿到记事本中合并，避免覆盖。";
  if(!NoteUnchanged()){Send(new{type="error",message=conflict});return;}
  string pending=NotesPath+".pending-"+Guid.NewGuid().ToString("N");
  try{File.WriteAllText(pending,value);if(!NoteUnchanged()){Send(new{type="error",message=conflict});return;}if(File.Exists(NotesPath))File.Replace(pending,NotesPath,NotesPath+".previous");else File.Move(pending,NotesPath);noteRevision=File.GetLastWriteTimeUtc(NotesPath).Ticks;Send(new{type="saved",message="已保存到原有随手记"});}
  finally{try{if(File.Exists(pending))File.Delete(pending);}catch{}}
 }
 static string EntryIcon(string path){SHFILEINFO info;SHGetFileInfo(path,0,out info,(uint)Marshal.SizeOf(typeof(SHFILEINFO)),0x100);if(info.icon==IntPtr.Zero)return null;try{using(var icon=Icon.FromHandle(info.icon))using(var bitmap=icon.ToBitmap())using(var bytes=new MemoryStream()){bitmap.Save(bytes,System.Drawing.Imaging.ImageFormat.Png);return "data:image/png;base64,"+Convert.ToBase64String(bytes.ToArray());}}catch{return null;}finally{DestroyIcon(info.icon);}}
 async void Desktop(){int ticket=++generation;Send(new{type="loading"});var entries=await Task.Run(()=>{var items=new List<ShelfEntry>();var names=new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)}){try{foreach(var item in Directory.EnumerateFileSystemEntries(folder)){if(items.Count>=1000)break;try{var attrs=File.GetAttributes(item);if((attrs&(FileAttributes.Hidden|FileAttributes.System))!=0)continue;var name=Path.GetFileName(item);if(!names.Add(name))continue;items.Add(new ShelfEntry{name=Path.GetExtension(name).Equals(".lnk",StringComparison.OrdinalIgnoreCase)?Path.GetFileNameWithoutExtension(name):name,path=item,directory=(attrs&FileAttributes.Directory)!=0});}catch{}}}catch{}}return items;});if(closed||ticket!=generation)return;offered.Clear();foreach(var item in entries){offered[item.path]=item;string icon;if(iconCache.TryGetValue(item.path,out icon))item.icon=icon;}Send(new{type="desktop",entries=entries,truncated=entries.Count>=1000});
  for(int i=0;i<Math.Min(120,entries.Count);i++){if(closed||ticket!=generation)return;var item=entries[i];if(item.icon!=null)continue;string icon=await Task.Run(()=>EntryIcon(item.path));if(closed||ticket!=generation)return;if(icon!=null){iconCache[item.path]=icon;Send(new{type="icon",path=item.path,icon=icon});}}
 }
 void Home(){generation++;path="";query="";offered.Clear();var entries=new List<ShelfEntry>();
  foreach(var named in new[]{new[]{"桌面",Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)},new[]{"文档",Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)},new[]{"下载",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")}})if(Directory.Exists(named[1]))entries.Add(new ShelfEntry{name=named[0],path=named[1],directory=true});
  foreach(var drive in DriveInfo.GetDrives())entries.Add(new ShelfEntry{name=drive.Name+" 驱动器",path=drive.Name,directory=true});
  foreach(var entry in entries)offered[entry.path]=entry;Send(new{type="page",path="",parent="",entries=entries,more=false,error="",append=false,canBack=history.Count>0});
 }
 async void Read(string target,bool append,bool remember){
  if(reading)return;reading=true;int ticket=++generation;if(remember&&target!=path)history.Add(path);string search=query;
  Send(new{type="loading"});try{var page=await Task.Run(()=>reader.Read(target,search,append));if(closed||ticket!=generation)return;
   if(page.error==null){path=page.path;if(!append)offered.Clear();foreach(var item in page.entries)offered[item.path]=item;}
   Send(new{type="page",path=page.path??target,parent=page.parent??"",entries=page.entries,more=page.more,error=page.error??"",append=append,canBack=history.Count>0,scanned=page.scanned});
  }finally{reading=false;if(pendingFilter&&!closed&&expanded&&path!=""){pendingFilter=false;Read(path,false,false);}}
 }
 void Receive(string text){try{
  if(text=="ready"){ready=true;Publish();Send(new{type="local-state",state=state});Send(new{type="quota",snapshot=quotaSnapshot,busy=false});RefreshQuota(false);return;}if(text=="painted"){if(!painted){painted=true;if(Ready!=null)Ready();}return;}if(text!=null&&text.StartsWith("error:",StringComparison.Ordinal)){try{File.WriteAllText(Path.Combine(root,"shelf-error.txt"),text.Substring(6));}catch{}Close();return;}if(text==null||text.Length>70000)return;
  var msg=json.Deserialize<Dictionary<string,object>>(text);object raw;string action=msg.TryGetValue("action",out raw)?raw as string:null;string value=msg.TryGetValue("value",out raw)?raw as string:"";
  if((action=="hover-mode"||action=="pin-mode")&&(value=="manage"||Array.IndexOf(CardNames,value)>=0&&!state.hiddenCards.Contains(value))){mode=value;SetExpanded(true,action=="pin-mode");return;}
  if(action=="mode"&&(value=="manage"||Array.IndexOf(CardNames,value)>=0&&!state.hiddenCards.Contains(value))){if(expanded&&mode==value)SetExpanded(false);else{mode=value;SetExpanded(true);}return;}
  if(action=="toggle"){SetExpanded(!expanded);return;}if(action=="close"){SetExpanded(false);return;}
  if(action=="shape"){double amount;if(msg.TryGetValue("amount",out raw)&&Double.TryParse(Convert.ToString(raw,System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out amount)&&!Double.IsNaN(amount))Shape((float)Math.Max(0,Math.Min(1,amount)));return;}
  if(!expanded)return;
  if(action=="quota-refresh"&&mode=="quota"){RefreshQuota(true);return;}
  if(action=="left-card-toggle"&&mode=="manage"&&!preview&&Array.IndexOf(LeftCardNames,value)>=0){if(state.hiddenLeftCards.Contains(value))state.hiddenLeftCards.Remove(value);else state.hiddenLeftCards.Add(value);SaveLeftVisibility();return;}
  if(action=="card-toggle"&&mode=="manage"&&!preview&&Array.IndexOf(CardNames,value)>=0){if(state.hiddenCards.Contains(value))state.hiddenCards.Remove(value);else if(VisibleCards().Count>1)state.hiddenCards.Add(value);else{Send(new{type="error",message="至少保留一个入口，管理按钮始终显示。"});return;}SaveState();Shape(expanded?1:0);return;}
  if(action=="link-add"&&mode=="links"&&!preview){object urlValue;string url=msg.TryGetValue("url",out urlValue)?urlValue as string:null,normalized;if(String.IsNullOrWhiteSpace(value)||value.Length>60||state.links.Count>=60||!SafeUrl(url,out normalized)){Send(new{type="error",message="请输入名称和 http / https 地址，最多保存 60 项。"});return;}state.links.Add(new ShelfLink{id=Guid.NewGuid().ToString("N"),name=value.Trim(),url=normalized});SaveState();return;}
  if(action=="link-remove"&&mode=="links"&&!preview){var item=state.links.Find(link=>link.id==value);if(item!=null){state.removedLink=item;state.links.Remove(item);SaveState();}return;}
  if(action=="link-restore"&&mode=="links"&&!preview){if(state.removedLink!=null&&state.links.Count<60){state.links.Add(state.removedLink);state.removedLink=null;SaveState();}return;}
  if(action=="link-open"&&mode=="links"&&!preview){var item=state.links.Find(link=>link.id==value);string normalized;if(item!=null&&SafeUrl(item.url,out normalized)){Process.Start(new ProcessStartInfo(normalized){UseShellExecute=true});SetExpanded(false);}return;}
  if(action=="note-save"&&mode=="notes"&&!preview){SaveNote(value);return;}
  if(action=="task-add"&&mode=="tasks"&&!preview){value=(value??"").Trim();if(value.Length==0||value.Length>120||state.tasks.Count>=50){Send(new{type="error",message="事项最多 50 条，每条不超过 120 字。"});return;}state.tasks.Add(new ShelfTask{id=Guid.NewGuid().ToString("N"),text=value,done=false});SaveState();return;}
  if(action=="task-toggle"&&mode=="tasks"&&!preview){foreach(var task in state.tasks)if(task.id==value){task.done=!task.done;SaveState();return;}return;}
  if(action=="countdown-save"&&mode=="countdown"&&!preview){object dateValue;string date=msg.TryGetValue("date",out dateValue)?dateValue as string:null;DateTime parsed;if(String.IsNullOrWhiteSpace(value)||value.Length>60||!DateTime.TryParseExact(date,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out parsed)){Send(new{type="error",message="请输入名称和有效日期。"});return;}state.countdownTitle=value.Trim();state.countdownDate=date;SaveState();return;}
  if(action=="system"&&!preview&&(value=="computer"||value=="recycle")){Process.Start(new ProcessStartInfo("explorer.exe",value=="computer"?"shell:MyComputerFolder":"shell:RecycleBinFolder"){UseShellExecute=true});SetExpanded(false);return;}
  if(action=="home"){Home();return;}
  if(action=="back"&&history.Count>0&&!reading){var previous=history[history.Count-1];history.RemoveAt(history.Count-1);query="";if(previous=="")Home();else Read(previous,false,false);return;}
  if(action=="up"&&path!=""&&!reading){var parent=Directory.GetParent(path);query="";if(parent==null)Home();else Read(parent.FullName,false,true);return;}
  if(action=="filter"&&path!=""){query=(value??"");if(query.Length>100)query=query.Substring(0,100);if(reading){pendingFilter=true;generation++;}else Read(path,false,false);return;}
  if(action=="more"&&path!=""){Read(path,true,false);return;}
  if(action=="pick"&&!preview){using(var picker=new FolderBrowserDialog()){picker.Description="选择要浏览的文件夹";picker.ShowNewFolderButton=false;if(picker.ShowDialog(this)==DialogResult.OK){query="";Read(picker.SelectedPath,false,true);}}return;}
  if(action=="explorer"&&!preview){Process.Start(new ProcessStartInfo("explorer.exe",path==""?"":'"'+ShelfReader.Normalize(path)+'"'){UseShellExecute=true});return;}
  ShelfEntry entry;if(action=="open"&&value!=null&&offered.TryGetValue(value,out entry)){
   if(entry.directory){query="";Read(entry.path,false,true);}else if(!preview&&File.Exists(entry.path)){ShelfReader.Normalize(Path.GetDirectoryName(entry.path));var attr=File.GetAttributes(entry.path);if((attr&(FileAttributes.Hidden|FileAttributes.System|FileAttributes.ReparsePoint))!=0)throw new UnauthorizedAccessException();Process.Start(new ProcessStartInfo(entry.path){UseShellExecute=true});SetExpanded(false);}return;
  }
  if(action=="copy"&&path!=""&&!preview)Clipboard.SetText(path);
 }catch(Exception){Send(new{type="error",message="操作未完成。文件可能已移动，或此位置没有访问权限。"});}}
}
