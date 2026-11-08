'use strict';
// Runs the real renderer's message/frame logic with a recording WebGL surface.
// It verifies IPC behavior, not GPU pixels or installed native window clipping.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const source=fs.readFileSync(path.join(__dirname,'../src/LiquidDesktop/glass-renderer.js'),'utf8');
const materialSource=fs.readFileSync(path.join(__dirname,'../src/LiquidDesktop/glass-materials.js'),'utf8');
const passed=[];
function check(name,action){action();passed.push(name);console.log('PASS '+name);}
function renderer(native=false,nodes=[],deferWallpaper=false){
 const nativeEvents=new Map(),released=[],uniforms=new Map(),copies=[],scissors=[],documentEvents=new Map();let allocations=0;const messages=[],pending=new Map(),events=new Map(),timers=new Map(),windowEvents=new Map(),liveImages=[],wallpaperImages=[];let receiver,now=0,nextFrame=0,nextTimer=0,draws=0,lost=false;
 const gl=new Proxy({FRAMEBUFFER_COMPLETE:1,isContextLost:()=>lost,getShaderParameter:()=>true,getProgramParameter:()=>true,checkFramebufferStatus:()=>1,
  createShader:()=>({}),createProgram:()=>({}),createBuffer:()=>({}),createTexture:()=>({}),createFramebuffer:()=>({}),
  getUniformLocation:(_,name)=>name,uniform1f:(name,value)=>uniforms.set(name,value),uniform2f:(name,...value)=>uniforms.set(name,value),uniform4f:(name,...value)=>uniforms.set(name,value),uniform4fv:(name,value)=>uniforms.set(name,Array.from(value)),scissor:(...values)=>scissors.push(values),getAttribLocation:()=>0,texImage2D:(...args)=>{if(args[8]===null)allocations++;},drawArrays:()=>draws++},{get:(object,key)=>key in object?object[key]:()=>{}});
 const window={addEventListener:(type,handler)=>windowEvents.set(type,handler)},canvas={getContext:()=>gl,getBoundingClientRect:()=>({left:0,top:0,width:canvas.width||1000,height:canvas.height||700}),addEventListener:(type,handler)=>events.set(type,handler)};
 if(native)window.chrome={webview:{postMessage:message=>messages.push(message),addEventListener:(type,handler)=>nativeEvents.set(type,handler),releaseBuffer:buffer=>released.push(buffer)}};
 const document={body:{},hidden:false,addEventListener:(type,handler)=>documentEvents.set(type,handler),removeEventListener:type=>documentEvents.delete(type),querySelectorAll:()=>nodes,createElement:()=>({style:{},setAttribute(){},remove(){},getContext:()=>({clearRect(){},drawImage:(canvas,...values)=>copies.push(values)})})};
 const context=vm.createContext({window,document,getComputedStyle:node=>node.computed||{borderRadius:'20px',borderTopLeftRadius:'20px',overflow:'visible'},innerWidth:1000,innerHeight:700,ResizeObserver:class{observe(){}unobserve(){}disconnect(){}},MutationObserver:class{observe(){}disconnect(){}},performance:{now:()=>now},crypto:{getRandomValues:a=>(a[0]=123,a)},
  requestAnimationFrame:callback=>{const id=++nextFrame;pending.set(id,callback);return id;},cancelAnimationFrame:id=>pending.delete(id),
  setTimeout:callback=>{const id=++nextTimer;timers.set(id,callback);return id;},clearTimeout:id=>timers.delete(id),
  Image:class{set src(value){if(value.startsWith('https://prism-backdrop.invalid/'))liveImages.push(this);else{this.url=value;wallpaperImages.push(this);if(!deferWallpaper)this.onload();}}}});
 vm.runInContext(materialSource,context,{filename:'glass-materials.js'});vm.runInContext(source,context,{filename:'glass-renderer.js'});
 window.PrismGlassRenderer(canvas,message=>messages.push(message),callback=>receiver=callback);
 return{messages,liveImages,wallpaperImages,released,copies,scissors,documentEvent:type=>documentEvents.get(type)(),uniform:name=>uniforms.get(name),materials:window.PrismGlassMaterials,allocations:()=>allocations,shared:(data,buffer)=>nativeEvents.get('sharedbufferreceived')({additionalData:data,getBuffer:()=>buffer}),host:value=>nativeEvents.get('message')({data:value}),preferences:value=>windowEvents.get('prism-preferences')({detail:value}),lose:()=>{lost=true;},send:message=>receiver(message),draws:()=>draws,pending:()=>pending.size,
  event(type){let prevented=false;events.get(type)({preventDefault(){prevented=true;}});return prevented;},
  expire(){const callbacks=[...timers.values()];timers.clear();for(const callback of callbacks)callback();},
  frame(ms=16){now+=ms;const callbacks=[...pending.values()];pending.clear();for(const callback of callbacks)callback(now);},
  frames(count){for(let i=0;i<count;i++)this.frame();},
  count(type){return messages.filter(message=>message.startsWith(type+':')).length;},
  latest(type){const message=messages.filter(message=>message.startsWith(type+':')).pop();return message===undefined?undefined:JSON.parse(message.slice(type.length+1));},
  stats(){receiver({type:'stats'});return this.latest('stats');}};
}
const dock={name:'Dock',x:80,y:400,w:640,h:100,r:24,insetY:16,fixedShape:true};
const widget={name:'Weather',x:120,y:100,w:300,h:100,r:24,fixedShape:true};
const layout=cards=>({type:'layout',width:1000,height:700,theme:'Sky',paused:false,cards});
const geometry=(h,cards)=>h.send({type:'geometry',cards});

