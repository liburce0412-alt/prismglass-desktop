'use strict';
(() => {
const layer=document.getElementById('lyrics-layer'),pill=document.getElementById('dock-lyrics'),current=document.getElementById('lyric-current'),next=document.getElementById('lyric-next');
const reduced=matchMedia('(prefers-reduced-motion: reduce)'),clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
let glassReceive,layout=null,base=null,active=false,motionPaused=false,raf=0,last=0,watchdog=0,fluid=0,lastText='',lastNext='';
const size={w:{x:0,v:0,target:0},h:{x:0,v:0,target:0},reveal:{x:0,v:0,target:0}};
let iconSequence=0,iconAlpha=-1,iconDesired=255,iconSent=-Infinity,iconForce=true,iconPending=false,iconEpoch=0,iconSentEpoch=0,iconsHidden=false,iconLease=0;
let textWidth=360,textHeight=78,scrollAnimations=[],appearance=PrismLyrics.normalize(),appearanceKey='',palette=null;
PrismLyrics.apply(pill,appearance);
function still(){return reduced.matches||motionPaused||!!layout?.paused;}
// Same interruptible, critically damped response as the top island.
function step(p,dt){const omega=11,y=p.x-p.target,j=p.v+omega*y,e=Math.exp(-omega*dt);p.x=p.target+(y+j*dt)*e;p.v=(p.v-omega*j*dt)*e;const unit=p===size.reveal ? .001 : 1;if(Math.abs(p.x-p.target)<.15*unit&&Math.abs(p.v)<.4*unit){p.x=p.target;p.v=0;}}
function resetIconAck(preserveHidden=false){if(!preserveHidden||!iconsHidden){iconsHidden=false;clearTimeout(iconLease);}iconForce=true;iconEpoch++;}
function publishIcons(alpha){
 iconDesired=alpha;const now=performance.now();
 // One outstanding command; coalesce newer frame values until its acknowledgement.
 // A lost command is retried at the same bounded rate as the lyric heartbeat.
 if(iconPending&&now-iconSent<750)return;
 if(!iconForce&&alpha===iconAlpha&&now-iconSent<750)return;
 if(alpha>0)iconsHidden=false;iconForce=false;iconPending=true;iconAlpha=alpha;iconSent=now;iconSentEpoch=iconEpoch;iconSequence=iconSequence%2147483647+1;
 window.chrome.webview.postMessage('dock-icons:'+iconSequence+':'+alpha);
}
function cancel(animations){for(const a of animations)a.cancel();animations.length=0;}
function metrics(){
 if(!base)return;
 const dual=appearance.layout!=='single'&&appearance.layout!=='split'&&!next.parentElement.hidden,split=appearance.layout==='split';
 const maxWidth=Math.min(base.w,appearance.width),padding=Math.min(100,maxWidth*.18);
 const fontSize=dual?Math.min(appearance.fontSize,(base.h-26)/2.1125):appearance.fontSize;
 current.style.fontSize=fontSize+'px';next.style.fontSize=Math.round(fontSize*.69)+'px';
 const nextWidth=appearance.layout==='single'||next.parentElement.hidden?0:next.scrollWidth;
 const content=split?2*Math.max(current.scrollWidth,nextWidth)+40:Math.max(current.scrollWidth,nextWidth);
 textWidth=appearance.widthMode==='fixed'?maxWidth:clamp(content+padding,Math.min(360,maxWidth),maxWidth);
 const available=Math.max(1,split?(textWidth-padding-40)/2:textWidth-padding);
 pill.style.setProperty('--copy-width',available+'px');
 textHeight=Math.min(base.h,Math.max(dual?72:56,dual?(fontSize+Math.round(fontSize*.69))*1.25+26:fontSize*1.25+22));
 cancel(scrollAnimations);
 for(const el of [current,next]){
  const overflow=el.scrollWidth-available;
  el.parentElement.classList.toggle('overflowing',overflow>1);
  if(overflow>1&&!still()&&active&&!(el===next&&(appearance.layout==='single'||next.parentElement.hidden))){
   // This scrolls long lines, without inventing word timings absent from LRC.
   scrollAnimations.push(el.animate([{transform:'translateX(0)'},{transform:`translateX(${-overflow}px)`}],{duration:Math.max(2400,overflow/32*1000),delay:1200,iterations:Infinity,direction:'alternate',easing:'ease-in-out'}));
  }
 }
 targets();
}
function targets(){if(!base)return;size.w.target=active?textWidth:base.w;size.h.target=active?textHeight:base.h;size.reveal.target=active?1:0;}
function text(message,styleChanged=false){
 if(!message.show)return;
 next.parentElement.hidden=!message.next;
 const changed=message.current!==lastText||message.next!==lastNext;
 if(changed||styleChanged){
  PrismLyrics.cancel(pill);lastText=message.current;lastNext=message.next;
  current.textContent=lastText;next.textContent=lastNext;
  metrics();PrismLyrics.animate(pill,appearance,still());
 }
}
function draw(){
 if(!base||!glassReceive)return;
 if(still()&&(!active||iconsHidden))for(const p of Object.values(size)){p.x=p.target;p.v=0;}
 const w=size.w.x,h=size.h.x,x=base.x+(base.w-w)/2,y=base.y+base.h-h;
 const moving=Object.values(size).some(p=>p.x!==p.target||p.v!==0);
 const contraction=clamp((base.w-w)/Math.max(1,base.w-textWidth),0,1);
 const card={...base,x,y,w,h,r:base.r+(Math.min(h/2,30)-base.r)*contraction,clipOffsetX:x-base.x,clipOffsetY:y-base.y,fixedShape:active||moving,motionFluid:still()?0:fluid,flowY:Math.sign(-size.w.v)};
 glassReceive({type:'geometry',externallyDriven:true,cards:layout.cards.map(c=>c.name==='Dock'?card:c)});
 layer.style.width=layout.width+'px';layer.style.height=layout.height+'px';
 layer.style.transform=`scale(${innerWidth/layout.width},${innerHeight/layout.height})`;
 pill.style.left=x+'px';pill.style.top=y+'px';pill.style.width=w+'px';pill.style.height=h+'px';
 pill.style.borderRadius=card.r+'px';PrismLyrics.background(pill,appearance,palette,still()||!active);
 // One frame state drives geometry, icon fade and text, including equal-width capsules.
 const opening=clamp(size.reveal.x,0,1),alpha=still()&&active?0:Math.round(255*(1-clamp(opening/.18,0,1)));
 pill.style.opacity=iconsHidden&&alpha===0?String(still()&&active?1:clamp((opening-.22)/.55,0,1)):'0';
 pill.setAttribute('aria-hidden',String(!active));pill.classList.toggle('reduced',still()||!active);
 publishIcons(alpha);
 window.dockLyricsState={active,width:w,height:h,targetWidth:size.w.target,velocity:size.w.v,reveal:opening,iconAlpha:alpha,iconSequence,iconsHidden,current:current.textContent,next:next.textContent,opacity:Number(pill.style.opacity),reduced:still(),moving,appearance,gap:pill.classList.contains('interlude')};
}
function frame(now){
 raf=0;if(!base)return;const dt=clamp((now-last)/1000||.016,.001,.034);last=now;
 // Do not collapse past the icon fade while the native skin is busy. Waiting
 // uses this existing RAF only for bounded retries, without repainting glass.
 if(active&&!iconsHidden&&(still()||size.reveal.x>=.18)){
  publishIcons(0);raf=requestAnimationFrame(frame);return;
 }
 if(!still()){
  const before=Object.values(size).map(p=>({x:p.x,v:p.v}));
  for(const p of Object.values(size))step(p,dt);
  if(active&&!iconsHidden&&size.reveal.x>.18){
   const fraction=(.18-before[2].x)/(size.reveal.x-before[2].x);
   Object.values(size).forEach((p,i)=>{p.x=before[i].x+(p.x-before[i].x)*fraction;p.v=before[i].v+(p.v-before[i].v)*fraction;});size.reveal.x=.18;
  }
 }
 const target=still()?0:clamp(Math.abs(size.w.v)/Math.max(1,base.w-textWidth)/4,0,1);
 fluid+=(target-fluid)*(1-Math.exp(-14*dt));if(target===0&&fluid<.002)fluid=0;
 draw();if(Object.values(size).some(p=>p.x!==p.target||p.v!==0)||Math.abs(fluid-target)>.002)raf=requestAnimationFrame(frame);
}
function wake(){if(base&&!raf){last=performance.now();raf=requestAnimationFrame(frame);}}
function hide(){if(active)resetIconAck(true);active=false;targets();PrismLyrics.cancel(pill);cancel(scrollAnimations);draw();wake();}
function receive(m){
 if(m.type==='dock-icons-ack'){
  if(!iconPending||m.sequence!==iconSequence||m.alpha!==iconAlpha)return;
  iconPending=false;
  if(iconSentEpoch!==iconEpoch||m.alpha!==iconDesired){draw();wake();return;}
  clearTimeout(iconLease);iconsHidden=m.alpha===0;
  // Hide text before Rainmeter's three-second lease can restore orphaned icons.
  if(iconsHidden)iconLease=setTimeout(()=>{iconsHidden=false;iconForce=true;draw();wake();},1800);
  draw();wake();
 }else if(m.type==='layout'){
  resetIconAck();
  layout=m;const previous=base;base=m.cards.find(c=>c.name==='Dock')||null;
  if(!base){active=false;pill.style.opacity='0';}
  else if(!previous){size.w.x=base.w;size.h.x=base.h;}
  metrics();glassReceive({...m,paused:still()});draw();wake();
 }else if(m.type==='lyrics'){
  palette=m.palette||palette;
  clearTimeout(watchdog);const changedMotion=motionPaused!==!!m.motionPaused;motionPaused=!!m.motionPaused;
  if(changedMotion&&layout)glassReceive({...layout,paused:still()});
  const nextAppearance=PrismLyrics.normalize(m.appearance),key=JSON.stringify(nextAppearance),styleChanged=key!==appearanceKey;
  if(styleChanged||changedMotion){appearance=nextAppearance;appearanceKey=key;PrismLyrics.apply(pill,appearance,still());}
  pill.classList.toggle('interlude',!!m.gap);
  if(still()){PrismLyrics.cancel(pill);cancel(scrollAnimations);}
  const nextActive=!!m.show&&!!m.current,entering=!active&&nextActive;if(active!==nextActive)resetIconAck(true);active=nextActive;
  if(!active){PrismLyrics.cancel(pill);cancel(scrollAnimations);}
  text(m,styleChanged);if(entering||changedMotion)metrics();targets();draw();wake();watchdog=setTimeout(hide,3000);
 }else{
  if(m.type==='pointer'&&active&&base&&m.x>=base.x-24&&m.x<=base.x+base.w+24&&m.y>=base.y-48&&m.y<=base.y+base.h+32)hide();
  glassReceive(m);
 }
}
PrismGlassRenderer(document.getElementById('scene'),message=>{
 if(message.startsWith('stats:')){const stats=JSON.parse(message.slice(6));stats.lyrics=window.dockLyricsState||null;message='stats:'+JSON.stringify(stats);}
 window.chrome.webview.postMessage(message);
},fn=>glassReceive=fn);
window.chrome.webview.addEventListener('message',e=>receive(e.data));
reduced.addEventListener('change',()=>{if(still()){PrismLyrics.cancel(pill);cancel(scrollAnimations);}if(layout)glassReceive({...layout,paused:still()});metrics();draw();wake();});
window.addEventListener('resize',()=>{metrics();draw();wake();});
window.addEventListener('pagehide',()=>{clearTimeout(watchdog);clearTimeout(iconLease);cancelAnimationFrame(raf);PrismLyrics.cancel(pill);cancel(scrollAnimations);});
})();
