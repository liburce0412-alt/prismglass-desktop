using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;

// Windows owns DNS-SD service records; the separate scoped publisher supplies this host's IPv4 A record.
internal sealed class CompanionDiscovery : IDisposable {
 internal const uint Pending=9506;
 [UnmanagedFunctionPointer(CallingConvention.Winapi)] internal delegate void Completion(uint status,IntPtr context,IntPtr instance);
 [StructLayout(LayoutKind.Sequential)] internal struct RegisterRequest {public uint Version,InterfaceIndex;public IntPtr Instance,Callback,Context,Credentials;public int Unicast;}
 [StructLayout(LayoutKind.Sequential)] internal struct ServiceInstance {public IntPtr Name,Host,Ip4,Ip6;public ushort Port,Priority,Weight;public uint PropertyCount;public IntPtr Keys,Values;public uint InterfaceIndex;}
 internal interface IApi {
  IntPtr Construct(string service,string host,byte[] address,ushort port);
  uint Register(IntPtr request,IntPtr cancel);
  uint Cancel(IntPtr cancel);
  uint DeRegister(IntPtr request);
  void Free(IntPtr instance);
 }
 internal sealed class WindowsApi : IApi {
  [DllImport("dnsapi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern IntPtr DnsServiceConstructInstance(string service,string host,IntPtr ip4,IntPtr ip6,ushort port,ushort priority,ushort weight,uint properties,IntPtr keys,IntPtr values);
  [DllImport("dnsapi.dll",ExactSpelling=true)] static extern uint DnsServiceRegister(IntPtr request,IntPtr cancel);
  [DllImport("dnsapi.dll",ExactSpelling=true)] static extern uint DnsServiceRegisterCancel(IntPtr cancel);
  [DllImport("dnsapi.dll",ExactSpelling=true)] static extern uint DnsServiceDeRegister(IntPtr request,IntPtr cancel);
  [DllImport("dnsapi.dll",ExactSpelling=true)] static extern void DnsServiceFreeInstance(IntPtr instance);
  public IntPtr Construct(string service,string host,byte[] address,ushort port){IntPtr ip=Marshal.AllocHGlobal(4);try{Marshal.Copy(address,0,ip,4);return DnsServiceConstructInstance(service,host,ip,IntPtr.Zero,port,0,0,0,IntPtr.Zero,IntPtr.Zero);}finally{Marshal.FreeHGlobal(ip);}}
  public uint Register(IntPtr request,IntPtr cancel){return DnsServiceRegister(request,cancel);}
  public uint Cancel(IntPtr cancel){return DnsServiceRegisterCancel(cancel);}
  public uint DeRegister(IntPtr request){return DnsServiceDeRegister(request,IntPtr.Zero);}
  public void Free(IntPtr instance){if(instance!=IntPtr.Zero)DnsServiceFreeInstance(instance);}
 }
 sealed class Registration {
  public readonly object Serial=new object();public string Service,Host;public byte[] Address;public ushort Port;public uint Interface;
  public IntPtr Instance,RegisteredInstance,Request,DeregisterRequest,Cancel;public Completion Registered,Removed;
  public volatile bool Stop;public bool Started,Reported,CancelDone,Deregistering,Finished;
 }
 // Keep native callbacks rooted through asynchronous cancellation and deregistration, including after Dispose.
 static readonly HashSet<Registration> live=new HashSet<Registration>();
 readonly object gate=new object();readonly IApi api;readonly Func<string,int,bool> localInterface;readonly ICompanionAddressPublisher addressPublisher;
 Registration current;bool disposed;string status="stopped",error="";
 public CompanionDiscovery():this(new WindowsApi(),IsLocalInterface,new CompanionAddressDiscovery()){}
 internal CompanionDiscovery(IApi native,Func<string,int,bool> local,ICompanionAddressPublisher address){if(native==null||local==null||address==null)throw new ArgumentNullException();api=native;localInterface=local;addressPublisher=address;}
 public string Status {get{lock(gate)return status;}}
 public string Error {get{lock(gate)return error;}}
 public bool IsRegistered {get{lock(gate)return status=="registered"&&current!=null&&!current.Stop&&addressPublisher.IsReady;}}
 static bool IsLocalInterface(string address,int index){foreach(var item in NetworkInterface.GetAllNetworkInterfaces())if(item.OperationalStatus==OperationalStatus.Up&&item.Supports(NetworkInterfaceComponent.IPv4)){var properties=item.GetIPProperties();var ipv4=properties.GetIPv4Properties();if(ipv4!=null&&ipv4.Index==index)foreach(var ip in properties.UnicastAddresses)if(ip.Address.ToString()==address)return true;}return false;}
 public void Start(string address,int port,string hostName,int interfaceIndex){
  CompanionServer.Endpoint(address,port);string host=CompanionServer.NormalizeHostName(hostName);address=IPAddress.Parse(address).ToString();if(interfaceIndex<=0||!localInterface(address,interfaceIndex))throw new ArgumentException("发现服务必须使用所选地址的当前本机网卡。");
  var item=new Registration{Host=host,Service=host.Substring(0,host.Length-6)+"._https._tcp.local",Address=IPAddress.Parse(address).GetAddressBytes(),Port=(ushort)port,Interface=(uint)interfaceIndex};
  lock(gate){if(disposed)throw new ObjectDisposedException("CompanionDiscovery");if(current!=null)throw new InvalidOperationException("发现服务仍在开启或关闭，请稍后重试。");current=item;status="registering";error="";}
  ThreadPool.QueueUserWorkItem(ignored=>StartWork(item));
 }
 public void Stop(){Registration item;lock(gate){item=current;if(item==null){status=error.Length==0?"stopped":"error";return;}item.Stop=true;status="stopping";}ThreadPool.QueueUserWorkItem(ignored=>StopWork(item));}
 void State(Registration item,string value,string message){lock(gate){if(current==item&&!(value=="registered"&&item.Stop)){status=value;error=message;}}}
 static string Failure(string action,uint code){return "局域网发现"+action+"失败（"+code+"）。请在连接诊断中检查固定名称和所选网络。";}
 static IntPtr RequestPointer(RegisterRequest value){IntPtr pointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(RegisterRequest)));Marshal.StructureToPtr(value,pointer,false);return pointer;}
 void StartWork(Registration item){lock(item.Serial){
  if(item.Finished)return;if(item.Stop){Finish(item);return;}
  try{
   item.Instance=api.Construct(item.Service,item.Host,item.Address,item.Port);if(item.Instance==IntPtr.Zero)throw new InvalidOperationException();
   item.Registered=(code,context,instance)=>QueueCompletion(item,false,code,instance);item.Removed=(code,context,instance)=>QueueCompletion(item,true,code,instance);
   var request=new RegisterRequest{Version=1,InterfaceIndex=item.Interface,Instance=item.Instance,Callback=Marshal.GetFunctionPointerForDelegate(item.Registered)};
   item.Request=RequestPointer(request);request.Callback=Marshal.GetFunctionPointerForDelegate(item.Removed);item.DeregisterRequest=RequestPointer(request);item.Cancel=Marshal.AllocHGlobal(IntPtr.Size);Marshal.WriteIntPtr(item.Cancel,IntPtr.Zero);
   addressPublisher.Start(new IPAddress(item.Address).ToString(),item.Host,(int)item.Interface,message=>AddressFailed(item,message));if(item.Stop||!addressPublisher.IsReady){Finish(item);return;}
   lock(live)live.Add(item);uint result=api.Register(item.Request,item.Cancel);if(result!=Pending){State(item,"error",Failure("注册",result));Finish(item);return;}item.Started=true;
  }catch(DllNotFoundException){State(item,"error","此系统不支持 Windows 局域网发现接口。");Finish(item);}catch(EntryPointNotFoundException){State(item,"error","此系统不支持 Windows 局域网发现接口。");Finish(item);}catch{State(item,"error","局域网发现未能启动，请检查所选网络。");Finish(item);}
 }}
 void QueueCompletion(Registration item,bool removed,uint code,IntPtr instance){ThreadPool.QueueUserWorkItem(ignored=>Complete(item,removed,code,instance));}
 void AddressFailed(Registration item,string message){item.Stop=true;State(item,"error",message);ThreadPool.QueueUserWorkItem(ignored=>StopWork(item));}
 void Complete(Registration item,bool removed,uint code,IntPtr instance){lock(item.Serial){
  try{
   if(item.Finished)return;
   if(removed){item.Deregistering=false;if(code==0){Finish(item);}else State(item,"error",Failure("撤销",code));return;}
   item.Reported=true;if(code!=0){if(item.Deregistering||item.CancelDone)return;if(!item.Stop)State(item,"error",Failure("注册",code));Finish(item);return;}
   bool matches=Matches(item,instance);
   // Successful Windows callbacks can omit IP fields. Keep the original, complete registration for removal.
   // Only a conflict needs the returned identity to withdraw the unexpected advertisement.
   if(!matches&&instance!=IntPtr.Zero&&!item.Deregistering){item.RegisteredInstance=instance;instance=IntPtr.Zero;var request=(RegisterRequest)Marshal.PtrToStructure(item.DeregisterRequest,typeof(RegisterRequest));request.Instance=item.RegisteredInstance;Marshal.StructureToPtr(request,item.DeregisterRequest,false);}
   if(!matches){item.Stop=true;State(item,"error","局域网发现返回的名称或地址不符，正在停止发布。请检查名称冲突。");}
   if(item.Stop)Deregister(item);else State(item,"registered","");
  }catch{item.Stop=true;State(item,"error","局域网发现响应无效，正在撤销发布。");Deregister(item);}finally{if(instance!=IntPtr.Zero)api.Free(instance);}
 }}
 bool Matches(Registration item,IntPtr pointer){
  if(pointer==IntPtr.Zero)return false;var value=(ServiceInstance)Marshal.PtrToStructure(pointer,typeof(ServiceInstance));
  if(value.Ip6!=IntPtr.Zero||value.Port!=item.Port||(value.InterfaceIndex!=0&&value.InterfaceIndex!=item.Interface))return false;
  string service=Marshal.PtrToStringUni(value.Name),host=Marshal.PtrToStringUni(value.Host);if(!String.Equals((service??"").TrimEnd('.'),item.Service,StringComparison.OrdinalIgnoreCase)||!String.Equals((host??"").TrimEnd('.'),item.Host,StringComparison.OrdinalIgnoreCase))return false;
  // Observed Windows DNS-SD completion: exact name/port, but null IPv4 and interface index zero.
  // Missing callback data is accepted only against our retained explicit IPv4 and a still-matching local interface.
  var original=(ServiceInstance)Marshal.PtrToStructure(item.Instance,typeof(ServiceInstance));if(!SameAddress(original.Ip4,item.Address)||value.Ip4!=IntPtr.Zero&&!SameAddress(value.Ip4,item.Address))return false;
  return localInterface(new IPAddress(item.Address).ToString(),(int)item.Interface);
 }
 static bool SameAddress(IntPtr pointer,byte[] expected){if(pointer==IntPtr.Zero)return false;byte[] bytes=new byte[4];Marshal.Copy(pointer,bytes,0,4);for(int i=0;i<4;i++)if(bytes[i]!=expected[i])return false;return true;}
 void StopWork(Registration item){lock(item.Serial){if(item.Finished)return;if(!item.Started){Finish(item);return;}Deregister(item);}}
 void Deregister(Registration item){
  if(item.Finished||item.Deregistering)return;
  addressPublisher.Stop();
  try{
   // Cancel may not issue a completion callback. It is synchronous and runs off the UI thread.
   // A distinct deregistration callback provides the final lifetime boundary.
   if(!item.Reported&&!item.CancelDone){uint cancelled=api.Cancel(item.Cancel);item.CancelDone=true;if(cancelled!=0&&cancelled!=1223)State(item,"error",Failure("取消",cancelled));}
   item.Deregistering=true;uint result=api.DeRegister(item.DeregisterRequest);if(result!=Pending){item.Deregistering=false;State(item,"error",Failure("撤销",result));}
  }catch{item.Deregistering=false;State(item,"error","局域网发现撤销未确认，请重试关闭伴随屏或退出程序。");}
  // An unconfirmed failure retains one operation and its roots; never stack another advertisement.
 }
 void Finish(Registration item){
  if(item.Finished)return;item.Finished=true;addressPublisher.Stop();if(disposed)addressPublisher.Dispose();
  if(item.RegisteredInstance!=IntPtr.Zero&&item.RegisteredInstance!=item.Instance)api.Free(item.RegisteredInstance);if(item.Instance!=IntPtr.Zero)api.Free(item.Instance);
  foreach(IntPtr pointer in new[]{item.Request,item.DeregisterRequest,item.Cancel})if(pointer!=IntPtr.Zero)Marshal.FreeHGlobal(pointer);
  item.Instance=item.RegisteredInstance=item.Request=item.DeregisterRequest=item.Cancel=IntPtr.Zero;lock(live)live.Remove(item);
  lock(gate){if(current==item){current=null;status=error.Length==0?"stopped":"error";}}
 }
 public void Dispose(){lock(gate)disposed=true;Stop();lock(gate)if(current==null)addressPublisher.Dispose();}
}
