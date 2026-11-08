local locationBody=nil
local locationTick=0
local environmentState=nil
local function Environment(code)
 local ready=SKIN:GetVariable('WeatherReady','0')=='1'
 local day=SKIN:GetMeasure('Daylight')
 local night=day and tonumber(day:GetStringValue())==0
 local x,y=0,0
 if not ready or not code then
  if environmentState~='unavailable' then SKIN:Bang('!HideMeter','WeatherEnvironment');SKIN:Bang('!HideMeter','WeatherShade');environmentState='unavailable' end
  return
 end
 if code==0 then if night then x=627;y=836 end
 elseif code>=1 and code<=3 then x=627
 elseif code==45 or code==48 then y=836
 elseif (code>=71 and code<=77) or code==85 or code==86 then x=627;y=418
 elseif (code>=51 and code<=67) or (code>=80 and code<=82) or (code>=95 and code<=99) then y=418
 else if environmentState~='unavailable' then SKIN:Bang('!HideMeter','WeatherEnvironment');SKIN:Bang('!HideMeter','WeatherShade');environmentState='unavailable' end;return end
 local state=x..','..y..','..tostring(night)
 if state==environmentState then return end
 environmentState=state
 SKIN:Bang('!SetOption','WeatherEnvironment','ImageCrop',x..','..y..',627,418,1')
 SKIN:Bang('!SetOption','WeatherEnvironment','ImageTint',night and code~=0 and '105,132,180' or '255,255,255')
 SKIN:Bang('!ShowMeter','WeatherEnvironment');SKIN:Bang('!ShowMeter','WeatherShade')
 SKIN:Bang('!UpdateMeter','WeatherEnvironment');SKIN:Bang('!Redraw')
end
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
 Environment(nil)
 SKIN:Bang('!SetOption','Feed','URL','https://api.open-meteo.com/v1/forecast?latitude='..tostring(a)..'&longitude='..tostring(b)..'&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min,sunrise,sunset&timezone=auto&forecast_days=1')
 SKIN:Bang('!CommandMeasure','Feed','Update')
end
function Initialize()
 locationBody=nil;locationTick=0;environmentState=nil
 local city=SKIN:GetMeter('City')
 SKIN:Bang('!SetVariable','WeatherCity',city and city:GetOption('Text') or '已配置位置')
 LoadLocation()
end
function Update()
 locationTick=locationTick+1
 if locationTick>=3 then locationTick=0;LoadLocation() end
 local m=SKIN:GetMeasure('Code')
 local c=m and tonumber(m:GetStringValue())
 Environment(c)
 if not c then return 0 end
 local s='天气变化'
 if c==0 then s='晴' elseif c<=3 then s='多云' elseif c<=48 then s='雾' elseif c<=67 then s='有雨' elseif c<=77 then s='有雪' elseif c<=82 then s='阵雨' elseif c<=86 then s='阵雪' else s='雷雨' end
 SKIN:Bang('!SetOption','Condition','Text',s)
 return c
end
