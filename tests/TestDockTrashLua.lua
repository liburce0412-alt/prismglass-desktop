-- Isolated Rainmeter interpreter; no changes to the user's Dock or Recycle Bin.
function Initialize()
 local nativeSkin=SKIN;local folder=nativeSkin:GetVariable('Fixture');local script=nativeSkin:GetVariable('ProductionScript');local report=assert(io.open(folder..'lua-checks.txt','w'));local checks=0
 local function check(value,name)report:write((value and 'PASS ' or 'FAIL ')..name..'\n');report:flush();if not value then error(name) end;checks=checks+1 end
 local function write(value)local f=assert(io.open(folder..'dock-state.txt','w'));f:write(value);f:close()end
 local options,shown={},{};local vars={LiquidRoot=folder,Ink='255,255,255',Muted='227,237,249',SCREENAREAWIDTH='1920',SCREENAREAHEIGHT='1080'}
 SKIN={GetVariable=function(_,k)return vars[k]end,Bang=function(_,command,meter,key,value)if command=='!SetOption' then options[meter..'.'..key]=value elseif command=='!SetVariable' then vars[meter]=key elseif command=='!ShowMeter' then shown[meter]=true elseif command=='!HideMeter' then shown[meter]=false end end}
 local originalTime,originalClock=os.time,os.clock;local now,clock=2000000000,0;os.time=function()return now end;os.clock=function()clock=clock+.016;return clock end
 local ok,err=pcall(function()
  local row='\tVGVzdA==\tVGVzdA==\t0\t0\t';local pins='workspace'..row..'1\t0\np1'..row..'1\t0\n';local running='r1'..row..'0\t0\n';local trash='trash'..row..'0\t0\n'
  write(pins..running..trash);dofile(script);Initialize();Update();check(#items==4 and items[4].id=='trash','trash is the final visual entry');check(divider==3 and trashDivider==4 and shown.TrashDivider,'running apps and trash have separate breathing gaps');check(options['Hit4.RightMouseUpAction']=='' and options['Icon4.RightMouseUpAction']=='','trash has no destructive or pinning context action')
  Hover(4);for i=1,70 do Update()end;local x=tonumber(options['TrashDivider.X']);check(x>items[3].drawX+sizes[3]/2 and x<items[4].drawX-sizes[4]/2,'trash divider stays between magnified icons')
  now=now+1;write(pins..trash);ReloadLayout();Update();check(divider==3 and not trashDivider and not shown.TrashDivider,'all-pinned layout uses one divider before trash')
  now=now+1;write(pins..'trash\tVGVzdA==\tRnVsbA==\t0\t0\t0\t0\n');ReloadLayout();Update();check(options['Icon3.ImageName']=='Full' and items[3].id=='trash','a changed empty/full icon refreshes the same fixed slot')
 end)
 if not ok then report:write('ERROR '..tostring(err)..'\n')else report:write('Completed '..checks..' native Trash Lua checks.\n')end;report:close();os.time=originalTime;os.clock=originalClock;SKIN=nativeSkin;Update=function()return 0 end
end
