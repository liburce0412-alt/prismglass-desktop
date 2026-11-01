using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
internal static class TestNpuText {
 static async Task Run(string[] args){
  using(var image=new Bitmap(args[1])){
   var result=await NpuText.Recognize(args[0],image,CancellationToken.None);
   if(!((string)result["text"]).Contains("冰川测试124")||!Object.Equals(result["device"],"NPU"))throw new Exception("Production host did not return expected real NPU text");
   Console.WriteLine("PASS production C# host receives expected Chinese text from the actual NPU helper");
   using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{await NpuText.Recognize(args[0],image,cancelled.Token);throw new Exception("Cancelled request was accepted");}catch(OperationCanceledException){Console.WriteLine("PASS cancelled request never starts inference");}}
  }
 }
 static int Main(string[] args){try{Run(args).GetAwaiter().GetResult();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
