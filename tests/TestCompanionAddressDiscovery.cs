using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Linq;using System.Net;using System.Net.NetworkInformation;using System.Net.Sockets;using System.Reflection;using System.Text;using System.Threading;
class TestCompanionAddressDiscovery {
 const string Host="prismglass-a1b2c3d4.local";static readonly IPAddress Address=IPAddress.Parse("192.168.1.20"),Mask=IPAddress.Parse("255.255.255.0");static int checks;
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Put(Stream s,int value){s.WriteByte((byte)(value>>8));s.WriteByte((byte)value);}
 static int U16(byte[] b,int n){return b[n]*256+b[n+1];}static uint U32(byte[] b,int n){return ((uint)b[n]<<24)|((uint)b[n+1]<<16)|((uint)b[n+2]<<8)|b[n+3];}
 static byte[] Name(string name){using(var s=new MemoryStream()){if(name.Length>0)foreach(string label in name.Split('.')){byte[] b=Encoding.UTF8.GetBytes(label);s.WriteByte((byte)b.Length);s.Write(b,0,b.Length);}s.WriteByte(0);return s.ToArray();}}
 static byte[] Query(int id=1234,string name=Host,int type=1,int qclass=1,int flags=0){using(var s=new MemoryStream()){Put(s,id);Put(s,flags);Put(s,1);Put(s,0);Put(s,0);Put(s,0);byte[] b=Name(name);s.Write(b,0,b.Length);Put(s,type);Put(s,qclass);return s.ToArray();}}
 static byte[] Join(byte[] first,byte[] second){return first.Concat(second).ToArray();}
 static CompanionAddressDiscovery.Packet Parse(byte[] b){return CompanionAddressDiscovery.Parse(b,b.Length,Host,Address);}
 static byte[] Questions(byte[] first,byte[] name,int type=1,int qclass=1){using(var s=new MemoryStream()){s.Write(first,0,first.Length);s.Write(name,0,name.Length);Put(s,type);Put(s,qclass);byte[] result=s.ToArray();result[5]++;return result;}}
 static byte[] Known(uint ttl){byte[] query=Query(),answer=CompanionAddressDiscovery.Response(Host,Address,null,ttl);query[7]=1;return Join(query,answer.Skip(12).ToArray());}
 static void Protocol(){
  var p=Parse(Query());Check(p!=null&&p.Query&&p.Match&&p.Multicast&&p.Id==1234,"only exact hostname A question is recognized");
  Check(Parse(Query(type:255,qclass:255)).Match&&Parse(Query(name:Host.ToUpperInvariant())).Match,"ANY class/type and ASCII case folding remain bounded to the same name");
  Check(!Parse(Query(type:28)).Match&&!Parse(Query(name:"other.local")).Match&&!Parse(Query(name:Host+".evil")).Match,"AAAA, other hosts and suffix lookalikes receive no answer");
  Check(!Parse(Query(qclass:0x8001)).Multicast&&Parse(Query(flags:0x200)).Truncated,"QU and truncated-query scheduling bits are parsed");
  Check(Parse(Query(flags:0x100))!=null&&Parse(Query(flags:0x800))==null&&Parse(Query(flags:1))==null,"ordinary RD is ignored but opcode and error queries are rejected");
  Check(Parse(new byte[11])==null&&Parse(new byte[1501])==null&&Parse(Join(Query(),new byte[]{0}))==null,"short, oversized and trailing-data datagrams are rejected");
  byte[] tooMany=Query();tooMany[4]=1;Check(Parse(tooMany)==null,"untrusted question count cannot allocate an unbounded parser");
  foreach(byte[] badName in new[]{new byte[]{0xc0,12},new byte[]{0xc0,0},new byte[]{0xc0,20},new byte[]{0xc0},new byte[]{0x40},new byte[]{63,1,2}}){byte[] malformed=Join(Query().Take(12).ToArray(),badName);Check(Parse(malformed)==null,"invalid compression, label prefix or truncated name is rejected");}
  byte[] longName=Name(String.Join(".",Enumerable.Repeat(new string('a',63),5)));Check(Parse(Questions(Query(),longName))==null,"expanded domain length is bounded independently of compression");
  byte[] compressed=Questions(Query(),new byte[]{0xc0,12},255,0x8001);p=Parse(compressed);Check(p!=null&&p.Questions.Count==2&&p.Match&&p.Multicast,"backward compressed questions retain original consumed offset and mixed QM preference");
  byte[] partial=Questions(Query(),new byte[]{0xc0});Check(Parse(partial)==null,"a valid first question never bypasses invalid later questions");
  byte[] mixed=Questions(Questions(Query(),Name("设备 名._service._tcp.local")),Name(""));p=Parse(mixed);Check(p!=null&&p.Match&&p.Questions[1].Name==null&&p.Questions[2].Name=="","unrelated UTF-8 labels and root names do not invalidate an exact A question");
  byte[] legacy=CompanionAddressDiscovery.Response(Host,Address,p,10);var legacyParsed=CompanionAddressDiscovery.Parse(legacy,legacy.Length,Host,Address);Check(legacyParsed!=null&&legacyParsed.Questions.Count==3&&legacyParsed.Questions[1].WireName.SequenceEqual(p.Questions[1].WireName)&&U16(legacy,0)==1234&&U16(legacy,2)==0x8400,"legacy response echoes decompressed raw question names and transaction ID safely");
  Check(U16(legacy,legacy.Length-12)==1&&U32(legacy,legacy.Length-10)==10,"legacy A uses low TTL and never sets cache-flush");
  byte[] multicast=CompanionAddressDiscovery.Response(Host,Address,null,120);Check(U16(multicast,0)==0&&U16(multicast,4)==0&&U16(multicast,6)==1&&U16(multicast,multicast.Length-12)==0x8001&&U32(multicast,multicast.Length-10)==120,"multicast response contains one fixed cache-flush A and no echoed questions");
  Check(multicast.Skip(multicast.Length-4).SequenceEqual(Address.GetAddressBytes())&&Parse(multicast).Known,"fixed answer contains only the selected IPv4 bytes");
  Check(Parse(Known(60)).Known&&!Parse(Known(59)).Known,"known-answer suppression uses half the fixed 120-second lifetime");
  byte[] goodbye=CompanionAddressDiscovery.Response(Host,Address,null,0);Check(U32(goodbye,goodbye.Length-10)==0&&!Parse(goodbye).Known&&!Parse(goodbye).Conflict,"goodbye has TTL zero without claiming a conflict");
  byte[] other=CompanionAddressDiscovery.Response(Host,IPAddress.Parse("192.168.1.21"),null,120);Check(Parse(other).Conflict&&!Parse(other).Query,"conflicting authoritative IPv4 answers are recognized");
  byte[] probe=CompanionAddressDiscovery.Probe(Host,Address);Check(Parse(probe).Match&&!Parse(probe).Conflict&&U16(probe,8)==1,"startup probe proposes only the selected A in its authority section");
  Check(Parse(CompanionAddressDiscovery.Probe(Host,IPAddress.Parse("192.168.1.21"))).Conflict,"a different simultaneous A proposal is detected");
  byte[] oldKnown=Known(120);oldKnown[oldKnown.Length-1]=21;Check(!Parse(oldKnown).Conflict&&!Parse(oldKnown).Known,"stale cached address in a query is not treated as ownership");
  byte[] badRecord=Known(120);badRecord[badRecord.Length-5]=3;Check(Parse(badRecord)==null,"A record data length must be exactly four bytes");
  byte[] embedded=Name(Host);embedded[0]=(byte)(Host.Length);embedded=Join(new[]{(byte)Host.Length},Join(Encoding.ASCII.GetBytes(Host),new byte[]{0}));p=Parse(Questions(Query(name:"other.local"),embedded));Check(p!=null&&!p.Match,"a literal dot inside a DNS label cannot impersonate two host labels");
 }
 static void Scope(){var peer=new IPEndPoint(IPAddress.Parse("192.168.1.30"),5353);Check(CompanionAddressDiscovery.Allowed(Address,Mask,19,CompanionAddressDiscovery.Group,19,peer,false),"selected interface and on-subnet multicast peer are admitted");
  Check(!CompanionAddressDiscovery.Allowed(Address,Mask,19,CompanionAddressDiscovery.Group,20,peer,false)&&!CompanionAddressDiscovery.Allowed(Address,Mask,19,IPAddress.Parse("192.168.1.21"),19,peer,false),"other interfaces and destination addresses are rejected before parsing");
  foreach(string ip in new[]{"192.168.2.30","192.168.1.0","192.168.1.255","0.0.0.0","224.0.0.251","127.0.0.1"})Check(!CompanionAddressDiscovery.Allowed(Address,Mask,19,Address,19,new IPEndPoint(IPAddress.Parse(ip),12345),false),"off-link, network, broadcast or invalid source is rejected");
  Check(!CompanionAddressDiscovery.ValidMask(IPAddress.Any)&&!CompanionAddressDiscovery.ValidMask(IPAddress.Parse("255.0.255.0"))&&CompanionAddressDiscovery.ValidMask(Mask),"subnet masks must be nonzero and contiguous");
 }
 static byte[] Read(Socket socket,int milliseconds,Func<byte[],bool> matches){var timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<milliseconds){try{byte[] b=new byte[1501];int count=socket.Receive(b);Array.Resize(ref b,count);if(matches(b))return b;}catch(SocketException ex){if(ex.SocketErrorCode!=SocketError.TimedOut)throw;}}return null;}
 static void Loopback(){int index=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.NetworkInterfaceType==NetworkInterfaceType.Loopback&&n.Supports(NetworkInterfaceComponent.IPv4)).Select(n=>n.GetIPProperties().GetIPv4Properties().Index).First();
  using(var peer=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp)){peer.Bind(new IPEndPoint(IPAddress.Loopback,0));peer.ReceiveTimeout=30;string error=null;
   using(var publisher=new CompanionAddressDiscovery()){publisher.StartLoopback(Host,index,(IPEndPoint)peer.LocalEndPoint,message=>error=message);int port=publisher.BoundPort;var target=new IPEndPoint(IPAddress.Loopback,port);Check(publisher.IsReady&&port!=5353&&error==null,"loopback publisher probes and announces on an ephemeral port without LAN membership");
    byte[] question=Query();peer.SendTo(question,target);byte[] reply=Read(peer,1000,b=>b.Length>=12&&U16(b,0)==1234&&(U16(b,2)&0x8000)!=0);Check(reply!=null&&U32(reply,reply.Length-10)==10&&reply.Skip(reply.Length-4).SequenceEqual(IPAddress.Loopback.GetAddressBytes()),"real ReceiveMessageFrom metadata permits an exact legacy loopback question and fixed A response");
    peer.SendTo(Query(id:4321,name:"private-other.local"),target);Check(Read(peer,180,b=>b.Length>=12&&U16(b,0)==4321)==null,"real socket emits no answer for another hostname");
    peer.SendTo(CompanionAddressDiscovery.Response(Host,IPAddress.Parse("127.0.0.2"),null,120),target);Thread.Sleep(40);Check(publisher.IsReady&&error==null,"a forged response from a legacy source port cannot stop publication");
    object run=typeof(CompanionAddressDiscovery).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(publisher);var fields=run.GetType();var clock=(Stopwatch)fields.GetField("Clock").GetValue(run);fields.GetField("SendWindow").SetValue(run,clock.ElapsedMilliseconds);fields.GetField("SendCount").SetValue(run,0);
    for(int i=0;i<60;i++)peer.SendTo(Query(id:2000+i),target);int replies=0;var timer=Stopwatch.StartNew();while(timer.ElapsedMilliseconds<350){byte[] r=Read(peer,30,b=>b.Length>=12&&U16(b,0)>=2000&&U16(b,0)<2060);if(r!=null)replies++;}Check(replies>0&&replies<=20,"actual UDP replies obey the global twenty-per-second output budget");
    publisher.Stop();byte[] bye=Read(peer,1000,b=>b.Length>20&&U16(b,0)==0&&U16(b,6)==1&&U32(b,b.Length-10)==0);Check(bye!=null&&!publisher.IsReady,"stop emits a fixed TTL-zero goodbye before closing");
    using(var rebound=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp)){rebound.ExclusiveAddressUse=true;rebound.Bind(target);Check(true,"stop releases the UDP listener for exclusive rebinding");}
    Check(Read(peer,100,b=>b.Length>20&&U16(b,6)==1&&U32(b,b.Length-10)>0)==null,"no positive response is sent after the goodbye");
    publisher.StartLoopback(Host,index,(IPEndPoint)peer.LocalEndPoint,message=>error=message);Check(publisher.IsReady,"same publisher can restart after complete socket cleanup");publisher.Stop();
   }
  }
 }
 static int Main(){try{Protocol();Scope();Loopback();Console.WriteLine("Passed "+checks+" address discovery checks; only loopback ephemeral UDP was used.");return 0;}catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}}
}
