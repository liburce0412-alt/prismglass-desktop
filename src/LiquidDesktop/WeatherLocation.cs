using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class WeatherPlace {
 public string name;public double latitude,longitude;
}
internal static class WeatherLocation {
 const int Limit=262144;
 static readonly HttpClient client=CreateClient();
 static HttpClient CreateClient(){var value=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,SslProtocols=System.Security.Authentication.SslProtocols.Tls12});value.Timeout=TimeSpan.FromSeconds(8);value.MaxResponseContentBufferSize=Limit;return value;}
 static bool Coordinate(double value,double max){return !Double.IsNaN(value)&&!Double.IsInfinity(value)&&Math.Abs(value)<=max;}
 static bool Name(string value){if(String.IsNullOrWhiteSpace(value)||value.Length>200)return false;foreach(char c in value)if(Char.IsControl(c)||c=='['||c==']'||c=='#'||c=='"'||c=='\\')return false;return true;}
 public static string SearchUrl(string query){query=(query??"").Trim();if(query.Length<2||query.Length>80)throw new ArgumentException("请输入 2–80 个字的城市名称。");foreach(char c in query)if(Char.IsControl(c))throw new ArgumentException("城市名称不能包含换行或控制字符。");return "https://geocoding-api.open-meteo.com/v1/search?name="+Uri.EscapeDataString(query)+"&count=10&language=zh&format=json";}
 public static List<WeatherPlace> ParseResults(string text){
  if(text==null||Encoding.UTF8.GetByteCount(text)>Limit)throw new IOException("城市搜索返回的数据过大，请稍后重试。");
  var root=new JavaScriptSerializer{MaxJsonLength=Limit}.DeserializeObject(text) as Dictionary<string,object>;if(root==null)throw new IOException("城市搜索返回的数据无效。");
  object raw;if(root.TryGetValue("error",out raw)&&Object.Equals(raw,true))throw new IOException("城市服务暂时无法完成搜索，请换个名称或稍后重试。");
  var result=new List<WeatherPlace>();if(!root.TryGetValue("results",out raw))return result;var rows=raw as IEnumerable;if(rows==null||raw is string)throw new IOException("城市搜索结果无效。");
  foreach(var item in rows){var row=item as Dictionary<string,object>;object lat,lon,n;if(row==null||!row.TryGetValue("latitude",out lat)||!row.TryGetValue("longitude",out lon)||!row.TryGetValue("name",out n)||!(n is string))continue;double a,b;if(lat is bool||lon is bool||!Double.TryParse(Convert.ToString(lat,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out a)||!Double.TryParse(Convert.ToString(lon,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out b)||!Coordinate(a,90)||!Coordinate(b,180))continue;
   var names=new List<string>{(string)n};foreach(string key in new[]{"admin4","admin3","admin2","admin1","country"}){object part;if(row.TryGetValue(key,out part)&&part is string&&!String.IsNullOrWhiteSpace((string)part)&&!names.Contains((string)part))names.Add((string)part);}object feature;if(row.TryGetValue("feature_code",out feature)&&Object.Equals(feature,"PPL"))names[0]+="（居民点）";string label=String.Join(" · ",names);if(!Name(label))continue;result.Add(new WeatherPlace{name=label,latitude=a,longitude=b});if(result.Count==10)break;
  }return result;
 }
 public static async Task<List<WeatherPlace>> Search(string query,CancellationToken cancel){using(var response=await client.GetAsync(SearchUrl(query),HttpCompletionOption.ResponseContentRead,cancel).ConfigureAwait(false)){if(!response.IsSuccessStatusCode)throw new IOException("城市服务暂时不可用，请稍后重试。");string text=await response.Content.ReadAsStringAsync().ConfigureAwait(false);cancel.ThrowIfCancellationRequested();return ParseResults(text);}}
 public static string ReadText(string root){string file=Path.Combine(root,"weather-location.txt");if(!File.Exists(file))return null;if(new FileInfo(file).Length>4096)throw new IOException("天气城市设置文件过大，原文件已保留。");return DesktopSettingsFiles.Read(file);}
 // Existing installations may only have coordinates in their collector skin.
 // Read them without changing the saved city or choosing a new default city.
 internal static WeatherPlace ReadCollectorLocation(string file){
  if(!File.Exists(file))return null;if(new FileInfo(file).Length>65536)throw new IOException("天气采集配置过大。");string section="",url="",name="已配置位置";
  foreach(string raw in File.ReadAllLines(file)){string line=raw.Trim();if(line.StartsWith("[",StringComparison.Ordinal)&&line.EndsWith("]",StringComparison.Ordinal)){section=line.Substring(1,line.Length-2);continue;}int equals=line.IndexOf('=');if(equals<1)continue;string key=line.Substring(0,equals).Trim(),value=line.Substring(equals+1).Trim();if(section=="Feed"&&key=="URL")url=value;if(section=="City"&&key=="Text"&&value!="示例位置"&&Name(value))name=value;}
  Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="api.open-meteo.com")return null;double a=Double.NaN,b=Double.NaN;
  foreach(string pair in uri.Query.TrimStart('?').Split('&')){var parts=pair.Split('=');if(parts.Length!=2)continue;double coordinate;if(!Double.TryParse(Uri.UnescapeDataString(parts[1]),NumberStyles.Float,CultureInfo.InvariantCulture,out coordinate))continue;if(parts[0]=="latitude")a=coordinate;if(parts[0]=="longitude")b=coordinate;}
  return Coordinate(a,90)&&Coordinate(b,180)?new WeatherPlace{name=name,latitude=a,longitude=b}:null;
 }
 public static WeatherPlace ParseSaved(string text){if(text==null)return null;var lines=text.Replace("\r","").TrimEnd('\n').Split('\n');double a,b;if(lines.Length!=3||!Double.TryParse(lines[0],NumberStyles.Float,CultureInfo.InvariantCulture,out a)||!Double.TryParse(lines[1],NumberStyles.Float,CultureInfo.InvariantCulture,out b)||!Coordinate(a,90)||!Coordinate(b,180)||!Name(lines[2]))throw new IOException("天气城市设置无效，原文件已保留。");return new WeatherPlace{latitude=a,longitude=b,name=lines[2]};}
 public static void Save(string root,WeatherPlace place,string expected){if(place==null||!Coordinate(place.latitude,90)||!Coordinate(place.longitude,180)||!Name(place.name))throw new ArgumentException("所选城市无效，请重新搜索。");DesktopSettingsFiles.Locked(()=>{string before=ReadText(root);ParseSaved(before);if(before!=expected)throw new IOException("城市已在另一窗口更新，请重新打开天气设置。");DesktopSettingsFiles.Write(Path.Combine(root,"weather-location.txt"),place.latitude.ToString("R",CultureInfo.InvariantCulture)+"\n"+place.longitude.ToString("R",CultureInfo.InvariantCulture)+"\n"+place.name+"\n");});}
}
