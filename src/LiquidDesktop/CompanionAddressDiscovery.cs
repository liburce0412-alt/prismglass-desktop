using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal interface ICompanionAddressPublisher : IDisposable {
 bool IsReady {get;}
 void Start(string address,string hostName,int interfaceIndex,Action<string> failed);
 void Stop();
}

// A single IPv4 name, not a DNS server: bounded packets, selected interface, on-subnet peers only.
internal sealed class CompanionAddressDiscovery : ICompanionAddressPublisher {
 internal static readonly IPAddress Group=IPAddress.Parse("224.0.0.251");
 internal sealed class Question {public string Name;public byte[] WireName;public ushort Type,Class;}
 internal sealed class Packet {public ushort Id;public bool Query,Match,Multicast,Truncated,Known,Conflict;public readonly List<Question> Questions=new List<Question>();}
 sealed class Pending {public IPEndPoint Target;public byte[] Bytes;public long Due;public bool Multicast;}
 sealed class Run {
  public Socket Socket;public IPAddress Address,Mask;public string Host;public int Index,ThreadId;public bool Loopback;
  public IPEndPoint Multicast;public Func<bool> StillLocal;public Action<string> Failed;public volatile bool Stop,Ready;
  public readonly object SendGate=new object();public readonly TaskCompletionSource<bool> Started=new TaskCompletionSource<bool>(),Done=new TaskCompletionSource<bool>();public readonly Stopwatch Clock=Stopwatch.StartNew();public readonly List<Pending> Pending=new List<Pending>();
  public string Error="";public int Probes,Announcements,ReadCount,SendCount;public long NextProbe,NextAnnouncement,ReadWindow,SendWindow,LastMulticast=-1000;public readonly Random Random=new Random();
 }
 readonly object gate=new object();Run current;bool disposed;
 public bool IsReady {get{lock(gate)return current!=null&&current.Ready&&!current.Stop;}}
 internal int BoundPort {get{lock(gate)return current==null?0:((IPEndPoint)current.Socket.LocalEndPoint).Port;}}
 internal static IPAddress SelectedMask(string address,int index){
  if(index<=0)return null;foreach(var nic in NetworkInterface.GetAllNetworkInterfaces())if(nic.OperationalStatus==OperationalStatus.Up&&nic.Supports(NetworkInterfaceComponent.IPv4)){var properties=nic.GetIPProperties();if(properties.GetIPv4Properties().Index==index)foreach(var entry in properties.UnicastAddresses)if(entry.Address.ToString()==address)return entry.IPv4Mask;}return null;
 }
 public void Start(string address,string hostName,int interfaceIndex,Action<string> failed){
  CompanionServer.Endpoint(address,9443);string host=CompanionServer.NormalizeHostName(hostName);IPAddress ip=IPAddress.Parse(address),mask=SelectedMask(ip.ToString(),interfaceIndex);if(mask==null||!ValidMask(mask))throw new ArgumentException("无法确认所选网卡的 IPv4 子网。");
  StartCore(new Run{Address=ip,Mask=mask,Host=host,Index=interfaceIndex,Multicast=new IPEndPoint(Group,5353),StillLocal=()=>{var now=SelectedMask(ip.ToString(),interfaceIndex);return now!=null&&now.Equals(mask);},Failed=failed},5353);
 }
 // Deliberately isolated seam: always loopback, ephemeral listener, and loopback-only announcement sink.
 internal void StartLoopback(string hostName,int interfaceIndex,IPEndPoint sink,Action<string> failed){if(sink==null||!IPAddress.IsLoopback(sink.Address)||sink.Port<1024)throw new ArgumentException();StartCore(new Run{Address=IPAddress.Loopback,Mask=IPAddress.Parse("255.0.0.0"),Host=CompanionServer.NormalizeHostName(hostName),Index=interfaceIndex,Multicast=sink,StillLocal=()=>true,Failed=failed,Loopback=true},0);}
 void StartCore(Run run,int port){
  lock(gate){if(disposed)throw new ObjectDisposedException("CompanionAddressDiscovery");if(current!=null)throw new InvalidOperationException("地址发现服务尚未关闭。");current=run;}
  try{
   var socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);run.Socket=socket;socket.ExclusiveAddressUse=run.Loopback;if(!run.Loopback)socket.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.ReuseAddress,true);
   socket.ReceiveBufferSize=32768;socket.SendBufferSize=8192;socket.ReceiveTimeout=20;socket.SendTimeout=500;socket.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.PacketInformation,true);socket.Ttl=255;
   socket.Bind(new IPEndPoint(run.Loopback?IPAddress.Loopback:IPAddress.Any,port));
   if(!run.Loopback){socket.SetSocketOption(SocketOptionLevel.IP,(SocketOptionName)31,IPAddress.HostToNetworkOrder(run.Index));/* Windows IP_UNICAST_IF: network-byte-order interface index. */socket.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.MulticastInterface,run.Address.GetAddressBytes());socket.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.MulticastTimeToLive,255);socket.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.AddMembership,new MulticastOption(Group,run.Address));}
   run.NextProbe=run.Random.Next(0,251);ThreadPool.QueueUserWorkItem(ignored=>Receive(run));
   if(!run.Started.Task.Wait(3000)||!run.Ready||run.Stop){Stop();throw new InvalidOperationException(run.Error.Length==0?"地址发现启动超时或已取消，请检查所选网络。":run.Error);}
  }catch{run.Stop=true;if(run.Socket!=null)run.Socket.Close();lock(gate)if(current==run)current=null;throw;}
 }
 void Receive(Run run){run.ThreadId=Thread.CurrentThread.ManagedThreadId;byte[] bytes=new byte[1501];try{
  while(!run.Stop){Tick(run);if(run.Stop)break;try{EndPoint remote=new IPEndPoint(IPAddress.Any,0);SocketFlags flags=SocketFlags.None;IPPacketInformation info;int length=run.Socket.ReceiveMessageFrom(bytes,0,bytes.Length,ref flags,ref remote,out info);
   long now=run.Clock.ElapsedMilliseconds;if(now-run.ReadWindow>=1000){run.ReadWindow=now;run.ReadCount=0;}if(++run.ReadCount>100||length>1500||(flags&SocketFlags.Truncated)!=0||!Allowed(run.Address,run.Mask,run.Index,info.Address,info.Interface,remote as IPEndPoint,run.Loopback))continue;
   var packet=Parse(bytes,length,run.Host,run.Address);if(packet==null)continue;int sourcePort=((IPEndPoint)remote).Port;if(!packet.Query&&sourcePort!=5353)continue;if(packet.Conflict&&sourcePort==5353){Fail(run,"固定名称已被其他地址使用，地址发现已关闭。请重新检查连接名称。");break;}if(!run.Ready)continue;
   if(packet.Known){if(!packet.Query)run.Pending.RemoveAll(p=>p.Multicast);continue;}if(!packet.Query||!packet.Match)continue;bool legacy=sourcePort!=5353;bool multicast=!legacy&&(packet.Multicast||now-run.LastMulticast>=30000);IPEndPoint target=multicast?run.Multicast:(IPEndPoint)remote;
   if(run.Pending.Count>=16||run.Pending.Exists(p=>p.Multicast&&multicast||!p.Multicast&&!multicast&&p.Target.Equals(target)))continue;
   long due=now+(packet.Truncated?run.Random.Next(400,501):legacy?0:run.Random.Next(20,121));if(multicast)due=Math.Max(due,run.LastMulticast+1000);
   byte[] response=Response(run.Host,run.Address,legacy?packet:null,legacy?10U:120U);if(response!=null)run.Pending.Add(new Pending{Target=target,Bytes=response,Due=due,Multicast=multicast});
  }catch(SocketException ex){if(!run.Stop&&ex.SocketErrorCode!=SocketError.TimedOut&&ex.SocketErrorCode!=SocketError.WouldBlock&&ex.SocketErrorCode!=SocketError.MessageSize)throw;}catch(ObjectDisposedException){if(!run.Stop)throw;}}
 }catch{if(!run.Stop)Fail(run,"地址发现接收失败，请重新检查所选网络和 UDP 5353 规则。");}finally{run.Ready=false;run.Stop=true;run.Started.TrySetResult(false);try{run.Socket.Close();}catch{}run.Done.TrySetResult(true);lock(gate)if(current==run)current=null;}}
 void Tick(Run run){long now=run.Clock.ElapsedMilliseconds;
  if(!run.Ready&&now>=run.NextProbe){if(run.Probes<3){if(!Send(run,Probe(run.Host,run.Address),run.Multicast,false))return;run.Probes++;run.NextProbe=now+250;}else{if(!Send(run,Response(run.Host,run.Address,null,120),run.Multicast,true))return;run.Ready=true;run.Announcements=1;run.NextAnnouncement=now+1000;run.Started.TrySetResult(true);}}
  if(run.Ready&&run.Announcements==1&&now>=run.NextAnnouncement){if(now-run.LastMulticast<1000)run.NextAnnouncement=run.LastMulticast+1000;else{if(!Send(run,Response(run.Host,run.Address,null,120),run.Multicast,true))return;run.Announcements++;}}
  for(int i=run.Pending.Count-1;i>=0;i--)if(run.Pending[i].Due<=now){var pending=run.Pending[i];if(pending.Multicast&&now-run.LastMulticast<1000){pending.Due=run.LastMulticast+1000;continue;}run.Pending.RemoveAt(i);if(!Send(run,pending.Bytes,pending.Target,pending.Multicast))return;}
 }
 bool Send(Run run,byte[] bytes,IPEndPoint target,bool multicast){lock(run.SendGate){
  if(run.Stop)return false;try{if(!run.StillLocal()){Fail(run,"所选网卡或地址已变化，地址发现已关闭。");return false;}long now=run.Clock.ElapsedMilliseconds;if(now-run.SendWindow>=1000){run.SendWindow=now;run.SendCount=0;}if(run.SendCount>=20)return true;run.SendCount++;run.Socket.SendTo(bytes,target);if(multicast)run.LastMulticast=now;return true;}catch{if(!run.Stop)Fail(run,"地址发现发送失败，请检查所选网络和 UDP 5353 规则。");return false;}
 }}
 void Fail(Run run,string error){if(run.Stop)return;run.Error=error;run.Stop=true;run.Ready=false;run.Started.TrySetResult(false);try{if(run.Failed!=null)run.Failed(error);}catch{}}
 public void Stop(){Run run;lock(gate){run=current;if(run==null)return;}
  lock(run.SendGate){bool goodbye=run.Ready&&!run.Stop;run.Ready=false;run.Stop=true;try{if(goodbye&&run.StillLocal())run.Socket.SendTo(Response(run.Host,run.Address,null,0),run.Multicast);}catch{}finally{try{run.Socket.Close();}catch{}}}
  if(run.ThreadId!=Thread.CurrentThread.ManagedThreadId&&run.ThreadId!=0)run.Done.Task.Wait(1000);lock(gate)if(current==run)current=null;
 }
 public void Dispose(){lock(gate)disposed=true;Stop();}
 internal static bool ValidMask(IPAddress mask){if(mask==null||mask.AddressFamily!=AddressFamily.InterNetwork)return false;uint value=Number(mask),inverse=~value;return value!=0&&(inverse&(inverse+1))==0;}
 static uint Number(IPAddress address){byte[] a=address.GetAddressBytes();return ((uint)a[0]<<24)|((uint)a[1]<<16)|((uint)a[2]<<8)|a[3];}
 internal static bool Allowed(IPAddress address,IPAddress mask,int selected,IPAddress destination,int incoming,IPEndPoint remote,bool loopback){
  if(remote==null||remote.Port==0||selected<=0||incoming!=selected||destination==null||(!destination.Equals(address)&&!destination.Equals(Group))||remote.Address.AddressFamily!=AddressFamily.InterNetwork||!ValidMask(mask))return false;
  byte[] bytes=remote.Address.GetAddressBytes();if(bytes[0]==0||bytes[0]>=224||!loopback&&IPAddress.IsLoopback(remote.Address))return false;uint peer=Number(remote.Address),own=Number(address),netmask=Number(mask);if((peer&netmask)!=(own&netmask))return false;uint host=peer&~netmask;return peer==own||(host!=0&&host!=~netmask);
 }
 // Protocol helpers below never touch sockets or private desktop state.
 internal static Packet Parse(byte[] bytes,int length,string host,IPAddress address){
  if(bytes==null||length<12||length>1500||length>bytes.Length)return null;try{int flags=U16(bytes,2),questions=U16(bytes,4),answers=U16(bytes,6),authorities=U16(bytes,8),extra=U16(bytes,10);if((flags&0x780f)!=0||questions>8||answers+authorities+extra>32)return null;
   var result=new Packet{Id=(ushort)U16(bytes,0),Query=(flags&0x8000)==0,Truncated=(flags&0x0200)!=0};int offset=12;
   for(int i=0;i<questions;i++){byte[] wire;string name=ReadName(bytes,length,ref offset,out wire);Need(offset,4,length);ushort type=(ushort)U16(bytes,offset),qclass=(ushort)U16(bytes,offset+2);offset+=4;result.Questions.Add(new Question{Name=name,WireName=wire,Type=type,Class=qclass});if(String.Equals(name,host,StringComparison.OrdinalIgnoreCase)&&(type==1||type==255)&&((qclass&0x7fff)==1||(qclass&0x7fff)==255)){result.Match=true;if((qclass&0x8000)==0)result.Multicast=true;}}
   for(int i=0;i<answers+authorities+extra;i++){string name=ReadName(bytes,length,ref offset);Need(offset,10,length);int type=U16(bytes,offset),rclass=U16(bytes,offset+2),count=U16(bytes,offset+8);uint ttl=U32(bytes,offset+4);offset+=10;Need(offset,count,length);if(type==1&&count!=4||type==28&&count!=16)return null;
    if(type==1&&(rclass&0x7fff)==1&&String.Equals(name,host,StringComparison.OrdinalIgnoreCase)){byte[] expected=address.GetAddressBytes();bool same=true;for(int j=0;j<4;j++)if(bytes[offset+j]!=expected[j])same=false;if(same&&ttl>=60&&i<answers)result.Known=true;if(!same&&ttl>0&&(!result.Query||i>=answers&&i<answers+authorities))result.Conflict=true;}offset+=count;
   }
   return offset==length?result:null;
  }catch(ArgumentException){return null;}
 }
 static void Need(int offset,int count,int length){if(offset<0||count<0||offset>length-count)throw new ArgumentException();}
 static int U16(byte[] bytes,int offset){return (bytes[offset]<<8)|bytes[offset+1];}
 static uint U32(byte[] bytes,int offset){return ((uint)bytes[offset]<<24)|((uint)bytes[offset+1]<<16)|((uint)bytes[offset+2]<<8)|bytes[offset+3];}
 static string ReadName(byte[] bytes,int length,ref int offset){byte[] ignored;return ReadName(bytes,length,ref offset,out ignored);}
 static string ReadName(byte[] bytes,int length,ref int offset,out byte[] encoded){int cursor=offset,expanded=0,jumps=0;bool jumped=false,ascii=true;var visited=new HashSet<int>();var labels=new List<string>();using(var wire=new MemoryStream()){while(true){Need(cursor,1,length);if(!visited.Add(cursor))throw new ArgumentException();int count=bytes[cursor++];if(count==0){if(!jumped)offset=cursor;wire.WriteByte(0);encoded=wire.ToArray();return ascii?String.Join(".",labels.ToArray()):null;}if((count&0xc0)==0xc0){Need(cursor,1,length);int target=((count&0x3f)<<8)|bytes[cursor++];if(target<12||target>=cursor-2||++jumps>16)throw new ArgumentException();if(!jumped)offset=cursor;jumped=true;cursor=target;continue;}if((count&0xc0)!=0||count>63)throw new ArgumentException();Need(cursor,count,length);expanded+=count+1;if(expanded>254)throw new ArgumentException();for(int i=0;i<count;i++){byte c=bytes[cursor+i];if(c<33||c>126||c=='.'||c=='\\')ascii=false;}wire.WriteByte((byte)count);wire.Write(bytes,cursor,count);labels.Add(Encoding.ASCII.GetString(bytes,cursor,count));cursor+=count;}}}
 static void Put16(Stream stream,int value){stream.WriteByte((byte)(value>>8));stream.WriteByte((byte)value);}
 static void Put32(Stream stream,uint value){stream.WriteByte((byte)(value>>24));stream.WriteByte((byte)(value>>16));stream.WriteByte((byte)(value>>8));stream.WriteByte((byte)value);}
 static void Name(Stream stream,string name){if(name.Length>0)foreach(string label in name.Split('.')){byte[] bytes=Encoding.ASCII.GetBytes(label);stream.WriteByte((byte)bytes.Length);stream.Write(bytes,0,bytes.Length);}stream.WriteByte(0);}
 static void Answer(Stream stream,string host,IPAddress address,uint ttl,bool flush){Name(stream,host);Put16(stream,1);Put16(stream,flush?0x8001:1);Put32(stream,ttl);Put16(stream,4);byte[] bytes=address.GetAddressBytes();stream.Write(bytes,0,bytes.Length);}
 internal static byte[] Response(string host,IPAddress address,Packet legacy,uint ttl){using(var stream=new MemoryStream()){Put16(stream,legacy==null?0:legacy.Id);Put16(stream,0x8400);Put16(stream,legacy==null?0:legacy.Questions.Count);Put16(stream,1);Put16(stream,0);Put16(stream,0);if(legacy!=null)foreach(var question in legacy.Questions){stream.Write(question.WireName,0,question.WireName.Length);Put16(stream,question.Type);Put16(stream,question.Class&0x7fff);}Answer(stream,host,address,ttl,legacy==null);return stream.Length<=1500?stream.ToArray():null;}}
 internal static byte[] Probe(string host,IPAddress address){using(var stream=new MemoryStream()){Put16(stream,0);Put16(stream,0);Put16(stream,1);Put16(stream,0);Put16(stream,1);Put16(stream,0);Name(stream,host);Put16(stream,1);Put16(stream,1);Answer(stream,host,address,120,false);return stream.ToArray();}}
}
