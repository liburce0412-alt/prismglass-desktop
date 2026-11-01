-- Fixture observer: every production bang is executed on the real Rainmeter skin.
local native
local updates,redraws=0,0
local productionUpdate,productionOpacity
local sweepStart,sweepRows
function BeginSweep()sweepStart=os.clock();sweepRows={} end
local function snapshot(name)
 local meter=native:GetMeter('Icon1')
 if not meter then return end
 local f=assert(io.open(native:GetVariable('LiquidRoot')..name..'.txt','w'))
 f:write(table.concat({os.time(),tonumber(meter:GetOption('ImageAlpha','255')),iconOpacity or -1,iconLease or -1,updates,redraws,meter:GetW(),meter:GetH()},'|'))
 f:close()
end
function Update()
 if not productionUpdate then return 0 end
 local t=sweepStart and os.clock()-sweepStart
 if t then
  local phase=math.min(t,4)/2;local fraction=.08+.84*(phase<=1 and phase or 2-phase)
  Pointer(t<4 and fraction or -1)
  local index=0;local distance=math.huge
  if t<4 then for i,e in ipairs(items) do local d=math.abs((e.drawX or e.cx)-fraction*tonumber(native:GetVariable('DockWidth')));if d<distance then index=i;distance=d end end end
  if hover~=index then Hover(index) end
 end
 local result=productionUpdate()
 if t then
  local peak=0;for i=1,#items do peak=math.max(peak,native:GetMeter('Icon'..i):GetW()) end
  sweepRows[#sweepRows+1]=string.format('%.4f,%.3f,%s',t,waveX or 0,peak)
  if t>=4.8 then local f=assert(io.open(native:GetVariable('LiquidRoot')..'sweep.csv','w'));f:write(table.concat(sweepRows,'\n'));f:close();sweepStart=nil end
 else snapshot('live') end
 return result
end
function SetIconOpacity(alpha)
 updates,redraws=0,0
 local result=productionOpacity(alpha)
 snapshot('ack')
 return result
end
function Initialize()
 native=SKIN
 SKIN={
  GetVariable=function(_,...) return native:GetVariable(...) end,
  Bang=function(_,command,...)
   native:Bang(command,...)
   if command=='!UpdateMeter' then updates=updates+1 end
   if command=='!Redraw' then redraws=redraws+1 end
  end
 }
 local fixtureUpdate,fixtureOpacity=Update,SetIconOpacity
 dofile(native:GetVariable('ProductionScript'))
 local productionInitialize=Initialize
 productionUpdate,productionOpacity=Update,SetIconOpacity
 Update,SetIconOpacity=fixtureUpdate,fixtureOpacity
 productionInitialize()
end
