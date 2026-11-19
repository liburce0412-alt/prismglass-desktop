using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

// WinRT interop follows Microsoft's Composition Win32 capture sample (MIT).
internal sealed class WindowCapture : IDisposable {
 [ComImport,Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface ItemInterop {IntPtr CreateForWindow(IntPtr window,ref Guid iid);IntPtr CreateForMonitor(IntPtr monitor,ref Guid iid);}
 [ComImport,Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]internal interface MemoryAccess {void GetBuffer(out IntPtr data,out uint capacity);}
 [ComImport,Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]interface DxgiAccess {IntPtr GetInterface(ref Guid iid);}
 [DllImport("kernel32.dll",EntryPoint="RtlMoveMemory")]static extern void CopyMemory(IntPtr target,IntPtr source,UIntPtr bytes);
 [DllImport("d3d11.dll")]static extern int D3D11CreateDevice(IntPtr adapter,int driver,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out int level,out IntPtr context);
 [DllImport("d3d11.dll")]static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi,out IntPtr device);
 [DllImport("d3d11.dll")]static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgi,out IntPtr surface);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")]static extern bool GetWindowDisplayAffinity(IntPtr window,out uint affinity);
 [StructLayout(LayoutKind.Sequential)]struct RECT {public int left,top,right,bottom;}
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out RECT bounds,int bytes);
 static IDirect3DDevice device;
 static readonly object deviceLock=new object();
 internal static async Task<bool> Borderless(){
  var operation=GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);var completed=new TaskCompletionSource<bool>();
  operation.Completed=(sender,status)=>{try{completed.TrySetResult(sender.GetResults()==Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.Allowed);}catch{completed.TrySetResult(false);}};
  try{return await completed.Task;}finally{((Windows.Foundation.IAsyncInfo)operation).Close();}
 }
 readonly IntPtr window;readonly uint pid;readonly bool borderless;GraphicsCaptureItem item;Direct3D11CaptureFramePool pool;GraphicsCaptureSession session;SizeInt32 size;bool disposed,busy,scaleFailed;GpuDownsample downsample;
 internal bool LastGpuScaled {get;private set;}
 internal long LastFrameTime {get;private set;}
 internal string GpuScaleError {get;private set;}
 internal string Diagnostic="";
 internal Action Available;
 internal bool Valid {get{uint current,affinity;GetWindowThreadProcessId(window,out current);return !disposed&&current==pid&&IsWindowVisible(window)&&!IsIconic(window)&&(!GetWindowDisplayAffinity(window,out affinity)||affinity==0);}}
 static IDirect3DDevice Device(){
  lock(deviceLock){
  if(device!=null)return device;IntPtr native=IntPtr.Zero,context=IntPtr.Zero,dxgi=IntPtr.Zero,projected=IntPtr.Zero;int level;
  try{
   Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out native,out level,out context));
   var id=new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");Marshal.ThrowExceptionForHR(Marshal.QueryInterface(native,ref id,out dxgi));
   Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi,out projected));device=(IDirect3DDevice)Marshal.GetObjectForIUnknown(projected);return device;
  }finally{if(projected!=IntPtr.Zero)Marshal.Release(projected);if(dxgi!=IntPtr.Zero)Marshal.Release(dxgi);if(context!=IntPtr.Zero)Marshal.Release(context);if(native!=IntPtr.Zero)Marshal.Release(native);}
  }
 }
 internal WindowCapture(IntPtr handle,uint process,bool borderless){
  window=handle;pid=process;this.borderless=borderless;if(!Valid||!GraphicsCaptureSession.IsSupported())throw new InvalidOperationException("窗口不允许捕获。");
  try{
   var factory=(ItemInterop)WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));var iid=new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");var pointer=factory.CreateForWindow(handle,ref iid);
   try{item=(GraphicsCaptureItem)Marshal.GetObjectForIUnknown(pointer);}finally{Marshal.Release(pointer);}
   Start(item.Size);
  }catch{Dispose();throw;}
 }
 void FrameAvailable(Direct3D11CaptureFramePool sender,object e){var available=Available;if(!disposed&&available!=null)available();}
 void Start(SizeInt32 bounds){Budget(bounds);if(session!=null)session.Dispose();if(pool!=null){pool.FrameArrived-=FrameAvailable;pool.Dispose();}size=bounds;pool=Direct3D11CaptureFramePool.CreateFreeThreaded(Device(),DirectXPixelFormat.B8G8R8A8UIntNormalized,2,size);pool.FrameArrived+=FrameAvailable;session=pool.CreateCaptureSession(item);session.IsCursorCaptureEnabled=false;if(borderless)session.IsBorderRequired=false;session.StartCapture();}
 static void Budget(SizeInt32 value){if(value.Width<1||value.Height<1||(long)value.Width*value.Height>16777216)throw new InvalidOperationException("窗口画面超过捕获预算。");}
 internal async Task<SoftwareBitmap> Next(int maximumWidth=0,int maximumPixels=0){
  if(busy||!Valid)return null;busy=true;Direct3D11CaptureFrame frame=null;SizeInt32 resize=new SizeInt32();bool recreate=false;
  try{frame=pool.TryGetNextFrame();for(int i=0;i<3&&frame!=null;i++){var newer=pool.TryGetNextFrame();if(newer==null)break;frame.Dispose();frame=newer;}
   if(frame==null)return null;var next=frame.ContentSize;Budget(next);
   RECT bounds;if(DwmGetWindowAttribute(window,9,out bounds,16)==0&&(next.Width!=bounds.right-bounds.left||next.Height!=bounds.bottom-bounds.top)){Diagnostic="frame="+next.Width+"x"+next.Height+" window="+(bounds.right-bounds.left)+"x"+(bounds.bottom-bounds.top)+" pool="+size.Width+"x"+size.Height;return null;}
   if(next.Width!=size.Width||next.Height!=size.Height){resize=next;recreate=true;return null;}
   var surface=frame.Surface;LastGpuScaled=false;LastFrameTime=frame.SystemRelativeTime.Ticks;
   if(maximumWidth>0&&maximumPixels>0&&!scaleFailed)try{
    if(downsample==null)downsample=new GpuDownsample();surface=downsample.Scale(surface,next.Width,next.Height,maximumWidth,maximumPixels);LastGpuScaled=downsample.Active;
   }catch(ExternalException error){scaleFailed=true;GpuScaleError=error.Message;if(downsample!=null)downsample.Dispose();downsample=null;surface=frame.Surface;}
   var operation=SoftwareBitmap.CreateCopyFromSurfaceAsync(surface,BitmapAlphaMode.Premultiplied);var state=(Windows.Foundation.IAsyncInfo)operation;var finished=new TaskCompletionSource<SoftwareBitmap>();
   operation.Completed=(sender,status)=>{try{finished.TrySetResult(sender.GetResults());}catch(Exception ex){finished.TrySetException(ex);}};
   try{var pixels=await finished.Task;if(!Valid){pixels.Dispose();return null;}return pixels;}finally{state.Close();}
  }finally{if(frame!=null)frame.Dispose();try{if(recreate&&!disposed)Start(resize);}finally{busy=false;}}
 }
 internal static void Copy(SoftwareBitmap pixels,IntPtr target,int stride){
  int row=checked(pixels.PixelWidth*4);if(target==IntPtr.Zero||stride<row)throw new ArgumentException("Invalid pixel destination.");
  using(var buffer=pixels.LockBuffer(BitmapBufferAccessMode.Read))using(var reference=buffer.CreateReference()){
   IntPtr source;uint capacity;((MemoryAccess)reference).GetBuffer(out source,out capacity);var plane=buffer.GetPlaneDescription(0);
   if(plane.Stride<row||plane.StartIndex<0||(long)plane.StartIndex+(long)(pixels.PixelHeight-1)*plane.Stride+row>capacity)throw new InvalidOperationException("Invalid capture buffer.");
   if(stride==row&&plane.Stride==row)CopyMemory(target,IntPtr.Add(source,plane.StartIndex),new UIntPtr((uint)(row*pixels.PixelHeight)));
   else for(int y=0;y<pixels.PixelHeight;y++)CopyMemory(IntPtr.Add(target,y*stride),IntPtr.Add(source,plane.StartIndex+y*plane.Stride),new UIntPtr((uint)row));
  }
 }
 internal static void CopyScaled(SoftwareBitmap pixels,IntPtr target,int width,int height){
  if(width==pixels.PixelWidth&&height==pixels.PixelHeight){Copy(pixels,target,width*4);return;}
  if(target==IntPtr.Zero||width<1||height<1||(long)width*height>1048576)throw new ArgumentException("Invalid scaled capture destination.");
  using(var buffer=pixels.LockBuffer(BitmapBufferAccessMode.Read))using(var reference=buffer.CreateReference()){
   IntPtr source;uint capacity;((MemoryAccess)reference).GetBuffer(out source,out capacity);var plane=buffer.GetPlaneDescription(0);int row=checked(pixels.PixelWidth*4);
   if(plane.Stride<row||plane.StartIndex<0||(long)plane.StartIndex+(long)(pixels.PixelHeight-1)*plane.Stride+row>capacity)throw new InvalidOperationException("Invalid capture buffer.");
   using(var input=new Bitmap(pixels.PixelWidth,pixels.PixelHeight,plane.Stride,PixelFormat.Format32bppPArgb,IntPtr.Add(source,plane.StartIndex)))
   using(var output=new Bitmap(width,height,width*4,PixelFormat.Format32bppPArgb,target))using(var graphics=Graphics.FromImage(output)){
    graphics.CompositingMode=CompositingMode.SourceCopy;graphics.InterpolationMode=InterpolationMode.Bilinear;graphics.PixelOffsetMode=PixelOffsetMode.Half;
    graphics.DrawImage(input,new Rectangle(0,0,width,height),0,0,pixels.PixelWidth,pixels.PixelHeight,GraphicsUnit.Pixel);
   }
  }
 }
 // D3D11's documented mip generator performs the resize before CPU readback.
 // Only the selected mip is read; all textures/views are reused until size changes.
 // Native slots follow ID3D11Device/ID3D11DeviceContext in the Windows SDK.
 internal sealed class GpuDownsample : IDisposable {
  [StructLayout(LayoutKind.Sequential)]struct TextureDescription {internal uint width,height,mips,arraySize,format,samples,quality,usage,bind,cpu,misc;}
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void GetObject(IntPtr self,out IntPtr value);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateTexture(IntPtr self,ref TextureDescription desc,IntPtr data,out IntPtr texture);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateView(IntPtr self,IntPtr resource,IntPtr desc,out IntPtr view);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int FormatSupport(IntPtr self,uint format,out uint support);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int ProtectThreads(IntPtr self,int enabled);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void CopyRegion(IntPtr self,IntPtr destination,uint subresource,uint x,uint y,uint z,IntPtr source,uint sourceSubresource,IntPtr box);
  [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Generate(IntPtr self,IntPtr view);
  IntPtr native,context,mips,target,view;IDirect3DSurface result;int sourceWidth,sourceHeight,level;CopyRegion copy;Generate generate;
  internal int Allocations {get;private set;}
  internal bool Active {get{return result!=null;}}
  static T Method<T>(IntPtr instance,int slot) where T:class{return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size),typeof(T));}
  static void Release(ref IntPtr pointer){if(pointer!=IntPtr.Zero)Marshal.Release(pointer);pointer=IntPtr.Zero;}
  void Clear(){try{if(result!=null)result.Dispose();}finally{result=null;Release(ref view);Release(ref target);Release(ref mips);sourceWidth=sourceHeight=level=0;}}
  void Initialize(IntPtr source){
   Method<GetObject>(source,3)(source,out native);Method<GetObject>(native,40)(native,out context);
   uint supported;Marshal.ThrowExceptionForHR(Method<FormatSupport>(native,29)(native,87,out supported));
   if((supported&0x2000)==0)throw new COMException("BGRA mip generation is unavailable.");
   IntPtr multithread=IntPtr.Zero;var id=new Guid("9B7E4E00-342C-4106-A19F-4F2704F689F0");
   try{Marshal.ThrowExceptionForHR(Marshal.QueryInterface(context,ref id,out multithread));Method<ProtectThreads>(multithread,5)(multithread,1);}finally{Release(ref multithread);}
   copy=Method<CopyRegion>(context,46);generate=Method<Generate>(context,54);
  }
  void Allocate(int width,int height,int mip,int outputWidth,int outputHeight){
   Clear();var create=Method<CreateTexture>(native,5);
   var desc=new TextureDescription{width=(uint)width,height=(uint)height,mips=(uint)mip+1,arraySize=1,format=87,samples=1,bind=0x28,misc=1};
   Marshal.ThrowExceptionForHR(create(native,ref desc,IntPtr.Zero,out mips));
   Marshal.ThrowExceptionForHR(Method<CreateView>(native,7)(native,mips,IntPtr.Zero,out view));
   desc.width=(uint)outputWidth;desc.height=(uint)outputHeight;desc.mips=1;desc.bind=0;desc.misc=0;
   Marshal.ThrowExceptionForHR(create(native,ref desc,IntPtr.Zero,out target));
   IntPtr dxgi=IntPtr.Zero,projected=IntPtr.Zero;var id=new Guid("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
   try{Marshal.ThrowExceptionForHR(Marshal.QueryInterface(target,ref id,out dxgi));Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgi,out projected));result=(IDirect3DSurface)Marshal.GetObjectForIUnknown(projected);}finally{Release(ref projected);Release(ref dxgi);}
   sourceWidth=width;sourceHeight=height;level=mip;Allocations++;
  }
  internal IDirect3DSurface Scale(IDirect3DSurface surface,int width,int height,int maximumWidth,int maximumPixels){
   if(surface==null||width<1||height<1||(long)width*height>16777216||maximumWidth<1||maximumPixels<1)throw new ArgumentException("Invalid GPU resize dimensions.");
   int mip=0,w=width,h=height;while(w>maximumWidth||(long)w*h>maximumPixels){w=Math.Max(1,w/2);h=Math.Max(1,h/2);mip++;}
   double previousScale=Math.Min(1,Math.Min((double)maximumWidth/width,Math.Sqrt((double)maximumPixels/((long)width*height))));
   long previousPixels=(long)Math.Max(1,(int)Math.Floor(width*previousScale))*Math.Max(1,(int)Math.Floor(height*previousScale));
   // A mip must retain at least 90% of the previous output detail budget.
   // Otherwise keep the original surface for the existing arbitrary CPU resize.
   if(mip==0||(long)w*h*10<previousPixels*9){Clear();return surface;}
   IntPtr source=IntPtr.Zero;var id=new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
   try{
    source=((DxgiAccess)surface).GetInterface(ref id);
    // Share immediate-context protection with WinRT's asynchronous readback.
    lock(deviceLock){
     if(native==IntPtr.Zero)Initialize(source);
     if(sourceWidth!=width||sourceHeight!=height||level!=mip)Allocate(width,height,mip,w,h);
     copy(context,mips,0,0,0,0,source,0,IntPtr.Zero);generate(context,view);copy(context,target,0,0,0,0,mips,(uint)mip,IntPtr.Zero);
    }
    return result;
   }finally{Release(ref source);}
  }
  public void Dispose(){lock(deviceLock){Clear();Release(ref context);Release(ref native);}}
 }
 public void Dispose(){if(disposed)return;disposed=true;Available=null;if(session!=null)session.Dispose();if(pool!=null){pool.FrameArrived-=FrameAvailable;pool.Dispose();}if(downsample!=null)downsample.Dispose();downsample=null;session=null;pool=null;item=null;}
}
