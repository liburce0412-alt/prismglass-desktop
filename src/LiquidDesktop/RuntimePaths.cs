using System;
using System.IO;
internal static class RuntimePaths {
 public static string Rainmeter {get {
  var config=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"rainmeter-path.txt");
  if(File.Exists(config)){var path=File.ReadAllText(config).Trim();if(File.Exists(path))return path;}
  var standard=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Rainmeter","Rainmeter.exe");
  if(File.Exists(standard))return standard;
  throw new FileNotFoundException("Run Install.ps1 with -RainmeterPath before starting PrismGlass.");
 }}
}
