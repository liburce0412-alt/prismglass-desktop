local palettes={
 {name='极光',period=8.4,tint='255,255,255',color='192,224,255'},
 {name='晚霞',period=10.5,tint='255,208,226',color='255,222,232'},
 {name='青空',period=12,tint='198,255,232',color='198,246,255'}
}
local root,statePath,choice,phase,glint,hover,hoverTarget,stopped,lastPoll,lastManual,lastDivider,lastTick
local function option(meter,key,value)SKIN:Bang('!SetOption',meter,key,tostring(value))end
local function shape(index,value)option('AmbientArt',index==1 and 'Shape' or 'Shape'..index,value)end
local function clock()
 -- Rainmeter uses the Windows CRT wall clock; fall back if its long-running counter expires.
 local value=os.clock();return value>=0 and value or os.time()
end
local function ellipse(x,y,rx,ry,color,alpha)
 return string.format('Ellipse %.2f,%.2f,%.2f,%.2f | Fill Color %s,%d | StrokeWidth 0',x,y,rx,ry,color,math.min(255,math.max(0,alpha)))
end
local function copy()
 local palette=palettes[(choice-1)%#palettes+1]
 option('Artwork','ImageTint',palette.tint)
 option('OrbInput','ToolTipText',palette.name..' · 点击切换 · 拖动可移动')
 SKIN:Bang('!UpdateMeter','Artwork');SKIN:Bang('!UpdateMeter','OrbInput')
end
local function render()
 local palette=palettes[(choice-1)%#palettes+1]
  local breath=(1-math.cos(phase*math.pi*2/palette.period))/2
  local size=180+breath*10+hover*2+math.sin(glint*math.pi)*3
  option('Artwork','X',122-size/2);option('Artwork','Y',100-size/2);option('Artwork','W',size);option('Artwork','H',size)
  option('Artwork','ImageAlpha',math.floor(225+breath*30))
  shape(1,ellipse(122,151,46+breath*4,3.5,'176,190,255',math.floor(28+breath*12)))
 SKIN:Bang('!UpdateMeterGroup','AmbientRender');SKIN:Bang('!Redraw')
end
function MotionPolicy()
 local now=os.time()
 if now~=lastPoll then
  local f=io.open(root..'paused.txt','r');lastManual=f and f:read('*a'):match('true')~=nil or false
  if f then f:close() end;lastPoll=now
 end
 local nextStopped=SKIN:GetVariable('AmbientPaused','1')=='1' or lastManual
 if nextStopped~=stopped then lastTick=clock() end
 stopped=nextStopped
 local divider=stopped and SKIN:GetVariable('AmbientIdleDivider','63') or '1'
 if divider~=lastDivider then option('Ambient','UpdateDivider',divider);lastDivider=divider end
end
function Initialize()
 root=SKIN:GetVariable('LiquidRoot')
 statePath=root..'ambient-BreathingOrb.txt'
 local f=io.open(statePath,'r');choice=f and tonumber(f:read('*a')) or 1;if f then f:close() end
 if not choice or choice%1~=0 or choice<1 or choice>#palettes then choice=1 end
 phase=0;glint=0;hover=0;hoverTarget=0;lastPoll=nil;lastManual=false;lastDivider=nil;stopped=nil;lastTick=clock()
 MotionPolicy();copy();render()
end
function Update()
 MotionPolicy()
 local now=clock();local elapsed=math.max(0,math.min(.15,now-lastTick));lastTick=now
 if stopped then return choice end
 phase=phase+elapsed;glint=math.max(0,glint-elapsed*2.8);hover=hover+(hoverTarget-hover)*(1-math.exp(-elapsed*5));render()
 return choice
end
function Cycle()
 choice=choice%#palettes+1
 local f=io.open(statePath,'w');if f then f:write(tostring(choice));f:close() end
 MotionPolicy();glint=stopped and 0 or 1;lastTick=clock();MotionPolicy();copy();render()
end
function Hover(value)hoverTarget=value==1 and 1 or 0 end
