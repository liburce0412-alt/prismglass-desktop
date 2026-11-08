using System;using System.IO;
internal static class TestPhotoHttp {
 static int Main(){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"photo-http-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"photo-cards"));try{
  string token=new string('a',32);File.WriteAllText(Path.Combine(root,"photo-cards",token+".png"),"private local fixture");File.WriteAllText(Path.Combine(root,"photo-cards","Photo.txt"),token);
  using(var server=new CompanionServer(root,(path,body,grant)=>CompanionReply.Json(200,new{ok=true}),()=>false)){
   foreach(string path in new[]{"/photo-cards/"+token+".png","/photo-cards/Photo.txt","/../photo-cards/"+token+".png"}){
    var reply=server.Dispatch(new CompanionRequest{Path=path,Method="GET",Origin="https://192.168.1.20:38473",RemoteAddress="192.168.1.21"},"https://192.168.1.20:38473");
    if(reply.Status!=404)throw new Exception("Local photo resource was not rejected: "+path);Console.WriteLine("PASS companion HTTP rejects "+path);
   }
  }return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{Directory.Delete(root,true);}}
}
