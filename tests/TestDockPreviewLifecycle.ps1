$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$source=Get-Content (Join-Path $repo 'src/LiquidDesktop/LiquidDesktop.cs') -Raw
$start=$source.IndexOf(' void PreviewTick(Point cursor){',[StringComparison]::Ordinal)
$end=$source.IndexOf(' protected override bool ShowWithoutActivation',$start,[StringComparison]::Ordinal)
if($start -lt 0 -or $end -lt 0){throw 'PreviewTick method was not found'}
# Compile the unchanged production method with recording collaborators. No HWNDs are created.
$method=$source.Substring($start,$end-$start)
$code=@'
using System;using System.Drawing;using System.Collections.Generic;
public sealed class Entry {public string id;public string name="App";public bool system;public List<int> windows=new List<int>{1};}
public sealed class DockPreview {
 public static bool PickerOpen; public bool IsDisposed,KeyboardMode,Shown,Dismissing;public string EntryId;public Rectangle Bounds=new Rectangle(200,300,400,200);
 public DockPreview(string entry,Rectangle dock,bool astro){EntryId=entry;}
 public DockPreview(Entry entry,Rectangle dock,bool astro):this(entry.id,dock,astro){}
 public void Dispose(){IsDisposed=true;}public void Show(){Shown=true;}public void Dismiss(){Dismissing=true;}public void Revive(){Dismissing=false;}
}
public sealed class DesktopWidgets {public string Hover(Point p,out Rectangle bounds,out string caption){bounds=Rectangle.Empty;caption="";return "";}}
public sealed class StageManager {public string Hover(Point p,out Rectangle bounds,out string caption){bounds=Rectangle.Empty;caption="";return "";}}
public static class NativeHintHost {public static bool shown;public static Rectangle anchor=new Rectangle(100,100,50,40);public static bool Hover(Point p,out Rectangle bounds,out string caption){bounds=anchor;caption=shown?"Settings hint":"";return shown;}}
public sealed class Backend {public Entry Entry(string id){return id=="removed"?null:new Entry{id=id,system=id=="workspace",windows=id=="empty"?new List<int>():new List<int>{1}};}}
public sealed class DockLabel {public DockLabel(IntPtr h,Action<object> send,Func<Rectangle> screen){}public bool Visible;public static bool TryAnchor(string value,string id,out double anchor){anchor=.5;return value==id&&id.Length>0;}public void Display(string name,Rectangle dock,double anchor,bool astro){Visible=true;}public void Hide(){Visible=false;}}
public static class DockBackend {public static bool Interaction(string root){return false;}}
public sealed class PreviewLifecycle {
 DockLabel dockLabel;string root="";IntPtr Handle;void Send(object value){}bool paused,draggingDock;DockPreview preview;Rectangle screen=new Rectangle(0,0,1200,800),dockRectangle=new Rectangle(100,600,1000,100);bool dockShown=true;string instance="primary",theme="Sky",hoverId="",nextId="";
 readonly PreviewLifecycle coordinator;bool WorkspaceOpen;string previewSuppressedId="";readonly DesktopWidgets desktopWidgets=new DesktopWidgets();readonly StageManager stages=new StageManager();
 bool dockCovered;bool DockPointerAt(Point at){return dockShown&&!dockCovered&&dockRectangle.Contains(at);}
 DateTime previewLeave,hoverSince;readonly Backend backend=new Backend();int checks;
 PreviewLifecycle(){coordinator=this;}
 string Read(string file,string fallback){return nextId;}
 void Check(bool passed,string label){if(!passed)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
 METHOD
 public static void Main(){new PreviewLifecycle().Run();}
 void Run(){
  var dockPoint=new Point(400,650);var old=new DockPreview("old",dockRectangle,false);preview=old;hoverId="old";nextId="new";
  PreviewTick(dockPoint);Check(old.Dismissing&&preview==null,"new icon starts the previous preview exit and releases hover ownership");
  Check(hoverId=="new"&&(DateTime.UtcNow-hoverSince).TotalMilliseconds<100,"new icon retains its own hover delay");
  hoverSince=DateTime.UtcNow.AddMilliseconds(-450);PreviewTick(dockPoint);Check(preview!=null&&preview.EntryId=="new"&&preview.Shown,"new preview opens after its own delay");
  var current=preview;nextId="";PreviewTick(new Point(400,550));Check(preview==current&&!current.IsDisposed,"leaving Dock toward the preview keeps the 400 ms grace period");
  PreviewTick(new Point(400,400));Check(preview==current&&previewLeave>DateTime.UtcNow.AddMilliseconds(300),"entering preview refreshes its leave grace period");
  previewLeave=DateTime.UtcNow.AddMilliseconds(-1);PreviewTick(new Point(900,200));Check(preview==current&&current.Dismissing,"leaving both Dock and preview starts the exit after the grace period");current.Dispose();PreviewTick(new Point(900,200));Check(preview==null,"completed exit releases the preview reference");
  preview=new DockPreview("keyboard",dockRectangle,false){KeyboardMode=true};nextId="new";PreviewTick(dockPoint);Check(preview.EntryId=="keyboard"&&!preview.IsDisposed,"keyboard preview keeps its existing navigation ownership");
  current=preview;WorkspaceOpen=true;PreviewTick(dockPoint);Check(preview==null&&current.IsDisposed,"opening the workspace dismisses an existing Dock preview");
  hoverId="new";hoverSince=DateTime.UtcNow.AddSeconds(-5);PreviewTick(dockPoint);Check(preview==null&&hoverId=="","idle pointer over Dock cannot create a preview while the workspace is open");
  WorkspaceOpen=false;PreviewTick(dockPoint);Check(preview==null,"returning from workspace starts a fresh hover delay");
  hoverSince=DateTime.UtcNow.AddMilliseconds(-450);PreviewTick(dockPoint);Check(preview!=null&&preview.EntryId=="new","normal application previews resume after the workspace closes");
  foreach(var id in new[]{"workspace","empty","removed"}){nextId=id;PreviewTick(dockPoint);hoverSince=DateTime.UtcNow.AddMilliseconds(-450);PreviewTick(dockPoint);Check(preview==null,id+" entry never creates an empty window preview");}
  nextId="empty";PreviewTick(dockPoint);Check(dockLabel!=null&&dockLabel.Visible,"only hovered app owns the capsule without a window preview");
  PreviewTick(new Point(900,200));Check(!dockLabel.Visible,"leaving hides the capsule immediately");
  nextId="empty";draggingDock=true;PreviewTick(dockPoint);Check(!dockLabel.Visible,"dragging suppresses the capsule");draggingDock=false;
  nextId="new";hoverId="new";hoverSince=DateTime.UtcNow.AddSeconds(-2);draggingDock=true;PreviewTick(dockPoint);Check(preview==null&&hoverId=="","dragging also cancels pending application previews");draggingDock=false;
  dockCovered=true;PreviewTick(dockPoint);hoverSince=DateTime.UtcNow.AddSeconds(-2);PreviewTick(dockPoint);Check(preview==null&&!dockLabel.Visible,"another application covering the Dock cannot reuse its stale hover file");dockCovered=false;
  PreviewTick(dockPoint);WorkspaceOpen=true;PreviewTick(dockPoint);Check(!dockLabel.Visible,"workspace opening hides the capsule");
  WorkspaceOpen=false;preview=null;nextId="new";hoverId="new";hoverSince=DateTime.UtcNow.AddSeconds(-2);DockPreview.PickerOpen=true;PreviewTick(dockPoint);Check(preview==null,"keyboard picker prevents a second hover preview");DockPreview.PickerOpen=false;previewSuppressedId="new";PreviewTick(dockPoint);Check(preview==null,"click suppression prevents immediate preview reopening");nextId="";PreviewTick(new Point(900,200));nextId="new";PreviewTick(dockPoint);hoverSince=DateTime.UtcNow.AddSeconds(-2);PreviewTick(dockPoint);Check(preview!=null,"leaving and reentering permits the next intentional preview");
  NativeHintHost.shown=true;WorkspaceOpen=true;PreviewTick(dockPoint);Check(preview==null&&dockLabel.Visible,"settings hints use the shared capsule even while application previews are suppressed");NativeHintHost.shown=false;PreviewTick(dockPoint);Check(!dockLabel.Visible,"leaving a settings control releases its shared capsule");
  NativeHintHost.shown=true;NativeHintHost.anchor=new Rectangle(1300,100,50,40);PreviewTick(dockPoint);Check(!dockLabel.Visible,"keyboard hint on another monitor is not sampled from the pointer monitor");screen=new Rectangle(1200,0,1200,800);PreviewTick(new Point(-1100,120));Check(dockLabel.Visible,"keyboard hint uses its target monitor even when the pointer remains on the first monitor");
  Console.WriteLine(checks+" Dock preview lifecycle checks passed.");
 }
}
'@
$code=$code.Replace(' METHOD',$method)
$out=Join-Path $repo 'artifacts/tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$testSource=Join-Path $out 'TestDockPreviewLifecycle.generated.cs'
$testExe=Join-Path $out 'TestDockPreviewLifecycle.exe'
try{
 Set-Content -LiteralPath $testSource -Value $code -Encoding utf8
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo "/out:$testExe" /r:System.Drawing.dll $testSource
 if($LASTEXITCODE -ne 0){throw 'Preview lifecycle compilation failed'}
 & $testExe | Tee-Object (Join-Path $out 'dock-preview-lifecycle-checks.txt')
 if($LASTEXITCODE -ne 0){throw 'Preview lifecycle check failed'}
}finally{if(Test-Path -LiteralPath $testSource){Remove-Item -LiteralPath $testSource -Force}}
