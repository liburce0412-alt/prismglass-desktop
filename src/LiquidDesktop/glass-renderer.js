'use strict';
// Glasswork inward-rim / RGB split and tone model: Copyright (c) 2026 Kubai087, MIT.
// Adaptation details and preserved license: THIRD-PARTY-NOTICES.md, licenses/glasswork-MIT.txt.
window.PrismGlassRenderer = function(canvas,post,subscribe){
const opticalPost=message=>window.chrome?.webview?.postMessage(message);
const gl=canvas.getContext('webgl',{alpha:true,premultipliedAlpha:true,antialias:false,powerPreference:'default',preserveDrawingBuffer:true});
let width=2560,height=1440,sceneWidth=2560,sceneHeight=1440,originX=0,originY=0,cards=[],theme='Sky',paused=false,material=1,glassStyle='',loaded=false,raf=0,last=0,frames=0;
let contextLost=false,recoveryTimer=0,wallpaperVersion=0,wallpaperRequest=0,wallpaperLoadedVersion=null,wallpaperLoading=false,sceneDirty=false;
let liveRequested=false;
let glassOverrides={},glassPresets=[];const materialCache=new Map();
function resolvedMaterial(key){const catalog=window.PrismGlassMaterials;if(!catalog||!catalog.resolve)return null;key=key||'I';if(!materialCache.has(key))materialCache.set(key,catalog.resolve(key,glassOverrides,glassPresets));return materialCache.get(key);}
const opticalTextures=new Map();let opticalLayout={active:false,sources:[]},opticalWasEnabled=false,sceneAllocatedWidth=0,sceneAllocatedHeight=0;
let mouse={x:-999,y:-999,down:false},leans=new Map();
const outlineKeys=new Map();
const surfaces=new Map();let surfaceCards=[];
const surfaceResize=new ResizeObserver(()=>wake());
function surfaceViewport(node,bounds,sx,sy,ancestors){
 let left=bounds.left,top=bounds.top,right=bounds.left+bounds.width,bottom=bounds.top+bounds.height;
 for(let parent=node.parentElement;parent;parent=parent.parentElement){
  if(!ancestors.has(parent)){
   const style=getComputedStyle(parent),clipX=/^(auto|scroll|hidden|clip)$/.test(style.overflowX||style.overflow),clipY=/^(auto|scroll|hidden|clip)$/.test(style.overflowY||style.overflow);
   let clip=null;
   if(clipX||clipY){const r=parent.getBoundingClientRect(),scaleX=r.width/(parent.offsetWidth||r.width||1),scaleY=r.height/(parent.offsetHeight||r.height||1),x=r.left+parent.clientLeft*scaleX,y=r.top+parent.clientTop*scaleY;clip={clipX,clipY,left:x,top:y,right:x+parent.clientWidth*scaleX,bottom:y+parent.clientHeight*scaleY};}
   ancestors.set(parent,clip);
  }
  const clip=ancestors.get(parent);if(!clip)continue;
  if(clip.clipX){left=Math.max(left,clip.left);right=Math.min(right,clip.right);}
  if(clip.clipY){top=Math.max(top,clip.top);bottom=Math.min(bottom,clip.bottom);}
 }
 return{left:(left-bounds.left)*sx,top:(top-bounds.top)*sy,right:(right-bounds.left)*sx,bottom:(bottom-bounds.top)*sy};
}
function measureSurfaces(){
 const bounds=canvas.getBoundingClientRect(),next=[];
 if(!bounds.width||!bounds.height)return next;
 const sx=width/bounds.width,sy=height/bounds.height,ancestors=new Map();
 for(const [node,surface] of surfaces)if(!node.isConnected||!node.hasAttribute('data-glass-surface')){surfaceResize.unobserve(node);surface.layer.remove();surfaces.delete(node);leans.delete(surface.name);}
 for(const node of document.querySelectorAll('[data-glass-surface]')){
  const rect=node.getBoundingClientRect();if(!rect.width||!rect.height)continue;
  let surface=surfaces.get(node);
  if(!surface){const layer=document.createElement('canvas');layer.className='prism-surface-layer';layer.setAttribute('aria-hidden','true');layer.style.cssText='position:absolute;pointer-events:none;z-index:0;';node.before(layer);surface={layer,context:layer.getContext('2d'),name:'Surface:'+node.dataset.glassSurface};surfaces.set(node,surface);surfaceResize.observe(node);}
  const parent=node.parentElement.getBoundingClientRect();Object.assign(surface.layer.style,{left:(rect.left-parent.left+node.parentElement.scrollLeft)+'px',top:(rect.top-parent.top+node.parentElement.scrollTop)+'px',width:rect.width+'px',height:rect.height+'px',borderRadius:getComputedStyle(node).borderRadius});
  surface.rect={x:(rect.left-bounds.left)*sx,y:(rect.top-bounds.top)*sy,w:rect.width*sx,h:rect.height*sy};
  surface.clip=surfaceViewport(node,bounds,sx,sy,ancestors);
  next.push({...surface.rect,clip:surface.clip,name:surface.name,r:(parseFloat(getComputedStyle(node).borderTopLeftRadius)||20)*sx,fixedShape:true,rimStrength:.8,surface:true,glassStyle:node.dataset.glassStyle});
 }
 return next;
}
function paintSurfaces(){
 for(const [node,surface] of surfaces){const r=surface.rect;if(!r||!node.isConnected)continue;const layer=surface.layer,w=Math.max(1,Math.round(r.w)),h=Math.max(1,Math.round(r.h));if(layer.width!==w)layer.width=w;if(layer.height!==h)layer.height=h;surface.context.clearRect(0,0,w,h);
  const clip=surface.clip,left=Math.max(r.x,Math.ceil(clip.left)),top=Math.max(r.y,Math.ceil(clip.top)),right=Math.min(r.x+r.w,Math.floor(clip.right)),bottom=Math.min(r.y+r.h,Math.floor(clip.bottom));
  if(right>left&&bottom>top)surface.context.drawImage(canvas,left,top,right-left,bottom-top,(left-r.x)*w/r.w,(top-r.y)*h/r.h,(right-left)*w/r.w,(bottom-top)*h/r.h);
  node.classList.add('glass-surface-ready');}
}
// Popovers share this WebGL scene; their DOM layers only copy the finished surface.
const surfaceChanges=new MutationObserver(()=>wake());surfaceChanges.observe(document.body,{childList:true,subtree:true,attributes:true,attributeFilter:['data-glass-style']});
function postOutline(type,outline){
 // Compare at a quarter physical pixel, but preserve the original clip points.
 const key=JSON.stringify(outline,(_,value)=>typeof value==='number'?Math.round(value*4)/4:value);
 if(outlineKeys.get(type)===key)return;
 outlineKeys.set(type,key);stats.outlineMessages++;post(type+':'+JSON.stringify(outline));
}
function randomEffect(){const value=new Uint32Array(1);crypto.getRandomValues(value);return value[0]/4294967296;}
const stats={ready:false,frames:0,outlineMessages:0,theme:'Sky',paused:false,errors:[],cards:0};
const vertex='attribute vec2 a;varying vec2 uv;void main(){uv=a*.5+.5;gl_Position=vec4(a,0.,1.);}';
const sceneFrag=`precision highp float;varying vec2 uv;uniform sampler2D wallpaper;uniform sampler2D liveWindow;uniform float liveLayer;uniform vec4 liveRect;uniform float astro;uniform vec2 size;uniform vec2 sceneSize;uniform vec2 origin;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
void main(){if(liveLayer>.5){vec2 q=(vec2(uv.x,1.-uv.y)*size-liveRect.xy)/liveRect.zw;if(q.x<0.||q.y<0.||q.x>1.||q.y>1.)discard;vec4 c=texture2D(liveWindow,q);gl_FragColor=c.bgra;return;}vec2 p=(origin+vec2(uv.x,1.-uv.y)*size)/sceneSize;vec3 c=texture2D(wallpaper,p).rgb;
 if(astro>.5){float light=exp(-length((p-vec2(.65,.84))*vec2(1.,2.))*3.);float gray=dot(c,vec3(.2126,.7152,.0722));c=mix(vec3(gray),c,.74);c=mix(c,vec3(.095,.105,.13),.22)+vec3(.025,.035,.06)*light;
 vec2 q=p*sceneSize/60.;vec2 cell=floor(q);float seed=hash(cell);vec2 star=vec2(hash(cell+2.),hash(cell+7.))*.8+.1;
 vec2 st=(fract(q)-star)*60.;float core=exp(-dot(st,st)/mix(.45,1.5,step(.96,seed)));float glow=exp(-dot(st,st)/12.)*.09;
 float rays=(exp(-abs(st.x)*3.-abs(st.y)*.65)+exp(-abs(st.y)*3.-abs(st.x)*.65))*.14*step(.985,seed);
 vec3 starColor=mix(vec3(.68,.84,1.),vec3(1.,.96,.82),step(.92,seed));c+=(core+glow+rays)*step(.78,seed)*starColor*mix(.65,1.15,step(.96,seed));}
 gl_FragColor=vec4(c,1.);}`;
const glassFrag=`precision highp float;varying vec2 uv;uniform sampler2D scene;uniform sampler2D wallpaper;uniform vec2 size;uniform vec2 sceneSize;uniform vec2 origin;uniform vec2 center;uniform vec2 halfSize;uniform float radius;uniform vec2 lean;uniform float astro;uniform float onlyBackground;uniform float optical;uniform float hover;uniform float press;uniform float fixedShape;uniform float rimStrength;uniform float opaqueSurface;uniform float material;uniform float surfaceOpacity;uniform float liquidAmplitude;uniform float liquidDirection;uniform float motionTime;uniform float auroraColor;uniform float effectSeed;uniform vec4 meteorHead[5];uniform vec2 meteorDir[5];
uniform float styleEnabled;uniform vec4 styleShape;uniform vec4 styleBody;uniform vec4 styleTint;uniform vec4 styleLight;uniform vec4 styleTone;uniform vec4 styleVeil;uniform vec2 styleExtra;
float sd(vec2 p,vec2 b,float r){r=clamp(r,0.,max(0.,min(b.x,b.y)));vec2 q=abs(p)-b+r;return length(max(q,0.))+min(max(q.x,q.y),0.)-r;}
float liquidProfile(float nx){float a=min(abs(nx),1.);float middle=exp(-a*a*6.);float shoulders=exp(-pow((a-.55)*3.,2.));return mix(.8*shoulders-.25*middle,.9*middle-.3*pow(a,1.4),(liquidDirection+1.)*.5);}
vec3 materialSample(vec2 at){return texture2D(scene,clamp(at,vec2(0.),vec2(1.))).rgb;}
vec3 materialSplit(vec2 red,vec2 green,vec2 blue){return vec3(materialSample(red).r,materialSample(green).g,materialSample(blue).b);}
vec3 selectedMaterial(vec2 d,vec2 grad,float depth){
 bool glasswork=styleExtra.y>.5;
 float bevel=min(styleShape.x,max(2.,min(halfSize.x,halfSize.y)*(glasswork?2./3.5:.76)));
 float t=clamp(depth/bevel,0.,1.),arc=sqrt(max(1.-(1.-t)*(1.-t),0.));
 float edge=1.-smoothstep(0.,bevel,depth);
 vec3 normal=normalize(vec3(-grad*.74*(1.-t)/max(arc,.17),1.));
 vec2 axis=vec2(1.,-1.)/size;
 // Shared with the style lab: leave the body registered; only the bevel follows the pointer.
 vec2 toCursor=lean*halfSize-d;float focus=exp(-dot(toCursor,toCursor)/7500.);
 vec2 drift=lean*edge*edge*hover*1.4;
 vec2 red,green,blue;
 if(glasswork){
  // Glasswork refract.js: inward edge * rim * (1 - depth / rim)^power; red bends further.
  float amount=styleTone.z*bevel*pow(1.-t,styleTone.w)*styleShape.y*(styleShape.z-1.)/.46;
  vec2 inward=-grad*amount*(1.+hover*.10-press*.12);
  red=uv+(inward*(1.+styleShape.w)+drift)*axis;green=uv+(inward+drift)*axis;blue=uv+(inward*(1.-styleShape.w)+drift)*axis;
 }else{
  float path=bevel*(.72+1.18*arc)*styleShape.y*(1.+hover*.10-press*.12)*styleTone.z*pow(max(1.-t,.001),styleTone.w/2.6-1.);
  vec3 rayR=refract(vec3(0.,0.,-1.),normal,1./max(1.05,styleShape.z-styleShape.w));vec3 rayG=refract(vec3(0.,0.,-1.),normal,1./styleShape.z);vec3 rayB=refract(vec3(0.,0.,-1.),normal,1./(styleShape.z+styleShape.w));
  red=uv+(rayR.xy/max(-rayR.z,.3)*path+drift)*axis;green=uv+(rayG.xy/max(-rayG.z,.3)*path+drift)*axis;blue=uv+(rayB.xy/max(-rayB.z,.3)*path+drift)*axis;
 }
 vec3 c=materialSplit(red,green,blue);
 if(styleBody.x>.01&&styleBody.y>.001){
 if(glasswork){
  vec2 step=axis*styleBody.x;vec3 soft=c*4.;
  soft+=(materialSplit(red+vec2(step.x,0.),green+vec2(step.x,0.),blue+vec2(step.x,0.))+materialSplit(red-vec2(step.x,0.),green-vec2(step.x,0.),blue-vec2(step.x,0.))+materialSplit(red+vec2(0.,step.y),green+vec2(0.,step.y),blue+vec2(0.,step.y))+materialSplit(red-vec2(0.,step.y),green-vec2(0.,step.y),blue-vec2(0.,step.y)))*2.;
  c=mix(c,soft/12.,styleBody.y);
 }else{
  vec2 step=axis*styleBody.x*mix(1.,.22,edge);vec3 soft=materialSample(green)*4.;
  soft+=(materialSample(green+vec2(step.x,0.))+materialSample(green-vec2(step.x,0.))+materialSample(green+vec2(0.,step.y))+materialSample(green-vec2(0.,step.y)))*2.;
  soft+=materialSample(green+step)+materialSample(green-step)+materialSample(green+vec2(step.x,-step.y))+materialSample(green+vec2(-step.x,step.y));
  c=mix(c,soft/16.,styleBody.y*mix(1.,.25,edge));
 }
 }
 vec3 luma=vec3(.2126,.7152,.0722);float luminance=dot(c,luma);c=mix(vec3(luminance),c,styleBody.z);c=((c-.5)*styleTone.y+.5)*styleTone.x;
 float tint=styleTint.a;
 if(styleVeil.z>.001){float target=.5;if(styleVeil.y>.001){float l=dot(materialSample(uv),luma),a=dot(materialSample(uv+axis*vec2(12.,8.)),luma),b=dot(materialSample(uv-axis*vec2(12.,8.)),luma);float activity=clamp(abs(a-b)*2.4+abs(l-(a+b)*.5)*1.3+(1.-l)*.12,0.,1.);target=mix(.5,activity,styleVeil.y);}tint+=mix(styleVeil.w,styleExtra.x,target)*styleVeil.z;}
 c=mix(c,styleTint.rgb,clamp(tint,0.,1.));
 float fresnel=.04+.96*pow(1.-normal.z,5.);vec3 reflected=materialSample(uv+grad*axis*bevel*1.6);
 c=mix(c,reflected,fresnel*styleLight.x*edge);
 float key=pow(max(dot(grad,normalize(vec2(-.65,-.85)+lean*hover*.16)),0.),3.);
 float fineRim=exp(-pow((depth-.65)/.65,2.));c+=max(vec3(0.),vec3(1.)-c)*fineRim*styleLight.y*(.15+.85*key);
 c-=edge*edge*(1.-key)*styleLight.z*.11;c+=max(vec3(0.),reflected-c)*focus*edge*hover*.19;
 c*=1.-press*edge*.025;
 if(styleBody.w>.001)c+=(fract(sin(dot(floor(d+center),vec2(127.1,311.7)))*43758.5453)-.5)*styleBody.w*(1.-edge*.8);
 return clamp(c,0.,1.);
}
void main(){vec2 p=vec2(uv.x,1.-uv.y)*size;vec3 original=optical>.5?texture2D(scene,uv).rgb:texture2D(wallpaper,(origin+vec2(uv.x,1.-uv.y)*size)/sceneSize).rgb;
 if(onlyBackground>.5){gl_FragColor=vec4(0.);return;}
 float motionScale=min(1.,halfSize.y/70.)*(1.-fixedShape);vec2 d=p-center-lean*vec2(5.,4.)*hover*motionScale;
 d/=vec2(1.-(hover*.045+press*.025)*(1.-fixedShape),1.+(hover*.035-press*.065)*(1.-fixedShape));
 d.x+=sin(d.y/max(halfSize.y,1.)*1.5)*lean.x*15.*motionScale;d.y+=sin(d.x/max(halfSize.x,1.)*1.4)*lean.y*11.*motionScale;
 if(liquidAmplitude>.001)d.y=(d.y-liquidAmplitude)/(1.+liquidAmplitude/max(halfSize.y,1.)*liquidProfile(d.x/max(halfSize.x,1.)));
 vec2 glassSize=max(vec2(.01),halfSize-vec2(2.)+vec2((hover*1.5-press*2.5)*(1.-fixedShape)));float dist=sd(d,glassSize,radius);if(dist>8.)discard;vec3 result=vec3(0.);float coverage=1.-smoothstep(-.8,.8,dist);
 if(dist<1.){float e=.7;vec2 grad=normalize(vec2(sd(d+vec2(e,0.),glassSize,radius)-sd(d-vec2(e,0.),glassSize,radius),sd(d+vec2(0.,e),glassSize,radius)-sd(d-vec2(0.,e),glassSize,radius))+.0001);
 // Circular thickness/bevel and Snell/Fresnel model adapted from Oliver Nemo's
 // MIT webgl-apple-liquid-glass; see THIRD-PARTY-NOTICES and preserved license.
 float depth=max(-dist,0.);float bevel=min(30.,max(10.,halfSize.y*.46));float rim=exp(-depth/bevel);
 float t=clamp(depth/bevel,0.,1.);float arc=sqrt(max(1.-(1.-t)*(1.-t),0.));
 float slope=.72*(1.-t)/max(arc,.16);vec3 normal=normalize(vec3(-grad*slope,1.));
 vec2 localCursor=lean*halfSize*.82;vec2 toCursor=d-localCursor;float focus=exp(-dot(toCursor,toCursor)/4800.);
 vec3 c;float bright=0.;
 if(styleEnabled>.5)c=selectedMaterial(d,grad,depth);
 else{
 float opticalPath=bevel*(.8+1.15*arc)*(1.+hover*.16);
 vec3 rayR=refract(vec3(0.,0.,-1.),normal,1./(1.46-.065));
 vec3 rayG=refract(vec3(0.,0.,-1.),normal,1./1.46);
 vec3 rayB=refract(vec3(0.,0.,-1.),normal,1./(1.46+.065));
 // Anchor the body to the scene; pointer refraction belongs to the bevel.
 vec2 drift=d*.008+(lean*12.+toCursor*focus*hover*.12)*rim*rim;
 vec2 bendR=rayR.xy/max(-rayR.z,.3)*opticalPath+drift;
 vec2 bendG=rayG.xy/max(-rayG.z,.3)*opticalPath+drift;
 vec2 bendB=rayB.xy/max(-rayB.z,.3)*opticalPath+drift;
 vec2 axis=vec2(1.,-1.)/size;float opticalStrength=mix(.78,1.,rimStrength);
 vec2 at=uv+bendG*axis*opticalStrength;
 c=vec3(texture2D(scene,uv+bendR*axis*opticalStrength).r,texture2D(scene,at).g,texture2D(scene,uv+bendB*axis*opticalStrength).b);
 // The body diffuses background detail; the bevel keeps a sharper refracted image.
 vec2 footprint=axis*mix(2.2,.65,rim);vec3 diffusion=texture2D(scene,at).rgb*4.;
 diffusion+=(texture2D(scene,at+vec2(footprint.x,0.)).rgb+texture2D(scene,at-vec2(footprint.x,0.)).rgb+texture2D(scene,at+vec2(0.,footprint.y)).rgb+texture2D(scene,at-vec2(0.,footprint.y)).rgb)*2.;
 diffusion+=texture2D(scene,at+footprint).rgb+texture2D(scene,at-footprint).rgb;
 diffusion+=texture2D(scene,at+vec2(footprint.x,-footprint.y)).rgb+texture2D(scene,at+vec2(-footprint.x,footprint.y)).rgb;c=mix(c,diffusion/16.,mix(.5,.18,rim));
 float luminance=dot(c,vec3(.2126,.7152,.0722));c=mix(vec3(luminance),c,mix(1.16,.86,astro));
 bright=smoothstep(.35,.88,luminance);float dark=1.-smoothstep(.04,.3,luminance);
 // Neutral backgrounds pick up a little surrounding color instead of a flat gray veil.
 vec3 ambient=texture2D(wallpaper,(origin+center)/sceneSize).rgb;float chroma=max(c.r,max(c.g,c.b))-min(c.r,min(c.g,c.b));
 c=mix(c,ambient*luminance/max(dot(ambient,vec3(.2126,.7152,.0722)),.12),(1.-smoothstep(.05,.22,chroma))*.08);
 // Clear glass deliberately favors transmitted light over a uniform text-contrast veil.
 c*=mix(1.,mix(.86,.94,astro),bright);
 c+=vec3(.012,.018,.027)*dark;
 float fresnel=.04+.96*pow(1.-normal.z,5.);
 vec3 reflection=texture2D(scene,uv+grad*axis*bevel*2.).rgb;
 c=mix(c,reflection*.65+vec3(.28,.32,.36),fresnel*.38*rim);
 }
 if(astro>.5&&hover>.003){
  vec2 a=(d+halfSize)/(halfSize*2.);float t=motionTime+effectSeed;
  float wave=.57+sin(a.x*5.5+t*.38+lean.x*.6)*.16+sin(a.x*12.-t*.28)*.055;
  float above=wave-a.y;
  float curtain=exp(-max(above,0.)*6.)*smoothstep(-.035,.015,above);
  float filaments=.38+.62*pow(.5+.5*sin(a.x*73.+sin(a.x*16.-t*.6)*3.+t*.4),2.);
  float hem=exp(-pow(above*38.,2.));
  float veil=(curtain*filaments*.34+hem*.28)*(.65+.35*sin(a.x*3.+t*.25));
  c+=hover*(auroraColor<.5?vec3(.10,.85,.49):vec3(.95,.13,.28))*veil;
  for(int i=0;i<5;i++){
   if(meteorHead[i].w>.001){
    vec2 dir=meteorDir[i],rel=d-meteorHead[i].xy;float trail=dot(-rel,dir),crossing=dot(rel,vec2(-dir.y,dir.x));
    float streak=exp(-crossing*crossing/2.)*smoothstep(0.,4.,trail)*(1.-smoothstep(10.,meteorHead[i].z,trail));
    float spark=exp(-dot(rel,rel)/4.5);
    c+=(streak*.72+spark)*meteorHead[i].w*hover*vec3(.66,.87,1.);
   }
  }
 }
 if(styleEnabled<.5){
 float light=pow(max(dot(grad,normalize(vec2(-.65,-.85)+lean*.25)),0.),4.);
 float specular=pow(max(dot(normal,normalize(vec3(.4,.6,1.))),0.),32.);
 c+=exp(-pow((depth-.9)/.48,2.))*(.10+.44*light)*(1.-bright*.35)*mix(.5,1.,rimStrength)+specular*rim*.16;
 c+=focus*hover*(astro>.5?.075:.13)+rim*hover*.07;c-=rim*(1.-light)*.045;
 }
 vec2 edgeCursor=lean/max(max(abs(lean.x),abs(lean.y)),.05)*halfSize;
 vec2 edgeDelta=d-edgeCursor;
 float following=exp(-dot(edgeDelta,edgeDelta)/20000.);
 float flow=.5+.5*sin((d.x+d.y)*.021-motionTime*1.4);
 vec3 flowingColor=mix(mix(vec3(.20,.88,1.),vec3(.80,.43,1.),flow),vec3(1.),astro);
 float edgeBand=exp(-pow((depth-2.)/1.7,2.));
 c+=flowingColor*following*hover*(edgeBand*.65+exp(-depth/7.)*.12);
 result=mix(original,c,material*(styleEnabled>.5?styleVeil.x:1.));}
 if(opaqueSurface>.5){result=mix(original,result,coverage);coverage=1.;}
 gl_FragColor=vec4(result*coverage,coverage)*surfaceOpacity;}`;
function fail(e){stats.errors.push(String(e));post('error:'+e);}
if(!gl){fail('WebGL unavailable');return;}
function program(src){function shader(t,s){const sh=gl.createShader(t);gl.shaderSource(sh,s);gl.compileShader(sh);if(!gl.getShaderParameter(sh,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(sh));return sh;}const p=gl.createProgram(),v=shader(gl.VERTEX_SHADER,vertex),f=shader(gl.FRAGMENT_SHADER,src);gl.attachShader(p,v);gl.attachShader(p,f);gl.linkProgram(p);if(!gl.getProgramParameter(p,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(p));gl.deleteShader(v);gl.deleteShader(f);return p;}
let sceneProgram,glassProgram,buffer,wallpaper,scene,fbo,dockScene=null,dockSceneWidth=0,dockSceneHeight=0;const cache=new Map();
function releaseDockScene(){if(dockScene&&!gl.isContextLost())gl.deleteTexture(dockScene);dockScene=null;dockSceneWidth=dockSceneHeight=0;stats.dockSceneActive=false;}
function initialize(){dockScene=null;dockSceneWidth=dockSceneHeight=0;sceneAllocatedWidth=sceneAllocatedHeight=0;for(const source of opticalTextures.values())source.texture=null;sceneProgram=program(sceneFrag);glassProgram=program(glassFrag);buffer=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,buffer);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array([-1,-1,1,-1,-1,1,-1,1,1,-1,1,1]),gl.STATIC_DRAW);wallpaper=texture();scene=texture();fbo=gl.createFramebuffer();cache.clear();outlineKeys.clear();}
function texture(){const t=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,t);for(const key of [gl.TEXTURE_MIN_FILTER,gl.TEXTURE_MAG_FILTER])gl.texParameteri(gl.TEXTURE_2D,key,gl.LINEAR);for(const key of [gl.TEXTURE_WRAP_S,gl.TEXTURE_WRAP_T])gl.texParameteri(gl.TEXTURE_2D,key,gl.CLAMP_TO_EDGE);return t;}
try{initialize();}catch(e){fail(e);return;}
function loc(p,n){const k=(p===sceneProgram?'s':'g')+n;if(!cache.has(k))cache.set(k,gl.getUniformLocation(p,n));return cache.get(k);}
function one(p,n,v){gl.uniform1f(loc(p,n),v);}function two(p,n,x,y){gl.uniform2f(loc(p,n),x,y);}
function use(p){gl.useProgram(p);const a=gl.getAttribLocation(p,'a');gl.enableVertexAttribArray(a);gl.vertexAttribPointer(a,2,gl.FLOAT,false,0,0);}
function bind(p,n,t,slot){gl.activeTexture(gl.TEXTURE0+slot);gl.bindTexture(gl.TEXTURE_2D,t);gl.uniform1i(loc(p,n),slot);}
function opticalEnabled(){return opticalLayout.active&&liveRequested&&!paused&&!contextLost;}
function buildScene(){
 sceneDirty=true;wake();
}
function composeScene(){
 if(!loaded||gl.isContextLost())return;
 if(canvas.width!==width)canvas.width=width;if(canvas.height!==height)canvas.height=height;
 gl.viewport(0,0,width,height);gl.disable(gl.SCISSOR_TEST);gl.disable(gl.BLEND);gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,scene);
 if(sceneAllocatedWidth!==width||sceneAllocatedHeight!==height){gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,width,height,0,gl.RGBA,gl.UNSIGNED_BYTE,null);sceneAllocatedWidth=width;sceneAllocatedHeight=height;}
 gl.bindFramebuffer(gl.FRAMEBUFFER,fbo);gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT0,gl.TEXTURE_2D,scene,0);
 if(gl.checkFramebufferStatus(gl.FRAMEBUFFER)!==gl.FRAMEBUFFER_COMPLETE){if(!gl.isContextLost())fail('incomplete framebuffer');return;}
 use(sceneProgram);bind(sceneProgram,'wallpaper',wallpaper,0);two(sceneProgram,'size',width,height);two(sceneProgram,'sceneSize',sceneWidth,sceneHeight);two(sceneProgram,'origin',originX,originY);one(sceneProgram,'astro',theme==='Astro'?1:0);one(sceneProgram,'liveLayer',0);gl.drawArrays(gl.TRIANGLES,0,6);
 if(opticalEnabled()){
  gl.enable(gl.BLEND);gl.blendFunc(gl.ONE,gl.ONE_MINUS_SRC_ALPHA);one(sceneProgram,'liveLayer',1);
  const sx=width/opticalLayout.width,sy=height/opticalLayout.height;
  const layers=[...opticalLayout.sources].reverse();
  const drawLayer=layer=>{const source=opticalTextures.get(layer.id);if(!source?.texture)return;bind(sceneProgram,'liveWindow',source.texture,1);gl.uniform4f(loc(sceneProgram,'liveRect'),layer.x*sx,layer.y*sy,layer.w*sx,layer.h*sy);gl.drawArrays(gl.TRIANGLES,0,6);};
  for(const layer of layers)if(layer.target!=='Dock')drawLayer(layer);
  if(layers.some(layer=>layer.target==='Dock')){
   // Copy the base using the existing FBO. Only the Dock samples this texture;
   // its bevel can read beyond the card bounds without leaking into widgets.
   if(!dockScene)dockScene=texture();gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,dockScene);
   if(dockSceneWidth!==width||dockSceneHeight!==height){gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,width,height,0,gl.RGBA,gl.UNSIGNED_BYTE,null);dockSceneWidth=width;dockSceneHeight=height;}
   gl.copyTexSubImage2D(gl.TEXTURE_2D,0,0,0,0,0,width,height);gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT0,gl.TEXTURE_2D,dockScene,0);bind(sceneProgram,'wallpaper',wallpaper,0);
   if(gl.checkFramebufferStatus(gl.FRAMEBUFFER)!==gl.FRAMEBUFFER_COMPLETE){releaseDockScene();if(!gl.isContextLost())fail('incomplete Dock framebuffer');return;}
   for(const layer of layers)if(layer.target==='Dock')drawLayer(layer);stats.dockSceneActive=true;
  }else releaseDockScene();
  gl.disable(gl.BLEND);
 }else releaseDockScene();
 gl.bindFramebuffer(gl.FRAMEBUFFER,null);sceneDirty=false;stats.sceneBuilds=(stats.sceneBuilds||0)+1;
}
function releaseOptical(id){const source=opticalTextures.get(id);if(!source)return;if(source.texture&&!gl.isContextLost())gl.deleteTexture(source.texture);if(source.buffer)window.chrome.webview.releaseBuffer(source.buffer);opticalTextures.delete(id);}
function opticalFrame(data,buffer){
 try{
  if(data.type!=='optical-frame')return;let source=opticalTextures.get(data.id);
  if(buffer&&(!liveRequested||paused||contextLost)){window.chrome.webview.releaseBuffer(buffer);return;}
  if(buffer){releaseOptical(data.id);source={buffer,width:data.width,height:data.height,texture:null};opticalTextures.set(data.id,source);}
  if(!source||!opticalEnabled())return;const length=source.width*source.height*4;
  if(source.width<1||source.height<1||length>67108864||source.buffer.byteLength<length)throw Error('invalid optical frame');
  gl.activeTexture(gl.TEXTURE0);const first=!source.texture;if(first)source.texture=texture();gl.bindTexture(gl.TEXTURE_2D,source.texture);
  const pixels=new Uint8Array(source.buffer,0,length);if(first)gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,source.width,source.height,0,gl.RGBA,gl.UNSIGNED_BYTE,pixels);else gl.texSubImage2D(gl.TEXTURE_2D,0,0,0,source.width,source.height,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
  stats.liveBackground='optical';stats.opticalFrames=(stats.opticalFrames||0)+1;buildScene();
 }finally{if(data.serial)opticalPost('optical-ack:'+data.serial);}
}
function opticalMessage(data){
 if(data.type==='optical-frame'){opticalFrame(data);return;}
 if(data.type!=='optical-layout')return;opticalLayout=data;
 const ids=new Set(data.active?data.sources.map(s=>s.id):[]);for(const id of opticalTextures.keys())if(!ids.has(id))releaseOptical(id);
 syncLive();buildScene();
}
function liquidShape(c){return{amplitude:Math.max(0,Math.min(c.h*.32,18,(height-c.y-c.h-5)/1.9))*Math.max(0,Math.min(1,c.motionFluid||0)),direction:Math.max(-1,Math.min(1,c.flowY||0))};}
function liquidProfile(nx,direction){const a=Math.min(Math.abs(nx),1),middle=Math.exp(-a*a*6),shoulders=Math.exp(-Math.pow((a-.55)*3,2)),t=(direction+1)*.5;return(.8*shoulders-.25*middle)*(1-t)+(.9*middle-.3*Math.pow(a,1.4))*t;}
function dockOutline(c,v){
 const weight=1-(c.name==='TopIsland'&&v.lock!==undefined?v.lock:c.fixedShape?1:0),h=(v.h||0)*weight,p=(v.p||0)*weight,m=Math.min(1,c.h/140)*weight,bx=Math.max(.01,c.w/2-2+h*1.5-p*2.5),by=Math.max(.01,c.h/2-2+h*1.5-p*2.5),r=Math.max(0,Math.min(c.r,bx,by)),points=[];
 const liquid=liquidShape(c);
 const corners=[[-bx+r,-by+r,Math.PI],[bx-r,-by+r,Math.PI*1.5],[bx-r,by-r,0],[-bx+r,by-r,Math.PI*.5]];
 function point(dx,dy){
  const flowingY=dy*(1+liquid.amplitude/Math.max(c.h/2,1)*liquidProfile(dx/Math.max(c.w/2,1),liquid.direction))+liquid.amplitude;
  const ay=flowingY-Math.sin(dx/(c.w/2)*1.4)*v.y*11*m;
  const ax=dx-Math.sin(ay/(c.h/2)*1.5)*v.x*15*m;
  points.push([26+(c.clipOffsetX||0)+c.w/2+v.x*5*(v.h||0)*m+ax*(1-h*.045-p*.025),26+(c.clipOffsetY||0)+(c.insetY||0)+c.h/2+v.y*4*(v.h||0)*m+ay*(1+h*.035-p*.065)]);
 }
 for(let corner=0;corner<4;corner++){
  const [cx,cy,start]=corners[corner];
  for(let i=0;i<=24;i++){const angle=start+i*Math.PI/48;point(cx+Math.cos(angle)*r,cy+Math.sin(angle)*r);}
  // The native clip must follow the wave along long edges, not join only its corners.
  if(liquid.amplitude>.001&&(corner===0||corner===2)){
   const next=corners[(corner+1)%4],fromX=cx+Math.cos(start+Math.PI/2)*r,fromY=cy+Math.sin(start+Math.PI/2)*r,toX=next[0]+Math.cos(next[2])*r;
   for(let i=1;i<32;i++)point(fromX+(toX-fromX)*i/32,fromY);
  }
 }
 if(c.name==='Dock')postOutline('dock-shape',points);
 return points;
}
function drawCard(c,lean){
 bind(glassProgram,'scene',c.name==='Dock'&&dockScene&&opticalEnabled()?dockScene:scene,0);
 one(glassProgram,'optical',opticalEnabled()?1:0);
 const preset=resolvedMaterial(c.glassStyle===undefined?glassStyle:c.glassStyle),selected=preset&&!preset.legacy;
 one(glassProgram,'styleEnabled',selected?1:0);
 if(selected){gl.uniform4f(loc(glassProgram,'styleShape'),preset.bevel,preset.refraction,preset.ior,preset.dispersion);gl.uniform4f(loc(glassProgram,'styleBody'),preset.blur,preset.diffusion,preset.saturation,preset.grain);gl.uniform4fv(loc(glassProgram,'styleTint'),preset.tint);gl.uniform4f(loc(glassProgram,'styleLight'),preset.reflection,preset.highlight,preset.shadow,0);gl.uniform4f(loc(glassProgram,'styleTone'),preset.brightness,preset.contrast,preset.edge,preset.power);gl.uniform4f(loc(glassProgram,'styleVeil'),preset.opacity,preset.adaptive,preset.density,preset.tintMin);two(glassProgram,'styleExtra',preset.tintMax,preset.model==='glasswork'?1:0);}
 const now=performance.now()/1000,heads=new Float32Array(20),dirs=new Float32Array(10);
 const liquid=liquidShape(c);one(glassProgram,'liquidAmplitude',liquid.amplitude);one(glassProgram,'liquidDirection',liquid.direction);
 for(const [i,m] of (lean.meteors||[]).slice(0,5).entries()){
  const age=now-m.start;heads.set([m.x+age*m.speed*m.dx,m.y+age*m.speed*m.dy,m.tail,Math.min(1,age*8)*Math.min(1,Math.max(0,m.life-age)*3)],i*4);dirs.set([m.dx,m.dy],i*2);
 }
 gl.uniform4fv(loc(glassProgram,'meteorHead[0]'),heads);gl.uniform2fv(loc(glassProgram,'meteorDir[0]'),dirs);one(glassProgram,'auroraColor',lean.auroraColor||0);one(glassProgram,'effectSeed',lean.effectSeed||0);one(glassProgram,'motionTime',now);one(glassProgram,'rimStrength',c.rimStrength===undefined?1:c.rimStrength);one(glassProgram,'opaqueSurface',c.name==='NativePopup'?1:0);one(glassProgram,'fixedShape',c.name==='TopIsland'&&lean.lock!==undefined?lean.lock:c.fixedShape?1:0);two(glassProgram,'center',c.x+c.w/2,c.y+c.h/2);two(glassProgram,'halfSize',c.w/2,c.h/2);two(glassProgram,'lean',lean.x,lean.y);one(glassProgram,'radius',c.r);one(glassProgram,'surfaceOpacity',c.opacity===undefined?1:Math.max(0,Math.min(1,c.opacity)));one(glassProgram,'hover',lean.h||0);one(glassProgram,'press',lean.p||0);
 const pad=26+Math.ceil(liquid.amplitude*2),clip=c.clip||{left:0,top:0,right:width,bottom:height},left=Math.max(0,Math.floor(c.x-pad),Math.ceil(clip.left)),top=Math.max(0,Math.floor(c.y-pad),Math.ceil(clip.top)),right=Math.min(width,Math.ceil(c.x+c.w+pad),Math.floor(clip.right)),bottom=Math.min(height,Math.ceil(c.y+c.h+pad),Math.floor(clip.bottom));
 gl.scissor(left,height-bottom,Math.max(0,right-left),Math.max(0,bottom-top));gl.drawArrays(gl.TRIANGLES,0,6);if(c.name==='Dock')dockOutline(c,lean);}
