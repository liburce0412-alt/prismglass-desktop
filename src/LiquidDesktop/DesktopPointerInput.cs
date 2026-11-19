using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

// UI-thread only: one logical pointer packet in flight and one latest position.
internal sealed class DesktopPointerDelivery {
 internal sealed class Packet {public string type="pointer";public int sequence,x,y;public bool down;}
 readonly Action<object> send;readonly Func<double> clock;Point latest,delivered;bool latestDown,deliveredDown,hasLatest,hasDelivered;Packet flight;int sequence;double sentAt=Double.NegativeInfinity;
 internal DesktopPointerDelivery(Action<object> output):this(output,()=>Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency){}
 internal DesktopPointerDelivery(Action<object> output,Func<double> now){send=output;clock=now;}
 internal void Offer(Point position,bool down){latest=position;latestDown=down;hasLatest=true;Pump();}
 void Pump(){double now=clock();if(flight!=null){if(now-sentAt<750)return;flight=null;hasDelivered=false;}
  if(!hasLatest)return;if(hasDelivered&&latest==delivered&&latestDown==deliveredDown){hasLatest=false;return;}if(now-sentAt<8)return;
  hasLatest=false;sequence=sequence%Int32.MaxValue+1;flight=new Packet{sequence=sequence,x=latest.X,y=latest.Y,down=latestDown};sentAt=now;send(flight);
 }
 internal void Acknowledge(int value){if(flight==null||flight.sequence!=value)return;delivered=new Point(flight.x,flight.y);deliveredDown=flight.down;hasDelivered=true;flight=null;Pump();}
 internal void Reset(){flight=null;hasLatest=hasDelivered=false;sentAt=Double.NegativeInfinity;}
}

// Register only on the coordinator HWND. Legacy input remains enabled.
internal sealed class DesktopRawMouseInput : IDisposable {
 [StructLayout(LayoutKind.Sequential)]internal struct Device {public ushort page,usage;public uint flags;public IntPtr target;}
 [StructLayout(LayoutKind.Sequential)]struct Header {public uint type,size;public IntPtr device,wParam;}
 [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterRawInputDevices([In]Device[] devices,uint count,uint size);
 [DllImport("user32.dll")]static extern uint GetRegisteredRawInputDevices([Out]Device[] devices,ref uint count,uint size);
 [DllImport("user32.dll")]static extern uint GetRawInputData(IntPtr input,uint command,out Header header,ref uint bytes,uint headerSize);
 readonly IntPtr target;readonly Action changed;readonly uint headerSize=(uint)Marshal.SizeOf(typeof(Header));bool registered,disposed;long dispatched;
 internal DesktopRawMouseInput(IntPtr window,Action callback){target=window;changed=callback;}
 internal bool Registered {get{return registered;}}
 bool RegistrationOwner(out bool ours){ours=false;uint count=0,size=(uint)Marshal.SizeOf(typeof(Device));if(GetRegisteredRawInputDevices(null,ref count,size)==UInt32.MaxValue||count>256)return false;if(count==0)return true;var devices=new Device[count];if(GetRegisteredRawInputDevices(devices,ref count,size)==UInt32.MaxValue)return false;foreach(var device in devices)if(device.page==1&&device.usage==2){ours=device.target==target;return ours;}return true;}
 internal void Enable(bool enabled){if(disposed||registered==enabled)return;bool ours;if(!RegistrationOwner(out ours)){if(!enabled)registered=false;return;}if(!enabled&&!ours){registered=false;return;}var input=new Device{page=1,usage=2,flags=enabled?0x100u:1u,target=enabled?target:IntPtr.Zero};if(RegisterRawInputDevices(new[]{input},1,(uint)Marshal.SizeOf(typeof(Device))))registered=enabled;}
 internal void WindowMessage(int message,IntPtr input){if(message!=0xFF||!registered||disposed)return;Header header;uint bytes=headerSize;if(GetRawInputData(input,0x10000005,out header,ref bytes,headerSize)!=headerSize||header.type!=0)return;
  long now=Stopwatch.GetTimestamp();if(dispatched!=0&&(now-dispatched)*1000.0/Stopwatch.Frequency<8)return;dispatched=now;changed();
 }
 public void Dispose(){if(disposed)return;Enable(false);disposed=true;}
}
