$ErrorActionPreference='Stop'
$p=Join-Path $PSScriptRoot '../src/LiquidDesktop/DockPreview.cs'
$s=Get-Content -LiteralPath $p -Raw
function Block([string]$from,[string]$to){
 $i=$s.IndexOf($from);if($i -lt 0){throw "Missing $from"}
 $s.Substring($i,$s.IndexOf($to,$i)-$i)
}
$types=Block ' [StructLayout' ' [DllImport'
$states=Block ' internal enum ThumbnailState' ' internal ThumbnailState PreviewState'
$release=Block ' static void Release(Thumbnail' ' void Clear()'
$update=Block ' void UpdateSource(Thumbnail' ' void Build()'
$code=@"
using System;using System.Drawing;using System.Runtime.InteropServices;
public class PreviewStateFixture{
 class DockWindow{}static class DockBackend{public static bool Valid(DockWindow w){return true;}}
 static IntPtr Handle=new IntPtr(1);static bool visible=true,protectedContent=false,iconic=false,failRegister=true;static int requests,released;
 static bool IsWindowVisible(IntPtr h){return visible;}static bool IsIconic(IntPtr h){return iconic;}
 static bool GetWindowDisplayAffinity(IntPtr h,out uint affinity){affinity=protectedContent?17u:0u;return true;}
 static int DwmRegisterThumbnail(IntPtr dest,IntPtr source,out IntPtr handle){requests++;handle=failRegister?IntPtr.Zero:new IntPtr(9);return failRegister?-1:0;}
 static void DwmUnregisterThumbnail(IntPtr h){released++;}static int DwmQueryThumbnailSourceSize(IntPtr h,out SIZE size){size=new SIZE{x=400,y=300};return 0;}
 static int DwmUpdateThumbnailProperties(IntPtr h,ref PROPS props){return 0;}static Rectangle SourceCrop(IntPtr h,Size size){return new Rectangle(Point.Empty,size);}
$types
$states
$release
$update
 static void Assert(bool value){if(!value)throw new Exception("Preview state regression");}
 public void Run(){
  var t=new Thumbnail{window=new DockWindow(),source=new IntPtr(2),tile=new Rectangle(0,0,180,130)};
  UpdateSource(t);Assert(t.state==ThumbnailState.Pending&&!t.ready);
  for(int i=0;i<2;i++){t.retry=DateTime.MinValue;UpdateSource(t);}Assert(t.state==ThumbnailState.Unavailable&&requests==3);UpdateSource(t);Assert(requests==3);
  protectedContent=true;UpdateSource(t);Assert(t.state==ThumbnailState.Protected&&!t.ready);
  protectedContent=false;failRegister=false;iconic=true;UpdateSource(t);Assert(t.ready&&t.state==ThumbnailState.Minimized&&requests==4);
  iconic=false;UpdateSource(t);Assert(t.state==ThumbnailState.Submitted&&requests==4);
  protectedContent=true;UpdateSource(t);Assert(t.state==ThumbnailState.Protected&&released==1&&!t.ready);
  visible=false;UpdateSource(t);Assert(t.state==ThumbnailState.Unavailable&&!t.ready);
 }
}
"@
Add-Type -TypeDefinition $code
[PreviewStateFixture]::new().Run()
'PASS pending, bounded failure, protected release and recovery, minimized retained relationship, live submission and invisible source'
