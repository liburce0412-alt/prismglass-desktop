function Initialize()
 local nativeSkin=SKIN
 local root=nativeSkin:GetVariable('Fixture')
 local script=nativeSkin:GetVariable('ProductionScript')
 local report=assert(io.open(root..'lua-checks.txt','w'))
 local originalTime,originalClock=os.time,os.clock
 local now,checks,redraws,tick,textWrites=2000000000,0,0,0,0
 local values={LiquidRoot=root,AmbientKind='BreathingOrb',AmbientPaused='0'}
 local options={}
 local function check(value,name)
  report:write((value and 'PASS ' or 'FAIL ')..name..'\n');report:flush()
  if not value then error(name) end;checks=checks+1
 end
 local function write(name,text)local f=assert(io.open(root..name,'wb'));f:write(text);f:close()end
 SKIN={GetVariable=function(_,key,default)return values[key] or default end,
  Bang=function(_,command,meter,key,value)
   if command=='!SetOption' then options[meter..'.'..key]=value;if key=='Text' then textWrites=textWrites+1 end elseif command=='!Redraw' then redraws=redraws+1 end
  end}
 os.time=function()return now end
 os.clock=function()return tick end
 local ok,err=pcall(function()
  for _,kind in ipairs({'BreathingOrb'}) do
   values.AmbientKind=kind;values.AmbientPaused='0';write('paused.txt','false');write('ambient-'..kind..'.txt','999')
   dofile(script);Initialize()
   check(Update()==1,kind..' rejects invalid saved state')
   local before=options['Artwork.ImageTint']
   Cycle();check(Update()==2 and before~=options['Artwork.ImageTint'],kind..' click changes its visible content')
   local f=assert(io.open(root..'ambient-'..kind..'.txt','r'));local saved=f:read('*a');f:close()
   check(saved=='2',kind..' selection persists locally')
   Initialize();check(Update()==2,kind..' reload retains the selection')
   values.AmbientPaused='1';MotionPolicy();local draws=redraws
   for i=1,5 do Update() end
   check(redraws==draws and options['Ambient.UpdateDivider']=='63',kind..' runtime pause stops animation and reduces update rate')
   Cycle();check(Update()==3,kind..' remains clickable with reduced motion')
   values.AmbientPaused='0';write('paused.txt','true');now=now+1;MotionPolicy();draws=redraws;Update()
   check(redraws==draws,kind..' also respects the existing manual pause file')
   write('paused.txt','false');now=now+1;MotionPolicy();Cycle();draws=redraws;Update()
   check(redraws>draws,kind..' resumes only after both pause sources clear')
  end
  values.AmbientKind='BreathingOrb';write('ambient-BreathingOrb.txt','1');tick=0;Initialize()
  local writes=textWrites
  for i=1,60 do tick=i/60;Update() end
  local fast=tonumber(options['Artwork.W']);tick=0;Initialize()
  for i=1,30 do tick=i/30;Update() end
  check(math.abs(fast-tonumber(options['Artwork.W']))<.0001,'breathing follows elapsed time at both 30 and 60 updates per second')
  check(textWrites==writes,'art-only cards never write persistent captions')
  values.AmbientPaused='1';MotionPolicy();local frozen=options['Artwork.W'];tick=tick+600;Update();values.AmbientPaused='0';MotionPolicy();Update()
  check(options['Artwork.W']==frozen,'resuming after a long pause does not jump or catch up')
  values.AmbientPaused='0';MotionPolicy();Hover(1);tick=tick+.1;Update()
  check(options['OrbInput.ToolTipText']~=nil and options['Artwork.ToolTipText']==nil and options['OrbInput.X']==nil and options['OrbInput.W']==nil,'hover input and tooltip remain on the fixed hit area')
  local resource=script:gsub('Ambient.lua$','')
  dofile(resource..'Moon.lua')
  -- NASA/Fred Espenak UTC phase table: eclipse.gsfc.nasa.gov/phase/phase2001gmt.html
  local f=Illumination(1712600460);check(f<.01,'April 2024 eclipse/new moon is nearly unlit')
  f=Illumination(1711350000);check(f>.99,'March 2024 full moon is nearly fully lit')
  local waxing;f,waxing=Illumination(1713208380);check(math.abs(f-.5)<.04 and waxing,'April 2024 first quarter has a waxing half disk')
  f,waxing=Illumination(1712027700);check(math.abs(f-.5)<.04 and not waxing,'April 2024 last quarter has a waning half disk')
  check(PhasePath(.25,true)~=PhasePath(.25,false),'waxing and waning illumination use opposite limbs')
  Initialize();local draws=redraws;Update();check(redraws==draws,'moon visual updates only when the hour changes')
  dofile(resource..'Photo.lua');Initialize();check(Update()==0 and options['Photograph.ImageName']=='','missing wallpaper leaves the always-visible local fallback')
  write('wallpaper.png','\137PNG\13\10\26\10'..string.rep('a',60)..'IEND');Update()
  check(options['Photograph.ImageName']==root..'wallpaper.png','complete local wallpaper is displayed')
  write('wallpaper.png','\137PNG\13\10\26\10'..string.rep('a',60));Update()
  check(options['Photograph.ImageName']=='','an incomplete wallpaper replacement returns to the local fallback')
  write('wallpaper.png','\137PNG\13\10\26\10'..string.rep('b',60)..'IEND');Update()
  check(options['Photograph.ImageName']==root..'wallpaper.png','finished wallpaper replacement becomes visible again')
  local token=string.rep('a',32);write('photo-cards/Photo2.txt',token..'\n197\n290\nPortrait\n');write('photo-cards/'..token..'.png','\137PNG\13\10\26\10'..string.rep('b',60)..'IEND')
  values.PhotoSlot='Photo2';Initialize()
  check(options['Photograph.ImageName']==root..'photo-cards/'..token..'.png','each photo slot reads only its own imported image')
  check(options['Photograph.W']=='187' and options['Photograph.H']=='280' and options['LiquidHitArea.Shape']:find('197,290',1,true),'portrait image and native hit region resize together')
  OpenSettings();local f=assert(io.open(root..'workspace.request','r'));local request=f:read('*a');f:close();check(request=='components','double click opens the existing desktop component settings')
  os.remove(root..'photo-cards/'..token..'.png');Update();check(options['Photograph.ImageName']=='' and options['Photograph.H']=='280','a missing imported file keeps its photo proportions and visible fallback')
  write('photo-cards/Photo2.txt','../../private\n197\n290\nInvalid\n');Update();check(options['Photograph.ImageName']==root..'wallpaper.png','invalid slot metadata cannot select arbitrary paths')
  report:write('Completed '..checks..' widget Lua checks.\n')
 end)
 if not ok then report:write('ERROR '..tostring(err)..'\n') end
 report:close();SKIN=nativeSkin;os.time=originalTime;os.clock=originalClock
end
function Update()return 0 end
