using System;using System.IO;using System.Text;using System.Linq;using System.Net;using System.Net.Http;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Collections.Generic;
internal static class TestDockLyrics {
 static int checks;static string root;static DateTime now=new DateTime(2026,10,20,8,0,0,DateTimeKind.Utc);static readonly JavaScriptSerializer json=new JavaScriptSerializer();
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 sealed class FakeHttp:HttpMessageHandler{
  public int calls;public readonly List<TaskCompletionSource<HttpResponseMessage>> pending=new List<TaskCompletionSource<HttpResponseMessage>>();
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){calls++;var t=new TaskCompletionSource<HttpResponseMessage>();lock(pending)pending.Add(t);return t.Task;}
  public void Complete(int index,string lrc){pending[index].SetResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json.Serialize(new{syncedLyrics=lrc}))});}
  public void Fail(int index){pending[index].SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable){Content=new StringContent("")});}
 }
 static MediaSnapshot Session(string name,double position=1){return new MediaSnapshot{source="TestPlayer",title=name,artist="Test artist",album="Test album",duration=120,position=position,status="playing",timeline=true,sampledAt=MediaSnapshot.Unix(now)};}
 static void Write(MediaSnapshot m){File.WriteAllText(Path.Combine(root,"..","music-data","session.json"),json.Serialize(m));}
 static string[] Output(){return File.ReadAllLines(Path.Combine(root,"dock-lyrics.txt"));}
 static bool Showing(){return Output()[0].Split('|')[1]=="1";}
 static string Current(){return Encoding.UTF8.GetString(Convert.FromBase64String(Output()[1]));}
 static void Step(DockLyrics service,MediaSnapshot m,bool visible=true,bool near=false){now=now.AddMilliseconds(300);if(m!=null){m.sampledAt=MediaSnapshot.Unix(now);Write(m);}service.Tick(now,visible,near,false);}
 static void Wait(Func<bool> condition){for(int i=0;i<200;i++){if(condition())return;Thread.Sleep(10);}throw new Exception("Async operation did not complete");}
 static void Pump(DockLyrics service,MediaSnapshot m){for(int i=0;i<15;i++){Thread.Sleep(10);Step(service,m);}}
 static void Main(string[] args){
  root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"..","music-data"));
  var lrc=LyricsTimeline.Parse("[offset:100]\n[00:01.20]第一句\n[00:02.123][00:06]second\n[00:02.123]译文\n[00:04]\n[00:03.5]third");
  Check(lrc.Count==5,"multiple timestamps, translations and blank instrumental markers");
  Check(Math.Abs(lrc[0].time-1.1)<.0001,"positive LRC offset advances lyrics");
  Check(LyricsTimeline.At(lrc,.5)[0]=="","intro does not display future lyrics");
  Check(LyricsTimeline.At(lrc,2.5)[0]=="second · 译文","equal timestamps merge instead of flicker");
  Check(LyricsTimeline.At(lrc,4.2)[0]=="","blank instrumental section restores icons");
  Check(LyricsTimeline.At(lrc,3.6)[0]=="third","backward seek uses correct line");
  Check(LyricsTimeline.Parse("plain unsynced lyrics").Count==0,"plain text never gets invented timestamps");
  Check(LyricsTimeline.DisplayText("#ROOT#[Measure]")=="＃ROOT＃［Measure］","remote lyric text cannot expand Rainmeter variables");
  var s=Session("one",10);Check(Math.Abs(s.Position(now.AddSeconds(2))-12)<.001,"playing timeline interpolates from sample");
  s.status="paused";Check(s.Position(now.AddSeconds(2))==10,"paused timeline stays still");
  Check(!s.Fresh(now.AddSeconds(5)),"stale media snapshots expire");
  Check(NetEaseSession.ValidClock(32,120.02,120),"NetEase clock matches exact queue duration");
  Check(!NetEaseSession.ValidClock(32,240,120),"mismatched NetEase clock is rejected");
  Check(!NetEaseSession.ValidClock(Double.NaN,120,120)&&!NetEaseSession.ValidClock(Double.PositiveInfinity,120,120),"invalid native numbers never become lyrics");
  Check(!NetEaseSession.ValidClock(-1,120,120)&&!NetEaseSession.ValidClock(121,120,120),"native position must remain within track");
  Check(NetEaseSession.Advancing(10,10.4,.4),"real advancing native clock marks playing");
  Check(!NetEaseSession.Advancing(10,10,.4),"frozen native clock marks paused");
  Check(!NetEaseSession.Advancing(10,40,.4)&&!NetEaseSession.Advancing(40,10,.4),"seeking alone is not mistaken for playing");
  Check(!NetEaseSession.Advancing(Double.NaN,20,.4)&&!NetEaseSession.Advancing(20,24,5),"startup and stale native samples require resynchronization");
  var clock=new NetEaseSession.PlaybackClock();
  Check(!clock.Observe(10,now)&&clock.Observe(10.4,now.AddSeconds(.4)),"native clock requires actual progress before playing");
  Check(clock.Observe(10.4,now.AddSeconds(.8))&&clock.Observe(10.4,now.AddSeconds(1.2)),"brief unchanged readings keep lyrics stable");
  Check(!clock.Observe(10.4,now.AddSeconds(1.7)),"sustained pause still restores the Dock");
  Check(!clock.Observe(40,now.AddSeconds(2.1))&&clock.Observe(40.4,now.AddSeconds(2.5)),"paused seek cannot fake playback; real resume works");
  string nativeFile=Path.Combine(root,"..","music-data","netease-session.json");
  var native=Session("native",20);native.source="cloudmusic.exe";File.WriteAllText(nativeFile,json.Serialize(native));Write(s);
  Check(MediaSnapshot.Read(root,now).title=="native","playing NetEase replaces a paused system session");
  s.status="playing";Write(s);Check(MediaSnapshot.Read(root,now).title=="one","active system session retains priority");
  s.timeline=false;Write(s);Check(MediaSnapshot.Read(root,now).title=="native","NetEase replaces a session without a usable timeline");
  native.sampledAt-=5;File.WriteAllText(nativeFile,json.Serialize(native));Check(MediaSnapshot.Read(root,now).title=="one","stale native playback is ignored");
  File.Delete(nativeFile);
  var settings=new LyricsSettings{enabled=true,online=true};settings.Save(root);
  settings.appearance=new LyricsAppearance{font="kai",layout="split",texture="shimmer",effect="wave",color="#FFEECC",fontSize=99,width=20};settings.Save(root);
  var loaded=LyricsSettings.Load(root);
  Check(loaded.enabled&&loaded.online&&loaded.holdGaps&&loaded.appearance.font=="kai"&&loaded.appearance.color=="#ffeecc"&&loaded.appearance.fontSize==32&&loaded.appearance.width==420,"appearance persists and clamps unsafe geometry without losing switches");
  var invalid=new LyricsAppearance{font="url(bad)",effect="bad",color="red;display:none"}.Normalize();
  Check(invalid.font=="sans"&&invalid.effect=="float"&&invalid.color=="#ffffff","appearance accepts only safe supported values");
  settings.appearance=new LyricsAppearance();settings.Save(root);
  var http=new FakeHttp();
  using(var service=new DockLyrics(root,http,false)){
   s=Session("A");Step(service,s);Wait(()=>http.calls==1);
   s=Session("B");Step(service,s);Wait(()=>http.calls==2);
   http.Complete(0,"[00:00]WRONG OLD SONG");Pump(service,s);
   Check(!Showing(),"late response from previous track cannot replace current lyrics");
   http.Complete(1,"[00:00]B first\n[00:10]B second\n[00:20]\n[00:25]B last");Pump(service,s);
   Check(Showing()&&Current()=="B first","resolved current track appears");
   string sessionFile=Path.Combine(root,"..","music-data","session.json");
   using(var locked=new FileStream(sessionFile,FileMode.Open,FileAccess.Read,FileShare.None)){Step(service,null);Check(Showing()&&Current()=="B first","brief unreadable session keeps the fresh lyric instead of flickering");}
   string settingsFile=Path.Combine(root,"lyrics-settings.json");
   using(var locked=new FileStream(settingsFile,FileMode.Open,FileAccess.Read,FileShare.None)){Step(service,s);Check(Showing(),"brief settings read failure does not disable lyrics");}
   Step(service,s,true,true);Check(!Showing(),"pointer approach restores applications immediately");
   Step(service,s);Check(!Showing(),"short leave avoids oscillation");
   Step(service,s);Step(service,s);Check(Showing(),"lyrics return after pointer settles away");
   Step(service,s,false);Check(!Showing(),"covered Dock never displays lyrics");
   s.status="paused";Step(service,s);Check(!Showing(),"pause restores icons");
   s.status="playing";s.position=12;Step(service,s);Check(Current()=="B second","forward seek replaces current line");
   s.position=1;Step(service,s);Check(Current()=="B first","backward seek replaces current line");
   s.position=22;Step(service,s);Check(Showing()&&Current()=="B","instrumental break keeps the capsule with track information");
   settings.holdGaps=false;settings.Save(root);Step(service,s);Check(!Showing(),"users can still restore applications during instrumental breaks");
   settings.holdGaps=true;settings.Save(root);
   s.position=120;Step(service,s);Check(!Showing(),"finished timeline never retains the last lyric forever");
   s.position=9.9;settings.offsetMs=200;settings.Save(root);Step(service,s);Check(Current()=="B second","manual timing adjustment advances line");
   settings.online=false;settings.Save(root);Step(service,s);Check(Showing()&&http.calls==2,"cached lyrics work offline without new requests");
   now=now.AddSeconds(5);service.Tick(now,true,false,false);Check(!Showing(),"lost media heartbeat restores icons");
   s=Session("C");Step(service,s);Check(!Showing()&&http.calls==2,"uncached offline track leaves Dock usable");
   settings.enabled=false;settings.Save(root);Step(service,s);Check(!Showing(),"disable takes effect without restarting desktop");
  }
  Check(!Showing(),"dispose restores icons");
  settings.enabled=true;settings.online=false;settings.offsetMs=0;settings.Save(root);
  using(var service=new DockLyrics(root,new FakeHttp(),false)){s=Session("B");Step(service,s);Check(Showing(),"bounded disk cache survives restart offline");}
  settings.online=true;settings.Save(root);http=new FakeHttp();
  using(var service=new DockLyrics(root,http,false)){
   s=Session("Network failure");Step(service,s);Wait(()=>http.calls==1);http.Fail(0);Pump(service,s);
   Check(!Showing()&&File.ReadAllText(Path.Combine(root,"lyrics-status.txt")).Contains("暂不可用"),"server failure is distinguished from missing lyrics");
   now=now.AddSeconds(31);Step(service,s);Wait(()=>http.calls==2);
   settings.online=false;settings.Save(root);Step(service,s);http.Complete(1,"[00:00]Late after opt-out");Pump(service,s);
   Check(!Showing()&&http.calls==2,"disabling online matching discards an in-flight result");
  }
  Console.WriteLine("Completed "+checks+" lyric behavior checks.");
 }
}