function render(){if(!loaded||gl.isContextLost())return;if(sceneDirty)composeScene();gl.viewport(0,0,width,height);use(glassProgram);bind(glassProgram,'scene',scene,0);bind(glassProgram,'wallpaper',wallpaper,1);two(glassProgram,'size',width,height);two(glassProgram,'sceneSize',sceneWidth,sceneHeight);two(glassProgram,'origin',originX,originY);one(glassProgram,'astro',theme==='Astro'?1:0);one(glassProgram,'material',material);one(glassProgram,'onlyBackground',1);gl.disable(gl.BLEND);gl.disable(gl.SCISSOR_TEST);gl.drawArrays(gl.TRIANGLES,0,6);one(glassProgram,'onlyBackground',0);gl.enable(gl.BLEND);gl.blendFunc(gl.ONE,gl.ONE_MINUS_SRC_ALPHA);gl.enable(gl.SCISSOR_TEST);

 surfaceCards=measureSurfaces();const outlines=[];
 for(const c of cards.concat(surfaceCards)){const v=leans.get(c.name)||{x:0,y:0};drawCard(c,v);if(c.name!=='Dock'&&!c.surface)outlines.push(dockOutline(c,v).map(p=>[p[0]+c.x-26,p[1]+c.y-26]));}
 postOutline('widget-shapes',outlines);
 paintSurfaces();stats.surfaces=surfaceCards.length;
 gl.disable(gl.SCISSOR_TEST);gl.disable(gl.BLEND);frames++;stats.frames=frames;stats.theme=theme;stats.paused=paused;stats.cards=cards.length;stats.canvasWidth=width;stats.canvasHeight=height;stats.sceneWidth=sceneWidth;stats.sceneHeight=sceneHeight;stats.activeCards=cards.filter(c=>(leans.get(c.name)?.h||0)>.05).map(c=>c.name);stats.maxDeformation=Math.max(0,...[...leans.values()].map(v=>Math.abs(v.x)*20+Math.abs(v.y)*15+v.h*8));if(!stats.ready)post('painted');stats.ready=true;}
