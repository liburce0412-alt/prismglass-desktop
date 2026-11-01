using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

internal static class TestCompanionStream {
 sealed class Fake : ICompanionStreamRuntime,ICompanionAudioRuntime {
  public bool AudioAlive {get;set;}public bool failAudio;public int audioStarts;
  public string StartAudio(string offer){audioStarts++;if(failAudio)throw new IOException();AudioAlive=true;return "v=0\r\n";}public void StopAudio(){AudioAlive=false;}
  public System.Drawing.Rectangle CaptureBounds {get{return new System.Drawing.Rectangle(-1280,0,1280,720);}} public string Instance {get{return "test-instance";}}
  public bool Alive {get;set;} public int starts,stops;public Action duringStart;public string mode;public int monitor;public bool failStop,portrait;
  public object Describe(){return new{available=true};}
  public string Start(string mode,int monitor,string quality,string offer,bool portrait=false){this.mode=mode;this.monitor=monitor;this.portrait=portrait;starts++;Alive=true;if(duringStart!=null)duringStart();return "v=0\r\n";}
  public void Stop(){StopAudio();stops++;if(failStop){failStop=false;throw new IOException();}Alive=false;}public void Dispose(){Stop();}
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
   var stopped=new List<CompanionGrant>();stream.Stopped=grant=>stopped.Add(grant);
   Check(stream.Handle("/api/screen/begin",Offer(),new CompanionGrant{Active=true}).Status==403&&backend.starts==0,"old pairings do not inherit screen access");
   var bad=Offer();bad["monitor"]=-1;Check(stream.Handle("/api/screen/begin",bad,first).Status==400&&backend.starts==0,"invalid monitor cannot start capture");
   bad=Offer();bad["monitor"]=4096;Check(stream.Handle("/api/screen/begin",bad,first).Status==400&&backend.starts==0,"monitor identifier above the history bound is rejected");
   bad=Offer();bad["mode"]="unknown";Check(stream.Handle("/api/screen/begin",bad,first).Status==400&&backend.starts==0,"unknown screen mode cannot start capture");
   bad=Offer();bad["mode"]="extend";Check(stream.Handle("/api/screen/begin",bad,first).Status==400&&backend.starts==0,"extension does not accept a physical monitor identifier");
   bad=Offer();bad["quality"]="2160";Check(stream.Handle("/api/screen/begin",bad,first).Status==400,"quality is a fixed allowlist");
   bad=Offer();bad["portrait"]="true";Check(stream.Handle("/api/screen/begin",bad,first).Status==400&&backend.starts==0,"orientation requires a boolean before capture starts");
   bad=Offer();bad["offer"]="v=0\r\n"+new string('a',50000);Check(stream.Handle("/api/screen/begin",bad,first).Status==400,"offer size is bounded");
   bad=Offer();bad["instance"]="old-process";Check(stream.Handle("/api/screen/begin",bad,first).Status==409&&backend.starts==0,"stale process identity cannot capture a reassigned monitor ID");
   var started=stream.Handle("/api/screen/begin",Offer(),first);Check(started.Status==200&&backend.Alive&&backend.mode=="mirror"&&backend.monitor==0,"authorized viewer starts physical screen capture");var lease=Lease(started);
   var audioOffer=new Dictionary<string,object>{{"lease",lease["lease"]},{"sequence",1},{"offer","v=0\r\n"}};
   Check(stream.Handle("/api/screen/audio/begin",audioOffer,first).Status==403&&backend.audioStarts==0,"screen permission never implicitly captures system sound");
   first.audio=true;Check(stream.Handle("/api/screen/audio/begin",audioOffer,first).Status==200&&backend.AudioAlive,"explicit audio permission starts the separate stream");
   first.audio=false;stream.Sweep();Check(!backend.AudioAlive&&backend.Alive,"audio revocation stops capture while retaining authorized video");
   first.audio=true;audioOffer["sequence"]=2;backend.failAudio=true;Check(stream.Handle("/api/screen/audio/begin",audioOffer,first).Status==503&&backend.Alive&&!backend.AudioAlive,"audio startup failure never tears down video");backend.failAudio=false;
   audioOffer["sequence"]=3;stream.Handle("/api/screen/audio/begin",audioOffer,first);var audioEnd=new Dictionary<string,object>{{"lease",lease["lease"]},{"sequence",4}};Check(stream.Handle("/api/screen/audio/end",audioEnd,first).Status==200&&!backend.AudioAlive&&backend.Alive,"explicit audio stop leaves video running");
   Check(stream.Handle("/api/screen/audio/begin",audioOffer,first).Status==409&&!backend.AudioAlive,"late audio begin cannot undo its stop");
   audioOffer["sequence"]=5;stream.Handle("/api/screen/audio/begin",audioOffer,first);stream.Handle("/api/screen/audio/end",audioEnd,first);Check(backend.AudioAlive,"late stop cannot close a newer audio connection");
   var audioPing=new Dictionary<string,object>{{"lease",lease["lease"]},{"audioSequence",5},{"audioEnabled",true}};
   now=now.AddSeconds(10);stream.Handle("/api/screen/ping",audioPing,first);now=now.AddSeconds(10);stream.Sweep();Check(backend.AudioAlive&&backend.Alive,"matching audio heartbeat renews both streams");
   stream.Handle("/api/screen/ping",lease,first);now=now.AddSeconds(6);stream.Sweep();Check(!backend.AudioAlive&&backend.Alive,"video-only heartbeat cannot keep abandoned audio alive");
   audioPing["audioSequence"]=6;stream.Handle("/api/screen/ping",audioPing,first);audioOffer["sequence"]=6;Check(stream.Handle("/api/screen/audio/begin",audioOffer,first).Status==200,"heartbeat arriving before begin does not consume the audio sequence");
   audioPing["audioEnabled"]=false;audioPing["audioSequence"]=4;stream.Handle("/api/screen/ping",audioPing,first);Check(backend.AudioAlive,"old heartbeat cannot close newer audio");audioPing["audioSequence"]=7;stream.Handle("/api/screen/ping",audioPing,first);Check(!backend.AudioAlive&&backend.Alive,"heartbeat confirms audio stop when end response was lost");
   int points=0;Func<System.Drawing.Rectangle,CompanionReply> point=area=>{points++;return CompanionReply.Json(200,new{ok=true});};
   Check(stream.WithCapture(second,(string)lease["lease"],point).Status==409&&points==0,"another device cannot use a screen lease for point input");
   Check(stream.WithCapture(first,"old-lease",point).Status==409&&points==0,"stale screen lease cannot inject a point");
   Check(stream.WithCapture(first,(string)lease["lease"],point).Status==200&&points==1,"current screen owner can map a point within the locked capture session");
   Check(stream.Handle("/api/screen/begin",Offer(),second).Status==409&&backend.starts==1,"another viewer cannot steal capture");
   Check(stream.Handle("/api/screen/end",lease,second).Status==409&&backend.Alive,"lease belongs to its authenticated grant");
   now=now.AddSeconds(10);Check(stream.Handle("/api/screen/ping",lease,first).Status==200,"heartbeat renews the active lease");now=now.AddSeconds(14);stream.Sweep();Check(backend.Alive,"valid heartbeat preserves capture");now=now.AddSeconds(2);stream.Sweep();Check(!backend.Alive,"missing heartbeat stops capture within fifteen seconds");
   started=stream.Handle("/api/screen/begin",Offer(),first);locked=true;stream.Sweep();Check(!backend.Alive,"computer lock stops capture");locked=false;
   started=stream.Handle("/api/screen/begin",Offer(),first);first.Active=false;stream.Sweep();Check(!backend.Alive,"grant replacement or revocation stops capture");first.Active=true;
   backend.duringStart=()=>first.Active=false;Check(stream.Handle("/api/screen/begin",Offer(),first).Status==423&&!backend.Alive,"revocation during encoder startup never returns a live stream");first.Active=true;backend.duringStart=null;
   started=stream.Handle("/api/screen/begin",Offer(),first);Check(stream.Handle("/api/screen/end",Lease(started),first).Status==200&&!backend.Alive,"explicit stop tears down capture");
   var extension=Offer();extension["mode"]="extend";extension["monitor"]=-1;started=stream.Handle("/api/screen/begin",extension,first);Check(started.Status==200&&backend.mode=="extend"&&backend.monitor==-1,"explicit extension mode starts the managed virtual display");
   Check(!backend.portrait,"existing pages retain the landscape default");stream.Stop();extension["portrait"]=true;
   started=stream.Handle("/api/screen/begin",extension,first);Check(started.Status==200&&backend.portrait,"portrait extension intent reaches the display runtime");
   now=now.AddSeconds(16);stream.Sweep();Check(!backend.Alive,"extension lease expiry tears down its runtime");
   var legacy=Offer();legacy.Remove("mode");started=stream.Handle("/api/screen/begin",legacy,first);Check(started.Status==200&&backend.mode=="mirror","cached old pages can only request physical mirroring");stream.Stop();
   var history=Offer();history["monitor"]=4095;started=stream.Handle("/api/screen/begin",history,first);Check(started.Status==200&&backend.monitor==4095,"protocol accepts bounded historical display IDs");stream.Stop();
   started=stream.Handle("/api/screen/begin",extension,first);backend.failStop=true;Check(stream.Handle("/api/screen/end",Lease(started),first).Status==503&&backend.Alive&&stream.IsActive,"failed cleanup keeps the screen active until shutdown is confirmed");stream.Sweep();Check(!backend.Alive&&!stream.IsActive,"watchdog retries cleanup even after the viewer lease was cleared");
   Check(stopped.Count==backend.starts&&stopped.TrueForAll(grant=>ReferenceEquals(grant,first)),"every ended screen releases its owner input exactly once, including failed cleanup");
   int notifications=stopped.Count;stream.Stop();Check(stopped.Count==notifications,"idle cleanup never ends another input session");
   backend.duringStart=()=>{throw new IOException();};Check(stream.Handle("/api/screen/begin",Offer(),first).Status==503&&!backend.Alive,"failed startup leaves no capture process");
  }
  string config=CompanionStreamRuntime.RouterConfig("127.0.0.1");Check(config.Contains("webrtcAddress: 127.0.0.1:")&&config.Contains("rtspAddress: 127.0.0.1:")&&config.Contains("webrtcICEServers2: []")&&config.Contains("record: false"),"signaling stays local, no public relay or recording");
  Console.WriteLine("Companion stream: "+checks+" checks passed.");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
