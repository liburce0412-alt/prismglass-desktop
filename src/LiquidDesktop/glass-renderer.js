'use strict';
window.PrismGlassRenderer = function(canvas,post,subscribe){
const gl=canvas.getContext('webgl',{alpha:false,antialias:false,powerPreference:'low-power',preserveDrawingBuffer:true});
let width=2560,height=1440,sceneWidth=2560,sceneHeight=1440,originX=0,originY=0,cards=[],theme='Sky',paused=false,loaded=false,raf=0,last=0,frames=0;
let mouse={x:-999,y:-999,down:false},leans=new Map();
function randomEffect(){const value=new Uint32Array(1);crypto.getRandomValues(value);return value[0]/4294967296;}
const stats={ready:false,frames:0,theme:'Sky',paused:false,errors:[],cards:0};
const vertex='attribute vec2 a;varying vec2 uv;void main(){uv=a*.5+.5;gl_Position=vec4(a,0.,1.);}';
const sceneFrag=`precision highp float;varying vec2 uv;uniform sampler2D wallpaper;uniform float astro;uniform vec2 size;uniform vec2 sceneSize;uniform vec2 origin;
float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
void main(){vec2 p=(origin+vec2(uv.x,1.-uv.y)*size)/sceneSize;vec3 c=texture2D(wallpaper,p).rgb;
 if(astro>.5){float light=exp(-length((p-vec2(.65,.84))*vec2(1.,2.))*3.);c=mix(c,vec3(.018,.035,.082),.76)+vec3(.035,.065,.15)*light;
 vec2 q=p*sceneSize/60.;vec2 cell=floor(q);float seed=hash(cell);vec2 star=vec2(hash(cell+2.),hash(cell+7.))*.8+.1;
 vec2 st=(fract(q)-star)*60.;float core=exp(-dot(st,st)/mix(.45,1.5,step(.96,seed)));float glow=exp(-dot(st,st)/12.)*.09;
 float rays=(exp(-abs(st.x)*3.-abs(st.y)*.65)+exp(-abs(st.y)*3.-abs(st.x)*.65))*.14*step(.985,seed);
 vec3 starColor=mix(vec3(.68,.84,1.),vec3(1.,.96,.82),step(.92,seed));c+=(core+glow+rays)*step(.78,seed)*starColor*mix(.38,.8,step(.96,seed));}
 gl_FragColor=vec4(c,1.);}`;
const glassFrag=`precision highp float;varying vec2 uv;uniform sampler2D scene;uniform sampler2D wallpaper;uniform vec2 size;uniform vec2 sceneSize;uniform vec2 origin;uniform vec2 center;uniform vec2 halfSize;uniform float radius;uniform vec2 lean;uniform float astro;uniform float onlyBackground;uniform float hover;uniform float press;uniform float fixedShape;uniform float rimStrength;uniform float liquidAmplitude;uniform float liquidDirection;uniform float motionTime;uniform float auroraColor;uniform float effectSeed;uniform vec4 meteorHead[5];uniform vec2 meteorDir[5];
float sd(vec2 p,vec2 b,float r){vec2 q=abs(p)-b+r;return length(max(q,0.))+min(max(q.x,q.y),0.)-r;}
float liquidProfile(float nx){float a=min(abs(nx),1.);float middle=exp(-a*a*6.);float shoulders=exp(-pow((a-.55)*3.,2.));return mix(.8*shoulders-.25*middle,.9*middle-.3*pow(a,1.4),(liquidDirection+1.)*.5);}
void main(){vec2 p=vec2(uv.x,1.-uv.y)*size;vec3 original=texture2D(wallpaper,(origin+vec2(uv.x,1.-uv.y)*size)/sceneSize).rgb;
 if(onlyBackground>.5){gl_FragColor=vec4(original,1.);return;}
 float motionScale=min(1.,halfSize.y/70.)*(1.-fixedShape);vec2 d=p-center-lean*vec2(5.,4.)*hover*motionScale;
 d/=vec2(1.-(hover*.045+press*.025)*(1.-fixedShape),1.+(hover*.035-press*.065)*(1.-fixedShape));
 d.x+=sin(d.y/max(halfSize.y,1.)*1.5)*lean.x*15.*motionScale;d.y+=sin(d.x/max(halfSize.x,1.)*1.4)*lean.y*11.*motionScale;
 if(liquidAmplitude>.001)d.y=(d.y-liquidAmplitude)/(1.+liquidAmplitude/max(halfSize.y,1.)*liquidProfile(d.x/max(halfSize.x,1.)));
 vec2 glassSize=halfSize-vec2(2.)+vec2((hover*1.5-press*2.5)*(1.-fixedShape));float dist=sd(d,glassSize,radius);if(dist>8.)discard;vec3 result=original*(1.-exp(-max(dist,0.)/3.)*.08);
 if(dist<1.){float e=.7;vec2 grad=normalize(vec2(sd(d+vec2(e,0.),glassSize,radius)-sd(d-vec2(e,0.),glassSize,radius),sd(d+vec2(0.,e),glassSize,radius)-sd(d-vec2(0.,e),glassSize,radius))+.0001);
 float depth=max(-dist,0.);float rim=exp(-depth/12.);vec3 normal=normalize(vec3(grad*exp(-depth/23.)*.85,1.));
 vec3 ray=refract(vec3(0.,0.,-1.),normal,1./1.46);vec2 localCursor=lean*halfSize*.82;vec2 toCursor=d-localCursor;float focus=exp(-dot(toCursor,toCursor)/4800.);vec2 bend=ray.xy*(44.+hover*10.)+d*.025+lean*27.+toCursor*focus*hover*.22;vec2 at=uv+vec2(bend.x,-bend.y)*mix(.45,1.,rimStrength)/size;
 vec2 split=vec2(grad.x,-grad.y)*rim*.6*(1.-astro)/size;
 vec3 c=vec3(texture2D(scene,at+split).r,texture2D(scene,at).g,texture2D(scene,at-split).b);
 c=mix(c,astro>.5?vec3(.12,.17,.27):vec3(.97,.99,1.),astro>.5?.10:.19);
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
 float light=pow(max(dot(grad,normalize(vec2(-.65,-.85)+lean*.25)),0.),4.);
 c+=exp(-pow((depth-1.3)/.95,2.))*(.18+.44*light)*rimStrength+rim*light*.12*rimStrength;
 c+=focus*hover*(astro>.5?.075:.13)+rim*hover*.07;c-=rim*(1.-light)*.045;c+=exp(-pow((depth-5.)/2.4,2.))*.03;
 vec2 edgeCursor=lean/max(max(abs(lean.x),abs(lean.y)),.05)*halfSize;
 vec2 edgeDelta=d-edgeCursor;
 float following=exp(-dot(edgeDelta,edgeDelta)/20000.);
 float flow=.5+.5*sin((d.x+d.y)*.021-motionTime*1.4);
 vec3 flowingColor=mix(vec3(.20,.88,1.),vec3(.80,.43,1.),flow);
 float edgeBand=exp(-pow((depth-2.)/1.7,2.));
 c+=flowingColor*following*hover*(edgeBand*.65+exp(-depth/7.)*.12);
 result=mix(original,c,1.-smoothstep(-.7,.7,dist));}
 gl_FragColor=vec4(result,1.);}`;
function fail(e){stats.errors.push(String(e));post('error:'+e);}
if(!gl){fail('WebGL unavailable');return;}
function program(src){function shader(t,s){const sh=gl.createShader(t);gl.shaderSource(sh,s);gl.compileShader(sh);if(!gl.getShaderParameter(sh,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(sh));return sh;}const p=gl.createProgram(),v=shader(gl.VERTEX_SHADER,vertex),f=shader(gl.FRAGMENT_SHADER,src);gl.attachShader(p,v);gl.attachShader(p,f);gl.linkProgram(p);if(!gl.getProgramParameter(p,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(p));gl.deleteShader(v);gl.deleteShader(f);return p;}
let sceneProgram,glassProgram;try{sceneProgram=program(sceneFrag);glassProgram=program(glassFrag);}catch(e){fail(e);return;}
const buffer=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,buffer);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array([-1,-1,1,-1,-1,1,-1,1,1,-1,1,1]),gl.STATIC_DRAW);
function texture(){const t=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,t);for(const key of [gl.TEXTURE_MIN_FILTER,gl.TEXTURE_MAG_FILTER])gl.texParameteri(gl.TEXTURE_2D,key,gl.LINEAR);for(const key of [gl.TEXTURE_WRAP_S,gl.TEXTURE_WRAP_T])gl.texParameteri(gl.TEXTURE_2D,key,gl.CLAMP_TO_EDGE);return t;}
const wallpaper=texture(),scene=texture(),fbo=gl.createFramebuffer(),cache=new Map();
function loc(p,n){const k=(p===sceneProgram?'s':'g')+n;if(!cache.has(k))cache.set(k,gl.getUniformLocation(p,n));return cache.get(k);}
function one(p,n,v){gl.uniform1f(loc(p,n),v);}function two(p,n,x,y){gl.uniform2f(loc(p,n),x,y);}
function use(p){gl.useProgram(p);const a=gl.getAttribLocation(p,'a');gl.enableVertexAttribArray(a);gl.vertexAttribPointer(a,2,gl.FLOAT,false,0,0);}
function bind(p,n,t,slot){gl.activeTexture(gl.TEXTURE0+slot);gl.bindTexture(gl.TEXTURE_2D,t);gl.uniform1i(loc(p,n),slot);}
function buildScene(){if(!loaded)return;canvas.width=width;canvas.height=height;gl.viewport(0,0,width,height);gl.disable(gl.SCISSOR_TEST);gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,scene);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,width,height,0,gl.RGBA,gl.UNSIGNED_BYTE,null);gl.bindFramebuffer(gl.FRAMEBUFFER,fbo);gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT0,gl.TEXTURE_2D,scene,0);if(gl.checkFramebufferStatus(gl.FRAMEBUFFER)!==gl.FRAMEBUFFER_COMPLETE){fail('incomplete framebuffer');return;}use(sceneProgram);bind(sceneProgram,'wallpaper',wallpaper,0);two(sceneProgram,'size',width,height);two(sceneProgram,'sceneSize',sceneWidth,sceneHeight);two(sceneProgram,'origin',originX,originY);one(sceneProgram,'astro',theme==='Astro'?1:0);gl.drawArrays(gl.TRIANGLES,0,6);gl.bindFramebuffer(gl.FRAMEBUFFER,null);wake();}
function liquidShape(c){return{amplitude:Math.max(0,Math.min(c.h*.32,18,(height-c.y-c.h-5)/1.9))*Math.max(0,Math.min(1,c.motionFluid||0)),direction:Math.max(-1,Math.min(1,c.flowY||0))};}
function liquidProfile(nx,direction){const a=Math.min(Math.abs(nx),1),middle=Math.exp(-a*a*6),shoulders=Math.exp(-Math.pow((a-.55)*3,2)),t=(direction+1)*.5;return(.8*shoulders-.25*middle)*(1-t)+(.9*middle-.3*Math.pow(a,1.4))*t;}
function dockOutline(c,v){
 const weight=1-(c.name==='TopIsland'&&v.lock!==undefined?v.lock:c.fixedShape?1:0),h=(v.h||0)*weight,p=(v.p||0)*weight,m=Math.min(1,c.h/140)*weight,bx=c.w/2-2+h*1.5-p*2.5,by=c.h/2-2+h*1.5-p*2.5,r=c.r,points=[];
 const liquid=liquidShape(c);
 const corners=[[-bx+r,-by+r,Math.PI],[bx-r,-by+r,Math.PI*1.5],[bx-r,by-r,0],[-bx+r,by-r,Math.PI*.5]];
 function point(dx,dy){
  const flowingY=dy*(1+liquid.amplitude/Math.max(c.h/2,1)*liquidProfile(dx/Math.max(c.w/2,1),liquid.direction))+liquid.amplitude;
  const ay=flowingY-Math.sin(dx/(c.w/2)*1.4)*v.y*11*m;
  const ax=dx-Math.sin(ay/(c.h/2)*1.5)*v.x*15*m;
  points.push([26+c.w/2+v.x*5*(v.h||0)*m+ax*(1-h*.045-p*.025),26+(c.insetY||0)+c.h/2+v.y*4*(v.h||0)*m+ay*(1+h*.035-p*.065)]);
 }
 for(let corner=0;corner<4;corner++){
  const [cx,cy,start]=corners[corner];
  for(let i=0;i<=12;i++){const angle=start+i*Math.PI/24;point(cx+Math.cos(angle)*r,cy+Math.sin(angle)*r);}
  // The native clip must follow the wave along long edges, not join only its corners.
  if(liquid.amplitude>.001&&(corner===0||corner===2)){
   const next=corners[(corner+1)%4],fromX=cx+Math.cos(start+Math.PI/2)*r,fromY=cy+Math.sin(start+Math.PI/2)*r,toX=next[0]+Math.cos(next[2])*r;
   for(let i=1;i<32;i++)point(fromX+(toX-fromX)*i/32,fromY);
  }
 }
 if(c.name==='Dock')post('dock-shape:'+JSON.stringify(points));
 return points;
}
function drawCard(c,lean){
 const now=performance.now()/1000,heads=new Float32Array(20),dirs=new Float32Array(10);
 const liquid=liquidShape(c);one(glassProgram,'liquidAmplitude',liquid.amplitude);one(glassProgram,'liquidDirection',liquid.direction);
 for(const [i,m] of (lean.meteors||[]).slice(0,5).entries()){
  const age=now-m.start;heads.set([m.x+age*m.speed*m.dx,m.y+age*m.speed*m.dy,m.tail,Math.min(1,age*8)*Math.min(1,Math.max(0,m.life-age)*3)],i*4);dirs.set([m.dx,m.dy],i*2);
 }
 gl.uniform4fv(loc(glassProgram,'meteorHead[0]'),heads);gl.uniform2fv(loc(glassProgram,'meteorDir[0]'),dirs);one(glassProgram,'auroraColor',lean.auroraColor||0);one(glassProgram,'effectSeed',lean.effectSeed||0);one(glassProgram,'motionTime',now);one(glassProgram,'rimStrength',c.rimStrength===undefined?1:c.rimStrength);one(glassProgram,'fixedShape',c.name==='TopIsland'&&lean.lock!==undefined?lean.lock:c.fixedShape?1:0);two(glassProgram,'center',c.x+c.w/2,c.y+c.h/2);two(glassProgram,'halfSize',c.w/2,c.h/2);two(glassProgram,'lean',lean.x,lean.y);one(glassProgram,'radius',c.r);one(glassProgram,'hover',lean.h||0);one(glassProgram,'press',lean.p||0);const pad=26+Math.ceil(liquid.amplitude*2),left=Math.max(0,Math.floor(c.x-pad)),top=Math.max(0,Math.floor(c.y-pad)),right=Math.min(width,Math.ceil(c.x+c.w+pad)),bottom=Math.min(height,Math.ceil(c.y+c.h+pad));gl.scissor(left,height-bottom,Math.max(0,right-left),Math.max(0,bottom-top));gl.drawArrays(gl.TRIANGLES,0,6);if(c.name==='Dock')dockOutline(c,lean);}
