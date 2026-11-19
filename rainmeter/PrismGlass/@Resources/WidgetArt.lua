local root,kind,choice,remaining,running,deadline,lastSecond,lastTick,pulse,paused,divider,shapeCount
local duration,restElapsed,restStarted,restRunning,context,contextKey
local optionCache,dirtyMeters,secondTick,leafSettle,taskSignature
local colors={'169,218,255','255,211,166','179,235,205'}
-- Retained Shape layers: JaxCore ModularClocks separates static geometry from
-- clock-driven layers. Keep that update pattern without its Core dependency.
local function option(meter,key,value)
 value=tostring(value);local id=meter..'.'..key
 if optionCache[id]~=value then optionCache[id]=value;SKIN:Bang('!SetOption',meter,key,value);if meter~='Ambient' then dirtyMeters[meter]=true end end
end
local function read(path,limit)local f=io.open(path,'rb');if not f then return '' end;local s=f:read(limit+1) or '';f:close();return #s<=limit and s or '' end
local function save()
 local f=io.open(root..'widget-'..kind..'.state','w');if f then f:write(choice..' '..math.floor(remaining)..' '..(running and deadline or 0)..' '..duration);f:close()end
end
local function ellipse(x,y,rx,ry,color,alpha)return string.format('Ellipse %.2f,%.2f,%.2f,%.2f | Fill Color %s,%d | StrokeWidth 0',x,y,rx,ry,color,alpha or 255)end
local function line(x,y,a,b,color,width)return string.format('Line %.2f,%.2f,%.2f,%.2f | Stroke Color %s | StrokeWidth %.2f | StrokeStartCap Round | StrokeEndCap Round',x,y,a,b,color,width or 2)end
function Daylight(now,text,location)
 -- The shared cache is written by WeatherData; do not request location or weather again.
 if #text>131072 then return nil end
 local fetched=tonumber(text:match('"fetchedAt"%s*:%s*(%d+)'))
 if location and location~='' then
  local lat,lon=location:match('^([^\r\n]+)[\r\n]+([^\r\n]+)');local cachedLat,cachedLon=text:match('"locationKey"%s*:%s*"([^\\]+)\\n([^\\]+)\\n')
  if not tonumber(lat) or not tonumber(lon) or tonumber(lat)~=tonumber(cachedLat) or tonumber(lon)~=tonumber(cachedLon) then return nil end
 end
 text=text:gsub('\\"','"')
 local rise=tonumber(text:match('"sunrise"%s*:%s*%[%s*(%d+)'))
 local set=tonumber(text:match('"sunset"%s*:%s*%[%s*(%d+)'))
 local offset=tonumber(text:match('"utc_offset_seconds"%s*:%s*([%-]?%d+)'))
 if not fetched or fetched>now*1000+60000 or now*1000-fetched>7200000 or not rise or not set or set<=rise or set-rise>86400 or not offset or math.abs(offset)>50400 then return nil end
 if math.floor((rise+offset)/86400)~=math.floor((now+offset)/86400) then return nil end
 return math.max(0,math.min(1,(now-rise)/(set-rise))),rise,set,offset
end
function YearProgress(now)
 local date=os.date('*t',now);local leap=date.year%4==0 and (date.year%100~=0 or date.year%400==0)
 return date.yday,leap and 366 or 365
end
local function request(file,value)local f=io.open(root..file,'w');if f then f:write(value);f:close()end end
local function refreshContext(now)
 local text=read(root..'widget-work-context.txt',16384);local data={}
 for key,value in text:gmatch('([%a]+)=([^\r\n]*)')do data[key]=value end
 local stamp=tonumber(data.stamp);if data.version~='1' or not stamp or now-stamp>12 or stamp>now+2 then data={} end
 local changed=text~=contextKey or (context and context.version~=data.version);contextKey=text;context=data;return changed
