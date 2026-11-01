using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.CoreAudioApi;

// A bounded, continuously clocked PCM pipe; silence must not stall the video receiver.
internal static class CompanionAudioProgram {
 sealed class LoopbackCapture:WasapiCapture {
  public LoopbackCapture(MMDevice device):base(device,true,20){}
  protected override AudioClientStreamFlags GetAudioClientStreamFlags(){return AudioClientStreamFlags.Loopback|base.GetAudioClientStreamFlags();}
 }
 static int Main(string[] args){
  if(args.Length!=1||args[0]!="--loopback")return 1;
  try{
   using(var devices=new MMDeviceEnumerator())using(var device=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))using(var capture=new LoopbackCapture(device)){
    capture.WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
    var buffer=new BufferedWaveProvider(capture.WaveFormat){BufferDuration=TimeSpan.FromMilliseconds(80),DiscardOnBufferOverflow=true,ReadFully=true};
    Exception failure=null;capture.DataAvailable+=(s,e)=>buffer.AddSamples(e.Buffer,0,e.BytesRecorded);capture.RecordingStopped+=(s,e)=>failure=e.Exception??new IOException("Audio capture stopped");
    capture.StartRecording();using(var output=Console.OpenStandardOutput()){
     var block=new byte[480*2*4];var clock=Stopwatch.StartNew();long frames=0;
     while(Volatile.Read(ref failure)==null){
      if(frames%48000==0)using(var current=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))if(current.ID!=device.ID)throw new IOException("Output device changed");
      long due=frames*1000/48000,wait=due-clock.ElapsedMilliseconds;
      if(wait>0){Thread.Sleep((int)Math.Min(wait,10));continue;}
      if(wait < -200)throw new IOException("Audio clock fell behind");
      buffer.Read(block,0,block.Length);output.Write(block,0,block.Length);frames+=480;
     }
     throw failure;
    }
   }
  }catch(Exception error){Console.Error.WriteLine(error.GetType().Name+": system audio unavailable");return 2;}
 }
}
