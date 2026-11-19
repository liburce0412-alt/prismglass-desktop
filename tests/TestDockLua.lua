-- Real Lua interpreter in an isolated off-screen Rainmeter; only SKIN is recorded.
function Initialize()
 local nativeSkin=SKIN
 local folder=nativeSkin:GetVariable('Fixture')
 local script=nativeSkin:GetVariable('ProductionScript')
 local report=assert(io.open(folder..'lua-checks.txt','w'))
 local checks=0
 local function check(value,name)report:write((value and 'PASS ' or 'FAIL ')..name..'\n');report:flush();if not value then error(name) end;checks=checks+1 end
 local function write(file,value)local f=assert(io.open(folder..file,'w'));f:write(value);f:close()end
 local options,shown={},{}
 local updates,redraws,wildcardUpdates,duplicateOptions,hoverWrites=0,0,0,0,0
 local anchorWrites=0
 local originalOpen=io.open;local failLabel=false
 io.open=function(path,mode)if mode=='w' and path:find('dock-anchors',1,true) then anchorWrites=anchorWrites+1 end;if mode=='w' and path:find('dock-label',1,true) and failLabel then failLabel=false;return nil,'fixture sharing conflict' end;if mode=='w' and path:find('dock-hover',1,true) then hoverWrites=hoverWrites+1 end;return originalOpen(path,mode)end
 local fastCommands={}
 local vars={LiquidRoot=folder,Ink='255,255,255',Muted='227,237,249',SCREENAREAWIDTH='1920',SCREENAREAHEIGHT='1080'}
 SKIN={GetVariable=function(_,k)return vars[k]end,Bang=function(_,command,meter,key,value)
  if command=='!CommandMeasure' and meter=='DockFrames' then fastCommands[#fastCommands+1]=key end
  if command=='!SetOption' then if options[meter..'.'..key]==value then duplicateOptions=duplicateOptions+1 end;options[meter..'.'..key]=value elseif command=='!SetVariable' then vars[meter]=key
  elseif command=='!ShowMeter' then shown[meter]=true elseif command=='!HideMeter' then shown[meter]=false
  elseif command=='!UpdateMeter' then updates=updates+1;if meter=='*' then wildcardUpdates=wildcardUpdates+1 end elseif command=='!Redraw' then redraws=redraws+1 end
 end}
 local originalTime,originalClock=os.time,os.clock;local fakeTime,fakeClock,frameStep=2000000000,0,.016;os.time=function()return fakeTime end;os.clock=function()fakeClock=fakeClock+frameStep;return fakeClock end
 local ok,err=pcall(function()
  dofile(script);Initialize()
  for _,state in ipairs({'launching','launch-failed'}) do
   fakeTime=fakeTime+1
   write('dock-state.txt','p1\tVGVzdA==\tVGVzdA==\t0\t0\t1\t0\t'..state..'\n')
   for i=1,80 do Update() end
   check(shown.Dot1 and options['Dot1.Shape']:find(state=='launching' and 'StrokeWidth 1.5' or '245,106,96',1,true),'zero-window '..state..' marker is visible')
   SetIconOpacity(0);assert(not shown.Dot1);SetIconOpacity(255);assert(shown.Dot1)
  end
  fakeTime=fakeTime+1
  write('dock-state.txt','p1\tVGVzdA==\tVGVzdA==\t1\t0\t1\t0\n')
  write('dock-lyrics.txt',fakeTime+3 ..'|1|0\n56ys5LiA5Y+l\nTmV4dA==\n')
  for i=1,80 do Update() end
  check(options['LyricCurrent.Text']==nil and options['LyricNext.Text']==nil,'Unicode text remains owned by the glass WebView')
  check(options['Icon1.ImageAlpha']=='255','legacy shared lyric files cannot run an independent fade clock')
  local u,r=updates,redraws;SetIconOpacity(0)
  check(options['Icon1.ImageAlpha']=='0' and updates>u and redraws>r,'zero opacity completes meter update and redraw before command return')
  check(not shown.Dot1,'running indicators hide with their icons')
  SetIconOpacity(128);check(options['Icon1.ImageAlpha']=='128','intermediate opacity is applied exactly')
  for i=1,30 do Update() end;check(options['Icon1.ImageAlpha']=='128','native frames do not interpolate independently')
  fakeTime=fakeTime+1;u=updates;r=redraws;SetIconOpacity(128)
  check(updates==u and redraws==r and iconLease==fakeTime+3,'identical opacity renews its lease without repainting')
  Approach();Hover(1);Update();check(options['Icon1.ImageAlpha']=='128','approach and hover cannot override the authoritative frame opacity')
  check(options['Hit1.ToolTipHidden']=='1','hovering an invisible hit target cannot reopen a stale tooltip')
  SetIconOpacity(0);check(options['Hit1.ToolTipHidden']=='1','fading icons clears their old tooltip')
  fakeTime=fakeTime+2;Update();check(options['Icon1.ImageAlpha']=='0','valid opacity lease remains active before expiry')
  SetIconOpacity(0);fakeTime=fakeTime+2;Update();check(options['Icon1.ImageAlpha']=='0','repeated zero opacity extends the lease')
  fakeTime=fakeTime+1;Update();check(options['Icon1.ImageAlpha']=='255','lease expiry restores application icons exactly at the deadline')
  SetIconOpacity(128);local lease=iconLease
  fakeTime=fakeTime+1
  for _,value in ipairs({-1,256,1.5,'0',0/0}) do SetIconOpacity(value) end
  check(options['Icon1.ImageAlpha']=='128' and iconLease==lease,'invalid opacity values neither paint nor extend the lease')
  SetIconOpacity(0);fakeTime=fakeTime+1
  write('dock-state.txt','p1\tVGVzdA==\tVGVzdA==\t1\t0\t1\t0\np2\tTmV4dA==\tVGVzdA==\t0\t0\t1\t0\n');Update()
  check(options['Icon2.ImageAlpha']=='0','newly loaded icons inherit the current hidden opacity')
  check(math.abs((items[1].cx+items[2].cx)/2-tonumber(vars.DockWidth)/2)<.001,'an all-pinned row is centered before magnification')
  SetIconOpacity(255);Hover(1);for i=1,12 do Update() end
  check(tooltipIndex==1 and options['Hit1.ToolTipHidden']=='1' and options['Icon1.ToolTipHidden']=='1' and options['Hit1.ToolTipText']=='' and options['Icon1.ToolTipText']=='','only the hovered icon owns the native capsule, without ANSI fallback tooltip text')
  failLabel=true;Hover(2);local label=assert(originalOpen(folder..'dock-label.txt','r'));local failedLabel=label:read('*a');label:close();Hover(2);label=assert(originalOpen(folder..'dock-label.txt','r'));local retriedLabel=label:read('*a');label:close()
  check(failedLabel:find('p1|',1,true)==1 and retriedLabel:find('p2|',1,true)==1,'a temporarily locked label file retries the same stable hover instead of caching the failed write');Hover(1)
  check(tonumber(options['Icon1.W'])>48 and tonumber(options['Icon1.Y'])<84-48 and tonumber(options['Icon1.Y'])+tonumber(options['Icon1.W'])==84,'hover magnifies upward from the shared bottom baseline')
  check(tonumber(options['Icon2.W'])>48,'neighboring icons participate in the magnification wave')
  Pointer(items[1].cx/tonumber(vars.DockWidth));for i=1,60 do Update() end
  check(tonumber(options['Icon1.W'])>=80,'wave crest reaches a clearly visible magnification')
  local center=waveX;Pointer((items[1].cx+cell*.2)/tonumber(vars.DockWidth));Update()
  check(math.abs(waveX-(items[1].cx+cell*.2))<.000001 and waveX>center,'the wave target follows the latest pointer without an additional smoothing delay')
  Pointer(items[2].drawX/tonumber(vars.DockWidth))
  check(hover==2 and tooltipIndex==2,'native pointer position updates the hovered application without a Rainmeter MouseOver event')
  for i=1,60 do Update() end
  check(sizes[2]>sizes[1] and waveX>items[1].cx,'a missed MouseOver cannot leave the magnification crest on the old application')
  local validPointer=pointerX;Pointer(2);Pointer(0/0)
  check(pointerX==validPointer and hover==2,'invalid native positions cannot change the current hit target')
  Pointer(-1)
  check(hover==0 and tooltipIndex==0,'native pointer exit clears a missed Rainmeter MouseLeave and its tooltip')
  for i=1,120 do Update() end
  check(tonumber(options['Icon1.W'])==48 and tonumber(options['Icon2.W'])==48,'a missed skin leave still settles magnification without another mouse event')
  Hover(1);Press(1);Pointer(-1);Release()
  check(pressIndex==0 and tapUntil[items[1].id]==nil,'leaving a pressed skin cannot leave a compressed icon or synthesize a click lift')
  local prior=sizes[1];Hover(2);Update();check(math.abs(sizes[1]-prior)<8,'reversing the wave continues from its current size')
  Hover(0);for i=1,120 do Update() end;check(tonumber(options['Icon1.W'])==48 and tonumber(options['Icon2.W'])==48,'leaving settles every icon to its original size')
  Hover(2);check(tooltipIndex==2 and options['Hit1.ToolTipHidden']=='1' and options['Hit2.ToolTipHidden']=='1','moving to another icon immediately clears the old tooltip')
  Press(2);check(options['Hit2.ToolTipHidden']=='1','pressing clears the tooltip before launch or drag')
  Hover(2);fakeTime=fakeTime+1;write('dock-state.txt','p1\tVGVzdA==\tVGVzdA==\t1\t0\t1\t0\n');Update()
  check(hover==0 and options['Hit2.ToolTipHidden']=='1' and options['Hit2.ToolTipText']=='','removing a hovered entry clears tooltip text and identity')
  Hover(1);for i=1,80 do Update() end;check(math.abs(items[1].drawX-tonumber(vars.DockWidth)/2)<1,'a single magnified icon remains centered')
  Hover(1);Hover(0);check(options['Hit1.ToolTipHidden']=='1','leaving the Dock clears the tooltip')
  fakeTime=fakeTime+1;write('dock-state.txt','p1\tVGVzdA==\tVGVzdA==\t1\t0\t1\t1\n');Update()
  check(tonumber(options['Icon1.Y'])<84-tonumber(options['Icon1.W']),'application attention keeps its bounce')
  fakeTime=fakeTime+4;write('dock-lyrics.txt',fakeTime+3 ..'|1|1\nYQ==\nYg==\n');Update()
  check(options['Icon1.ImageAlpha']=='255','orphaned reduced-motion lyric files cannot leave icons hidden')
  local rows={};for i=1,20 do rows[#rows+1]='p'..i..'\tVGVzdA==\tVGVzdA==\t1\t0\t'..(i<=10 and '1' or '0')..'\t0\n' end;write('dock-state.txt',table.concat(rows))
  local function sample(step,count)frameStep=step;Initialize();Update();Hover(10);Pointer(items[10].cx/tonumber(vars.DockWidth));for i=1,count do Update() end;return sizes[10],waveAmount end
  local fast,fastAmount=sample(.008,25);local slow,slowAmount=sample(.02,10)
  check(math.abs(fast-slow)<1.2 and math.abs(fastAmount-slowAmount)<.00001,'wave timing stays consistent at 125 and 50 updates per second')
  frameStep=.016;local startDivider=tonumber(options['Divider.X']);Pointer(items[11].cx/tonumber(vars.DockWidth));Hover(11);local clear=true
  for i=1,80 do Update();local x=tonumber(options['Divider.X']);clear=clear and x>items[10].drawX+sizes[10]/2 and x<items[11].drawX-sizes[11]/2 end
  check(clear and math.abs(startDivider-tonumber(options['Divider.X']))>1,'divider follows the breathing gap without crossing enlarged icons')
  local resting=sizes[11];Press(11);for i=1,8 do Update() end
  check(sizes[11]<resting*.97,'pointer down visibly compresses the icon before native launch acknowledgement')
  Release();check(tapUntil[items[11].id]>elapsed and tapUntil[items[11].id]<=elapsed+.43,'a completed click has one bounded release lift, without claiming application readiness')
  for i=1,40 do Update() end;check(math.abs(sizes[11]-resting)<.2,'pointer up springs the icon back without changing its native release command')
  check(tapUntil[items[11].id]<elapsed,'release lift expires instead of leaving a permanent animation')
  local writes=hoverWrites;for i=1,30 do Hover(11) end;check(hoverWrites==writes,'duplicate hit and image hover events do not rewrite the IPC file')
  local duplicateBefore=duplicateOptions;ReloadLayout();for i=1,10 do Update() end;check(duplicateOptions==duplicateBefore,'state reload and live motion never resend identical native meter options')
  Hover(0);for i=1,6 do Update() end;local position,velocity=waveAmount,waveAmountVelocity;Hover(11)
  check(waveAmount==position and waveAmountVelocity==velocity and velocity<0,'rapid reversal retains the current wave position and outgoing velocity')
  local savedSize,savedVelocity,savedX=sizes[11],sizeVelocity[11],items[11].drawX;ReloadLayout()
  check(sizes[11]==savedSize and sizeVelocity[11]==savedVelocity and items[11].drawX==savedX,'state refresh keeps the current icon position and spring velocity')
  Update();local wildcards=wildcardUpdates;for i=1,10 do Update() end
  check(wildcardUpdates==wildcards,'ordinary motion updates only changed meters rather than every hidden slot')
  local priorElapsed=elapsed;frameStep=-1;Update();check(elapsed>priorElapsed and sizes[11]==sizes[11],'clock overflow retains a finite animation cadence');frameStep=.016
  vars.DockFastFrames='1';Initialize();Update();Hover(1);local start=#fastCommands
  check(fastCommands[start]=='Execute 1','pointer feedback wakes the built-in ActionTimer');frameStep=.008
  local burstWrites=anchorWrites;for i=1,4 do FastFrame(1) end
  check(#fastCommands==start and anchorWrites==burstWrites,'a four-frame burst neither requeues itself nor writes animation anchors to disk')
  FastEnd(1);check(fastCommands[#fastCommands]=='Execute 2','only completed bursts alternate the worker action lists')
  local priorFrame=lastFrame;FastFrame(1);check(lastFrame==priorFrame,'late callbacks from an older burst are discarded')
  write('paused.txt','true');motionPoll=0;FastFrame(2);local stopped=#fastCommands;Pointer(.25)
  check(motionPaused and fastCommands[stopped]=='Stop 2' and #fastCommands==stopped,'pause stops both lists and pointer messages cannot restart fast motion')
  write('paused.txt','false');motionPoll=0;dragPoll=0;write('dock-drag-feedback-primary.txt',(fakeTime+3)..'|1|0|0');Update();local count=#fastCommands;Hover(2);Pointer(.5)
  check(dragActive and #fastCommands==count,'drag feedback keeps the ordinary state poll without restarting the fast animation loop')
  write('dock-drag-feedback-primary.txt','');write('paused.txt','false');vars.DockFastFrames='0';vars.SCREENAREAWIDTH='2560';vars.SCREENAREAHEIGHT='1440';vars.DockIconSize='44';frameStep=.016
  rows={};for i=1,27 do rows[#rows+1]='p'..i..'\tVGVzdA==\tVGVzdA==\t0\t0\t1\t0\n' end;write('dock-state.txt',table.concat(rows));Initialize();Update()
  for i=1,150 do Pointer(items[5].drawX/tonumber(vars.DockWidth));Update() end
  local enlarged=0;local baseline=true;for i,e in ipairs(items) do if sizes[i]>44.05 then enlarged=enlarged+1 end;baseline=baseline and e.drawY+math.floor(sizes[i]+.5)==84 end
  check(enlarged==5 and sizes[3]>44.05 and sizes[7]>44.05 and sizes[2]<44.05 and sizes[8]<44.05,'a settled visible-center hover affects exactly the center and two neighbors on each side')
  check(sizes[5]>sizes[4] and sizes[4]>sizes[3] and sizes[5]>sizes[6] and sizes[6]>sizes[7] and math.abs(sizes[4]-sizes[6])<.05,'the visible center is the largest and both neighbor pairs decay symmetrically')
  check(baseline and math.abs(items[5].drawX-items[5].cx)>5,'ordinary hover keeps every bottom aligned even when the visible centers have shifted from their base slots')
  local left,right=items[5].drawX,items[6].drawX;Pointer((left+(right-left)*.49)/tonumber(vars.DockWidth));for i=1,100 do Update() end
  local before={};for i=1,#items do before[i]=sizes[i] end;left,right=items[5].drawX,items[6].drawX;Pointer((left+(right-left)*.51)/tonumber(vars.DockWidth));Update();local continuous=true;for i=1,#items do continuous=continuous and math.abs(sizes[i]-before[i])<2 end
  check(continuous and hover==6,'crossing adjacent visual hit centers changes the selected icon without snapping any icon size')
  Pointer(-1);for i=1,140 do Update() end;local settled=true;for i=1,#items do settled=settled and math.abs(sizes[i]-44)<.01 and items[i].drawY==40 end
  check(settled,'all twenty-seven icons settle to the original size and baseline after leaving the five-icon wave')
  vars.DockFastFrames='1';Initialize();Update();SetIconOpacity(0);Pointer(items[5].drawX/tonumber(vars.DockWidth));local hiddenCommands=#fastCommands;SetIconOpacity(252)
  check(#fastCommands==hiddenCommands+1 and fastCommands[#fastCommands]=='Execute 1' and options['Icon5.ImageAlpha']=='252' and tooltipIndex==0,'visible icon fade wakes magnification before full opacity while preserving the applied alpha and hidden label')
  for i=1,30 do FastFrame(1) end
  check(sizes[5]>70 and waveX==pointerX and tooltipIndex==0,'almost opaque icons follow the received pointer without waiting for the lyric fade to reach alpha 255')
  local fadeSize,fadeVelocity,fadeX=sizes[5],sizeVelocity[5],items[5].drawX;SetIconOpacity(96)
  local continuousFade=sizes[5]==fadeSize and sizeVelocity[5]==fadeVelocity and items[5].drawX==fadeX
  Pointer(items[19].drawX/tonumber(vars.DockWidth));for i=1,8 do FastFrame(1) end
  fadeSize,fadeVelocity,fadeX=sizes[19],sizeVelocity[19],items[19].drawX;SetIconOpacity(180)
  check(continuousFade and sizes[19]==fadeSize and sizeVelocity[19]==fadeVelocity and items[19].drawX==fadeX,'reversing a visible fade preserves the current icon position and spring velocity')
  for i=1,30 do FastFrame(1) end
  check(sizes[19]>70 and sizes[5]<45 and waveX==pointerX and options['Icon19.ImageAlpha']=='180' and tooltipIndex==0,'magnification follows another icon through a fade-out and fade-in reversal')
  SetIconOpacity(0);local hiddenWave=waveX;hiddenCommands=#fastCommands;Pointer(items[9].drawX/tonumber(vars.DockWidth));for i=1,40 do Update() end
  check(fastCommands[hiddenCommands]=='Stop 2' and #fastCommands==hiddenCommands and waveX==hiddenWave and sizes[9]<44.01 and options['Icon9.ImageAlpha']=='0' and tooltipIndex==0,'fully hidden icons stay inactive after pointer delivery and do not restart the fast loop')
  SetIconOpacity(128);for i=1,30 do FastFrame(1) end
  check(sizes[9]>70 and waveX==pointerX and options['Icon9.ImageAlpha']=='128' and tooltipIndex==0,'reappearing icons resume the latest pointer without requiring a new hover event')
  if nativeSkin:GetVariable('TraceLayer')=='1' then
   vars.DockFastFrames='0';vars.DockIconSize='48';vars.SCREENAREAWIDTH='1050'
   local entries={};for i=1,9 do entries[i]='p'..i..'\tVGVzdA==\tVGVzdA==\t1\t0\t1\t0' end
   write('dock-state.txt',table.concat(entries,'\n')..'\n');Initialize();Update()
   local frames={};local optionUpdates=updates
   for frame=1,160 do
    local x=-1
    if frame<=35 then x=.5 elseif frame<=75 then x=.5+(frame-35)*.008 elseif frame<=100 then x=.82-(frame-75)*.021 end
    Pointer(x);Update()
    local boxes={}
    for i=1,#items do
     boxes[i]=string.format('[%d,%d,%d]',tonumber(options['Icon'..i..'.X']),tonumber(options['Icon'..i..'.Y']),tonumber(options['Icon'..i..'.W']))
    end
    frames[frame]='['..table.concat(boxes,',')..']'
   end
   write('layer-trace.json',string.format('{"width":%d,"meterUpdates":%d,"stepMs":16,"frames":[%s]}',tonumber(vars.DockWidth),updates-optionUpdates,table.concat(frames,',')))
  end
 end)
 if not ok then report:write('ERROR '..tostring(err)..'\n')else report:write('Completed '..checks..' native Lua checks.\n')end
 report:close();io.open=originalOpen;os.time=originalTime;os.clock=originalClock;SKIN=nativeSkin;Update=function()return 0 end
end
