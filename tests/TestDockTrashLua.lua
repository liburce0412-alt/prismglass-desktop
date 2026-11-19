-- Isolated Rainmeter interpreter; no changes to the user's Dock or Recycle Bin.
function Initialize()
 local nativeSkin=SKIN;local folder=nativeSkin:GetVariable('Fixture');local script=nativeSkin:GetVariable('ProductionScript');local report=assert(io.open(folder..'lua-checks.txt','w'));local checks=0
 local function check(value,name)report:write((value and 'PASS ' or 'FAIL ')..name..'\n');report:flush();if not value then error(name) end;checks=checks+1 end
 local function write(value)local f=assert(io.open(folder..'dock-state.txt','w'));f:write(value);f:close()end
 local options,shown={},{};local imageChanges=0;local vars={LiquidRoot=folder,Ink='255,255,255',Muted='227,237,249',SCREENAREAWIDTH='1920',SCREENAREAHEIGHT='1080'};vars['@']=script:gsub('Dock%.lua$','')
 SKIN={GetVariable=function(_,k)return vars[k]end,Bang=function(_,command,meter,key,value)if command=='!SetOption' then options[meter..'.'..key]=value;if key=='ImageName' then imageChanges=imageChanges+1 end elseif command=='!SetVariable' then vars[meter]=key elseif command=='!ShowMeter' then shown[meter]=true elseif command=='!HideMeter' then shown[meter]=false end end}
 local originalTime,originalClock=os.time,os.clock;local now,clock=2000000000,0;os.time=function()return now end;os.clock=function()clock=clock+.016;return clock end
 local ok,err=pcall(function()
  local row='\tVGVzdA==\tVGVzdA==\t0\t0\t';local pins='workspace'..row..'1\t0\np1'..row..'1\t0\n';local running='r1'..row..'0\t0\n';local trash='trash'..row..'0\t0\n'
  write(pins..running..trash);dofile(script);Initialize();Update();check(#items==4 and items[4].id=='trash','trash is the final visual entry');check(divider==3 and trashDivider==4 and shown.TrashDivider,'running apps and trash have separate breathing gaps');check(options['Hit4.RightMouseUpAction']=='' and options['Icon4.RightMouseUpAction']=='','trash has no destructive or pinning context action')
  Hover(4);for i=1,70 do Update()end;local x=tonumber(options['TrashDivider.X']);check(x>items[3].drawX+sizes[3]/2 and x<items[4].drawX-sizes[4]/2,'trash divider stays between magnified icons')
  now=now+1;write(pins..trash);ReloadLayout();Update();check(divider==3 and not trashDivider and not shown.TrashDivider,'all-pinned layout uses one divider before trash')
  now=now+1;write(pins..'trash\tVGVzdA==\tRnVsbA==\t0\t0\t0\t0\n');ReloadLayout();Update();check(options['Icon3.ImageName']=='Full' and items[3].id=='trash','a changed empty/full icon refreshes the same fixed slot')
  write(pins..'trash\tVGVzdA==\tdHJhc2gtZW1wdHkucG5n\t0\t0\t0\t0\n');ReloadLayout();Update()
  check(options['Icon1.ImageName']=='Test' and options['Icon3.ImageName']=='trash-empty.png','workspace and empty trash retain the original backend image instead of a replacement family')
  check(options['Icon2.ImageName']=='Test','application branding keeps its original native image')
  Hover(3);for i=1,8 do Update() end;local size=sizes[3]
  check(size>48 and options['Icon3.ImageName']=='trash-empty.png','hover magnifies the original empty trash image')
  Hover(0);Update();check(math.abs(sizes[3]-size)<8 and sizes[3]>48,'leaving continues from the current spring pose without jumping to rest')
  local paused=assert(io.open(folder..'paused.txt','w'));paused:write('true');paused:close();motionPoll=0;Update()
  check(motionPaused and sizes[3]==48 and options['Icon3.ImageName']=='trash-empty.png','pause restores original static size without replacing the artwork')
  paused=assert(io.open(folder..'paused.txt','w'));paused:write('false');paused:close();motionPoll=0;Hover(3);for i=1,40 do Update() end
  check(sizes[3]>80 and options['Icon3.ImageName']=='trash-empty.png','resumed hover restores the original image magnification')
  Hover(0);for i=1,40 do Update() end
  write(pins..'trash\tVGVzdA==\tdHJhc2gtZnVsbC5wbmc=\t0\t0\t0\t0\n');ReloadLayout();Update()
  check(items[3].id=='trash' and options['Icon3.ImageName']=='trash-full.png','full recycle-bin state shows the backend full image in the same slot')
  Hover(1);for i=1,40 do Update() end;local original=sizes[1];Press(1);for i=1,8 do Update() end;local pressed=sizes[1];Release();Update()
  check(pressed<original*.97 and tapUntil.workspace>elapsed and options['Icon1.ImageName']=='Test','original application-center artwork keeps the press and bounded release feedback')
  for i=1,90 do Update() end;local settled=imageChanges;for i=1,30 do Update() end
  check(imageChanges==settled and tapUntil.workspace<elapsed,'settled hover stops release feedback and never reloads unchanged artwork')
  Hover(0);for i=1,80 do Update() end
  check(sizes[1]==48 and sizes[3]==48 and options['Icon3.ImageName']=='trash-full.png','leaving restores the configured size for both original functional images')
 end)
 if not ok then report:write('ERROR '..tostring(err)..'\n')else report:write('Completed '..checks..' native Trash Lua checks.\n')end;report:close();os.time=originalTime;os.clock=originalClock;SKIN=nativeSkin;Update=function()return 0 end
end
