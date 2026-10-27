'use strict';
// Runs the real renderer's message/frame logic with a recording WebGL surface.
// It verifies IPC behavior, not GPU pixels or installed native window clipping.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const source=fs.readFileSync(path.join(__dirname,'../src/LiquidDesktop/glass-renderer.js'),'utf8');
const passed=[];
function check(name,action){action();passed.push(name);console.log('PASS '+name);}
function renderer(){
 const messages=[],pending=new Map();let receiver,now=0,nextFrame=0,draws=0;
 const gl=new Proxy({FRAMEBUFFER_COMPLETE:1,getShaderParameter:()=>true,getProgramParameter:()=>true,checkFramebufferStatus:()=>1,
  createShader:()=>({}),createProgram:()=>({}),createBuffer:()=>({}),createTexture:()=>({}),createFramebuffer:()=>({}),
  getUniformLocation:()=>({}),getAttribLocation:()=>0,drawArrays:()=>draws++},{get:(object,key)=>key in object?object[key]:()=>{}});
 const window={addEventListener(){}},canvas={getContext:()=>gl,addEventListener(){}};
 const context=vm.createContext({window,performance:{now:()=>now},crypto:{getRandomValues:a=>(a[0]=123,a)},
  requestAnimationFrame:callback=>{const id=++nextFrame;pending.set(id,callback);return id;},cancelAnimationFrame:id=>pending.delete(id),
  Image:class{set src(value){this.onload();}}});
 vm.runInContext(source,context,{filename:'glass-renderer.js'});
 window.PrismGlassRenderer(canvas,message=>messages.push(message),callback=>receiver=callback);
 return{messages,send:message=>receiver(message),draws:()=>draws,pending:()=>pending.size,
  frame(){now+=16;const callbacks=[...pending.values()];pending.clear();for(const callback of callbacks)callback(now);},
  frames(count){for(let i=0;i<count;i++)this.frame();},
  count(type){return messages.filter(message=>message.startsWith(type+':')).length;},
  latest(type){const message=messages.filter(message=>message.startsWith(type+':')).pop();return message===undefined?undefined:JSON.parse(message.slice(type.length+1));},
  stats(){receiver({type:'stats'});return this.latest('stats');}};
}
const dock={name:'Dock',x:80,y:400,w:640,h:100,r:24,insetY:16,fixedShape:true};
const widget={name:'Weather',x:120,y:100,w:300,h:100,r:24,fixedShape:true};
const layout=cards=>({type:'layout',width:1000,height:700,theme:'Sky',paused:false,cards});
const geometry=(h,cards)=>h.send({type:'geometry',cards});

const first=renderer();first.frame();
check('initial empty scene publishes its clip once',()=>assert.equal(first.count('widget-shapes'),1));
check('initial empty clip is an empty array',()=>assert.deepEqual(first.latest('widget-shapes'),[]));
check('initial readiness and first paint are preserved',()=>{assert.ok(first.messages.includes('ready'));assert.ok(first.messages.includes('painted'));});
geometry(first,[]);geometry(first,[]);
check('repeated empty paints do not repeat clip IPC',()=>assert.equal(first.count('widget-shapes'),1));

const h=renderer();h.send(layout([dock]));h.frame();
check('first Dock paint publishes Dock and empty widget clips',()=>{assert.equal(h.count('dock-shape'),1);assert.equal(h.count('widget-shapes'),1);});
const original=h.latest('dock-shape'),beforeDraws=h.draws();
for(let i=0;i<60;i++)geometry(h,[dock]);
check('sixty static redraws keep one Dock clip message',()=>assert.equal(h.count('dock-shape'),1));
check('sixty static redraws keep one empty widget clip message',()=>assert.equal(h.count('widget-shapes'),1));
check('outline deduplication never skips actual WebGL drawing',()=>assert.equal(h.draws()-beforeDraws,120));
check('published coordinates retain their original subpixel precision',()=>assert.ok(original.some(point=>point.some(value=>Math.abs(value*4-Math.round(value*4))>1e-5))));
geometry(h,[{...dock,clipOffsetX:.001,clipOffsetY:.001}]);
check('sub-quarter-pixel jitter does not republish an unchanged comparison key',()=>assert.equal(h.count('dock-shape'),1));
geometry(h,[{...dock,clipOffsetX:.5}]);
check('physical clip movement publishes a new Dock clip',()=>assert.equal(h.count('dock-shape'),2));
geometry(h,[{...dock,motionFluid:.5,flowY:1}]);
check('liquid deformation publishes its long-edge outline points',()=>{assert.equal(h.count('dock-shape'),3);assert.ok(h.latest('dock-shape').length>original.length);});
geometry(h,[dock]);
check('settling from deformation republishes the original clip',()=>{assert.equal(h.count('dock-shape'),4);assert.deepEqual(h.latest('dock-shape'),original);});

geometry(h,[dock,widget]);
check('empty to nonempty widget geometry publishes',()=>{assert.equal(h.count('widget-shapes'),2);assert.equal(h.latest('widget-shapes').length,1);});
geometry(h,[dock,widget]);
check('stable nonempty widget geometry is deduplicated',()=>assert.equal(h.count('widget-shapes'),2));
geometry(h,[dock,{...widget,x:widget.x+.5}]);
check('widget clip screen movement publishes',()=>assert.equal(h.count('widget-shapes'),3));
geometry(h,[dock]);geometry(h,[dock]);
check('nonempty to empty widget geometry publishes exactly once',()=>{assert.equal(h.count('widget-shapes'),4);assert.deepEqual(h.latest('widget-shapes'),[]);});
check('widget-only changes leave the Dock clip cached',()=>assert.equal(h.count('dock-shape'),4));
h.send(layout([dock]));h.frame();
check('identical layout resends clips after the native host may have reset them',()=>{assert.equal(h.count('dock-shape'),5);assert.equal(h.count('widget-shapes'),5);});
h.send({...layout([dock]),width:1200,originX:2560,originY:-164});h.frame();
check('monitor layout changes republish the Dock clip even when local points match',()=>assert.equal(h.count('dock-shape'),6));
const independent=renderer();independent.send(layout([dock]));independent.frame();
check('each renderer owns an independent initial clip cache',()=>assert.equal(independent.count('dock-shape'),1));

const astro=renderer();astro.send({...layout([dock]),theme:'Astro'});astro.frame();
astro.send({type:'pointer',x:400,y:450,down:false});astro.frames(200);
const astroDraws=astro.draws(),astroClips=astro.count('dock-shape');astro.frames(30);
check('Astro hover continues drawing after fixed clip geometry settles',()=>{assert.ok(astro.draws()>astroDraws);assert.ok(astro.pending()>0);assert.equal(astro.count('dock-shape'),astroClips);});
astro.send({...layout([dock]),theme:'Astro',paused:true});astro.frames(300);
check('pause still paints the final state and settles its frame loop',()=>{assert.equal(astro.stats().paused,true);assert.equal(astro.pending(),0);});
const pausedClips=astro.count('dock-shape'),pausedDraws=astro.draws();geometry(astro,[dock]);
check('paused repeated drawing remains clipped without extra IPC',()=>{assert.ok(astro.draws()>pausedDraws);assert.equal(astro.count('dock-shape'),pausedClips);});
astro.send({...layout([dock]),theme:'Astro',paused:false});astro.frames(30);
check('resuming Astro restores active drawing and required layout clips',()=>{assert.equal(astro.stats().paused,false);assert.ok(astro.pending()>0);assert.equal(astro.count('dock-shape'),pausedClips+1);});
console.log(passed.length+' glass outline message checks passed.');
