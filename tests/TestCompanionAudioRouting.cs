using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// Opt-in endpoint mute probe. PCM remains in memory and the original mute state is restored.
internal static class TestCompanionAudioRouting {
 static float Probe(string helper,MMDevice device){
  using(var capture=new Process{StartInfo=new ProcessStartInfo(helper,"--loopback"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){
   capture.Start();var errors=capture.StandardError.ReadToEndAsync();
   try{
    var pcm=new byte[48000*2*4*2];var reading=Task.Run(()=>{int offset=0;while(offset<pcm.Length){int count=capture.StandardOutput.BaseStream.Read(pcm,offset,pcm.Length-offset);if(count==0)break;offset+=count;}return offset;});
    var tone=new byte[48000*2];for(int i=0;i<48000;i++){short sample=(short)(Math.Sin(i*2*Math.PI*997/48000)*1000);tone[i*2]=(byte)sample;tone[i*2+1]=(byte)(sample>>8);}
    using(var source=new RawSourceWaveStream(new MemoryStream(tone),new WaveFormat(48000,16,1)))using(var player=new WasapiOut(device,AudioClientShareMode.Shared,false,100)){
     player.Init(source);Thread.Sleep(300);player.Play();Thread.Sleep(1200);
     if(!reading.Wait(6000)||reading.Result!=pcm.Length)throw new Exception("Production loopback helper did not provide two seconds of PCM");
    }
    float peak=0;for(int i=0;i<pcm.Length;i+=4){float sample=Math.Abs(BitConverter.ToSingle(pcm,i));if(float.IsNaN(sample)||float.IsInfinity(sample))throw new Exception("Invalid PCM");peak=Math.Max(peak,sample);}return peak;
   }finally{if(!capture.HasExited){capture.Kill();capture.WaitForExit(3000);}errors.Wait(1000);}
  }
 }
 static int Main(string[] args){
  try{
   using(var devices=new MMDeviceEnumerator())using(var device=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia)){
    bool originalMute=device.AudioEndpointVolume.Mute;float originalVolume=device.AudioEndpointVolume.MasterVolumeLevelScalar;
    Console.WriteLine("Endpoint: "+device.FriendlyName+"; originalMute="+originalMute+"; volume="+originalVolume);
    try{
     if(!originalMute){float baseline=Probe(args[0],device);Console.WriteLine("Unmuted decoded PCM peak: "+baseline);if(baseline<.0001)throw new Exception("Unmuted baseline contains no measurable tone");}
     device.AudioEndpointVolume.Mute=true;Thread.Sleep(250);
     float muted=Probe(args[0],device);Console.WriteLine("Muted decoded PCM peak: "+muted);
     Console.WriteLine(muted>.0001?"SUPPORTED on this endpoint: system mute retains non-silent loopback PCM.":"UNSUPPORTED on this endpoint: system mute also silences loopback PCM.");
    }finally{device.AudioEndpointVolume.Mute=originalMute;}
    using(var current=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))if(current.ID!=device.ID)throw new Exception("Default output changed during the probe; routing result is not stable");
    if(device.AudioEndpointVolume.Mute!=originalMute||device.AudioEndpointVolume.MasterVolumeLevelScalar!=originalVolume)throw new Exception("Endpoint state differs from the original observation");
    Console.WriteLine("PASS original mute and volume restored; default output unchanged; no audio saved.");
   }
   return 0;
  }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
 }
}
