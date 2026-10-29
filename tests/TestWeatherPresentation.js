'use strict';
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const nodes=new Map(),storage=new Map(),listeners={};
function node(id){if(!nodes.has(id))nodes.set(id,{textContent:'',value:'',dataset:{},attributes:{},style:{setProperty(){}},classList:{toggle(){}},setAttribute(key,value){this.attributes[key]=String(value);},getAttribute(key){return this.attributes[key]??null;},toggleAttribute(){}});return nodes.get(id);}
class FixtureDate extends Date{constructor(...args){super(...(args.length?args:[Date.UTC(2026,9,29,12)]));}static now(){return Date.UTC(2026,9,29,12);}}
const context={Date:FixtureDate,document:{getElementById:node,querySelector:node,querySelectorAll:selector=>selector==='.landscape'?[node('landscape')]:[],body:node('body')},localStorage:{getItem:key=>storage.get(key)||null,setItem:(key,value)=>storage.set(key,value)},window:{dispatchEvent(){},addEventListener:(name,callback)=>listeners[name]=callback,chrome:{webview:{postMessage(){},addEventListener:(name,callback)=>listeners['webview-'+name]=callback}}},Event:function(){},CustomEvent:function(){},setInterval(){}};
node('dock').dataset.mode='weather';
vm.runInNewContext(fs.readFileSync(path.join(__dirname,'../src/LiquidDesktop/lifestyle.js'),'utf8'),context);
function receive(weather,available=true){listeners['webview-message']({data:{type:'widgets',weather,sourceStatus:{weather:{available}},music:{title:'',artist:'',cover:'',playing:false}}});}
receive({ready:true,temp:21.8,code:0,location:'杭州 · 杭州市 · 浙江 · 中国'});
assert.equal(node('weather-scene-label').textContent,'天气景观 · 示意');assert.equal(node('landscape').dataset.code,'clear');
receive({ready:true,temp:7,code:3,stale:true,location:'成都 · 四川 · 中国'});
assert.equal(node('weather-scene-label').textContent,'上次天气 · 景观示意');assert.equal(node('landscape').dataset.code,'cloud');
node('dock').dataset.mode='life';
receive({ready:true,temp:18,code:0,location:'示例位置'});
assert.equal(node('weather-scene-label').textContent,'天气景观 · 示意');
receive({ready:false,location:'成都 · 四川 · 中国'});
assert.equal(node('weather-scene-label').textContent,'天气景观 · 暂无有效数据');assert.equal(node('landscape').dataset.code,'unknown');
receive({ready:true,temp:18,code:0},false);assert.equal(node('landscape').dataset.code,'unknown','An expired source must not keep showing a sunny scene');
receive({ready:true,temp:0,code:null,sunrise:'invalid',sunset:'invalid'});assert.equal(node('landscape').dataset.code,'unknown','Missing weather code is not clear or cloudy');assert.equal(node('solar-status').textContent,'日照数据暂不可用');assert.equal(node('sun-times').textContent,'日出日落暂不可用');
receive({ready:true,temp:null,code:0});assert.equal(node('weather-scene-label').textContent,'天气景观 · 暂无有效数据','Missing temperature is not a valid zero degree report');
receive({ready:true,temp:20,code:1,sunrise:'2026-10-29T06:00',sunset:'2026-10-29T18:00',utcOffsetSeconds:-14400});assert.equal(node('solar-status').textContent,'白昼 12 小时');assert.ok(Math.abs(Number(node('solar-marker').attributes.cx)-(20+280/6))<.0001,'Sunrise geometry uses the selected city timezone instead of the computer timezone');
console.log('Completed 8 weather presentation checks.');
