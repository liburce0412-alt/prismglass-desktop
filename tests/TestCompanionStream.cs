using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

internal static class TestCompanionStream {
 sealed class Fake : ICompanionStreamRuntime {
  public bool Alive {get;set;} public int starts,stops;public Action duringStart;
  public object Describe(){return new{available=true};}
  public string Start(int monitor,string quality,string offer){starts++;Alive=true;if(duringStart!=null)duringStart();return "v=0\r\n";}
  public void Stop(){stops++;Alive=false;}public void Dispose(){Stop();}
 }
 static int checks;
 static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static Dictionary<string,object> Offer(){return new Dictionary<string,object>{{"monitor",0},{"quality","720"},{"offer","v=0\r\n"}};}
 static Dictionary<string,object> Lease(CompanionReply reply){var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(reply.Body));return new Dictionary<string,object>{{"lease",data["lease"]}};}
 static int Main(string[] args){try{
  if(args.Length>0&&args[0]=="--integration"){
   using(var runtime=new CompanionStreamRuntime(args[1])){string answer=runtime.Start(0,args[2],File.ReadAllText(args[3]));File.WriteAllText(args[4],answer);var deadline=DateTime.UtcNow.AddSeconds(35);while(DateTime.UtcNow<deadline&&!File.Exists(args[5])&&runtime.Alive)Thread.Sleep(100);}return 0;
  }
  var backend=new Fake();bool locked=false;var now=DateTime.UtcNow;var first=new CompanionGrant{Active=true,screen=true};var second=new CompanionGrant{Active=true,screen=true};
  using(var stream=new CompanionStream(backend,()=>locked,()=>now,false)){
   Check(stream.Handle("/api/screen/begin",Offer(),new CompanionGrant{Active=true}).Status==403&&backend.starts==0,"old pairings do not inherit screen access");
   var bad=Offer();bad["monitor"]=-1;Check(stream.Handle("/api/screen/begin",bad,first).Status==400&&backend.starts==0,"invalid monitor cannot start capture");
   bad=Offer();bad["quality"]="2160";Check(stream.Handle("/api/screen/begin",bad,first).Status==400,"quality is a fixed allowlist");
   bad=Offer();bad["offer"]="v=0\r\n"+new string('a',50000);Check(stream.Handle("/api/screen/begin",bad,first).Status==400,"offer size is bounded");
   var started=stream.Handle("/api/screen/begin",Offer(),first);Check(started.Status==200&&backend.Alive,"authorized viewer starts capture");var lease=Lease(started);
   Check(stream.Handle("/api/screen/begin",Offer(),second).Status==409&&backend.starts==1,"another viewer cannot steal capture");
   Check(stream.Handle("/api/screen/end",lease,second).Status==409&&backend.Alive,"lease belongs to its authenticated grant");
   now=now.AddSeconds(10);Check(stream.Handle("/api/screen/ping",lease,first).Status==200,"heartbeat renews the active lease");now=now.AddSeconds(14);stream.Sweep();Check(backend.Alive,"valid heartbeat preserves capture");now=now.AddSeconds(2);stream.Sweep();Check(!backend.Alive,"missing heartbeat stops capture within fifteen seconds");
   started=stream.Handle("/api/screen/begin",Offer(),first);locked=true;stream.Sweep();Check(!backend.Alive,"computer lock stops capture");locked=false;
   started=stream.Handle("/api/screen/begin",Offer(),first);first.Active=false;stream.Sweep();Check(!backend.Alive,"grant replacement or revocation stops capture");first.Active=true;
   backend.duringStart=()=>first.Active=false;Check(stream.Handle("/api/screen/begin",Offer(),first).Status==423&&!backend.Alive,"revocation during encoder startup never returns a live stream");first.Active=true;backend.duringStart=null;
   started=stream.Handle("/api/screen/begin",Offer(),first);Check(stream.Handle("/api/screen/end",Lease(started),first).Status==200&&!backend.Alive,"explicit stop tears down capture");
   backend.duringStart=()=>{throw new IOException();};Check(stream.Handle("/api/screen/begin",Offer(),first).Status==503&&!backend.Alive,"failed startup leaves no capture process");
  }
  string config=CompanionStreamRuntime.RouterConfig("127.0.0.1");Check(config.Contains("webrtcAddress: 127.0.0.1:")&&config.Contains("rtspAddress: 127.0.0.1:")&&config.Contains("webrtcICEServers2: []")&&config.Contains("record: false"),"signaling stays local, no public relay or recording");
  Console.WriteLine("Companion stream: "+checks+" checks passed.");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
