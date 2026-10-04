local previous=''
function Update()
 local f=io.open([[{{INSTALL_ROOT}}\music-data\now.txt]],'r')
 if not f then return end
 local content=f:read('*all') f:close()
 if content==previous then return end
 previous=content
 local title,artist,cover,album=content:match('([^\n]*)\n([^\n]*)\n([^\n]*)\n([^\n]*)')
 if not title then return end
 SKIN:Bang('!SetOption','Player','Text',title)
 SKIN:Bang('!SetOption','CompactSummary','Text',title)
 SKIN:Bang('!SetOption','CompactSummary','ToolTipText',title..' · '..artist)
 SKIN:Bang('!SetOption','Player','ToolTipText',title..' · '..artist)
 SKIN:Bang('!SetOption','PlayerHint','Text',artist)
 if cover~='' then
  SKIN:Bang('!SetOption','Cover','ImageName',cover)
  SKIN:Bang('!ShowMeter','Cover') SKIN:Bang('!HideMeter','Note')
 else
  SKIN:Bang('!HideMeter','Cover') SKIN:Bang('!ShowMeter','Note')
 end
 SKIN:Bang('!UpdateMeter','*') SKIN:Bang('!Redraw')
end
