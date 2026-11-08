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
 [DllImport("kernel32.dll",EntryPoint="RtlMoveMemory")]static extern void CopyMemory(IntPtr target,IntPtr source,UIntPtr bytes);
 [DllImport("d3d11.dll")]static extern int D3D11CreateDevice(IntPtr adapter,int driver,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out int level,out IntPtr context);
 [DllImport("d3d11.dll")]static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi,out IntPtr device);
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
 readonly IntPtr window;readonly uint pid;readonly bool borderless;GraphicsCaptureItem item;Direct3D11CaptureFramePool pool;GraphicsCaptureSession session;SizeInt32 size;bool disposed,busy;
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
 internal async Task<SoftwareBitmap> Next(){
  if(busy||!Valid)return null;busy=true;Direct3D11CaptureFrame frame=null;SizeInt32 resize=new SizeInt32();bool recreate=false;
  try{frame=pool.TryGetNextFrame();for(int i=0;i<3&&frame!=null;i++){var newer=pool.TryGetNextFrame();if(newer==null)break;frame.Dispose();frame=newer;}
   if(frame==null)return null;var next=frame.ContentSize;Budget(next);
   RECT bounds;if(DwmGetWindowAttribute(window,9,out bounds,16)==0&&(next.Width!=bounds.right-bounds.left||next.Height!=bounds.bottom-bounds.top)){Diagnostic="frame="+next.Width+"x"+next.Height+" window="+(bounds.right-bounds.left)+"x"+(bounds.bottom-bounds.top)+" pool="+size.Width+"x"+size.Height;return null;}
   if(next.Width!=size.Width||next.Height!=size.Height){resize=next;recreate=true;return null;}
   var operation=SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface,BitmapAlphaMode.Premultiplied);var state=(Windows.Foundation.IAsyncInfo)operation;var finished=new TaskCompletionSource<SoftwareBitmap>();
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
 public void Dispose(){if(disposed)return;disposed=true;Available=null;if(session!=null)session.Dispose();if(pool!=null){pool.FrameArrived-=FrameAvailable;pool.Dispose();}session=null;pool=null;item=null;}
}
