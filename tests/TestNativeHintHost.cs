using System;using System.Collections.Generic;using System.Drawing;
// These two stand-ins isolate text measurement; native plate composition is covered by TestDockLabel.
internal sealed class DockPlate:IDisposable{public void Hide(){}public void Sync(IntPtr a,IntPtr b,Rectangle c,Rectangle d,bool e,byte f){}public void Outline(float[][] points){}public void Dispose(){}}
internal static class MonitorDock{public static double PixelScale(Rectangle area){return 1;}}
internal static class TestNativeHintHost{
 static int checks;static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static Dictionary<string,object> Message(){return new Dictionary<string,object>{{"x",100.0},{"y",50.0},{"w",200.0},{"h",50.0},{"viewportWidth",600.0},{"viewportHeight",400.0}};}
 [STAThread]static void Main(){Rectangle result;var viewport=new Rectangle(100,200,1200,800);var value=Message();
  Check(NativeHintHost.TryBounds(value,viewport,out result)&&result==new Rectangle(300,300,400,100),"CSS target maps through the actual WebView viewport scale");
  value["x"]=-20.0;value["w"]=100.0;Check(NativeHintHost.TryBounds(value,viewport,out result)&&result==new Rectangle(100,300,160,100),"partly visible targets are clipped to the owner client area");
  foreach(var invalid in new object[]{double.NaN,double.PositiveInfinity,"10",-2000.0,700.0}){value=Message();value["x"]=invalid;Check(!NativeHintHost.TryBounds(value,viewport,out result),"invalid or off-owner target is rejected: "+invalid);}
  value=Message();value["viewportWidth"]=0.0;Check(!NativeHintHost.TryBounds(value,viewport,out result),"zero browser viewport cannot produce an anchor");
  value=Message();value["h"]=-1.0;Check(!NativeHintHost.TryBounds(value,viewport,out result),"negative target dimensions are rejected");
  value=Message();value["w"]=1300.0;Check(!NativeHintHost.TryBounds(value,viewport,out result),"oversized claimed target cannot cover unrelated windows");
  var shortSize=DockLabel.Measure("文件资源管理器",1,new Size(1280,720));
  Check(shortSize.Height>=36&&shortSize.Height<=42&&shortSize.Width<300,"short labels retain the compact original capsule size");
  string longText="C:/工作资料/项目与组件/"+new string('路',90)+"/审计记录.html\nhttps://example.test/path/"+new string('a',140);
  var full=DockLabel.Measure(longText,1,new Size(1280,720));Check(full.Width<=440&&full.Height>shortSize.Height*2&&full.Height<=704,"full paths, URLs and explicit newlines expand the label instead of one-line clipping");
  var small=DockLabel.Measure(longText,1,new Size(240,320));Check(small.Width<=224&&small.Height<=304,"long labels remain inside a narrow work area");
  var scaled=DockLabel.Measure("文件资源管理器",2,new Size(1920,1080));Check(scaled.Height>=72&&scaled.Width>shortSize.Width*1.8,"label text measurement follows monitor scale");
  Console.WriteLine("Completed "+checks+" native hint geometry and text checks; no WebView or system interaction.");
 }
}
