using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
class TestCompanionVirtualDisplay {
 static int checks;
 static void Check(bool condition,string name){if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static void Reject(Action action,string name){bool caught=false;try{action();}catch(InvalidOperationException){caught=true;}Check(caught,name);}
 static CompanionDisplayTopology.DisplayPath Path(uint target,uint source,uint sourceMode,uint targetMode,uint adapter){return new CompanionDisplayTopology.DisplayPath{flags=1,source=new CompanionDisplayTopology.Source{adapter=new CompanionDisplayTopology.Luid{low=adapter},id=source,mode=sourceMode},target=new CompanionDisplayTopology.Target{adapter=new CompanionDisplayTopology.Luid{low=adapter},id=target,mode=targetMode,available=true,rotation=1,scaling=1,refresh=new CompanionDisplayTopology.Rational{numerator=60,denominator=1},scanline=1}};}
 static CompanionDisplayTopology.Mode Source(uint id,uint adapter,int x,int y,uint width,uint height){return new CompanionDisplayTopology.Mode{type=1,id=id,adapter=new CompanionDisplayTopology.Luid{low=adapter},source=new CompanionDisplayTopology.SourceMode{width=width,height=height,format=4,position=new CompanionDisplayTopology.Point{x=x,y=y}}};}
 static CompanionDisplayTopology.Mode Target(uint id,uint adapter){return new CompanionDisplayTopology.Mode{type=2,id=id,adapter=new CompanionDisplayTopology.Luid{low=adapter},signal=new CompanionDisplayTopology.Signal{a=100,b=200,c=((ulong)1<<32)|60,f=(ulong)1<<32}};}
 static CompanionDisplayTopology.Snapshot Fixture(){return new CompanionDisplayTopology.Snapshot{paths=new[]{Path(10,0,0,1,1),Path(11,0,0,2,1),Path(12,1,3,4,1),Path(256,0,5,6,2)},modes=new[]{Source(0,1,0,0,2560,1440),Target(10,1),Target(11,1),Source(1,1,-1920,200,1920,1080),Target(12,1),Source(0,2,2560,0,1440,1080),Target(256,2)}};}
 static bool Virtual(CompanionDisplayTopology.DisplayPath path){return path.target.adapter.low==2;}
 [STAThread]static int Main(string[] args){try{
  if(args.Length==1&&(args[0]=="--describe"||args[0]=="--validate")){
   var display=new CompanionVirtualDisplay(AppDomain.CurrentDomain.BaseDirectory);var screens=display.PhysicalMonitors();Console.WriteLine("extensionAvailable="+display.Available+"; physicalLogicalScreens="+screens.Count);foreach(var screen in screens)Console.WriteLine(screen.Id+" "+screen.DeviceName+" "+screen.Name+" "+screen.Bounds);
   if(display.Available){var all=CompanionDisplayTopology.Read(1);var target=CompanionDisplayTopology.FindVirtual(all);var nativePreferred=CompanionDisplayTopology.Preferred(target.target);Console.WriteLine("preferred="+nativePreferred.width+"x"+nativePreferred.height);
    if(args[0]=="--validate"){var disabled=CompanionDisplayTopology.Disable(all,target);Console.WriteLine("Validating disabled plan...");CompanionDisplayTopology.Validate(disabled);Console.WriteLine("Validating enabled plan...");CompanionDisplayTopology.Validate(CompanionDisplayTopology.Enable(disabled,target,nativePreferred));Console.WriteLine("Both native plans validate; no display state was applied.");}
   }
   return 0;
  }
  CompanionDisplayTopology.CheckSizes();Check(true,"CCD native structure sizes match Windows ABI");
  Check(CompanionDisplayTopology.MatchesVirtualIdentity(@"\\?\DISPLAY#MTT1337#x",@"\\?\ROOT#DISPLAY#0000#{x}",new[]{@"Root\MttVDD"}),"specific driver and monitor pair is recognized");
  Check(!CompanionDisplayTopology.MatchesVirtualIdentity(@"\\?\DISPLAY#MTT1337#x",@"\\?\ROOT#DISPLAY#0000#{x}",new[]{@"Root\Other"}),"same monitor name on another driver is rejected");
  Check(!CompanionDisplayTopology.MatchesVirtualIdentity(@"\\?\DISPLAY#DELL#x",@"\\?\ROOT#DISPLAY#0000#{x}",new[]{@"Root\MttVDD"}),"physical monitor on unexpected adapter is rejected");
  Check(!CompanionDisplayTopology.MatchesVirtualIdentity(@"\\?\DISPLAY#MTT1337#x",@"\\?\PCI#DISPLAY#0000#{x}",new[]{@"Root\MttVDD"}),"non-root display adapter is rejected");
  var initial=Fixture();string original=CompanionDisplayTopology.Fingerprint(initial,Virtual);var off=CompanionDisplayTopology.Disable(initial,initial.paths[3]);
  Check(off.paths.Length==3&&off.paths.All(path=>!Virtual(path)),"disable removes only the virtual path");
  Check(original==CompanionDisplayTopology.Fingerprint(off,Virtual),"disable preserves two physical clones and another differently positioned screen");
  Check(initial.paths.Length==4&&initial.modes[5].source.position.x==2560,"planning leaves input snapshot unchanged");
  var preferred=new CompanionDisplayTopology.PreferredMode{width=1440,height=1080,signal=new CompanionDisplayTopology.Signal{a=46656000,c=((ulong)1<<32)|30,f=(ulong)1<<32}};
  var on=CompanionDisplayTopology.Enable(off,initial.paths[3],preferred);
  Check(on.paths.Length==4&&original==CompanionDisplayTopology.Fingerprint(on,Virtual),"enable preserves all physical timing and clone relationships");
  Check(CompanionDisplayTopology.Bounds(on,on.paths[3])==new Rectangle(2560,0,1440,1080),"virtual screen is independently placed to the right of physical bounds");
  Check(on.paths[3].target.refresh.numerator==30&&on.paths[3].target.refresh.denominator==1,"preferred virtual refresh rate is retained");
  var captureBounds=new Rectangle(2560,0,1440,1080);
  Check(CompanionDisplayTopology.SameCaptureTarget(on.paths[3],captureBounds,on,on.paths[3]),"unchanged active virtual target remains a valid capture");
  var inactive=on.paths[3];inactive.flags=0;Check(!CompanionDisplayTopology.SameCaptureTarget(on.paths[3],captureBounds,on,inactive),"externally disabled virtual target invalidates capture");
  var movedBounds=new Rectangle(0,1440,1440,1080);Check(!CompanionDisplayTopology.SameCaptureTarget(on.paths[3],movedBounds,on,on.paths[3]),"changed virtual coordinates invalidate the original capture rectangle");
  var replaced=on.paths[3];replaced.target.id++;Check(!CompanionDisplayTopology.SameCaptureTarget(on.paths[3],captureBounds,on,replaced),"replacement virtual target cannot reuse an existing capture");
  var shared=initial.paths[3];shared.source=off.paths[0].source;Reject(()=>CompanionDisplayTopology.Enable(off,shared,preferred),"shared physical source cannot be repurposed for a virtual screen");
  var invalid=preferred;invalid.width=0;Reject(()=>CompanionDisplayTopology.Enable(off,initial.paths[3],invalid),"zero-sized preferred mode is rejected");
  invalid.width=8193;Reject(()=>CompanionDisplayTopology.Enable(off,initial.paths[3],invalid),"unbounded virtual resolution is rejected");
  var only=new CompanionDisplayTopology.Snapshot{paths=new[]{initial.paths[3]},modes=initial.modes};Reject(()=>CompanionDisplayTopology.Disable(only,only.paths[0]),"sole active display cannot be disabled");
  var positions=new[]{new DesktopIcons.Position{path="first",x=16,y=2},new DesktopIcons.Position{path="other-physical",x=-1900,y=500},new DesktopIcons.Position{path="virtual",x=3000,y=50},new DesktopIcons.Position{path="removed",x=9000,y=9000}};
  var filtered=CompanionVirtualDisplay.VisiblePositions(positions,new[]{new Rectangle(0,0,2560,1440),new Rectangle(-1920,200,1920,1080)});
  Check(filtered.Length==2&&filtered[0].path=="first"&&filtered[1].path=="other-physical","icon recovery restores only positions within current physical displays");
  var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();var restored=serializer.Deserialize<DesktopIcons.Position[]>(serializer.Serialize(positions));Check(restored.Length==positions.Length&&restored[1].x==-1900&&restored[1].path=="other-physical","private icon journal round-trips its shell identities and negative coordinates");
  Reject(()=>CompanionVirtualDisplay.VisiblePositions(null,new Rectangle[0]),"invalid icon journal is retained through a reported error");
  Console.WriteLine(checks+" virtual-display checks passed without changing displays or desktop icons.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);if(ex is System.ComponentModel.Win32Exception)Console.Error.WriteLine("NativeError="+((System.ComponentModel.Win32Exception)ex).NativeErrorCode);return 1;}}
}
