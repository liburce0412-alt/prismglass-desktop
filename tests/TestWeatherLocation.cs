using System;using System.IO;using System.Globalization;using System.Collections.Generic;
internal static class TestWeatherLocation {
 static int checks;static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
 static void Reject(Action action,string name){bool refused=false;try{action();}catch(ArgumentException){refused=true;}catch(IOException){refused=true;}Check(refused,name);}
 static int Main(string[] args){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"weather-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{
  Check(WeatherLocation.ReadText(root)==null&&WeatherLocation.ParseSaved(null)==null,"unset city leaves the existing collector location unchanged");
  Check(WeatherLocation.SearchUrl(" 杭州 ")=="https://geocoding-api.open-meteo.com/v1/search?name=%E6%9D%AD%E5%B7%9E&count=10&language=zh&format=json","search sends only the encoded user city to the fixed provider");
  Reject(()=>WeatherLocation.SearchUrl("杭"),"single-character searches rejected before networking");Reject(()=>WeatherLocation.SearchUrl("杭州\n北京"),"control characters rejected");
  var places=WeatherLocation.ParseResults("{\"results\":[{\"name\":\"杭州\",\"latitude\":30.27,\"longitude\":120.15,\"admin1\":\"浙江省\",\"country\":\"中国\"},{\"name\":\"Invalid\",\"latitude\":91,\"longitude\":12},{\"name\":\"[#Unsafe]\",\"latitude\":3,\"longitude\":4}]}");
  Check(places.Count==1&&places[0].name=="杭州 · 浙江省 · 中国","results retain region labels and reject unsafe or invalid coordinates");
  Check(WeatherLocation.ParseResults("{}").Count==0,"empty results are valid and do not invent a city");Reject(()=>WeatherLocation.ParseResults("{\"error\":true}"),"provider errors are not treated as successful results");Reject(()=>WeatherLocation.ParseResults(new string(' ',262145)),"oversized results are rejected");
  CultureInfo previous=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("fr-FR");WeatherLocation.Save(root,places[0],null);}finally{CultureInfo.CurrentCulture=previous;}
  string saved=WeatherLocation.ReadText(root);Check(saved.StartsWith("30.27\n120.15\n")&&WeatherLocation.ParseSaved(saved).name==places[0].name,"city round trip uses invariant coordinates and preserves Chinese labels");
  Reject(()=>WeatherLocation.Save(root,new WeatherPlace{name="Other",latitude=0,longitude=0},null),"stale city edits cannot overwrite a newer selection");Check(WeatherLocation.ReadText(root)==saved,"conflicting save retains original bytes");
  Reject(()=>WeatherLocation.Save(root,new WeatherPlace{name="Invalid",latitude=Double.NaN,longitude=0},saved),"NaN cannot be saved");Reject(()=>WeatherLocation.ParseSaved("1\n2\n[!Execute]\n"),"saved skin expressions are rejected");Reject(()=>WeatherLocation.ParseSaved("NaN\n0\nInvalid\n"),"invalid saved coordinates are rejected");
  File.WriteAllText(Path.Combine(root,"weather-location.txt"),"broken");Reject(()=>WeatherLocation.Save(root,places[0],"broken"),"corrupt settings are not silently replaced");Check(File.ReadAllText(Path.Combine(root,"weather-location.txt"))=="broken","corrupt bytes remain available for recovery");
  if(Array.IndexOf(args,"--live")>=0){var found=WeatherLocation.Search("杭州",System.Threading.CancellationToken.None).GetAwaiter().GetResult();Check(found.Count>0&&found.Exists(p=>p.latitude>29&&p.latitude<31&&p.longitude>119&&p.longitude<121),"production city provider finds the public Hangzhou example");}
  Console.WriteLine("Completed "+checks+" weather location checks.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{Directory.Delete(root,true);}}
}