function render(){gl.viewport(0,0,width,height);use(glassProgram);bind(glassProgram,'scene',scene,0);bind(glassProgram,'wallpaper',wallpaper,1);two(glassProgram,'size',width,height);two(glassProgram,'sceneSize',sceneWidth,sceneHeight);two(glassProgram,'origin',originX,originY);one(glassProgram,'astro',theme==='Astro'?1:0);one(glassProgram,'onlyBackground',1);gl.disable(gl.SCISSOR_TEST);gl.drawArrays(gl.TRIANGLES,0,6);one(glassProgram,'onlyBackground',0);gl.enable(gl.SCISSOR_TEST);

 const outlines=[];
 for(const c of cards){const v=leans.get(c.name)||{x:0,y:0};drawCard(c,v);if(c.name!=='Dock')outlines.push(dockOutline(c,v).map(p=>[p[0]+c.x-26,p[1]+c.y-26]));}
 post('widget-shapes:'+JSON.stringify(outlines));
 gl.disable(gl.SCISSOR_TEST);frames++;stats.frames=frames;stats.theme=theme;stats.paused=paused;stats.cards=cards.length;stats.canvasWidth=width;stats.canvasHeight=height;stats.sceneWidth=sceneWidth;stats.sceneHeight=sceneHeight;stats.activeCards=cards.filter(c=>(leans.get(c.name)?.h||0)>.05).map(c=>c.name);stats.maxDeformation=Math.max(0,...[...leans.values()].map(v=>Math.abs(v.x)*20+Math.abs(v.y)*15+v.h*8));if(!stats.ready)post('painted');stats.ready=true;}
