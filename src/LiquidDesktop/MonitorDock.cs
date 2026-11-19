using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

internal sealed class MonitorDock : IDisposable {
 const string Marker="; PrismGlass generated monitor instance";
 static readonly object generatedGate=new object();
 readonly string generation=Marker+" "+Guid.NewGuid().ToString("N");
 internal System.Threading.Tasks.Task Cleanup {get;private set;}
 internal static string CleanupError {get;private set;}
 static int catalogRevision;static DateTime catalogReady=DateTime.MinValue;int appliedCatalogRevision;
 readonly string root,instance,skinRoot;string signature="",generatedFile;volatile bool enabled,disposed;bool catalogRefreshed;DateTime nextAttempt=DateTime.MinValue,nextVisibilityAttempt=DateTime.MinValue,nextIconAttempt=DateTime.MinValue;
 readonly Func<string,bool> sendPointer;readonly object pointerGate=new object();bool pointerSending,pointerPending;double requestedPointer;int pointerRevision;
 public string SkinName {get{return instance=="primary"?"Dock":"Dock-"+instance;}}
 public string ConfigName {get{return "PrismGlass\\"+SkinName;}}
 [StructLayout(LayoutKind.Sequential)]struct POINT {public int x,y;}
 [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(POINT point,uint flags);
 [DllImport("shcore.dll")]static extern int GetDpiForMonitor(IntPtr monitor,int kind,out uint x,out uint y);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string title);
 [StructLayout(LayoutKind.Sequential)]struct CopyData {public IntPtr id;public int bytes;public IntPtr text;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr sender,ref CopyData data,uint flags,uint timeout,out IntPtr result);
 [DllImport("user32.dll")]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr sender,IntPtr data,uint flags,uint timeout,out IntPtr result);
 IntPtr checkedWindow;bool checking,responsive;
 internal DateTime LastResponse {get;private set;}
 internal bool Responsive(IntPtr window){
  lock(pointerGate){
   if(checkedWindow!=window){checkedWindow=window;responsive=false;LastResponse=DateTime.MinValue;}
   if(window==IntPtr.Zero)return false;
   if(!checking){
    checking=true;
    ThreadPool.QueueUserWorkItem(_=>{
     IntPtr ignored;bool ok=SendMessageTimeout(window,0,IntPtr.Zero,IntPtr.Zero,2,100,out ignored)!=IntPtr.Zero;
     lock(pointerGate){checking=false;if(checkedWindow==window){responsive=ok;if(ok)LastResponse=DateTime.UtcNow;}}
    });
   }
   return responsive;
  }
 }
 public MonitorDock(string directory,string screenInstance):this(directory,screenInstance,null){}
 internal MonitorDock(string directory,string screenInstance,Func<string,bool> pointerOutput){if(!Regex.IsMatch(screenInstance,"^(primary|monitor-[0-9]+)$"))throw new ArgumentException("Invalid Dock instance");root=directory;instance=screenInstance;var configured=Path.Combine(root,"skin-path.txt");skinRoot=Path.GetFullPath(File.Exists(configured)?File.ReadAllText(configured).Trim():Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Rainmeter","Skins","PrismGlass"));sendPointer=pointerOutput??(command=>SendSkinCommand(FindWindow("RainmeterMeterWindow",Path.Combine(skinRoot,SkinName,"Main.ini")),command));}
 static string Set(string text,string key,string value){string expression="(?m)^"+Regex.Escape(key)+"=.*$";if(Regex.IsMatch(text,expression))return Regex.Replace(text,expression,key+"="+value);return text.Replace("[Variables]","[Variables]\r\n"+key+"="+value);}
 public static double PixelScale(Rectangle bounds){try{uint x,y;var point=new POINT{x=bounds.Left+bounds.Width/2,y=bounds.Top+bounds.Height/2};if(GetDpiForMonitor(MonitorFromPoint(point,2),0,out x,out y)==0&&x>0)return x/96.0;}catch(EntryPointNotFoundException){}catch(DllNotFoundException){}return 1;}
 internal RainmeterDispatch Dispatch;string desiredSignature;
 static void Bang(string command,RainmeterDispatch dispatch=null){
  Func<bool> send=()=>{RainmeterCommands.Send(RuntimePaths.Rainmeter,command,50);return true;};
  if(dispatch==null)send();else dispatch.Send("configuration",command,send);
 }
 internal static bool SkinCatalogReady {get{return DateTime.UtcNow>=catalogReady;}}
 internal void DisplaysChanged(){if(Dispatch!=null)Dispatch.Reset();Pointer(-1);signature="";lock(pointerGate){pointerRevision++;lastPointer=-2;pointerRetry=DateTime.MinValue;}nextAttempt=nextVisibilityAttempt=nextIconAttempt=DateTime.MinValue;}
 internal static void RefreshSkinCatalog(RainmeterDispatch dispatch=null){Bang("!RefreshApp",dispatch);catalogRevision++;catalogReady=DateTime.UtcNow.AddSeconds(1);}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowLong(IntPtr window,int index);
 bool? maskAtLastProbe;
 string LayerCommand(bool mask){return "[!ZPos "+(mask?"0":"1")+" \""+ConfigName+"\"]";}
 public void Configure(MonitorRecord monitor,MonitorPreferences preferences,int iconSize,bool startupMask=false){
  if(disposed)return;var bounds=monitor.Screen.WorkingArea;bounds.Height=monitor.Screen.Bounds.Bottom-bounds.Top;string next=preferences.enabled+"|"+preferences.dock+"|"+bounds+"|"+preferences.dockOffset+"|"+iconSize+"|"+PixelScale(bounds);
  if(maskAtLastProbe!=startupMask){if(Dispatch!=null){Dispatch.Cancel("configuration");Dispatch.Cancel("layer");}maskAtLastProbe=startupMask;}
  if(Dispatch!=null&&desiredSignature!=next){Dispatch.Cancel("configuration");signature="";desiredSignature=next;}
  bool show=preferences.enabled&&preferences.dock;
  if(show&&DateTime.UtcNow<catalogReady)return;
  if(appliedCatalogRevision==catalogRevision&&next==signature){
   if(!show)return;
   var window=FindWindow("RainmeterMeterWindow",Path.Combine(skinRoot,SkinName,"Main.ini"));
   if(window!=IntPtr.Zero){
    // Refresh starts every skin on the normal layer. Reconcile only a mismatched installed skin.
    bool topmost=(GetWindowLong(window,-20)&8)!=0;
    if(topmost==startupMask){
     string layer=LayerCommand(startupMask);Func<bool> apply=()=>SendSkinCommand(window,layer);
     if(Dispatch==null)apply();else Dispatch.Send("layer",window+layer,apply);
    }else if(Dispatch!=null)Dispatch.Cancel("layer");
    return;
   }
   if(DateTime.UtcNow<nextAttempt)return;
  }
  if(!show){if(enabled||instance=="primary"||Dispatch!=null)Bang("!DeactivateConfig \""+ConfigName+"\"",Dispatch);enabled=false;signature=next;appliedCatalogRevision=catalogRevision;return;}
  var variables=new[]{new[]{"DockInstance",instance},new[]{"DockMonitorLeft",bounds.Left.ToString()},new[]{"DockMonitorTop",bounds.Top.ToString()},new[]{"DockMonitorWidth",bounds.Width.ToString()},new[]{"DockMonitorHeight",bounds.Height.ToString()},new[]{"DockOffset",preferences.dockOffset.ToString()},new[]{"DockIconSize",iconSize.ToString()},new[]{"DockPixelScale",PixelScale(bounds).ToString(CultureInfo.InvariantCulture)},new[]{"DockLyricsFile","dock-lyrics.txt"}};
  if(instance!="primary"&&next!=signature){
   var source=Path.Combine(skinRoot,"Dock","Main.ini");if(!File.Exists(source))throw new IOException("未找到已安装的 PrismGlass Dock 皮肤，多屏 Dock 未启动。");
   var folder=Path.GetFullPath(Path.Combine(skinRoot,SkinName));if(!folder.StartsWith(skinRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid Dock directory");
   if(Directory.Exists(folder)&&(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Dock 实例目录不能是链接。");
   string target=Path.Combine(folder,"Main.ini");lock(generatedGate){if(File.Exists(target)&&!File.ReadAllText(target).StartsWith(Marker,StringComparison.Ordinal))throw new IOException("同名 Dock 目录包含已有皮肤，未覆盖。");
   string content=File.ReadAllText(source);foreach(var pair in variables)content=Set(content,pair[0],pair[1]);Directory.CreateDirectory(folder);File.WriteAllText(target,generation+"\r\n"+content,Encoding.Unicode);generatedFile=target;}
  }
  // RefreshApp posts a delayed refresh. Activating in the same multi-bang runs
  // before the new directory is registered, so activation must be a later call.
  // RefreshApp also resets existing skins, including their runtime monitor and DPI variables.
  if(instance!="primary"&&!catalogRefreshed){RefreshSkinCatalog(Dispatch);catalogRefreshed=true;signature=next;nextAttempt=catalogReady;return;}
  var command=new StringBuilder();command.Append("[!ActivateConfig \"").Append(ConfigName).Append("\" \"Main.ini\"]");foreach(var pair in variables)command.Append("[!SetVariable ").Append(pair[0]).Append(" \"").Append(pair[1]).Append("\" \"").Append(ConfigName).Append("\"]");command.Append("[!CommandMeasure Dock \"ReloadLayout()\" \"").Append(ConfigName).Append("\"]");command.Append(LayerCommand(startupMask));Bang(command.ToString(),Dispatch);enabled=true;signature=next;appliedCatalogRevision=catalogRevision;lock(pointerGate){pointerRevision++;lastPointer=-2;}nextAttempt=DateTime.UtcNow.AddSeconds(2);
 }
 internal static bool SendSkinCommand(IntPtr window,string command){
  if(window==IntPtr.Zero)return false;
  var data=new CopyData{id=new IntPtr(1),bytes=(command.Length+1)*2,text=Marshal.StringToHGlobalUni(command)};
  try{IntPtr result;return SendMessageTimeout(window,0x004A,IntPtr.Zero,ref data,2,50,out result)!=IntPtr.Zero&&result!=IntPtr.Zero;}finally{Marshal.FreeHGlobal(data.text);}
 }
 public bool SetVisible(bool visible){
  // Rainmeter accepts bangs on the skin HWND; avoid process startup and waits on every hover.
  if(!enabled||disposed||DateTime.UtcNow<nextVisibilityAttempt)return false;
    IntPtr window=FindWindow("RainmeterMeterWindow",Path.Combine(skinRoot,SkinName,"Main.ini"));string command=(visible?"!Show":"!Hide")+" \""+ConfigName+"\"";
  Func<bool> deliver=()=>SendSkinCommand(window,command);
  bool sent;if(Dispatch==null)sent=deliver();else if(!Dispatch.TrySend("visibility",window+command,deliver,out sent))return false;
  nextVisibilityAttempt=sent?DateTime.MinValue:DateTime.UtcNow.AddMilliseconds(250);return sent;
 }
 internal static bool TryIconMessage(string value,out int sequence,out int alpha){
  sequence=alpha=0;if(value==null)return false;var parts=value.Split(':');
  return parts.Length==2&&Int32.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out sequence)&&sequence>0&&Int32.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out alpha)&&alpha>=0&&alpha<=255;
 }
 double lastPointer=-2;DateTime pointerRetry;
 public void Pointer(double fraction){
  if(disposed||!enabled||double.IsNaN(fraction)||fraction< -1||fraction>1)return;
  lock(pointerGate){requestedPointer=fraction;pointerPending=true;if(pointerSending||DateTime.UtcNow<pointerRetry)return;if(Math.Abs(fraction-lastPointer)<.0002){pointerPending=false;return;}pointerSending=true;}
  // Keep one native command and one latest position; Rainmeter must not stall the UI thread.
  ThreadPool.QueueUserWorkItem(_=>{
   while(true){
    double latest;int revision;lock(pointerGate){if(disposed||!enabled||!pointerPending){pointerSending=false;return;}latest=requestedPointer;revision=pointerRevision;pointerPending=false;}
    bool sent=false;try{sent=sendPointer("!CommandMeasure Dock \"Pointer("+latest.ToString(CultureInfo.InvariantCulture)+")\" \""+ConfigName+"\"");}catch(InvalidOperationException){}catch(ExternalException){}
    lock(pointerGate){if(!sent){lastPointer=-2;pointerRetry=DateTime.UtcNow.AddMilliseconds(250);pointerSending=false;return;}if(revision==pointerRevision)lastPointer=latest;if(pointerPending&&Math.Abs(requestedPointer-lastPointer)<.0002)pointerPending=false;}
   }
  });
 }
 public bool SetIconOpacity(int alpha){
  if(alpha<0||alpha>255||!enabled||disposed||DateTime.UtcNow<nextIconAttempt)return false;
  bool sent=SendSkinCommand(FindWindow("RainmeterMeterWindow",Path.Combine(skinRoot,SkinName,"Main.ini")),"!CommandMeasure Dock \"SetIconOpacity("+alpha.ToString(CultureInfo.InvariantCulture)+")\" \""+ConfigName+"\"");
  nextIconAttempt=sent?DateTime.MinValue:DateTime.UtcNow.AddMilliseconds(250);return sent;
 }
 public void Dispose(){
  if(disposed)return;
  disposed=true;if(Dispatch!=null)Dispatch.Reset();
  try{Bang("!DeactivateConfig \""+ConfigName+"\"");}
  catch(Exception error){CleanupError=ConfigName+"："+error.Message;return;}
  if(generatedFile==null)return;
  Cleanup=System.Threading.Tasks.Task.Run(()=>{
   try{
    var deadline=System.Diagnostics.Stopwatch.StartNew();
    while(deadline.ElapsedMilliseconds<2000){
     lock(generatedGate){
      if(!File.Exists(generatedFile))return;
      using(var reader=new StreamReader(generatedFile)){if(reader.ReadLine()!=generation)return;}
      if(!RainmeterCommands.SkinOpen(generatedFile)){
       File.Delete(generatedFile);
       var directory=Path.GetDirectoryName(generatedFile);
       if(Directory.GetFileSystemEntries(directory).Length==0)Directory.Delete(directory);
       return;
      }
     }
     Thread.Sleep(50);
    }
    CleanupError=ConfigName+"：未确认 Dock 皮肤关闭，保留生成的皮肤文件。";
   }catch(Exception error){CleanupError=ConfigName+"："+error.Message;}
  });
 }
}
