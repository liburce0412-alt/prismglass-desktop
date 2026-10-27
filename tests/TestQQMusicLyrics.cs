using System;using System.IO;using System.Text;using System.Net;using System.Net.Http;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Collections.Generic;
internal static class TestQQMusicLyrics {
 static readonly JavaScriptSerializer json=new JavaScriptSerializer();static int checks;
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static object Candidate(string mid="00123456789abc",string title="Fixture song",string artist="Fixture singer",string album="Fixture album",int duration=120){return new{mid=mid,title=title,name="Fixture song",interval=duration,album=new{name=album},singer=new[]{new{name=artist}},grp=new object[0]};}
 static string Search(params object[] songs){return json.Serialize(new Dictionary<string,object>{{"code",0},{"music.search.SearchCgiService",new{code=0,data=new{body=new{song=new{list=songs}}}}}});}
 static MediaSnapshot Song(){return new MediaSnapshot{source="QQMusic.exe",smtc=true,title="Fixture song",artist="Fixture singer",album="Fixture album",duration=120.2,position=2,status="playing",timeline=true};}
 sealed class QQHttp:HttpMessageHandler {
  public int calls;public bool unexpected;public object[] candidates=new[]{Candidate()};public readonly List<string> requests=new List<string>();
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   calls++;string body=request.Content==null?"":request.Content.ReadAsStringAsync().Result;requests.Add(request.RequestUri.AbsoluteUri+" "+body);
   if(request.Headers.Contains("Cookie")||!request.RequestUri.Host.EndsWith(".qq.com",StringComparison.OrdinalIgnoreCase))unexpected=true;
   string result=request.Method==HttpMethod.Post?(body.Contains("SearchCgiService")?Search(candidates):json.Serialize(new{code=0,detail=new{code=0,data=new{track_info=Candidate(mid:(string)((Dictionary<string,object>)((Dictionary<string,object>)json.Deserialize<Dictionary<string,object>>(body)["detail"])["param"])["song_mid"])}}})):json.Serialize(new{code=0,retcode=0,lyric="[00:00]Original QQ fixture line\n[00:15]Next QQ fixture line"});
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(result,Encoding.UTF8,"application/json")});
  }
 }
 static void Step(DockLyrics service,string root,MediaSnapshot song,ref DateTime now){now=now.AddMilliseconds(300);song.sampledAt=MediaSnapshot.Unix(now);File.WriteAllText(Path.Combine(root,"..","music-data","session.json"),json.Serialize(song));service.Tick(now,true,false,false);}
 static bool Match(object candidate,MediaSnapshot song){return QQMusicLyrics.MatchesDetail(json.Serialize(new{code=0,detail=new{code=0,data=new{track_info=candidate}}}),"00123456789abc",song);}
 static void Main(string[] args){
  var song=Song();string mid="00123456789abc";
  Check(Match(Candidate(),song),"QQ candidate requires matching song, singer, album and quantized duration");
  Check(!Match(Candidate(title:"Fixture song (Live)"),song),"live version is not reduced to the original title");
  Check(!Match(Candidate(title:"Fixture song (Remix)"),song),"remix version is not reduced to the original title");
  Check(!Match(Candidate(artist:"Different singer"),song),"same-title cover by another singer is rejected");
  Check(!Match(Candidate(album:"Different album"),song),"different album edition is rejected");
  Check(!Match(Candidate(duration:126),song),"duration mismatch cannot be patched with a lyric offset");
  var ambiguous=new QQHttp{candidates=new[]{Candidate(),Candidate(mid:"00987654321abc")}};using(var client=new HttpClient(ambiguous)){Check(QQMusicLyrics.FetchAsync(client,song,CancellationToken.None).GetAwaiter().GetResult().lrc==""&&ambiguous.calls==3,"multiple verified QQ IDs remain ambiguous and never fetch lyrics");}
  var duplicate=new QQHttp{candidates=new[]{Candidate(),Candidate()}};using(var client=new HttpClient(duplicate)){Check(QQMusicLyrics.FetchAsync(client,song,CancellationToken.None).GetAwaiter().GetResult().mid==mid&&duplicate.calls==3,"repeated results for one QQ ID use one detail and lyric request");}
  Check(!Match(Candidate(mid:"../foreign?x=1"),song),"remote identifiers cannot become arbitrary request paths");
  song.album="";Check(!Match(Candidate(),song),"missing album does not silently weaken version matching");song=Song();
  song.artist="Fixture singer (Different guest)";Check(!Match(Candidate(),song),"unverified parenthetical artist names are not stripped");song=Song();
  string detail=json.Serialize(new{code=0,detail=new{code=0,data=new{track_info=Candidate()}}});
  Check(QQMusicLyrics.MatchesDetail(detail,mid,song),"selected QQ song details independently confirm the version");
  Check(!QQMusicLyrics.MatchesDetail(detail,"00987654321abc",song),"details for a different QQ ID are rejected");
  var aliasCandidate=new{mid=mid,title="Fixture song",interval=120,album=new{name="Fixture album"},singer=new[]{new{name="Fixture singer",title="Fixture singer (Official alias)"}}};
  string aliasDetail=json.Serialize(new{code=0,detail=new{code=0,data=new{track_info=aliasCandidate}}});song.artist="Fixture singer (Official alias)";
  Check(QQMusicLyrics.MatchesDetail(aliasDetail,mid,song),"artist alias is accepted only when the QQ song detail supplies it");
  song.artist="Fixture singer (Unknown alias)";Check(!QQMusicLyrics.MatchesDetail(aliasDetail,mid,song),"a different artist alias remains unverified");song=Song();
  Check(QQMusicLyrics.DecodeLyric(json.Serialize(new{code=0,retcode=0,lyric="[00:00]Original fixture\n[00:15]Second fixture"}),song.duration).Length>0,"QQ's timed original lyrics are accepted");
  Check(QQMusicLyrics.DecodeLyric(json.Serialize(new{code=1,retcode=0,lyric="[00:00]Rejected fixture"}),song.duration)=="","QQ API errors cannot be displayed as matched lyrics");
  Check(QQMusicLyrics.DecodeLyric(json.Serialize(new{code=0,retcode=0,lyric="plain fixture without time"}),song.duration)=="","plain lyrics cannot invent synchronization");
  Check(QQMusicLyrics.DecodeLyric(json.Serialize(new{code=0,retcode=0,lyric="[09:00]Outside this recording"}),song.duration)=="","lyric timestamps outside the recording are rejected");
  string root=Path.GetFullPath(args[0]),data=Path.GetFullPath(Path.Combine(root,"..","music-data"));Directory.CreateDirectory(root);Directory.CreateDirectory(data);
  var settings=new LyricsSettings{enabled=true,online=true};settings.Save(root);
  File.WriteAllText(Path.Combine(data,"lyrics-cache.json"),json.Serialize(new Dictionary<string,string>{{song.Key,"[00:00]Unverified old QQ cache"}}));
  var transport=new QQHttp();var now=DateTime.UtcNow;
  using(var service=new DockLyrics(root,transport,false)){
   for(int i=0;i<12;i++){Step(service,root,song,ref now);Thread.Sleep(10);}
   string[] output=File.ReadAllLines(Path.Combine(root,"dock-lyrics.txt"));string current=Encoding.UTF8.GetString(Convert.FromBase64String(output[1]));
   Check(current=="Original QQ fixture line"&&transport.calls==3,"QQ searches, verifies details and gets lyrics by ID instead of trusting old cache");
   Check(!transport.unexpected&&transport.requests[2].Contains(mid),"QQ lyrics are fetched anonymously by the verified song ID without LRCLIB fallback");
   var companion=json.Deserialize<Dictionary<string,object>>(json.Serialize(service.CompanionSnapshot(now)));
   Check((bool)companion["available"]&&(string)companion["current"]==current&&(string)companion["next"]=="Next QQ fixture line"&&(string)companion["source"]=="QQMusic.exe","desktop and companion share the verified QQ song and synchronized lyric lines");
  }
  settings.online=false;settings.Save(root);transport=new QQHttp();
  using(var service=new DockLyrics(root,transport,false)){
   Step(service,root,song,ref now);string[] output=File.ReadAllLines(Path.Combine(root,"dock-lyrics.txt"));
   Check(transport.calls==0&&Encoding.UTF8.GetString(Convert.FromBase64String(output[1]))=="Original QQ fixture line","verified QQ lyrics remain available offline without new requests");
  }
  Console.WriteLine("Completed "+checks+" QQ version matching checks.");
 }
}
