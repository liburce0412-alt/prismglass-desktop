using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class AutoUpdateDownload {
 // Leave network capacity for the live companion stream.
 const int BytesPerSecond=512*1024;
 static readonly HttpClient client=ReleaseUpdates.CreateClient();
 internal static bool IsAssetRedirect(Uri target){
  return target!=null&&target.IsAbsoluteUri&&target.Scheme=="https"&&target.Host=="release-assets.githubusercontent.com"&&target.Port==443&&target.UserInfo.Length==0&&target.Fragment.Length==0&&target.OriginalString.Length<=8192&&Regex.IsMatch(target.AbsolutePath,@"\A/github-production-release-asset/[0-9]+/[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\z");
 }
 internal static void RejectLink(string path){
  try{if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("更新路径不能使用链接。");}
  catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
 }
 static void Commit(string source,string destination){
  RejectLink(destination);
  if(File.Exists(destination))File.Replace(source,destination,null);else File.Move(source,destination);
 }
 internal static void WriteJson(string path,object value){
  string temporary=path+"."+Guid.NewGuid().ToString("N")+".new";
  try{using(var writer=new StreamWriter(new FileStream(temporary,FileMode.CreateNew)))writer.Write(new JavaScriptSerializer().Serialize(value));Commit(temporary,path);}
  finally{File.Delete(temporary);}
 }
 static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
 static bool HasVerifiedFile(string path,ReleaseAsset asset){
  if(!File.Exists(path))return false;
  using(var existing=File.OpenRead(path))using(var sha=SHA256.Create())return existing.Length==asset.size&&Hex(sha.ComputeHash(existing))==asset.sha256;
 }
 static async Task<HttpResponseMessage> Open(HttpClient transport,string url,CancellationToken cancel){
  using(var request=new HttpRequestMessage(HttpMethod.Get,url)){
   request.Headers.UserAgent.ParseAdd("PrismGlass-UpdateCheck/1.0");
   return await transport.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancel).ConfigureAwait(false);
  }
 }
 static async Task Download(HttpClient transport,ReleaseAsset asset,string path,Action<int> progress,CancellationToken cancel){
  var response=await Open(transport,asset.url,cancel).ConfigureAwait(false);
  try{
   if(response.StatusCode==HttpStatusCode.Redirect){
    Uri target=response.Headers.Location;
    if(!IsAssetRedirect(target))throw new IOException("更新下载地址验证失败。");
    response.Dispose();response=await Open(transport,target.AbsoluteUri,cancel).ConfigureAwait(false);
   }
   if(response.StatusCode!=HttpStatusCode.OK||response.Content==null||(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value!=asset.size))throw new IOException("更新下载响应与官方发布信息不符。");
   using(cancel.Register(response.Dispose))
   using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
   using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
   using(var sha=SHA256.Create()){
    var buffer=new byte[65536];long length=0;int percent=0,read;
    while((read=await input.ReadAsync(buffer,0,buffer.Length,cancel).ConfigureAwait(false))!=0){
     length+=read;if(length>asset.size)throw new IOException("更新文件超过官方发布的大小。");
     sha.TransformBlock(buffer,0,read,buffer,0);
     await output.WriteAsync(buffer,0,read,cancel).ConfigureAwait(false);
     int received=(int)(length*100/asset.size);if(received>percent){percent=received;progress(percent);}
     await Task.Delay((read*1000+BytesPerSecond-1)/BytesPerSecond,cancel).ConfigureAwait(false);
    }
    sha.TransformFinalBlock(buffer,0,0);
    if(length!=asset.size||Hex(sha.Hash)!=asset.sha256)throw new IOException("更新文件的大小或 SHA-256 校验不符。");
   }
  }finally{response.Dispose();}
 }
 internal static Task<string> Stage(string directory,ReleaseUpdateState release,Action<int> progress,CancellationToken cancel){return Stage(client,directory,release,progress,cancel);}
 internal static async Task<string> Stage(HttpClient transport,string directory,ReleaseUpdateState release,Action<int> progress,CancellationToken cancel){
  var asset=release.installer;if(asset==null)throw new IOException("官方发布暂未提供可验证的安装包。");
  string file=Path.Combine(directory,asset.name),manifest=Path.Combine(directory,"staged.json"),temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".part");
  RejectLink(Path.GetDirectoryName(directory));RejectLink(directory);RejectLink(file);RejectLink(manifest);Directory.CreateDirectory(directory);
  using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel)){
   timeout.CancelAfter(TimeSpan.FromMinutes(15));
   try{
    bool exists=await Task.Run(()=>HasVerifiedFile(file,asset),timeout.Token).ConfigureAwait(false);
    if(!exists)await Download(transport,asset,temporary,progress,timeout.Token).ConfigureAwait(false);
    timeout.Token.ThrowIfCancellationRequested();
    if(!exists)Commit(temporary,file);
    WriteJson(manifest,new{version=release.latest,tag=release.tag,name=asset.name,sha256=asset.sha256,size=asset.size});return manifest;
   }catch(Exception){timeout.Token.ThrowIfCancellationRequested();throw;}
   finally{File.Delete(temporary);}
  }
 }
}
