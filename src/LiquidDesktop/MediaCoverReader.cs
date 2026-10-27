using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

// Framework PowerShell exposes some WinRT thumbnail streams as untyped RCWs.
public static class MediaCoverReader {
 [DllImport("shcore.dll")]static extern int CreateStreamOverRandomAccessStream(IntPtr source,ref Guid iid,out IStream stream);
 public static byte[] Read(object source,int limit){
  if(source==null)return new byte[0];IntPtr unknown=IntPtr.Zero,count=IntPtr.Zero;IStream stream=null;
  try{unknown=Marshal.GetIUnknownForObject(source);var iid=typeof(IStream).GUID;Marshal.ThrowExceptionForHR(CreateStreamOverRandomAccessStream(unknown,ref iid,out stream));System.Runtime.InteropServices.ComTypes.STATSTG state;stream.Stat(out state,1);if(state.cbSize<=0||state.cbSize>limit)return new byte[0];
   var bytes=new byte[(int)state.cbSize];count=Marshal.AllocCoTaskMem(4);stream.Read(bytes,bytes.Length,count);int read=Marshal.ReadInt32(count);if(read!=bytes.Length)return new byte[0];return bytes;
  }finally{if(count!=IntPtr.Zero)Marshal.FreeCoTaskMem(count);if(stream!=null&&Marshal.IsComObject(stream))Marshal.ReleaseComObject(stream);if(unknown!=IntPtr.Zero)Marshal.Release(unknown);}
 }
}
