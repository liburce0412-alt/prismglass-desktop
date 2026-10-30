using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

internal static class TestCompanionStream {
 sealed class Fake : ICompanionStreamRuntime {
  public bool Alive {get;set;} public int starts,stops;public Action duringStart;public string mode;public int monitor;public bool failStop;
  public object Describe(){return new{available=true};}
  public string Start(string mode,int monitor,string quality,string offer){this.mode=mode;this.monitor=monitor;starts++;Alive=true;if(duringStart!=null)duringStart();return "v=0\r\n";}
  public void Stop(){stops++;if(failStop){failStop=false;throw new IOException();}Alive=false;}public void Dispose(){Stop();}
 }
 static int checks;
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static void RejectSelection(Action action,string message){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,message);}
 static CompanionPhysicalMonitor Monitor(string identity,string device){return new CompanionPhysicalMonitor{Identity=identity,DeviceName=device,Name=device};}
 static string ReadAddress(CompanionStreamRuntime runtime){var value=typeof(CompanionStreamRuntime).GetMethod("Read",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(runtime,null);return (string)value.GetType().GetField("address").GetValue(value);}
 static void RejectAddress(CompanionStreamRuntime runtime,string message){bool rejected=false;try{ReadAddress(runtime);}catch(System.Reflection.TargetInvocationException error){rejected=error.InnerException is IOException||error.InnerException is ArgumentException;}Check(rejected,message);}
 static Dictionary<string,object> Offer(){return new Dictionary<string,object>{{"mode","mirror"},{"monitor",0},{"quality","720"},{"offer","v=0\r\n"}};}
 static Dictionary<string,object> Lease(CompanionReply reply){var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(reply.Body));return new Dictionary<string,object>{{"lease",data["lease"]}};}
 static int Main(string[] args){try{
  if(args.Length>0&&args[0]=="--integration"){
   if(args.Length<6||args.Length>8)throw new ArgumentException("Expected --integration root quality offer answer stop [mirror|extend] [connection-address]");
   string mode=args.Length>=7?args[6]:"mirror";if(mode!="mirror"&&mode!="extend")throw new ArgumentException("Unknown integration mode");
   using(var runtime=new CompanionStreamRuntime(args[1],args.Length==8?(Func<string>)(()=>args[7]):null)){runtime.Describe();string answer=runtime.Start(mode,mode=="extend"?-1:0,args[2],File.ReadAllText(args[3]));File.WriteAllText(args[4],answer);var deadline=DateTime.UtcNow.AddSeconds(35);while(DateTime.UtcNow<deadline&&!File.Exists(args[5])&&runtime.Alive)Thread.Sleep(100);}return 0;
  }
  string fixture=Path.Combine(Path.GetTempPath(),"PrismGlass-stream-address-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(fixture,"runtime-data","companion"));
  try{
   string file=Path.Combine(fixture,"runtime-data","companion","screen-stream.json"),tool=System.Reflection.Assembly.GetExecutingAssembly().Location;
   string saved=new JavaScriptSerializer().Serialize(new{enabled=true,address="10.255.254.253",ffmpeg=tool,mediamtx=tool});File.WriteAllText(file,saved);
   string active="127.0.0.1";using(var runtime=new CompanionStreamRuntime(fixture,()=>active)){
    Check(ReadAddress(runtime)==active,"running companion address overrides stale stream configuration");
    Check(File.ReadAllText(file)==saved,"following the active connection preserves saved streaming settings");
    active="";RejectAddress(runtime,"stopped companion never falls back to the previous saved address");
    active="8.8.8.8";RejectAddress(runtime,"active public address cannot become a streaming endpoint");
    active="10.255.254.253";RejectAddress(runtime,"disconnected private address is rejected before starting tools");
    active="127.0.0.1";Check(ReadAddress(runtime)==active,"reconnected endpoint is read again without recreating the runtime");
   }
   File.WriteAllText(file,new JavaScriptSerializer().Serialize(new{enabled=true,address="127.0.0.1",ffmpeg=tool,mediamtx=tool}));
   using(var runtime=new CompanionStreamRuntime(fixture)){Check(ReadAddress(runtime)=="127.0.0.1","standalone loopback diagnostics still use their configured local address");}
  }finally{Directory.Delete(fixture,true);}
  var mapping=new CompanionMonitorSelection();CompanionPhysicalMonitor a=Monitor("target-a|DISPLAY1|adapter/1","DISPLAY1"),b=Monitor("target-b|DISPLAY2|adapter/2","DISPLAY2"),c=Monitor("target-c|DISPLAY3|adapter/3","DISPLAY3");
  var initial=new List<CompanionPhysicalMonitor>{a,b,c};RejectSelection(()=>mapping.Resolve(0,initial),"capture rejects a monitor ID that was never advertised");
  mapping.Publish(initial);int selectedA=a.Id,selectedB=b.Id,selectedC=c.Id;
  var reordered=new List<CompanionPhysicalMonitor>{c,a,b};mapping.Publish(reordered);
  Check(mapping.Resolve(selectedB,reordered)==b&&a.Id==selectedA&&c.Id==selectedC,"physical display reorder and primary changes cannot remap an advertised ID");
  var unplugged=new List<CompanionPhysicalMonitor>{a,c};mapping.Publish(unplugged);
  RejectSelection(()=>mapping.Resolve(selectedB,unplugged),"unplugged selection is rejected instead of capturing the next screen");
  var replacement=Monitor("different-target|DISPLAY2|adapter/2","DISPLAY2");unplugged.Add(replacement);mapping.Publish(unplugged);
  Check(replacement.Id!=selectedB,"reused DISPLAY number with different hardware receives a new ID");
  RejectSelection(()=>mapping.Resolve(selectedB,unplugged),"cached selection cannot capture a replacement display on the same source");
  var returned=Monitor("TARGET-B|DISPLAY2|ADAPTER/2","DISPLAY2");unplugged.Add(returned);mapping.Publish(unplugged);
  Check(returned.Id==selectedB&&mapping.Resolve(selectedB,unplugged)==returned,"same hardware identity can reconnect without case-sensitive remapping");
  var currentSource=Monitor("target-b|DISPLAY2|adapter/7","DISPLAY2");var switched=new List<CompanionPhysicalMonitor>{currentSource};mapping.Publish(switched);
  RejectSelection(()=>mapping.Resolve(selectedB,switched),"changed display source requires selecting the refreshed display");
  var many=new List<CompanionPhysicalMonitor>();for(int i=0;i<17;i++)many.Add(Monitor("display-"+i,"DISPLAY"+i));
  var bounded=new CompanionMonitorSelection();Check(bounded.Publish(many).Count==16,"one metadata response advertises no more than sixteen physical displays");
  RejectSelection(()=>bounded.Resolve(16,many),"unadvertised seventeenth screen cannot be selected");
  bounded.Publish(new List<CompanionPhysicalMonitor>{many[16]});Check(many[16].Id==16&&bounded.Resolve(16,many)==many[16],"historical IDs above fifteen remain usable after a topology change");
  Check(new CompanionMonitorSelection().Publish(new List<CompanionPhysicalMonitor>{Monitor(null,"DISPLAY1")}).Count==0,"display without a hardware identity is not offered for capture");
  a.Bounds=new System.Drawing.Rectangle(0,0,1920,1080);var live=Monitor(a.Identity,a.DeviceName);live.Bounds=a.Bounds;
  Check(CompanionMonitorSelection.SameCapture(a,live),"unchanged physical capture identity and rectangle stay valid");
  live.Bounds=new System.Drawing.Rectangle(1920,0,1920,1080);Check(!CompanionMonitorSelection.SameCapture(a,live),"moving the captured physical screen invalidates its old capture rectangle");
  live.Bounds=new System.Drawing.Rectangle(0,0,2560,1440);Check(!CompanionMonitorSelection.SameCapture(a,live),"resizing the captured physical screen invalidates its old capture rectangle");
  live.Bounds=a.Bounds;live.Identity=b.Identity;Check(!CompanionMonitorSelection.SameCapture(a,live),"replacement screen at the same coordinates cannot keep an old capture alive");
  Check(!CompanionMonitorSelection.SameCapture(a,null),"disconnected capture source cannot stay live");
  var backend=new Fake();bool locked=false;var now=DateTime.UtcNow;var first=new CompanionGrant{Active=true,screen=true};var second=new CompanionGrant{Active=true,screen=true};
  using(var stream=new CompanionStream(backend,()=>locked,()=>now,false)){
   Check(stream.Handle("/api/screen/begin",Offer(),new CompanionGrant{Active=true}).Status==403&&backend.starts==0,"old pairings do not inherit screen access");
   var bad=Offer();bad["monitor"]=-1;Check(stream.Handle("/api/screen/begin",bad,first).Status==400&&backend.starts==0,"invalid monitor cannot start capture");
   bad=Offer();bad["monitor"]=4096;Check(stream.Handle("/api/screen/begin",bad,first).Status==400&&backend.starts==0,"monitor identifier above the history bound is rejected");
   bad=Offer();bad["mode"]="unknown";Check(stream.Handle("/api/screen/begin",bad,first).Status==400&&backend.starts==0,"unknown screen mode cannot start capture");
   bad=Offer();bad["mode"]="extend";Check(stream.Handle("/api/screen/begin",bad,first).Status==400&&backend.starts==0,"extension does not accept a physical monitor identifier");
   bad=Offer();bad["quality"]="2160";Check(stream.Handle("/api/screen/begin",bad,first).Status==400,"quality is a fixed allowlist");
   bad=Offer();bad["offer"]="v=0\r\n"+new string('a',50000);Check(stream.Handle("/api/screen/begin",bad,first).Status==400,"offer size is bounded");
   var started=stream.Handle("/api/screen/begin",Offer(),first);Check(started.Status==200&&backend.Alive&&backend.mode=="mirror"&&backend.monitor==0,"authorized viewer starts physical screen capture");var lease=Lease(started);
   Check(stream.Handle("/api/screen/begin",Offer(),second).Status==409&&backend.starts==1,"another viewer cannot steal capture");
   Check(stream.Handle("/api/screen/end",lease,second).Status==409&&backend.Alive,"lease belongs to its authenticated grant");
   now=now.AddSeconds(10);Check(stream.Handle("/api/screen/ping",lease,first).Status==200,"heartbeat renews the active lease");now=now.AddSeconds(14);stream.Sweep();Check(backend.Alive,"valid heartbeat preserves capture");now=now.AddSeconds(2);stream.Sweep();Check(!backend.Alive,"missing heartbeat stops capture within fifteen seconds");
   started=stream.Handle("/api/screen/begin",Offer(),first);locked=true;stream.Sweep();Check(!backend.Alive,"computer lock stops capture");locked=false;
   started=stream.Handle("/api/screen/begin",Offer(),first);first.Active=false;stream.Sweep();Check(!backend.Alive,"grant replacement or revocation stops capture");first.Active=true;
   backend.duringStart=()=>first.Active=false;Check(stream.Handle("/api/screen/begin",Offer(),first).Status==423&&!backend.Alive,"revocation during encoder startup never returns a live stream");first.Active=true;backend.duringStart=null;
   started=stream.Handle("/api/screen/begin",Offer(),first);Check(stream.Handle("/api/screen/end",Lease(started),first).Status==200&&!backend.Alive,"explicit stop tears down capture");
   var extension=Offer();extension["mode"]="extend";extension["monitor"]=-1;started=stream.Handle("/api/screen/begin",extension,first);Check(started.Status==200&&backend.mode=="extend"&&backend.monitor==-1,"explicit extension mode starts the managed virtual display");
   now=now.AddSeconds(16);stream.Sweep();Check(!backend.Alive,"extension lease expiry tears down its runtime");
   var legacy=Offer();legacy.Remove("mode");started=stream.Handle("/api/screen/begin",legacy,first);Check(started.Status==200&&backend.mode=="mirror","cached old pages can only request physical mirroring");stream.Stop();
   var history=Offer();history["monitor"]=4095;started=stream.Handle("/api/screen/begin",history,first);Check(started.Status==200&&backend.monitor==4095,"protocol accepts bounded historical display IDs");stream.Stop();
   started=stream.Handle("/api/screen/begin",extension,first);backend.failStop=true;Check(stream.Handle("/api/screen/end",Lease(started),first).Status==503&&backend.Alive&&stream.IsActive,"failed cleanup keeps the screen active until shutdown is confirmed");stream.Sweep();Check(!backend.Alive&&!stream.IsActive,"watchdog retries cleanup even after the viewer lease was cleared");
   backend.duringStart=()=>{throw new IOException();};Check(stream.Handle("/api/screen/begin",Offer(),first).Status==503&&!backend.Alive,"failed startup leaves no capture process");
  }
  string config=CompanionStreamRuntime.RouterConfig("127.0.0.1");Check(config.Contains("webrtcAddress: 127.0.0.1:")&&config.Contains("rtspAddress: 127.0.0.1:")&&config.Contains("webrtcICEServers2: []")&&config.Contains("record: false"),"signaling stays local, no public relay or recording");
  Console.WriteLine("Companion stream: "+checks+" checks passed.");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
