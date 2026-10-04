using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
internal sealed class WidgetBackend {
 readonly string root;readonly JavaScriptSerializer json=new JavaScriptSerializer();
 public WidgetBackend(string path){root=path;}
 object Snapshot(string name){try{var file=Path.Combine(root,"widget-"+name+".json");if((DateTime.UtcNow-File.GetLastWriteTimeUtc(file)).TotalSeconds>8)return null;return json.DeserializeObject(File.ReadAllText(file));}catch{return null;}}
 public object Read(){
  long total=1500,remaining=1500,deadline=0;bool focusReady=false;
  try{var fields=File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Rainmeter","Skins","PrismGlass","@Resources","focus-state.txt")).Trim().Split(' ');total=long.Parse(fields[0]);remaining=long.Parse(fields[1]);deadline=long.Parse(fields[2]);focusReady=true;}catch{}
  long completed=0,lastCompleted=0;try{var record=File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Rainmeter","Skins","PrismGlass","@Resources","focus-completed.txt")).Split(' ');completed=long.Parse(record[0]);lastCompleted=long.Parse(record[1]);}catch{}
  string title="等待播放",artist="",cover="";
  try{var lines=File.ReadAllLines(Path.Combine(root,"..","music-data","now.txt"));if(lines.Length>0)title=lines[0];if(lines.Length>1)artist=lines[1];if(lines.Length>2&&File.Exists(lines[2])){string media=Path.GetFullPath(Path.Combine(root,"..","music-data"))+Path.DirectorySeparatorChar;string full=Path.GetFullPath(lines[2]);if(full.StartsWith(media,StringComparison.OrdinalIgnoreCase))cover="https://prism-media.local/"+Uri.EscapeDataString(Path.GetFileName(full))+"?v="+File.GetLastWriteTimeUtc(full).Ticks;}}catch{}
  bool? playing=null;try{var status=Path.Combine(root,"..","music-data","playback.txt");if((DateTime.UtcNow-File.GetLastWriteTimeUtc(status)).TotalSeconds<12){var state=File.ReadAllText(status).Trim();if(state=="playing"||state=="paused")playing=state=="playing";}}catch{}
  return new{type="widgets",weather=Snapshot("Weather"),system=Snapshot("System"),volume=Snapshot("Music"),music=new{title=title,artist=artist,cover=cover,playing=playing},focus=new{available=focusReady,total=total,remaining=remaining,deadline=deadline,completed=completed,lastCompleted=lastCompleted}};
 }
 [DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 static void Brightness(int delta){using(var query=new ManagementObjectSearcher("root\\WMI","SELECT * FROM WmiMonitorBrightness")){foreach(ManagementObject item in query.Get()){int level=Math.Max(0,Math.Min(100,Convert.ToInt32(item["CurrentBrightness"])+delta));using(var methods=new ManagementObjectSearcher("root\\WMI","SELECT * FROM WmiMonitorBrightnessMethods")){foreach(ManagementObject method in methods.Get())method.InvokeMethod("WmiSetBrightness",new object[]{(uint)1,(byte)level});}return;}}throw new InvalidOperationException("此显示器不支持系统亮度控制，请使用显示器按键。");}
 static void Launch(string file,string args){Process.Start(new ProcessStartInfo(file,args){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Normal});}
 static void Bang(string args){Process.Start(new ProcessStartInfo(RuntimePaths.Rainmeter,args){UseShellExecute=false,CreateNoWindow=true});}
 public void Action(string id){
  switch(id){
   case "theme-sky":File.WriteAllText(Path.Combine(root,"theme.txt"),"Sky");break;
   case "theme-astro":File.WriteAllText(Path.Combine(root,"theme.txt"),"Astro");break;
   case "mute":keybd_event(0xAD,0,0,UIntPtr.Zero);keybd_event(0xAD,0,2,UIntPtr.Zero);break;
   case "brightness-up":Brightness(10);break;
   case "brightness-down":Brightness(-10);break;
   case "display":Launch("ms-settings:display","");break;
   case "focus-break":Bang("!CommandMeasure Timer \"Preset(5);Toggle()\" \"PrismGlass\\Focus\"");break;
   case "focus-again":Bang("!CommandMeasure Timer \"Preset(25);Toggle()\" \"PrismGlass\\Focus\"");break;
   case "focus-toggle":Bang("!CommandMeasure Timer \"Toggle()\" \"PrismGlass\\Focus\"");break;
   case "focus-reset":Bang("!CommandMeasure Timer \"Reset()\" \"PrismGlass\\Focus\"");break;
   case "focus-25":Bang("!CommandMeasure Timer \"Preset(25)\" \"PrismGlass\\Focus\"");break;
   case "focus-5":Bang("!CommandMeasure Timer \"Preset(5)\" \"PrismGlass\\Focus\"");break;
   case "volume-up":Bang("!CommandMeasure Volume \"ChangeVolume 5\" \"PrismGlass\\Music\"");break;
   case "volume-down":Bang("!CommandMeasure Volume \"ChangeVolume -5\" \"PrismGlass\\Music\"");break;
   case "sound":Launch("ms-settings:sound","");break;
   case "music":Launch("mswindowsmusic:","");break;
   case "weather":Launch("https://www.msn.com/weather","");break;
   case "task-manager":Launch("taskmgr.exe","");break;
   case "desktop":Launch("explorer.exe","\""+Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)+"\"");break;
   case "documents":Launch("explorer.exe","\""+Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)+"\"");break;
   case "downloads":Launch("explorer.exe","\""+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")+"\"");break;
   case "apps":Launch("explorer.exe","shell:AppsFolder");break;
   case "capture":Launch("ms-screenclip:","");break;
   case "recycle":Launch("explorer.exe","shell:RecycleBinFolder");break;
   case "notes":Launch("notepad.exe","\""+Path.GetFullPath(Path.Combine(root,"..","随手记.txt"))+"\"");break;
   case "clipboard":Process.Start(new ProcessStartInfo("pwsh.exe","-NoProfile -WindowStyle Hidden -File \""+Path.GetFullPath(Path.Combine(root,"..","OpenClipboardHistory.ps1"))+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});break;
   default:throw new ArgumentException("Unknown widget action: "+id);
  }
 }
}
