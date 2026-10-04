'use strict';
(() => {
const $=id=>document.getElementById(id),post=text=>window.chrome?.webview?.postMessage(text);
const dock=$('dock'),detail=$('detail'),capsule=$('capsule'),pages=$('status-pages');
const reduced=matchMedia('(prefers-reduced-motion: reduce)');
let panelHeight=552,panelTop=4;let bridgeY=40;let mode='closed',pinned=false,leaveTimer=0,openTimer=0,raf=0,last=0,frameCount=0,theme='Sky';
let motionPaused=false,monthOffset=0,glassReceive=null,glassLayout=null;
let railHeight=668,visibilityKey=null;
const motionOff=()=>motionPaused||reduced.matches;
PrismGlassRenderer($('popup-glass'),s=>{if(s==='painted')document.body.dataset.glassReady='true';if(s.startsWith('error:'))post(s);},receive=>glassReceive=receive);
function updateGlass(rebuild=false){
 if(!glassLayout||!glassReceive)return;const m=glassLayout,scale=m.scale,w=Math.max(0,springs.width.x),a=Math.max(0,Math.min(1,springs.opacity.x));
 const cards=railHeight>0?[{name:'Rail',x:4*scale,y:4*scale,w:88*scale,h:railHeight*scale,r:28*scale,fixedShape:true,rimStrength:.32}]:[];
 for(const el of document.querySelectorAll('.rail-item')){if(!el.matches(':hover'))continue;const r=el.getBoundingClientRect();cards.push({name:el.dataset.widget,x:r.x,y:r.y,w:r.width,h:r.height,r:18*scale,rimStrength:.12});}
 if(w>1)cards.push({name:'Detail',x:108*scale,y:panelTop*scale,w:w*scale,h:panelHeight*scale,r:28*scale,fixedShape:true,rimStrength:.32});
 glassReceive({type:rebuild?'layout':'geometry',width:m.viewWidth||Math.ceil(852*scale),height:m.viewHeight||Math.ceil(680*scale),sceneWidth:m.width,sceneHeight:m.height,originX:m.x,originY:m.y,cards,theme,paused:motionOff()});
}
let battery={},network={},latestWidgets=null;
const springs={height:{x:552,v:0,target:552},top:{x:4,v:0,target:4},width:{x:0,v:0,target:0},opacity:{x:0,v:0,target:0},capsule:{x:0,v:0,target:0},network:{x:0,v:0,target:0},page:{x:0,v:0,target:0}};
function step(s,dt){const omega=24,y=s.x-s.target,j=s.v+omega*y,e=Math.exp(-omega*dt);s.x=s.target+(y+j*dt)*e;s.v=(s.v-omega*j*dt)*e;if(Math.abs(s.target-s.x)<.015&&Math.abs(s.v)<.03){s.x=s.target;s.v=0;}return s.x;}
function report(){post('status:'+JSON.stringify({mode,pinned,width:Math.round(springs.width.x),frames:frameCount,reducedMotion:reduced.matches,theme,batteryAvailable:typeof battery.percent==='number',networkConnected:!!network.connected}));}
function render(now){
 raf=0;const dt=Math.min((now-last)/1000||.016,.034);last=now;let moving=false;
 for(const s of Object.values(springs)){if(motionOff()){s.x=s.target;s.v=0;}else step(s,dt);if(s.x!==s.target||s.v!==0)moving=true;}
 const w=Math.max(0,springs.width.x),a=Math.max(0,Math.min(1,springs.opacity.x)),m=springs.capsule.x,n=springs.network.x;
 panelHeight=springs.height.x;panelTop=springs.top.x;detail.style.top=panelTop+'px';detail.style.height=panelHeight+'px';detail.style.setProperty('--panel-height',panelHeight+'px');detail.style.width=w+'px';detail.style.opacity=a;detail.style.pointerEvents=a>.1?'auto':'none';
 detail.style.transform='none';detail.querySelectorAll('.page').forEach(el=>el.style.visibility=a>.995?'visible':'hidden');
 $('bridge').style.opacity=a;
 capsule.style.background=`rgba(2,13,24,${.16+m*.34})`;
 // The reference ring remains centered in both idle and expanded states.
 capsule.style.background='transparent';
 $('rail').style.borderRadius='28px';
 pages.style.transform=`translateX(${-368*springs.page.x}px)`;
 updateGlass();frameCount++;if(frameCount===1)post('painted');post('shape:'+JSON.stringify([w,panelHeight,n,bridgeY,panelTop,railHeight]));
 if(moving)raf=requestAnimationFrame(render);else report();
}
function wake(){if(!raf){last=performance.now();raf=requestAnimationFrame(render);}}
function setPanelSize(height,keepAnchor=false){const h=Math.max(330,Math.min(640,height)),top=Math.max(4,Math.min(668-h,keepAnchor?springs.top.target:bridgeY-32));springs.height.target=h;springs.top.target=top;wake();}
function fitContent(){if(mode==='closed'||$('widget-page').hidden)return;setPanelSize(Math.ceil($('widget-page').scrollHeight)+$('detail').querySelector('.panel-top').offsetHeight+8,true);}
function applyVisibility(values){
 const names=new Set(['network','weather','music','focus','folders','system','tools','life']);
 const hidden=new Set((Array.isArray(values)?values:[]).filter(value=>typeof value==='string').map(value=>value.toLowerCase()).filter(value=>names.has(value))),key=[...hidden].sort().join('|');
 if(key===visibilityKey)return;visibilityKey=key;clearTimeout(openTimer);openTimer=0;
 capsule.hidden=hidden.has('network');document.querySelectorAll('[data-widget]').forEach(el=>el.hidden=hidden.has(el.dataset.widget));
 const rail=$('rail'),any=hidden.size<names.size;rail.hidden=!any;$('battery-trigger').hidden=!any;
 if(!any)railHeight=0;else if(hidden.size===0)railHeight=668;else{
  const css=getComputedStyle(rail),children=[...rail.children].filter(el=>!el.hidden&&getComputedStyle(el).display!=='none'),number=value=>parseFloat(value)||0;
  railHeight=Math.min(668,Math.max(72,number(css.paddingTop)+number(css.paddingBottom)+number(css.rowGap)*(children.length-1)+children.reduce((height,el)=>{const style=getComputedStyle(el);return height+el.offsetHeight+number(style.marginTop)+number(style.marginBottom)},0)));
 }
 rail.style.setProperty('height',railHeight+'px','important');
 if(!any||hidden.has(mode)||(mode==='both'&&hidden.has('network')))choose('closed');else if(mode!=='closed')choose(mode,pinned);else wake();
}
window.addEventListener('prism-content-change',()=>requestAnimationFrame(fitContent));
function choose(next,pin=false){
 clearTimeout(openTimer);openTimer=0;
 $('action-error')?.remove();
 clearTimeout(leaveTimer);const selected=document.querySelector(`[data-widget="${next}"]`);bridgeY=selected?selected.offsetTop+14:40;document.getElementById("bridge").style.top=bridgeY+"px";mode=next;pinned=pin;dock.dataset.mode=next;setPanelSize(({weather:610,music:570,focus:570,folders:405,system:450,tools:490,life:610,calendar:552})[next]||552);
 const open=next!=='closed';$('rail').style.opacity='1';const widget=document.querySelector(`[data-widget-page="${next}"]`);$('widget-page').hidden=!widget;document.querySelectorAll('[data-widget-page]').forEach(el=>el.hidden=el!==widget);document.querySelectorAll('[data-widget]').forEach(el=>{el.setAttribute('aria-expanded',String(el.dataset.widget===next));el.classList.toggle('selected',el.dataset.widget===next);});if(widget)text('widget-title',document.querySelector(`[data-widget="${next}"] .rail-label`).textContent);document.querySelector('.tabs').style.visibility=widget||next==='calendar'?'hidden':'visible';springs.width.target=open?(next==='both'?736:368):0;springs.opacity.target=open?1:0;springs.capsule.target=open?1:0;springs.network.target=next==='network'?1:0;springs.page.target=next==='network'?1:0;
 detail.inert=!open;detail.setAttribute('aria-hidden',String(!open));
 $('battery-trigger').setAttribute('aria-expanded',String(next==='battery'||next==='both'));
 $('wifi-trigger').setAttribute('aria-expanded',String(next==='network'||next==='both'));
 for(const tab of document.querySelectorAll('[data-tab]')){tab.setAttribute('aria-selected',String(tab.dataset.tab===next));tab.tabIndex=tab.dataset.tab===next?0:-1;}
 $('calendar-page').hidden=next!=='calendar';pages.style.display=next==='calendar'||widget?'none':'flex';
 $('battery-page').inert=next==='network'||next==='calendar'||!open;
 $('network-page').inert=next!=='network'&&next!=='both';
 $('battery-page').style.opacity=next==='network'?'0':'1';$('network-page').style.opacity=next==='battery'?'0':'1';
 if(Object.keys(battery).length)batteryData(battery);if(Object.keys(network).length)networkData(network);if(latestWidgets)widgetData(latestWidgets,false);
 window.dispatchEvent(new CustomEvent('prism-panel-change',{detail:{mode}}));
 if(next==='calendar')clock();requestAnimationFrame(fitContent);wake();
}
function trigger(id,next){
 $(id).addEventListener('pointerenter',()=>{if(!pinned)choose(next);});
 $(id).addEventListener('click',()=>choose(mode===next&&pinned?'closed':next,!(mode===next&&pinned)));
 $(id).addEventListener('focus',()=>{if(!pinned)choose(next);});
}
document.querySelectorAll('[data-widget]').forEach(el=>triggerWidget(el));
function triggerWidget(el){el.addEventListener('pointerenter',()=>{if(!pinned){clearTimeout(leaveTimer);clearTimeout(openTimer);openTimer=setTimeout(()=>{openTimer=0;if(el.matches(':hover')&&!pinned)choose(el.dataset.widget);},120);}});el.addEventListener('pointerleave',()=>{clearTimeout(openTimer);openTimer=0;});el.addEventListener('focus',()=>{if(!pinned)choose(el.dataset.widget);});el.addEventListener('click',()=>choose(mode===el.dataset.widget&&pinned?'closed':el.dataset.widget,!(mode===el.dataset.widget&&pinned)));}
document.querySelectorAll('[data-action]').forEach(el=>el.addEventListener('click',()=>post('action:'+el.dataset.action)));
trigger('battery-trigger','battery');trigger('wifi-trigger','network');
dock.addEventListener('pointerleave',()=>{clearTimeout(openTimer);openTimer=0;clearTimeout(leaveTimer);if(!pinned)leaveTimer=setTimeout(()=>choose('closed'),260);});
dock.addEventListener('pointerenter',()=>clearTimeout(leaveTimer));
detail.addEventListener('pointerenter',()=>clearTimeout(leaveTimer));
detail.addEventListener('pointerdown',()=>{clearTimeout(leaveTimer);pinned=true;});
$('close-panel').addEventListener('click',()=>{choose('closed');$('battery-trigger').focus({preventScroll:true});choose('closed');});
document.addEventListener('keydown',e=>{if(e.key==='Escape'){choose('closed');document.activeElement?.blur();}if(e.key==='ArrowRight'||e.key==='ArrowLeft'){const tab=e.target.closest('[data-tab]');if(tab){e.preventDefault();const names=['battery','network','both'],index=names.indexOf(tab.dataset.tab),next=names[(index+(e.key==='ArrowRight'?1:2))%3];choose(next,true);document.querySelector(`[data-tab=${next}]`).focus();}}});
document.querySelectorAll('[data-tab]').forEach(tab=>tab.addEventListener('click',()=>choose(tab.dataset.tab,true)));
$('power-settings').addEventListener('click',()=>post('settings-power'));
$('network-settings').addEventListener('click',()=>post('settings-network'));
const text=(id,value)=>{const el=$(id),next=String(value);if(el.textContent!==next)el.textContent=next;};
function batteryData(b){
 battery=b;const valid=typeof b.percent==='number';text('compact-battery',valid?b.percent:'—');
 if(mode!=='battery'&&mode!=='both')return;
 $('battery-percent').replaceChildren(document.createTextNode(valid?String(b.percent):'—'),Object.assign(document.createElement('small'),{textContent:valid?'%':''}));
 const state=!b.present?'未检测到电池':b.charging?'正在充电':b.plugged?'已接通电源':'正在使用电池';
 const time=!b.plugged&&b.seconds>0?` · 约 ${Math.floor(b.seconds/3600)} 小时 ${Math.floor(b.seconds%3600/60)} 分钟`:'';
 text('battery-label',b.plugged?'外接电源':'电池供电');text('battery-status',state+time);
 $('battery-fill').style.width=(valid?b.percent:0)+'%';$('charge-symbol').style.display=b.charging?'block':'none';
 text('battery-health',typeof b.health==='number'?b.health+'% 容量':'系统未提供');text('battery-cycles',typeof b.cycles==='number'?b.cycles+' 次':'系统未提供');
 const now=Date.now()/1000,bins=Array.from({length:48},()=>null);
 for(const sample of b.history||[]){const index=47-Math.floor((now-sample.time)/1800);if(index>=0&&index<48)bins[index]=sample;}
 $('battery-chart').replaceChildren(...bins.map(sample=>{const bar=document.createElement('i');if(sample){bar.className='known'+(sample.charging?' charging':'');bar.style.height=Math.max(3,sample.percent*.74)+'px';bar.title=new Date(sample.time*1000).toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'})+' · '+sample.percent+'%';}return bar;}));
 const count=bins.filter(Boolean).length;
 text('history-note',!b.present?'此设备未报告电池信息。':count<2?'已开始记录电量；空白时段暂无数据。':'蓝色表示接通电源；空白时段暂无数据。');
 $('battery-chart').setAttribute('aria-label',`最近 24 小时电量记录，${count} 个时段有数据，当前${valid?b.percent+'%':'不可用'}`);
}
function networkData(n){
 network=n;if(mode!=='network'&&mode!=='both')return;
 network=n;text('network-name',n.name||'未连接');text('network-status',n.connected?(n.wifi?'Wi-Fi 已连接':'有线网络已连接'):'当前没有网络连接');
 text('network-type',n.connected?(n.wifi?'Wi-Fi':'以太网'):'离线');text('network-speed',n.speed>0?n.speed+' Mbps':'—');
 text('network-rates',n.receive>0&&n.transmit>0?`${n.receive} / ${n.transmit} Mbps`:'系统未提供');
 text('signal-quality',typeof n.signal==='number'?n.signal+'%':n.wifi?'系统未提供':'—');
 const strength=typeof n.signal==='number'?Math.ceil(n.signal/20):0;
 [...$('signal-bars').children].forEach((bar,i)=>bar.classList.toggle('on',i<strength));
 text('network-adapter',n.adapter||'');$('wifi-trigger').style.color=n.connected?'#e8f7ff':'#8ca2b2';
}
let widgetFocus=null,cpuHistory=[];
function focusClock(){if(!widgetFocus)return;const f=widgetFocus,n=f.deadline>0?Math.max(0,f.deadline-Math.floor(Date.now()/1000)):f.remaining,t=Math.floor(n/60).toString().padStart(2,'0')+':'+Math.floor(n%60).toString().padStart(2,'0');text('rail-focus',f.available?t:'—');if(mode!=='focus')return;text('widget-focus-time',f.available?t:'—');text('widget-focus-toggle',f.deadline>0?'暂停':n===0?'再来一轮':'开始');text('widget-focus-state',!f.available?'计时器暂不可用':f.deadline>0?'专注于眼前这一件事':n===0?'时间到了，活动一下吧':'给自己一段不被打扰的时间');$('widget-focus-progress').value=1-n/Math.max(1,f.total);}
function widgetData(m,sample=true){
 latestWidgets=m;const w=m.weather,valid=w&&w.ready,s=m.system;
 text('rail-weather',valid?Math.round(w.temp)+'°':'—°');text('rail-system',s?Math.round(s.cpu)+'%':'—');text('rail-music',m.music.title==='网易云音乐'||m.music.title==='等待播放'?'待播放':'当前曲目');widgetFocus=m.focus;focusClock();
 if(s&&sample){cpuHistory.push(s.cpu);if(cpuHistory.length>60)cpuHistory.shift();}
 if(mode==='weather'){text('weather-temp',valid?Math.round(w.temp)+'°':'—°');text('weather-high',valid?Math.round(w.high)+'°':'—');text('weather-low',valid?Math.round(w.low)+'°':'—');const c=w?.code;text('weather-condition',!valid?'天气暂不可用':c===0?'晴':c<=3?'多云':c<=48?'雾':c<=67?'有雨':c<=77?'有雪':c<=82?'阵雨':c<=86?'阵雪':'雷雨');}
 if(mode==='system'){text('system-cpu',s?Math.round(s.cpu)+'%':'—');text('system-ram',s?Math.round(s.ram)+'%':'—');$('cpu-progress').value=s?.cpu||0;$('ram-progress').value=s?.ram||0;const rate=n=>n>1048576?(n/1048576).toFixed(1)+' MB/s':(n/1024).toFixed(1)+' KB/s';text('system-network',s?'↓ '+rate(s.down)+'　↑ '+rate(s.up):'性能数据暂不可用');$('cpu-history').querySelector('polyline').setAttribute('points',cpuHistory.map((v,i)=>(i*320/59)+','+(68-v*.65)).join(' '));}
 if(mode==='music'){text('music-title',m.music.title);text('music-artist',m.music.artist);text('music-volume',m.volume?'音量 '+Math.round(m.volume.value)+'%':'音量 —');const img=$('music-cover');if(m.music.cover&&img.getAttribute('src')!==m.music.cover)img.src=m.music.cover;img.hidden=!m.music.cover;$('music-symbol').hidden=!!m.music.cover;}
}
let dayKey='';
function calendar(){
 const now=new Date(),date=new Date(now.getFullYear(),now.getMonth()+monthOffset,1),first=(date.getDay()+6)%7,days=new Date(date.getFullYear(),date.getMonth()+1,0).getDate();
 text('calendar-today',date.getFullYear()+' 年 '+(date.getMonth()+1)+' 月');
 const full=['一','二','三','四','五','六','日'].map(day=>Object.assign(document.createElement('span'),{className:'weekday',textContent:day}));
 for(let i=0;i<42;i++){const d=i-first+1,el=document.createElement('span');el.textContent=d>0&&d<=days?d:'';if(monthOffset===0&&d===now.getDate())el.className='today';full.push(el);}
 $('full-calendar').replaceChildren(...full);
}
$('previous-month').onclick=()=>{monthOffset--;calendar();};$('next-month').onclick=()=>{monthOffset++;calendar();};$('calendar-today').onclick=()=>{monthOffset=0;calendar();};
for(let i=1;i<=12;i++){const mark=document.createElement('em');mark.textContent=i;mark.style.setProperty('--hour',i);document.querySelector('.clock-face').append(mark);}
function clock(){
 if(mode!=='calendar')return;
 const now=new Date(),hours=now.getHours(),minutes=now.getMinutes(),seconds=now.getSeconds();
 document.querySelector('.hand.hour').style.transform=`rotate(${hours%12*30+minutes*.5}deg)`;
 document.querySelector('.hand.minute').style.transform=`rotate(${minutes*6}deg)`;
 document.querySelector('.hand.second').style.transform=`rotate(${seconds*6}deg)`;
 text('large-time',now.toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit',hour12:false}));
 if(dayKey===now.toDateString())return;dayKey=now.toDateString();
 text('today-date',now.toLocaleDateString('zh-CN'));text('full-date',now.toLocaleDateString('zh-CN',{month:'long',day:'numeric',weekday:'long'}));calendar();
}
window.chrome?.webview?.addEventListener('message',e=>{
 const m=e.data;
 if(m.type==='layout'){
  const previous=glassLayout;theme=m.theme;glassLayout=m;const canvas=$('popup-glass');canvas.style.left='0px';canvas.style.top='0px';canvas.style.width=(m.viewWidth||Math.ceil(852*m.scale))+'px';canvas.style.height=(m.viewHeight||Math.ceil(680*m.scale))+'px';updateGlass(!previous||previous.width!==m.width||previous.height!==m.height||previous.viewWidth!==m.viewWidth||previous.viewHeight!==m.viewHeight||previous.x!==m.x||previous.y!==m.y||previous.theme!==m.theme);if(!previous||previous.wallpaper!==m.wallpaper)glassReceive({type:'wallpaper',version:m.wallpaper});document.body.dataset.theme=theme;document.documentElement.style.setProperty('--scale',m.scale);
  document.documentElement.style.setProperty('--wall-x',-m.x+'px');document.documentElement.style.setProperty('--wall-y',-m.y+'px');document.documentElement.style.setProperty('--screen-w',m.width+'px');document.documentElement.style.setProperty('--screen-h',m.height+'px');
  document.body.style.backgroundImage=`url(wallpaper.png?v=${encodeURIComponent(m.wallpaper)})`;
  wake();
 }else if(m.type==='data'){batteryData(m.battery);networkData(m.network);clock();report();}
 else if(m.type==='widgets')widgetData(m);
 else if(m.type==='open')choose(mode===m.mode&&pinned?'closed':m.mode,!(mode===m.mode&&pinned));
 else if(m.type==='motion'){motionPaused=!!m.paused;document.body.classList.toggle('motion-paused',motionPaused);updateGlass(true);wake();}
 else if(m.type==='visibility')applyVisibility(m.hiddenLeftCards);
 else if(m.type==='action-error'){let notice=$('action-error');if(!notice){notice=document.createElement('p');notice.id='action-error';notice.setAttribute('role','alert');detail.append(notice);}notice.textContent=m.message;}
 else if(m.type==='dismiss')choose('closed');
});
window.addEventListener('error',e=>post('error:'+e.message));
window.addEventListener('unhandledrejection',e=>post('error:'+String(e.reason)));
reduced.addEventListener('change',()=>{clock();updateGlass(true);wake();});
function pointer(e,down){if(glassLayout)glassReceive({type:'pointer',x:e.clientX,y:e.clientY,down});}
document.addEventListener('pointermove',e=>pointer(e,!!e.buttons));document.addEventListener('pointerdown',e=>pointer(e,true));document.addEventListener('pointerup',e=>pointer(e,false));document.addEventListener('pointerleave',()=>glassReceive?.({type:'pointer',x:-999,y:-999,down:false}));
setInterval(()=>{clock();focusClock();},1000);clock();post('ready');choose('closed');
})();
