'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const nodes=new Map(),storage=new Map(),listeners={};
function node(id){if(!nodes.has(id))nodes.set(id,{textContent:'',value:'',dataset:{},style:{setProperty(){}},classList:{toggle(){}},setAttribute(){},getAttribute(){return null;},toggleAttribute(){}});return nodes.get(id);}
const context={document:{getElementById:node,querySelector:node,querySelectorAll:()=>[],body:node('body')},localStorage:{getItem:key=>storage.get(key)||null,setItem:(key,value)=>storage.set(key,value)},window:{dispatchEvent(){},addEventListener:(name,callback)=>listeners[name]=callback,chrome:{webview:{postMessage(){},addEventListener:(name,callback)=>listeners['webview-'+name]=callback}}},Event:function(){},CustomEvent:function(){},setInterval(){}};
node('dock').dataset.mode='weather';
vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../src/LiquidDesktop/lifestyle.js'),'utf8'),context);
function receive(weather){listeners['webview-message']({data:{type:'widgets',weather,music:{title:'',artist:'',cover:'',playing:false}}});}
receive({ready:true,temp:21.8,code:0,location:'杭州 · 杭州市 · 浙江 · 中国'});
assert.equal(node('weather-scene-label').textContent,'22° · 杭州 · 杭州市 · 浙江 · 中国');
receive({ready:true,temp:7,code:3,location:'成都 · 四川 · 中国'});
assert.equal(node('weather-scene-label').textContent,'7° · 成都 · 四川 · 中国');
node('dock').dataset.mode='life';
receive({ready:true,temp:18,code:0,location:'示例位置'});
assert.equal(node('weather-scene-label').textContent,'18° · 示例位置');
receive({ready:false,location:'成都 · 四川 · 中国'});
assert.equal(node('weather-scene-label').textContent,'等待实际天气');
console.log('Completed 4 weather presentation checks.');
