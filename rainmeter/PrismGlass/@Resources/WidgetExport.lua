local previousBody=nil
local lastWrite=0
local weatherUpdatedAt=0
function Initialize()
 name=SKIN:GetVariable('CURRENTCONFIG'):match('([^\\]+)$')
 path=SKIN:GetVariable('LiquidRoot')..'widget-'..name..'.json'
 previousBody=nil;lastWrite=0;weatherUpdatedAt=0
end
function MarkWeatherUpdated()
 -- This is called by WebParser FinishAction, not by the four-second heartbeat.
 weatherUpdatedAt=os.time()*1000
end
function value(name)
 local m=SKIN:GetMeasure(name)
 return m and tonumber(m:GetValue()) or 0
end
function safeText(name)
 local m=SKIN:GetMeasure(name);return m and m:GetStringValue():gsub('["\\]','') or ''
end
function Update()
 local body
 if name=='Weather' then
  body=string.format('{"ready":%s,"updatedAt":%.0f,"temp":%s,"code":%s,"high":%s,"low":%s,"sunrise":"%s","sunset":"%s"}',SKIN:GetVariable('WeatherReady','0')=='1' and 'true' or 'false',weatherUpdatedAt,value('Temp'),value('Code'),value('High'),value('Low'),safeText('Sunrise'),safeText('Sunset'))
 elseif name=='System' then
  local m=SKIN:GetMeasure('RAM');local max=m and m:GetMaxValue() or 0
  body=string.format('{"cpu":%s,"ram":%s,"down":%s,"up":%s}',value('CPU'),max>0 and value('RAM')/max*100 or 0,value('Down'),value('Up'))
 elseif name=='Music' then body=string.format('{"value":%s}',value('Volume'))
 end
 if body and (body~=previousBody or os.time()-lastWrite>=4) then
  local f=io.open(path,'w');if f then f:write(body);f:close();previousBody=body;lastWrite=os.time() end
 end
 return 0
end
