using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal sealed class AppLibraryEntry {
 public string id,name,path,target,arguments,icon,kind,category,folder,alias,search,initials,identity;
 public bool exists,manual; public int order; public List<string> sources=new List<string>();
}
internal sealed class AppLibraryCategory { public string id,name; public int order; }
internal sealed class AppLibraryFolder { public string id,name,category; public int order; }
internal sealed class AppLibrarySnapshot {
 public int version=1; public long revision; public string error="";
 public List<AppLibraryEntry> entries=new List<AppLibraryEntry>();
 public List<AppLibraryCategory> categories=new List<AppLibraryCategory>();
 public List<AppLibraryFolder> folders=new List<AppLibraryFolder>();
 public List<string> excluded=new List<string>();
}
internal sealed class AppLibrary : IDisposable {
 readonly string root,file; readonly object gate=new object(),scanGate=new object();
 readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=32*1024*1024};
 readonly Dictionary<string,string> pinyin=new Dictionary<string,string>();
 readonly Dictionary<string,string> icons=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
 AppLibrarySnapshot state; volatile bool dirty=true; volatile bool disposed;DateTime scanned=DateTime.MinValue;string observed;
 public bool NeedsRefresh { get {return dirty||DateTime.UtcNow-scanned>TimeSpan.FromMinutes(10);} }
 public AppLibrary(string folder) {
  root=folder;file=Path.Combine(root,"app-library.json");Directory.CreateDirectory(root);state=Read();
  string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app-center-pinyin.txt");if(!File.Exists(data))data=Path.Combine(root,"app-center-pinyin.txt");
  if(File.Exists(data))foreach(var line in File.ReadLines(data)){if(line.StartsWith("#"))continue;var fields=line.Split('\t');if(fields.Length==2)pinyin[fields[0]]=fields[1];}
  foreach(var path in Roots())if(Directory.Exists(path))try{var w=new FileSystemWatcher(path){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite};w.Changed+=(s,e)=>dirty=true;w.Created+=(s,e)=>dirty=true;w.Deleted+=(s,e)=>dirty=true;w.Renamed+=(s,e)=>dirty=true;w.Error+=(s,e)=>dirty=true;w.EnableRaisingEvents=true;watchers.Add(w);}catch{}
 }
 static string[] Roots(){return new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)}.Where(p=>!String.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();}
 static AppLibrarySnapshot Defaults(){var s=new AppLibrarySnapshot();string[] names={"未分类","游戏","学习","工作","娱乐","其他"};string[] ids={"uncategorized","games","study","work","media","other"};for(int i=0;i<names.Length;i++)s.categories.Add(new AppLibraryCategory{id=ids[i],name=names[i],order=i});return s;}
 AppLibrarySnapshot Read(){
  if(!File.Exists(file)){observed=null;return Defaults();}
  try {if(new FileInfo(file).Length>32*1024*1024)throw new InvalidDataException("应用库文件过大");observed=File.ReadAllText(file,Encoding.UTF8);var s=json.Deserialize<AppLibrarySnapshot>(observed);if(s==null||s.version!=1||s.entries==null||s.categories==null||s.folders==null||s.excluded==null)throw new InvalidDataException("应用库格式或版本不受支持");
   if(!s.categories.Any(c=>c.id=="uncategorized"))s.categories.Insert(0,new AppLibraryCategory{id="uncategorized",name="未分类"});
   foreach(var e in s.entries){if(!s.categories.Any(c=>c.id==e.category))e.category="uncategorized";if(!s.folders.Any(f=>f.id==e.folder&&f.category==e.category))e.folder="";if(e.sources==null)e.sources=new List<string>();}return s;
  }catch(Exception e){var s=Defaults();s.error="应用库读取失败，原文件已保留："+e.Message;return s;}
 }
 void Commit(AppLibrarySnapshot next){
  if(!String.IsNullOrEmpty(next.error))throw new InvalidOperationException(next.error);
  next.revision=Math.Max(next.revision,state.revision)+1;string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{string content=json.Serialize(next);using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(stream,new UTF8Encoding(false))){writer.Write(content);writer.Flush();stream.Flush(true);}if((File.Exists(file)?File.ReadAllText(file,Encoding.UTF8):null)!=observed)throw new IOException("应用库刚刚被外部修改，请刷新后重试。");if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);state=next;observed=content;}
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public AppLibrarySnapshot GetSnapshot(){lock(gate){var s=json.Deserialize<AppLibrarySnapshot>(json.Serialize(state));foreach(var e in s.entries){Index(e);e.icon=CachedIcon(e.path);}foreach(var a in new[]{new[]{"settings","PrismGlass 设置","设置 shezhi settings"},new[]{"windows-settings","Windows 设置","系统设置 xitong shezhi settings"},new[]{"task-manager","任务管理器","renwu guanliqi task manager"},new[]{"explorer","文件资源管理器","wenjian ziyuan guanliqi explorer"}}){var entry=new AppLibraryEntry{id="action:"+a[0],name=a[1],path=a[0],kind="action",category="uncategorized",folder="",alias=a[2],exists=true,order=Int32.MaxValue};Index(entry);s.entries.Add(entry);}s.excluded.Clear();return s;}}
 public AppLibraryEntry Find(string id){lock(gate){var e=state.entries.FirstOrDefault(x=>x.id==id);if(e==null)return null;var result=json.Deserialize<AppLibraryEntry>(json.Serialize(e));result.exists=result.exists&&EntryExists(result);return result;}}
 static string Text(string s){return (s??"").Trim();}
 static string Name(string s){s=Text(s);if(s.Length==0||s.Length>80)throw new ArgumentException("名称需为 1–80 个字符");return s;}
 internal static string Identity(string path,string arguments){path=Text(path);if(!path.StartsWith("shell:",StringComparison.OrdinalIgnoreCase))path=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);return path.ToLowerInvariant()+"\n"+Text(arguments);}
 static string EntryId(string identity){return "app:"+Convert.ToBase64String(Encoding.UTF8.GetBytes(identity));}
 internal static void Merge(AppLibrarySnapshot s,IEnumerable<AppLibraryEntry> discovered){
  foreach(var found in discovered){if(s.excluded.Contains(found.identity))continue;var e=s.entries.FirstOrDefault(x=>x.identity==found.identity);if(e==null){found.id=EntryId(found.identity);found.category="uncategorized";found.folder="";found.alias="";found.order=s.entries.Count+s.folders.Count;s.entries.Add(found);e=found;}else{e.exists=e.exists||found.exists;if(found.exists&&!Exists(e.path))e.path=found.path;e.target=found.target;e.kind=found.kind;if(!e.manual)e.name=found.name;}
   foreach(var source in found.sources)if(!e.sources.Contains(source,StringComparer.OrdinalIgnoreCase))e.sources.Add(source);
  }
 }
 public void Refresh(){var requested=DateTime.UtcNow;lock(scanGate){if(scanned>=requested)return;RefreshCore();}}
 void RefreshCore(){
  if(disposed)return;var found=new List<AppLibraryEntry>();Exception failure=null;Action scan=()=>{try{Discover(found);}catch(Exception e){failure=e;}};
  if(Thread.CurrentThread.GetApartmentState()==ApartmentState.STA)scan();else{var thread=new Thread(()=>scan());thread.SetApartmentState(ApartmentState.STA);thread.IsBackground=true;thread.Start();thread.Join();}
  string[] iconPaths;lock(gate){if(disposed)return;var next=Read();if(!String.IsNullOrEmpty(next.error)){state=next;return;}if(failure!=null){state.error="应用扫描失败："+failure.Message;return;}foreach(var e in next.entries)e.exists=e.manual&&EntryExists(e);Merge(next,found);Commit(next);iconPaths=next.entries.Select(e=>e.path).ToArray();}
  foreach(var path in iconPaths){if(disposed)return;GetIcon(path);}lock(gate){if(!disposed){dirty=false;scanned=DateTime.UtcNow;}}
 }
 static bool Exists(string path){return !String.IsNullOrEmpty(path)&&(path.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase)||File.Exists(path)||Directory.Exists(path));}
 static bool EntryExists(AppLibraryEntry entry){return Exists(entry.path)&&(String.IsNullOrEmpty(entry.target)||Exists(entry.target));}
 static object Get(object obj,string name,params object[] args){return obj.GetType().InvokeMember(name,BindingFlags.GetProperty,null,obj,args);}
 static object Call(object obj,string name,params object[] args){return obj.GetType().InvokeMember(name,BindingFlags.InvokeMethod,null,obj,args);}
 static void Release(object obj){if(obj!=null&&Marshal.IsComObject(obj))Marshal.FinalReleaseComObject(obj);}
 static AppLibraryEntry Entry(string path,object shortcuts){
  path=Path.GetFullPath(path);var target=path;string arguments="",name=Path.GetFileNameWithoutExtension(path);bool directory=Directory.Exists(path);
  if(Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase)&&shortcuts!=null){object link=null;try{link=Call(shortcuts,"CreateShortcut",path);string resolved=Convert.ToString(Get(link,"TargetPath"));arguments=Convert.ToString(Get(link,"Arguments"));if(!String.IsNullOrEmpty(resolved))target=resolved;}catch{}finally{Release(link);}}
  return new AppLibraryEntry{identity=Identity(target,arguments),name=directory?Path.GetFileName(path):name,path=path,target=target,arguments="",exists=(File.Exists(path)||directory)&&(target==path||File.Exists(target)||Directory.Exists(target)),kind=directory?"directory":new[]{".lnk",".url",".exe",".appref-ms"}.Contains(Path.GetExtension(path).ToLowerInvariant())?"app":"file",sources=new List<string>{path}};
 }
 static IEnumerable<string> Files(string path,bool recursive){
  var queue=new Queue<string>();queue.Enqueue(path);while(queue.Count>0){var current=queue.Dequeue();string[] files,dirs;try{files=Directory.GetFiles(current);dirs=Directory.GetDirectories(current);}catch{continue;}foreach(var f in files)yield return f;if(recursive){foreach(var d in dirs){FileAttributes a;try{a=File.GetAttributes(d);}catch{continue;}if((a&FileAttributes.ReparsePoint)==0)queue.Enqueue(d);}}else foreach(var d in dirs)yield return d;}
 }
 static void Discover(List<AppLibraryEntry> result){
  object shortcuts=null,shell=null,folder=null,items=null;try{var type=Type.GetTypeFromProgID("WScript.Shell");if(type!=null)shortcuts=Activator.CreateInstance(type);foreach(var path in Roots()){bool menu=path.IndexOf("Start Menu",StringComparison.OrdinalIgnoreCase)>=0||path==Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)||path==Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);foreach(var f in Files(path,menu)){if(Path.GetFileName(f).Equals("desktop.ini",StringComparison.OrdinalIgnoreCase))continue;if(menu&&!new[]{".lnk",".url",".exe",".appref-ms"}.Contains(Path.GetExtension(f).ToLowerInvariant()))continue;try{result.Add(Entry(f,shortcuts));}catch{}}}
   try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));folder=Call(shell,"NameSpace","shell:AppsFolder");if(folder!=null){items=Call(folder,"Items");int count=Convert.ToInt32(Get(items,"Count"));for(int i=0;i<count;i++){object item=null;try{item=Call(items,"Item",i);string id=Convert.ToString(Call(item,"ExtendedProperty","System.AppUserModel.ID"));if(String.IsNullOrEmpty(id)||!id.Contains("!"))continue;var path="shell:AppsFolder\\"+id;result.Add(new AppLibraryEntry{identity=Identity(path,""),name=Convert.ToString(Get(item,"Name")),path=path,target=path,arguments="",kind="uwp",exists=true,sources=new List<string>{path}});}catch{}finally{Release(item);}}}}catch{}
  }finally{Release(items);Release(folder);Release(shell);Release(shortcuts);}
 }
 public void Add(string path){if(!File.Exists(path)&&!Directory.Exists(path))throw new FileNotFoundException("找不到所选项目",path);object shortcuts=null;AppLibraryEntry entry;try{shortcuts=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));entry=Entry(path,shortcuts);}finally{Release(shortcuts);}lock(gate){var s=Read();if(!String.IsNullOrEmpty(s.error))throw new InvalidOperationException(s.error);s.excluded.Remove(entry.identity);entry.manual=true;Merge(s,new[]{entry});var current=s.entries.First(e=>e.identity==entry.identity);current.manual=true;current.exists=entry.exists;Commit(s);}GetIcon(path);}
 public void Repair(string id,string path){if(!File.Exists(path)&&!Directory.Exists(path))throw new FileNotFoundException("找不到所选项目",path);lock(gate){var s=Read();var e=s.entries.FirstOrDefault(x=>x.id==id);if(e==null)throw new ArgumentException("项目已移除");object shortcuts=null;AppLibraryEntry replacement;try{shortcuts=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));replacement=Entry(path,shortcuts);}finally{Release(shortcuts);}if(s.entries.Any(x=>x.id!=id&&x.identity==replacement.identity))throw new InvalidOperationException("所选目标已在应用库中");s.excluded.Add(e.identity);e.identity=replacement.identity;e.path=replacement.path;e.target=replacement.target;e.arguments=replacement.arguments;e.sources=replacement.sources;e.kind=replacement.kind;e.manual=true;e.exists=replacement.exists;Commit(s);}GetIcon(path);}
 public void Apply(string command,string id,string target,string name){lock(gate){var s=Read();if(!String.IsNullOrEmpty(s.error))throw new InvalidOperationException(s.error);var e=s.entries.FirstOrDefault(x=>x.id==id);var f=s.folders.FirstOrDefault(x=>x.id==id);var c=s.categories.FirstOrDefault(x=>x.id==id);
  switch(command){
   case "categoryCreate":s.categories.Add(new AppLibraryCategory{id="category:"+Guid.NewGuid().ToString("N"),name=Name(name),order=s.categories.Count});break;
   case "categoryRename":if(c==null||id=="uncategorized")throw new ArgumentException("此分类不可改名");c.name=Name(name);break;
   case "categoryDelete":if(c==null||id=="uncategorized")throw new ArgumentException("此分类不可移除");foreach(var x in s.entries.Where(x=>x.category==id)){x.category="uncategorized";x.folder="";}s.folders.RemoveAll(x=>x.category==id);s.categories.Remove(c);break;
   case "categoryMove":var categories=s.categories.OrderBy(x=>x.order).ToList();var destination=categories.FindIndex(x=>x.id==target);if(c==null||destination<0)throw new ArgumentException("分类不存在");categories.Remove(c);destination=categories.FindIndex(x=>x.id==target);categories.Insert(destination<0?categories.Count:destination,c);for(int i=0;i<categories.Count;i++)categories[i].order=i;break;
   case "folderCreate":if(!s.categories.Any(x=>x.id==target))throw new ArgumentException("分类不存在");s.folders.Add(new AppLibraryFolder{id="folder:"+Guid.NewGuid().ToString("N"),name=Name(name),category=target,order=s.entries.Count+s.folders.Count});break;
   case "folderRename":if(f==null)throw new ArgumentException("文件夹不存在");f.name=Name(name);break;
   case "folderDelete":if(f==null)throw new ArgumentException("文件夹不存在");foreach(var x in s.entries.Where(x=>x.folder==id))x.folder="";s.folders.Remove(f);break;
   case "move":var folder=s.folders.FirstOrDefault(x=>x.id==target);string category=folder==null?target:folder.category;if(!s.categories.Any(x=>x.id==category)||(e==null&&f==null))throw new ArgumentException("目标不存在");if(e!=null){e.category=category;e.folder=folder==null?"":folder.id;e.order=s.entries.Select(x=>x.order).DefaultIfEmpty(0).Max()+1;}else{if(folder!=null)throw new ArgumentException("不支持嵌套文件夹");f.category=category;foreach(var x in s.entries.Where(x=>x.folder==id))x.category=category;}break;
   case "group":var other=s.entries.FirstOrDefault(x=>x.id==target);if(e==null||other==null||e==other||e.folder!=""||other.folder!=""||e.category!=other.category)throw new ArgumentException("只能将同一分类的两个项目收纳为文件夹");var group=new AppLibraryFolder{id="folder:"+Guid.NewGuid().ToString("N"),name=Name(name),category=e.category,order=Math.Min(e.order,other.order)};s.folders.Add(group);e.folder=other.folder=group.id;break;
   case "reorder":Reorder(s,id,target);break;
   case "alias":if(e==null)throw new ArgumentException("项目不存在");e.alias=Text(name);if(e.alias.Length>256)throw new ArgumentException("别名最多 256 个字符");break;
   case "remove":if(e==null)throw new ArgumentException("项目不存在");s.excluded.Add(e.identity);s.entries.Remove(e);break;
   default:throw new ArgumentException("不支持的应用库操作");
  }Commit(s);
 }}
 static void Reorder(AppLibrarySnapshot s,string id,string target){var e=s.entries.FirstOrDefault(x=>x.id==id);var f=s.folders.FirstOrDefault(x=>x.id==id);if(e==null&&f==null)throw new ArgumentException("项目不存在");string folder=e!=null?e.folder:"";var items=s.entries.Where(x=>x.folder==folder).Select(x=>new KeyValuePair<string,int>(x.id,x.order)).Concat(folder==""?s.folders.Select(x=>new KeyValuePair<string,int>(x.id,x.order)):Enumerable.Empty<KeyValuePair<string,int>>()).OrderBy(x=>x.Value).Select(x=>x.Key).ToList();if(id==target)return;items.Remove(id);int at=items.IndexOf(target);if(at<0&&!String.IsNullOrEmpty(target))throw new ArgumentException("排序目标不在当前层级");items.Insert(at<0?items.Count:at,id);for(int i=0;i<items.Count;i++){var app=s.entries.FirstOrDefault(x=>x.id==items[i]);if(app!=null)app.order=i;else s.folders.First(x=>x.id==items[i]).order=i;}}
 // shortcut: Contextual pronunciation covers 音乐; extend it when another app name needs disambiguation.
 void Index(AppLibraryEntry e){var full=new StringBuilder();var initial=new StringBuilder();string text=(e.name??"")+" "+(e.alias??"");for(int i=0;i<text.Length;i++){string ch=Char.IsHighSurrogate(text[i])&&i+1<text.Length?text.Substring(i++,2):text[i].ToString();string phonetic;if(pinyin.TryGetValue(ch,out phonetic)){if(ch=="乐"&&i>0&&text[i-1]=='音')phonetic="yue";full.Append(phonetic);initial.Append(phonetic[0]);}else{full.Append(ch.ToLowerInvariant());if(Char.IsLetterOrDigit(ch,0))initial.Append(ch.ToLowerInvariant());}}e.search=text.ToLowerInvariant()+" "+full;e.initials=initial.ToString();}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct SHFILEINFO{public IntPtr icon;public int index;public uint attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string display;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string type;}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SHGetFileInfo(string path,uint attributes,out SHFILEINFO info,uint size,uint flags);
 [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
 [StructLayout(LayoutKind.Sequential)]struct ImageSize{public int x,y;}
 [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface ImageFactory{[PreserveSig]int GetImage(ImageSize size,int flags,out IntPtr bitmap);}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]static extern void SHCreateItemFromParsingName(string path,IntPtr context,ref Guid guid,[MarshalAs(UnmanagedType.Interface)]out ImageFactory item);
 [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
 [DllImport("gdi32.dll")]static extern IntPtr CreateCompatibleDC(IntPtr h);
 [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr h);
 [StructLayout(LayoutKind.Sequential)]struct BitmapInfo{public uint size;public int width,height;public ushort planes,bits;public uint compression,imageSize;public int x,y;public uint used,important;}
 [DllImport("gdi32.dll")]static extern int GetDIBits(IntPtr dc,IntPtr bitmap,uint start,uint rows,IntPtr data,ref BitmapInfo info,uint usage);
 static string ShellIcon(string path){ImageFactory factory=null;IntPtr bitmap=IntPtr.Zero;try{var iid=typeof(ImageFactory).GUID;SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out factory);if(factory.GetImage(new ImageSize{x=64,y=64},5,out bitmap)!=0||bitmap==IntPtr.Zero)return "";using(var source=Image.FromHbitmap(bitmap))using(var output=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb)){var data=output.LockBits(new Rectangle(0,0,output.Width,output.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);var info=new BitmapInfo{size=40,width=output.Width,height=-output.Height,planes=1,bits=32};var dc=CreateCompatibleDC(IntPtr.Zero);try{if(GetDIBits(dc,bitmap,0,(uint)output.Height,data.Scan0,ref info,0)==0)return "";}finally{DeleteDC(dc);output.UnlockBits(data);}using(var stream=new MemoryStream()){output.Save(stream,ImageFormat.Png);return "data:image/png;base64,"+Convert.ToBase64String(stream.ToArray());}}}finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);Release(factory);}}
 // Snapshot publication must never wait for Shell icon extraction on the UI thread.
 string CachedIcon(string path){string value;lock(icons)return path!=null&&icons.TryGetValue(path,out value)?value:"";}
 string GetIcon(string path){if(String.IsNullOrEmpty(path))return "";string value;lock(icons){if(icons.TryGetValue(path,out value))return value;}try{value=ShellIcon(path);}catch{value="";}if(String.IsNullOrEmpty(value))try{SHFILEINFO info;SHGetFileInfo(path,0,out info,(uint)Marshal.SizeOf(typeof(SHFILEINFO)),0x100);if(info.icon!=IntPtr.Zero)try{using(var icon=Icon.FromHandle(info.icon))using(var bitmap=icon.ToBitmap())using(var stream=new MemoryStream()){bitmap.Save(stream,ImageFormat.Png);value="data:image/png;base64,"+Convert.ToBase64String(stream.ToArray());}}finally{DestroyIcon(info.icon);}}catch{}lock(icons){if(!disposed&&icons.Count<1024)icons[path]=value;}return value;}
 public void Dispose(){lock(gate){disposed=true;foreach(var w in watchers)w.Dispose();watchers.Clear();lock(icons)icons.Clear();}}
}