function frame(now){raf=0;if(!loaded)return;const dt=Math.max(.001,Math.min((now-last)/1000||.016,.034));last=now;let moving=false;
 for(const c of cards){let v=leans.get(c.name);if(!v){v={x:0,y:0,h:0,p:0,vx:0,vy:0};leans.set(c.name,v);}const inside=!paused&&mouse.x>=c.x&&mouse.x<=c.x+c.w&&mouse.y>=c.y&&mouse.y<=c.y+c.h;const tx=inside?(mouse.x-c.x-c.w/2)/(c.w/2):0,ty=inside?(mouse.y-c.y-c.h/2)/(c.h/2):0;const th=inside?1:0,tp=inside&&mouse.down?1:0;
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
 }else{v.vx+=(tx-v.x)*150*dt;v.vy+=(ty-v.y)*150*dt;v.vx*=Math.exp(-13*dt);v.vy*=Math.exp(-13*dt);v.x+=v.vx*dt;v.y+=v.vy*dt;}
 v.h+=(th-v.h)*(1-Math.exp(-11*dt));v.p+=(tp-v.p)*(1-Math.exp(-20*dt));
 if((theme==='Astro'&&!paused&&v.h>.006)||Math.abs(tx-v.x)+Math.abs(ty-v.y)+Math.abs(th-v.h)+Math.abs(tp-v.p)+Math.abs(v.vx)+Math.abs(v.vy)>.006)moving=true;}

 render();if(moving)raf=requestAnimationFrame(frame);}
