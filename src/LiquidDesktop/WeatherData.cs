using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// One collector per installation is shared by the desktop and companion views.
// Open-Meteo current values are model estimates, not station observations.
internal sealed class WeatherData {
 const int Limit=65536;
 static readonly object instancesGate=new object();
 static readonly Dictionary<string,WeatherData> instances=new Dictionary<string,WeatherData>(StringComparer.OrdinalIgnoreCase);
 static readonly HttpClient client=CreateClient();
 readonly object gate=new object();readonly string root;readonly Func<string> collectorFile;readonly Func<string,Task<string>> fetch;readonly Func<DateTime> clock;
 WeatherPlace place;string placeKey="",failure="";Dictionary<string,object> sample;DateTime locationAt=DateTime.MinValue,locationWrite=DateTime.MinValue,nextAttempt=DateTime.MinValue;Task pending;
 public static WeatherData ForRoot(string root,Func<string> collectorFile){string full=Path.GetFullPath(root),volume=Path.GetPathRoot(full);root=full.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);if(root.Length<volume.Length)root=volume;lock(instancesGate){WeatherData value;if(!instances.TryGetValue(root,out value))instances[root]=value=new WeatherData(root,collectorFile,Fetch,()=>DateTime.UtcNow);return value;}}
 internal WeatherData(string root,Func<string> collectorFile,Func<string,Task<string>> fetch,Func<DateTime> clock){this.root=root;this.collectorFile=collectorFile;this.fetch=fetch;this.clock=clock;}
 static HttpClient CreateClient(){var value=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,SslProtocols=System.Security.Authentication.SslProtocols.Tls12});value.Timeout=TimeSpan.FromSeconds(8);value.MaxResponseContentBufferSize=Limit;return value;}
 internal static async Task<string> Fetch(string url){using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseContentRead).ConfigureAwait(false)){response.EnsureSuccessStatusCode();return await response.Content.ReadAsStringAsync().ConfigureAwait(false);}}
 static long Milliseconds(DateTime value){return (long)(value.ToUniversalTime()-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
 static string Key(WeatherPlace value){return value.latitude.ToString("R",CultureInfo.InvariantCulture)+"\n"+value.longitude.ToString("R",CultureInfo.InvariantCulture)+"\n"+value.name;}
 internal static string Url(WeatherPlace value,bool cma){return "https://api.open-meteo.com/v1/forecast?latitude="+value.latitude.ToString("R",CultureInfo.InvariantCulture)+"&longitude="+value.longitude.ToString("R",CultureInfo.InvariantCulture)+"&current=temperature_2m,apparent_temperature,weather_code&daily=temperature_2m_max,temperature_2m_min,sunrise,sunset&timezone=auto&forecast_days=1&timeformat=unixtime"+(cma?"&models=cma_grapes_global":"");}
 static object Value(Dictionary<string,object> map,string key){object value;return map!=null&&map.TryGetValue(key,out value)?value:null;}
 static double? Number(object raw,double min,double max){if(!(raw is int||raw is long||raw is double||raw is decimal))return null;double value=Convert.ToDouble(raw,CultureInfo.InvariantCulture);return Double.IsNaN(value)||Double.IsInfinity(value)||value<min||value>max?(double?)null:value;}
 static double? First(Dictionary<string,object> map,string key,double min,double max){var values=Value(map,key) as IList;return values==null||values.Count!=1?null:Number(values[0],min,max);}
 static double? Code(object raw){double? value=Number(raw,0,99);return value.HasValue&&Array.IndexOf(new[]{0,1,2,3,45,48,51,53,55,56,57,61,63,65,66,67,71,73,75,77,80,81,82,85,86,95,96,99},(int)value.Value)>=0&&value==Math.Floor(value.Value)?value:null;}
 static DateTime LocalTime(double seconds,double offset){return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds+offset);}
 internal static Dictionary<string,object> Parse(string body,WeatherPlace place,DateTime fetchedAt,bool cma){
  if(String.IsNullOrEmpty(body)||body.Length>Limit)throw new IOException("天气响应为空或过大。");
  var data=new JavaScriptSerializer{MaxJsonLength=Limit}.DeserializeObject(body) as Dictionary<string,object>;if(data==null||Object.Equals(Value(data,"error"),true))throw new IOException("天气服务未返回有效数据。");
  var current=Value(data,"current") as Dictionary<string,object>;var units=Value(data,"current_units") as Dictionary<string,object>;double? time=Number(Value(current,"time"),1,4102444800),temperature=Number(Value(current,"temperature_2m"),-100,80),offset=Number(Value(data,"utc_offset_seconds"),-50400,50400);string zone=Value(data,"timezone") as string;
  if(!time.HasValue||time!=Math.Floor(time.Value)||!temperature.HasValue||!offset.HasValue||offset!=Math.Floor(offset.Value)||String.IsNullOrWhiteSpace(zone)||zone.Length>100||!Object.Equals(Value(units,"time"),"unixtime")||!Object.Equals(Value(units,"temperature_2m"),"°C"))throw new IOException("天气数据缺少有效时间、温度或单位。");
  long stamp=(long)time.Value*1000,received=Milliseconds(fetchedAt);if(stamp>received+60000||stamp<received-7200000)throw new IOException("天气服务返回的数据已过期或时间无效。");
  var daily=Value(data,"daily") as Dictionary<string,object>;var dailyUnits=Value(data,"daily_units") as Dictionary<string,object>;double? day=First(daily,"time",1,4102444800);bool sameDay=day.HasValue&&Object.Equals(Value(dailyUnits,"time"),"unixtime")&&LocalTime(day.Value,offset.Value).Date==LocalTime(time.Value,offset.Value).Date;
  double? high=sameDay&&Object.Equals(Value(dailyUnits,"temperature_2m_max"),"°C")?First(daily,"temperature_2m_max",-100,80):null,low=sameDay&&Object.Equals(Value(dailyUnits,"temperature_2m_min"),"°C")?First(daily,"temperature_2m_min",-100,80):null;if(high.HasValue&&low.HasValue&&low>high){high=null;low=null;}
  double? rise=sameDay&&Object.Equals(Value(dailyUnits,"sunrise"),"unixtime")?First(daily,"sunrise",1,4102444800):null,set=sameDay&&Object.Equals(Value(dailyUnits,"sunset"),"unixtime")?First(daily,"sunset",1,4102444800):null;
  return new Dictionary<string,object>{{"ready",true},{"stale",false},{"reason",""},{"location",place.name},{"temp",temperature},{"feelsLike",Object.Equals(Value(units,"apparent_temperature"),"°C")?Number(Value(current,"apparent_temperature"),-120,100):null},{"code",Object.Equals(Value(units,"weather_code"),"wmo code")?Code(Value(current,"weather_code")):null},{"high",high},{"low",low},{"sunrise",rise.HasValue&&LocalTime(rise.Value,offset.Value).Date==LocalTime(time.Value,offset.Value).Date?LocalTime(rise.Value,offset.Value).ToString("yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture):""},{"sunset",set.HasValue&&LocalTime(set.Value,offset.Value).Date==LocalTime(time.Value,offset.Value).Date?LocalTime(set.Value,offset.Value).ToString("yyyy-MM-dd'T'HH:mm",CultureInfo.InvariantCulture):""},{"updatedAt",stamp},{"fetchedAt",received},{"timezone",zone},{"utcOffsetSeconds",(int)offset.Value},{"source",cma?"CMA GRAPES · Open-Meteo":"Open-Meteo"},{"kind","model"}};
 }
 void LoadCache(DateTime now){try{string file=Path.Combine(root,"weather-cache.json");if(!File.Exists(file)||new FileInfo(file).Length>Limit*2)return;var saved=new JavaScriptSerializer{MaxJsonLength=Limit*2}.DeserializeObject(File.ReadAllText(file)) as Dictionary<string,object>;if(!Object.Equals(Value(saved,"locationKey"),placeKey))return;double? at=Number(Value(saved,"fetchedAt"),1,4102444800000);if(!at.HasValue||at>Milliseconds(now)+60000||at<Milliseconds(now)-7200000)return;sample=Parse(Value(saved,"body") as string,place,new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(at.Value),Object.Equals(Value(saved,"cma"),true));}catch{}}
 public Dictionary<string,object> Read(){lock(gate){DateTime now=clock();try{DateTime written=File.GetLastWriteTimeUtc(Path.Combine(root,"weather-location.txt"));if(now>=locationAt||written!=locationWrite){locationAt=now.AddSeconds(3);locationWrite=written;
    WeatherPlace selected=WeatherLocation.ParseSaved(WeatherLocation.ReadText(root))??WeatherLocation.ReadCollectorLocation(collectorFile());string key=selected==null?"":Key(selected);if(key!=placeKey){place=selected;placeKey=key;sample=null;failure="";nextAttempt=DateTime.MinValue;if(place!=null)LoadCache(now);}else place=selected;
   }}catch{place=null;placeKey="";sample=null;failure="天气城市设置无法读取，请在设置中检查；原文件已保留。";}
   if(place!=null&&(pending==null||pending.IsCompleted)&&now>=nextAttempt){WeatherPlace requested=place;string key=placeKey;nextAttempt=now.AddMinutes(15);pending=Task.Run(()=>Refresh(requested,key));}
   var result=sample==null?new Dictionary<string,object>{{"temp",null},{"code",null},{"high",null},{"low",null},{"feelsLike",null},{"updatedAt",0L},{"fetchedAt",0L},{"source",""},{"kind","model"}}:new Dictionary<string,object>(sample);
   long stamp=Convert.ToInt64(result["updatedAt"]);double age=Milliseconds(now)-stamp;bool available=sample!=null&&age>=-60000&&age<=7200000;bool stale=sample!=null&&(failure.Length>0||age>1800000);
   result["ready"]=available;result["stale"]=stale;result["location"]=place==null?"":place.name;result["reason"]=failure.Length>0?failure:place==null?"请在设置中选择天气城市。":sample==null?"正在获取天气…":!available?"天气数据已过期，正在等待更新。":stale?"显示上次天气，正在等待更新。":Convert.ToString(result["reason"]);return result;
  }}
 internal Task Pending {get{lock(gate)return pending??Task.FromResult(0);}}
 async Task Refresh(WeatherPlace requested,string key){Dictionary<string,object> next=null;string body="",error="";bool cma=true;DateTime fetched=clock();
  try{body=await fetch(Url(requested,true)).ConfigureAwait(false);fetched=clock();next=Parse(body,requested,fetched,true);}catch{cma=false;}
  if(next==null)try{body=await fetch(Url(requested,false)).ConfigureAwait(false);fetched=clock();next=Parse(body,requested,fetched,false);next["reason"]="CMA 数据暂不可用，已切换到 Open-Meteo 综合预报。";}catch{error="天气更新失败，请检查网络；已有天气会保留并标记为上次数据。";}
  lock(gate){if(key!=placeKey)return;failure=error;nextAttempt=clock().AddSeconds(next==null?60:900);if(next==null)return;sample=next;try{DesktopSettingsFiles.Write(Path.Combine(root,"weather-cache.json"),new JavaScriptSerializer{MaxJsonLength=Limit*2}.Serialize(new{locationKey=key,fetchedAt=Milliseconds(fetched),cma=cma,body=body}));}catch{}}
 }
}
