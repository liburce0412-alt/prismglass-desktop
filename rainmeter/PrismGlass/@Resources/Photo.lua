local root,slot,lastSignature,lastSize
local function set(meter,key,value)SKIN:Bang('!SetOption',meter,key,tostring(value))end
local function selection()
 local file=io.open(root..'photo-cards/'..slot..'.txt','rb');if not file then return nil end
 local text=file:read(2048);file:close()
 local token,width,height=text:match('^([a-f0-9]+)\r?\n(%d+)\r?\n(%d+)\r?\n')
 width=tonumber(width);height=tonumber(height)
 if not token or #token~=32 or width<80 or width>330 or height<80 or height>330 then return nil end
 return {path=root..'photo-cards/'..token..'.png',width=width,height=height,token=token}
end
local function signature(path)
 local file=io.open(path,'rb');if not file then return nil end
 local size=file:seek('end');file:seek('set',0);local head=file:read(1024)
 local tail='';if size and size>1024 then file:seek('set',size-1024);tail=file:read(1024) or '' end;file:close()
 if not size or size<45 or not head or head:sub(1,8)~='\137PNG\13\10\26\10' or not (head..tail):find('IEND',1,true) then return nil end
 return tostring(size)..head..tail
end
local function resize(width,height)
 local key=width..','..height;if key==lastSize then return end;lastSize=key
 set('LiquidHitArea','Shape',string.format('Rectangle 0,0,%d,%d | Fill Color 0,0,0,1 | StrokeWidth 0',width,height))
 set('LegacyGlass','Shape',string.format('Rectangle .5,.5,%d,%d,23.5 | Fill LinearGradient Base | Stroke Color 255,255,255,90 | StrokeWidth 1',width-1,height-1))
 set('PhotoClip','Shape',string.format('Rectangle 5,5,%d,%d,20 | Fill Color 255,255,255 | StrokeWidth 0',width-10,height-10))
 set('Photograph','W',width-10);set('Photograph','H',height-10)
 set('Frame','Shape',string.format('Rectangle 5.5,5.5,%d,%d,19.5 | Fill Color 0,0,0,0 | Stroke Color 255,255,255,90 | StrokeWidth 1',width-11,height-11))
 set('Input','Shape',string.format('Rectangle 4,4,%d,%d,21 | Fill Color 0,0,0,1 | StrokeWidth 0',width-8,height-8))
 set('Fallback','Shape',string.format('Rectangle 0,0,%d,%d | Fill LinearGradient Sky | StrokeWidth 0',width,height))
 set('Fallback','Shape2',string.format('Ellipse %.1f,%.1f,26 | Fill Color 255,230,184,240 | StrokeWidth 0',width*.76,height*.24))
 set('Fallback','Shape3',string.format('Ellipse %.1f,%.1f,%.1f,%.1f | Fill LinearGradient Hill | StrokeWidth 0',width*.23,height*1.14,width*.66,height*.6))
 set('Fallback','Shape4',string.format('Ellipse %.1f,%.1f,%.1f,%.1f | Fill Color 32,70,79,255 | StrokeWidth 0',width*.85,height*1.17,width*.6,height*.54))
 set('Placeholder','X',width/2);set('Placeholder','Y',height/2-10);set('Placeholder','W',width-20)
 SKIN:Bang('!UpdateMeter','*')
end
function Update()
 local card=selection();local path=card and card.path or root..'wallpaper.png'
 local current=signature(path);local key=(card and card.token or 'wallpaper')..(current or 'missing')
 if key~=lastSignature then
  lastSignature=key;resize(card and card.width or 244,card and card.height or 196)
  set('Photograph','ImageName','');SKIN:Bang('!UpdateMeter','Photograph')
  if current then set('Photograph','ImageName',path);SKIN:Bang('!UpdateMeter','Photograph') end
  SKIN:Bang(card and current and '!HideMeter' or '!ShowMeter','Placeholder')
  set('Placeholder','Text',card and '照片暂不可用' or '添加照片')
  set('Input','LeftMouseUpAction',card and '' or '[!CommandMeasure PhotoState "OpenSettings()"]')
  SKIN:Bang('!UpdateMeter','Placeholder');SKIN:Bang('!UpdateMeter','Input');SKIN:Bang('!Redraw')
 end
 return current and 1 or 0
end
function OpenSettings()
 local file=io.open(root..'workspace.request','w');if file then file:write('components');file:close() end
end
function Initialize()
 root=SKIN:GetVariable('LiquidRoot');slot=SKIN:GetVariable('PhotoSlot','Photo')
 if slot~='Photo' and not slot:match('^Photo[2-6]$') then slot='Photo' end
 lastSignature=nil;lastSize=nil;Update()
end
