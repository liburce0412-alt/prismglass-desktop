local locationBody=nil
local locationTick=0
function LoadLocation()
 local file=io.open(SKIN:GetVariable('LiquidRoot')..'weather-location.txt','r')
 if not file then return end
 local body=file:read(4097);file:close()
 if not body or #body>4096 or body==locationBody then return end
 local latitude,longitude,city=body:gsub('\r',''):match('^([^\n]+)\n([^\n]+)\n([^\n]+)\n*$')
 local a,b=tonumber(latitude),tonumber(longitude)
 if not a or not b or a~=a or b~=b or math.abs(a)>90 or math.abs(b)>180 or not city or city:find('[%c%[%]#"\\]') then return end
 locationBody=body
 SKIN:Bang('!SetVariable','WeatherCity',city)
 SKIN:Bang('!SetOption','City','Text',city)
 SKIN:Bang('!SetVariable','WeatherReady','0')
 SKIN:Bang('!SetOption','Feed','URL','https://api.open-meteo.com/v1/forecast?latitude='..tostring(a)..'&longitude='..tostring(b)..'&current=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min,sunrise,sunset&timezone=auto&forecast_days=1')
 SKIN:Bang('!CommandMeasure','Feed','Update')
end
function Initialize()
 locationBody=nil;locationTick=0
 local city=SKIN:GetMeter('City')
 SKIN:Bang('!SetVariable','WeatherCity',city and city:GetOption('Text') or '已配置位置')
 LoadLocation()
end
function Update()
 locationTick=locationTick+1
 if locationTick>=3 then locationTick=0;LoadLocation() end
 local m=SKIN:GetMeasure('Code')
 local c=m and tonumber(m:GetStringValue())
 if not c then return 0 end
 local s='天气变化'
 if c==0 then s='晴' elseif c<=3 then s='多云' elseif c<=48 then s='雾' elseif c<=67 then s='有雨' elseif c<=77 then s='有雪' elseif c<=82 then s='阵雨' elseif c<=86 then s='阵雪' else s='雷雨' end
 SKIN:Bang('!SetOption','Condition','Text',s)
 return c
end
