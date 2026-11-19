-- Fixture observer: every production bang is executed on the real Rainmeter skin.
local native
local updates,redraws=0,0
local productionUpdate,productionOpacity,productionPointer
local productionFast,fastUpdates= nil,0
local sweepStart,sweepRows
local externalStart,externalInputs,externalFrames
function BeginSweep()sweepStart=os.clock();sweepRows={} end
function BeginExternalSweep()externalStart=os.clock();externalInputs={};externalFrames={} end
function EndExternalSweep()
 for name,rows in pairs({inputs=externalInputs,frames=externalFrames}) do local f=assert(io.open(native:GetVariable('LiquidRoot')..'external-'..name..'.csv','w'));f:write(table.concat(rows,'\n'));f:close() end
 externalStart=nil
end
function Pointer(fraction)
 local result=productionPointer(fraction)
 if externalStart then externalInputs[#externalInputs+1]=string.format('%.6f,%.12f,%d',os.clock()-externalStart,fraction,hover) end
 return result
end
function CadenceSnapshot()
 local f=assert(io.open(native:GetVariable('LiquidRoot')..'cadence.txt','w'));f:write(fastUpdates);f:close()
end
function PointerSnapshot()
 local f=assert(io.open(native:GetVariable('LiquidRoot')..'pointer.txt','w'));f:write(tostring(pointerX and pointerX/tonumber(native:GetVariable('DockWidth')) or -1)..'|'..hover);f:close()
end
function IconFrameSnapshot()
 local rows={}
 for i,e in ipairs(items) do local meter=native:GetMeter('Icon'..i);rows[#rows+1]=e.id..'|'..meter:GetOption('ImageName')..'|'..meter:GetW()..'|'..meter:GetH() end
 local f=assert(io.open(native:GetVariable('LiquidRoot')..'icon-frames.txt','w'));f:write(table.concat(rows,'\n'));f:close()
end
function FocusIcon(index)Pointer((items[index].drawX or items[index].cx)/tonumber(native:GetVariable('DockWidth'))) end
function GeometrySnapshot()
 local rows={}
 for i,e in ipairs(items) do local meter=native:GetMeter('Icon'..i);rows[#rows+1]=table.concat({e.id,meter:GetX(),meter:GetY(),meter:GetW(),meter:GetH()},'|') end
 local f=assert(io.open(native:GetVariable('LiquidRoot')..'geometry.txt','w'));f:write(table.concat(rows,'\n'));f:close()
end
function FastFrame(index)
 local before=lastFrame;productionFast(index);if lastFrame~=before then fastUpdates=fastUpdates+1 end
end
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
 local previous=lastFrame
 local result=productionUpdate()
 if externalStart and lastFrame~=previous then
  local peak,center=0,0
  for i=1,#items do local meter=native:GetMeter('Icon'..i);if meter:GetW()>peak then peak=meter:GetW();center=meter:GetX()+peak/2 end end
  externalFrames[#externalFrames+1]=string.format('%.6f,%.6f,%.6f,%.3f,%d,%d,%s',os.clock()-externalStart,pointerX or -1,waveX or -1,center,peak,hover,native:GetVariable('DockWidth'))
 end
 if t and lastFrame~=previous then
  local peak,covered,right=0,true,0
  for i,e in ipairs(items) do
   local meter,hit=native:GetMeter('Icon'..i),native:GetMeter('Hit'..i)
   peak=math.max(peak,meter:GetW())
   covered=covered and math.abs(hit:GetX()-right)<=2 and hit:GetH()==96 and e.drawX>=hit:GetX() and e.drawX<=hit:GetX()+hit:GetW()
   right=hit:GetX()+hit:GetW()
  end
  covered=covered and math.abs(right-tonumber(native:GetVariable('DockWidth')))<=2
  sweepRows[#sweepRows+1]=string.format('%.4f,%.3f,%s,%d',t,waveX or 0,peak,covered and 1 or 0)
  if t>=4.8 then local f=assert(io.open(native:GetVariable('LiquidRoot')..'sweep.csv','w'));f:write(table.concat(sweepRows,'\n'));f:close();sweepStart=nil end
 elseif not t then snapshot('live') end
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
 local fixtureUpdate,fixtureOpacity,fixtureFast,fixturePointer=Update,SetIconOpacity,FastFrame,Pointer
 dofile(native:GetVariable('ProductionScript'))
 local productionInitialize=Initialize
 productionUpdate,productionOpacity,productionFast,productionPointer=Update,SetIconOpacity,FastFrame,Pointer
 Update,SetIconOpacity,FastFrame,Pointer=fixtureUpdate,fixtureOpacity,fixtureFast,fixturePointer
 productionInitialize()
end
