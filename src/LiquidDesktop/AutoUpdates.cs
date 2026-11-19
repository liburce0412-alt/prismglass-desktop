using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

internal sealed class AutoUpdates : IDisposable {
 sealed class ApplyResult {public string version{get;set;}public string status{get;set;}public string message{get;set;}public string atUtc{get;set;}}
 readonly string directory,current,enabledPath,lockPath,statusPath,stagedPath,resultPath;
 readonly Func<bool> canApply;
 readonly Func<string,bool> startApply;
 readonly bool supported;
 CancellationTokenSource operation;
 DateTime nextCheck=DateTime.UtcNow.AddSeconds(20),nextApply,nextRefresh,statusStamp;
 bool disposed,enabled;
 public ReleaseUpdateState State{get;private set;}
 public event Action Changed;

 internal AutoUpdates(string root,string currentVersion,Func<bool> canApply,Func<string,bool> startApply){
  directory=Path.Combine(root,"runtime-data","updates");current=currentVersion;this.canApply=canApply;this.startApply=startApply;
  enabledPath=Path.Combine(directory,"enabled");lockPath=Path.Combine(directory,"download.lock");statusPath=Path.Combine(directory,"status.json");stagedPath=Path.Combine(directory,"staged.json");resultPath=Path.Combine(directory,"apply-result.json");
  supported=canApply!=null&&startApply!=null;SetState(new ReleaseUpdateState{current=current},false);Refresh();
 }
 void SetState(ReleaseUpdateState value,bool share){
  value.automaticSupported=supported;value.automaticEnabled=enabled;State=value;
  if(share&&supported){try{AutoUpdateDownload.WriteJson(statusPath,State);statusStamp=File.GetLastWriteTimeUtc(statusPath);}catch(IOException){}catch(UnauthorizedAccessException){}}
  var changed=Changed;if(changed!=null)changed();
 }
 static T ReadJson<T>(string path) where T:class {
  try{
   AutoUpdateDownload.RejectLink(path);
   using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
    if(file.Length>16384)return null;
    using(var reader=new StreamReader(file))return new JavaScriptSerializer{MaxJsonLength=16384}.Deserialize<T>(reader.ReadToEnd());
   }
  }catch(Exception){return null;}
 }
 void AssertDirectory(){AutoUpdateDownload.RejectLink(Path.GetDirectoryName(directory));AutoUpdateDownload.RejectLink(directory);}
 FileStream TryLock(){
  AssertDirectory();Directory.CreateDirectory(directory);AutoUpdateDownload.RejectLink(lockPath);
  try{return new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
  catch(IOException error){int code=error.HResult&0xffff;if(code==32||code==33)return null;throw;}
 }
 void Refresh(){
  if(!supported)return;
  try{
   AssertDirectory();AutoUpdateDownload.RejectLink(enabledPath);bool flag=File.Exists(enabledPath);
   bool changed=flag!=enabled;enabled=flag;
   if(changed&&!enabled&&operation!=null)operation.Cancel();
   if(operation==null){
    DateTime stamp=File.GetLastWriteTimeUtc(statusPath);
    if(stamp!=statusStamp){statusStamp=stamp;var shared=ReadJson<ReleaseUpdateState>(statusPath);if(shared!=null&&shared.current==current){SetState(shared,false);return;}}
   }
   if(changed)SetState(State,false);
  }catch(IOException){}catch(UnauthorizedAccessException){}
 }
 internal void SetEnabled(bool value){
  if(!supported)throw new InvalidOperationException("请先通过官方安装器安装 PrismGlass，以使用自动覆盖更新。");
  AssertDirectory();AutoUpdateDownload.RejectLink(enabledPath);
  if(value){Directory.CreateDirectory(directory);using(File.Open(enabledPath,FileMode.OpenOrCreate,FileAccess.Write,FileShare.Read)){}nextCheck=DateTime.UtcNow;}
  else{File.Delete(enabledPath);if(operation!=null)operation.Cancel();}
  enabled=value;SetState(State,false);
 }
 internal async void Check(bool manual=true){
  if(disposed||operation!=null){if(!disposed)SetState(State,false);return;}
  var token=new CancellationTokenSource();FileStream lease=null;
  try{
   Refresh();nextCheck=DateTime.UtcNow.AddSeconds(20);
   if(supported){lease=TryLock();if(lease==null){Refresh();SetState(State,false);return;}}
   operation=token;
   SetState(new ReleaseUpdateState{current=current,state="checking",message="正在检查最新正式版本…"},true);
   var release=await ReleaseUpdates.Check(current,token.Token);
   token.Token.ThrowIfCancellationRequested();if(disposed)return;
   nextCheck=DateTime.UtcNow.AddHours(release.state=="error"?1:6);
   if(!enabled||release.state!="available"){SetState(release,true);return;}
   if(release.installer==null){release.message="发现新版本，GitHub 暂未提供可供自动校验的安装包。可打开官方发布页下载。";SetState(release,true);return;}
   var last=ReadJson<ApplyResult>(resultPath);bool failed=last!=null&&last.version==release.latest&&last.status=="failed";
   if(failed&&!manual){release.state="failed";release.message=ResultMessage(last);SetState(release,true);return;}
   release.state="downloading";release.message="正在下载并校验更新，可继续使用伴随屏。";SetState(release,true);
   var progress=new Progress<int>(percent=>{if(!disposed&&operation==token&&release.state=="downloading"&&percent>=release.progress+5){release.progress=percent;SetState(release,true);}});
   await AutoUpdateDownload.Stage(directory,release,((IProgress<int>)progress).Report,token.Token);
   token.Token.ThrowIfCancellationRequested();if(disposed)return;
   if(failed){File.Delete(resultPath);nextApply=DateTime.MinValue;}
   release.state="ready";release.progress=100;release.message="新版已下载并通过校验，将在可以安全重启时自动覆盖安装。";SetState(release,true);
  }catch(OperationCanceledException){if(!disposed){if(token.IsCancellationRequested)SetState(new ReleaseUpdateState{current=current},true);else UpdateError("更新下载超时。可重试检查或打开官方发布页。");}}
  catch(Exception){if(!disposed)UpdateError("无法下载或校验更新。可重试检查或打开官方发布页。");}
  finally{if(operation==token)operation=null;if(lease!=null)lease.Dispose();token.Dispose();}
 }
 static string ResultMessage(ApplyResult result){return String.IsNullOrWhiteSpace(result.message)?"覆盖更新失败，可手动重试检查。":result.message;}
 void UpdateError(string message){nextCheck=DateTime.UtcNow.AddHours(1);State.state="error";State.message=message;SetState(State,true);}
 internal void Tick(bool schedule=true){
  if(disposed||!supported)return;
  DateTime now=DateTime.UtcNow;if(now<nextRefresh)return;nextRefresh=now.AddSeconds(2);Refresh();
  if(operation!=null)return;
  if(State.state=="ready"||State.state=="applying"||State.state=="failed"){
   var result=ReadJson<ApplyResult>(resultPath);
   if(result!=null&&result.version==State.latest){
    if(result.status=="failed"){
     if(State.state!="failed"){State.state="failed";State.message=ResultMessage(result);SetState(State,false);}
     if(schedule&&enabled&&now>=nextCheck)Check(false);return;
    }
    DateTimeOffset at;
    if(result.status=="deferred"&&DateTimeOffset.TryParse(result.atUtc,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out at)&&at.UtcDateTime.AddMinutes(5)>now){
     if(State.state=="applying"){State.state="ready";State.message="新版已下载并通过校验，等待可以安全重启。";SetState(State,false);}return;
    }
   }
   if(schedule&&enabled&&now>=nextApply&&File.Exists(stagedPath)){
    nextApply=now.AddSeconds(30);
    try{using(var lease=TryLock())if(lease!=null&&File.Exists(enabledPath)&&canApply()&&startApply(stagedPath)){State.state="applying";State.message="正在准备覆盖安装，桌面将在更新完成后重新开启。";SetState(State,true);}}
    catch(Exception){State.state="ready";State.message="暂时无法启动安装，将稍后重试。";SetState(State,false);}
   }
   if(File.Exists(stagedPath))return;
  }
  if(schedule&&enabled&&now>=nextCheck)Check(false);
 }
 public void Dispose(){disposed=true;if(operation!=null)operation.Cancel();}
}