const wallpaperRace=renderer(false,[],true);
check('repeated wallpaper layouts share one pending request and late old images never win',()=>{
 wallpaperRace.send({type:'wallpaper',version:'new'});wallpaperRace.send({type:'wallpaper',version:'new'});assert.equal(wallpaperRace.wallpaperImages.length,2);
 wallpaperRace.wallpaperImages[1].onload();wallpaperRace.wallpaperImages[0].onload();assert.deepEqual(wallpaperRace.messages.filter(x=>x.startsWith('wallpaper-loaded:')),['wallpaper-loaded:new']);
 wallpaperRace.send({type:'wallpaper',version:'new'});assert.equal(wallpaperRace.wallpaperImages.length,2);
});
check('a failed wallpaper request can retry without discarding the last successful background',()=>{
 wallpaperRace.send({type:'wallpaper',version:'retry'});wallpaperRace.wallpaperImages[2].onerror();wallpaperRace.send({type:'wallpaper',version:'retry'});assert.equal(wallpaperRace.wallpaperImages.length,4);wallpaperRace.wallpaperImages[3].onload();assert.ok(wallpaperRace.messages.includes('wallpaper-loaded:retry'));
});
check('returning to an already loaded wallpaper cancels a newer pending image',()=>{
 wallpaperRace.send({type:'wallpaper',version:'discard'});wallpaperRace.send({type:'wallpaper',version:'retry'});wallpaperRace.wallpaperImages[4].onload();assert.ok(!wallpaperRace.messages.includes('wallpaper-loaded:discard'));
});
const styles=renderer();styles.send(layout([widget]));styles.frame();
check('unselected glass retains the original material path',()=>assert.equal(styles.uniform('styleEnabled'),0));
check('ten immutable materials include classic, Glasswork, frosted grain and warm glass',()=>{
 const catalog=styles.materials;assert.equal(Object.keys(catalog).join(''),'ABCDEFGHIJ');assert.ok(Object.isFrozen(catalog)&&Object.isFrozen(catalog.H.tint));
 assert.ok(catalog.G.grain>0&&catalog.G.blur>catalog.B.blur&&catalog.G.tint[3]<.05);
 assert.ok(catalog.H.tint[0]>catalog.H.tint[2]&&catalog.H.tint[3]<.1&&catalog.H.diffusion<.2);
 assert.equal(catalog.I.legacy,true);assert.equal(catalog.J.model,'glasswork');assert.equal(catalog.J.bevel,40);assert.equal(catalog.J.edge,1.1);assert.equal(catalog.J.power,2.6);assert.equal(catalog.J.dispersion,.15);
});
check('each selected style uploads its shared physical parameters to the production shader',()=>{
 for(const [key,m] of Object.entries(styles.materials)){
  styles.preferences({glassStyle:key});styles.frame();assert.equal(styles.uniform('styleEnabled'),m.legacy?0:1);if(m.legacy)continue;
  assert.deepEqual(styles.uniform('styleShape'),[m.bevel,m.refraction,m.ior,m.dispersion]);
  assert.deepEqual(styles.uniform('styleBody'),[m.blur,m.diffusion,m.saturation,m.grain]);assert.deepEqual(styles.uniform('styleTint'),Array.from(m.tint));
  assert.deepEqual(styles.uniform('styleLight'),[m.reflection,m.highlight,m.shadow,0]);
  assert.deepEqual(styles.uniform('styleTone'),[m.brightness,m.contrast,m.edge,m.power]);assert.deepEqual(styles.uniform('styleVeil'),[m.opacity,m.adaptive,m.density,m.tintMin]);assert.deepEqual(styles.uniform('styleExtra'),[m.tintMax,m.model==='glasswork'?1:0]);
 }
});
check('material changes do not alter or republish window outlines',()=>assert.equal(styles.count('widget-shapes'),1));
check('unrelated preference updates preserve the selected material',()=>{styles.preferences({glassOpacity:80});styles.frame();assert.equal(styles.stats().glassStyle,'J');assert.equal(styles.uniform('styleEnabled'),1);});
check('individual material previews override the global style without changing it',()=>{
 styles.preferences({glassStyle:'G'});geometry(styles,[{...widget,glassStyle:'A'}]);styles.frame();assert.equal(styles.uniform('styleBody')[0],styles.materials.A.blur);assert.equal(styles.stats().glassStyle,'G');
 geometry(styles,[widget]);styles.frame();assert.equal(styles.uniform('styleBody')[3],styles.materials.G.grain);
});
check('empty, classic and invalid selections explicitly restore the original material',()=>{for(const glassStyle of ['','I','toString','K',null]){styles.preferences({glassStyle});styles.frame();assert.equal(styles.uniform('styleEnabled'),0);}});
const labFile=path.join(__dirname,'../src/LiquidDesktop/glass-editor-renderer.js');
check('the canonical editor consumes the same material catalog',()=>{
 const window={PrismGlassMaterials:styles.materials};vm.runInNewContext(fs.readFileSync(labFile,'utf8'),{window});assert.equal(window.GlassLab.presets,styles.materials);
});
check('all twenty-four JavaScript parameter bounds match native persistence validation',()=>{
 const cs=fs.readFileSync(path.join(__dirname,'../src/LiquidDesktop/DesktopPreferences.cs'),'utf8'),body=cs.match(/GlassRanges=new Dictionary<string,double\[\]>\{([\s\S]*?)\};/)[1],native={};
 for(const match of body.matchAll(/\{"([^"]+)",new\[\]\{([\d.]+),([\d.]+)\}\}/g))native[match[1]]=[Number(match[2]),Number(match[3])];
 assert.equal(Object.keys(native).length,24);assert.deepEqual(Object.keys(native).sort(),Object.keys(styles.materials.ranges).sort());
 for(const [field,range] of Object.entries(styles.materials.ranges))assert.deepEqual(native[field],[range.min,range.max],field);
});
check('normalization admits only bounded finite numeric overrides and keeps tint bounds ordered',()=>{
 const catalog=styles.materials,result=catalog.normalizeOverrides({A:{blur:99,brightness:'2',opacity:false,grain:NaN,unknown:1},K:{blur:2}});
 assert.equal(JSON.stringify(result),'{"A":{"blur":30}}');assert.equal(catalog.resolve('A',{A:{tintMin:.4,tintMax:.1}}).tintMin,.1);assert.equal(catalog.resolve('A',{A:{tintMin:.4,tintMax:.1}}).tintMax,.4);
});
check('classic remains exact until a real valid edit and resets without losing the named identity',()=>{
 const catalog=styles.materials;assert.equal(catalog.resolve('',{}).key,'I');assert.ok(catalog.resolve('I',{I:{blur:catalog.I.blur,invalid:1}}).legacy);assert.ok(!catalog.resolve('I',{I:{blur:8}}).legacy);
 styles.preferences({glassStyle:'I',glassOverrides:{I:{blur:8,brightness:1.3}}});styles.frame();assert.equal(styles.uniform('styleEnabled'),1);assert.equal(styles.uniform('styleBody')[0],8);assert.equal(styles.uniform('styleTone')[0],1.3);
 styles.preferences({glassOverrides:{}});styles.frame();assert.equal(styles.uniform('styleEnabled'),0);assert.equal(styles.stats().glassStyle,'I');
});
check('same-style slider changes update actual shader uniforms immediately',()=>{
 styles.preferences({glassStyle:'J',glassOverrides:{J:{edge:1.5,power:4,adaptive:.3,density:.7}}});styles.frame();assert.deepEqual(styles.uniform('styleTone'),[1.06,1,1.5,4]);assert.deepEqual(styles.uniform('styleVeil'),[1,.3,.7,.06]);
 styles.preferences({glassOverrides:{J:{edge:.7,contrast:1.6}}});styles.frame();assert.deepEqual(styles.uniform('styleTone'),[1.06,1.6,.7,2.6]);
});
const customId='user-0123456789abcdef0123456789abcdef';
check('named custom materials are independent snapshots of builtin overrides',()=>{
 const presets=[{id:customId,name:' 我的暖色 ',baseStyle:'H',parameters:{blur:3,brightness:1.2,tintR:.9}}],catalog=styles.materials;
 const resolved=catalog.resolve(customId,{H:{blur:29,brightness:2}},presets);assert.equal(resolved.key,customId);assert.equal(resolved.baseKey,'H');assert.equal(resolved.name,'我的暖色');assert.equal(resolved.blur,3);assert.equal(resolved.brightness,1.2);assert.equal(resolved.tint[0],.9);
 styles.preferences({glassStyle:customId,glassOverrides:{H:{blur:29}},glassPresets:presets});styles.frame();assert.equal(styles.uniform('styleBody')[0],3);assert.equal(styles.uniform('styleTone')[0],1.2);
 presets[0].parameters.blur=5;styles.preferences({glassPresets:presets});styles.frame();assert.equal(styles.uniform('styleBody')[0],5);
});
check('custom classic snapshots preserve the original shader and nested custom bases are rejected',()=>{
 const catalog=styles.materials;assert.ok(catalog.resolve(customId,{I:{blur:20}},[{id:customId,name:'原始',baseStyle:'I',parameters:{}}]).legacy);
 assert.equal(catalog.resolve(customId,{},[{id:customId,name:'嵌套',baseStyle:customId,parameters:{blur:20}}]).key,'I');
 styles.preferences({glassStyle:'',glassOverrides:{},glassPresets:[]});styles.frame();assert.equal(styles.uniform('styleEnabled'),0);
});

const outer={parentElement:null,computed:{overflow:'hidden'},offsetWidth:500,offsetHeight:400,clientLeft:2,clientTop:2,clientWidth:496,clientHeight:396,getBoundingClientRect:()=>({left:100,top:80,width:500,height:400})};
const scroll={parentElement:outer,computed:{overflow:'auto'},offsetWidth:600,offsetHeight:380,clientLeft:4,clientTop:4,clientWidth:592,clientHeight:372,scrollLeft:0,scrollTop:0,getBoundingClientRect:()=>({left:80,top:60,width:600,height:380})};
const cardRect={left:90,top:400,width:300,height:160},surface={parentElement:scroll,isConnected:true,dataset:{glassSurface:'material-G',glassStyle:'G'},hasAttribute:()=>true,before(){},classList:{add(){}},getBoundingClientRect:()=>cardRect};
const clipped=renderer(false,[surface]);clipped.send(layout([]));clipped.frame();
check('a material surface clips against every scrolling ancestor client viewport',()=>{
 assert.deepEqual(clipped.scissors.at(-1),[102,264,314,62]);assert.deepEqual(clipped.copies.at(-1),[102,400,288,36,12,0,288,36]);
 assert.deepEqual(clipped.uniform('center'),[240,480]);assert.deepEqual(clipped.uniform('halfSize'),[150,80]);assert.equal(clipped.uniform('radius'),20);
});
check('scrolling redraws the real surface position without manufacturing a new rounded edge',()=>{
 cardRect.top=300;clipped.documentEvent('scroll');assert.equal(clipped.pending(),1);clipped.frame();
 assert.deepEqual(clipped.scissors.at(-1),[102,264,314,162]);assert.deepEqual(clipped.copies.at(-1),[102,300,288,136,12,0,288,136]);assert.deepEqual(clipped.uniform('halfSize'),[150,80]);
});
check('fully offscreen materials draw no pixels or copies into the footer',()=>{
 const count=clipped.copies.length;cardRect.top=600;clipped.documentEvent('scroll');clipped.frame();assert.equal(clipped.scissors.at(-1)[3],0);assert.equal(clipped.copies.length,count);assert.equal(clipped.stats().surfaces,1);
});

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
const pendingLoss=renderer();pendingLoss.send(layout([dock]));pendingLoss.frame();const pendingLossDraws=pendingLoss.draws();pendingLoss.lose();
check('pending context-loss event does not allow layout or geometry to draw lost resources',()=>{pendingLoss.send({...layout([dock]),width:1200});geometry(pendingLoss,[dock]);pendingLoss.frame();assert.equal(pendingLoss.draws(),pendingLossDraws);assert.ok(!pendingLoss.messages.some(m=>m.startsWith('error:')));});
const recovery=renderer();recovery.send(layout([dock]));recovery.frame();const beforeLoss=recovery.draws();
check('context loss permits restoration and suspends drawing without fatal host messages',()=>{assert.equal(recovery.event('webglcontextlost'),true);geometry(recovery,[dock]);recovery.frames(3);assert.equal(recovery.draws(),beforeLoss);assert.ok(!recovery.messages.some(m=>m.startsWith('error:')));});
check('restoration rebuilds the scene and republishes the native clip and paint readiness',()=>{recovery.event('webglcontextrestored');recovery.frame();assert.ok(recovery.draws()>beforeLoss);assert.equal(recovery.count('dock-shape'),2);assert.equal(recovery.messages.filter(m=>m==='painted').length,2);});
check('successful restoration cancels its failure deadline',()=>{recovery.expire();assert.ok(!recovery.messages.some(m=>m.startsWith('error:')));});
check('unrecoverable context loss still reports a bounded fatal failure',()=>{recovery.event('webglcontextlost');recovery.expire();assert.ok(recovery.messages.includes('error:graphics recovery timed out'));});
const narrow=renderer();narrow.send(layout([{name:'ShelfPanel',x:100,y:80,w:18,h:300,r:26,fixedShape:true}]));narrow.frame();
check('narrow panel corners remain within the surface instead of crossing',()=>{const points=narrow.latest('widget-shapes')[0];assert.ok(points.every(([x,y])=>x>=101.99&&x<=116.01&&y>=81.99&&y<=378.01));});
const live=renderer();live.send(layout([dock]));live.frame();live.expire();
check('wallpaper mode makes no live background requests',()=>assert.equal(live.liveImages.length,0));
live.preferences({liveGlassBackground:true});live.expire();
check('without a native capture bridge the original wallpaper remains available',()=>{assert.equal(live.stats().liveBackground,'wallpaper');assert.equal(live.liveImages.length,0);});
const optical=renderer(true);optical.send(layout([dock]));optical.preferences({liveGlassBackground:true});
const opticalLayout={type:'optical-layout',active:true,width:1000,height:700,sources:[{id:'fixture',x:0,y:0,w:1000,h:700}]};optical.host(opticalLayout);
const pixels=new ArrayBuffer(64);optical.shared({type:'optical-frame',id:'fixture',serial:1,width:4,height:4},pixels);optical.frame();
check('shared native pixels enter the optical shader and acknowledge upload',()=>{assert.equal(optical.stats().liveBackground,'optical');assert.ok(optical.messages.includes('optical-ack:1'));});
const allocated=optical.allocations();optical.host({type:'optical-frame',id:'fixture',serial:2,width:4,height:4});optical.frame();
check('subsequent optical frames reuse the scene allocation and mapped buffer',()=>{assert.equal(optical.allocations(),allocated);assert.equal(optical.stats().opticalFrames,2);assert.ok(optical.messages.includes('optical-ack:2'));});
check('a raised Dock layer gets one reusable scene while sharing the capture buffer',()=>{
 optical.host({...opticalLayout,sources:[{...opticalLayout.sources[0],target:'Dock'},opticalLayout.sources[0]]});optical.frame();
 assert.equal(optical.stats().dockSceneActive,true);assert.equal(optical.released.length,0);assert.equal(optical.allocations(),allocated+1);
 optical.frame();assert.equal(optical.allocations(),allocated+1);optical.host(opticalLayout);optical.frame();assert.equal(optical.stats().dockSceneActive,false);
});
optical.expire();check('native optical frames never start the JPEG request loop',()=>assert.equal(optical.liveImages.length,0));
optical.preferences({liveGlassBackground:false});optical.frame();
check('disabling live optics immediately releases mapped pixels and restores wallpaper',()=>{assert.equal(optical.released.length,1);assert.equal(optical.stats().liveBackground,'wallpaper');});
optical.shared({type:'optical-frame',id:'fixture',serial:3,width:4,height:4},new ArrayBuffer(64));
check('a late shared frame is released and cannot revive disabled optics',()=>{assert.equal(optical.released.length,2);assert.equal(optical.stats().liveBackground,'wallpaper');assert.ok(optical.messages.includes('optical-ack:3'));});
optical.preferences({liveGlassBackground:true});optical.host(opticalLayout);optical.shared({type:'optical-frame',id:'fixture',serial:4,width:4,height:4},new ArrayBuffer(64));optical.host({...opticalLayout,sources:[]});
check('closing a background source releases its shared texture',()=>assert.equal(optical.released.length,3));
const burst=renderer(true);burst.send(layout([dock]));burst.preferences({liveGlassBackground:true});burst.host({...opticalLayout,sources:[...opticalLayout.sources,{id:'second',x:100,y:100,w:200,h:200}]});
burst.shared({type:'optical-frame',id:'fixture',serial:10,width:4,height:4},new ArrayBuffer(64));burst.shared({type:'optical-frame',id:'second',serial:11,width:4,height:4},new ArrayBuffer(64));
check('multiple live sources acknowledge upload before the display frame',()=>{assert.ok(burst.messages.includes('optical-ack:10')&&burst.messages.includes('optical-ack:11'));assert.equal(burst.stats().sceneBuilds||0,0);});
burst.frame();const composed=burst.stats().sceneBuilds;burst.host({type:'optical-frame',id:'fixture',serial:12});burst.host({type:'optical-frame',id:'second',serial:13});burst.frame();
check('a burst of source updates composes the shared scene once per display frame',()=>assert.equal(burst.stats().sceneBuilds-composed,1));
check('glass spring follows the same path at different display refresh rates, including reversal',()=>{
 const slow=renderer(),fast=renderer();
 for(const view of [slow,fast]){view.send(layout([{...widget,fixedShape:false}]));view.frame();view.send({type:'pointer',x:390,y:170});}
 for(let i=0;i<12;i++){slow.frame(16);fast.frame(8);fast.frame(8);}
 assert.ok(Math.abs(slow.stats().maxDeformation-fast.stats().maxDeformation)<1e-8);
 for(const view of [slow,fast])view.send({type:'pointer',x:135,y:110});
 for(let i=0;i<8;i++){slow.frame(16);fast.frame(8);fast.frame(8);}
 assert.ok(Math.abs(slow.stats().maxDeformation-fast.stats().maxDeformation)<1e-8);
});
console.log(passed.length+' glass outline message checks passed.');
