-- Static face / live hands follow ModularClocks' layered Shape update pattern.
-- Rainmeter Time values are integer seconds; interpolate the visible hand only,
-- and resynchronise on each actual civil second (never accumulate timer ticks).
local second,anchor,paused,divider,root,lastPose
local function hand(angle,length,width,color,tail)
 local a=angle*math.pi*2-math.pi/2
 return string.format('Line %.3f,%.3f,%.3f,%.3f | Stroke Color %s | StrokeWidth %.2f | StrokeStartCap Round | StrokeEndCap Round',122-math.cos(a)*(tail or 0),98-math.sin(a)*(tail or 0),122+math.cos(a)*length,98+math.sin(a)*length,color,width)
end
function MotionPolicy()
 local f=io.open(root..'paused.txt','r');local stopped=f and f:read('*a'):match('true')~=nil;if f then f:close()end
 paused=SKIN:GetVariable('AmbientPaused','1')=='1' or stopped
 local value=paused and '63' or '1'
 if divider~=value then SKIN:Bang('!SetOption','Ambient','UpdateDivider',value);divider=value end
end
function Initialize()
 root=SKIN:GetVariable('LiquidRoot');second=-1;anchor=os.clock();lastPose='';divider=nil;MotionPolicy();Update()
end
function Update()
 local now=os.time();local tick=os.clock()
 if now~=second then second=now;anchor=tick;MotionPolicy()end
 local date=os.date('*t',now);local time=date.hour*3600+date.min*60+date.sec+(not paused and math.max(0,math.min(.999,tick-anchor)) or 0)
 local ink=SKIN:GetVariable('Ink','255,255,255');local pose=hand(time/43200,37,4.5,ink..',245',3)..hand(time/3600,56,2.5,ink..',235',5)..hand(time/60,65,1.1,'255,177,151,245',10)
 if pose~=lastPose then
  SKIN:Bang('!SetOption','Hands','Shape',hand(time/43200,37,4.5,ink..',245',3))
  SKIN:Bang('!SetOption','Hands','Shape2',hand(time/3600,56,2.5,ink..',235',5))
  SKIN:Bang('!SetOption','Hands','Shape3',hand(time/60,65,1.1,'255,177,151,245',10))
  SKIN:Bang('!UpdateMeter','Hands');SKIN:Bang('!Redraw');lastPose=pose
 end
 return time
end
