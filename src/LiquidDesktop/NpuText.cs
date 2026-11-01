using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class NpuText {
 sealed class Config {public string python,root;}
 static Config Read(string root){
  string file=Path.Combine(root,"runtime-data","npu.json");
  if(!File.Exists(file))throw new IOException("尚未配置本机 NPU 工具，请先按使用说明准备运行环境。");
  var value=new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(file));
  if(value==null||String.IsNullOrEmpty(value.python)||String.IsNullOrEmpty(value.root)||!Path.IsPathRooted(value.python)||!Path.IsPathRooted(value.root)||!File.Exists(value.python)||!File.Exists(Path.Combine(value.root,"models","ocr-det.onnx"))||!File.Exists(Path.Combine(value.root,"models","ocr-rec.onnx"))||!File.Exists(Path.Combine(root,"PrismNpu","main.py")))throw new IOException("NPU 工具或模型不完整，请重新检查本机配置。");
  return value;
 }
 internal static object Describe(string root){try{Read(root);return new{available=true,message="点击后由本机 NPU 识别当前屏幕。"};}catch(Exception error){return new{available=false,message=error.Message};}}
 static string Quote(string value){if(value.IndexOfAny(new[]{'"','\r','\n','\0'})>=0)throw new ArgumentException("NPU 路径无效。");return "\""+value+"\"";}
 internal static async Task<Dictionary<string,object>> Recognize(string root,Bitmap picture,CancellationToken cancellation){
  var config=Read(root);string image;using(var bytes=new MemoryStream()){picture.Save(bytes,ImageFormat.Png);if(bytes.Length>16*1024*1024)throw new IOException("画面过大，请降低屏幕分辨率后重试。");image=Convert.ToBase64String(bytes.ToArray());}
  var json=new JavaScriptSerializer{MaxJsonLength=24*1024*1024};
  using(var process=new Process{StartInfo=new ProcessStartInfo(config.python,"-I -X utf8 "+Quote(Path.Combine(root,"PrismNpu","main.py"))+" --root "+Quote(config.root)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8}}){
   cancellation.ThrowIfCancellationRequested();process.Start();
   using(cancellation.Register(()=>{try{if(!process.HasExited)process.Kill();}catch{}}))try{
    var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
    await process.StandardInput.WriteAsync(json.Serialize(new{image=image}));process.StandardInput.Close();
    if(!await Task.Run(()=>process.WaitForExit(30000)))throw new TimeoutException("识别超时，请稍后重试。");
    cancellation.ThrowIfCancellationRequested();string text=await output;await errors;
    if(text.Length>262144)throw new IOException("识别结果过大。");
    var result=json.Deserialize<Dictionary<string,object>>(text);object ok,device,content;
    if(result==null||!result.TryGetValue("ok",out ok)||!(ok is bool)||(bool)ok==false||process.ExitCode!=0){object message;throw new IOException(result!=null&&result.TryGetValue("message",out message)?Convert.ToString(message):"NPU 识别未能完成，请检查驱动与运行环境。");}
    if(!result.TryGetValue("device",out device)||!Object.Equals(device,"NPU")||!result.TryGetValue("text",out content)||!(content is string)||((string)content).Length>32000)throw new IOException("NPU 返回的识别结果无效。");
    return result;
   }finally{try{if(!process.HasExited){process.Kill();process.WaitForExit(2000);}}catch{}}
  }
 }
}
