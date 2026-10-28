'use strict';
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const nodes=new Map(),messages=[],timers=new Map();let timer=0;
function node(id){if(!nodes.has(id))nodes.set(id,{dataset:{action:id==='refresh'?'refresh':''},style:{},classList:{toggle(){}},events:{},addEventListener(e,f){this.events[e]=f;},setAttribute(k,v){this[k]=v;}});return nodes.get(id);}
const window={chrome:{webview:{postMessage:m=>messages.push(m),addEventListener(){}}},addEventListener(){}};
const context={window,document:{getElementById:node,querySelector:()=>node('refresh'),querySelectorAll:s=>s==='[data-action]'?[node('refresh')]:[],addEventListener(){}},matchMedia:()=>({matches:false}),PrismGlassRenderer:(c,send,ready)=>ready(()=>{}),setInterval:()=>1,clearInterval(){},requestAnimationFrame:()=>1,cancelAnimationFrame(){},performance:{now:()=>0},setTimeout:f=>{timers.set(++timer,f);return timer;},clearTimeout:id=>timers.delete(id)};
vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../src/LiquidDesktop/island.js'),'utf8'),context);
node('refresh').events.click();assert.ok(messages.includes('action:refresh'));
window.PrismIsland.receive({type:'wallpaper-refresh',busy:true,message:'正在更新玻璃…'});
assert.equal(node('refresh').disabled,true);assert.equal(node('wallpaper-status').hidden,false);assert.equal(node('wallpaper-status').textContent,'正在更新玻璃…');
window.PrismIsland.receive({type:'wallpaper-refresh',busy:false,failed:true,message:'更新失败'});
assert.equal(node('refresh').disabled,false);assert.equal(node('refresh').textContent,'!');assert.equal(node('wallpaper-status').textContent,'更新失败');
window.PrismIsland.receive({type:'wallpaper-refresh',busy:true,message:'重试中'});assert.equal(timers.size,0);
window.PrismIsland.receive({type:'wallpaper-refresh',busy:false,failed:false,message:'壁纸玻璃已更新'});
assert.equal(node('refresh').textContent,'✓');assert.equal(node('refresh')['aria-label'],'壁纸玻璃已更新');
for(const f of timers.values())f();assert.equal(node('wallpaper-status').hidden,true);
console.log('Wallpaper click, progress, failure, retry and success feedback passed.');
