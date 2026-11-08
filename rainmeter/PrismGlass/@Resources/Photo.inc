[Rainmeter]
DefaultAlwaysOnTop=-2
Update=1000
AccurateText=1
DynamicWindowSize=1
OnRefreshAction=[!Hide]

[LiquidHitArea]
Meter=Shape
Shape=Rectangle 0,0,244,196,0 | Fill Color 0,0,0,1 | StrokeWidth 0
UpdateDivider=-1

[LegacyGlass]
Meter=Shape
Group=LegacyGlass
Shape=Rectangle .5,.5,243,195,23.5 | Fill LinearGradient Base | Stroke Color 255,255,255,90 | StrokeWidth 1
Base=120 | 112,137,209,120 ; 0 | 79,82,151,155 ; 1
UpdateDivider=-1

[PhotoClip]
Meter=Shape
Shape=Rectangle 5,5,234,186,20 | Fill Color 255,255,255 | StrokeWidth 0
UpdateDivider=-1

; Always painted under the image; a missing or temporarily replaced wallpaper never leaves a blank card.
[Fallback]
Meter=Shape
Container=PhotoClip
Shape=Rectangle 0,0,244,196 | Fill LinearGradient Sky | StrokeWidth 0
Shape2=Ellipse 185,47,26 | Fill Color 255,230,184,240 | StrokeWidth 0
Shape3=Ellipse 55,224,160,118 | Fill LinearGradient Hill | StrokeWidth 0
Shape4=Ellipse 208,230,145,105 | Fill Color 32,70,79,255 | StrokeWidth 0
Sky=90 | 117,155,175,255 ; 0 | 222,197,169,255 ; 1
Hill=90 | 79,129,132,255 ; 0 | 38,87,95,255 ; 1
UpdateDivider=-1

[Photograph]
Meter=Image
Container=PhotoClip
ImageName=
X=5
Y=5
W=234
H=186
PreserveAspectRatio=1
UpdateDivider=-1

[Frame]
Meter=Shape
Group=LegacyGlass
Shape=Rectangle 5.5,5.5,233,185,19.5 | Fill Color 0,0,0,0 | Stroke Color 255,255,255,90 | StrokeWidth 1
UpdateDivider=-1

[PhotoState]
Measure=Script
ScriptFile=#@#Photo.lua
UpdateDivider=1

[LiquidState]
Measure=Script
ScriptFile=#@#LiquidGlass.lua

[Placeholder]
Meter=String
X=122
Y=90
Text=添加照片
StringAlign=Center
FontFace=Microsoft YaHei UI
FontSize=12
FontColor=255,255,255,230
W=224
ClipString=1
AntiAlias=1
UpdateDivider=-1

[Input]
Meter=Shape
Shape=Rectangle 4,4,236,188,21 | Fill Color 0,0,0,1 | StrokeWidth 0
LeftMouseDoubleClickAction=[!CommandMeasure PhotoState "OpenSettings()"]
ToolTipText=双击打开设置 → 桌面组件 → 照片 · 拖动可移动
UpdateDivider=-1
