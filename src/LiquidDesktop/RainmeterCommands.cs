using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class RainmeterCommands {
 [StructLayout(LayoutKind.Sequential)]struct CopyData {public IntPtr id;public int bytes;public IntPtr text;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string title);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,uint processId);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int size);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
 [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr sender,IntPtr data,uint flags,uint timeout,out IntPtr result);
 [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr sender,ref CopyData data,uint flags,uint timeout,out IntPtr result);
 static readonly object gate=new object();static DateTime retryAt;static string failedExecutable;
 public static IntPtr Find(string executable){
  string expected=Path.GetFullPath(executable);IntPtr window=IntPtr.Zero;
  while((window=FindWindowEx(IntPtr.Zero,window,"DummyRainWClass","Rainmeter control window"))!=IntPtr.Zero){
   uint id;GetWindowThreadProcessId(window,out id);IntPtr process=OpenProcess(0x1000,false,id);if(process==IntPtr.Zero)continue;
   try{var path=new StringBuilder(32768);int length=path.Capacity;if(QueryFullProcessImageName(process,0,path,ref length)&&String.Equals(expected,path.ToString(),StringComparison.OrdinalIgnoreCase))return window;}finally{CloseHandle(process);}
  }return IntPtr.Zero;
 }
 public static bool Probe(string executable,uint timeoutMs){IntPtr window=Find(executable),result;return window!=IntPtr.Zero&&SendMessageTimeout(window,0,IntPtr.Zero,IntPtr.Zero,2,timeoutMs,out result)!=IntPtr.Zero;}
 public static bool SkinOpen(string skinFile){return FindWindowEx(IntPtr.Zero,IntPtr.Zero,"RainmeterMeterWindow",Path.GetFullPath(skinFile))!=IntPtr.Zero;}
 static void Deliver(IntPtr window,string command,uint timeoutMs){
  var data=new CopyData{id=new IntPtr(1),bytes=checked((command.Length+1)*2),text=Marshal.StringToHGlobalUni(command)};
  try{
   IntPtr result;
   // Rainmeter queues the bang and returns zero; only SendMessageTimeout reports delivery.
   if(SendMessageTimeout(window,0x004A,IntPtr.Zero,ref data,2,timeoutMs,out result)==IntPtr.Zero){int error=Marshal.GetLastWin32Error();if(error==5)throw new Win32Exception(error,"Rainmeter 与 PrismGlass 权限不同，控制命令未发送。");throw new TimeoutException("Rainmeter 未响应控制命令，已暂停重试。");}
  }finally{Marshal.FreeHGlobal(data.text);}
 }
 // Called only after PrismGlass skin windows have closed. Hidden foreign skins
 // still own the host; never quit it merely because no skin is currently visible.
 public static string[] QuitIfIdle(string executable,uint timeoutMs,uint exitTimeoutMs){
  if(timeoutMs==0||timeoutMs>5000)throw new ArgumentOutOfRangeException("timeoutMs");
  if(exitTimeoutMs==0||exitTimeoutMs>10000)throw new ArgumentOutOfRangeException("exitTimeoutMs");
  string expected=Path.GetFullPath(executable);
  if(!Monitor.TryEnter(gate))throw new InvalidOperationException("Rainmeter 正在处理上一条控制命令，请稍后重试。");
  try{
   if(String.Equals(failedExecutable,expected,StringComparison.OrdinalIgnoreCase)&&DateTime.UtcNow<retryAt)throw new InvalidOperationException("Rainmeter 控制暂未恢复，已暂停重试。");
   try{
    IntPtr window=Find(expected);if(window==IntPtr.Zero)throw new InvalidOperationException("Rainmeter 控制窗口未就绪，无法确认正常退出。");
    uint id;GetWindowThreadProcessId(window,out id);IntPtr process=OpenProcess(0x101000,false,id);
    if(process==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法确认 Rainmeter 进程退出，请检查进程权限。");
    try{
     var path=new StringBuilder(32768);int length=path.Capacity;
     if(!QueryFullProcessImageName(process,0,path,ref length))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法确认 Rainmeter 进程所属的安装位置。");
     if(!String.Equals(expected,path.ToString(),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Rainmeter 控制进程已改变，已停止退出操作。");
     var remaining=new List<string>();IntPtr skin=IntPtr.Zero;
     while((skin=FindWindowEx(IntPtr.Zero,skin,"RainmeterMeterWindow",null))!=IntPtr.Zero){
      uint owner;GetWindowThreadProcessId(skin,out owner);if(owner!=id)continue;
      var name=new StringBuilder(32768);GetWindowText(skin,name,name.Capacity);remaining.Add(name.Length==0?"未命名的 Rainmeter 皮肤":name.ToString());
     }
     if(remaining.Count>0)return remaining.ToArray();
     uint current;GetWindowThreadProcessId(window,out current);if(current!=id||Find(expected)!=window)throw new InvalidOperationException("Rainmeter 控制窗口已改变，已停止退出操作。");
     Deliver(window,"!Quit",timeoutMs);
     uint result=WaitForSingleObject(process,exitTimeoutMs);
     if(result==258)throw new TimeoutException("Rainmeter 已收到退出请求，但进程未在规定时间内退出；已停止重试，未强制终止进程。");
     if(result!=0)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法确认 Rainmeter 进程已经退出。");
     failedExecutable=null;retryAt=DateTime.MinValue;return new string[0];
    }finally{CloseHandle(process);}
   }catch{failedExecutable=expected;retryAt=DateTime.UtcNow.AddSeconds(2);throw;}
  }finally{Monitor.Exit(gate);}
 }
 public static void Send(string executable,string command,uint timeoutMs){
  if(String.IsNullOrEmpty(command)||command.IndexOf('\0')>=0||(command[0]!='!'&&command[0]!='['))throw new ArgumentException("Invalid Rainmeter command");
  if(timeoutMs==0||timeoutMs>5000)throw new ArgumentOutOfRangeException("timeoutMs");
  string expected=Path.GetFullPath(executable);
  if(!Monitor.TryEnter(gate))throw new InvalidOperationException("Rainmeter 正在处理上一条控制命令，请稍后重试。");
  try{
   if(String.Equals(failedExecutable,expected,StringComparison.OrdinalIgnoreCase)&&DateTime.UtcNow<retryAt)throw new InvalidOperationException("Rainmeter 控制暂未恢复，已暂停重试。");
   try{
    IntPtr window=Find(expected);if(window==IntPtr.Zero)throw new InvalidOperationException("Rainmeter 控制窗口未就绪，请重新启动 PrismGlass。");
    Deliver(window,command,timeoutMs);
    failedExecutable=null;retryAt=DateTime.MinValue;
   }catch{failedExecutable=expected;retryAt=DateTime.UtcNow.AddSeconds(2);throw;}
  }finally{Monitor.Exit(gate);}
 }
}

internal sealed class RainmeterDispatch {
 internal sealed class Pending : Exception {}
 sealed class Request {internal string key;internal Func<bool> send;internal bool done,result;internal Exception error;}
 readonly Dictionary<string,Request> requests=new Dictionary<string,Request>();
 readonly Queue<string> waiting=new Queue<string>();bool running;
 internal void Reset(){lock(requests){requests.Clear();waiting.Clear();}}
 internal void Cancel(string slot){lock(requests)requests.Remove(slot);}
 internal bool Send(string slot,string key,Func<bool> send){bool result;if(TrySend(slot,key,send,out result))return result;throw new Pending();}
 internal bool TrySend(string slot,string key,Func<bool> send,out bool result){
  result=false;
  lock(requests){
   Request request;
   if(requests.TryGetValue(slot,out request)&&request.key==key){
    if(!request.done)return false;
    requests.Remove(slot);if(request.error!=null)throw request.error;result=request.result;return true;
   }
   requests[slot]=new Request{key=key,send=send};
   if(!waiting.Contains(slot))waiting.Enqueue(slot);
   if(!running){running=true;ThreadPool.QueueUserWorkItem(_=>Drain());}
   return false;
  }
 }
 void Drain(){
  while(true){
   Request request;lock(requests){if(waiting.Count==0){running=false;return;}if(!requests.TryGetValue(waiting.Dequeue(),out request))continue;}
   bool result=false;Exception error=null;try{result=request.send();}catch(Exception ex){error=ex;}
   lock(requests){request.result=result;request.error=error;request.done=true;}
  }
 }
}