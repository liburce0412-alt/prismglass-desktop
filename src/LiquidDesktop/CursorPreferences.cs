using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal sealed class CursorPreferences {
 public string style="rounded-dark",trailStyle="particles",trailColor="#79C3E3";
 public bool trailEnabled=true;
 public static readonly string[] Styles={"rounded-dark","rounded-light","macos","windows"};
 public static readonly string[] Trails={"particles","stars","ribbon"};
 public static readonly int[] Sizes={75,100,125,150,200};
 public CursorPreferences Copy(){return (CursorPreferences)MemberwiseClone();}
 public static bool IsColor(string value){return value!=null&&Regex.IsMatch(value,@"\A#[0-9a-fA-F]{6}\z");}
 public bool Change(string name,string value){
  if(name=="cursor-style"&&Array.IndexOf(Styles,value)>=0){style=value;return true;}
  if(name=="cursor-trail"&&(value=="true"||value=="false")){trailEnabled=value=="true";return true;}
  if(name=="cursor-trail-style"&&Array.IndexOf(Trails,value)>=0){trailStyle=value;return true;}
  if(name=="cursor-trail-color"&&IsColor(value)){trailColor=value.ToUpperInvariant();return true;}
  return false;
 }
 public static CursorPreferences Load(string directory){
  CursorPreferences result=null;
  try{var file=Path.Combine(directory,"cursor-options.json");if(File.Exists(file))result=new JavaScriptSerializer().Deserialize<CursorPreferences>(File.ReadAllText(file));}catch{}
  if(result==null)result=new CursorPreferences();
  if(Array.IndexOf(Styles,result.style)<0)result.style="rounded-dark";
  if(Array.IndexOf(Trails,result.trailStyle)<0)result.trailStyle="particles";
  result.trailColor=IsColor(result.trailColor)?result.trailColor.ToUpperInvariant():"#79C3E3";
  return result;
 }
 public static int ReadScale(string directory){try{int value;return Int32.TryParse(File.ReadAllText(Path.Combine(directory,"cursor-scale.txt")).Trim(),out value)&&value>=75&&value<=200?value:100;}catch{return 100;}}
 public void Save(string directory){
  string file=Path.Combine(directory,"cursor-options.json"),temp=file+".tmp";
  try{File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
