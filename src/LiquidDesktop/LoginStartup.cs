using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

internal sealed class LoginStartupState { public bool enabled,registered,systemDisabled;public string error=""; }
internal static class LoginStartup {
 const string Name="PrismGlass.lnk";
 static string Folder {get{return Environment.GetFolderPath(Environment.SpecialFolder.Startup);}}
 static string Shell {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");}}
 internal static string Arguments(string root){return "-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File \""+Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)),"DesktopMode.ps1")+"\" -Mode Beauty";}
 public static LoginStartupState Read(string root){try{bool disabled=false;using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder")){disabled=Disabled(key==null?null:key.GetValue(Name) as byte[]);}return ReadFrom(root,Folder,disabled);}catch(Exception ex){return new LoginStartupState{error="无法读取开机自启状态："+ex.Message};}}
 internal static bool Disabled(byte[] value){return value!=null&&value.Length>=4&&(value[0]==3||value[0]==7);}
 internal static LoginStartupState ReadFrom(string root,string folder,bool disabled){
  string file=Path.Combine(folder,Name);if(!File.Exists(file))return new LoginStartupState();
  if(!Owned(root,file))return new LoginStartupState{error="启动目录中已有其他安装的 PrismGlass 项，请先处理原安装。"};
  return new LoginStartupState{registered=true,enabled=!disabled,systemDisabled=disabled};
 }
 public static LoginStartupState Set(string root,bool enabled){var state=Read(root);if(state.error.Length>0)throw new IOException(state.error);SetIn(root,Folder,enabled);state=Read(root);if(state.error.Length>0)throw new IOException(state.error);if(enabled&&state.systemDisabled)throw new InvalidOperationException("Windows 已禁用此启动项，请在 Windows 设置 → 应用 → 启动中开启 PrismGlass。");return state;}
 internal static void SetIn(string root,string folder,bool enabled){
  string file=Path.Combine(folder,Name);if(File.Exists(file)&&!Owned(root,file))throw new IOException("已有其他安装的启动项，未覆盖或删除。");
  if(!enabled){if(File.Exists(file))File.Delete(file);return;}
  if(!File.Exists(Shell)||!File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)),"DesktopMode.ps1"))||!File.Exists(Path.Combine(root,"LiquidDesktop.exe")))throw new FileNotFoundException("启动文件不完整，请修复或重新安装 PrismGlass。");
  Directory.CreateDirectory(folder);string temp=Path.Combine(folder,".PrismGlass-"+Guid.NewGuid().ToString("N")+".tmp");object raw=null;
  try{raw=new ShellLink();var link=(IShellLinkW)raw;link.SetPath(Shell);link.SetArguments(Arguments(root));link.SetWorkingDirectory(Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)));link.SetDescription("PrismGlass 登录启动");link.SetIconLocation(Path.Combine(root,"LiquidDesktop.exe"),0);link.SetShowCmd(7);((IPersistFile)raw).Save(temp,true);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);if(!Owned(root,file))throw new IOException("启动项写入后校验失败，请重试。");}
  finally{if(raw!=null)Marshal.FinalReleaseComObject(raw);if(File.Exists(temp))File.Delete(temp);}
 }
 static bool Owned(string root,string file){
  if(new FileInfo(file).Length>65536)throw new IOException("启动快捷方式无效，未修改原文件。");object raw=null;
  try{raw=new ShellLink();((IPersistFile)raw).Load(file,0);var link=(IShellLinkW)raw;var target=new StringBuilder(32768);var args=new StringBuilder(32768);link.GetPath(target,target.Capacity,IntPtr.Zero,4);link.GetArguments(args,args.Capacity);return String.Equals(target.ToString(),Shell,StringComparison.OrdinalIgnoreCase)&&String.Equals(args.ToString(),Arguments(root),StringComparison.OrdinalIgnoreCase);}
  finally{if(raw!=null)Marshal.FinalReleaseComObject(raw);}
 }
 [ComImport,Guid("00021401-0000-0000-C000-000000000046")]class ShellLink{}
 [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface IShellLinkW{
  void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder path,int count,IntPtr data,uint flags);
  void GetIDList(out IntPtr value);void SetIDList(IntPtr value);
  void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int count);void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string value);
  void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int count);void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string value);
  void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int count);void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string value);
  void GetHotkey(out short value);void SetHotkey(short value);void GetShowCmd(out int value);void SetShowCmd(int value);
  void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int count,out int index);void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string value,int index);
  void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string value,uint reserved);void Resolve(IntPtr hwnd,uint flags);void SetPath([MarshalAs(UnmanagedType.LPWStr)]string value);
 }
}
