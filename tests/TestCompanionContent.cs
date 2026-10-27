using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class TestCompanionContent {
 static int checks;static readonly JavaScriptSerializer json=new JavaScriptSerializer();
 static Dictionary<string,object> Map(object value){return json.DeserializeObject(json.Serialize(value)) as Dictionary<string,object>;}
 static string Revision(object value){return (string)Map(value)["revision"];}
 static Dictionary<string,object> Request(params string[] pairs){var value=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)value[pairs[i]]=pairs[i+1];return value;}
 static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Throws<T>(Action action,string name) where T:Exception {try{action();}catch(T){Check(true,name);return;}throw new Exception(name);}
 static object[] Items(object value){return (object[])Map(value)["items"];}
 static void Main(string[] args){
  string root=args[0],state=Path.Combine(root,"shelf-state.json"),note=Path.GetFullPath(Path.Combine(root,"..","随手记.txt"));Directory.CreateDirectory(root);
  var content=new CompanionContent(root);var first=content.ReadTasks();string revision=Revision(first);
  Check((bool)Map(first)["available"]&&Items(first).Length==0&&revision.Length==32,"empty tasks have opaque revision");
  Check(Revision(content.ReadTasks())==revision,"unchanged task reads keep revision");
  var added=content.WriteTasks(Request("expectedRevision",revision,"command","add","text","  测试事项  "));string id=(string)Map(Items(added)[0])["id"];
  Check(Items(added).Length==1&&(string)Map(Items(added)[0])["text"]=="测试事项"&&Revision(added)!=revision,"add trims text and updates revision");
  Throws<CompanionContentConflictException>(()=>content.WriteTasks(Request("expectedRevision",revision,"command","add","text","旧请求")),"stale task revision cannot overwrite");
  var toggled=content.WriteTasks(Request("expectedRevision",Revision(added),"command","toggle","id",id));Check((bool)Map(Items(toggled)[0])["done"],"toggle persists completion");
  Check((bool)Map(Items(new CompanionContent(root).ReadTasks())[0])["done"],"a fresh reader sees persisted tasks");
  var disk=DesktopSettingsFiles.Map(File.ReadAllText(state));disk["customField"]="preserved";disk["countdownTitle"]="读书";disk["countdownDate"]="2027-01-01";DesktopSettingsFiles.Write(state,json.Serialize(disk));
  Throws<CompanionContentConflictException>(()=>content.WriteTasks(Request("expectedRevision",Revision(toggled),"command","delete","id",id)),"external desktop change invalidates remote revision");
  var changed=content.ReadTasks();Check((string)Map(changed)["countdownTitle"]=="读书","countdown shared from existing shelf data");
  var deleted=content.WriteTasks(Request("expectedRevision",Revision(changed),"command","delete","id",id));Check(Items(deleted).Length==0&&(string)DesktopSettingsFiles.Map(File.ReadAllText(state))["customField"]=="preserved","delete retains unrelated shelf fields");
  Throws<ArgumentException>(()=>content.WriteTasks(Request("expectedRevision",Revision(deleted),"command","run","text","x")),"unknown task command rejected");
  Throws<ArgumentException>(()=>content.WriteTasks(Request("expectedRevision",Revision(deleted),"command","add","text",new string('x',121))),"task text length enforced");
  Throws<ArgumentException>(()=>content.WriteTasks(Request("expectedRevision",Revision(deleted),"command","add","text","   ")),"empty task text rejected");
  string concurrentRevision=Revision(content.ReadTasks());int saved=0,conflicts=0;
  Parallel.For(0,2,i=>{try{content.WriteTasks(Request("expectedRevision",concurrentRevision,"command","add","text","writer "+i));System.Threading.Interlocked.Increment(ref saved);}catch(CompanionContentConflictException){System.Threading.Interlocked.Increment(ref conflicts);}});
  Check(saved==1&&conflicts==1&&Items(content.ReadTasks()).Length==1,"concurrent writers have one winner without lost update");
  string authorizedRevision=Revision(content.ReadTasks()),beforeRevoke=File.ReadAllText(state);bool authorized=true;Task waiting=null;
  using(var started=new System.Threading.ManualResetEventSlim(false)){DesktopSettingsFiles.Locked(()=>{waiting=Task.Run(()=>{started.Set();Throws<UnauthorizedAccessException>(()=>content.WriteTasks(Request("expectedRevision",authorizedRevision,"command","add","text","已撤销请求"),()=>authorized),"revoked task writer is denied after waiting for storage lock");});if(!started.Wait(2000))throw new Exception("writer did not start");authorized=false;});waiting.GetAwaiter().GetResult();}
  Check(File.ReadAllText(state)==beforeRevoke,"revoked task request cannot alter data");int authorizationChecks=0;
  Throws<UnauthorizedAccessException>(()=>content.WriteTasks(Request("expectedRevision",authorizedRevision,"command","add","text","提交前锁屏"),()=>++authorizationChecks==1),"task writer rechecks immediately before commit");
  var max=new List<object>();for(int i=0;i<50;i++)max.Add(new{id="item"+i,text="事项 "+i,done=false});DesktopSettingsFiles.Write(state,json.Serialize(new{tasks=max}));var full=content.ReadTasks();
  Throws<ArgumentException>(()=>content.WriteTasks(Request("expectedRevision",Revision(full),"command","add","text","overflow")),"task count limit enforced");
  string broken="{\"tasks\":\"bad\",\"customField\":\"keep\"}";File.WriteAllText(state,broken);
  Throws<IOException>(()=>content.ReadTasks(),"malformed task data rejected");Check(File.ReadAllText(state)==broken,"malformed source remains untouched");
  var emptyNote=content.ReadNotes();Check((string)Map(emptyNote)["text"]==""&&Revision(emptyNote)==Revision(content.ReadNotes()),"empty note revision stable");
  var savedNote=content.WriteNotes(Request("expectedRevision",Revision(emptyNote),"text","第一份笔记"));Check(File.ReadAllText(note)=="第一份笔记"&&Revision(savedNote)!=Revision(emptyNote),"notes save to shared file");
  authorized=true;using(var started=new System.Threading.ManualResetEventSlim(false)){DesktopSettingsFiles.Locked(()=>{waiting=Task.Run(()=>{started.Set();Throws<UnauthorizedAccessException>(()=>content.WriteNotes(Request("expectedRevision",Revision(savedNote),"text","已撤销笔记"),()=>authorized),"revoked note writer is denied after waiting for storage lock");});if(!started.Wait(2000))throw new Exception("writer did not start");authorized=false;});waiting.GetAwaiter().GetResult();}
  Check(File.ReadAllText(note)=="第一份笔记","revoked note request cannot alter data");authorizationChecks=0;
  Throws<UnauthorizedAccessException>(()=>content.WriteNotes(Request("expectedRevision",Revision(savedNote),"text","提交前锁屏"),()=>++authorizationChecks<3),"note writer rechecks before atomic replacement");
  Throws<CompanionContentConflictException>(()=>content.WriteNotes(Request("expectedRevision",Revision(emptyNote),"text","stale")),"stale note request cannot overwrite");
  var desktop=CompanionContent.ReadNoteFile(note);CompanionContent.SaveNoteFile(note,"电脑修改",desktop.source,desktop.stamp);
  Throws<CompanionContentConflictException>(()=>content.WriteNotes(Request("expectedRevision",Revision(savedNote),"text","iPad旧草稿")),"desktop save conflicts with iPad draft");
  Check(File.ReadAllText(note)=="电脑修改"&&File.ReadAllText(note+".previous")=="第一份笔记","single recovery copy retains prior note");
  var latest=content.ReadNotes();content.WriteNotes(Request("expectedRevision",Revision(latest),"text","iPad修改"));
  Throws<CompanionContentConflictException>(()=>CompanionContent.SaveNoteFile(note,"电脑旧草稿",desktop.source,desktop.stamp),"iPad save conflicts with desktop draft");
  string currentNote=File.ReadAllText(note);long stamp=File.GetLastWriteTimeUtc(note).Ticks;File.WriteAllText(note,"同时间戳改动");File.SetLastWriteTimeUtc(note,new DateTime(stamp,DateTimeKind.Utc));
  Throws<CompanionContentConflictException>(()=>CompanionContent.SaveNoteFile(note,"不应覆盖",currentNote,stamp),"actual content comparison detects preserved timestamps");
  Throws<ArgumentException>(()=>content.WriteNotes(Request("expectedRevision",Revision(content.ReadNotes()),"text",new string('x',32769))),"note input length enforced");
  File.WriteAllText(note,new string('x',131073));Throws<IOException>(()=>content.ReadNotes(),"oversized existing note rejected without truncation");
  Check(new FileInfo(note).Length==131073,"oversized original note retained");
  Check(!Directory.EnumerateFiles(root,"*.pending-*").Any()&&!Directory.EnumerateFiles(Path.GetDirectoryName(note),"*.pending-*").Any(),"no pending files left after successful and rejected writes");
  Check(!json.Serialize(deleted).Contains(root)&&!json.Serialize(savedNote).Contains(note),"network snapshots contain no local paths");
  Console.WriteLine(checks+" companion content checks passed.");
 }
}
