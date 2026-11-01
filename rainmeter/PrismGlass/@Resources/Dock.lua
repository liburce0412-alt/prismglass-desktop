local alphabet='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'
local function decode(s)
 local bits=s:gsub('.',function(x)
  local p=alphabet:find(x,1,true);if not p then return '' end
  local v=p-1;local b='';for i=5,0,-1 do b=b..(math.floor(v/2^i)%2) end;return b
 end)
 bits=bits:sub(1,math.floor(#bits/8)*8);return bits:gsub('%d%d%d%d%d%d%d%d',function(b) local n=0;for i=1,8 do n=n*2+tonumber(b:sub(i,i)) end;return string.char(n) end)
end
local function option(m,k,v) SKIN:Bang('!SetOption',m,k,tostring(v)) end
local function tooltip(i)
 local anchor=items[i] and (items[i].drawX or items[i].cx)
 local key=tostring(i)..':'..tostring(anchor and math.floor(anchor+.5))
 if tooltipKey==key then return end
 tooltipKey=key;tooltipIndex=i
 -- The native capsule owns the single label; Rainmeter tooltips stay disabled.
 local f=io.open(root..'dock-label'..suffix..'.txt','w')
 if f then local e=items[i];f:write(e and e.cx and (e.id..'|'..tostring((e.drawX or e.cx)/tonumber(SKIN:GetVariable('DockWidth')))) or '');f:close() end
end
function Initialize() root=SKIN:GetVariable('LiquidRoot');instance=SKIN:GetVariable('DockInstance') or 'primary';if instance=='' then instance='primary' end;suffix=instance=='primary' and '' or '-'..instance;last='';stamp=0;hover=0;waveX=nil;pointerX=nil;waveAmount=0;tooltipIndex=0;tooltipKey=nil;items={};sizes={};drawn={};cell=60;dirty=true;elapsed=0;lastFrame=os.clock();attentionSeen={};bounce={};iconOpacity=255;iconLease=0;iconPaint=nil;divider=false;dividerX=nil;dragActive=false;dragPoll=0;motionPoll=0;motionPaused=false;iconSize=tonumber(SKIN:GetVariable('DockIconSize')) or 48;Hover(0) end
-- Pointer approach is handled by the host; only WebView frames own the icon fade.
function Approach() end
function Pointer(fraction)
 if type(fraction)~='number' or fraction~=fraction or fraction< -1 or fraction>1 then return end
 pointerX=fraction>=0 and fraction*tonumber(SKIN:GetVariable('DockWidth')) or nil
end
function Hover(i) if not items[i] then i=0 end;if i~=hover then hoverStart=elapsed end;hover=i;if i>0 then Approach() end;tooltip((dragActive or iconOpacity<255) and 0 or i);local f=io.open(root..'dock-hover'..suffix..'.txt','w');if f then f:write(items[i] and items[i].id or '');f:close() end end
function Press(i) local e=items[i];if not e then return end;tooltip(0);Approach();local f=io.open(root..'dock-drag-request-'..instance..'.txt','w');if f then f:write(os.time()..'|'..e.id);f:close() end end
local function load()
 if dragActive then return end
 local f=io.open(root..'dock-state.txt','r');if not f then return end;local data=f:read('*a');f:close();if data==last then return end;local hoveredSlot,hoveredId=hover,items[hover] and items[hover].id;tooltip(0);last=data
 local previousSizes={};for i,e in ipairs(items) do previousSizes[e.id]=sizes[i] end
 items={};sizes={};drawn={};dirty=true
 for id,name,icon,count,active,pinned,attention in data:gmatch('([%w_-]+)	([^	]+)	([^	]+)	(%d+)	([01])	([01])	([01])') do
  table.insert(items,{id=id,name=decode(name),icon=decode(icon),count=tonumber(count),active=active=='1',pinned=pinned=='1',attention=attention=='1'})
  sizes[#items]=previousSizes[id]
  if attention=='1' and not attentionSeen[id] then bounce[id]=elapsed+2.4 end;attentionSeen[id]=attention=='1'
 end
 if #items==0 then Hover(0);return end
 local sw=tonumber(SKIN:GetVariable('SCREENAREAWIDTH'));local sh=tonumber(SKIN:GetVariable('SCREENAREAHEIGHT'))
 -- Keep equal side margins so the Dock and its lyric capsule share the island's center.
 -- Clear SideDock's physical right edge; Rainmeter coordinates follow Windows DPI.
 local physicalWidth,physicalHeight=sw,sh
 local layout=io.open(root..'layout.json','r')
 if layout then local content=layout:read('*a');layout:close();physicalWidth=tonumber(content:match('"width"%s*:%s*(%d+)')) or sw;physicalHeight=tonumber(content:match('"height"%s*:%s*(%d+)')) or sh end
 local monitorWidth=tonumber(SKIN:GetVariable('DockMonitorWidth')) or 0
 local monitorHeight=tonumber(SKIN:GetVariable('DockMonitorHeight')) or 0
 local dpi=tonumber(SKIN:GetVariable('DockPixelScale')) or 0;if dpi<=0 then dpi=physicalWidth/sw end
 if monitorWidth>0 and monitorHeight>0 then physicalWidth=monitorWidth;physicalHeight=monitorHeight;sw=monitorWidth/dpi;sh=monitorHeight/dpi end
 -- Windows virtualizes the monitor's dimensions, but retains its desktop origin.
 local monitorLeft=tonumber(SKIN:GetVariable('DockMonitorLeft')) or 0
 local monitorTop=tonumber(SKIN:GetVariable('DockMonitorTop')) or 0
 local offset=(tonumber(SKIN:GetVariable('DockOffset')) or 18)/dpi
 local railScale=math.max(.5,math.min(1.25,(physicalHeight-240)/680,(physicalWidth-48)/852))
 local left=math.ceil((16+92*railScale)/dpi)
 local width=sw-2*left
 cell=(width-52)/#items
 SKIN:Bang('!SetVariable','DockWidth',width);SKIN:Bang('!Move',monitorLeft+left,monitorTop+sh-104-offset)
 local diagnostic=io.open(root..'dock-layout'..suffix..'.json','w')
 if diagnostic then diagnostic:write(string.format('{"left":%s,"width":%s,"cell":%s,"count":%s,"dpi":%s}',left,width,cell,#items,dpi));diagnostic:close() end
 local gap=0;divider=false;dividerX=nil;SKIN:Bang('!HideMeter','Divider');iconPaint=nil
 for i=2,#items do if not items[i].pinned and items[i-1].pinned then divider=i;break end end
 local inset=divider and 16 or 26
 for i=1,32 do local e=items[i]
  if e then
   if divider==i then gap=20;SKIN:Bang('!ShowMeter','Divider') end
   e.x=inset+(i-1)*cell+gap;e.cx=e.x+cell/2
   for _,m in ipairs({'Hit'..i,'Icon'..i}) do
    SKIN:Bang('!ShowMeter',m);option(m,'ToolTipHidden','1');option(m,'ToolTipText',m=='Hit'..i and e.name or '')
    option(m,'LeftMouseDownAction','[!CommandMeasure Dock "Press('..i..')"]');option(m,'LeftMouseUpAction','')
    option(m,'RightMouseUpAction','["'..root..'LiquidDesktop.exe" "--dock-menu" "'..e.id..'"]')
   end
   option('Hit'..i,'X',e.x);option('Hit'..i,'Shape','Rectangle 0,0,'..cell..',96 | Fill Color 0,0,0,1 | StrokeWidth 0')
   option('Icon'..i,'ImageName',e.icon);option('Dot'..i,'X',e.cx)
   option('Dot'..i,'Shape',e.attention and 'Ellipse 0,0,3,3 | Fill Color 255,167,78 | StrokeWidth 0' or e.active and 'Rectangle -6,-2,12,4,2 | Fill Color #Blue# | StrokeWidth 0' or 'Ellipse 0,0,2,2 | Fill Color #Ink# | StrokeWidth 0')
   SKIN:Bang(e.count>0 and '!ShowMeter' or '!HideMeter','Dot'..i)
   else for _,m in ipairs({'Hit','Icon','Dot'}) do option(m..i,'ToolTipHidden','1');option(m..i,'ToolTipText','');SKIN:Bang('!HideMeter',m..i) end end
  end
 local retained=items[hoveredSlot] and items[hoveredSlot].id==hoveredId and hoveredSlot or 0
 if retained==0 then waveX=nil;waveAmount=0 end
 Hover(retained)
end
function ReloadLayout() last='';stamp=0;iconSize=tonumber(SKIN:GetVariable('DockIconSize')) or 48;load();dirty=true end
local function paintIcons()
 if iconPaint==iconOpacity then return false end
 iconPaint=iconOpacity;dirty=true
 tooltip((dragActive or iconOpacity<255) and 0 or hover)
 for i,e in ipairs(items) do
  option('Icon'..i,'ImageAlpha',iconOpacity)
  SKIN:Bang(iconOpacity>191 and e.count>0 and '!ShowMeter' or '!HideMeter','Dot'..i)
 end
 SKIN:Bang(iconOpacity>191 and divider and '!ShowMeter' or '!HideMeter','Divider')
 return true
end
function SetIconOpacity(alpha)
 if type(alpha)~='number' or alpha~=alpha or alpha<0 or alpha>255 or alpha~=math.floor(alpha) then return end
 iconOpacity=alpha;iconLease=os.time()+3
 -- Complete the meter update before the native host acknowledges this frame.
 if paintIcons() then SKIN:Bang('!UpdateMeter','*');SKIN:Bang('!Redraw') end
end
local function icons()
 if iconLease>0 and os.time()>=iconLease then iconLease=0;iconOpacity=255 end
 paintIcons()
end
local function interaction()
 if elapsed>=motionPoll then
  motionPoll=elapsed+.25;local f=io.open(root..'dock-motion-'..instance..'.txt','r')
  if not f then f=io.open(root..'paused.txt','r') end
  if f then motionPaused=f:read('*a'):match('true')~=nil;f:close() end
 end
 if elapsed<dragPoll then return end;dragPoll=elapsed+.048
 local f=io.open(root..'dock-drag-feedback-'..instance..'.txt','r');local expiry,slot,remove,external
 if f then expiry,slot,remove,external=f:read('*a'):match('^(%d+)|(%d+)|([012])|([01])$');f:close() end
 local active=expiry and tonumber(expiry)>=os.time() or false
 if active then
   dragActive=true;tooltip(0);Approach();local x=16+tonumber(slot)*cell
  option('DragMarker','X',x);SKIN:Bang(remove=='0' and '!ShowMeter' or '!HideMeter','DragMarker')
  option('DragHint','X',math.max(70,math.min(tonumber(SKIN:GetVariable('DockWidth'))-70,x)))
  option('DragHint','Text',remove=='1' and '松手取消固定 · 保留原文件' or remove=='2' and '松手取消 · Esc 返回' or external=='1' and '松手固定到 Dock · Esc 取消' or '松手调整顺序 · Esc 取消')
  SKIN:Bang('!ShowMeter','DragHint');dirty=true
 elseif dragActive then dragActive=false;last='';stamp=0;dirty=true;SKIN:Bang('!HideMeter','DragMarker');SKIN:Bang('!HideMeter','DragHint') end
end
function Update()
 -- shortcut: after the Windows CRT clock expires (~24.8 days), use nominal timing until Rainmeter restarts.
 local nowFrame=os.clock();local dt=nowFrame>lastFrame and math.min(.064,nowFrame-lastFrame) or .016;lastFrame=nowFrame
 elapsed=elapsed+dt
 interaction()
 local now=os.time();if now~=stamp then stamp=now;load() end
 icons()
 local active=items[hover] and not motionPaused and not dragActive and iconOpacity==255
 local destination=active and (pointerX or items[hover].cx) or waveX
 if destination then waveX=waveX and waveX+(destination-waveX)*(1-math.exp(-50*dt)) or destination end
 if motionPaused or dragActive then waveAmount=0 else waveAmount=waveAmount+((active and 1 or 0)-waveAmount)*(1-math.exp(-16*dt)) end
 local base=math.max(16,math.min(iconSize,cell-10))
 -- Spread neighbors with the wave, reserving enough room for crowded rows.
 local growth=math.min(base*.85,82-base,math.max(0,(tonumber(SKIN:GetVariable('DockWidth'))-52-base*#items)/math.min(#items,2.5)))
 for i,e in ipairs(items) do
  local distance=waveX and math.abs(e.cx-waveX)/math.max(1,cell*2.2) or 1
  local influence=distance<1 and (1+math.cos(distance*math.pi))*.5 or 0
  local target=base+growth*influence*waveAmount;local size=(sizes[i] or target);size=(motionPaused or dragActive) and target or size+(target-size)*(1-math.exp(-32*dt));sizes[i]=size
  local pixels=math.floor(size+.5)
  local remain=math.max(0,(bounce[e.id] or 0)-elapsed);local jump=math.max(math.abs(math.sin(remain*8))*26*math.min(1,remain),8*influence*waveAmount)
  jump=motionPaused and 0 or math.floor(math.min(jump,math.max(0,84-pixels-3)))
  local key=pixels..':'..jump
  if drawn[i]~=key then
   drawn[i]=key;dirty=true
   option('Icon'..i,'W',pixels);option('Icon'..i,'H',pixels);option('Icon'..i,'Y',84-pixels-jump)
  end
 end
 local total=0;for i,e in ipairs(items) do total=total+(sizes[i] or base) end
 local width=tonumber(SKIN:GetVariable('DockWidth')) or 850
 local dividerGap=divider and 20 or 0
 local gap=math.min(math.max(0,cell-base),math.max(0,(width-52-total-dividerGap)/math.max(1,#items-1)));local x=(width-total-gap*math.max(0,#items-1)-dividerGap)/2
 for i,e in ipairs(items) do
  if divider==i then x=x+dividerGap end
  local pixels=math.floor((sizes[i] or base)+.5)
  local spread=x+pixels/2;local target=e.cx+(spread-e.cx)*waveAmount
  e.drawX=(motionPaused or dragActive) and e.cx or (e.drawX or e.cx)+(target-(e.drawX or e.cx))*(1-math.exp(-38*dt))
  x=x+pixels+gap
 end
 for i,e in ipairs(items) do
  local pixels=math.floor((sizes[i] or base)+.5)
  local left=i==1 and 0 or ((items[i-1].drawX or items[i-1].cx)+e.drawX)/2
  local right=i==#items and width or (e.drawX+(items[i+1].drawX or items[i+1].cx))/2
  local key=math.floor(e.drawX*2+.5)..':'..pixels..':'..math.floor(left*2+.5)..':'..math.floor(right*2+.5)
  if e.positionKey~=key then
   e.positionKey=key;dirty=true;option('Icon'..i,'X',math.floor(e.drawX-pixels/2));option('Dot'..i,'X',e.drawX)
   option('Hit'..i,'X',left);option('Hit'..i,'Shape','Rectangle 0,0,'..math.max(1,right-left)..',96 | Fill Color 0,0,0,1 | StrokeWidth 0')
  end
 end
 if divider then local left,right=items[divider-1],items[divider];local nextX=math.floor((left.drawX+sizes[divider-1]/2+right.drawX-sizes[divider]/2)/2+.5);if dividerX~=nextX then dividerX=nextX;option('Divider','X',nextX);dirty=true end end
 tooltip((dragActive or iconOpacity<255) and 0 or hover)
 if dirty then SKIN:Bang('!UpdateMeter','*');SKIN:Bang('!Redraw');dirty=false end
 return #items
end
