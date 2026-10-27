using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class TestAppLibrary {
 static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 static void Reject(Action operation,string name){bool rejected=false;try{operation();}catch{rejected=true;}Check(rejected,name);}
 [STAThread] static int Main(){
  string fixture=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"app-library-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
  try {
   string first=Path.Combine(fixture,"微信.txt"),second=Path.Combine(fixture,"另一文件.txt"),third=Path.Combine(fixture,"替换入口.txt");File.WriteAllText(first,"source unchanged");File.WriteAllText(second,"second");File.WriteAllText(third,"replacement");
   string map=Path.Combine(Environment.CurrentDirectory,"src","LiquidDesktop","app-center-pinyin.txt");if(File.Exists(map))File.Copy(map,Path.Combine(fixture,"app-center-pinyin.txt"));
   using(var library=new AppLibrary(fixture)){
    library.Add(first);library.Add(second);library.Add(first);var s=library.GetSnapshot();var entries=s.entries.Where(e=>e.kind!="action").ToList();Check(entries.Count==2,"manual add deduplicates by target");Check(entries.All(e=>e.exists),"adding does not invalidate prior entries");string a=entries.First(e=>e.path==first).id,b=entries.First(e=>e.path==second).id;
    Check(s.entries.Count(e=>e.kind=="action")==4,"system actions are explicit IDs");Check(entries.First(e=>e.id==a).search.Contains("weixin"),"offline Mandarin search index");Check(entries.First(e=>e.id==a).initials.StartsWith("wx"),"Mandarin initials");
    library.Apply("alias",b,"","网易云音乐 快乐");var music=library.GetSnapshot().entries.First(e=>e.id==b);Check(music.search.Contains("wangyiyunyinyue"),"music uses contextual yue pronunciation");Check(music.initials.Contains("wyyyy"),"music initials match the contextual pronunciation");Check(music.search.Contains("kuaile"),"other uses of le keep their original pronunciation");
    library.Apply("categoryCreate","","","我的分类");string category=library.GetSnapshot().categories.First(c=>c.name=="我的分类").id;library.Apply("move",a,category,"");library.Apply("move",b,category,"");library.Apply("group",a,b,"工具");s=library.GetSnapshot();string folder=s.folders.Single().id;Check(s.entries.First(e=>e.id==a).folder==folder&&s.entries.First(e=>e.id==b).folder==folder,"group commits two entries together");
    Reject(()=>library.Apply("move",folder,folder,""),"folder nesting rejected");library.Apply("folderRename",folder,"","常用工具");library.Apply("reorder",b,a,"");s=library.GetSnapshot();Check(s.entries.First(e=>e.id==b).order<s.entries.First(e=>e.id==a).order,"folder order persists");
    library.Apply("alias",a,"","we chat 聊天");using(var reloaded=new AppLibrary(fixture)){Check(reloaded.Find(a).folder==folder&&reloaded.Find(a).alias=="we chat 聊天","reload retains folder and alias");reloaded.Apply("categoryRename",category,"","重命名分类");}library.Apply("alias",b,"","second");Check(library.GetSnapshot().categories.Any(c=>c.name=="重命名分类"),"mutation merges current disk state");
    library.Apply("folderDelete",folder,"","");s=library.GetSnapshot();Check(s.entries.Where(e=>e.id==a||e.id==b).All(e=>e.folder==""&&e.category==category),"folder deletion restores parent category");library.Apply("categoryDelete",category,"","");Check(library.Find(a).category=="uncategorized","category deletion keeps entries");Reject(()=>library.Apply("categoryDelete","uncategorized","",""),"uncategorized cannot be deleted");
    library.Repair(a,third);Check(library.Find(a).path==third,"repair preserves stable entry ID");Reject(()=>library.Repair(a,second),"repair cannot silently merge two targets");library.Apply("remove",b,"","");Check(library.Find(b)==null&&File.Exists(second),"remove only deletes library record");library.Add(second);Check(library.GetSnapshot().entries.Count(e=>e.path==second)==1,"explicit add restores excluded entry");Check(File.ReadAllText(first)=="source unchanged","source contents preserved");
    long revision=library.GetSnapshot().revision;Reject(()=>library.Apply("categoryCreate","","",new string('x',81)),"invalid name rejected");Check(library.GetSnapshot().revision==revision,"failed mutation not committed");
    var stale=library.GetSnapshot();string store=Path.Combine(fixture,"app-library.json"),external=File.ReadAllText(store)+" ";File.WriteAllText(store,external);
    var commit=typeof(AppLibrary).GetMethod("Commit",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);bool conflict=false;try{commit.Invoke(library,new object[]{stale});}catch(System.Reflection.TargetInvocationException ex){conflict=ex.InnerException is IOException;}
    Check(conflict&&File.ReadAllText(store)==external,"a change after reading cannot be overwritten by a stale commit");
    Check(!Directory.EnumerateFiles(fixture,"*.tmp").Any(),"conflict cleans its temporary write");
   }
   var state=new AppLibrarySnapshot();state.categories.Add(new AppLibraryCategory{id="uncategorized",name="未分类"});string identity=AppLibrary.Identity(first,"");AppLibrary.Merge(state,new[]{new AppLibraryEntry{identity=identity,name="同名",path=first,exists=true,sources=new List<string>{first}},new AppLibraryEntry{identity=identity,name="同名",path=second,exists=true,sources=new List<string>{second}},new AppLibraryEntry{identity=AppLibrary.Identity(third,""),name="同名",path=third,exists=true,sources=new List<string>{third}}});Check(state.entries.Count==2&&state.entries[0].sources.Count==2,"same identity merges sources, same name different target retained");
   state.entries[0].category="games";state.entries[0].exists=false;AppLibrary.Merge(state,new[]{new AppLibraryEntry{identity=identity,name="新名称",path=first,exists=true,sources=new List<string>{first}}});Check(state.entries[0].category=="games"&&state.entries[0].exists,"rescan keeps manual category and stable ID");state.excluded.Add(identity);state.entries.RemoveAt(0);AppLibrary.Merge(state,new[]{new AppLibraryEntry{identity=identity,name="同名",path=first,sources=new List<string>{first}}});Check(state.entries.Count==1,"excluded identity does not return on scan");
   string data=Path.Combine(fixture,"app-library.json");File.WriteAllText(data,"invalid JSON");using(var broken=new AppLibrary(fixture)){Check(broken.GetSnapshot().error.Contains("读取失败"),"corrupt file has actionable error");Reject(()=>broken.Add(first),"corrupt file not overwritten by mutation");Check(File.ReadAllText(data)=="invalid JSON","corrupt source retained");}
   Console.WriteLine("AppLibrary: "+checks+" behavior checks passed.");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(fixture.StartsWith(AppDomain.CurrentDomain.BaseDirectory,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(fixture))Directory.Delete(fixture,true);}
 }
}
