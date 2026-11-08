'use strict';
(() => {
const $=id=>document.getElementById(id),post=text=>window.chrome?.webview?.postMessage(text);
const dock=$('dock'),detail=$('detail'),capsule=$('capsule'),pages=$('status-pages');
const reduced=matchMedia('(prefers-reduced-motion: reduce)');
let panelHeight=552,panelTop=4;let bridgeY=40;let mode='closed',pinned=false,leaveTimer=0,openTimer=0,raf=0,last=0,frameCount=0,theme='Sky';
let composing=false,returnFocus=null,restoringFocus=false;let motionPaused=false,reduceMotion=false,monthOffset=0,glassReceive=null,glassLayout=null;
let railHeight=668,visibilityKey=null,selectionElement=null,selectionCard=null,selectionScale=0,lastShape='',pagesVisible=null;
let positionDragging=false;const positionGrip=document.createElement('button');positionGrip.id='position-grip';positionGrip.type='button';positionGrip.textContent='⠿';positionGrip.title='拖动左栏位置 · 右键或 Home 恢复默认 · 方向键微调';positionGrip.setAttribute('aria-label',positionGrip.title);dock.append(positionGrip);
positionGrip.addEventListener('pointerdown',e=>{if(e.button!==0)return;e.preventDefault();e.stopPropagation();clearTimeout(openTimer);clearTimeout(leaveTimer);positionDragging=true;positionGrip.setPointerCapture(e.pointerId);post('position:drag');});
for(const event of ['pointerup','pointercancel','lostpointercapture'])positionGrip.addEventListener(event,()=>{positionDragging=false;if(event==='pointercancel')post('position:cancel');});
positionGrip.addEventListener('contextmenu',e=>{e.preventDefault();post('position:reset');});positionGrip.addEventListener('keydown',e=>{const command={Home:'reset',Escape:'cancel',ArrowLeft:'left',ArrowRight:'right',ArrowUp:'up',ArrowDown:'down'}[e.key];if(command){e.preventDefault();e.stopPropagation();post('position:'+command);}});
const motionOff=()=>motionPaused||reduceMotion||reduced.matches;
PrismGlassRenderer($('popup-glass'),s=>{if(s==='painted')document.body.dataset.glassReady='true';if(s.startsWith('error:'))post(s);},receive=>glassReceive=receive);
function updateGlass(rebuild=false){
 if(!glassLayout||!glassReceive)return;const m=glassLayout,scale=m.scale,w=Math.max(0,springs.width.x),a=Math.max(0,Math.min(1,springs.opacity.x));
 const cards=railHeight>0?[{name:'Rail',x:4*scale,y:4*scale,w:88*scale,h:railHeight*scale,r:28*scale,fixedShape:true,rimStrength:.32}]:[];
 if(selectionCard)cards.push(selectionCard);
 if(w>1)cards.push({name:'Detail',x:108*scale,y:panelTop*scale,w:w*scale,h:panelHeight*scale,r:28*scale,fixedShape:true,rimStrength:.32,opacity:a});
 glassReceive({type:rebuild?'layout':'geometry',width:m.viewWidth||Math.ceil(852*scale),height:m.viewHeight||Math.ceil(680*scale),sceneWidth:m.width,sceneHeight:m.height,originX:m.x,originY:m.y,cards,theme,paused:motionOff()});
}
function measureSelection(){const el=document.querySelector('.rail-item:hover')||document.querySelector('.rail-item.selected'),scale=glassLayout?.scale||1;if(el&&el===selectionElement&&scale===selectionScale)return;selectionElement=el;selectionScale=scale;selectionCard=null;if(el){const r=el.getBoundingClientRect();selectionCard={name:el.dataset.widget,x:r.x,y:r.y,w:r.width,h:r.height,r:18*scale,rimStrength:.12};}}
let battery={},network={},latestWidgets=null;
const springs={height:{x:552,v:0,target:552},top:{x:4,v:0,target:4},width:{x:0,v:0,target:0},opacity:{x:0,v:0,target:0},capsule:{x:0,v:0,target:0},network:{x:0,v:0,target:0},page:{x:0,v:0,target:0}};
function step(s,dt){const omega=24,y=s.x-s.target,j=s.v+omega*y,e=Math.exp(-omega*dt);s.x=s.target+(y+j*dt)*e;s.v=(s.v-omega*j*dt)*e;if(Math.abs(s.target-s.x)<.015&&Math.abs(s.v)<.03){s.x=s.target;s.v=0;}return s.x;}
function report(){post('status:'+JSON.stringify({mode,pinned,width:Math.round(springs.width.x),frames:frameCount,reducedMotion:reduced.matches,theme,batteryAvailable:typeof battery.percent==='number',networkConnected:!!network.connected}));}
function render(now){
 raf=0;const dt=Math.max(0,Math.min((now-last)/1000,.1));last=now;let moving=false;measureSelection();
 for(const s of Object.values(springs)){if(motionOff()){s.x=s.target;s.v=0;}else step(s,dt);if(s.x!==s.target||s.v!==0)moving=true;}
 const w=Math.max(0,springs.width.x),a=Math.max(0,Math.min(1,springs.opacity.x)),m=springs.capsule.x,n=springs.network.x;
 panelHeight=springs.height.x;panelTop=springs.top.x;detail.style.top=panelTop+'px';detail.style.height=panelHeight+'px';detail.style.setProperty('--panel-height',panelHeight+'px');detail.style.width=w+'px';detail.style.opacity=a;detail.style.pointerEvents=a>.1?'auto':'none';
 if(pagesVisible!==(a>.001)){pagesVisible=a>.001;detail.querySelectorAll('.page').forEach(el=>el.style.visibility=pagesVisible?'visible':'hidden');}
 $('bridge').style.opacity=a;
 pages.style.transform=`translateX(${-368*springs.page.x}px)`;
 updateGlass();frameCount++;if(frameCount===1)post('painted');const q=(glassLayout?.scale||1)*2,shape=JSON.stringify([w,panelHeight,n,bridgeY,panelTop,railHeight].map(value=>Math.round(value*q)/q)),submitted=shape!==lastShape;if(submitted){lastShape=shape;post('shape:'+shape);}if(window.sideTrace)window.sideTrace.push({time:now,width:w,velocity:springs.width.v,dt,submitted});
 if(moving)raf=requestAnimationFrame(render);else report();
}
function wake(){if(!raf){last=performance.now();raf=requestAnimationFrame(render);}}
detail.style.transform='none';capsule.style.background='transparent';$('rail').style.borderRadius='28px';
$('rail').addEventListener('pointerover',wake);$('rail').addEventListener('pointerout',wake);
function setPanelSize(height,keepAnchor=false){const h=Math.max(330,Math.min(640,height)),top=Math.max(4,Math.min(668-h,keepAnchor?springs.top.target:bridgeY-32));springs.height.target=h;springs.top.target=top;wake();}
function fitContent(){if(mode==='closed'||$('widget-page').hidden)return;setPanelSize(Math.ceil($('widget-page').scrollHeight)+$('detail').querySelector('.panel-top').offsetHeight+8,true);}
function applyVisibility(values,order=[]){
 const names=new Set(['network','weather','music','focus','folders','system','tools','life']);
 const ordered=[...new Set([...order.map(value=>String(value).toLowerCase()),...names])].filter(name=>names.has(name));
 const hidden=new Set((Array.isArray(values)?values:[]).filter(value=>typeof value==='string').map(value=>value.toLowerCase()).filter(value=>names.has(value))),key=[...hidden].sort().join('|')+';'+ordered.join('|');
 if(key===visibilityKey)return;visibilityKey=key;selectionElement=null;clearTimeout(openTimer);openTimer=0;
 capsule.hidden=hidden.has('network');document.querySelectorAll('[data-widget]').forEach(el=>el.hidden=hidden.has(el.dataset.widget));
 capsule.style.order=ordered.indexOf('network');document.querySelectorAll('[data-widget]').forEach(el=>el.style.order=ordered.indexOf(el.dataset.widget));
 const rail=$('rail'),any=hidden.size<names.size;rail.hidden=!any;$('battery-trigger').hidden=!any;
 if(!any)railHeight=0;else if(hidden.size===0)railHeight=668;else{
  const css=getComputedStyle(rail),children=[...rail.children].filter(el=>!el.hidden&&getComputedStyle(el).display!=='none'),number=value=>parseFloat(value)||0;
  railHeight=Math.min(668,Math.max(72,number(css.paddingTop)+number(css.paddingBottom)+number(css.rowGap)*(children.length-1)+children.reduce((height,el)=>{const style=getComputedStyle(el);return height+el.offsetHeight+number(style.marginTop)+number(style.marginBottom)},0)));
 }
 rail.style.setProperty('height',railHeight+'px','important');
 if(!any||hidden.has(mode)||(mode==='both'&&hidden.has('network')))choose('closed');else if(mode!=='closed')choose(mode,pinned);else wake();
}
window.addEventListener('prism-content-change',()=>requestAnimationFrame(fitContent));
window.addEventListener('prism-preferences',e=>{reduceMotion=!!e.detail?.reducedMotion;updateGlass(true);wake();});
function choose(next,pin=false){
 if(next!=='closed')returnFocus=document.querySelector(`[data-widget="${next}"]`)||$(next==='network'?'wifi-trigger':'battery-trigger');
 clearTimeout(openTimer);openTimer=0;
 post('panel:'+next);
 $('action-error')?.remove();
 clearTimeout(leaveTimer);const open=next!=='closed',selected=document.querySelector(`[data-widget="${next}"]`);mode=next;pinned=pin;dock.dataset.mode=next;
 if(open){bridgeY=selected?selected.offsetTop+14:40;document.getElementById('bridge').style.top=bridgeY+'px';setPanelSize(({weather:610,music:570,focus:570,folders:405,system:450,tools:490,life:610,calendar:552})[next]||552);}
 $('rail').style.opacity='1';const widget=document.querySelector(`[data-widget-page="${next}"]`);
 // Keep the outgoing content and anchor until its glass has finished closing.
 if(open){$('widget-page').hidden=!widget;document.querySelectorAll('[data-widget-page]').forEach(el=>el.hidden=el!==widget);if(widget)text('widget-title',document.querySelector(`[data-widget="${next}"] .rail-label`).textContent);document.querySelector('.tabs').style.visibility=widget||next==='calendar'?'hidden':'visible';springs.network.target=next==='network'?1:0;springs.page.target=next==='network'?1:0;}
 document.querySelectorAll('[data-widget]').forEach(el=>{el.setAttribute('aria-expanded',String(el.dataset.widget===next));el.classList.toggle('selected',el.dataset.widget===next);});springs.width.target=open?(next==='both'?736:368):0;springs.opacity.target=open?1:0;springs.capsule.target=open?1:0;
 detail.inert=!open;detail.setAttribute('aria-hidden',String(!open));
 $('battery-trigger').setAttribute('aria-expanded',String(next==='battery'||next==='both'));
 $('wifi-trigger').setAttribute('aria-expanded',String(next==='network'||next==='both'));
 for(const tab of document.querySelectorAll('[data-tab]')){tab.setAttribute('aria-selected',String(tab.dataset.tab===next));tab.tabIndex=tab.dataset.tab===next?0:-1;}
 if(open){$('calendar-page').hidden=next!=='calendar';pages.style.display=next==='calendar'||widget?'none':'flex';}
 $('battery-page').inert=next==='network'||next==='calendar'||!open;
 $('network-page').inert=next!=='network'&&next!=='both';
 if(open){$('battery-page').style.opacity=next==='network'?'0':'1';$('network-page').style.opacity=next==='battery'?'0':'1';}
 if(Object.keys(battery).length)batteryData(battery);if(Object.keys(network).length)networkData(network);if(latestWidgets)widgetData(latestWidgets,false);
 window.dispatchEvent(new CustomEvent('prism-panel-change',{detail:{mode}}));
 if(next==='calendar')clock();requestAnimationFrame(fitContent);wake();
}
function trigger(id,next){
 $(id).addEventListener('pointerenter',()=>{if(!pinned&&!restoringFocus)choose(next);});
 $(id).addEventListener('click',()=>choose(mode===next&&pinned?'closed':next,!(mode===next&&pinned)));
 $(id).addEventListener('focus',()=>{if(!pinned&&!restoringFocus)choose(next);});
}
document.querySelectorAll('[data-widget]').forEach(el=>triggerWidget(el));
function triggerWidget(el){el.addEventListener('pointerenter',()=>{if(!pinned){clearTimeout(leaveTimer);clearTimeout(openTimer);openTimer=setTimeout(()=>{openTimer=0;if(el.matches(':hover')&&!pinned)choose(el.dataset.widget);},120);}});el.addEventListener('pointerleave',()=>{clearTimeout(openTimer);openTimer=0;});el.addEventListener('focus',()=>{if(!pinned&&!restoringFocus)choose(el.dataset.widget);});el.addEventListener('click',()=>choose(mode===el.dataset.widget&&pinned?'closed':el.dataset.widget,!(mode===el.dataset.widget&&pinned)));}
document.querySelectorAll('[data-action]').forEach(el=>el.addEventListener('click',()=>post('action:'+el.dataset.action)));
trigger('battery-trigger','battery');trigger('wifi-trigger','network');
dock.addEventListener('pointerleave',()=>{clearTimeout(openTimer);openTimer=0;clearTimeout(leaveTimer);if(!pinned&&!positionDragging)leaveTimer=setTimeout(()=>choose('closed'),260);});
dock.addEventListener('pointerenter',()=>clearTimeout(leaveTimer));
detail.addEventListener('pointerenter',()=>clearTimeout(leaveTimer));
detail.addEventListener('pointerdown',()=>{clearTimeout(leaveTimer);pinned=true;});
function closePanel(){choose('closed');restoringFocus=true;returnFocus?.focus({preventScroll:true});restoringFocus=false;}
$('close-panel').addEventListener('click',closePanel);
document.addEventListener('compositionstart',()=>composing=true);document.addEventListener('compositionend',()=>composing=false);
document.addEventListener('keydown',e=>{if(composing||e.isComposing||e.keyCode===229||e.defaultPrevented)return;if(e.key==='Escape'){e.preventDefault();closePanel();}if(e.key==='ArrowRight'||e.key==='ArrowLeft'){const tab=e.target.closest('[data-tab]');if(tab){e.preventDefault();const names=['battery','network','both'],index=names.indexOf(tab.dataset.tab),next=names[(index+(e.key==='ArrowRight'?1:2))%3];choose(next,true);document.querySelector(`[data-tab=${next}]`).focus();}}});
document.querySelectorAll('[data-tab]').forEach(tab=>tab.addEventListener('click',()=>choose(tab.dataset.tab,true)));
$('power-settings').addEventListener('click',()=>post('settings-power'));
$('network-settings').addEventListener('click',()=>post('settings-network'));
$('wifi-connect').addEventListener('click',()=>post('wifi-connect'));
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
let widgetFocus=null,cpuHistory=[],weatherViewKey='';
function weatherName(code){if(!Number.isFinite(code))return '天气状况未提供';if(code===0)return '晴';if(code===1)return '晴间少云';if(code===2)return '多云';if(code===3)return '阴';if(code===45||code===48)return '雾';if(code>=51&&code<=67)return '雨';if(code>=71&&code<=77||code===85||code===86)return '雪';if(code>=80&&code<=82)return '阵雨';if(code>=95&&code<=99)return '雷雨';return '天气状况未提供';}
function weatherTime(value){if(!Number.isFinite(value)||value<=0)return '数据时间未提供';const date=new Date(value),today=date.toDateString()===new Date().toDateString();return date.toLocaleString('zh-CN',{month:today?undefined:'2-digit',day:today?undefined:'2-digit',hour:'2-digit',minute:'2-digit',hour12:false})+' 数据';}
function weatherData(w,status,valid){
 const key=JSON.stringify([valid,w,status?.reason,new Date().toDateString()]);if(key===weatherViewKey)return;weatherViewKey=key;
 const stale=valid&&w.stale===true,reason=status?.reason||w?.reason||'',degrees=value=>valid&&Number.isFinite(value)?Math.round(value)+'°':'—';
 text('weather-city',w?.location||'天气位置未提供');text('weather-temp',degrees(w?.temp));text('weather-high',degrees(w?.high));text('weather-low',degrees(w?.low));
 text('weather-condition',valid?weatherName(w.code):'天气暂不可用');text('weather-feels',valid&&Number.isFinite(w.feelsLike)?'体感 '+Math.round(w.feelsLike)+'°':'');$('weather-feels').hidden=!valid||!Number.isFinite(w.feelsLike);
 text('weather-stamp',valid?(stale?'上次天气 · ':'')+weatherTime(w.updatedAt):'等待有效数据');
 const source=[w?.source,w?.kind==='model'?'数值预报':w?.kind==='observation'?'观测数据':''].filter(Boolean).join(' · ');text('weather-source',source);$('weather-source').hidden=!source;
 text('weather-status',!valid?reason||'请在天气设置中检查位置与网络。':stale?reason||'正在等待新的天气数据。':'');$('weather-status').hidden=valid&&!stale;
 const page=document.querySelector('[data-widget-page=weather]');page.dataset.state=!valid?'unavailable':stale?'stale':'ready';
 requestAnimationFrame(fitContent);
}
function focusClock(){if(!widgetFocus)return;const f=widgetFocus,n=f.deadline>0?Math.max(0,f.deadline-Math.floor(Date.now()/1000)):f.remaining,t=Math.floor(n/60).toString().padStart(2,'0')+':'+Math.floor(n%60).toString().padStart(2,'0');text('rail-focus',f.available?t:'—');if(mode!=='focus')return;text('widget-focus-time',f.available?t:'—');text('widget-focus-toggle',f.deadline>0?'暂停':n===0?'重新开始':n<f.total?'继续':'开始');text('widget-focus-state',!f.available?'计时器暂不可用':f.deadline>0?(f.total===300?'休息进行中':'专注进行中'):n===0?'本轮已结束':n<f.total?'已暂停':'准备开始');$('widget-focus-progress').value=1-n/Math.max(1,f.total);}
function widgetData(m,sample=true){
 latestWidgets=m;const w=m.weather,weatherStatus=m.sourceStatus?.weather,valid=w?.ready===true&&weatherStatus?.available===true&&Number.isFinite(w.temp),s=m.system;
 text('rail-weather',valid?Math.round(w.temp)+'°':'—');document.querySelector('[data-widget=weather]').setAttribute('aria-label',valid?(w.stale?'上次天气，':'天气，')+Math.round(w.temp)+'度，'+weatherName(w.code):'天气暂不可用');text('rail-system',s?Math.round(s.cpu)+'%':'—');text('rail-music',m.music.title==='网易云音乐'||m.music.title==='等待播放'?'待播放':'当前曲目');widgetFocus=m.focus;focusClock();
 if(s&&sample){cpuHistory.push(s.cpu);if(cpuHistory.length>60)cpuHistory.shift();}
 if(mode==='weather')weatherData(w,weatherStatus,valid);
 if(mode==='system'){text('system-cpu',s?Math.round(s.cpu)+'%':'—');text('system-ram',s?Math.round(s.ram)+'%':'—');$('cpu-progress').value=s?.cpu||0;$('ram-progress').value=s?.ram||0;const rate=n=>n>1048576?(n/1048576).toFixed(1)+' MB/s':(n/1024).toFixed(1)+' KB/s';text('system-network',s?'↓ '+rate(s.down)+'　↑ '+rate(s.up):'性能数据暂不可用');$('cpu-history').querySelector('polyline').setAttribute('points',cpuHistory.map((v,i)=>(i*320/59)+','+(68-v*.65)).join(' '));}
 if(mode==='music'){text('music-title',m.music.title);text('music-artist',m.music.artist);text('music-volume',m.volume?'音量 '+Math.round(m.volume.value)+'%':'音量 —');const img=$('music-cover');if(m.music.cover&&img.getAttribute('src')!==m.music.cover)img.src=m.music.cover;img.hidden=!m.music.cover;$('music-symbol').hidden=!!m.music.cover;}
 if(m.lyrics){for(const [id,on] of [['lyrics-toggle',m.lyrics.enabled],['lyrics-online',m.lyrics.online]]){text(id,on?'开启':'关闭');$(id).setAttribute('aria-checked',String(on));}text('lyrics-status',m.lyrics.status);const offset=m.lyrics.offsetMs; text('lyrics-offset',(offset===0?'0.0 秒':(offset>0?'提前 ':'延后 ')+(Math.abs(offset)/1000).toFixed(1)+' 秒')+' · 重置');}
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
  const previous=glassLayout;theme=m.theme;glassLayout=m;selectionElement=null;const canvas=$('popup-glass');canvas.style.left='0px';canvas.style.top='0px';canvas.style.width=(m.viewWidth||Math.ceil(852*m.scale))+'px';canvas.style.height=(m.viewHeight||Math.ceil(680*m.scale))+'px';updateGlass(!previous||previous.width!==m.width||previous.height!==m.height||previous.viewWidth!==m.viewWidth||previous.viewHeight!==m.viewHeight||previous.x!==m.x||previous.y!==m.y||previous.theme!==m.theme);if(!previous||previous.wallpaper!==m.wallpaper)glassReceive({type:'wallpaper',version:m.wallpaper});document.body.dataset.theme=theme;document.documentElement.style.setProperty('--scale',m.scale);
  document.documentElement.style.setProperty('--wall-x',-m.x+'px');document.documentElement.style.setProperty('--wall-y',-m.y+'px');document.documentElement.style.setProperty('--screen-w',m.width+'px');document.documentElement.style.setProperty('--screen-h',m.height+'px');
  document.body.style.backgroundImage=`url(wallpaper.png?v=${encodeURIComponent(m.wallpaper)})`;
  wake();
 }else if(m.type==='data'){batteryData(m.battery);networkData(m.network);clock();report();}
 else if(m.type==='widgets')widgetData(m);
 else if(m.type==='open')choose(mode===m.mode&&pinned?'closed':m.mode,!(mode===m.mode&&pinned));
 else if(m.type==='motion'){motionPaused=!!m.paused;document.body.classList.toggle('motion-paused',motionPaused);updateGlass(true);wake();}
 else if(m.type==='visibility')applyVisibility(m.hiddenLeftCards,m.leftCardOrder||[]);
 else if(m.type==='action-error'){let notice=$('action-error');if(!notice){notice=document.createElement('p');notice.id='action-error';notice.setAttribute('role','alert');detail.append(notice);}notice.textContent=m.message;}
 else if(m.type==='dismiss'&&!pinned)choose('closed');
});
window.addEventListener('error',e=>post('error:'+e.message));
window.addEventListener('unhandledrejection',e=>post('error:'+String(e.reason)));
reduced.addEventListener('change',()=>{clock();updateGlass(true);wake();});
function pointer(e,down){if(glassLayout)glassReceive({type:'pointer',x:e.clientX,y:e.clientY,down});}
document.addEventListener('pointermove',e=>pointer(e,!!e.buttons));document.addEventListener('pointerdown',e=>pointer(e,true));document.addEventListener('pointerup',e=>pointer(e,false));document.addEventListener('pointerleave',()=>glassReceive?.({type:'pointer',x:-999,y:-999,down:false}));
setInterval(()=>{clock();focusClock();},1000);clock();post('ready');choose('closed');
})();