end
local function clock(seconds)seconds=math.max(0,math.ceil(seconds));return string.format('%02d:%02d',math.floor(seconds/60),seconds%60)end
local function restTime()return math.min(60,restElapsed+(restRunning and math.max(0,os.time()-restStarted) or 0))end
local function focusTime()if context.focusAvailable~='1' then return nil end;local endAt=tonumber(context.focusDeadline) or 0;return math.max(0,endAt>0 and endAt-os.time() or tonumber(context.focusRemaining) or 0)end
local function subsecond()return math.max(0,math.min(.999,os.clock()-secondTick))end
local function taskProgress()
 local total=math.max(0,tonumber(context.taskTotal) or 0);local pending=math.max(0,math.min(total,tonumber(context.taskPending) or 0))
 return total,total-pending,context.tasksAvailable=='1'
end
local function dateDistance(value)
 local y,m,d=value:match('^(%d%d%d%d)%-(%d%d)%-(%d%d)$');y,m,d=tonumber(y),tonumber(m),tonumber(d)
 if not y or y<1900 or y>2100 or m<1 or m>12 or d<1 or d>31 then return nil end
 local target=os.time({year=y,month=m,day=d,hour=12});local check=os.date('*t',target);if check.year~=y or check.month~=m or check.day~=d then return nil end
 local today=os.date('*t');today.hour=12;today.min=0;today.sec=0;return math.floor((target-os.time(today))/86400+.5)
end
local function describe()
 local value,detail,action,secondary,eyebrow='','','','', ''
 if kind=='Hourglass' then value=remaining==0 and '时间到' or clock(remaining);eyebrow='留一点专注';detail=running and '正在计时' or remaining<duration and '已暂停' or '选好时长，慢慢开始';action=running and '暂停' or remaining<duration and remaining>0 and '继续' or '开始';secondary='重置'
 elseif kind=='SunPath' then
  local progress,rise,set,offset=Daylight(os.time(),read(root..'weather-cache.json',131072),read(root..'weather-location.txt',4096));local temp=tonumber(context.weatherTemp)
  value=temp and string.format('%.0f°',temp) or '日光';eyebrow=context.weatherLocation~='' and context.weatherLocation or '今天的天光'
  if not progress then detail='天气待同步' elseif os.time()<rise then detail='日出 '..os.date('!%H:%M',rise+offset) elseif os.time()<set then local minutes=math.ceil((set-os.time())/60);detail='日落还有 '..math.floor(minutes/60)..'时'..(minutes%60)..'分' else detail='日光已落 · 休息一下' end;action='天气详情'
 elseif kind=='YearDots' then
  value=os.date('%m / %d');eyebrow=os.date('%Y')..' · 今天';local days=dateDistance(context.countdownDate or '')
  detail=context.tasksAvailable~='1' and '倒数日待同步' or days and ((context.countdownTitle or '')..' · '..(days==0 and '就是今天' or days>0 and '还有 '..days..' 天' or '已过 '..math.abs(days)..' 天')) or '记住一个值得期待的日子';action='倒数日';secondary='日历'
 elseif kind=='Tide' then
  local elapsed=restTime();local phase=math.floor(elapsed)%8;value=clock(60-elapsed);eyebrow='给自己一分钟';detail=elapsed>=60 and '慢慢回到手边的事' or restRunning and (phase<4 and '吸气  '..(4-phase) or '呼气  '..(8-phase)) or '放松肩膀，跟随潮汐';action=restRunning and '暂停' or elapsed>0 and elapsed<60 and '继续' or '休息 1 分钟';secondary='重置'
 elseif kind=='Orrery' then
  local seconds=focusTime();value=seconds and clock(seconds) or '25:00';eyebrow='只做眼前这一件';detail=seconds and ('已完成 '..(tonumber(context.focusCompleted) or 0)..' 轮专注') or '点击启动电脑专注';action=(tonumber(context.focusDeadline) or 0)>0 and '暂停' or '开始专注';secondary='重置'
 elseif kind=='Bonsai' then
  local pending=tonumber(context.taskPending) or 0;value=context.tasksAvailable=='1' and (pending==0 and '都做好了' or pending..' 件待办') or '小小待办';eyebrow='手边的事';detail=context.tasksAvailable=='1' and (pending==0 and '留一点空白给自己' or context.taskFirst or '') or '打开待办，放下脑中琐事';action='打开待办'
 end
 option('Caption','Text',value);option('Detail','Text',detail);option('Eyebrow','Text',eyebrow or '今天的天光');option('Primary','Text',action);option('Secondary','Text',secondary);option('Secondary','Hidden',secondary=='' and 1 or 0);option('SecondaryHit','Hidden',secondary=='' and 1 or 0);option('ActionGlass','Shape2',secondary=='' and 'Ellipse 0,0,0 | StrokeWidth 0' or 'Rectangle 126,163,101,26,13 | Fill Color 234,243,255,12 | Stroke Color 244,248,255,35 | StrokeWidth .6')
 for i,minutes in ipairs({5,15,25,50})do option('Preset'..i,'Hidden',kind=='Hourglass' and 0 or 1);option('PresetHit'..i,'Hidden',kind=='Hourglass' and 0 or 1);option('Preset'..i,'FontColor',duration==minutes*60 and '255,229,188,255' or '225,235,248,170')end
