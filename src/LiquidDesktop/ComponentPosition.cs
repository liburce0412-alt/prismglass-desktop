using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Positions belong to each display; only explicit grip drags write this sidecar.
internal sealed class ComponentPosition:IDisposable {
 readonly string file,kind;readonly Func<Rectangle> read;readonly Action<Rectangle> place;readonly Action<string> error;
 readonly Timer timer=new Timer{Interval=8};readonly JavaScriptSerializer json=new JavaScriptSerializer();
 Rectangle area,normal,footprint;Point anchor,start,origin;string monitor="";double? x,y;bool placing,disposed;
 public bool Dragging {get;private set;}
 internal static bool LayoutLocked;
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 public ComponentPosition(string root,string name,Func<Rectangle> bounds,Action<Rectangle> move,Action<string> failed){file=Path.Combine(root,"component-positions.json");kind=name;read=bounds;place=move;error=failed;timer.Tick+=(s,e)=>Step(Cursor.Position,GetAsyncKeyState(1)<0,GetAsyncKeyState(27)<0);}
 string Key {get{return monitor+"/"+kind;}}
 Dictionary<string,object> Map(){if(File.Exists(file)&&new FileInfo(file).Length>65536)throw new IOException("组件位置文件过大，原文件已保留。");return DesktopSettingsFiles.Map(DesktopSettingsFiles.Read(file));}
 void Load(){x=y=null;var map=Map();object raw;var value=map.TryGetValue(Key,out raw)?raw as Dictionary<string,object>:null;if(value==null)return;object a,b;if(value.TryGetValue("x",out a)&&value.TryGetValue("y",out b)){double px=Convert.ToDouble(a),py=Convert.ToDouble(b);if(!Double.IsNaN(px)&&!Double.IsInfinity(px)&&!Double.IsNaN(py)&&!Double.IsInfinity(py)&&px>=0&&px<=1&&py>=0&&py<=1){x=px;y=py;}}}
 public void Arrange(Rectangle defaults,Rectangle screen,Point localAnchor,Rectangle visible){
  if(disposed||screen.Width<1||screen.Height<1||defaults.Width<1||defaults.Height<1)return;
  normal=defaults;anchor=localAnchor;footprint=visible;
  if(area!=screen||monitor.Length==0){if(Dragging)Finish(true);area=screen;var display=Screen.FromRectangle(screen);var known=MonitorCatalog.Read().Find(m=>m.Screen.DeviceName==display.DeviceName);monitor=known==null?display.DeviceName:known.Id;try{Load();}catch(Exception ex){error(ex.Message);}}
  Apply(Dragging?read().Location:new Point(x.HasValue?area.Left+(int)Math.Round(x.Value*area.Width)-anchor.X:normal.X,y.HasValue?area.Top+(int)Math.Round(y.Value*area.Height)-anchor.Y:normal.Y));
 }
 internal static Point Clamp(Point location,Rectangle visible,Rectangle screen){
  int left=screen.Left-visible.Left,top=screen.Top-visible.Top;
  return new Point(Math.Max(left,Math.Min(location.X,screen.Right-visible.Right)),Math.Max(top,Math.Min(location.Y,screen.Bottom-visible.Bottom)));
 }
 void Apply(Point at){if(placing||normal.IsEmpty)return;var next=new Rectangle(Clamp(at,footprint,area),normal.Size);if(next==read())return;placing=true;try{place(next);}finally{placing=false;}}
 public bool Receive(string message){
  if(message==null||!message.StartsWith("position:",StringComparison.Ordinal))return false;
  try{switch(message.Substring(9)){case "drag":if(GetAsyncKeyState(1)<0)Begin(Cursor.Position);break;case "cancel":Finish(true);break;case "reset":Reset();break;case "left":Nudge(-8,0);break;case "right":Nudge(8,0);break;case "up":Nudge(0,-8);break;case "down":Nudge(0,8);break;}}catch(Exception ex){error(ex.Message);}return true;
 }
 internal void Begin(Point pointer){if(LayoutLocked||disposed||normal.IsEmpty||Dragging)return;start=pointer;origin=read().Location;Dragging=true;timer.Start();}
 internal void Step(Point pointer,bool down,bool escape){if(!Dragging)return;try{if(escape||LayoutLocked){Finish(true);return;}if(!down){Finish(false);return;}Apply(new Point(origin.X+pointer.X-start.X,origin.Y+pointer.Y-start.Y));}catch(Exception ex){Finish(true);error(ex.Message);}}
 void Save(){var at=read().Location;double px=Math.Max(0,Math.Min(1,(at.X+anchor.X-area.Left)/(double)area.Width)),py=Math.Max(0,Math.Min(1,(at.Y+anchor.Y-area.Top)/(double)area.Height));DesktopSettingsFiles.Locked(()=>{var map=Map();map[Key]=new{x=px,y=py};DesktopSettingsFiles.Write(file,json.Serialize(map));});x=px;y=py;}
 void Finish(bool cancel){if(!Dragging)return;timer.Stop();Dragging=false;if(cancel)Apply(origin);else if(read().Location!=origin)Save();}
 void Nudge(int dx,int dy){if(LayoutLocked)return;var at=read().Location;Apply(new Point(at.X+dx,at.Y+dy));Save();}
 internal void Reset(){if(LayoutLocked)return;Finish(true);DesktopSettingsFiles.Locked(()=>{var map=Map();map.Remove(Key);DesktopSettingsFiles.Write(file,json.Serialize(map));});x=y=null;Apply(normal.Location);}
 public void Dispose(){if(disposed)return;timer.Stop();Dragging=false;timer.Dispose();disposed=true;}
}
