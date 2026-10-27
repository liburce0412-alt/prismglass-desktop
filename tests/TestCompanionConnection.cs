using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Web.Script.Serialization;

class TestCompanionConnection {
 const string Host="prismglass-a1b2c3d4.local";static int checks;static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CommandLineToArgvW(string command,out int count);
 [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr value);
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static bool Rejects(Action action){try{action();return false;}catch(ArgumentException){return true;}catch(InvalidDataException){return true;}catch(InvalidOperationException){return true;}}
 static string[] Parse(string command){int count;IntPtr pointer=CommandLineToArgvW("fixture.exe "+command,out count);if(pointer==IntPtr.Zero)throw new Exception("Windows argument parser unavailable");try{var result=new string[count-1];for(int i=1;i<count;i++)result[i-1]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer,i*IntPtr.Size));return result;}finally{LocalFree(pointer);}}
 static Dictionary<string,object> Record(string root){return new Dictionary<string,object>{{"schemaVersion",1},{"owner","PrismGlass.Companion.Network.v1"},{"appRoot",root},{"hostName",Host},{"port",9443},{"interfaceId","wifi-id"},{"interfaceIndex",19},{"address","192.168.1.20"}};}
 static void Write(string root,Dictionary<string,object> value){File.WriteAllText(Path.Combine(root,"runtime-data","companion-network.json"),Json.Serialize(value));}
 static void Main(string[] args){string fixtures=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"connection-fixture-"+Guid.NewGuid().ToString("N")));try{Directory.CreateDirectory(fixtures);if(args.Length!=1)throw new ArgumentException("Pass the running PowerShell 7 executable path from the test script.");LanChecks();SelectionChecks();RecordChecks(fixtures);ArgumentChecks(fixtures);PowerShellChecks(fixtures,args[0]);Console.WriteLine("Passed "+checks+" companion connection checks (no listener, subprocess, certificate or network changes).");}catch(Exception ex){Console.WriteLine("FAIL "+ex);Environment.ExitCode=1;}finally{string parent=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!fixtures.StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new Exception("Fixture cleanup escaped the test directory");if(Directory.Exists(fixtures))Directory.Delete(fixtures,true);}}
 static void LanChecks(){
  foreach(string address in new[]{"10.0.0.1","10.255.255.254","172.16.0.1","172.31.255.254","192.168.0.1","192.168.255.254"})Check(CompanionConnection.Lan(IPAddress.Parse(address)),"private IPv4 accepted: "+address);
  foreach(string address in new[]{"8.8.8.8","127.0.0.1","0.0.0.0","169.254.1.2","172.15.255.254","172.32.0.1","192.167.255.254","192.169.0.1","224.0.0.1","255.255.255.255","::1","fd00::1","fe80::1","::ffff:192.168.1.2"})Check(!CompanionConnection.Lan(IPAddress.Parse(address)),"non-LAN IPv4 or IPv6 rejected: "+address);
 }
 static void SelectionChecks(){
  var publicEthernet=new CompanionNetworkChoice{address="192.168.2.10",id="ethernet",index=2,category=0};var preferred=new CompanionNetworkChoice{address="10.91.174.4",id="WiFi-Guid",index=19,category=1};var otherPrivate=new CompanionNetworkChoice{address="192.168.1.20",id="other",index=3,category=1};var choices=new[]{publicEthernet,otherPrivate,preferred};
  Check(Object.ReferenceEquals(CompanionConnection.Select(choices,publicEthernet.address,preferred.id),publicEthernet),"a still-current selected address wins over remembered interface");
  Check(Object.ReferenceEquals(CompanionConnection.Select(choices,"192.168.31.201","wifi-guid"),preferred),"stale IP follows the same interface across a Wi-Fi change, ignoring ID case");
  Check(Object.ReferenceEquals(CompanionConnection.Select(choices,"192.168.31.201","missing"),otherPrivate),"missing interface falls back to the first private network");
  Check(Object.ReferenceEquals(CompanionConnection.Select(new[]{publicEthernet},"old","missing"),publicEthernet),"public network remains selectable without being marked private");
  Check(CompanionConnection.Select(new CompanionNetworkChoice[0],"old","missing")==null,"no usable adapters returns no choice");
  Check(!publicEthernet.IsPrivate&&publicEthernet.Description.Contains("公共网络")&&preferred.IsPrivate,"network descriptions do not imply a public network is trusted");
  const string id="aabbccdd-1122-3344-5566-778899aabbcc";var original=new CompanionNetworkChoice{address="10.91.174.4",id=id,index=19,name="Home Wi-Fi",category=1};var equivalent=new CompanionNetworkChoice{address=original.address,id="{"+id.ToUpperInvariant()+"}",index=19,name=original.name,category=1};
  Check(CompanionConnection.InterfaceId(equivalent.id)==id&&Object.ReferenceEquals(CompanionConnection.Select(new[]{otherPrivate,original},"192.168.31.201",equivalent.id),original),"braced uppercase Windows GUID selects the same interface as its canonical ID");
  Check(CompanionConnection.SameNetwork(original,equivalent),"GUID formatting differences do not invalidate an unchanged private network");
  equivalent.name="Other Wi-Fi";bool changedName=!CompanionConnection.SameNetwork(original,equivalent);equivalent.name=original.name;equivalent.index=20;Check(changedName&&!CompanionConnection.SameNetwork(original,equivalent),"same IP and interface ID cannot hide a changed network name or interface index");
 }
 static void RecordChecks(string fixtures){
  string root=Path.Combine(fixtures,"app");Directory.CreateDirectory(Path.Combine(root,"runtime-data"));string file=Path.Combine(root,"runtime-data","companion-network.json");
  Check(CompanionConnection.ReadRecord(root)==null,"missing setup record is an unconfigured installation");Write(root,Record(root));Check(CompanionConnection.Text(CompanionConnection.ReadRecord(root),"hostName")==Host,"valid owned record loads for this installation");
  var validCase=Record(root.ToUpperInvariant());Write(root,validCase);Check(CompanionConnection.ReadRecord(root)!=null,"Windows installation identity ignores path case");
  foreach(object version in new object[]{"1",1.5,0,2,true,null}){var record=Record(root);record["schemaVersion"]=version;Write(root,record);Check(Rejects(()=>CompanionConnection.ReadRecord(root)),"schema must be integer version one: "+Convert.ToString(version));}
  foreach(object owner in new object[]{"another.application",true,null}){var record=Record(root);record["owner"]=owner;Write(root,record);Check(Rejects(()=>CompanionConnection.ReadRecord(root)),"foreign or malformed owner rejected");}
  foreach(object appRoot in new object[]{root+"-other",Path.Combine(root,"child"),12,null}){var record=Record(root);record["appRoot"]=appRoot;Write(root,record);Check(Rejects(()=>CompanionConnection.ReadRecord(root)),"foreign or malformed appRoot rejected");}
  string previous=Environment.CurrentDirectory;try{Environment.CurrentDirectory=root;var record=Record(".");Write(root,record);Check(Rejects(()=>CompanionConnection.ReadRecord(root)),"relative appRoot is rejected even when it resolves to this installation");}finally{Environment.CurrentDirectory=previous;}
  foreach(string host in new[]{"arbitrary.local","prismglass-a1b2c3d4.local.evil","prismglass-a1b2c3d4.local\n",""}){var record=Record(root);record["hostName"]=host;Write(root,record);Check(Rejects(()=>CompanionConnection.ReadRecord(root)),"record cannot substitute an unrelated or malformed host");}
  File.WriteAllText(file,"{broken");Check(Rejects(()=>CompanionConnection.ReadRecord(root)),"malformed JSON fails without replacing the record");Check(File.ReadAllText(file)=="{broken","invalid record remains untouched for diagnosis");
  File.WriteAllText(file,new string(' ',70000));Check(Rejects(()=>CompanionConnection.ReadRecord(root)),"oversized configuration is rejected before deserialization");
  var typed=new Dictionary<string,object>{{"text",10},{"flag","true"},{"number","9443"}};Check(CompanionConnection.Text(typed,"text")==""&&!CompanionConnection.Flag(typed,"flag")&&CompanionConnection.Number(typed,"number")==0,"record accessors do not coerce strings into flags or numbers");
 }
 static void ArgumentChecks(string fixtures){
  foreach(string value in new[]{"plain","with spaces & ' ; $(text)","中文",@"C:\folder\",@"C:\folder\\",""}){string[] parsed=Parse(CompanionConnection.Quote(value));Check(parsed.Length==1&&parsed[0]==value,"quoting round trips through the Windows argument parser: "+value);}
  foreach(string value in new[]{null,"one\rtwo","one\ntwo","one\0two","one\"two"})Check(Rejects(()=>CompanionConnection.Quote(value)),"null, CR, LF, NUL and embedded quotes are rejected");
  string parent=Path.Combine(fixtures,"setup & 空间"),root=Path.Combine(parent,"app"),script=Path.Combine(parent,"RepairCompanion.ps1"),record=Path.Combine(parent,"previous setup.json");Directory.CreateDirectory(root);File.WriteAllText(script,"throw 'This test fixture must never execute.'");
  var choice=new CompanionNetworkChoice{address="10.91.174.4",index=19,id="wifi"};string[] baseline=Parse(CompanionConnection.Arguments(root,choice,Host,9443,null,false,false));
  Check(baseline.SequenceEqual(new[]{"-NoProfile","-NonInteractive","-File",script,"-AppRoot",root,"-Address",choice.address,"-InterfaceIndex","19","-HostName",Host,"-Port","9443","-UserSid",WindowsIdentity.GetCurrent().User.Value}),"check arguments preserve literal paths and values, use File mode, and omit mutation flags");
  string[] apply=Parse(CompanionConnection.Arguments(root,choice,Host,9443,record,true,true));Check(apply.Skip(baseline.Length).SequenceEqual(new[]{"-ExistingSetupRecord",record,"-TrustNetwork","-Apply"}),"trust and apply appear only when explicitly requested");
  string[] trailing=Parse(CompanionConnection.Arguments(root+Path.DirectorySeparatorChar,choice,Host,9443,null,false,false));Check(trailing[3]==script&&trailing[5].TrimEnd(Path.DirectorySeparatorChar)==root,"a trailing appRoot separator neither loses the sibling repair tool nor swallows arguments");
  Check(Rejects(()=>CompanionConnection.Arguments(root,null,Host,9443,null,false,false))&&Rejects(()=>CompanionConnection.Arguments(root,new CompanionNetworkChoice{address=choice.address,index=0},Host,9443,null,false,false)),"missing choice and invalid interface index cannot build commands");
  Check(Rejects(()=>CompanionConnection.Arguments(root,new CompanionNetworkChoice{address="8.8.8.8",index=19},Host,9443,null,false,false))&&Rejects(()=>CompanionConnection.Arguments(root,choice,"anything.local",9443,null,false,false))&&Rejects(()=>CompanionConnection.Arguments(root,choice,Host,443,null,false,false)),"public IP, foreign hostname and privileged port are rejected before launch");
  Check(Rejects(()=>CompanionConnection.Arguments(root,choice,Host,9443,"previous\nrecord.json",false,false))&&Rejects(()=>CompanionConnection.Arguments(root,choice,Host,9443,"previous\"record.json",false,false)),"existing-record argument cannot contain line breaks or quotes");
  Check(Rejects(()=>CompanionConnection.Arguments(Path.GetPathRoot(root),choice,Host,9443,null,false,false)),"a drive root is rejected rather than searching an unrelated repair tool");
  File.Delete(script);Check(Rejects(()=>CompanionConnection.Arguments(root,choice,Host,9443,null,false,false)),"missing repair script fails rather than launching a different command");
 }
 static void PowerShellChecks(string fixtures,string currentPowerShell){
  string root=Path.Combine(fixtures,"powershell-app"),fakeDirectory=Path.Combine(fixtures,"invalid-runtime"),originalPath=Environment.GetEnvironmentVariable("PATH");Directory.CreateDirectory(Path.Combine(root,"runtime-data"));Directory.CreateDirectory(fakeDirectory);currentPowerShell=Path.GetFullPath(currentPowerShell);
  string standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"PowerShell","7","pwsh.exe"),fallback=currentPowerShell;if(File.Exists(standard)){var version=FileVersionInfo.GetVersionInfo(standard);if(version.ProductName=="PowerShell"&&version.FileMajorPart>=7)fallback=Path.GetFullPath(standard);}
  try{
   var record=Record(root);record["powerShellPath"]=currentPowerShell;Write(root,record);Environment.SetEnvironmentVariable("PATH","");Check(CompanionConnection.PowerShell(root)==currentPowerShell,"record selects the actual running PowerShell 7 path without launching it");
   record["powerShellPath"]=Path.Combine(fixtures,"missing-runtime","pwsh.exe");Write(root,record);Environment.SetEnvironmentVariable("PATH",Path.GetDirectoryName(currentPowerShell));Check(CompanionConnection.PowerShell(root)==fallback,"missing recorded runtime falls back to validated Program Files or PATH runtime");
   string fake=Path.Combine(fakeDirectory,"pwsh.exe");File.WriteAllText(fake,"Not an executable and not a PowerShell product.");record["powerShellPath"]=fake;Write(root,record);Environment.SetEnvironmentVariable("PATH",fakeDirectory+";"+Path.GetDirectoryName(currentPowerShell));Check(CompanionConnection.PowerShell(root)==fallback,"same-name invalid executable in the record and before PATH runtime is skipped");
   if(!File.Exists(standard)){Environment.SetEnvironmentVariable("PATH",fakeDirectory);Check(CompanionConnection.PowerShell(root)==Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"),"without a valid PowerShell 7 installation only the system Windows PowerShell is selected");}
  }finally{Environment.SetEnvironmentVariable("PATH",originalPath);}
 }
}
