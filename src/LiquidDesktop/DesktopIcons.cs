using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Microsoft.Win32;

// Keep Explorer's saved view ordinary; beautification hides only its current HWND.
public static class DesktopIcons {
 const uint NoIcons=0x1000;
 const string DesktopKey=@"Software\Microsoft\Windows\Shell\Bags\1\Desktop";
 [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] struct PropertyKey {public Guid Format;public uint Id;}
 [StructLayout(LayoutKind.Sequential)] struct ShellState {public uint Flags,Unused1,Unused2;public int Sort,Direction;public uint Version,Unused3,Flags2;}
 [ComImport,Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Services {[PreserveSig]int QueryService(ref Guid service,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object result);}
 [ComImport,Guid("000214E2-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface Browser {
  void GetWindow(out IntPtr window);void ContextSensitiveHelp(bool enter);
  void InsertMenus(IntPtr menu,IntPtr widths);void SetMenu(IntPtr menu,IntPtr oleMenu,IntPtr active);void RemoveMenus(IntPtr menu);
  void SetStatusText([MarshalAs(UnmanagedType.LPWStr)]string text);void EnableModeless(bool enable);void TranslateAccelerator(IntPtr message,ushort id);
  void BrowseObject(IntPtr id,uint flags);void GetViewStateStream(uint mode,out IntPtr stream);void GetControlWindow(uint id,out IntPtr window);
  void SendControlMessage(uint id,uint message,IntPtr w,IntPtr l,out IntPtr result);
  void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)]out object view);
 }
 [ComImport,Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface FolderView {
  void GetCurrentViewMode(out uint mode);void SetCurrentViewMode(uint mode);void GetFolder(ref Guid iid,out IntPtr folder);
  void Item(int index,out IntPtr item);void ItemCount(uint flags,out int count);void Items(uint flags,ref Guid iid,out IntPtr items);
  void GetSelectionMarkedItem(out int item);void GetFocusedItem(out int item);void GetItemPosition(IntPtr item,out Point point);
  void GetSpacing(out Point point);void GetDefaultSpacing(out Point point);[PreserveSig]int GetAutoArrange();
  void SelectItem(int item,uint flags);void SelectAndPositionItems(uint count,IntPtr items,IntPtr points,uint flags);
  void SetGroupBy(ref PropertyKey key,bool ascending);void GetGroupBy(out PropertyKey key,out bool ascending);
  void SetViewProperty(IntPtr item,ref PropertyKey key,IntPtr value);void GetViewProperty(IntPtr item,ref PropertyKey key,IntPtr value);
  void SetTileViewProperties(IntPtr item,[MarshalAs(UnmanagedType.LPWStr)]string properties);
  void SetExtendedTileViewProperties(IntPtr item,[MarshalAs(UnmanagedType.LPWStr)]string properties);
  void SetText(uint type,[MarshalAs(UnmanagedType.LPWStr)]string text);
  void SetCurrentFolderFlags(uint mask,uint flags);void GetCurrentFolderFlags(out uint flags);
 }
 delegate bool EnumProc(IntPtr window,IntPtr parameter);
 [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc callback,IntPtr parameter);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")]static extern bool GetLayeredWindowAttributes(IntPtr window,out uint key,out byte alpha,out uint flags);
 [DllImport("user32.dll")]static extern bool SetLayeredWindowAttributes(IntPtr window,uint key,byte alpha,uint flags);
 [DllImport("user32.dll")]static extern bool RedrawWindow(IntPtr window,IntPtr rect,IntPtr region,uint flags);
 [DllImport("shell32.dll")]static extern void SHGetSetSettings(ref ShellState state,uint mask,bool save);
 static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
 static T WithView<T>(Func<FolderView,T> action){
  object windows=null,desktop=null,browser=null,view=null;
  try{
   windows=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")));
   object[] args={0,null,8,0,1}; // CSIDL_DESKTOP, SWC_DESKTOP, SWFO_NEEDDISPATCH
   desktop=windows.GetType().InvokeMember("FindWindowSW",BindingFlags.InvokeMethod,null,windows,args);
   if(desktop==null)throw new InvalidOperationException("Explorer desktop view is unavailable.");
   var service=new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");var iid=new Guid("000214E2-0000-0000-C000-000000000046");
   Marshal.ThrowExceptionForHR(((Services)desktop).QueryService(ref service,ref iid,out browser));
   ((Browser)browser).QueryActiveShellView(out view);
   return action((FolderView)view);
  }finally{Release(view);Release(browser);Release(desktop);Release(windows);}
 }
 public static uint CurrentFlags(){return WithView(view=>{uint flags;view.GetCurrentFolderFlags(out flags);return flags;});}
 public static int SavedFlags(){using(var key=Registry.CurrentUser.OpenSubKey(DesktopKey)){return key==null?0:Convert.ToInt32(key.GetValue("FFlags",0));}}
 public static bool Visible(){bool visible=false;EnumWindows((window,p)=>{var view=FindWindowEx(window,IntPtr.Zero,"SHELLDLL_DefView",null);if(view==IntPtr.Zero)return true;var list=FindWindowEx(view,IntPtr.Zero,"SysListView32",null);visible=list!=IntPtr.Zero&&IsWindowVisible(view)&&IsWindowVisible(list);return false;},IntPtr.Zero);return visible;}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHGetNameFromIDList(IntPtr item,uint kind,out IntPtr name);
 internal sealed class Position {public string path;public int x,y;}
 static string ItemName(IntPtr item){IntPtr name=IntPtr.Zero;try{Marshal.ThrowExceptionForHR(SHGetNameFromIDList(item,0x80028000,out name));return Marshal.PtrToStringUni(name);}finally{if(name!=IntPtr.Zero)Marshal.FreeCoTaskMem(name);}}
 internal static Position[] CapturePositions(){return WithView(view=>{int count;view.ItemCount(2,out count);var result=new List<Position>();for(int i=0;i<count;i++){IntPtr item=IntPtr.Zero;try{view.Item(i,out item);Point point;view.GetItemPosition(item,out point);result.Add(new Position{path=ItemName(item),x=point.X,y=point.Y});}finally{if(item!=IntPtr.Zero)Marshal.FreeCoTaskMem(item);}}return result.ToArray();});}
 internal static void RestorePositions(Position[] positions){WithView(view=>{
  if(view.GetAutoArrange()==0)return true;
  var wanted=new Dictionary<string,Position>(StringComparer.OrdinalIgnoreCase);foreach(var position in positions)if(position!=null&&!String.IsNullOrEmpty(position.path))wanted[position.path]=position;
  int count;view.ItemCount(2,out count);var items=new List<IntPtr>();var points=new List<Point>();IntPtr itemArray=IntPtr.Zero,pointArray=IntPtr.Zero;
  try{for(int i=0;i<count;i++){IntPtr item=IntPtr.Zero;try{view.Item(i,out item);Position position;if(!wanted.TryGetValue(ItemName(item),out position))continue;Point current;view.GetItemPosition(item,out current);if(current.X==position.x&&current.Y==position.y)continue;items.Add(item);item=IntPtr.Zero;points.Add(new Point{X=position.x,Y=position.y});}finally{if(item!=IntPtr.Zero)Marshal.FreeCoTaskMem(item);}}
   if(items.Count==0)return true;itemArray=Marshal.AllocHGlobal(items.Count*IntPtr.Size);pointArray=Marshal.AllocHGlobal(items.Count*8);
   for(int i=0;i<items.Count;i++){Marshal.WriteIntPtr(itemArray,i*IntPtr.Size,items[i]);Marshal.StructureToPtr(points[i],IntPtr.Add(pointArray,i*8),false);}
   view.SelectAndPositionItems((uint)items.Count,itemArray,pointArray,0x80000080);return true;
  }finally{foreach(var item in items)Marshal.FreeCoTaskMem(item);if(itemArray!=IntPtr.Zero)Marshal.FreeHGlobal(itemArray);if(pointArray!=IntPtr.Zero)Marshal.FreeHGlobal(pointArray);}
 });}
 static void OrdinaryPreference(){
  // Updating only Advanced\HideIcons leaves FWF_NOICONS cached in the desktop
  // view and in Bags\1\Desktop. Explorer can load that stale flag next login.
  var state=new ShellState();SHGetSetSettings(ref state,0x4000,true); // SSF_HIDEICONS only
  WithView(view=>{uint flags;view.GetCurrentFolderFlags(out flags);if((flags&NoIcons)!=0)view.SetCurrentFolderFlags(NoIcons,0);view.GetCurrentFolderFlags(out flags);if((flags&NoIcons)!=0)throw new InvalidOperationException("Explorer still has its hide-icons flag set.");return true;});
  using(var key=Registry.CurrentUser.OpenSubKey(DesktopKey,true)){
   if(key!=null){var value=key.GetValue("FFlags");if(value is int&&(((int)value)&(int)NoIcons)!=0)key.SetValue("FFlags",((int)value)&~(int)NoIcons,RegistryValueKind.DWord);}
  }
 }
 public static void SetVisible(bool visible){
  OrdinaryPreference();
  bool found=false;
  EnumWindows((window,p)=>{var view=FindWindowEx(window,IntPtr.Zero,"SHELLDLL_DefView",null);if(view==IntPtr.Zero)return true;var list=FindWindowEx(view,IntPtr.Zero,"SysListView32",null);if(list==IntPtr.Zero)return true;found=true;
   if(visible){ShowWindow(view,5);uint key,flags;byte alpha;if(GetLayeredWindowAttributes(view,out key,out alpha,out flags)&&(flags&2)!=0)SetLayeredWindowAttributes(view,key,255,flags);}
   ShowWindow(list,visible?5:0);RedrawWindow(view,IntPtr.Zero,IntPtr.Zero,0x185);return false;
  },IntPtr.Zero);
  if(!found)throw new InvalidOperationException("Explorer desktop icon window is unavailable.");
 }
}
