local alphabet='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'
local function decode(s)
 local bits=s:gsub('.',function(x)
  local p=alphabet:find(x,1,true);if not p then return '' end
  local v=p-1;local b='';for i=5,0,-1 do b=b..(math.floor(v/2^i)%2) end;return b
 end)
 bits=bits:sub(1,math.floor(#bits/8)*8);return bits:gsub('%d%d%d%d%d%d%d%d',function(b) local n=0;for i=1,8 do n=n*2+tonumber(b:sub(i,i)) end;return string.char(n) end)
end
function Initialize() root=SKIN:GetVariable('LiquidRoot');last='';stamp=0;hover=0;items={};sizes={};drawn={};cell=60;dirty=true;elapsed=0;attentionSeen={};bounce={};Hover(0) end
function Hover(i) if i~=hover then hoverStart=elapsed end;hover=i;local f=io.open(root..'dock-hover.txt','w');if f then f:write(items[i] and items[i].id or '');f:close() end end
local function option(m,k,v) SKIN:Bang('!SetOption',m,k,tostring(v)) end
local function load()
 local f=io.open(root..'dock-state.txt','r');if not f then return end;local data=f:read('*a');f:close();if data==last then return end;last=data;items={};drawn={};dirty=true
 for id,name,icon,count,active,pinned,attention in data:gmatch('([pr]%d+)	([^	]+)	([^	]+)	(%d+)	([01])	([01])	([01])') do
  table.insert(items,{id=id,name=decode(name),icon=decode(icon),count=tonumber(count),active=active=='1',pinned=pinned=='1',attention=attention=='1'})
  if attention=='1' and not attentionSeen[id] then bounce[id]=elapsed+2.4 end;attentionSeen[id]=attention=='1'
 end
 if #items==0 then return end
 local sw=tonumber(SKIN:GetVariable('SCREENAREAWIDTH'));local sh=tonumber(SKIN:GetVariable('SCREENAREAHEIGHT'))
 -- Match SideDock's physical right edge; Rainmeter coordinates follow Windows DPI.
 local physicalWidth,physicalHeight=sw,sh
 local layout=io.open(root..'layout.json','r')
 if layout then local content=layout:read('*a');layout:close();physicalWidth=tonumber(content:match('"width"%s*:%s*(%d+)')) or sw;physicalHeight=tonumber(content:match('"height"%s*:%s*(%d+)')) or sh end
 local dpi=physicalWidth/sw
 local railScale=math.max(.5,math.min(1.25,(physicalHeight-240)/680,(physicalWidth-48)/852))
 local left=math.ceil((16+92*railScale)/dpi)
 local width=sw-left-math.ceil(24/dpi)
 cell=(width-52)/#items
 SKIN:Bang('!SetVariable','DockWidth',width);SKIN:Bang('!Move',left,sh-136)
 local diagnostic=io.open(root..'dock-layout.json','w')
 if diagnostic then diagnostic:write(string.format('{"left":%s,"width":%s,"cell":%s,"count":%s,"dpi":%s}',left,width,cell,#items,dpi));diagnostic:close() end
 local gap=0;SKIN:Bang('!HideMeter','Divider')
 for i=1,32 do local e=items[i]
  if e then
   if not e.pinned and i>1 and items[i-1].pinned then gap=20;option('Divider','X',16+(i-1)*cell+10);SKIN:Bang('!ShowMeter','Divider') end
   e.x=16+(i-1)*cell+gap;e.cx=e.x+cell/2
   for _,m in ipairs({'Hit'..i,'Icon'..i}) do
    SKIN:Bang('!ShowMeter',m);option(m,'ToolTipHidden','1')
    option(m,'LeftMouseUpAction','["'..root..'LiquidDesktop.exe" "--dock" "'..e.id..'"]')
    option(m,'RightMouseUpAction','["'..root..'LiquidDesktop.exe" "--dock-menu" "'..e.id..'"]')
   end
   option('Hit'..i,'X',e.x);option('Hit'..i,'Shape','Rectangle 0,0,'..cell..',96 | Fill Color 0,0,0,1 | StrokeWidth 0')
   option('Icon'..i,'ImageName',e.icon);option('Dot'..i,'X',e.cx)
   option('Dot'..i,'Shape',e.attention and 'Ellipse 0,0,3,3 | Fill Color 255,167,78 | StrokeWidth 0' or e.active and 'Rectangle -6,-2,12,4,2 | Fill Color #Blue# | StrokeWidth 0' or 'Ellipse 0,0,2,2 | Fill Color #Ink# | StrokeWidth 0')
   SKIN:Bang(e.count>0 and '!ShowMeter' or '!HideMeter','Dot'..i)
  else for _,m in ipairs({'Hit','Icon','Dot'}) do SKIN:Bang('!HideMeter',m..i) end end
 end
end
function Update()
 elapsed=elapsed+.016
 local now=os.time();if now~=stamp then stamp=now;load() end
 for i,e in ipairs(items) do
  local target=math.min(48,cell-10)+(i==hover and 30 or 0);local size=(sizes[i] or target);size=size+(target-size)*.14;sizes[i]=size
  local pixels=math.floor(size+.5)
  local remain=math.max(0,(bounce[e.id] or 0)-elapsed);local jump=math.abs(math.sin(remain*8))*26*math.min(1,remain)
  local age=elapsed-(hoverStart or 0)
  if i==hover and age<.65 then jump=math.max(jump,math.abs(math.sin(age*10))*18*math.exp(-age*2.4)) end
  jump=math.floor(math.min(jump,math.max(0,84-pixels-3)))
  local key=pixels..':'..jump
  if drawn[i]~=key then
   drawn[i]=key;dirty=true
   option('Icon'..i,'W',pixels);option('Icon'..i,'H',pixels);option('Icon'..i,'X',math.floor(e.cx-pixels/2));option('Icon'..i,'Y',84-pixels-jump)
  end
 end
 if dirty then SKIN:Bang('!UpdateMeter','*');SKIN:Bang('!Redraw');dirty=false end
 return #items
end
