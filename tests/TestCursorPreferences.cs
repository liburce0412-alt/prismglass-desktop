using System;
using System.IO;
class TestCursorPreferences {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(){
  string root=Path.Combine(Path.GetTempPath(),"PrismCursorPreferences-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try{
   var original=CursorPreferences.Load(root);
   Check(original.style=="rounded-dark"&&original.trailEnabled&&original.trailStyle=="particles","existing users retain the rounded pointer and particles");
   File.WriteAllText(Path.Combine(root,"cursor-scale.txt"),"150");
   Check(CursorPreferences.ReadScale(root)==150,"legacy 150 percent choice retained");
   var next=original.Copy();Check(next.Change("cursor-style","rounded-light"),"light pointer accepted");
   Check(next.Change("cursor-trail-style","ribbon")&&next.Change("cursor-trail-color","#abcdef")&&next.Change("cursor-trail","false"),"independent trail options accepted");
   next.Save(root);var restored=CursorPreferences.Load(root);
   Check(restored.style=="rounded-light"&&!restored.trailEnabled&&restored.trailStyle=="ribbon"&&restored.trailColor=="#ABCDEF","selection including disabled trail survives reload");
   Check(original.style=="rounded-dark"&&original.trailEnabled,"draft cannot mutate active settings");
   Check(!next.Change("cursor-style","../bad")&&!next.Change("cursor-trail","yes")&&!next.Change("cursor-trail-color","red;exit")&&!next.Change("cursor-trail-style","unknown"),"malformed commands and colors rejected");
   foreach(var style in CursorPreferences.Styles){Check(next.Change("cursor-style",style),"supported pointer "+style);next.Save(root);Check(CursorPreferences.Load(root).style==style,"saved pointer "+style);}
   foreach(var trail in CursorPreferences.Trails)Check(next.Change("cursor-trail-style",trail),"supported trail "+trail);
   File.WriteAllText(Path.Combine(root,"cursor-options.json"),"{\"style\":\"bad\",\"trailStyle\":null,\"trailColor\":\"#NOPE00\",\"trailEnabled\":false}");
   var bad=CursorPreferences.Load(root);Check(bad.style=="rounded-dark"&&bad.trailStyle=="particles"&&bad.trailColor=="#79C3E3"&&!bad.trailEnabled,"invalid fields recover without enabling disabled trail");
   File.WriteAllText(Path.Combine(root,"cursor-options.json"),"{broken");Check(CursorPreferences.Load(root).style=="rounded-dark","corrupt configuration has a safe default");
   Check(!File.Exists(Path.Combine(root,"cursor-options.json.tmp")),"atomic save leaves no temporary file");
  }catch(Exception ex){Console.WriteLine("FAIL "+ex.GetType().Name+": "+ex.Message);Environment.ExitCode=1;}
  finally{Directory.Delete(root,true);}
 }
}
