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
 local updates,redraws=0,0
 local vars={LiquidRoot=folder,Ink='255,255,255',Muted='227,237,249',SCREENAREAWIDTH='1920',SCREENAREAHEIGHT='1080'}
 SKIN={GetVariable=function(_,k)return vars[k]end,Bang=function(_,command,meter,key,value)
  if command=='!SetOption' then options[meter..'.'..key]=value elseif command=='!SetVariable' then vars[meter]=key
  elseif command=='!ShowMeter' then shown[meter]=true elseif command=='!HideMeter' then shown[meter]=false
  elseif command=='!UpdateMeter' then updates=updates+1 elseif command=='!Redraw' then redraws=redraws+1 end
 end}
 local originalTime=os.time;local fakeTime=2000000000;os.time=function()return fakeTime end
 local ok,err=pcall(function()
  dofile(script);Initialize()
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
  SetIconOpacity(255);Hover(1);for i=1,12 do Update() end
  check(options['Hit1.ToolTipHidden']=='0' and options['Icon1.ToolTipHidden']=='1' and options['Icon1.ToolTipText']=='','only the hovered hit meter owns a tooltip')
  check(tonumber(options['Icon1.W'])>48 and tonumber(options['Icon1.Y'])<84-tonumber(options['Icon1.W']),'hover still enlarges and bounces the visible icon')
  Hover(2);check(options['Hit1.ToolTipHidden']=='1' and options['Hit2.ToolTipHidden']=='0','moving to another icon immediately clears the old tooltip')
  Press(2);check(options['Hit2.ToolTipHidden']=='1','pressing clears the tooltip before launch or drag')
  Hover(2);fakeTime=fakeTime+1;write('dock-state.txt','p1\tVGVzdA==\tVGVzdA==\t1\t0\t1\t0\n');Update()
  check(hover==0 and options['Hit2.ToolTipHidden']=='1' and options['Hit2.ToolTipText']=='','removing a hovered entry clears tooltip text and identity')
  Hover(1);Hover(0);check(options['Hit1.ToolTipHidden']=='1','leaving the Dock clears the tooltip')
  fakeTime=fakeTime+1;write('dock-state.txt','p1\tVGVzdA==\tVGVzdA==\t1\t0\t1\t1\n');Update()
  check(tonumber(options['Icon1.Y'])<84-tonumber(options['Icon1.W']),'application attention keeps its bounce')
  fakeTime=fakeTime+4;write('dock-lyrics.txt',fakeTime+3 ..'|1|1\nYQ==\nYg==\n');Update()
  check(options['Icon1.ImageAlpha']=='255','orphaned reduced-motion lyric files cannot leave icons hidden')
 end)
 if not ok then report:write('ERROR '..tostring(err)..'\n')else report:write('Completed '..checks..' native Lua checks.\n')end
 report:close();os.time=originalTime;SKIN=nativeSkin;Update=function()return 0 end
end
