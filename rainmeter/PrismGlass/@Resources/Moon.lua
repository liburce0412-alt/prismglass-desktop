-- Solar/lunar coordinates adapted from SunCalc 1.9.0, BSD-2-Clause.
-- See licenses/SunCalc-BSD-2-Clause.txt. Fixed north-up approximation, no location needed.
local sin,cos,asin,acos,atan2,rad=math.sin,math.cos,math.asin,math.acos,math.atan2,math.pi/180
local lastHour
local function clamp(v)return math.max(-1,math.min(1,v))end
local function coords(l,b)
 local e=rad*23.4397
 return atan2(sin(l)*cos(e)-math.tan(b)*sin(e),cos(l)),asin(sin(b)*cos(e)+cos(b)*sin(e)*sin(l))
end
function Illumination(timestamp)
 local d=timestamp/86400+2440587.5-2451545
 local m=rad*(357.5291+.98560028*d)
 local sunL=m+rad*(1.9148*sin(m)+.02*sin(2*m)+.0003*sin(3*m))+rad*102.9372+math.pi
 local sra,sdec=coords(sunL,0)
 local lm=rad*(134.963+13.064993*d)
 local moonL=rad*(218.316+13.176396*d)+rad*6.289*sin(lm)
 local moonB=rad*5.128*sin(rad*(93.272+13.229350*d))
 local mra,mdec=coords(moonL,moonB)
 local distance=385001-20905*cos(lm)
 local phi=acos(clamp(sin(sdec)*sin(mdec)+cos(sdec)*cos(mdec)*cos(sra-mra)))
 local incidence=atan2(149598000*sin(phi),distance-149598000*cos(phi))
 return (1+cos(incidence))/2,sin(moonL-sunL)>0
end
function PhasePath(fraction,waxing)
 local points={'122,26'}
 local sign=waxing and 1 or -1
 -- A lit limb and an elliptical terminator give the correct illuminated disk area.
 for i=1,64 do
  local angle=math.pi*i/64
  points[#points+1]=string.format('LineTo %.3f,%.3f',122+sign*62*sin(angle),88-62*cos(angle))
 end
 for i=63,0,-1 do
  local angle=math.pi*i/64
  points[#points+1]=string.format('LineTo %.3f,%.3f',122+sign*(1-2*fraction)*62*sin(angle),88-62*cos(angle))
 end
 points[#points+1]='ClosePath 1';return table.concat(points,' | ')
end
function Update()
 local hour=math.floor(os.time()/3600);if hour==lastHour then return 0 end;lastHour=hour
 local f,waxing=Illumination(os.time())
 local label=f<.02 and '新月' or f>.98 and '满月' or (f>.46 and f<.54) and (waxing and '上弦月' or '下弦月') or waxing and (f<.5 and '峨眉月' or '盈凸月') or (f<.5 and '残月' or '亏凸月')
 SKIN:Bang('!SetOption','LitMoonClip','Phase',PhasePath(f,waxing));SKIN:Bang('!UpdateMeter','LitMoonClip')
 SKIN:Bang('!SetOption','PhaseLabel','Text',label..'  '..math.floor(f*100+.5)..'%');SKIN:Bang('!UpdateMeter','PhaseLabel')
 SKIN:Bang('!UpdateMeter','MoonLight');SKIN:Bang('!Redraw');return f
end
function Initialize()lastHour=nil;Update()end
