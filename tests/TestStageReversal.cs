using System;using System.Reflection;
internal static class TestStageReversal {
 static readonly BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;static Type type;static object flight;
 static double Read(string name){return (double)type.GetField(name,flags).GetValue(flight);}
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static bool Advance(double time){return (bool)type.GetMethod("Advance",flags).Invoke(flight,new object[]{time});}
 static void Retarget(double goal,double time){type.GetMethod("Retarget",flags).Invoke(flight,new object[]{goal,time});}
 static int Main(string[] args){try{
  type=Assembly.LoadFrom(args[0]).GetType("StageManager+StageFlight",true);flight=Activator.CreateInstance(type,true);Retarget(1,0);Advance(.09);double position=Read("position"),velocity=Read("velocity");Check(position>0&&position<1&&velocity>1,"flight is moving before the reversal");
  Retarget(0,.09);Check(Read("position")==position&&Read("velocity")==velocity,"reversal preserves both the presented position and its velocity");
  Advance(.090001);Check(Math.Abs((Read("position")-position)/.000001-velocity)<.001,"first reversed frame has the same position derivative instead of stopping");
  Check(Math.Abs(Read("velocity")-velocity)<.001,"velocity changes continuously under the new target");Advance(.18);Check(Read("velocity")<0&&Read("position")>0&&Read("position")<1,"retained momentum slows and then turns toward the new group");
  Check(Advance(.8)&&Read("position")==0&&Read("velocity")==0,"reversed flight settles exactly at its new target");Console.WriteLine("One reversal scenario passed (6 checks).");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
