-- Fixture observer: every production bang is executed on the real Rainmeter skin.
local native
local updates,redraws=0,0
local productionUpdate,productionOpacity
local function snapshot(name)
 local meter=native:GetMeter('Icon1')
 if not meter then return end
 local f=assert(io.open(native:GetVariable('LiquidRoot')..name..'.txt','w'))
 f:write(table.concat({os.time(),tonumber(meter:GetOption('ImageAlpha','255')),iconOpacity or -1,iconLease or -1,updates,redraws,meter:GetW(),meter:GetH()},'|'))
 f:close()
end
function Update()
 if not productionUpdate then return 0 end
 local result=productionUpdate()
 snapshot('live')
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