end
local function render()
 local shapes={};local color=colors[choice];local caption=''
 local function add(s)shapes[#shapes+1]=s;return #shapes end
 local function gradient(name,definition)option('Art',name,definition)end
 local function path(name,definition,style)option('Art',name,definition);add('Path '..name..' | '..style)end
 local function sphere(x,y,r,paint)
  add(string.format('Ellipse %.2f,%.2f,%.2f | Fill RadialGradient %s | Stroke Color 255,255,255,125 | StrokeWidth .6',x,y,r,paint))
  add(ellipse(x-r*.28,y-r*.32,r*.25,r*.16,'255,255,255',160))
 end
 gradient('Pearl','-0.5,-0.7 | 255,255,255,245 ; 0 | '..color..',225 ; 0.48 | 119,137,198,150 ; 1')
 gradient('QuietPearl','-0.5,-0.7 | 255,255,255,82 ; 0 | 188,208,241,35 ; 0.5 | 137,154,193,22 ; 1')
 gradient('Warm','-4,-5 | 255,255,244,255 ; 0 | 255,224,157,250 ; 0.4 | 235,153,117,225 ; 1')
 gradient('Cool','-3,-4 | 249,255,255,255 ; 0 | 166,225,248,240 ; 0.4 | 113,143,218,205 ; 1')
 gradient('Rose','-3,-4 | 255,250,255,255 ; 0 | 225,196,240,230 ; 0.45 | 167,131,211,205 ; 1')
 gradient('Glow','0,0 | '..color..',55 ; 0 | '..color..',0 ; 1')
 gradient('Glass','270 | 255,255,255,125 ; 0 | 217,232,255,16 ; .45 | 185,208,252,75 ; 1')
 gradient('Prism','20 | 193,238,255,190 ; 0 | 255,246,255,225 ; .34 | 241,198,229,150 ; .68 | 189,174,249,105 ; 1')
 option('MonthA','Hidden','1');option('MonthB','Hidden','1');option('MonthC','Hidden','1')
 if kind=='Hourglass' then
  local fraction=math.max(0,math.min(1,(remaining-(running and not paused and subsecond() or 0))/duration));local top=87-57*math.sqrt(fraction)
  add('Ellipse 122,157,46,9 | Fill RadialGradient Glow | StrokeWidth 0')
  path('Vessel','82,24 | LineTo 162,24 | CurveTo 127,88,162,62,135,75 | CurveTo 162,154,135,103,162,116 | LineTo 82,154 | CurveTo 117,88,82,116,109,103 | CurveTo 82,24,109,75,82,62 | ClosePath 1','Fill LinearGradient Glass | Stroke LinearGradient Prism | StrokeWidth 1.3')
  gradient('Sand','270 | 255,240,200,235 ; 0 | 232,190,143,235 ; .4 | 211,158,123,225 ; 1')
  local function half(y)return math.max(1,math.min(30,(87-y)*.68))end
  if fraction>0 then
   local points={string.format('%.2f,%.2f',122-half(top),top)}
   for i=0,18 do local y=top+(87-top)*i/18;points[#points+1]=string.format('LineTo %.2f,%.2f',122-half(y),y)end
   for i=18,0,-1 do local y=top+(87-top)*i/18;points[#points+1]=string.format('LineTo %.2f,%.2f',122+half(y),y)end
   path('UpperSand',table.concat(points,' | ')..' | ClosePath 1','Fill LinearGradient Sand | StrokeWidth 0')
   add(ellipse(122,top,half(top),3,'255,230,182',235))
   for i=1,14 do local y=top+(87-top)*(i/15);local x=122+math.sin(i*2.4)*half(y)*.7;add(ellipse(x,y,.5,.4,'255,250,225',155))end
  end
  if fraction<1 then local height=48*(1-fraction);path('LowerSand',string.format('92,151 | CurveTo 122,%.2f,101,151,114,%.2f | CurveTo 152,151,131,%.2f,143,151 | ClosePath 1',151-height,153-height,153-height),'Fill LinearGradient Sand | StrokeWidth 0');add(ellipse(122,151,29,3,'248,218,173',220))end
  if running and remaining>0 then
   local floor=142-42*(1-fraction);add(line(122,87,122,floor,'255,231,184,90',.6))
   for i=0,3 do local travel=(pulse*1.6+i*.25)%1;add(ellipse(122+math.sin(i*2.4)*.6,89+(floor-89)*travel,.8,1.3,'255,248,220',230))end
  end
  add('Curve 88,32,119,85,88,60,112,73 | Stroke Color 255,255,255,180 | StrokeWidth 1.4')
  add('Curve 133,103,156,145,143,117,154,130 | Stroke Color 255,235,211,130 | StrokeWidth .8')
  for _,y in ipairs({22,156})do add(string.format('Ellipse 122,%d,43,4 | Fill LinearGradient Glass | Stroke LinearGradient Prism | StrokeWidth 1',y));add(line(90,y-1,150,y-1,'255,255,255,150',.7))end
  caption=remaining==0 and '时间到' or string.format('%02d:%02d',math.floor(remaining/60),remaining%60)
 elseif kind=='SunPath' then
  local progress,rise,set,offset=Daylight(os.time(),read(root..'weather-cache.json',131072),read(root..'weather-location.txt',4096))
  gradient('Dawn','270 | 255,235,210,75 ; 0 | 232,211,235,18 ; .42 | 171,213,244,55 ; 1')
  gradient('SunHalo','0,0 | 255,215,141,100 ; 0 | 255,191,141,35 ; .45 | 255,192,159,0 ; 1')
  add('Ellipse 122,142,97,17 | Fill LinearGradient Glass | Stroke Color 255,255,255,105 | StrokeWidth .6')
  path('Dome','34,136 | CurveTo 210,136,43,11,201,11 | ClosePath 1','Fill LinearGradient Dawn | Stroke Color 255,248,236,125 | StrokeWidth .8')
  add('Curve 36,135,208,135,45,17,199,17 | Stroke LinearGradient Prism | StrokeWidth 1.5')
  add('Curve 35,141,209,141,89,131,155,131 | Stroke Color 255,255,255,150 | StrokeWidth .7')
  for i=0,8 do local x=46+i*19;add(line(x,141,x,143+(i%4==0 and 3 or 0),'235,244,255,100',.7))end
  if progress then local x=36+172*progress;local y=135-88*math.sin(math.pi*progress);add(string.format('Ellipse %.2f,%.2f,33 | Fill RadialGradient SunHalo | StrokeWidth 0',x,y));sphere(x,y,12,'Warm');caption=os.date('!%H:%M',rise+offset)..'  —  '..os.date('!%H:%M',set+offset)
  else caption='等待有效天气 · 点击设置' end
  option('MonthA','Hidden',0);option('MonthB','Hidden',0);option('MonthA','X',39);option('MonthB','X',205);option('MonthA','Y',127);option('MonthB','Y',127);option('MonthA','StringAlign','Left');option('MonthB','StringAlign','Right');option('MonthA','Text',progress and os.date('!%H:%M',rise+offset) or '—');option('MonthB','Text',progress and os.date('!%H:%M',set+offset) or '—')
 elseif kind=='YearDots' then
  local today=os.date('*t');local first=os.date('*t',os.time({year=today.year,month=today.month,day=1,hour=12}));local nextMonth=os.time({year=today.year,month=today.month+1,day=1,hour=12});local days=tonumber(os.date('%d',nextMonth-86400));local offset=(first.wday+5)%7
  local target=tonumber((context.countdownDate or ''):match('^'..today.year..'%-'..string.format('%02d',today.month)..'%-(%d%d)$'))
  for cell=0,41 do local day=cell-offset+1;local valid=day>=1 and day<=days;local x=120+(cell%7)*15;local y=51+math.floor(cell/7)*15
   option('Day'..cell,'Hidden',valid and 0 or 1);option('Day'..cell,'Text',valid and day or '');option('Day'..cell,'FontColor',day==today.day and '255,231,191,255' or day<today.day and '224,235,249,140' or '243,249,255,240')
   if valid then local ax=(x-88)/.65;local ay=(y+5-25)/.65
    if day==today.day then add(string.format('Ellipse %.2f,%.2f,10 | Fill LinearGradient Glass | Stroke Color 255,234,199,195 | StrokeWidth 1',ax,ay))end
    if day==target then add(ellipse(ax,ay+12,2,1.4,'179,235,205',240))end
   end
  end
  for i=0,6 do add(line(42+i*23,24,42+i*23,27,'235,244,255,90',1))end
 elseif kind=='Tide' then
  add('Ellipse 122,153,94,11 | Fill RadialGradient Glow | StrokeWidth 0')
  for ribbon=1,4 do
   local phase=restTime()+(restRunning and not paused and subsecond() or 0)
   local shift=(ribbon-1)*17;local wobble=math.sin(phase*math.pi/4+ribbon*.45)*15
   gradient('Ribbon'..ribbon, '300 | 249,249,255,155 ; 0 | '..(ribbon%2==0 and '220,190,238' or color)..',95 ; .26 | 148,201,235,30 ; .5 | 229,240,255,205 ; .78 | 196,178,237,100 ; 1')
   local def=string.format('18,%.2f | CurveTo 224,%.2f,81,%.2f,154,%.2f | LineTo 224,%.2f | CurveTo 18,%.2f,154,%.2f,81,%.2f | ClosePath 1',63+shift,44+shift,17+shift+wobble,111+shift-wobble,62+shift,83+shift,131+shift-wobble,37+shift+wobble)
   path('RibbonPath'..ribbon,def,'Fill LinearGradient Ribbon'..ribbon..' | Stroke Color 236,247,255,75 | StrokeWidth .5')
   add(string.format('Curve 18,%.2f,224,%.2f,81,%.2f,154,%.2f | Stroke LinearGradient Prism | StrokeWidth 1.1',63+shift,44+shift,17+shift+wobble,111+shift-wobble))
  end
 elseif kind=='Orrery' then
  add('Ellipse 122,150,70,12 | Fill RadialGradient Glow | StrokeWidth 0')
  add('Ellipse 122,145,55,10 | Fill LinearGradient Glass | Stroke Color 245,247,255,110 | StrokeWidth .7')
  add(line(122,138,122,92,'243,222,191,160',3));add(line(121,137,121,94,'255,248,229,160',.7))
  local orbitColors={'Cool','Rose','Warm'}
  for ring=1,3 do local rx=43+ring*13;local ry=20+ring*5;local tilt=-34+ring*21;add(string.format('Ellipse 122,87,%d,%d | Fill Color 0,0,0,0 | Stroke LinearGradient Prism | StrokeWidth %.1f | Rotate %d',rx,ry,ring==2 and 1.5 or .8,tilt))end
  add('Ellipse 122,87,34 | Fill RadialGradient Glow | StrokeWidth 0');sphere(122,87,19,'Warm')
  for ring=1,3 do local rx=43+ring*13;local ry=20+ring*5;local tilt=(-34+ring*21)*math.pi/180;local total=tonumber(context.focusTotal) or 1500;local elapsed=math.max(0,total-(focusTime() or total))+((tonumber(context.focusDeadline) or 0)>0 and not paused and subsecond() or 0);local period=ring==1 and 60 or ring==2 and 300 or math.max(1,total);local a=ring*2.15+elapsed/period*math.pi*2;local x=math.cos(a)*rx;local y=math.sin(a)*ry;sphere(122+x*math.cos(tilt)-y*math.sin(tilt),87+x*math.sin(tilt)+y*math.cos(tilt),5+ring*1.3,orbitColors[ring])end
  for _,p in ipairs({{49,50},{194,45},{181,126}})do add(line(p[1]-2,p[2],p[1]+2,p[2],'255,248,233,165',.6));add(line(p[1],p[2]-2,p[1],p[2]+2,'255,248,233,165',.6))end
 elseif kind=='Bonsai' then
  add('Ellipse 122,157,71,14 | Fill RadialGradient Glow | StrokeWidth 0')
  local total,done,available=taskProgress();local count=math.max(8,math.min(12,total));local complete=available and total>0 and done/total or 0
  local leaves={{102,72,-1,117,108},{133,64,1,121,99},{111,50,-1,122,84},{129,39,1,124,68},{90,89,-1,117,119},{153,85,1,125,114},{143,105,1,122,133},{106,111,-1,119,135},{86,60,-1,116,96},{150,45,1,124,80},{116,29,-1,125,56},{162,104,1,129,129}}
  gradient('Leaf','230 | 241,255,248,210 ; 0 | 165,228,207,110 ; .28 | 95,171,167,55 ; .65 | 213,251,230,195 ; 1')
  gradient('LeafDone','230 | 255,249,211,245 ; 0 | 241,211,151,180 ; .35 | 169,197,177,65 ; .7 | 255,239,190,220 ; 1')
  path('Trunk','117,151 | CurveTo 125,53,135,117,110,92','Fill Color 0,0,0,0 | Stroke LinearGradient Prism | StrokeWidth 3.2')
  for i=1,count do
   local leaf=leaves[i];local side=leaf[3];local bx,by=leaf[4],leaf[5]
   local sway=leafSettle>0 and not paused and math.sin((1-leafSettle)*12+i*.7)*leafSettle*3 or 0
   local x=leaf[1]+sway;local y=leaf[2];local paint=i<=math.floor(complete*count+.5) and 'LeafDone' or available and 'Leaf' or 'QuietPearl'
   path('Branch'..i,string.format('%.2f,%.2f | CurveTo %.2f,%.2f,%.2f,%.2f,%.2f,%.2f',bx,by,x,y,bx+side*9,by-2,x-side*5,y+10),'Fill Color 0,0,0,0 | Stroke LinearGradient Prism | StrokeWidth 1.1')
   path('Leaf'..i,string.format('%.2f,%.2f | CurveTo %.2f,%.2f,%.2f,%.2f,%.2f,%.2f | CurveTo %.2f,%.2f,%.2f,%.2f,%.2f,%.2f | ClosePath 1',x,y,x+side*24,y-15,x+side*2,y-19,x+side*17,y-22,x,y,x+side*26,y-1,x+side*11,y+3),'Fill LinearGradient '..paint..' | Stroke Color 228,255,240,150 | StrokeWidth .6')
   add(line(x,y,x+side*20,y-13,'242,255,244,150',.6))
  end
  path('Planter','87,145 | LineTo 157,145 | LineTo 151,166 | CurveTo 93,166,137,173,108,173 | ClosePath 1','Fill LinearGradient Glass | Stroke LinearGradient Prism | StrokeWidth .9')
  add('Ellipse 122,145,35,6 | Fill LinearGradient Glass | Stroke Color 243,252,255,170 | StrokeWidth .7')
  option('MonthA','Hidden',0);option('MonthA','X',218);option('MonthA','Y',35);option('MonthA','StringAlign','Right');option('MonthA','FontFace','Microsoft YaHei UI');option('MonthA','FontSize',8);option('MonthA','Text',available and total>0 and (done..' / '..total) or '')
 end
 for i,s in ipairs(shapes)do option('Art',i==1 and 'Shape' or 'Shape'..i,s)end
 for i=#shapes+1,shapeCount do option('Art',i==1 and 'Shape' or 'Shape'..i,'Ellipse 0,0,0 | StrokeWidth 0')end
 shapeCount=#shapes;describe();for meter in pairs(dirtyMeters)do SKIN:Bang('!UpdateMeter',meter)end;dirtyMeters={};SKIN:Bang('!Redraw')
end
function MotionPolicy()
 local stop=SKIN:GetVariable('AmbientPaused','1')=='1' or read(root..'paused.txt',16):match('true')~=nil
 if stop and restRunning then restElapsed=restTime();restRunning=false end;if stop~=paused then pulse=0;lastTick=os.clock()end;paused=stop
 local moving=kind=='Tide' and restRunning or kind=='Hourglass' and running or kind=='Orrery' and (tonumber(context.focusDeadline) or 0)>os.time() or kind=='Bonsai' and leafSettle>0
 local nextDivider=not paused and moving and '1' or SKIN:GetVariable('AmbientIdleDivider','125')
 if nextDivider~=divider then option('Ambient','UpdateDivider',nextDivider);divider=nextDivider end
end
function Initialize()
 optionCache={};dirtyMeters={};root=SKIN:GetVariable('LiquidRoot');kind=SKIN:GetVariable('WidgetKind');choice=(kind=='Hourglass' or kind=='SunPath') and 2 or (kind=='Tide' or kind=='Bonsai') and 3 or 1;duration=300;remaining=300;running=false;restElapsed=0;restStarted=0;restRunning=false;context={};contextKey='';deadline=0;shapeCount=0;pulse=0;divider=nil;paused=nil;lastSecond=-1;lastTick=os.clock();secondTick=lastTick;leafSettle=0;taskSignature=''
 local text=read(root..'widget-'..kind..'.state',128);local a,b,c,d=text:match('^(%d+) (%d+) (%d+) (%d+)$');if not a then a,b,c=text:match('^(%d+) (%d+) (%d+)$');d='300' end;a,b,c,d=tonumber(a),tonumber(b),tonumber(c),tonumber(d)
 if a and a>=1 and a<=3 and (d==300 or d==900 or d==1500 or d==3000) and b<=d and c<=os.time()+d then choice=a;duration=d;remaining=b;deadline=c;running=c>os.time();if c>0 then remaining=math.max(0,c-os.time())end end
 refreshContext(os.time())
 taskSignature=(context.taskTotal or '')..':'..(context.taskPending or '')..':'..(context.tasksAvailable or '')
 for cell=0,41 do option('Day'..cell,'Hidden',1)end
 local split=kind=='Hourglass' or kind=='Orrery';local transform=kind=='Hourglass' and '.9;0;0;.68;57;13' or kind=='Orrery' and '.76;0;0;.76;76;14' or kind=='YearDots' and '.65;0;0;.65;88;25' or kind=='Bonsai' and '.72;0;0;.72;34;-5' or kind=='SunPath' and '.8;0;0;.8;24;-10' or '.8;0;0;.8;24;0'
 option('Art','TransformationMatrix',transform)
 option('Caption','X',(split or kind=='YearDots') and 18 or 122);if split then option('Caption','X',18)end
 option('Caption','StringAlign',(split or kind=='YearDots') and 'Left' or 'Center');if split then option('Caption','StringAlign','Left')end
 option('Caption','Y',split and 65 or kind=='YearDots' and 64 or kind=='Bonsai' and 117 or 105);option('Caption','FontSize',split and 22 or kind=='YearDots' and 18 or kind=='Bonsai' and 13 or 21)
 if kind=='SunPath' then option('Caption','Y',119);option('Caption','FontSize',16);option('Eyebrow','W',112)end
 option('Detail','Y',kind=='Hourglass' and 106 or 140);option('Detail','W',kind=='Hourglass' and 112 or 208);option('Detail','X',18);option('Detail','FontSize',kind=='Bonsai' and 10 or 8)
 MotionPolicy();render()
end
function Update()
 local now=os.time();local tick=os.clock();local dt=math.max(0,math.min(.1,tick-lastTick));lastTick=tick
 local repaint=false
 if kind=='Hourglass' and running then local value=math.max(0,deadline-now);repaint=value~=remaining;remaining=value;if remaining==0 then running=false;save()end end
 if now~=lastSecond then
  secondTick=tick;if refreshContext(now) then repaint=true end
  local signature=(context.taskTotal or '')..':'..(context.taskPending or '')..':'..(context.tasksAvailable or '')
  if signature~=taskSignature then leafSettle=context.tasksAvailable=='1' and 1 or 0;taskSignature=signature end
  if kind=='Tide' and restRunning then if restTime()>=60 then restElapsed=60;restRunning=false end;repaint=true end
  if kind=='Orrery' and (tonumber(context.focusDeadline) or 0)>0 then repaint=true end
  if kind=='SunPath' or kind=='YearDots' then repaint=repaint or now%60==0 or lastSecond<0 end;lastSecond=now;MotionPolicy()
 end
 if not paused and divider=='1' then pulse=pulse+dt;leafSettle=math.max(0,leafSettle-dt/1.2);repaint=true;if kind=='Bonsai' and leafSettle==0 then MotionPolicy()end end
 if repaint then render()end;return remaining
end
function Activate()
 if kind=='Hourglass' then if running then remaining=math.max(0,deadline-os.time());running=false;deadline=0 else if remaining==0 then remaining=duration end;running=true;deadline=os.time()+remaining end;save()
 elseif kind=='SunPath' then request('workspace.request','weather')
 elseif kind=='YearDots' then request('shelf.request','countdown')
 elseif kind=='Bonsai' then request('shelf.request','tasks')
 elseif kind=='Orrery' then request('widget-action.request','focus-toggle')
 elseif kind=='Tide' then if restRunning then restElapsed=restTime();restRunning=false else if restElapsed>=60 then restElapsed=0 end;pulse=restElapsed;restStarted=os.time();restRunning=true end end
 MotionPolicy();render()
end
function Preset(minutes)if kind~='Hourglass' or (minutes~=5 and minutes~=15 and minutes~=25 and minutes~=50) then return end;duration=minutes*60;remaining=duration;running=false;deadline=0;save();MotionPolicy();render()end
function Reset()
 if kind=='Hourglass' then remaining=duration;running=false;deadline=0;save()
 elseif kind=='Tide' then restElapsed=0;restRunning=false;MotionPolicy()
 elseif kind=='Orrery' then request('widget-action.request','focus-reset')
 elseif kind=='YearDots' then request('panel.request','calendar')end;MotionPolicy();render()
end
