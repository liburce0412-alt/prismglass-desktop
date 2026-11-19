local anchorsPending,anchorRetry=false,0
local alphabet='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'
local function decode(s)
 local bits=s:gsub('.',function(x)
  local p=alphabet:find(x,1,true);if not p then return '' end
  local v=p-1;local b='';for i=5,0,-1 do b=b..(math.floor(v/2^i)%2) end;return b
 end)
 bits=bits:sub(1,math.floor(#bits/8)*8);return bits:gsub('%d%d%d%d%d%d%d%d',function(b) local n=0;for i=1,8 do n=n*2+tonumber(b:sub(i,i)) end;return string.char(n) end)
end
local changedMeters,optionCache={},{}
local fastEnabled,fastExecuting,fastExpected=false,false,0
local function stopFast()
 if fastExpected==0 then return end
 fastExpected=0;SKIN:Bang('!CommandMeasure','DockFrames','Stop 1');SKIN:Bang('!CommandMeasure','DockFrames','Stop 2')
end
local function wakeFast()
 if not fastEnabled or fastExpected~=0 or motionPaused or dragActive or iconOpacity<=0 or #items==0 then return end
 fastExpected=1;SKIN:Bang('!CommandMeasure','DockFrames','Execute 1')
end
function FastFrame(index)
 if fastExpected==0 or index~=fastExpected then return end
 -- Discard stale callbacks from a delayed burst instead of replaying old frames.
 if os.clock()-lastFrame<.004 then return end
 fastExecuting=true;Update();fastExecuting=false
end
function FastEnd(index)
 if fastExpected==0 or index~=fastExpected then return end
 if fastExpected~=0 then fastExpected=3-index;SKIN:Bang('!CommandMeasure','DockFrames','Execute '..fastExpected) end
end
local function option(m,k,v)
 local value=tostring(v);local key=m..'.'..k
 if optionCache[key]==value then return end
 optionCache[key]=value;SKIN:Bang('!SetOption',m,k,value);changedMeters[m]=true;return true
end
local function show(m,visible) SKIN:Bang(visible and '!ShowMeter' or '!HideMeter',m);changedMeters[m]=true end
local function flush()
 if not dirty then return end
 if fullUpdate then SKIN:Bang('!UpdateMeter','*');fullUpdate=false else for m in pairs(changedMeters) do SKIN:Bang('!UpdateMeter',m) end end
 changedMeters={};SKIN:Bang('!Redraw');dirty=false
end
local function spring(x,v,target,omega,dt)
 local y=x-target;local j=v+omega*y;local e=math.exp(-omega*dt)
 x=target+(y+j*dt)*e;v=(v-omega*j*dt)*e
 if math.abs(x-target)<.0001 and math.abs(v)<.001 then return target,0 end
 return x,v
end
local function tooltip(i)
 local anchor=items[i] and (items[i].drawX or items[i].cx)
 local key=tostring(i)..':'..tostring(anchor and math.floor(anchor+.5))
 if tooltipKey==key then return end
 tooltipIndex=i
 -- The native capsule owns the single label; Rainmeter tooltips stay disabled.
 local f=io.open(root..'dock-label'..suffix..'.txt','w')
 if f then local e=items[i];local wrote=f:write(e and e.cx and (e.id..'|'..tostring((e.drawX or e.cx)/tonumber(SKIN:GetVariable('DockWidth')))) or '');local closed=f:close();if wrote and closed then tooltipKey=key end end
end
function Initialize() anchorsPending=false;anchorRetry=0;stopFast();fastEnabled=SKIN:GetVariable('DockFastFrames')=='1';fastExecuting=false;changedMeters={};optionCache={};lastHoverId=nil;tapUntil={};fullUpdate=true;sizeVelocity={};waveVelocity=0;waveAmountVelocity=0;pressIndex=0;pressUntil=0;pressAmount=0;pressVelocity=0;root=SKIN:GetVariable('LiquidRoot');instance=SKIN:GetVariable('DockInstance') or 'primary';if instance=='' then instance='primary' end;suffix=instance=='primary' and '' or '-'..instance;last='';stamp=0;hover=0;waveX=nil;pointerX=nil;waveAmount=0;tooltipIndex=0;tooltipKey=nil;items={};sizes={};drawn={};cell=60;dirty=true;elapsed=0;lastFrame=os.clock();attentionSeen={};bounce={};iconOpacity=255;iconLease=0;iconPaint=nil;divider=false;dividerX=nil;trashDivider=false;trashDividerX=nil;dragActive=false;dragPoll=0;motionPoll=0;motionPaused=false;iconSize=tonumber(SKIN:GetVariable('DockIconSize')) or 48;Hover(0) end
-- Pointer approach is handled by the host; only WebView frames own the icon fade.
function Approach() end
local function pointerSlot(x)
 for i,e in ipairs(items) do
  local center=e.drawX or e.cx
  if x<=center then
   local previous=items[i-1]
   if not previous then return 1 end
   local left=previous.drawX or previous.cx
   return i-1+math.max(0,math.min(1,(x-left)/math.max(1,center-left)))
  end
 end
 return #items
end
function Pointer(fraction)
 if type(fraction)~='number' or fraction~=fraction or fraction< -1 or fraction>1 then return end
 local nextPointer=fraction>=0 and fraction*tonumber(SKIN:GetVariable('DockWidth')) or nil
 -- Rainmeter can miss MouseLeave when Windows hides or moves the skin.
 if fraction<0 then pressIndex=0;Hover(0) end
 if nextPointer and #items>0 then local index=math.floor(pointerSlot(nextPointer)+.5);if hover~=index then Hover(index) end end
 if nextPointer~=pointerX then pointerX=nextPointer;wakeFast() end
end
function Hover(i)
 if not items[i] then i=0 end
 if i~=hover then hoverStart=elapsed end;hover=i
 if i>0 then Approach() end;tooltip((dragActive or iconOpacity<255) and 0 or i)
 local id=items[i] and items[i].id or ''
 if id~=lastHoverId then local f=io.open(root..'dock-hover'..suffix..'.txt','w');if f then f:write(id);f:close();lastHoverId=id end end
 wakeFast()
end
function Press(i) local e=items[i];if not e then return end;pressIndex=i;pressUntil=elapsed+.5;tooltip(0);Approach();wakeFast();local f=io.open(root..'dock-drag-request-'..instance..'.txt','w');if f then f:write(os.time()..'|'..e.id);f:close() end end
local function publishAnchors(clicked)
 if not anchorsPending or (not clicked and elapsed<anchorRetry) then return end
 local anchors={}
 for i,e in ipairs(items) do
  if not sizes[i] or not e.drawX then return end
  local pixels=math.floor(sizes[i]+.5)
  anchors[#anchors+1]=string.format('"%s":[%d,%d,%d,%d]',e.id,math.floor(e.drawX-pixels/2),e.drawY or 36,pixels,pixels)
 end
 local geometry='{'..table.concat(anchors,',')..'}'
 if geometry~=lastAnchors then
  anchorRetry=elapsed+.25
  local f=io.open(root..'dock-anchors'..suffix..'.json','w');if not f then return end
  local wrote=f:write(geometry);local closed=f:close();if not wrote or not closed then return end
  lastAnchors=geometry
 end
 anchorsPending=false;anchorRetry=0
end
function Release()
 publishAnchors(true)
 local item=items[pressIndex]
 if item and hover==pressIndex and not dragActive and not motionPaused and elapsed<=pressUntil then
  tapUntil[item.id]=elapsed+.42
 end
 pressIndex=0;wakeFast()
end
local function load()
 if dragActive then return end
 local f=io.open(root..'dock-state.txt','r');if not f then return end;local data=f:read('*a');f:close();if data==last then return end;local hoveredSlot,hoveredId=hover,items[hover] and items[hover].id;tooltip(0);last=data
 local previousSizes,previousVelocity,previousItems={},{},{};for i,e in ipairs(items) do previousSizes[e.id]=sizes[i];previousVelocity[e.id]=sizeVelocity[i];previousItems[e.id]=e end
 items={};sizes={};sizeVelocity={};drawn={};dirty=true;fullUpdate=true;lastAnchors=nil
 for id,name,icon,count,active,pinned,attention,launch in data:gmatch('([%w_-]+)	([^	]+)	([^	]+)	(%d+)	([01])	([01])	([01])	?([a-z%-]*)') do
  local prior=previousItems[id]
  table.insert(items,{id=id,name=decode(name),icon=decode(icon),count=tonumber(count),active=active=='1',pinned=pinned=='1',attention=attention=='1',launch=launch,drawX=prior and prior.drawX,velocityX=prior and prior.velocityX})
  sizes[#items]=previousSizes[id];sizeVelocity[#items]=previousVelocity[id]
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
 local gap=0;divider=false;dividerX=nil;trashDivider=false;trashDividerX=nil;SKIN:Bang('!HideMeter','Divider');SKIN:Bang('!HideMeter','TrashDivider');iconPaint=nil
 for i=2,#items do if not items[i].pinned and items[i-1].pinned then divider=i;break end end
 if #items>1 and items[#items].id=='trash' and divider~=#items then trashDivider=#items end
 local inset=26-(divider and 10 or 0)-(trashDivider and 10 or 0)
 for i=1,32 do local e=items[i]
  if e then
   if divider==i then gap=gap+20;SKIN:Bang('!ShowMeter','Divider') end
   if trashDivider==i then gap=gap+20;SKIN:Bang('!ShowMeter','TrashDivider') end
   e.x=inset+(i-1)*cell+gap;e.cx=e.x+cell/2
   for _,m in ipairs({'Hit'..i,'Icon'..i}) do
    SKIN:Bang('!ShowMeter',m);option(m,'ToolTipHidden','1');option(m,'ToolTipText','')
    option(m,'LeftMouseDownAction','[!CommandMeasure Dock "Press('..i..')"]');option(m,'LeftMouseUpAction','[!CommandMeasure Dock "Release()"]["'..root..'LiquidDesktop.exe" "--dock-release" "'..e.id..'"]')
    option(m,'RightMouseUpAction',e.id=='trash' and '' or '["'..root..'LiquidDesktop.exe" "--dock-menu" "'..e.id..'"]')
   end
   option('Hit'..i,'X',e.x);option('Hit'..i,'Shape','Rectangle 0,0,'..cell..',96 | Fill Color 0,0,0,1 | StrokeWidth 0')
   option('Icon'..i,'ImageName',e.icon);option('Dot'..i,'X',e.cx)
   option('Dot'..i,'Shape',e.launch=='launch-failed' and 'Rectangle -3,-3,6,6,1 | Fill Color 245,106,96 | StrokeWidth 0' or e.launch=='launching' and 'Ellipse 0,0,3,3 | Fill Color 0,0,0,0 | Stroke Color #Ink# | StrokeWidth 1.5' or e.attention and 'Ellipse 0,0,3,3 | Fill Color 255,167,78 | StrokeWidth 0' or e.active and 'Rectangle -6,-2,12,4,2 | Fill Color #Blue# | StrokeWidth 0' or 'Ellipse 0,0,2,2 | Fill Color #Ink# | StrokeWidth 0')
   SKIN:Bang(iconOpacity>191 and (e.count>0 or e.launch=='launching' or e.launch=='launch-failed') and '!ShowMeter' or '!HideMeter','Dot'..i)
   else for _,m in ipairs({'Hit','Icon','Dot'}) do option(m..i,'ToolTipHidden','1');option(m..i,'ToolTipText','');SKIN:Bang('!HideMeter',m..i) end end
  end
 local retained=items[hoveredSlot] and items[hoveredSlot].id==hoveredId and hoveredSlot or 0
 if retained==0 then waveX=nil;waveAmount=0 end
 Hover(retained)
end
function ReloadLayout() last='';stamp=0;iconSize=tonumber(SKIN:GetVariable('DockIconSize')) or 48;load();dirty=true;wakeFast() end
local function paintIcons()
 if iconPaint==iconOpacity then return false end
 iconPaint=iconOpacity;dirty=true
 tooltip((dragActive or iconOpacity<255) and 0 or hover)
 for i,e in ipairs(items) do
  option('Icon'..i,'ImageAlpha',iconOpacity)
  show('Dot'..i,iconOpacity>191 and (e.count>0 or e.launch=='launching' or e.launch=='launch-failed'))
 end
 show('Divider',iconOpacity>191 and divider)
 show('TrashDivider',iconOpacity>191 and trashDivider)
 return true
end
function SetIconOpacity(alpha)
 if type(alpha)~='number' or alpha~=alpha or alpha<0 or alpha>255 or alpha~=math.floor(alpha) then return end
 iconOpacity=alpha;iconLease=os.time()+3
 -- Complete the meter update before the native host acknowledges this frame.
 if paintIcons() then flush() end
 if alpha>0 then wakeFast() else stopFast() end
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
  -- Keep Unicode in the skin; ASCII references avoid Rainmeter's ANSI Lua bang conversion.
  option('DragHint','Text',remove=='1' and '#DockHintUnpin#' or remove=='2' and '#DockHintCancel#' or external=='1' and '#DockHintPin#' or '#DockHintMove#')
  SKIN:Bang('!ShowMeter','DragHint');dirty=true
 elseif dragActive then dragActive=false;last='';stamp=0;dirty=true;SKIN:Bang('!HideMeter','DragMarker');SKIN:Bang('!HideMeter','DragHint') end
end
function Update()
 -- shortcut: after the Windows CRT clock expires (~24.8 days), use nominal timing until Rainmeter restarts.
 local nowFrame=os.clock()
 -- Keep the low-rate state poll, but do not double-step a live ActionTimer frame.
 if fastEnabled and fastExpected~=0 and not fastExecuting and nowFrame-lastFrame<.1 then return #items end
 local dt=nowFrame>lastFrame and math.min(.1,nowFrame-lastFrame) or .016;lastFrame=nowFrame
 elapsed=elapsed+dt
 interaction()
 local now=os.time();if now~=stamp then stamp=now;load() end
 icons()
 if pointerX and hover>0 then local index=math.floor(pointerSlot(pointerX)+.5);if hover~=index then Hover(index) end end
 local active=items[hover] and not motionPaused and not dragActive and iconOpacity>0
 local destination=active and (pointerX or items[hover].drawX or items[hover].cx) or waveX
 -- Keep the live pointer current; icon size and spacing already have their own springs.
 if destination then if active and pointerX then waveX=destination;waveVelocity=0 elseif waveX then waveX,waveVelocity=spring(waveX,waveVelocity or 0,destination,50,dt) else waveX=destination;waveVelocity=0 end end
 if motionPaused or dragActive then waveAmount=0;waveAmountVelocity=0 else waveAmount,waveAmountVelocity=spring(waveAmount,waveAmountVelocity or 0,active and 1 or 0,28,dt) end
 if dragActive or elapsed>pressUntil then pressIndex=0 end
 pressAmount,pressVelocity=spring(pressAmount,pressVelocity,pressIndex>0 and not motionPaused and 1 or 0,46,dt)
 local base=math.max(16,math.min(iconSize,cell-10))
 -- Spread neighbors with the wave, reserving enough room for crowded rows.
 local growth=math.min(base*.85,82-base,math.max(0,(tonumber(SKIN:GetVariable('DockWidth'))-52-base*#items)/math.min(#items,2.5)))
 local waveSlot=waveX and pointerSlot(waveX)
 for i,e in ipairs(items) do
  -- Interpolate between visible centers: the nearest icon and two on each side.
  local distance=waveSlot and math.abs(i-waveSlot)/2.5 or 1
  local influence=distance<1 and (1+math.cos(distance*math.pi))*.5 or 0
  local target=(base+growth*influence*waveAmount)*(1-(pressIndex==i and pressAmount or 0)*.09);local size=sizes[i] or target
  if motionPaused or dragActive then size=target;sizeVelocity[i]=0 else size,sizeVelocity[i]=spring(size,sizeVelocity[i] or 0,target,46,dt) end;sizes[i]=size
  local pixels=math.floor(size+.5)
  -- Hover grows from one baseline; only a real attention/click event lifts it.
  local remain=math.max(0,(bounce[e.id] or 0)-elapsed);local tap=math.max(0,(tapUntil[e.id] or 0)-elapsed);local jump=math.max(math.abs(math.sin(remain*8))*26*math.min(1,remain),tap>0 and 12*math.sin(math.pi*tap/.42) or 0)
  jump=motionPaused and 0 or math.floor(math.min(jump,math.max(0,84-pixels-3)))
  e.drawY=84-pixels-jump
  local key=pixels..':'..jump
  if drawn[i]~=key then
   drawn[i]=key;dirty=true
   option('Icon'..i,'W',pixels);option('Icon'..i,'H',pixels);option('Icon'..i,'Y',84-pixels-jump)
  end
 end
 local total=0;for i,e in ipairs(items) do total=total+(sizes[i] or base) end
 local width=tonumber(SKIN:GetVariable('DockWidth')) or 850
 local dividerGap=divider and 20 or 0;local trashGap=trashDivider and 20 or 0
 local gap=math.min(math.max(0,cell-base),math.max(0,(width-52-total-dividerGap-trashGap)/math.max(1,#items-1)));local x=(width-total-gap*math.max(0,#items-1)-dividerGap-trashGap)/2
 for i,e in ipairs(items) do
  if divider==i then x=x+dividerGap end
  if trashDivider==i then x=x+trashGap end
  local pixels=math.floor((sizes[i] or base)+.5)
  local spread=x+pixels/2;local target=e.cx+(spread-e.cx)*waveAmount
  if motionPaused or dragActive then e.drawX=e.cx;e.velocityX=0 else e.drawX,e.velocityX=spring(e.drawX or e.cx,e.velocityX or 0,target,50,dt) end
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
 if trashDivider then local left,right=items[trashDivider-1],items[trashDivider];local nextX=math.floor((left.drawX+sizes[trashDivider-1]/2+right.drawX-sizes[trashDivider]/2)/2+.5);if trashDividerX~=nextX then trashDividerX=nextX;option('TrashDivider','X',nextX);dirty=true end end
 anchorsPending=anchorsPending or dirty
 flush()
 local moving=math.abs(waveVelocity)>.01 or math.abs(waveAmountVelocity)>.001 or math.abs(pressVelocity)>.001 or pressIndex>0
 for i,e in ipairs(items) do moving=moving or math.abs(sizeVelocity[i] or 0)>.01 or math.abs(e.velocityX or 0)>.01 or (bounce[e.id] or 0)>elapsed or (tapUntil[e.id] or 0)>elapsed end
 if not moving or not lastAnchors then publishAnchors() end
 if not moving then tooltip((dragActive or iconOpacity<255) and 0 or hover) end
 if moving and not motionPaused and not dragActive then wakeFast() else stopFast() end
 return #items
end