function frame(now){raf=0;if(!loaded)return;const dt=Math.max(.001,Math.min((now-last)/1000||.016,.034));last=now;let moving=false;
 for(const c of cards.concat(surfaceCards)){let v=leans.get(c.name);if(!v){v={x:0,y:0,h:0,p:0,vx:0,vy:0};leans.set(c.name,v);}const inside=!paused&&mouse.x>=c.x&&mouse.x<=c.x+c.w&&mouse.y>=c.y&&mouse.y<=c.y+c.h;const tx=inside?(mouse.x-c.x-c.w/2)/(c.w/2):0,ty=inside?(mouse.y-c.y-c.h/2)/(c.h/2):0;const th=inside?1:0,tp=inside&&mouse.down?1:0;
 const seconds=now/1000;
 if(!v.meteors)v.meteors=[];
 v.meteors=v.meteors.filter(m=>seconds-m.start<m.life);
 if(inside&&!v.inside){
  v.auroraColor=randomEffect()<.5?0:1;v.effectSeed=randomEffect()*100;
  v.nextMeteor=seconds+.05+Math.random()*.3;
 }
 if(theme==='Astro'&&inside&&seconds>=(v.nextMeteor||0)){
  if(v.meteors.length<5){
   const angle=.15+Math.random()*.48,speed=190+Math.random()*300;
   v.meteors.push({start:seconds,x:-c.w/2-30+Math.random()*c.w*.75,y:-c.h/2-12+Math.random()*c.h*.7,dx:Math.cos(angle),dy:Math.sin(angle),speed,tail:55+Math.random()*80,life:.55+Math.random()*1.15});
  }
  v.nextMeteor=seconds+.16+Math.random()*.72;
 }
 v.inside=inside;
 if(c.name==='TopIsland'){
  // Direct manipulation should settle once; keep the shared atmospheric effects.
  const omega=18,e=Math.exp(-omega*dt),dx=v.x-tx,dy=v.y-ty,jx=v.vx+omega*dx,jy=v.vy+omega*dy;
  v.x=tx+(dx+jx*dt)*e;v.y=ty+(dy+jy*dt)*e;v.vx=(v.vx-omega*jx*dt)*e;v.vy=(v.vy-omega*jy*dt)*e;
  const lock=c.fixedShape?1:0;if(v.lock===undefined)v.lock=lock;v.lock+=(lock-v.lock)*(1-e);if(Math.abs(lock-v.lock)>.002)moving=true;else v.lock=lock;
 }else{
  // Solve the existing damped spring analytically so refresh rate does not change its motion.
  const e=Math.exp(-6.5*dt),w=Math.sqrt(107.75),cs=Math.cos(w*dt),sn=Math.sin(w*dt)/w,dx=v.x-tx,dy=v.y-ty;
  v.x=tx+(dx*cs+(v.vx+6.5*dx)*sn)*e;v.y=ty+(dy*cs+(v.vy+6.5*dy)*sn)*e;
  v.vx=(v.vx*cs-(6.5*v.vx+150*dx)*sn)*e;v.vy=(v.vy*cs-(6.5*v.vy+150*dy)*sn)*e;
 }
 v.h+=(th-v.h)*(1-Math.exp(-11*dt));v.p+=(tp-v.p)*(1-Math.exp(-20*dt));
 if((theme==='Astro'&&!paused&&v.h>.006)||Math.abs(tx-v.x)+Math.abs(ty-v.y)+Math.abs(th-v.h)+Math.abs(tp-v.p)+Math.abs(v.vx)+Math.abs(v.vy)>.006)moving=true;}

 render();if(moving)raf=requestAnimationFrame(frame);}
