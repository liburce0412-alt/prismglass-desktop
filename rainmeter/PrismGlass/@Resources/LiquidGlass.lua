function Initialize()
 heartbeat=SKIN:GetVariable('LiquidRoot')..'heartbeat.txt'
 lastMode=''; lastTheme=''
end
function Update()
 local f=io.open(heartbeat,'r'); local stamp,theme
 if f then stamp,theme=f:read('*a'):match('(%d+)%s+(%a+)');f:close() end
 local active=stamp and math.abs(os.time()-tonumber(stamp))<7
 local mode=active and 'live' or 'legacy'
 if mode~=lastMode then
  SKIN:Bang(active and '!HideMeterGroup' or '!ShowMeterGroup','LegacyGlass')
  lastMode=mode
 end
 theme=active and theme or 'Sky'
 if theme~=lastTheme then
  SKIN:Bang('!SetVariable','Ink',theme=='Astro' and '231,239,252' or '27,42,66')
  SKIN:Bang('!SetVariable','Muted',theme=='Astro' and '180,198,224' or '60,79,110')
  SKIN:Bang('!SetVariable','Blue',theme=='Astro' and '119,184,255' or '32,108,236')
  if SKIN:GetMeter('ThemeToggle') then
  SKIN:Bang('!SetOption','ThemeToggle','Text',theme=='Astro' and '' or '')
  SKIN:Bang('!SetOption','ThemeToggle','ToolTipText','当前 '..theme..'，点击切换主题')
  end
  lastTheme=theme
 end
 if SKIN:GetMeter('MotionToggle') then
  local p=io.open(SKIN:GetVariable('LiquidRoot')..'paused.txt','r')
  local stopped=false
  if p then stopped=p:read('*a'):match('true')~=nil;p:close() end
  SKIN:Bang('!SetOption','MotionToggle','Text',stopped and '' or '')
  SKIN:Bang('!SetOption','MotionToggle','ToolTipText',stopped and '动效已暂停，点击继续' or '动效已开启，点击暂停')
 end
 SKIN:Bang('!UpdateMeter','*');SKIN:Bang('!Redraw')
 return active and 1 or 0
end
