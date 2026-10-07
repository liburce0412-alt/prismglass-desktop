param([ValidateSet('Apply','Restore','Validate')][string]$Mode='Apply',[ValidateRange(75,200)][int]$Scale)
$ErrorActionPreference='Stop'
$marker=Join-Path $PSScriptRoot 'cursor-theme.active'
$cursorRoot=Join-Path $PSScriptRoot 'Cursors'
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'cursors-path.txt')){$cursorRoot=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'cursors-path.txt') -Raw).Trim()}
if(!('PrismCursorNative' -as [type])){
 Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class PrismCursorNative {
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]public static extern IntPtr LoadImage(IntPtr instance,string name,uint type,int cx,int cy,uint flags);
 [DllImport("user32.dll",SetLastError=true)]public static extern bool SetSystemCursor(IntPtr cursor,uint id);
 [DllImport("user32.dll")]public static extern bool DestroyCursor(IntPtr cursor);
 [DllImport("user32.dll",SetLastError=true)]public static extern bool SystemParametersInfo(uint action,uint param,IntPtr data,uint flags);
 [DllImport("user32.dll")]public static extern int GetSystemMetrics(int index);
 [DllImport("user32.dll")]public static extern bool SetProcessDPIAware();
}
"@
}
function Restore-Cursors {
 if(!(Test-Path -LiteralPath $marker)){return}
 if(![PrismCursorNative]::SystemParametersInfo(0x57,0,[IntPtr]::Zero,0)){throw 'Unable to restore saved Windows cursors.'}
 Remove-Item -LiteralPath $marker
}
if($Mode -eq 'Restore'){Restore-Cursors;return}
$map=[ordered]@{
 32512='Normal.cur';32513='Text.cur';32514='Busy.ani';32515='Precision.cur'
 32516='Alternate.cur';32642='Diagonal Resize 1.cur';32643='Diagonal Resize 2.cur'
 32644='Horizontal Resize.cur';32645='Vertical Resize.cur';32646='Move.cur'
 32648='Unavailable.cur';32649='Link.cur';32650='Working.ani';32651='Help.cur'
 32671='Pin.cur';32672='Person.cur'
}
[void][PrismCursorNative]::SetProcessDPIAware()
$scalePath=Join-Path $PSScriptRoot 'cursor-scale.txt'
if(!$PSBoundParameters.ContainsKey('Scale')){
 $Scale=100
 if(Test-Path -LiteralPath $scalePath){$saved=0;if([int]::TryParse((Get-Content -LiteralPath $scalePath -Raw).Trim(),[ref]$saved) -and $saved -ge 75 -and $saved -le 200){$Scale=$saved}}
}
$width=[Math]::Max(16,[int][Math]::Round([PrismCursorNative]::GetSystemMetrics(13)*$Scale/100.0))
$height=[Math]::Max(16,[int][Math]::Round([PrismCursorNative]::GetSystemMetrics(14)*$Scale/100.0))
$handles=@{}
try {
 foreach($entry in $map.GetEnumerator()){
  $path=Join-Path $cursorRoot $entry.Value
  if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing cursor: $path"}
  $handle=[PrismCursorNative]::LoadImage([IntPtr]::Zero,$path,2,$width,$height,0x10)
  if($handle -eq [IntPtr]::Zero){throw "Cannot load cursor: $path"}
  $handles[$entry.Key]=$handle
 }
 if($Mode -eq 'Validate'){"Validated $($handles.Count) native cursor resources.";return}
 'Transient macOS-style cursor theme; restore from Windows settings on exit.' | Set-Content -LiteralPath $marker
 foreach($id in @($handles.Keys)){
  if(![PrismCursorNative]::SetSystemCursor($handles[$id],[uint32]$id)){throw "Cannot apply cursor role $id"}
  # Windows owns and destroys this handle after a successful SetSystemCursor.
  $handles.Remove($id)
 }
 [IO.File]::WriteAllText($scalePath,$Scale.ToString())
 'Applied macOS-style cursors without changing the saved Windows pointer theme.'
} catch {
 if($Mode -eq 'Apply'){Restore-Cursors}
 throw
} finally {
 foreach($handle in $handles.Values){[void][PrismCursorNative]::DestroyCursor($handle)}
}
