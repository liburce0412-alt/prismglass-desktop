using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

internal sealed class LyricPaletteState {
 public string[] album,wallpaper;
 public string albumKey="",wallpaperKey="",status="暂无专辑封面 · 跟随壁纸";
}
internal static class LyricBackgroundPalette {
 static readonly string[] Neutral={"#263f62","#39595f","#40365d"};
 sealed class Sample {public string key;public Task<string[]> colors;}
 static readonly object gate=new object();static readonly List<Sample> cache=new List<Sample>();
 public static LyricPaletteState Read(string root,MediaSnapshot song){
  string wallpaper=Path.Combine(root,"wallpaper.png"),cover=CoverPath(root,song),wallpaperKey,coverKey;
  var backdrop=ReadColors(wallpaper,out wallpaperKey)??Neutral;var album=ReadColors(cover,out coverKey);
  return new LyricPaletteState{wallpaper=backdrop,album=album??backdrop,wallpaperKey=wallpaperKey,albumKey=coverKey.Length==0?wallpaperKey:coverKey,status=album!=null?"使用当前专辑的配色":cover.Length>0?"封面配色暂不可用 · 跟随壁纸":"暂无专辑封面 · 跟随壁纸"};
 }
 internal static string CoverPath(string root,MediaSnapshot song){
  if(song==null)return "";
  string data=Path.GetFullPath(Path.Combine(root,"..","music-data"));
  if(Regex.IsMatch(song.cover??"",@"^session-cover-[a-f0-9]{16}\.bin$")){
   string session=Path.Combine(data,song.cover);if(File.Exists(session))return session;
  }
  try{string file=Path.Combine(data,"now.txt");if(new FileInfo(file).Length>16384)return "";var lines=File.ReadAllLines(file);if(lines.Length<3||!String.Equals(lines[0].Trim(),song.title,StringComparison.Ordinal)||!String.Equals(lines[1].Trim(),song.artist,StringComparison.Ordinal))return "";string path=Path.GetFullPath(lines[2]);if(path.StartsWith(data+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(path))return path;}catch{}
  return "";
 }
 static string[] ReadColors(string path,out string key){
  key="";try{if(String.IsNullOrEmpty(path)||!File.Exists(path))return null;var file=new FileInfo(path);if(file.Length==0||file.Length>8388608)return null;key=file.FullName+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks;
   string cacheKey=key;lock(gate){var found=cache.Find(item=>item.key==cacheKey);if(found==null){while(cache.Count>=16){var old=cache.Find(item=>item.colors.IsCompleted);if(old==null)return null;cache.Remove(old);}string source=path;found=new Sample{key=key,colors=Task.Run(()=>{try{return Extract(source);}catch{return null;}})};cache.Add(found);}return found.colors.Status==TaskStatus.RanToCompletion?found.colors.Result:null;}
  }catch{return null;}
 }
 // Quantize a tiny thumbnail, keeping distinct saturated colors instead of cover text.
 internal static string[] Extract(string file){
  var buckets=new Dictionary<int,double>();
  using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
  using(var image=Image.FromStream(stream,false,true))using(var sample=new Bitmap(24,24)){
   if(image.Width>16384||image.Height>16384)throw new InvalidDataException("Cover is too large.");
   using(var g=Graphics.FromImage(sample)){g.InterpolationMode=InterpolationMode.HighQualityBilinear;g.DrawImage(image,new Rectangle(0,0,24,24));}
   for(int y=0;y<24;y++)for(int x=0;x<24;x++){var c=sample.GetPixel(x,y);if(c.A<128)continue;int max=Math.Max(c.R,Math.Max(c.G,c.B)),min=Math.Min(c.R,Math.Min(c.G,c.B));if(max<18||min>235)continue;int code=(c.R/32<<6)|(c.G/32<<3)|c.B/32;double value;buckets.TryGetValue(code,out value);buckets[code]=value+1+(max-min)/128.0;}
  }
  if(buckets.Count==0)return Neutral.ToArray();var colors=new List<Color>();
  foreach(var pair in buckets.OrderByDescending(item=>item.Value)){var color=Color.FromArgb(((pair.Key>>6)&7)*32+16,((pair.Key>>3)&7)*32+16,(pair.Key&7)*32+16);if(colors.Any(other=>Math.Abs(other.R-color.R)+Math.Abs(other.G-color.G)+Math.Abs(other.B-color.B)<100))continue;colors.Add(color);if(colors.Count==3)break;}
  while(colors.Count<3)colors.Add(colors[0]);return colors.Select(c=>"#"+c.R.ToString("x2")+c.G.ToString("x2")+c.B.ToString("x2")).ToArray();
 }
}
