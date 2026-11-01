using System;using System.Diagnostics;using System.IO;using System.Media;using System.Text;using System.Threading;using System.Threading.Tasks;
internal static class TestCompanionAudio {
 static Process Start(string file,string arguments,bool input){var p=new Process{StartInfo=new ProcessStartInfo(file,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=input,RedirectStandardOutput=true,RedirectStandardError=true}};p.Start();return p;}
 static void End(Process p){if(p==null)return;try{if(!p.HasExited){p.Kill();p.WaitForExit(2000);}}finally{p.Dispose();}}
 static int Main(string[] args){Process capture=null,encoder=null;try{
  if(args.Length!=2)throw new ArgumentException("helper ffmpeg");
  capture=Start(args[0],"--loopback",false);var captureError=capture.StandardError.ReadToEndAsync();
  var pcm=new byte[48000*2*4*2];var reading=Task.Run(()=>{int offset=0;while(offset<pcm.Length){int n=capture.StandardOutput.BaseStream.Read(pcm,offset,pcm.Length-offset);if(n==0)break;offset+=n;}return offset;});
  using(var wav=new MemoryStream()){
   using(var writer=new BinaryWriter(wav,Encoding.ASCII,true)){int count=48000;writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(48000);writer.Write(96000);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(count*2);for(int i=0;i<count;i++)writer.Write((short)(Math.Sin(i*2*Math.PI*997/48000)*1000));}
   wav.Position=0;using(var player=new SoundPlayer(wav)){Thread.Sleep(200);player.PlaySync();}
  }
  if(!reading.Wait(6000)||reading.Result!=pcm.Length)throw new Exception("Loopback PCM deadline or byte count failed");End(capture);capture=null;
  float peak=0;for(int i=0;i<pcm.Length;i+=4){float value=Math.Abs(BitConverter.ToSingle(pcm,i));if(float.IsNaN(value)||float.IsInfinity(value)||value>4)throw new Exception("Invalid float PCM");peak=Math.Max(peak,value);}if(peak<.0001)throw new Exception("Loopback contains no measurable sound");
  encoder=Start(args[1],"-hide_banner -loglevel error -f f32le -ar 48000 -ac 2 -i pipe:0 -c:a libopus -b:a 128k -f ogg pipe:1",true);var errors=encoder.StandardError.ReadToEndAsync();var bytes=new MemoryStream();var output=encoder.StandardOutput.BaseStream.CopyToAsync(bytes);encoder.StandardInput.BaseStream.Write(pcm,0,pcm.Length);encoder.StandardInput.Close();if(!encoder.WaitForExit(6000))throw new Exception("Opus encoding timed out");output.Wait();if(encoder.ExitCode!=0)throw new Exception(errors.Result);var encoded=bytes.ToArray();if(encoded.Length<100||Encoding.ASCII.GetString(encoded,0,4)!="OggS"||!Encoding.ASCII.GetString(encoded,0,Math.Min(100,encoded.Length)).Contains("OpusHead"))throw new Exception("Missing Opus stream header");
  Console.WriteLine("PASS real system loopback: 2 seconds / "+pcm.Length+" float PCM bytes, nonzero signal");Console.WriteLine("PASS installed FFmpeg encodes loopback as Opus: "+encoded.Length+" bytes in memory");Console.WriteLine("No captured audio written to disk; no microphone, display or volume settings changed.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}finally{End(capture);End(encoder);}}
}
