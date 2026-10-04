total=1500

remaining=1500

deadline=0

function Save()

 local f=io.open(statePath,'w')

 if f then f:write(total..' '..remaining..' '..deadline) f:close() end

end

function Initialize()

 statePath=SKIN:GetVariable('@')..'focus-state.txt'

 local f=io.open(statePath,'r')

 if f then

  local a,b,c=f:read('*a'):match('^(%d+) (%d+) (%d+)') f:close()

  if a then total=tonumber(a) remaining=tonumber(b) deadline=tonumber(c) end

 end

end

function Update()

 if deadline>0 then

  remaining=math.max(0,deadline-os.time())

  if remaining==0 then
   local completedAt=deadline
   deadline=0 Save()
   local path=SKIN:GetVariable('@')..'focus-completed.txt'
   local count=0;local old=io.open(path,'r')
   if old then count=tonumber(old:read('*a'):match('^(%d+)')) or 0;old:close() end
   if total>300 then count=count+1 end
   local record=io.open(path,'w');if record then record:write(count..' '..completedAt..' '..total);record:close() end
  end

 end

 SKIN:Bang('!SetOption','Countdown','Text',string.format('%02d:%02d',math.floor(remaining/60),remaining%60))

 SKIN:Bang('!SetOption','CompactSummary','Text',string.format('%02d:%02d',math.floor(remaining/60),remaining%60))

 SKIN:Bang('!SetOption','Toggle','Text',deadline>0 and '暂停' or (remaining==0 and '再来一轮' or '开始'))

 local status=remaining==0 and '时间到了，活动一下吧' or (deadline>0 and (total==300 and '休息一下，放松眼睛' or '正在专注，慢慢完成一件事') or (remaining<total and '已暂停，随时继续' or '给自己一段不被打扰的时间'))

 SKIN:Bang('!SetOption','Status','Text',status)

 SKIN:Bang('!SetOption','Progress','Shape2','Rectangle 0,0,'..math.floor(212*(1-remaining/total))..',4,2 | Fill Color 66,133,245,210 | StrokeWidth 0')

 SKIN:Bang('!UpdateMeter','*')

 SKIN:Bang('!Redraw')

 return remaining

end

function Toggle()

 if deadline>0 then remaining=math.max(0,deadline-os.time()) deadline=0

 else if remaining==0 then remaining=total end deadline=os.time()+remaining end

 Save() Update()

end

function Reset() remaining=total deadline=0 Save() Update() end

function Preset(minutes) total=minutes*60 Reset() end
