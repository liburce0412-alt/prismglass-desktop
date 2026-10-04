function Update()
 local m=SKIN:GetMeasure('Code')
 local c=m and tonumber(m:GetStringValue())
 if not c then return 0 end
 local s='天气变化'
 if c==0 then s='晴' elseif c<=3 then s='多云' elseif c<=48 then s='雾' elseif c<=67 then s='有雨' elseif c<=77 then s='有雪' elseif c<=82 then s='阵雨' elseif c<=86 then s='阵雪' else s='雷雨' end
 SKIN:Bang('!SetOption','Condition','Text',s)
 return c
end
