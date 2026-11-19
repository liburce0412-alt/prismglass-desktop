local locationBody=nil
local locationTick=0
local environmentState=nil
local sharedBody=nil
local function Environment(code,night)
 local ready=SKIN:GetVariable('WeatherReady','0')=='1'
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
 sharedBody=nil
end
function Initialize()
 locationBody=nil;locationTick=0;environmentState=nil;sharedBody=nil
 local city=SKIN:GetMeter('City')
 SKIN:Bang('!SetVariable','WeatherCity',city and city:GetOption('Text') or '已配置位置')
 LoadLocation()
end
function Update()
 locationTick=locationTick+1
 if locationTick>=3 then locationTick=0;LoadLocation() end
 local file=io.open(SKIN:GetVariable('LiquidRoot')..'widget-work-context.txt','rb')
 local body=file and file:read(16385) or '';if file then file:close() end
 local data={};if #body<=16384 then for key,value in body:gmatch('([%a]+)=([^\r\n]*)')do data[key]=value end end
 local now=os.time();local stamp=tonumber(data.stamp);local at=tonumber(data.weatherSharedUpdatedAt);local temp=tonumber(data.weatherSharedTemp)
 local alive=data.version=='1' and stamp and now-stamp<=12 and stamp<=now+2
 local cityMatches=not locationBody or data.weatherSharedLocation==SKIN:GetVariable('WeatherCity')
 local ready=alive and cityMatches and data.weatherReady=='1' and temp and at and at<=now*1000+60000 and now*1000-at<=7200000
 local signature=body..tostring(ready);if signature==sharedBody then return tonumber(data.weatherSharedCode) or 0 end;sharedBody=signature
 SKIN:Bang('!SetVariable','WeatherReady',ready and '1' or '0')
 if not ready then
  Environment(nil);SKIN:Bang('!HideMeterGroup','Data');SKIN:Bang('!ShowMeter','WeatherStatus')
  SKIN:Bang('!SetOption','WeatherStatus','Text',alive and cityMatches and data.weatherSharedReason~='' and data.weatherSharedReason or '正在等待电脑天气更新…');SKIN:Bang('!UpdateMeter','WeatherStatus');SKIN:Bang('!Redraw');return 0
 end
 local c=tonumber(data.weatherSharedCode);local s='天气变化'
 if c then if c==0 then s='晴' elseif c<=3 then s='多云' elseif c<=48 then s='雾' elseif c<=67 then s='有雨' elseif c<=77 then s='有雪' elseif c<=82 then s='阵雨' elseif c<=86 then s='阵雪' else s='雷雨' end end
 local stale=data.weatherStale=='1';local offset=tonumber(data.weatherSharedUtcOffsetSeconds) or 0
 local clock=os.date('!*t',now+offset);local minute=clock.hour*60+clock.min
 local function minutes(value)local h,m=(value or ''):match('T(%d%d):(%d%d)');return h and tonumber(h)*60+tonumber(m) end
 local rise,set=minutes(data.weatherSharedSunrise),minutes(data.weatherSharedSunset)
 Environment(c,rise and set and (minute<rise or minute>=set))
 local function degrees(value)local number=tonumber(value);return number and string.format('%.0f°',number) or '—' end
 SKIN:Bang('!SetOption','City','Text',data.weatherSharedLocation or '')
 SKIN:Bang('!SetOption','Temperature','Text',degrees(temp));SKIN:Bang('!SetOption','CompactSummary','Text',degrees(temp))
 SKIN:Bang('!SetOption','Condition','Text',s..(stale and ' · 上次数据' or ''))
 SKIN:Bang('!SetOption','Range','Text','高 '..degrees(data.weatherSharedHigh)..'  低 '..degrees(data.weatherSharedLow))
 SKIN:Bang('!SetOption','Temperature','ToolTipText',(data.weatherSharedSource or '')..' · '..os.date('!%H:%M',math.floor(at/1000)+offset)..' 数据'..(stale and ' · '..(data.weatherSharedReason or '') or ''))
 SKIN:Bang('!ShowMeterGroup','Data');SKIN:Bang('!HideMeter','WeatherStatus');SKIN:Bang('!UpdateMeter','*');SKIN:Bang('!Redraw');return c or 0
end
