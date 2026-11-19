using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

// Hardware D3D11 textures only: no desktop capture, windows or user pixels.
internal static class TestGpuDownsample {
 [StructLayout(LayoutKind.Sequential)]struct TextureDescription {internal uint width,height,mips,arraySize,format,samples,quality,usage,bind,cpu,misc;}
 [StructLayout(LayoutKind.Sequential)]struct InitialData {internal IntPtr pixels;internal uint pitch,slice;}
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateTexture(IntPtr self,ref TextureDescription desc,ref InitialData data,out IntPtr texture);
 [DllImport("d3d11.dll")]static extern int D3D11CreateDevice(IntPtr adapter,int driver,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out int level,out IntPtr context);
 [DllImport("d3d11.dll")]static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgi,out IntPtr surface);
 static IntPtr device,context;
 static int checks;
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);checks++;}
 static IDirect3DSurface Pattern(int width,int height){
  var bytes=new byte[width*height*4];for(int y=0;y<height;y++)for(int x=0;x<width;x++){int at=(y*width+x)*4;bytes[at+3]=255;if(x<width/2){bytes[at]=((x+y)%2==0)?(byte)200:(byte)0;bytes[at+2]=((x+y)%2==0)?(byte)0:(byte)200;}else bytes[at+1]=180;}
  var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);IntPtr texture=IntPtr.Zero,dxgi=IntPtr.Zero,projected=IntPtr.Zero;
  try{
   var desc=new TextureDescription{width=(uint)width,height=(uint)height,mips=1,arraySize=1,format=87,samples=1};var data=new InitialData{pixels=pinned.AddrOfPinnedObject(),pitch=(uint)width*4};
   var create=(CreateTexture)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(device),5*IntPtr.Size),typeof(CreateTexture));Marshal.ThrowExceptionForHR(create(device,ref desc,ref data,out texture));
   var id=new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");Marshal.ThrowExceptionForHR(Marshal.QueryInterface(texture,ref id,out dxgi));Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgi,out projected));return (IDirect3DSurface)Marshal.GetObjectForIUnknown(projected);
  }finally{pinned.Free();if(projected!=IntPtr.Zero)Marshal.Release(projected);if(dxgi!=IntPtr.Zero)Marshal.Release(dxgi);if(texture!=IntPtr.Zero)Marshal.Release(texture);}
 }
 static async Task<SoftwareBitmap> Read(IDirect3DSurface surface){var op=SoftwareBitmap.CreateCopyFromSurfaceAsync(surface,BitmapAlphaMode.Premultiplied);var done=new TaskCompletionSource<SoftwareBitmap>();op.Completed=(sender,status)=>{try{done.TrySetResult(sender.GetResults());}catch(Exception error){done.TrySetException(error);}};try{return await done.Task;}finally{((Windows.Foundation.IAsyncInfo)op).Close();}}
 static Color Pixel(SoftwareBitmap pixels,int x,int y){using(var image=new Bitmap(pixels.PixelWidth,pixels.PixelHeight,PixelFormat.Format32bppPArgb)){var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.WriteOnly,image.PixelFormat);try{WindowCapture.Copy(pixels,bits.Scan0,bits.Stride);}finally{image.UnlockBits(bits);}return image.GetPixel(x,y);}}
 static async Task QualityFallback(WindowCapture.GpuDownsample scale,int width,int height){
  int allocations=scale.Allocations;
  using(var source=Pattern(width,height)){
   var output=scale.Scale(source,width,height,1600,1048576);
   Check(Object.ReferenceEquals(output,source)&&!scale.Active&&scale.Allocations==allocations,width+"x"+height+" rejects a half-detail mip and releases its cached output");
   using(var pixels=await Read(output))using(var image=new Bitmap(1365,768,PixelFormat.Format32bppPArgb)){
    Check(pixels.PixelWidth==width&&pixels.PixelHeight==height,"quality fallback retains the complete source for CPU resizing");
    var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.WriteOnly,image.PixelFormat);
    try{WindowCapture.CopyScaled(pixels,bits.Scan0,image.Width,image.Height);}finally{image.UnlockBits(bits);}
    var green=image.GetPixel(1100,400);Check(green.G==180&&green.R==0&&green.B==0&&green.A==255,"quality fallback preserves the old 1365x768 filtered output and color registration");
   }
  }
 }
 static void CompareBridge(SoftwareBitmap frame){
  int bytes=checked(frame.PixelWidth*frame.PixelHeight*4);var stage=new byte[bytes];
  var pinned=GCHandle.Alloc(stage,GCHandleType.Pinned);IntPtr target=Marshal.AllocHGlobal(bytes);
  var prior=new List<double>();var current=new List<double>();var timer=new Stopwatch();
  var buffer=new SharedWindowCapture.Buffer{width=frame.PixelWidth,height=frame.PixelHeight,readback=frame,references=1};
  try{using(var lease=new SharedWindowCapture.FrameLease(buffer)){
   for(int i=0;i<36;i++)for(int turn=0;turn<2;turn++){
    bool direct=((i+turn)&1)==0;timer.Restart();
    if(direct)lease.CopyTo(target);
    else{WindowCapture.Copy(frame,pinned.AddrOfPinnedObject(),frame.PixelWidth*4);Marshal.Copy(stage,0,target,bytes);}
    timer.Stop();if(i>=4)(direct?current:prior).Add(timer.Elapsed.TotalMilliseconds);
   }
   prior.Sort();current.Sort();Console.WriteLine("INFO "+frame.PixelWidth+"x"+frame.PixelHeight+" CPU bridge n=32 alternating, prior p50/p95 ms="+prior[15].ToString("F3")+"/"+prior[30].ToString("F3")+" direct="+current[15].ToString("F3")+"/"+current[30].ToString("F3")+" bytes no longer staged per frame="+bytes);
  }}finally{Marshal.FreeHGlobal(target);pinned.Free();}
 }
 static async Task Run(){
  int feature;Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out device,out feature,out context));Console.WriteLine("INFO hardware feature level 0x"+feature.ToString("X"));
  using(var scale=new WindowCapture.GpuDownsample())using(var source=Pattern(2560,1600)){
   var watch=Stopwatch.StartNew();var output=scale.Scale(source,2560,1600,1600,1048576);
   using(var pixels=await Read(output)){
    Check(pixels.PixelWidth==1280&&pixels.PixelHeight==800,"GPU downsample precedes readback and stays within one million pixels");
    var a=Pixel(pixels,120,120);var b=Pixel(pixels,1000,120);Check(Math.Abs(a.R-100)<=2&&Math.Abs(a.B-100)<=2&&a.G<3&&a.A==255,"one-pixel BGRA checkerboard is filtered, not nearest-neighbor aliased");Check(b.G==180&&b.R==0&&b.B==0&&b.A==255,"color channels, alpha and source registration survive GPU scaling");
   }
   Console.WriteLine("INFO first allocation and readback ms="+watch.Elapsed.TotalMilliseconds.ToString("F3"));using(var warm=await Read(output))CompareBridge(warm);var costs=new List<double>();
   for(int n=0;n<12;n++){watch.Restart();Check(Object.ReferenceEquals(output,scale.Scale(source,2560,1600,1600,1048576)),"stable dimensions reuse the projected surface");using(var pixels=await Read(output)){}costs.Add(watch.Elapsed.TotalMilliseconds);}
   int beforeIdle=0,whileIdle=0;var demand=new SharedWindowCapture.Entry();var consumed=SharedWindowCapture.ReserveBuffer(demand);consumed.writing=false;
   for(int n=0;n<8;n++){
    using(var baseline=await Read(output)){beforeIdle++;}
    if(SharedWindowCapture.ReserveBuffer(demand)!=null)using(var unexpected=await Read(output)){whileIdle++;}
   }
   demand.requested=true;var resumed=SharedWindowCapture.ReserveBuffer(demand);
   using(var fresh=await Read(output))Check(resumed!=null&&fresh.PixelWidth==1280&&beforeIdle==8&&whileIdle==0,"available offscreen producer, idle consumers: 8 legacy GPU readbacks versus 0 demand-gated; fresh demand reads one");
   Check(scale.Allocations==1,"steady frames do not allocate new GPU textures or views");costs.Sort();Console.WriteLine("INFO warm scale plus readback p50Ms="+costs[5].ToString("F3")+" p95Ms="+costs[11].ToString("F3"));
   using(var resized=Pattern(2560,1536))using(var pixels=await Read(scale.Scale(resized,2560,1536,1600,1048576)))Check(pixels.PixelWidth==1280&&pixels.PixelHeight==768&&scale.Allocations==2,"eligible resize rebuilds only bounded textures and releases the old surface");
   using(var small=Pattern(640,360)){Check(Object.ReferenceEquals(small,scale.Scale(small,640,360,1600,1048576))&&!scale.Active,"small sources bypass GPU resize and release cached large textures");}
   using(var pixels=await Read(scale.Scale(source,2560,1600,1600,1048576)))Check(pixels.PixelWidth==1280&&scale.Allocations==3,"growing after the bypass recreates resources safely");
   await QualityFallback(scale,1920,1080);
   using(var pixels=await Read(scale.Scale(source,2560,1600,1600,1048576)))Check(pixels.PixelWidth==1280&&scale.Allocations==4,"GPU scaling resumes after quality fallback without retaining the previous textures");
   await QualityFallback(scale,1921,1081);
   using(var pixels=await Read(scale.Scale(source,2560,1600,1600,1048576)))Check(pixels.PixelWidth==1280&&scale.Allocations==5,"GPU scaling resumes after odd-size quality fallback");
  }
  using(var source=Pattern(2560,1600))using(var scale=new WindowCapture.GpuDownsample())using(var pixels=await Read(scale.Scale(source,2560,1600,1600,1048576)))Check(pixels.PixelHeight==800,"a fresh scaler works after complete disposal");
 }
 static void DirectLease(){
  var entry=new SharedWindowCapture.Entry();Check(SharedWindowCapture.ReserveBuffer(entry)!=null&&SharedWindowCapture.ReserveBuffer(entry)==null,"idle consumers cause no further capture readback reservations");entry.requested=true;Check(SharedWindowCapture.ReserveBuffer(entry)!=null,"fresh consumer demand resumes capture without rebuilding the session");
  byte[] expected={10,20,30,255,40,50,60,255};
  var bitmap=new SoftwareBitmap(BitmapPixelFormat.Bgra8,2,1,BitmapAlphaMode.Premultiplied);using(var writer=new DataWriter()){writer.WriteBytes(expected);bitmap.CopyFromBuffer(writer.DetachBuffer());}
  var buffer=new SharedWindowCapture.Buffer{readback=bitmap,width=2,height=1,references=2,retired=true};
  var address=Marshal.AllocHGlobal(8);
  try{using(var first=new SharedWindowCapture.FrameLease(buffer))using(var second=new SharedWindowCapture.FrameLease(buffer)){
   first.CopyTo(address);byte[] result=new byte[8];Marshal.Copy(address,result,0,8);
   Check(BitConverter.ToString(result)==BitConverter.ToString(expected)&&buffer.pixels==null,"bridge receives exact BGRA bytes without an intermediate managed frame");
   first.Dispose();Check(buffer.readback!=null&&BitConverter.ToString(second.Pixels)==BitConverter.ToString(expected),"snapshot consumer shares the held readback after another host releases it");
  }Check(buffer.readback==null,"last lease releases a retired readback");}finally{Marshal.FreeHGlobal(address);}
 }
 [MTAThread]static int Main(string[] args){try{DirectLease();if(args.Length>0&&args[0]=="--lease")return 0;Run().GetAwaiter().GetResult();Console.WriteLine("Completed "+checks+" offscreen GPU checks.");return 0;}catch(Exception error){Console.WriteLine(error);return 1;}finally{if(context!=IntPtr.Zero)Marshal.Release(context);if(device!=IntPtr.Zero)Marshal.Release(device);}}
}
