[LiquidHitArea]
Meter=Shape
Shape=Rectangle 0,0,244,196,0 | Fill Color 0,0,0,1 | StrokeWidth 0
UpdateDivider=-1

[LegacyGlass]
Meter=Shape
Group=LegacyGlass
Shape=Rectangle .5,.5,243,195,23.5 | Fill LinearGradient Base | Stroke Color 255,255,255,100 | StrokeWidth 1
Base=120 | 235,243,255,84 ; 0 | 174,193,246,32 ; 0.38 | 175,156,225,52 ; 1
Shape2=Curve 16,38,228,25,38,8,196,8 | Stroke Color 255,255,255,48 | StrokeWidth 1
UpdateDivider=-1

[Art]
Meter=Shape
Group=WidgetArt
Shape=Ellipse 122,98,1 | Fill Color 255,255,255 | StrokeWidth 0
UpdateDivider=-1

[MonthA]
Meter=String
Group=WidgetArt
X=122
Y=16
FontFace=Consolas
FontSize=7
FontColor=241,247,255,155
StringAlign=Center
AntiAlias=1
Text=01       02       03       04
Hidden=1
UpdateDivider=-1
[MonthB]
Meter=String
MeterStyle=MonthA
Group=WidgetArt
Y=63
Text=05       06       07       08
[MonthC]
Meter=String
MeterStyle=MonthA
Group=WidgetArt
Y=110
Text=09       10       11       12

[Caption]
Meter=String
Group=WidgetArt
X=122
Y=164
FontFace=Microsoft YaHei UI
FontSize=10
FontColor=245,248,255,225
StringAlign=Center
AntiAlias=1
UpdateDivider=-1

[Ambient]
Measure=Script
ScriptFile=#@#WidgetArt.lua

[LiquidState]
Measure=Script
ScriptFile=#@#LiquidGlass.lua
UpdateDivider=125

[Input]
Meter=Shape
Shape=Rectangle 4,4,236,188,21 | Fill Color 0,0,0,1 | StrokeWidth 0
ToolTipText=#WidgetHint#
UpdateDivider=-1

[Eyebrow]
Meter=String
Group=WidgetArt
X=18
Y=16
W=208
H=16
ClipString=1
FontFace=Microsoft YaHei UI
FontSize=8
FontColor=232,241,255,190
AntiAlias=1
UpdateDivider=-1

[Detail]
Meter=String
MeterStyle=Eyebrow
Group=WidgetArt
Y=140
H=20
FontSize=8
FontColor=242,246,255,225

[ActionGlass]
Meter=Shape
Group=WidgetArt
Shape=Rectangle 17,163,101,26,13 | Fill Color 234,243,255,22 | Stroke Color 244,248,255,65 | StrokeWidth .6
Shape2=Rectangle 126,163,101,26,13 | Fill Color 234,243,255,12 | Stroke Color 244,248,255,35 | StrokeWidth .6
UpdateDivider=-1

[Primary]
Meter=String
Group=WidgetArt
X=67
Y=168
W=101
H=21
FontFace=Microsoft YaHei UI
FontSize=9
FontColor=250,250,255,245
StringAlign=Center
SolidColor=0,0,0,1
AntiAlias=1
UpdateDivider=-1

[Secondary]
Meter=String
MeterStyle=Primary
Group=WidgetArt
X=176

[Preset1]
Meter=String
MeterStyle=Primary
Group=WidgetArt
X=38
Y=132
W=48
H=24
Text=5 分
FontSize=9
Hidden=1
[Preset2]
Meter=String
MeterStyle=Primary
Y=132
W=48
H=24
FontSize=9
Hidden=1
Group=WidgetArt
X=94
Text=15 分
[Preset3]
Meter=String
MeterStyle=Primary
Y=132
W=48
H=24
FontSize=9
Hidden=1
Group=WidgetArt
X=150
Text=25 分
[Preset4]
Meter=String
MeterStyle=Primary
Y=132
W=48
H=24
FontSize=9
Hidden=1
Group=WidgetArt
X=206
Text=50 分

[PrimaryHit]
Meter=Shape
Group=WidgetArt
Shape=Rectangle 17,163,101,26,8 | Fill Color 255,255,255,1 | StrokeWidth 0
LeftMouseUpAction=[!CommandMeasure Ambient "Activate()"]
UpdateDivider=-1

[SecondaryHit]
Meter=Shape
Group=WidgetArt
Shape=Rectangle 126,163,101,26,8 | Fill Color 255,255,255,1 | StrokeWidth 0
LeftMouseUpAction=[!CommandMeasure Ambient "Reset()"]
UpdateDivider=-1

[PresetHit1]
Meter=Shape
Group=WidgetArt
Shape=Rectangle 14,130,48,27,8 | Fill Color 255,255,255,1 | StrokeWidth 0
LeftMouseUpAction=[!CommandMeasure Ambient "Preset(5)"]
UpdateDivider=-1

[PresetHit2]
Meter=Shape
Group=WidgetArt
Shape=Rectangle 70,130,48,27,8 | Fill Color 255,255,255,1 | StrokeWidth 0
LeftMouseUpAction=[!CommandMeasure Ambient "Preset(15)"]
UpdateDivider=-1

[PresetHit3]
Meter=Shape
Group=WidgetArt
Shape=Rectangle 126,130,48,27,8 | Fill Color 255,255,255,1 | StrokeWidth 0
LeftMouseUpAction=[!CommandMeasure Ambient "Preset(25)"]
UpdateDivider=-1

[PresetHit4]
Meter=Shape
Group=WidgetArt
Shape=Rectangle 182,130,48,27,8 | Fill Color 255,255,255,1 | StrokeWidth 0
LeftMouseUpAction=[!CommandMeasure Ambient "Preset(50)"]
UpdateDivider=-1

; Readable civil-day cells; shapes are retained separately from text.
[DayStyle]
Meter=String
Group=WidgetArt
FontFace=Segoe UI
FontSize=7
StringAlign=Center
FontColor=243,249,255,240
AntiAlias=1
Hidden=1
UpdateDivider=-1

[Day0]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=120
Y=51

[Day1]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=135
Y=51

[Day2]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=150
Y=51

[Day3]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=165
Y=51

[Day4]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=180
Y=51

[Day5]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=195
Y=51

[Day6]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=210
Y=51

[Day7]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=120
Y=66

[Day8]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=135
Y=66

[Day9]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=150
Y=66

[Day10]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=165
Y=66

[Day11]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=180
Y=66

[Day12]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=195
Y=66

[Day13]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=210
Y=66

[Day14]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=120
Y=81

[Day15]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=135
Y=81

[Day16]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=150
Y=81

[Day17]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=165
Y=81

[Day18]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=180
Y=81

[Day19]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=195
Y=81

[Day20]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=210
Y=81

[Day21]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=120
Y=96

[Day22]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=135
Y=96

[Day23]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=150
Y=96

[Day24]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=165
Y=96

[Day25]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=180
Y=96

[Day26]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=195
Y=96

[Day27]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=210
Y=96

[Day28]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=120
Y=111

[Day29]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=135
Y=111

[Day30]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=150
Y=111

[Day31]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=165
Y=111

[Day32]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=180
Y=111

[Day33]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=195
Y=111

[Day34]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=210
Y=111

[Day35]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=120
Y=126

[Day36]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=135
Y=126

[Day37]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=150
Y=126

[Day38]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=165
Y=126

[Day39]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=180
Y=126

[Day40]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=195
Y=126

[Day41]
Meter=String
MeterStyle=DayStyle
Group=WidgetArt
X=210
Y=126
