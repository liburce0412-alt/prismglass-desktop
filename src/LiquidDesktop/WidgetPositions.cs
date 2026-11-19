using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

// Rainmeter's temporary on-screen corrections must not become the saved layout.
internal sealed class WidgetPositions {
 internal sealed class Anchor {public double x,y;}
 sealed class State {public int version=1;public Dictionary<string,string> homes=new Dictionary<string,string>();public Dictionary<string,Anchor> positions=new Dictionary<string,Anchor>();}
 readonly string file;readonly JavaScriptSerializer json=new JavaScriptSerializer();State state;
 internal WidgetPositions(string root){file=Path.Combine(root,"widget-positions.json");}
 static string Key(string monitor,string name){return monitor+"\n"+name;}
 State Read(){
  var text=DesktopSettingsFiles.Read(file);var next=text==null?new State():json.Deserialize<State>(text);
  if(next==null||next.version!=1||next.homes==null||next.positions==null||next.homes.Count>64||next.positions.Count>2048)throw new IOException("小组件位置记录无效，原文件已保留。");
  foreach(var pair in next.homes)if(String.IsNullOrEmpty(pair.Key)||pair.Key.Length>64||String.IsNullOrEmpty(pair.Value)||pair.Value.Length>512)throw new IOException("小组件屏幕记录无效。");
  foreach(var pair in next.positions){var a=pair.Value;if(pair.Key.Length>600||a==null||Double.IsNaN(a.x)||Double.IsInfinity(a.x)||Double.IsNaN(a.y)||Double.IsInfinity(a.y)||a.x<0||a.x>1||a.y<0||a.y>1)throw new IOException("小组件位置超出范围，原文件已保留。");}return next;
 }
 internal static string Nearest(Rectangle bounds,IDictionary<string,Rectangle> screens){
  if(screens.Count==0)throw new IOException("没有可用显示器。");
  return screens.OrderByDescending(p=>{var r=Rectangle.Intersect(bounds,p.Value);return (long)Math.Max(0,r.Width)*Math.Max(0,r.Height);}).ThenBy(p=>Math.Pow(bounds.Left+bounds.Width/2.0-p.Value.Left-p.Value.Width/2.0,2)+Math.Pow(bounds.Top+bounds.Height/2.0-p.Value.Top-p.Value.Height/2.0,2)).ThenBy(p=>p.Key,StringComparer.Ordinal).First().Key;
 }
 internal void Remember(string name,Rectangle bounds,IDictionary<string,Rectangle> screens){
  string monitor=Nearest(bounds,screens);var area=screens[monitor];if(area.Width<1||area.Height<1)throw new IOException("显示器工作区无效。");
  DesktopSettingsFiles.Locked(()=>{var next=Read();next.homes[name]=monitor;next.positions[Key(monitor,name)]=new Anchor{x=Math.Max(0,Math.Min(1,(bounds.Left-area.Left)/(double)area.Width)),y=Math.Max(0,Math.Min(1,(bounds.Top-area.Top)/(double)area.Height))};DesktopSettingsFiles.Write(file,json.Serialize(next));state=next;});
 }
 internal Rectangle Restore(string name,Rectangle current,IDictionary<string,Rectangle> screens,out bool temporary){
  if(state==null)state=Read();string home;if(!state.homes.TryGetValue(name,out home)){Remember(name,current,screens);home=state.homes[name];}
  string target=screens.ContainsKey(home)?home:Nearest(current,screens);temporary=target!=home;var area=screens[target];Anchor anchor;
  if(!state.positions.TryGetValue(Key(target,name),out anchor)&&!state.positions.TryGetValue(Key(home,name),out anchor))throw new IOException("小组件位置记录缺少锚点。");
  int x=area.Left+(int)Math.Round(anchor.x*area.Width),y=area.Top+(int)Math.Round(anchor.y*area.Height);
  var next=new Rectangle(Math.Max(area.Left,Math.Min(x,area.Right-current.Width)),Math.Max(area.Top,Math.Min(y,area.Bottom-current.Height)),current.Width,current.Height);temporary|=next.X!=x||next.Y!=y;return next;
 }
}
