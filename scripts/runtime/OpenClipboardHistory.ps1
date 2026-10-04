# Invoked only by the user's widget click; does not read clipboard contents.
$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ClipboardHistoryLauncher {
 [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
 public static void Open() {
  keybd_event(0x5B,0,0,UIntPtr.Zero);
  try {
   keybd_event(0x56,0,0,UIntPtr.Zero);
   keybd_event(0x56,0,2,UIntPtr.Zero);
  } finally { keybd_event(0x5B,0,2,UIntPtr.Zero); }
 }
}
'@
[ClipboardHistoryLauncher]::Open()
