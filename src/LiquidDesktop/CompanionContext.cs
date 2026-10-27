using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

internal sealed class CompanionContextChangedException : Exception {}
internal sealed class CompanionContext {
 internal sealed class Foreground {public IntPtr window;public int process;public long started;public string name;}
 internal sealed class Command {public string id,label;public ushort[] keys;public Command(string value,string text,params ushort[] chord){id=value;label=text;keys=chord;}}
 static readonly Command[] browser={new Command("back","后退",0x12,0x25),new Command("forward","前进",0x12,0x27),new Command("reload","刷新",0x11,0x52),new Command("new-tab","新标签",0x11,0x54),new Command("previous-tab","上一标签",0x11,0x10,0x09),new Command("next-tab","下一标签",0x11,0x09)};
 static readonly Command[] editor={new Command("palette","命令面板",0x11,0x10,0x50),new Command("quick-open","打开文件",0x11,0x50),new Command("find","查找",0x11,0x46),new Command("toggle-sidebar","侧边栏",0x11,0x42)};
 static readonly Command[] empty=new Command[0];
 static readonly Command[] desktop={new Command("next-window","切换窗口",0x12,0x09),new Command("task-view","任务视图",0x5B,0x09),new Command("show-desktop","显示桌面",0x5B,0x44),new Command("maximize","最大化窗口",0x5B,0x26),new Command("restore","还原 / 最小化",0x5B,0x28),new Command("previous-desktop","上一桌面",0x5B,0x11,0x25),new Command("next-desktop","下一桌面",0x5B,0x11,0x27)};
 readonly Func<Foreground> read;readonly Func<Foreground,ushort[],bool> send;Foreground previous;string id="";
 public CompanionContext():this(Read,Send){}
 internal CompanionContext(Func<Foreground> source,Func<Foreground,ushort[],bool> dispatch){read=source;send=dispatch;}
 static bool Same(Foreground a,Foreground b){return a!=null&&b!=null&&a.window==b.window&&a.process==b.process&&a.started==b.started&&a.name==b.name;}
 static string Scene(Foreground value){switch(value==null?"":value.name){case "chrome":case "msedge":return "browser";case "code":return "editor";case "cloudmusic":case "qqmusic":case "spotify":return "music";default:return "general";}}
 static string Name(Foreground value){switch(value==null?"":value.name){case "chrome":return "Chrome";case "msedge":return "Edge";case "code":return "VS Code";case "cloudmusic":return "网易云音乐";case "qqmusic":return "QQ 音乐";case "spotify":return "Spotify";default:return "电脑";}}
 static Command[] Commands(Foreground value){string scene=Scene(value);return scene=="browser"?browser:scene=="editor"?editor:empty;}
 public object Snapshot(){var current=read();if(!Same(previous,current)){previous=current;id=current==null?"":Guid.NewGuid().ToString("N");}return new{id=id,scene=Scene(current),appName=Name(current),available=current!=null,actions=Commands(current).Select(c=>new{c.id,c.label}).ToArray()};}
 public object Execute(string contextId,string command){var current=read();if(String.IsNullOrEmpty(contextId)||contextId!=id||!Same(previous,current))throw new CompanionContextChangedException();var action=Commands(current).FirstOrDefault(c=>c.id==command);if(action==null)throw new ArgumentException();if(!send(current,action.keys))throw new InvalidOperationException();return new{message="已发送到 "+Name(current)+"。"};}
 public object ExecuteGlobal(string contextId,string command){var current=read();if(String.IsNullOrEmpty(contextId)||contextId!=id||!Same(previous,current))throw new CompanionContextChangedException();var action=desktop.FirstOrDefault(c=>c.id==command);if(action==null)throw new ArgumentException();if(!send(current,action.keys))throw new InvalidOperationException();return new{message="已发送："+action.label+"。"};}
 static Foreground Read(){try{var window=GetForegroundWindow();uint pid;if(window==IntPtr.Zero||GetWindowThreadProcessId(window,out pid)==0)return null;using(var process=Process.GetProcessById((int)pid))return new Foreground{window=window,process=(int)pid,started=process.StartTime.ToUniversalTime().Ticks,name=process.ProcessName.ToLowerInvariant()};}catch{return null;}}
 // shortcut: Windows cannot atomically bind SendInput to a foreground window; use a per-app API if stronger targeting becomes available.
 static bool Send(Foreground target,ushort[] keys){if(new[]{0x10,0x11,0x12,0x5B,0x5C}.Any(k=>(GetAsyncKeyState(k)&0x8000)!=0))return false;var inputs=BuildInputs(keys,false);if(!Same(target,Read()))return false;uint sent=SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(Input)));if(sent==inputs.Length)return true;if(sent>0){var releases=BuildInputs(keys,true);SendInput((uint)releases.Length,releases,Marshal.SizeOf(typeof(Input)));}return false;}
 static Input[] BuildInputs(ushort[] keys,bool releaseOnly){if(releaseOnly)return keys.Reverse().Select(k=>Key(k,true)).ToArray();var inputs=new Input[keys.Length*2];for(int i=0;i<keys.Length;i++){inputs[i]=Key(keys[i],false);inputs[inputs.Length-1-i]=Key(keys[i],true);}return inputs;}
 static Input Key(ushort key,bool up){return new Input{type=1,data=new InputUnion{keyboard=new Keyboard{key=key,flags=(up?2u:0u)|(key>=0x25&&key<=0x28?1u:0u)}}};}
 [StructLayout(LayoutKind.Sequential)]struct Input{public uint type;public InputUnion data;}
 [StructLayout(LayoutKind.Explicit)]struct InputUnion{[FieldOffset(0)]public Keyboard keyboard;[FieldOffset(0)]public Mouse mouse;}
 [StructLayout(LayoutKind.Sequential)]struct Keyboard{public ushort key,scan;public uint flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Sequential)]struct Mouse{public int x,y;public uint data,flags,time;public UIntPtr extra;}
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll",SetLastError=true)]static extern uint SendInput(uint count,Input[] inputs,int size);
}
