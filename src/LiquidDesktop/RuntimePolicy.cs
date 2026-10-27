using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class RuntimeSignals {public bool locked,displayOff,remote,battery,powerSaver;}
internal sealed class RuntimePolicy : IDisposable {
 public readonly RuntimeSignals Signals=new RuntimeSignals();public event Action Changed;IntPtr powerNotification;bool disposed;
 static readonly Guid DisplayState=new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
 [StructLayout(LayoutKind.Sequential)]struct POWER_STATUS {public byte ac,battery,percent,saver;public uint lifetime,fullLifetime;}
 [DllImport("kernel32.dll")]static extern bool GetSystemPowerStatus(out POWER_STATUS status);
 [DllImport("user32.dll")]static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient,ref Guid setting,uint flags);
 [DllImport("user32.dll")]static extern bool UnregisterPowerSettingNotification(IntPtr handle);
 [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
 [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr desktop);
 public RuntimePolicy(IntPtr window){var setting=DisplayState;powerNotification=RegisterPowerSettingNotification(window,ref setting,0);Signals.remote=SystemInformation.TerminalServerSession;var desktop=OpenInputDesktop(0,false,0x0100);Signals.locked=desktop==IntPtr.Zero;if(desktop!=IntPtr.Zero)CloseDesktop(desktop);ReadPower();SystemEvents.SessionSwitch+=Session;SystemEvents.PowerModeChanged+=Power;}
 void Notify(){var handler=Changed;if(!disposed&&handler!=null)handler();}
 void Session(object sender,SessionSwitchEventArgs args){switch(args.Reason){case SessionSwitchReason.SessionLock:Signals.locked=true;break;case SessionSwitchReason.SessionUnlock:Signals.locked=false;break;case SessionSwitchReason.RemoteConnect:Signals.remote=true;break;case SessionSwitchReason.RemoteDisconnect:Signals.remote=false;break;case SessionSwitchReason.ConsoleConnect:Signals.remote=false;break;}Notify();}
 void Power(object sender,PowerModeChangedEventArgs args){if(args.Mode==PowerModes.Suspend)Signals.displayOff=true;if(args.Mode==PowerModes.Resume)Signals.displayOff=false;ReadPower();Notify();}
 public void ReadPower(){POWER_STATUS status;if(GetSystemPowerStatus(out status)){Signals.battery=status.ac==0;Signals.powerSaver=status.saver==1;}}
 public void WindowMessage(ref Message message){if(message.Msg==0x218&&message.WParam.ToInt64()==0x8013&&message.LParam!=IntPtr.Zero){var id=(Guid)Marshal.PtrToStructure(message.LParam,typeof(Guid));if(id==DisplayState&&Marshal.ReadInt32(message.LParam,16)>=4){Signals.displayOff=Marshal.ReadInt32(message.LParam,20)==0;ReadPower();Notify();}}}
 public static string[] Reasons(DesktopPreferenceState settings,RuntimeSignals signals,bool covered,bool hidden,bool systemAnimations){var result=new List<string>();if(settings.manualPaused)result.Add("手动暂停");if(signals.locked)result.Add("屏幕锁定");if(signals.displayOff)result.Add("屏幕关闭");if(signals.remote&&settings.pauseOnRemote)result.Add("远程会话");if(signals.battery&&settings.pauseOnBattery)result.Add("电池供电");if(signals.powerSaver&&settings.pauseOnPowerSaver)result.Add("系统节能");if(settings.reducedMotion||!systemAnimations)result.Add("减少动画");if(covered)result.Add("屏幕被覆盖");if(hidden)result.Add("组件不可见");return result.ToArray();}
 public void Dispose(){if(disposed)return;disposed=true;SystemEvents.SessionSwitch-=Session;SystemEvents.PowerModeChanged-=Power;if(powerNotification!=IntPtr.Zero){UnregisterPowerSettingNotification(powerNotification);powerNotification=IntPtr.Zero;}}
}

internal sealed class HotkeySpec {public uint modifiers,key;public string name;}
internal sealed class HotkeyBinding : IDisposable {
 readonly IntPtr window;int activeId,pendingId;string active="",pending="";
 [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
 [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr window,int id);
 public static HotkeySpec Parse(string text){
  if(String.IsNullOrWhiteSpace(text))return new HotkeySpec{name=""};if(text.Length>80)throw new ArgumentException("快捷键过长。");uint modifiers=0,key=0;var pieces=text.Split('+');foreach(string part in pieces){string token=part.Trim();uint bit=token.Equals("Ctrl",StringComparison.OrdinalIgnoreCase)||token.Equals("Control",StringComparison.OrdinalIgnoreCase)?2u:token.Equals("Alt",StringComparison.OrdinalIgnoreCase)?1u:token.Equals("Shift",StringComparison.OrdinalIgnoreCase)?4u:token.Equals("Win",StringComparison.OrdinalIgnoreCase)?8u:0u;if(bit!=0){if((modifiers&bit)!=0)throw new ArgumentException("快捷键修饰键重复。");modifiers|=bit;}else{Keys parsed;if(key!=0||!Enum.TryParse<Keys>(token,true,out parsed)||(int)parsed<8||(int)parsed>254||parsed==Keys.ControlKey||parsed==Keys.ShiftKey||parsed==Keys.Menu)throw new ArgumentException("快捷键必须包含一个有效按键。");key=(uint)parsed;}}if(key==0||modifiers==0)throw new ArgumentException("请使用 Ctrl、Alt、Shift 或 Win 组合快捷键。");return new HotkeySpec{key=key,modifiers=modifiers,name=text.Trim()};
 }
 public HotkeyBinding(IntPtr handle){window=handle;}
 public Action Prepare(string value){var spec=Parse(value);if(pendingId!=0){UnregisterHotKey(window,pendingId);pendingId=0;}pending=value??"";if(pending==active)return ()=>{};if(spec.key!=0){pendingId=activeId==0x5A11?0x5A12:0x5A11;if(!RegisterHotKey(window,pendingId,spec.modifiers|0x4000,spec.key)){pendingId=0;pending=active;throw new IOException("快捷键已被其他程序占用，原有快捷键仍然有效。");}}return ()=>{if(pendingId!=0)UnregisterHotKey(window,pendingId);pendingId=0;pending=active;};}
 public void Configure(string value){if(pending!=(value??""))Prepare(value);Commit();}
 public void Commit(){if(pending==active)return;if(activeId!=0)UnregisterHotKey(window,activeId);activeId=pendingId;active=pending;pendingId=0;}
 public bool Matches(Message message){return message.Msg==0x312&&activeId!=0&&message.WParam.ToInt32()==activeId;}
 public void Dispose(){if(activeId!=0)UnregisterHotKey(window,activeId);if(pendingId!=0)UnregisterHotKey(window,pendingId);activeId=pendingId=0;}
}

