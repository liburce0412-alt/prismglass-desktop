-- Exercise actual Rainmeter meters, including DynamicVariables refreshes.
function Initialize()
 folder=SKIN:GetVariable('LiquidRoot')
 tick=0;checks=0;failures=0
 report=assert(io.open(folder..'glass-checks.txt','w'))
end
local function heartbeat(age,theme)
 local f=assert(io.open(folder..'heartbeat.txt','w'))
 f:write(tostring(os.time()-age)..' '..theme);f:close()
end
local function check(value,name)
 checks=checks+1;if not value then failures=failures+1 end
 report:write((value and 'PASS ' or 'FAIL ')..name..'\n');report:flush()
end
function Update()
 tick=tick+1
 local rim=SKIN:GetMeter('Rim')
 if tick==3 then
  check(SKIN:GetMeasure('LiquidState'):GetValue()==1 and rim:GetW()==0,'fresh renderer hides the real fallback meter')
  SKIN:Bang('!UpdateMeter','*');SKIN:Bang('!Redraw')
 elseif tick==5 then
  check(rim:GetW()==0,'dynamic meter refresh keeps the fallback hidden')
  heartbeat(20,'Sky')
 elseif tick==8 then
  check(SKIN:GetMeasure('LiquidState'):GetValue()==0 and rim:GetW()>0,'expired renderer exposes fallback instead of a blank Dock')
  heartbeat(0,'Sky')
 elseif tick==11 then
  check(rim:GetW()==0 and SKIN:GetVariable('Ink')=='255,255,255','recovered renderer restores transparent glass and white text')
  heartbeat(0,'Astro')
 elseif tick==14 then
  check(rim:GetW()==0 and SKIN:GetVariable('Blue')=='119,184,255','theme change keeps the fallback hidden')
  report:write('Completed '..checks..' native glass checks; failures='..failures..'.\n');report:close()
 end
 return tick
end
