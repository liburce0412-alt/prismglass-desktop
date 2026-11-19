using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

internal class OpticalBackdrop {}
internal static class LiveGlassBackdrop {internal static OpticalBackdrop Attach(Func<Form> form,WebView2 view){throw new NotSupportedException();}}
internal static class CapsuleNativeTest {
 public class Raster {public int w,h;public double[][] outline;public int[][] rows;}
 [DllImport("gdi32.dll")]static extern bool PtInRegion(IntPtr region,int x,int y);
 static int Main(string[] args){
  int tested=0,excluded=0,outer=0;
  foreach(var raster in new JavaScriptSerializer().Deserialize<Raster[]>(File.ReadAllText(args[0]))){
   var points=raster.outline.Select(p=>new PointF((float)(Math.Round(p[0]*4)/4),(float)(Math.Round(p[1]*4)/4))).ToArray();
   using(var bitmap=new Bitmap(raster.w,raster.h))using(var graphics=Graphics.FromImage(bitmap))using(var path=new GraphicsPath(FillMode.Winding)){
    path.AddPolygon(points);using(var clip=GlassWindow.Clip(path,new Rectangle(0,0,raster.w,raster.h))){
     IntPtr region=clip.GetHrgn(graphics);
     try{for(int y=0;y<raster.h;y++)for(int x=0;x<raster.w;x++){
      bool hit=PtInRegion(region,x,y);var row=raster.rows[y];
      if(x>=row[0]&&x<=row[1]){tested++;if(!hit)excluded++;}
      if(hit){
       bool nearby=false;for(int j=Math.Max(0,y-2);j<=Math.Min(raster.h-1,y+2);j++){var neighbor=raster.rows[j];if(x>=neighbor[0]-2&&x<=neighbor[1]+2){nearby=true;break;}}
       if(!nearby)outer++;
      }
     }}finally{clip.ReleaseHrgn(region);}
    }
   }
  }
  Console.WriteLine("Native HRGN: "+tested+" visible pixel centers, "+excluded+" missed, "+outer+" accepted beyond two-pixel antialias neighborhood");
  return excluded==0&&outer==0?0:1;
 }
}
