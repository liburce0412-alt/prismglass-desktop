offset=0
function Change(n) offset=offset+n Update() SKIN:Bang('!Redraw') end
function Update()
 local now=os.date('*t')
 local t=os.time{year=now.year,month=now.month+offset,day=1,hour=12}
 local d=os.date('*t',t)
 local first=(d.wday+5)%7
 local days=os.date('*t',os.time{year=d.year,month=d.month+1,day=0,hour=12}).day
 SKIN:Bang('!SetOption','MonthTitle','Text',d.year..'年'..d.month..'月')
 SKIN:Bang('!HideMeter','TodayCircle')
 for i=0,41 do
  local day=i-first+1
  SKIN:Bang('!SetOption','Day'..i,'Text',(day>=1 and day<=days) and tostring(day) or '')
  local today=day==now.day and d.year==now.year and d.month==now.month
  SKIN:Bang('!SetOption','Day'..i,'FontColor',today and '255,255,255' or SKIN:GetVariable('Ink'))
  if today then
   SKIN:Bang('!SetOption','TodayCircle','X',44+(i%7)*26)
   SKIN:Bang('!SetOption','TodayCircle','Y',76+math.floor(i/7)*19)
   SKIN:Bang('!ShowMeter','TodayCircle')
  end
 end
 SKIN:Bang('!UpdateMeter','*')
 return offset
end
