-- Runs with a disposable off-screen Rainmeter profile and a mocked SKIN.
function Initialize()
 local nativeSkin=SKIN
 local folder=nativeSkin:GetVariable('Fixture')
 local script=nativeSkin:GetVariable('ProductionScript')
 local report=assert(io.open(folder..'lua-checks.txt','w'))
 local checks=0
 local function check(value,name)report:write((value and 'PASS ' or 'FAIL ')..name..'\n');report:flush();if not value then error(name) end;checks=checks+1 end
 local function write(file,value)local f=assert(io.open(folder..file,'w'));f:write(value);f:close()end
 local function read(file)local f=assert(io.open(folder..file,'r'));local value=f:read('*a');f:close();return value end
 local options,shown={},{}
 local vars={LiquidRoot=folder,DockInstance='monitor-1',DockMonitorLeft='-1600',DockMonitorTop='0',DockMonitorWidth='1600',DockMonitorHeight='900',DockPixelScale='1.25',DockOffset='18',DockIconSize='48',DockLyricsFile='lyrics-monitor-1.txt',SCREENAREAWIDTH='1920',SCREENAREAHEIGHT='1080'}
 local moved
 SKIN={GetVariable=function(_,k)return vars[k]end,Bang=function(_,command,meter,key,value)
  if command=='!SetOption' then options[meter..'.'..key]=value elseif command=='!SetVariable' then vars[meter]=key elseif command=='!ShowMeter' then shown[meter]=true elseif command=='!HideMeter' then shown[meter]=false elseif command=='!Move' then moved={meter,key} end
 end}
 local originalTime=os.time;local fakeTime=2000000000;os.time=function()return fakeTime end
 local ok,err=pcall(function()
  dofile(script);Initialize()
  local rows='workspace\tQXBwcw==\tR3JpZA==\t0\t0\t1\t0\npexplorer\tRmlsZXM=\tSWNvbkE=\t1\t0\t1\t0\np23\tRWRnZQ==\tSWNvbkI=\t0\t0\t1\t0\n'
  write('dock-state.txt',rows);write('lyrics-monitor-1.txt',fakeTime+3 ..'|0|1\n\n\n');Update()
  check(#items==3 and items[1].id=='workspace' and items[2].id=='pexplorer','system and legacy nonnumeric pin IDs remain visible')
  check(options['Icon2.LeftMouseDownAction']=='[!CommandMeasure Dock "Press(2)"]' and options['Icon2.LeftMouseUpAction']=='','real icon press starts the transaction without a second launch on mouse-up')
  Press(2);check(read('dock-drag-request-monitor-1.txt')==fakeTime..'|pexplorer','Rainmeter routes a real press to its own native Dock instance')
  Hover(2);check(read('dock-hover-monitor-1.txt')=='pexplorer','window-preview hover is isolated per monitor')
  check(read('dock-layout-monitor-1.json'):find('"dpi":1.25',1,true)~=nil and moved[1]<0,'monitor placement and diagnostics preserve negative coordinates and DPI scale')
  local expectedLeft=math.ceil((16+92*math.min(1.25,(900-240)/680,(1600-48)/852))/1.25)
  check(moved[1]==-1600+expectedLeft,'left-side monitor origin is preserved while local dimensions are scaled')
  vars.DockMonitorLeft='2560';vars.DockMonitorTop='-164';vars.DockMonitorWidth='2560';vars.DockMonitorHeight='1560';ReloadLayout()
  check(moved[1]==2665 and math.abs(moved[2]-965.6)<.001,'right-side mixed-DPI monitor preserves positive X and negative Y origins')
  for _,c in ipairs({
   {'-2560','360','1920','1040','1.5',-2477,360+1040/1.5-104-18/1.5,'150 percent left monitor with nonzero Y'},
   {'900','1700','1280','720','2',941,1947,'200 percent smaller separated monitor below desktop'},
   {'-800','-100','1920','1080','1',-670,858,'100 percent monitor with negative X and Y'}
  }) do
   vars.DockMonitorLeft=c[1];vars.DockMonitorTop=c[2];vars.DockMonitorWidth=c[3];vars.DockMonitorHeight=c[4];vars.DockPixelScale=c[5];ReloadLayout()
   check(math.abs(moved[1]-c[6])<.001 and math.abs(moved[2]-c[7])<.001,c[8]..' uses its own origin and work area')
  end
  vars.DockMonitorLeft='-1600';vars.DockMonitorTop='0';vars.DockMonitorWidth='1600';vars.DockMonitorHeight='900';vars.DockPixelScale='1.25';ReloadLayout()
  SetIconOpacity(255);Hover(0);write('dock-drag-feedback-monitor-1.txt',fakeTime+2 ..'|2|0|0');write('lyrics-monitor-1.txt',fakeTime+3 ..'|1|1\nYQ==\nYg==\n');for i=1,70 do Update() end
  check(shown.DragMarker and math.abs(tonumber(options['DragMarker.X'])-16-2*cell)<.01 and options['Icon2.ImageAlpha']=='255','insertion feedback holds application icons above the lyrics')
  write('dock-state.txt','workspace\tQXBwcw==\tR3JpZA==\t0\t0\t1\t0\np23\tRWRnZQ==\tSWNvbkI=\t0\t0\t1\t0\npexplorer\tRmlsZXM=\tSWNvbkE=\t1\t0\t1\t0\n');fakeTime=fakeTime+1;Update()
  check(items[2].id=='pexplorer','background application refresh does not shift hit targets during a drag')
  write('dock-drag-feedback-monitor-1.txt',fakeTime+2 ..'|0|1|0');for i=1,5 do Update() end
  check(not shown.DragMarker and options['DragHint.Text']:find('保留原文件',1,true)~=nil,'dragging outside clearly describes unpinning without file deletion')
  write('dock-drag-feedback-monitor-1.txt','');for i=1,5 do Update() end
  check(not dragActive and not shown.DragHint and items[2].id=='p23','ending the drag clears feedback and applies the committed order')
  vars.DockIconSize='60';vars.DockOffset='24';ReloadLayout();Hover(0);write('dock-motion-monitor-1.txt','true');for i=1,18 do Update() end;Hover(2);for i=1,5 do Update() end
  check(tonumber(options['Icon2.W'])==60 and tonumber(options['Icon2.Y'])==24,'per-monitor pause stops bounce and live icon-size settings are applied')
  check(math.abs(moved[2]-(900/1.25-104-24/1.25))<.001,'live bottom offset remains physical pixels at 125 percent scale')
  SetIconOpacity(0);Hover(0);write('dock-drag-feedback-monitor-1.txt','0|1|0|1');write('lyrics-monitor-1.txt',fakeTime+3 ..'|1|1\nYQ==\nYg==\n');for i=1,90 do Update() end
  check(options['Icon2.ImageAlpha']=='0','a stale drag cannot permanently block per-monitor lyrics')
 end)
 if not ok then report:write('ERROR '..tostring(err)..'\n')else report:write('Completed '..checks..' native Dock interaction Lua checks.\n')end
 report:close();os.time=originalTime;SKIN=nativeSkin;Update=function()return 0 end
end

