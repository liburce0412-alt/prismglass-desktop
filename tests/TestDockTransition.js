'use strict';
// Real desktop.js, a deterministic RAF and an explicitly acknowledged native skin.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const source=fs.readFileSync(path.join(__dirname,'../src/LiquidDesktop/desktop.js'),'utf8');
let checks=0;
function check(name,action){action();checks++;console.log('PASS '+name);}
const lyric={type:'lyrics',show:true,current:'切换时仍然连续',next:'保留既有样式',motionPaused:false};
function fixture(width=1070){
 let now=0,receiver,nextId=0,nativeAlpha=255,auto=true;
 const frames=new Map(),timers=new Map(),glass=[],commands=[],pending=[];
 function element(){const classes=new Set();return{style:{setProperty(){}},parentElement:{hidden:false,classList:{toggle(){}}},scrollWidth:240,textContent:'',classList:{toggle(n,on){if(on)classes.add(n);else classes.delete(n);},contains:n=>classes.has(n)},setAttribute(){},animate:()=>({cancel(){}})};}
 const elements=new Map(['lyrics-layer','dock-lyrics','lyric-current','lyric-next','scene'].map(id=>[id,element()]));
 const window={addEventListener(){},chrome:{webview:{addEventListener:(_,fn)=>receiver=m=>fn({data:m}),postMessage(message){if(!message.startsWith('dock-icons:'))return;const [,sequence,alpha]=message.split(':').map(Number);const value={sequence,alpha,time:now};commands.push(value);pending.push(value);}}}};
 const context=vm.createContext({window,document:{getElementById:id=>elements.get(id)},innerWidth:1200,innerHeight:800,performance:{now:()=>now},matchMedia:()=>({matches:false,addEventListener(){}}),
  PrismLyrics:{normalize:v=>({layout:'duo',width:900,widthMode:'adaptive',fontSize:26,...v}),apply(){},cancel(){},animate(){},background(){}},PrismGlassRenderer:(_,post,subscribe)=>subscribe(m=>glass.push(m)),
  requestAnimationFrame:fn=>{const id=++nextId;frames.set(id,fn);return id;},cancelAnimationFrame:id=>frames.delete(id),setTimeout:(fn,delay)=>{const id=++nextId;timers.set(id,{fn,due:now+delay});return id;},clearTimeout:id=>timers.delete(id)});
 vm.runInContext(source,context,{filename:'desktop.js'});
 function invariant(){assert.ok(Number(elements.get('dock-lyrics').style.opacity||0)===0||nativeAlpha===0,'text and application icons must never overlap');}
 function ack(command){nativeAlpha=command.alpha;invariant();receiver({type:'dock-icons-ack',sequence:command.sequence,alpha:command.alpha});invariant();}
 function flush(){if(auto){let guard=0;while(pending.length){assert.ok(++guard<100,'no acknowledgement loop');ack(pending.shift());}}}
 function send(message){receiver(message);invariant();flush();}
 function frame(count=1){for(let i=0;i<count;i++){now+=16;for(const [id,t] of [...timers])if(t.due<=now){timers.delete(id);t.fn();}const work=[...frames.values()];frames.clear();for(const fn of work){fn(now);invariant();}flush();}}
 const dock={name:'Dock',x:65,y:620,w:width,h:100,r:28,insetY:16};
 send({type:'layout',width:1200,height:800,theme:'Sky',paused:false,cards:[dock]});frame();
 return{send,frame,ack,commands,pending,glass,elements,auto(value){auto=value;flush();},get state(){return window.dockLyricsState;},get nativeAlpha(){return nativeAlpha;},get card(){return glass.filter(m=>m.type==='geometry').pop().cards.find(c=>c.name==='Dock');}};
}
const f=fixture();f.send(lyric);f.frame(10);
check('entry progressively contracts with the native fade on the same frame clock',()=>{assert.ok(f.state.width<1070&&f.state.width>f.state.targetWidth);assert.equal(f.card.fixedShape,true);assert.ok(f.commands.some(c=>c.alpha>0&&c.alpha<255));});
f.frame(65);
check('acknowledged zero opacity permits lyric text',()=>{assert.equal(f.nativeAlpha,0);assert.ok(f.state.opacity>.99);});
const settled=f.state.width;f.send({type:'pointer',x:600,y:670,down:false});
check('pointer exit preserves text opacity and current geometry at the reversal frame',()=>{assert.equal(f.state.width,settled);assert.ok(f.state.opacity>.99);});
f.frame(8);
check('exit expands progressively with no text/icon overlap on every sampled frame',()=>{assert.ok(f.state.width>settled&&f.state.width<1070);assert.ok(f.state.velocity>0);assert.equal(f.card.fixedShape,true);});
const midway=f.state.width,velocity=f.state.velocity,opacity=f.state.opacity;f.send(lyric);
check('rapid reversal preserves current position, velocity and acknowledged text opacity',()=>{assert.equal(f.state.width,midway);assert.equal(f.state.velocity,velocity);assert.equal(f.state.opacity,opacity);});
f.frame(90);f.send({type:'lyrics',show:false,current:'',next:'',motionPaused:false});f.frame(100);
check('settled exit restores icons and the original glass hover shape',()=>{assert.equal(f.state.width,1070);assert.equal(f.nativeAlpha,255);assert.equal(f.card.fixedShape,false);});
const slow=fixture();slow.auto(false);slow.send(lyric);slow.frame(40);
check('missing acknowledgement gates entry at 18 percent before text appears',()=>{assert.equal(slow.state.reveal,.18);assert.equal(slow.state.opacity,0);assert.ok(slow.state.width>slow.state.targetWidth);});
check('a busy host receives one pending command while frame values are coalesced',()=>assert.equal(slow.pending.length,1));
const renders=slow.glass.length;slow.frame(5);
check('waiting for native completion does not continuously repaint glass',()=>assert.equal(slow.glass.length,renders));
const first=slow.pending.shift();slow.ack(first);
check('pending completion sends the newest zero value instead of an intermediate queue',()=>{assert.equal(slow.pending.length,1);assert.equal(slow.pending[0].alpha,0);});
const zero=slow.pending.shift();slow.send({type:'lyrics',show:false,current:'',next:''});slow.send(lyric);slow.ack(zero);
check('a delayed zero acknowledgement from the previous direction cannot reveal lyrics',()=>{assert.equal(slow.state.iconsHidden,false);assert.equal(slow.state.opacity,0);assert.equal(slow.pending.length,1);});
slow.auto(true);slow.frame(85);
check('the current generation acknowledgement releases the held spring',()=>assert.ok(slow.state.opacity>.99));
const lost=fixture();lost.auto(false);lost.send(lyric);lost.frame(140);
check('lost acknowledgements retry latest alpha at most once per 750 milliseconds',()=>{const sent=lost.commands.slice(1);assert.equal(sent.length,3);assert.ok(sent.slice(1).every((c,i)=>c.time-sent[i].time>=750));assert.ok(sent.slice(1).every(c=>c.alpha===0));});
const equal=fixture(360);equal.send(lyric);equal.frame(90);
check('equal-width capsules still fade icons and reveal text through independent progress',()=>{assert.equal(equal.state.width,360);assert.equal(equal.nativeAlpha,0);assert.ok(equal.state.opacity>.99);});
equal.elements.get('lyric-current').scrollWidth=600;equal.send({...lyric,current:'换行歌词保持同一隐藏状态'});equal.frame(40);
check('changing lyric width while active cannot revive icons or reset the reveal',()=>{assert.equal(equal.nativeAlpha,0);assert.equal(equal.state.reveal,1);assert.ok(equal.state.opacity>.99);});
const quiet=fixture();quiet.auto(false);quiet.send({...lyric,motionPaused:true});quiet.frame(20);
check('reduced motion holds full geometry until the native zero acknowledgement',()=>{assert.equal(quiet.state.width,1070);assert.equal(quiet.state.opacity,0);assert.equal(quiet.pending[0].alpha,0);});
quiet.auto(true);
check('reduced motion applies the capsule and text together after acknowledgement',()=>{assert.equal(quiet.state.width,quiet.state.targetWidth);assert.equal(quiet.state.opacity,1);assert.equal(quiet.nativeAlpha,0);});
quiet.send({type:'lyrics',show:false,current:'',next:'',motionPaused:true});
check('reduced-motion exit restores geometry and icons without animation',()=>{assert.equal(quiet.state.width,1070);assert.equal(quiet.state.opacity,0);assert.equal(quiet.nativeAlpha,255);});
const lease=fixture();lease.send(lyric);lease.frame(90);lease.auto(false);lease.frame(110);
check('lost renewal hides text before the native three-second lease can restore icons',()=>assert.equal(lease.state.opacity,0));
lease.frame(100);
check('expired lyric heartbeat restores the application Dock even without native replies',()=>{assert.equal(lease.state.active,false);assert.equal(lease.state.width,1070);assert.equal(lease.state.opacity,0);});
const renewal=fixture();renewal.send(lyric);renewal.frame(150);const before=renewal.commands.length;renewal.send(lyric);
check('unchanged settled alpha is renewed by the existing lyric heartbeat',()=>{assert.equal(renewal.commands.length,before+1);assert.equal(renewal.commands.at(-1).alpha,0);});
const fade=fixture();fade.send(lyric);fade.frame(90);fade.send({type:'lyrics',show:false,current:'',next:''});let partial=false;
for(let i=0;i<70;i++){fade.frame();if(fade.state.opacity>0&&fade.state.opacity<1)partial=true;}
check('lyrics fade out through intermediate opacity before any icon returns',()=>{assert.equal(partial,true);assert.equal(fade.state.opacity,0);assert.equal(fade.nativeAlpha,255);});
console.log(checks+' Dock transition checks passed.');