function wake(){if(!raf&&loaded)raf=requestAnimationFrame(frame);}
function loadWallpaper(version){const img=new Image();img.onload=()=>{gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,wallpaper);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,img);loaded=true;buildScene();};img.onerror=()=>fail('wallpaper load failed');img.src='wallpaper.png?v='+encodeURIComponent(version);}
subscribe(m=>{if(m.type==='layout'){const sw=m.sceneWidth||m.width,sh=m.sceneHeight||m.height,ox=m.originX||0,oy=m.originY||0;const rebuild=width!==m.width||height!==m.height||theme!==m.theme||sceneWidth!==sw||sceneHeight!==sh||originX!==ox||originY!==oy;width=m.width;height=m.height;sceneWidth=sw;sceneHeight=sh;originX=ox;originY=oy;cards=m.cards;theme=m.theme;paused=m.paused;if(rebuild)buildScene();else wake();}else if(m.type==='geometry'){cards=m.cards;if(m.externallyDriven)wake();else if(loaded)render();}else if(m.type==='pointer'){const before=mouse;mouse={x:m.x,y:m.y,down:!!m.down};if(!paused&&cards.some(c=>(mouse.x>=c.x&&mouse.x<=c.x+c.w&&mouse.y>=c.y&&mouse.y<=c.y+c.h)||(before.x>=c.x&&before.x<=c.x+c.w&&before.y>=c.y&&before.y<=c.y+c.h)))wake();}else if(m.type==='wallpaper')loadWallpaper(m.version);else if(m.type==='stats')post('stats:'+JSON.stringify(stats));});
canvas.addEventListener('webglcontextlost',e=>{e.preventDefault();cancelAnimationFrame(raf);fail('graphics context lost');});
post('ready');loadWallpaper(Date.now());
};
