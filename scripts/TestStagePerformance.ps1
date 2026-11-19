param([string]$CacheRoot='D:/Cache/PrismGlass',[ValidatePattern('^[a-z0-9-]+$')][string]$RunName='baseline',[switch]$MergeOnly,[switch]$CloseSurface)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $repo ('artifacts/iteration6-20261112/stage/'+$RunName)
$app=Join-Path $out 'app'
$captures=Join-Path $out 'captures'
$sdk=Join-Path $CacheRoot 'webview2-1.0.4191.47'
$process=$null
New-Item -ItemType Directory -Path $app,$captures -Force | Out-Null
try {
 foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll')){Copy-Item -LiteralPath "$sdk/lib/net462/$name" -Destination $app -Force}
 Copy-Item -LiteralPath "$sdk/runtimes/win-x64/native/WebView2Loader.dll" -Destination $app -Force
 $sources=@('tests/TestStagePerformance.cs','src/LiquidDesktop/NativeWindowMotion.cs','src/LiquidDesktop/StageManager.cs','src/LiquidDesktop/StageThumbnailCapture.cs','src/LiquidDesktop/StageStrip.cs','src/LiquidDesktop/ComponentPosition.cs','src/LiquidDesktop/DesktopVisibilityPolicy.cs','src/LiquidDesktop/LiveGlassBackdrop.cs','src/LiquidDesktop/OpticalBackdrop.cs','src/LiquidDesktop/SharedWindowCapture.cs','src/LiquidDesktop/WindowCapture.cs','src/LiquidDesktop/NativeGlassMaterial.cs','src/LiquidDesktop/MonitorWallpaper.cs','src/LiquidDesktop/GlassWindow.cs','src/LiquidDesktop/WebViewVisibility.cs','src/LiquidDesktop/WebViewPixelScale.cs','src/LiquidDesktop/DockBackend.cs','src/LiquidDesktop/IslandEvents.cs','src/LiquidDesktop/DockPreview.cs','src/LiquidDesktop/DesktopPreferences.cs','src/LiquidDesktop/RuntimePolicy.cs','src/LiquidDesktop/MonitorCatalog.cs') | ForEach-Object {Join-Path $repo $_}
 $stageTrace=Join-Path $app 'StageManager.trace.cs'
 $stageText=[IO.File]::ReadAllText((Join-Path $repo 'src/LiquidDesktop/StageManager.cs')).Replace('Error=ex.Message;Stop();','Error=ex.ToString();Stop();')
 $stageText=$stageText.Replace('if(!await PrepareLayout(group))return;','TestStagePerformance.Phase("layout-start");if(!await PrepareLayout(group))return;TestStagePerformance.Phase("layout-end");')
 $stageText=$stageText.Replace('if(!await PrepareLayout(proposed))return;','TestStagePerformance.Phase("merge-layout-start");if(!await PrepareLayout(proposed))return;TestStagePerformance.Phase("merge-layout-end");')
 $stageText=$stageText.Replace('RefreshStrip(group.monitor);Fly(previous,false);Fly(group,true,origins);','TestStagePerformance.Phase("surface-start");RefreshStrip(group.monitor);Fly(previous,false);Fly(group,true,origins);TestStagePerformance.Phase("surface-created");')
 $stageText=$stageText.Replace('await Task.WhenAny(ready,Task.Delay(500));','await Task.WhenAny(ready,Task.Delay(500));TestStagePerformance.Phase("surface-ready");foreach(var pendingSurface in flights)TestStagePerformance.Phase("ready-state:"+pendingSurface.member.window.title+":"+pendingSurface.surface.GetHashCode()+":"+pendingSurface.surface.Ready.IsCompleted+":loaded="+pendingSurface.surface.IsLoaded+":visible="+pendingSurface.surface.IsVisible);')
 $stageText=$stageText.Replace('await NativeWindowMotion.Compose();','TestStagePerformance.Phase("compose-start");await NativeWindowMotion.Compose();TestStagePerformance.Phase("compose-end");')
 $stageText=$stageText.Replace('await Animate(version);','TestStagePerformance.Phase("animate-start");await Animate(version);TestStagePerformance.Phase("animate-end");')
 $stageText=$stageText.Replace('captured=await NativeWindowMotion.Snapshot(member.window,Rectangle.FromLTRB(rect.l,rect.t,rect.r,rect.b));','{TestStagePerformance.Phase("snapshot-start");captured=await NativeWindowMotion.Snapshot(member.window,Rectangle.FromLTRB(rect.l,rect.t,rect.r,rect.b));TestStagePerformance.Phase("snapshot-end");}')
 $stageText=$stageText.Replace('var texture=await Task.Run(()=>','TestStagePerformance.Phase("texture-start");var texture=await Task.Run(()=>')
 $stageText=$stageText.Replace('if(!wanted()||!StageThumbnailCapture.Allowed(member.window))return;pictures[','TestStagePerformance.Phase("texture-end");if(!wanted()||!StageThumbnailCapture.Allowed(member.window))return;pictures[')
 $stageText=$stageText.Replace('foreach(var member in group.members){var incoming=','TestStagePerformance.Phase("restore-start");foreach(var member in group.members){var incoming=')
 $stageText=$stageText.Replace('if(queued!=null)Retarget(queued,queuedFocus);','TestStagePerformance.Phase("restore-end");if(queued!=null)Retarget(queued,queuedFocus);')
 [IO.File]::WriteAllText($stageTrace,$stageText)
 $sources=@($sources | ForEach-Object {if([IO.Path]::GetFileName($_) -eq 'StageManager.cs'){$stageTrace}else{$_}})
 $motionTrace=Join-Path $app 'NativeWindowMotion.trace.cs'
 $motionText=[IO.File]::ReadAllText(($sources | Where-Object {[IO.Path]::GetFileName($_) -eq 'NativeWindowMotion.cs'}))
 $motionText=$motionText.Replace('ContentRendered+=(s,e)=>ready.TrySetResult(true);','Loaded+=(s,e)=>TestStagePerformance.Phase("surface-event-loaded:"+GetHashCode());ContentRendered+=(s,e)=>{TestStagePerformance.Phase("surface-event-rendered:"+GetHashCode());ready.TrySetResult(true);};')
 [IO.File]::WriteAllText($motionTrace,$motionText)
 $sources=@($sources | ForEach-Object {if([IO.Path]::GetFileName($_) -eq 'NativeWindowMotion.cs'){$motionTrace}else{$_}})
 & "$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/csc.exe" /nologo /utf8output /target:exe /platform:x64 "/out:$app/TestStagePerformance.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Numerics.dll /r:System.Numerics.Vectors.dll /r:System.Runtime.dll /r:System.Runtime.WindowsRuntime.dll /r:System.Runtime.InteropServices.WindowsRuntime.dll "/r:$env:WINDIR/System32/WinMetadata/Windows.Foundation.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Graphics.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Storage.winmd" "/r:$env:WINDIR/System32/WinMetadata/Windows.Security.winmd" /r:System.Xaml.dll "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/WindowsFormsIntegration.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/WindowsBase.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/PresentationCore.dll" "/r:$env:WINDIR/Microsoft.NET/Framework64/v4.0.30319/WPF/PresentationFramework.dll" /r:System.Web.Extensions.dll "/r:$app/Microsoft.Web.WebView2.Core.dll" "/r:$app/Microsoft.Web.WebView2.WinForms.dll" @sources
 if($LASTEXITCODE -ne 0){throw 'Stage performance test compilation failed'}
 $arguments='"'+$app+'" "'+$captures+'"'
 if($MergeOnly){$arguments+=' --merge-only'}
 if($CloseSurface){$arguments+=' --close-surface'}
 $process=Start-Process -FilePath "$app/TestStagePerformance.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru
 if(!$process.WaitForExit(30000)){throw 'Native effects short checks timed out'}
 Get-Content -LiteralPath "$captures/checks.txt"
 if($process.ExitCode -ne 0){throw 'Native effects checks failed'}
} finally {
 if($process){if(!$process.HasExited){Stop-Process -Id $process.Id};$process.Dispose()}
 $ownedRoot=[IO.Path]::GetFullPath($app)+[IO.Path]::DirectorySeparatorChar
 # Only the three owned source processes and fixture may be cleaned up.
 Get-CimInstance Win32_Process -Filter "Name='TestStagePerformance.exe' OR Name='msedgewebview2.exe'" | Where-Object {($_.ExecutablePath -and $_.ExecutablePath.StartsWith($ownedRoot,[StringComparison]::OrdinalIgnoreCase)) -or ($_.CommandLine -and $_.CommandLine.Contains($ownedRoot))} | ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue}
 if(Test-Path -LiteralPath $app){$path=(Resolve-Path -LiteralPath $app).Path;if(!$path.StartsWith([IO.Path]::GetFullPath($out)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup escaped test artifacts'};for($attempt=0;$attempt -lt 30;$attempt++){try{Remove-Item -LiteralPath $path -Recurse -Force;break}catch{if($attempt -eq 29){throw};Start-Sleep -Milliseconds 200}};if(Test-Path -LiteralPath $path){throw 'Cleanup failed'}}
}
