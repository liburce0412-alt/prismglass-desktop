using System;using System.Collections.Generic;using System.Drawing;using System.Globalization;using System.Runtime.InteropServices;using System.Web.Script.Serialization;using System.Windows.Forms;using Microsoft.Web.WebView2.WinForms;

// The web page supplies text and a control rectangle, never a window handle or drawing command.
internal static class NativeHintHost {
 static readonly JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=32768,RecursionLimit=8};
 static readonly HashSet<Form> registered=new HashSet<Form>();
 static Form owner;static WebView2 view;static Rectangle anchor,ownerBounds,viewBounds;static string caption="";static bool keyboard;
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
 static WebView2 FindView(Control parent){foreach(Control child in parent.Controls){var web=child as WebView2;if(web!=null)return web;var nested=FindView(child);if(nested!=null)return nested;}return null;}
 static bool Trusted(WebView2 web){if(web==null||web.IsDisposed||web.CoreWebView2==null)return false;var uri=web.Source;if(uri==null||uri.Scheme!="https")return false;return uri.AbsoluteUri=="https://prism-side.local/sidedock.html"||uri.AbsoluteUri=="https://prism-shelf.local/shelf.html"||uri.AbsoluteUri=="https://prism-island.local/island.html"||uri.AbsoluteUri=="https://prism-workspace.local/workspace.html";}
 static void Clear(Form source){if(owner!=source)return;owner=null;view=null;caption="";anchor=Rectangle.Empty;}
 static double Number(Dictionary<string,object> value,string key){object raw;if(!value.TryGetValue(key,out raw)||raw==null||raw is string||raw is bool)return double.NaN;try{return Convert.ToDouble(raw,CultureInfo.InvariantCulture);}catch{return double.NaN;}}
 internal static bool TryBounds(Dictionary<string,object> value,Rectangle viewport,out Rectangle result){
  result=Rectangle.Empty;double x=Number(value,"x"),y=Number(value,"y"),w=Number(value,"w"),h=Number(value,"h"),vw=Number(value,"viewportWidth"),vh=Number(value,"viewportHeight");
  foreach(var n in new[]{x,y,w,h,vw,vh})if(double.IsNaN(n)||double.IsInfinity(n))return false;
  if(viewport.Width<=0||viewport.Height<=0||vw<1||vh<1||vw>32768||vh>32768||w<=0||h<=0||w>vw*2||h>vh*2||x>=vw||y>=vh||x+w<=0||y+h<=0)return false;
  double left=Math.Max(0,x),top=Math.Max(0,y),right=Math.Min(vw,x+w),bottom=Math.Min(vh,y+h);
  result=Rectangle.FromLTRB(viewport.Left+(int)Math.Floor(left*viewport.Width/vw),viewport.Top+(int)Math.Floor(top*viewport.Height/vh),viewport.Left+(int)Math.Ceiling(right*viewport.Width/vw),viewport.Top+(int)Math.Ceiling(bottom*viewport.Height/vh));
  return result.Width>0&&result.Height>0&&viewport.Contains(result);
 }
 public static void Receive(Form source,string message){
  if(source==null||source.IsDisposed||String.IsNullOrEmpty(message)||!message.StartsWith("hint:",StringComparison.Ordinal)||message.Length>32768)return;
  if(message=="hint:hide"){Clear(source);return;}
  var web=FindView(source);if(!Trusted(web))return;
  if(registered.Add(source)){source.Disposed+=(s,e)=>{Clear(source);registered.Remove(source);};source.VisibleChanged+=(s,e)=>{if(!source.Visible)Clear(source);};source.Deactivate+=(s,e)=>Clear(source);}
  if(message=="hint:ready"){web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"hint-host\",\"native\":true}");return;}
  try{
   if(!source.Visible||source.WindowState==FormWindowState.Minimized||!web.IsHandleCreated){Clear(source);return;}
   var value=json.Deserialize<Dictionary<string,object>>(message.Substring(5));object text,mode;Rectangle bounds;
   var viewport=new Rectangle(web.PointToScreen(Point.Empty),web.ClientSize);
   if(value==null||!value.TryGetValue("text",out text)||!(text is string)||String.IsNullOrWhiteSpace((string)text)||((string)text).Length>8192||!TryBounds(value,viewport,out bounds)){Clear(source);return;}
   owner=source;view=web;ownerBounds=source.Bounds;viewBounds=viewport;anchor=bounds;caption=(string)text;keyboard=value.TryGetValue("keyboard",out mode)&&mode is bool&&(bool)mode;
  }catch{Clear(source);}
 }
 public static bool Hover(Point point,out Rectangle bounds,out string text){
  bounds=Rectangle.Empty;text="";
  if(owner==null)return false;
  if(owner.IsDisposed||!owner.Visible||owner.WindowState==FormWindowState.Minimized||view==null||view.IsDisposed||owner.Bounds!=ownerBounds||new Rectangle(view.PointToScreen(Point.Empty),view.ClientSize)!=viewBounds){Clear(owner);return false;}
  if(keyboard){if(!owner.ContainsFocus){Clear(owner);return false;}}
  else if(!anchor.Contains(point)||GetAncestor(WindowFromPoint(point),2)!=owner.Handle){Clear(owner);return false;}
  bounds=anchor;text=caption;return true;
 }
}