function wake(){if(!raf&&loaded)raf=requestAnimationFrame(frame);}
function uploadBackdrop(source){if(contextLost||gl.isContextLost())return;gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,wallpaper);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,source);loaded=true;buildScene();}
function syncLive(){const optical=opticalEnabled();if(optical!==opticalWasEnabled){opticalWasEnabled=optical;buildScene();}stats.liveBackground=optical&&[...opticalTextures.values()].some(s=>s.texture)?'optical':'wallpaper';}
function loadWallpaper(version){version=String(version);if(wallpaperLoading&&wallpaperVersion===version)return;wallpaperVersion=version;if(loaded&&wallpaperLoadedVersion===version){if(wallpaperLoading){wallpaperRequest++;wallpaperLoading=false;}post('wallpaper-loaded:'+version);return;}wallpaperLoading=true;const request=++wallpaperRequest,img=new Image();img.onload=()=>{if(contextLost||gl.isContextLost()||request!==wallpaperRequest)return;wallpaperLoading=false;uploadBackdrop(img);wallpaperLoadedVersion=version;syncLive();post('wallpaper-loaded:'+version);};img.onerror=()=>{if(!contextLost&&request===wallpaperRequest){wallpaperLoading=false;fail('wallpaper load failed');}};img.src='wallpaper.png?v='+encodeURIComponent(version);}
// A native layout can reset window regions even when the next local points match.
subscribe(m=>{if(m.type==='layout'){outlineKeys.clear();const sw=m.sceneWidth||m.width,sh=m.sceneHeight||m.height,ox=m.originX||0,oy=m.originY||0;const rebuild=width!==m.width||height!==m.height||theme!==m.theme||sceneWidth!==sw||sceneHeight!==sh||originX!==ox||originY!==oy;width=m.width;height=m.height;sceneWidth=sw;sceneHeight=sh;originX=ox;originY=oy;cards=m.cards;theme=m.theme;paused=m.paused;syncLive();if(rebuild)buildScene();else wake();}else if(m.type==='geometry'){cards=m.cards;if(m.externallyDriven)wake();else if(loaded)render();}else if(m.type==='pointer'){const before=mouse;mouse={x:m.x,y:m.y,down:!!m.down};if(!paused&&cards.some(c=>(mouse.x>=c.x&&mouse.x<=c.x+c.w&&mouse.y>=c.y&&mouse.y<=c.y+c.h)||(before.x>=c.x&&before.x<=c.x+c.w&&before.y>=c.y&&before.y<=c.y+c.h)))wake();}else if(m.type==='wallpaper')loadWallpaper(m.version);else if(m.type==='stats')post('stats:'+JSON.stringify(stats));});
canvas.addEventListener('webglcontextlost',e=>{e.preventDefault();contextLost=true;loaded=false;wallpaperLoading=false;stats.ready=false;wallpaperRequest++;syncLive();cancelAnimationFrame(raf);raf=0;clearTimeout(recoveryTimer);recoveryTimer=setTimeout(()=>{if(contextLost)fail('graphics recovery timed out');},10000);});
canvas.addEventListener('webglcontextrestored',()=>{clearTimeout(recoveryTimer);contextLost=false;try{initialize();loadWallpaper(wallpaperVersion);opticalPost('optical-reset');}catch(e){fail(e);}});
document.addEventListener('visibilitychange',syncLive);
document.addEventListener('scroll',wake,true);
if(window.chrome&&window.chrome.webview){window.chrome.webview.addEventListener('sharedbufferreceived',e=>opticalFrame(e.additionalData,e.getBuffer()));window.chrome.webview.addEventListener('message',e=>{if(!e.data)return;opticalMessage(e.data);});opticalPost('optical-ready');}
window.addEventListener('pagehide',()=>{liveRequested=false;syncLive();releaseDockScene();for(const id of opticalTextures.keys())releaseOptical(id);surfaceChanges.disconnect();surfaceResize.disconnect();document.removeEventListener('scroll',wake,true);clearTimeout(recoveryTimer);cancelAnimationFrame(raf);});
window.addEventListener('prism-preferences',e=>{material=Math.max(.6,Math.min(1,(e.detail.glassOpacity||100)/100));if(Object.prototype.hasOwnProperty.call(e.detail,'glassStyle'))glassStyle=typeof e.detail.glassStyle==='string'&&/^(?:[A-J]|user-[0-9a-f]{32})$/.test(e.detail.glassStyle)?e.detail.glassStyle:'';if(Object.prototype.hasOwnProperty.call(e.detail,'glassOverrides'))glassOverrides=window.PrismGlassMaterials?window.PrismGlassMaterials.normalizeOverrides(e.detail.glassOverrides):{};if(Object.prototype.hasOwnProperty.call(e.detail,'glassPresets'))glassPresets=Array.isArray(e.detail.glassPresets)?e.detail.glassPresets.slice(0,64):[];materialCache.clear();stats.glassStyle=glassStyle;const before=liveRequested;liveRequested=e.detail.liveGlassBackground===true;if(before!==liveRequested&&window.chrome&&window.chrome.webview){for(const id of opticalTextures.keys())releaseOptical(id);opticalPost('optical-reset');}syncLive();wake();});
post('ready');loadWallpaper(Date.now());
};
