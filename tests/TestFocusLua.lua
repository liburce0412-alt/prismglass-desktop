-- Runs in a disposable off-screen Rainmeter profile with only fixture paths.
function Initialize()
 local nativeSkin=SKIN
 local folder=nativeSkin:GetVariable('Fixture')
 local script=nativeSkin:GetVariable('ProductionScript')
 local exportScript=nativeSkin:GetVariable('ProductionExport')
 local originalOpen,originalTime=io.open,os.time
 local report=assert(originalOpen(folder..'lua-checks.txt','w'))
 local checks,heartbeatWrites=0,0
 local fakeTime=2000000000
 local failHeartbeat=false
 local options={}
 local function check(value,name)
  report:write((value and 'PASS ' or 'FAIL ')..name..'\n');report:flush()
  if not value then error(name) end;checks=checks+1
 end
 local function read(name)local f=assert(originalOpen(folder..name,'r'));local text=f:read('*a');f:close();return text end
 local function write(name,text)local f=assert(originalOpen(folder..name,'w'));f:write(text);f:close()end
 SKIN={GetVariable=function(_,key)if key=='@' or key=='LiquidRoot' then return folder end;error('Unexpected variable: '..key)end,
  Bang=function(_,command,meter,key,value)if command=='!SetOption' then options[meter..'.'..key]=value end end}
 os.time=function()return fakeTime end
 io.open=function(path,mode)
  if path==folder..'widget-Focus.json' and mode=='w' then heartbeatWrites=heartbeatWrites+1;if failHeartbeat then return nil end end
  return originalOpen(path,mode)
 end
 local ok,err=pcall(function()
  dofile(script);Initialize()
  check(read('focus-state.txt')=='1500 1500 0','first activation persists a paused default timer before its first command')
  write('focus-state.txt','0 0 0');Initialize()
  check(total==1500 and remaining==1500 and read('focus-state.txt')=='1500 1500 0','invalid zero-duration state resets safely without division by zero')
  write('focus-state.txt','1500 900 0')
  dofile(script);Initialize()
  check(total==1500 and remaining==900 and deadline==0,'existing saved timer state is restored')
  Update()
  check(heartbeatWrites==1 and read('widget-Focus.json')=='{"available":true}','first update writes only availability heartbeat')
  for i=1,10 do Update() end
  fakeTime=fakeTime+3;Update()
  check(heartbeatWrites==1,'repeated updates before four seconds do not rewrite heartbeat')
  fakeTime=fakeTime+1;Update()
  check(heartbeatWrites==2,'heartbeat refreshes at four seconds')
  failHeartbeat=true;fakeTime=fakeTime+4
  check(Update()==900 and heartbeatWrites==3,'unwritable heartbeat does not interrupt countdown display')
  failHeartbeat=false;Update()
  check(heartbeatWrites==4 and companionHeartbeat==fakeTime,'failed heartbeat retries on next update')
  Preset(25);Toggle()
  check(total==1500 and remaining==1500 and deadline==fakeTime+1500,'25-minute preset starts the existing timer')
  fakeTime=fakeTime+100;Update()
  check(remaining==1400 and options['Countdown.Text']=='23:20','timer progression and displayed minutes stay unchanged')
  Toggle();local pausedState=read('focus-state.txt');fakeTime=fakeTime+20;Update()
  check(remaining==1400 and deadline==0 and pausedState=='1500 1400 0','pause persists remaining time without counting elapsed pause')
  Toggle();local completedAt=deadline;fakeTime=deadline;Update()
  check(remaining==0 and deadline==0 and read('focus-completed.txt')=='1 '..completedAt..' 1500','focus completion grants exactly one existing reward')
  for i=1,8 do fakeTime=fakeTime+1;Update() end
  check(read('focus-completed.txt')=='1 '..completedAt..' 1500','subsequent heartbeats never duplicate completion rewards')
  Preset(5);Toggle();completedAt=deadline;fakeTime=deadline;Update()
  check(read('focus-completed.txt')=='1 '..completedAt..' 300','five-minute break records completion without adding a focus reward')
  Initialize();Update()
  check(total==300 and remaining==0 and deadline==0 and read('focus-completed.txt')=='1 '..completedAt..' 300','reloading completed timer does not issue another reward')
  Reset()
  check(remaining==300 and deadline==0 and read('focus-state.txt')=='300 300 0','reset preserves the current preset and paused state')
  local config,ready='Weather','0'
  local values={Temp=23,Code=2,High=26,Low=18,CPU=35,RAM=50,Down=512,Up=128,Volume=72}
  SKIN={GetVariable=function(_,key,default)if key=='CURRENTCONFIG' then return 'PrismGlass\\'..config elseif key=='LiquidRoot' then return folder elseif key=='WeatherReady' then return ready end;return default end,
   GetMeasure=function(_,key)return {GetValue=function()return values[key] or 0 end,GetMaxValue=function()return 100 end,GetStringValue=function()return key=='Sunrise' and '2026-10-26T06:20' or '2026-10-26T18:00' end}end}
  dofile(exportScript);Initialize();Update()
  check(read('widget-Weather.json'):find('"ready":false') and read('widget-Weather.json'):find('"updatedAt":0'),'weather has no invented successful timestamp before its first response')
  ready='1';MarkWeatherUpdated();Update();local actualUpdated=string.format('%.0f',fakeTime*1000)
  check(read('widget-Weather.json'):find('"updatedAt":'..actualUpdated) and read('widget-Weather.json'):find('"temp":23'),'weather response callback records real successful refresh time')
  fakeTime=fakeTime+60;Update()
  check(read('widget-Weather.json'):find('"updatedAt":'..actualUpdated),'weather heartbeat never pretends the forecast was freshly fetched')
  ready='0';Update()
  check(read('widget-Weather.json'):find('"ready":false'),'weather connection failure marks existing forecast unavailable')
  ready='1';MarkWeatherUpdated();Update()
  check(not read('widget-Weather.json'):find('"updatedAt":'..actualUpdated),'next successful weather response advances its update timestamp')
  config='System';Initialize();Update()
  check(read('widget-System.json'):find('"cpu":35') and read('widget-System.json'):find('"ram":50'),'system export retains CPU and memory measurements')
  config='Music';Initialize();Update()
  check(read('widget-Music.json')=='{"value":72}','volume export retains the actual audio measure')
 end)
 if ok then report:write('Completed '..checks..' native Focus Lua checks.\n')else report:write('ERROR '..tostring(err)..'\n')end
 report:close();io.open=originalOpen;os.time=originalTime;SKIN=nativeSkin
 Update=function()return 0 end
end
