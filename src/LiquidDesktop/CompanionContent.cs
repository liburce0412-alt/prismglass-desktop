using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class CompanionContentConflictException : IOException {
 public CompanionContentConflictException():base("内容已在其他入口更新，请重新载入并合并草稿。"){}
}
internal sealed class CompanionNoteFile {public string source;public long stamp;}
internal sealed class CompanionTaskItem {public string id,text;public bool done;}

// Desktop and companion writers use the same mutex and compare the source they actually read.
internal sealed class CompanionContent {
 readonly string stateFile,noteFile;readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=1048576,RecursionLimit=32};
 string taskSource,noteSource,taskRevision="",noteRevision="";long noteStamp;
 public CompanionContent(string root){stateFile=Path.Combine(root,"shelf-state.json");noteFile=Path.GetFullPath(Path.Combine(root,"..","随手记.txt"));}
 static string Text(Dictionary<string,object> request,string key){object value;if(request==null||!request.TryGetValue(key,out value)||!(value is string))throw new ArgumentException("内容格式无效。");return (string)value;}
 static string Optional(Dictionary<string,object> map,string key){object value;if(!map.TryGetValue(key,out value)||value==null)return "";if(!(value is string))throw new IOException("内容格式无效，原文件已保留。");return (string)value;}
 static List<CompanionTaskItem> Tasks(Dictionary<string,object> map){
  var result=new List<CompanionTaskItem>();object raw;if(!map.TryGetValue("tasks",out raw)||raw==null)return result;
  var items=raw as IEnumerable;if(items==null||raw is string||raw is IDictionary)throw new IOException("事项格式无效，原文件已保留。");
  var ids=new HashSet<string>(StringComparer.Ordinal);foreach(var item in items){var value=item as Dictionary<string,object>;object done;
   if(value==null||result.Count>=50||!value.TryGetValue("done",out done)||!(done is bool))throw new IOException("事项格式无效，原文件已保留。");
   string id=Optional(value,"id"),text=Optional(value,"text");if(id.Length==0||id.Length>128||!ids.Add(id)||String.IsNullOrWhiteSpace(text)||text.Length>120)throw new IOException("事项格式无效，原文件已保留。");
   result.Add(new CompanionTaskItem{id=id,text=text,done=(bool)done});
  }return result;
 }
 Dictionary<string,object> TaskMap(){string source=DesktopSettingsFiles.Read(stateFile);var map=DesktopSettingsFiles.Map(source);Tasks(map);if(taskRevision.Length==0||source!=taskSource){taskSource=source;taskRevision=Guid.NewGuid().ToString("N");}return map;}
 object TaskSnapshot(Dictionary<string,object> map){return new{available=true,revision=taskRevision,items=Tasks(map),countdownTitle=Optional(map,"countdownTitle"),countdownDate=Optional(map,"countdownDate")};}
 object NoteSnapshot(){var value=ReadNoteFile(noteFile);if(noteRevision.Length==0||value.source!=noteSource||value.stamp!=noteStamp){noteSource=value.source;noteStamp=value.stamp;noteRevision=Guid.NewGuid().ToString("N");}return new{available=true,revision=noteRevision,text=noteSource??""};}
 public object ReadTasks(){object result=null;DesktopSettingsFiles.Locked(()=>result=TaskSnapshot(TaskMap()));return result;}
 public object ReadNotes(){object result=null;DesktopSettingsFiles.Locked(()=>result=NoteSnapshot());return result;}
 static void Authorize(Func<bool> authorized){if(authorized!=null&&!authorized())throw new UnauthorizedAccessException("共享授权已撤销或电脑已锁定。");}
 public object WriteTasks(Dictionary<string,object> request,Func<bool> authorized=null){object result=null;DesktopSettingsFiles.Locked(()=>{
  Authorize(authorized);
  var map=TaskMap();if(Text(request,"expectedRevision")!=taskRevision)throw new CompanionContentConflictException();var items=Tasks(map);string command=Text(request,"command");
  if(command=="add"){string text=Text(request,"text").Trim();if(text.Length==0||text.Length>120||items.Count>=50)throw new ArgumentException("事项最多 50 条，每条不超过 120 字。");items.Add(new CompanionTaskItem{id=Guid.NewGuid().ToString("N"),text=text,done=false});}
  else if(command=="toggle"||command=="delete"){string id=Text(request,"id");var item=items.Find(value=>value.id==id);if(item==null)throw new CompanionContentConflictException();if(command=="toggle")item.done=!item.done;else items.Remove(item);}
  else throw new ArgumentException("未知事项操作。");
  map["tasks"]=items;string next=json.Serialize(map);if(DesktopSettingsFiles.Read(stateFile)!=taskSource)throw new CompanionContentConflictException();Authorize(authorized);DesktopSettingsFiles.Write(stateFile,next);result=TaskSnapshot(TaskMap());
 });return result;}
 public object WriteNotes(Dictionary<string,object> request,Func<bool> authorized=null){object result=null;DesktopSettingsFiles.Locked(()=>{
  Authorize(authorized);NoteSnapshot();if(Text(request,"expectedRevision")!=noteRevision)throw new CompanionContentConflictException();SaveNoteFile(noteFile,Text(request,"text"),noteSource,noteStamp,authorized);result=NoteSnapshot();
 });return result;}
 public static CompanionNoteFile ReadNoteFile(string file){
  if(!File.Exists(file))return new CompanionNoteFile{source=null,stamp=0};
  long before=File.GetLastWriteTimeUtc(file).Ticks;string text;
  using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
   if(stream.Length>131072)throw new IOException("随手记超过 128 KB，请在记事本中编辑。");
   using(var bytes=new MemoryStream()){var buffer=new byte[4096];int count;while((count=stream.Read(buffer,0,Math.Min(buffer.Length,131073-(int)bytes.Length)))>0){bytes.Write(buffer,0,count);if(bytes.Length>131072)throw new IOException("随手记超过 128 KB，请在记事本中编辑。");}bytes.Position=0;using(var reader=new StreamReader(bytes,Encoding.UTF8,true))text=reader.ReadToEnd();}
  }
  if(File.GetLastWriteTimeUtc(file).Ticks!=before)throw new CompanionContentConflictException();return new CompanionNoteFile{source=text,stamp=before};
 }
 public static long SaveNoteFile(string file,string text,string expectedSource,long expectedStamp,Func<bool> authorized=null){
  if(text==null||text.Length>32768)throw new ArgumentException("随手记不超过 32768 字。");long saved=0;
  DesktopSettingsFiles.Locked(()=>{
   Authorize(authorized);var current=ReadNoteFile(file);if(current.source!=expectedSource||current.stamp!=expectedStamp)throw new CompanionContentConflictException();
   string pending=file+".pending-"+Guid.NewGuid().ToString("N");try{
    File.WriteAllText(pending,text,new UTF8Encoding(false));current=ReadNoteFile(file);if(current.source!=expectedSource||current.stamp!=expectedStamp)throw new CompanionContentConflictException();
    Authorize(authorized);if(File.Exists(file))File.Replace(pending,file,file+".previous");else File.Move(pending,file);saved=File.GetLastWriteTimeUtc(file).Ticks;
   }finally{if(File.Exists(pending))File.Delete(pending);}
  });return saved;
 }
}
