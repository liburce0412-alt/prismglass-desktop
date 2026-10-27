using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class MonitorDock : IDisposable {
 const string Marker="; PrismGlass generated monitor instance";
 readonly string root,instance,skinRoot;string signature="",generatedFile;bool enabled,disposed,catalogRefreshed;DateTime nextAttempt=DateTime.MinValue,nextVisibilityAttempt=DateTime.MinValue,nextIconAttempt=DateTime.MinValue;
 public string SkinName {get{return instance=="primary"?"Dock":"Dock-"+instance;}}
 public string ConfigName {get{return "PrismGlass\\"+SkinName;}}
 [StructLayout(LayoutKind.Sequential)]struct POINT {public int x,y;}
 [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(POINT point,uint flags);
 [DllImport("shcore.dll")]static extern int GetDpiForMonitor(IntPtr monitor,int kind,out uint x,out uint y);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string className,string title);
 [StructLayout(LayoutKind.Sequential)]struct CopyData {public IntPtr id;public int bytes;public IntPtr text;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr sender,ref CopyData data,uint flags,uint timeout,out IntPtr result);
 public MonitorDock(string directory,string screenInstance){if(!Regex.IsMatch(screenInstance,"^(primary|monitor-[0-9]+)$"))throw new ArgumentException("Invalid Dock instance");root=directory;instance=screenInstance;var configured=Path.Combine(root,"skin-path.txt");skinRoot=Path.GetFullPath(File.Exists(configured)?File.ReadAllText(configured).Trim():Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Rainmeter","Skins","PrismGlass"));}
 static string Set(string text,string key,string value){string expression="(?m)^"+Regex.Escape(key)+"=.*$";if(Regex.IsMatch(text,expression))return Regex.Replace(text,expression,key+"="+value);return text.Replace("[Variables]","[Variables]\r\n"+key+"="+value);}
 public static double PixelScale(Rectangle bounds){try{uint x,y;var point=new POINT{x=bounds.Left+bounds.Width/2,y=bounds.Top+bounds.Height/2};if(GetDpiForMonitor(MonitorFromPoint(point,2),0,out x,out y)==0&&x>0)return x/96.0;}catch(EntryPointNotFoundException){}catch(DllNotFoundException){}return 1;}
 void Bang(string command){using(var process=Process.Start(new ProcessStartInfo(RuntimePaths.Rainmeter,command){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){if(process!=null)process.WaitForExit(1000);}}
 public void Configure(MonitorRecord monitor,MonitorPreferences preferences,int iconSize){
  if(disposed)return;var bounds=monitor.Screen.WorkingArea;bounds.Height=monitor.Screen.Bounds.Bottom-bounds.Top;string next=preferences.enabled+"|"+preferences.dock+"|"+bounds+"|"+preferences.dockOffset+"|"+iconSize+"|"+PixelScale(bounds);
  bool show=preferences.enabled&&preferences.dock;
  if(next==signature&&(!show||FindWindow("RainmeterMeterWindow",Path.Combine(skinRoot,SkinName,"Main.ini"))!=IntPtr.Zero||DateTime.UtcNow<nextAttempt))return;
  if(!show){if(enabled||instance=="primary")Bang("!DeactivateConfig \""+ConfigName+"\"");enabled=false;signature=next;return;}
  var variables=new[]{new[]{"DockInstance",instance},new[]{"DockMonitorLeft",bounds.Left.ToString()},new[]{"DockMonitorTop",bounds.Top.ToString()},new[]{"DockMonitorWidth",bounds.Width.ToString()},new[]{"DockMonitorHeight",bounds.Height.ToString()},new[]{"DockOffset",preferences.dockOffset.ToString()},new[]{"DockIconSize",iconSize.ToString()},new[]{"DockPixelScale",instance=="primary"?"0":PixelScale(bounds).ToString(CultureInfo.InvariantCulture)},new[]{"DockLyricsFile","dock-lyrics.txt"}};
  if(instance!="primary"&&next!=signature){
   var source=Path.Combine(skinRoot,"Dock","Main.ini");if(!File.Exists(source))throw new IOException("未找到已安装的 PrismGlass Dock 皮肤，多屏 Dock 未启动。");
   var folder=Path.GetFullPath(Path.Combine(skinRoot,SkinName));if(!folder.StartsWith(skinRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid Dock directory");
   if(Directory.Exists(folder)&&(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Dock 实例目录不能是链接。");
   string target=Path.Combine(folder,"Main.ini");if(File.Exists(target)&&!File.ReadAllText(target).StartsWith(Marker,StringComparison.Ordinal))throw new IOException("同名 Dock 目录包含已有皮肤，未覆盖。");
   string content=File.ReadAllText(source);foreach(var pair in variables)content=Set(content,pair[0],pair[1]);Directory.CreateDirectory(folder);File.WriteAllText(target,Marker+"\r\n"+content,Encoding.Unicode);generatedFile=target;
  }
  // RefreshApp posts a delayed refresh. Activating in the same multi-bang runs
  // before the new directory is registered, so activation must be a later call.
  if(instance!="primary"&&!catalogRefreshed){Bang("!RefreshApp");catalogRefreshed=true;signature=next;nextAttempt=DateTime.UtcNow.AddSeconds(1);return;}
  var command=new StringBuilder();command.Append("[!ActivateConfig \"").Append(ConfigName).Append("\" \"Main.ini\"]");foreach(var pair in variables)command.Append("[!SetVariable ").Append(pair[0]).Append(" \"").Append(pair[1]).Append("\" \"").Append(ConfigName).Append("\"]");command.Append("[!CommandMeasure Dock \"ReloadLayout()\" \"").Append(ConfigName).Append("\"]");Bang(command.ToString());enabled=true;signature=next;nextAttempt=DateTime.UtcNow.AddSeconds(2);
 }
 internal static bool SendSkinCommand(IntPtr window,string command){
  if(window==IntPtr.Zero)return false;
  var data=new CopyData{id=new IntPtr(1),bytes=(command.Length+1)*2,text=Marshal.StringToHGlobalUni(command)};
  try{IntPtr result;return SendMessageTimeout(window,0x004A,IntPtr.Zero,ref data,2,50,out result)!=IntPtr.Zero&&result!=IntPtr.Zero;}finally{Marshal.FreeHGlobal(data.text);}
 }
 public bool SetVisible(bool visible){
  // Rainmeter accepts bangs on the skin HWND; avoid process startup and waits on every hover.
  if(!enabled||disposed||DateTime.UtcNow<nextVisibilityAttempt)return false;
  bool sent=SendSkinCommand(FindWindow("RainmeterMeterWindow",Path.Combine(skinRoot,SkinName,"Main.ini")),(visible?"!Show":"!Hide")+" \""+ConfigName+"\"");
  nextVisibilityAttempt=sent?DateTime.MinValue:DateTime.UtcNow.AddMilliseconds(250);return sent;
 }
 internal static bool TryIconMessage(string value,out int sequence,out int alpha){
  sequence=alpha=0;if(value==null)return false;var parts=value.Split(':');
  return parts.Length==2&&Int32.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out sequence)&&sequence>0&&Int32.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out alpha)&&alpha>=0&&alpha<=255;
 }
 public bool SetIconOpacity(int alpha){
  if(alpha<0||alpha>255||!enabled||disposed||DateTime.UtcNow<nextIconAttempt)return false;
  bool sent=SendSkinCommand(FindWindow("RainmeterMeterWindow",Path.Combine(skinRoot,SkinName,"Main.ini")),"!CommandMeasure Dock \"SetIconOpacity("+alpha.ToString(CultureInfo.InvariantCulture)+")\" \""+ConfigName+"\"");
  nextIconAttempt=sent?DateTime.MinValue:DateTime.UtcNow.AddMilliseconds(250);return sent;
 }
 public void Dispose(){if(disposed)return;disposed=true;if(instance=="primary")return;try{Bang("!DeactivateConfig \""+ConfigName+"\"");}catch{}if(generatedFile==null)return;try{if(File.Exists(generatedFile)&&File.ReadAllText(generatedFile).StartsWith(Marker,StringComparison.Ordinal))File.Delete(generatedFile);string directory=Path.GetDirectoryName(generatedFile);if(Directory.Exists(directory)&&Directory.GetFileSystemEntries(directory).Length==0)Directory.Delete(directory);}catch{}}
}
