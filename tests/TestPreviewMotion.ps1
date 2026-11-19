$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$folder=Join-Path ([IO.Path]::GetTempPath()) ('PrismPreviewMotion-'+[guid]::NewGuid().ToString('N'))
New-Item -Type Directory -Path $folder | Out-Null
try{
 $fixture=Join-Path $folder 'Fixture.cs';$exe=Join-Path $folder 'Fixture.exe'
 @"
using System;using System.Collections.Generic;using System.Drawing;using System.Reflection;using System.Windows.Forms;
class DockWindow{public long handle;public uint pid;public string path,title;}
class DockEntry{public string id,name,path;public List<DockWindow> windows=new List<DockWindow>();}
static class DockBackend{public static bool Valid(DockWindow w){return false;}public static void Activate(DockWindow w){throw new Exception();}public static void HoldActivation(){throw new Exception();}}
static class Probe{
 static double Value(DockPreview preview,string field){return (double)typeof(DockPreview).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(preview);}
 [STAThread]static void Main(){
  using(var panel=new DockPreview(new DockEntry{id="fixture",name="fixture"},Rectangle.Empty,false)){
   var handle=panel.Handle;if(panel.Visible||handle==IntPtr.Zero)throw new Exception("fixture window visibility");
   double peak=0;int opening=0;while(opening++<500){panel.AdvanceMotion(.001,false);peak=Math.Max(peak,Value(panel,"progress"));if(Value(panel,"progress")==1)break;}
   if(peak!=1||opening>360)throw new Exception("open motion");
   panel.Dismiss();int closing=0;double at130=0;while(closing++<500){panel.AdvanceMotion(.001,false);if(closing==130)at130=Value(panel,"progress");if(Value(panel,"progress")==0)break;}
   panel.Revive();for(int i=0;i<45;i++)panel.AdvanceMotion(.001,false);double p=Value(panel,"progress"),v=Value(panel,"velocity");panel.Dismiss();
   if(p!=Value(panel,"progress")||v!=Value(panel,"velocity"))throw new Exception("direction snap");panel.AdvanceMotion(.0001,false);
   if(Value(panel,"progress")<=p||Value(panel,"velocity")<=0)throw new Exception("lost velocity");
   panel.AdvanceMotion(.001,true);if(Value(panel,"progress")!=0||Value(panel,"velocity")!=0)throw new Exception("reduced motion");
   Console.WriteLine("PASS hidden production preview; open settled {0}ms, peak {1:F4}; close settled {2}ms, remaining at 130ms {3:P2}; reversal p={4:F4}, velocity={5:F4}/s",opening,peak,closing,at130,p,v);
  }
 }
}
"@ | Set-Content -LiteralPath $fixture
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /nowarn:0649 "/out:$exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll $fixture (Join-Path $repo 'src/LiquidDesktop/DockPreview.cs') (Join-Path $repo 'src/LiquidDesktop/DesktopVisibilityPolicy.cs')
 if($LASTEXITCODE){throw 'Preview fixture compilation failed'}
 & $exe
 if($LASTEXITCODE){throw 'Preview motion fixture failed'}
}finally{
 foreach($file in @($fixture,$exe)){if($file -and (Test-Path -LiteralPath $file)){Remove-Item -LiteralPath $file}}
 [IO.Directory]::Delete($folder)
}
