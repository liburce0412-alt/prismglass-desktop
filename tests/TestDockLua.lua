-- Run in a separate off-screen Rainmeter instance; never sends bangs to real skins.
function Initialize()
 local nativeSkin=SKIN
 local folder=nativeSkin:GetVariable('Fixture')
 local script=nativeSkin:GetVariable('ProductionScript')
 local report=io.open(folder..'lua-checks.txt','w')
 local checks=0
 local function check(value,name)
  report:write((value and 'PASS ' or 'FAIL ')..name..'\n');report:flush()
  if not value then error(name) end;checks=checks+1
 end
 local function write(file,value)local f=assert(io.open(folder..file,'w'));f:write(value);f:close()end
 local options={}
 local vars={LiquidRoot=folder,Ink='255,255,255',Muted='227,237,249',SCREENAREAWIDTH='1920',SCREENAREAHEIGHT='1080'}
 SKIN={GetVariable=function(_,k)return vars[k]end,Bang=function(_,command,meter,key,value)
  if command=='!SetOption' then options[meter..'.'..key]=value
  elseif command=='!SetVariable' then vars[meter]=key end
 end}
 local originalTime=os.time
 local fakeTime=2000000000
 os.time=function()return fakeTime end
 local ok,err=pcall(function()
  dofile(script)
  Initialize()
  write('dock-state.txt','p1\tVGVzdA==\tVGVzdA==\t1\t0\t1\t0\n')
  write('dock-lyrics.txt',fakeTime+3 ..'|1|0\n56ys5LiA5Y+l\nTmV4dA==\n')
  for i=1,80 do Update() end
  check(options['LyricCurrent.Text']==nil and options['LyricNext.Text']==nil,'Unicode lyrics are rendered by the glass WebView, not legacy string meters')
  check(options['Icon1.ImageAlpha']=='0','icons fade away while glass lyrics show')
  local originalOpen=io.open
  io.open=function(path,mode)if path==folder..'dock-lyrics.txt' then return nil end;return originalOpen(path,mode)end
  for i=1,18 do Update() end
  check(options['Icon1.ImageAlpha']=='0','brief file contention does not flash application icons')
  io.open=originalOpen
  Approach();Update()
  check(options['Icon1.ImageAlpha']=='255','pointer approach restores icons on next frame')
  for i=1,20 do Update() end
  check(options['Icon1.ImageAlpha']=='255','leave hysteresis prevents flicker')
  for i=1,80 do Update() end
  check(options['Icon1.ImageAlpha']=='0','lyrics return after quiet delay')
  Hover(1);for i=1,12 do Update() end
  check(tonumber(options['Icon1.W'])>48 and tonumber(options['Icon1.Y'])<84-tonumber(options['Icon1.W']),'hover enlarges and bounces the icon after leaving lyrics')
  for i=1,68 do Update() end
  check(options['Icon1.ImageAlpha']=='255','icon hover keeps apps visible')
  Hover(0);for i=1,100 do Update() end
  fakeTime=fakeTime+5;for i=1,60 do Update() end
  check(options['Icon1.ImageAlpha']=='255','expired native heartbeat restores icons')
  write('dock-lyrics.txt','malformed\n');for i=1,10 do Update() end
  check(options['Icon1.ImageAlpha']=='255','invalid input never hides applications')
  write('dock-lyrics.txt',fakeTime+3 ..'|1|1\n56ys5LiA5Y+l\nTmV4dA==\n')
  for i=1,7 do Update() end
  check(options['Icon1.ImageAlpha']=='0','reduced motion changes state without fade')
  write('dock-lyrics.txt',fakeTime+3 ..'|0|1\n\n\n')
  for i=1,7 do Update() end
  check(options['Icon1.ImageAlpha']=='255','paused output returns application icons')
  fakeTime=fakeTime+1
  write('dock-state.txt','p1\tVGVzdA==\tVGVzdA==\t1\t0\t1\t1\n')
  Update()
  check(tonumber(options['Icon1.Y'])<84-tonumber(options['Icon1.W']),'application attention still bounces the icon')
 end)
 if not ok then report:write('ERROR '..tostring(err)..'\n')else report:write('Completed '..checks..' native Lua checks.\n')end
 report:close();os.time=originalTime;SKIN=nativeSkin
 Update=function()return 0 end
end
