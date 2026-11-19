using System;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

// Reuse the shipped NAudio wrappers for endpoint metadata only; no capture is opened.
internal sealed class AudioOutputState : IDisposable {
 internal sealed class State {public bool available,muted;public int value;public string name="",reason="正在读取默认声音输出。";}
 readonly object gate=new object();readonly Func<State> read;State state=new State();DateTime readAt=DateTime.MinValue;bool busy,disposed;
 internal AudioOutputState():this(ReadNative){}
 internal AudioOutputState(Func<State> reader){read=reader;}
 internal State Snapshot(){lock(gate){if(!disposed&&!busy&&(DateTime.UtcNow-readAt).TotalMilliseconds>=900){busy=true;Task.Run(()=>{State next;try{next=read();}catch{next=new State{reason="默认声音输出暂不可用。"};}lock(gate){if(!disposed){state=next;readAt=DateTime.UtcNow;}busy=false;}});}return state;}}
 static State ReadNative(){using(var devices=new MMDeviceEnumerator())using(var console=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Console))using(var multimedia=devices.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia)){
  // The old volume plugin does not publish its device ID. Never label one endpoint
  // with another endpoint's mute state when the user's default roles differ.
  if(console.ID!=multimedia.ID)return new State{reason="默认输出角色不同，静音状态暂不可用。"};
  var level=multimedia.AudioEndpointVolume;return new State{available=true,muted=level.Mute,value=(int)Math.Round(level.MasterVolumeLevelScalar*100),name=multimedia.FriendlyName,reason=""};
 }}
 public void Dispose(){lock(gate)disposed=true;}
}
