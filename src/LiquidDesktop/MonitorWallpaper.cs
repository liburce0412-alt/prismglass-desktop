using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

internal static class MonitorWallpaper {
 static string startupRoot;static readonly HashSet<string> cleanSamples=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 internal static void BeginStartup(string root){lock(cleanSamples){startupRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);cleanSamples.Clear();}}
 [StructLayout(LayoutKind.Sequential)]struct RECT {public int left,top,right,bottom;}
 [ComImport,Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IDesktopWallpaper {
  void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string monitor,[MarshalAs(UnmanagedType.LPWStr)]string path);
  [return:MarshalAs(UnmanagedType.LPWStr)]string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string monitor);
  [return:MarshalAs(UnmanagedType.LPWStr)]string GetMonitorDevicePathAt(uint index);
  uint GetMonitorDevicePathCount();RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)]string monitor);
  void SetBackgroundColor(uint color);uint GetBackgroundColor();void SetPosition(int position);int GetPosition();
 }
 internal sealed class State {public string Source,Monitor;public uint Background;public int Position;public string Key(Rectangle desktop){return Monitor+"|"+Source+"|"+(String.IsNullOrEmpty(Source)?0:File.GetLastWriteTimeUtc(Source).Ticks)+"|"+Background+"|"+Position+"|"+desktop;}}
 internal sealed class Sample {public string File,Version;}
 static Dictionary<Rectangle,string> names=new Dictionary<Rectangle,string>();
 internal const string Neutral="wallpaper-monitor-neutral.png";
 internal static void Bind(Dictionary<Rectangle,string> owners){
  var next=new Dictionary<Rectangle,string>();
  using(var hash=System.Security.Cryptography.SHA256.Create())foreach(var pair in owners){
   string id=BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(pair.Value.ToUpperInvariant()))).Replace("-","").Substring(0,24);
   next[pair.Key]=GeometryName(pair.Key).Replace(".png","-"+id+".png");
  }
  lock(cleanSamples)names=next;
 }
 internal static string Name(Rectangle bounds){lock(cleanSamples){string name;return names.TryGetValue(bounds,out name)?name:GeometryName(bounds);}}
 static string GeometryName(Rectangle bounds){return String.Format(CultureInfo.InvariantCulture,"wallpaper-monitor-{0}-{1}-{2}-{3}.png",bounds.X,bounds.Y,bounds.Width,bounds.Height);}
 internal static Sample For(string root,Rectangle bounds){
  string name,path;
  lock(cleanSamples){
   name=Name(bounds);path=Path.GetFullPath(Path.Combine(root,name));
   bool startup=String.Equals(Path.GetDirectoryName(path),startupRoot,StringComparison.OrdinalIgnoreCase);
   if((startup&&!cleanSamples.Contains(path))||!File.Exists(path)){
    name=startup&&names.Count!=0?Neutral:"wallpaper.png";
    path=Path.Combine(root,name);
   }
  }
  return new Sample{File=name,Version=name+":"+File.GetLastWriteTimeUtc(path).Ticks};
 }
 internal static State Read(Rectangle bounds){
  var wallpaper=(IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")));
  try{uint count=wallpaper.GetMonitorDevicePathCount();for(uint i=0;i<count;i++){string monitor=wallpaper.GetMonitorDevicePathAt(i);var area=wallpaper.GetMonitorRECT(monitor);if(area.left==bounds.Left&&area.top==bounds.Top&&area.right==bounds.Right&&area.bottom==bounds.Bottom)return new State{Monitor=monitor,Source=Environment.ExpandEnvironmentVariables(wallpaper.GetWallpaper(monitor)??""),Background=wallpaper.GetBackgroundColor(),Position=wallpaper.GetPosition()};}throw new IOException("无法查询此显示器的壁纸，请稍后重试。");}finally{Marshal.ReleaseComObject(wallpaper);}
 }
 internal static RectangleF Placement(Size image,Rectangle bounds,Rectangle desktop,int position){
  var area=position==5?desktop:bounds;float width=image.Width,height=image.Height;
  if(position==2){width=area.Width;height=area.Height;}
  else if(position!=0){float scale=position==3?Math.Min((float)area.Width/image.Width,(float)area.Height/image.Height):Math.Max((float)area.Width/image.Width,(float)area.Height/image.Height);width*=scale;height*=scale;}
  return new RectangleF(area.Left-bounds.Left+(area.Width-width)/2,area.Top-bounds.Top+(area.Height-height)/2,width,height);
 }
 internal static Bitmap Static(State state,Rectangle bounds,Rectangle desktop){
  if(bounds.Width<1||bounds.Height<1||(long)bounds.Width*bounds.Height>16777216)throw new IOException("壁纸取样的显示器尺寸无效。");
  var output=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppRgb);
  try{using(var graphics=Graphics.FromImage(output)){
   uint color=state.Background;graphics.Clear(Color.FromArgb((int)(color&255),(int)((color>>8)&255),(int)((color>>16)&255)));graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
   if(!String.IsNullOrEmpty(state.Source))using(var image=Image.FromFile(state.Source)){
    // Windows tiles from the primary monitor's origin, including monitors left or above it.
    if(state.Position==1)using(var brush=new TextureBrush(image)){brush.TranslateTransform(-bounds.Left,-bounds.Top);graphics.FillRectangle(brush,0,0,bounds.Width,bounds.Height);}
    else using(var attributes=new ImageAttributes()){attributes.SetWrapMode(WrapMode.TileFlipXY);var target=Placement(image.Size,bounds,desktop,state.Position);graphics.DrawImage(image,new[]{new PointF(target.Left,target.Top),new PointF(target.Right,target.Top),new PointF(target.Left,target.Bottom)},new RectangleF(0,0,image.Width,image.Height),GraphicsUnit.Pixel,attributes);}
   }
  }return output;}catch{output.Dispose();throw;}
 }
 internal static string Save(string root,Rectangle bounds,Bitmap image,string name=null){
  if(image.Width!=bounds.Width||image.Height!=bounds.Height)throw new IOException("壁纸取样尺寸与显示器不一致。");
  string target=Path.GetFullPath(Path.Combine(root,name??Name(bounds))),temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{image.Save(temporary,ImageFormat.Png);if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);lock(cleanSamples)cleanSamples.Add(target);return target;}finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
}
