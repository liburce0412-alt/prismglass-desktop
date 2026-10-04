'use strict';
(()=>{
const $=id=>document.getElementById(id),post=s=>window.chrome?.webview?.postMessage(s);
const read=(key,fallback)=>{try{return JSON.parse(localStorage.getItem('prism-'+key))??fallback}catch{return fallback}};
const save=(key,value)=>{const data=JSON.stringify(value);if(localStorage.getItem('prism-'+key)!==data)localStorage.setItem('prism-'+key,data);};
const put=(id,value)=>{const el=$(id),next=String(value);if(el.textContent!==next)el.textContent=next;};
let quotes=read('quotes',['慢慢来，也是在向前。','给今天留一点空白。','把热爱放在看得见的地方。']),scene=read('scene','sky'),gallery=read('gallery',[]),photo=0,lastFocus=null,completed=read('completed',0),lastAward=read('last-award',0);
let alertMode=read('alert','gentle'),latestWidgets=null,quoteDay='';
const plantNames={fern:'蕨叶',succulent:'多肉',bamboo:'细竹',orchid:'兰草',maple:'红枫',cedar:'松柏'};
const plantCosts={orchid:2,maple:3,cedar:4},decorNames={stones:'溪石',moss:'苔丘',sand:'白砂'},decorCosts={moss:2,sand:3};
const gardenInt=value=>Number.isFinite(Number(value))?Math.max(0,Math.floor(Number(value))):0;
let garden=read('garden',null);
if(!garden||typeof garden!=='object'||Array.isArray(garden))garden={plant:'fern',credited:completed,drops:0,watered:{fern:completed,succulent:0,bamboo:0}};
// Migration keeps earned resources and all existing growth; old focus is not counted again as new seed earnings.
garden.credited=gardenInt(garden.credited);garden.drops=gardenInt(garden.drops);garden.watered=garden.watered&&typeof garden.watered==='object'?garden.watered:{};
for(const name of Object.keys(plantNames))garden.watered[name]=gardenInt(garden.watered[name]);
garden.seeds=gardenInt(garden.seeds);garden.newFocus=gardenInt(garden.newFocus);garden.seedProgress=gardenInt(garden.seedProgress)%3;garden.careCount=gardenInt(garden.careCount);
garden.unlocked=[...new Set(['fern','succulent','bamboo',...(Array.isArray(garden.unlocked)?garden.unlocked.filter(p=>plantNames[p]):[])])];
garden.decorUnlocked=[...new Set(['stones',...(Array.isArray(garden.decorUnlocked)?garden.decorUnlocked.filter(d=>decorNames[d]):[])])];
garden.claimed=Array.isArray(garden.claimed)?[...new Set(garden.claimed)]:[];garden.labels=garden.labels&&typeof garden.labels==='object'?garden.labels:{};garden.decor=garden.decor&&typeof garden.decor==='object'?garden.decor:{};
garden.log=Array.isArray(garden.log)?garden.log.filter(item=>item&&typeof item.text==='string'&&Number.isFinite(item.time)).slice(-60):[];
if(!plantNames[garden.plant]||!garden.unlocked.includes(garden.plant))garden.plant='fern';garden.version=2;
for(const name of Object.keys(plantNames)){garden.labels[name]=String(garden.labels[name]||'').slice(0,12);if(!garden.decorUnlocked.includes(garden.decor[name]))garden.decor[name]='stones';}
save('garden',garden);
const gardenAchievements=[
 {id:'first-care',name:'第一次照料',detail:'亲手浇水 1 次',done:()=>garden.careCount>=1},
 {id:'steady-care',name:'细水长流',detail:'亲手浇水 12 次',done:()=>garden.careCount>=12},
 {id:'first-mature',name:'枝叶长成',detail:'任意一瓶长到成株',done:()=>Object.values(garden.watered).some(v=>v>=16)},
 {id:'three-mature',name:'窗边小森林',detail:'三瓶植物长到成株',done:()=>Object.values(garden.watered).filter(v=>v>=16).length>=3},
 {id:'focus-ten',name:'留一段专注',detail:'本版起新完成 10 轮专注',done:()=>garden.newFocus>=10},
 {id:'collector',name:'植物收藏家',detail:'收集全部 6 瓶植物',done:()=>garden.unlocked.length>=6}
];
let gardenRendered='';
const contentChanged=()=>window.dispatchEvent(new Event('prism-content-change'));
function setScene(value){scene=value;save('scene',value);document.body.classList.toggle('scene-space',value==='space');document.body.classList.toggle('scene-music',value==='music');$('scene-choice').value=value;post('action:theme-'+(value==='space'?'astro':'sky'));sceneLabel();sky();contentChanged();}
function sceneLabel(){document.querySelector('[data-life-tab=sky]').textContent=scene==='music'?'正在听':scene==='space'?'观测':'天空';}sceneLabel();$('scene-choice').value=scene;document.body.classList.toggle('scene-space',scene==='space');document.body.classList.toggle('scene-music',scene==='music');$('scene-choice').onchange=e=>setScene(e.target.value);
$('alert-choice').value=alertMode;$('alert-choice').onchange=e=>{alertMode=e.target.value;save('alert',alertMode)};
document.querySelectorAll('[data-life-tab]').forEach(button=>button.onclick=()=>{document.querySelectorAll('[data-life-tab]').forEach(b=>b.setAttribute('aria-selected',String(b===button)));document.querySelectorAll('[data-life-page]').forEach(p=>p.hidden=p.dataset.lifePage!==button.dataset.lifeTab);contentChanged()});
function gardenStage(value){return value<3?0:value<8?1:value<16?2:3;}
function gardenLog(text){garden.log.push({time:Date.now(),text});garden.log=garden.log.slice(-60);}
function gardenMessage(text){put('garden-feedback',text);}
function grow(){
 const signature=JSON.stringify(garden)+'|'+completed;if(signature===gardenRendered)return;gardenRendered=signature;
 const plant=garden.plant,value=garden.watered[plant],stage=gardenStage(value),jar=document.querySelector('.terrarium');jar.dataset.plant=plant;jar.dataset.stage=String(stage);jar.dataset.decor=garden.decor[plant];
 document.querySelector('.sprout').style.setProperty('--growth',[22,44,65,87][stage]+'px');$('garden-plant').value=plant;put('garden-count',(garden.labels[plant]||plantNames[plant])+' · '+['萌芽','展叶','繁茂','成株'][stage]);
 put('garden-drops',garden.drops+' 滴水');put('garden-seeds',garden.seeds+' 颗种子');put('garden-history','已完成 '+completed+' 轮专注');put('garden-seed-progress','再完成 '+(3-garden.seedProgress)+' 轮专注，获得 1 颗种子');
 $('garden-water').disabled=garden.drops<1;put('garden-water',(stage===3?'照料':'浇水')+' · 1 滴');put('garden-next',stage===3?'已长成，照料记录会继续保留。':'再浇 '+([3,8,16][stage]-value)+' 次，进入下一阶段。');jar.setAttribute('aria-label',plantNames[plant]+'，'+['萌芽','展叶','繁茂','成株'][stage]+'，'+decorNames[garden.decor[plant]]+'瓶景');
 for(const name of Object.keys(plantNames)){const unlocked=garden.unlocked.includes(name);$('garden-option-'+name).disabled=!unlocked;put('garden-status-'+name,unlocked?['萌芽','展叶','繁茂','成株'][gardenStage(garden.watered[name])]:'解锁 · '+plantCosts[name]+' 种子');const button=$('garden-bottle-'+name);button.setAttribute('aria-pressed',String(plant===name));button.disabled=!unlocked&&garden.seeds<plantCosts[name];button.title=unlocked?'切换到'+plantNames[name]:'花费 '+plantCosts[name]+' 颗种子，永久解锁'+plantNames[name];}
 for(const name of Object.keys(decorNames)){const unlocked=garden.decorUnlocked.includes(name),button=$('garden-decor-'+name);button.textContent=decorNames[name]+(unlocked?'':' · '+decorCosts[name]+' 种子');button.setAttribute('aria-pressed',String(garden.decor[plant]===name));button.disabled=!unlocked&&garden.seeds<decorCosts[name];}
 for(const achievement of gardenAchievements){const claimed=garden.claimed.includes(achievement.id),available=achievement.done(),button=$('garden-claim-'+achievement.id);button.disabled=claimed||!available;put('garden-achievement-'+achievement.id,achievement.name);put('garden-achievement-detail-'+achievement.id,achievement.detail);button.textContent=claimed?'已领取':available?'领取 1 种子':'未达成';}
 put('garden-log',garden.log.length?garden.log.slice(-12).reverse().map(item=>new Date(item.time).toLocaleString('zh-CN',{month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit'})+'  '+item.text).join('\n'):'这里会记录专注收获、浇水、解锁和成长。');
 put('garden-collection-count',garden.unlocked.length+' / 6 瓶');if(document.activeElement!==$('garden-name'))$('garden-name').value=garden.labels[plant];
}
function syncGarden(){if(completed>garden.credited){const gained=completed-garden.credited,seedSum=garden.seedProgress+gained,seeds=Math.floor(seedSum/3);garden.drops+=gained;garden.seeds+=seeds;garden.seedProgress=seedSum%3;garden.newFocus+=gained;garden.credited=completed;gardenLog('完成 '+gained+' 轮专注，收获 '+gained+' 滴水'+(seeds?'与 '+seeds+' 颗种子':''));save('garden',garden);}if($('dock').dataset.mode==='life')grow();}
function selectGardenPlant(plant){if(!plantNames[plant])return;gardenMessage('');if(!garden.unlocked.includes(plant)){const cost=plantCosts[plant];if(garden.seeds<cost){gardenMessage('还需要 '+(cost-garden.seeds)+' 颗种子。');return;}garden.seeds-=cost;garden.unlocked.push(plant);gardenLog('解锁 '+plantNames[plant]);gardenMessage(plantNames[plant]+'已加入收藏。');}garden.plant=plant;save('garden',garden);grow();}
$('garden-plant').onchange=e=>{if(garden.unlocked.includes(e.target.value))selectGardenPlant(e.target.value);};
for(const name of Object.keys(plantNames))$('garden-bottle-'+name).onclick=()=>selectGardenPlant(name);
$('garden-water').onclick=()=>{if(garden.drops<1)return;const plant=garden.plant,before=gardenStage(garden.watered[plant]);garden.drops--;garden.watered[plant]++;garden.careCount++;const after=gardenStage(garden.watered[plant]);gardenLog('照料 '+plantNames[plant]+(after>before?'，进入'+['萌芽','展叶','繁茂','成株'][after]+'阶段':''));save('garden',garden);grow();gardenMessage(after>before?plantNames[plant]+'长出了新的枝叶。':'已浇水，'+plantNames[plant]+'的成长已保存。');};
for(const name of Object.keys(decorNames))$('garden-decor-'+name).onclick=()=>{if(!garden.decorUnlocked.includes(name)){const cost=decorCosts[name];if(garden.seeds<cost)return;garden.seeds-=cost;garden.decorUnlocked.push(name);gardenLog('解锁 '+decorNames[name]+'瓶景');}garden.decor[garden.plant]=name;save('garden',garden);grow();gardenMessage('已换成'+decorNames[name]+'瓶景。');};
for(const achievement of gardenAchievements)$('garden-claim-'+achievement.id).onclick=()=>{if(garden.claimed.includes(achievement.id)||!achievement.done())return;garden.claimed.push(achievement.id);garden.seeds++;gardenLog('达成「'+achievement.name+'」，领取 1 颗种子');save('garden',garden);grow();gardenMessage('成就奖励已收下，每个成就只领取一次。');};
$('garden-name-save').onclick=()=>{const name=$('garden-name').value.trim().slice(0,12);garden.labels[garden.plant]=name;save('garden',garden);grow();gardenMessage(name?'这瓶植物叫「'+name+'」。':'已恢复植物原名。');};
$('garden-collection').ontoggle=()=>contentChanged();$('garden-achievements').ontoggle=()=>contentChanged();$('garden-journal').ontoggle=()=>contentChanged();$('garden-help').ontoggle=()=>contentChanged();grow();
function quote(){const now=new Date();quoteDay=now.toDateString();const day=Math.floor(Date.UTC(now.getFullYear(),now.getMonth(),now.getDate())/86400000);$('daily-quote').textContent=quotes.length?quotes[day%quotes.length]:'写一句属于你的话。';}quote();
$('edit-quotes').onclick=()=>{$('quote-editor').hidden=!$('quote-editor').hidden;$('quote-editor').value=quotes.join('\n');$('save-quotes').hidden=$('quote-editor').hidden;contentChanged()};
$('save-quotes').onclick=()=>{quotes=$('quote-editor').value.split('\n').map(x=>x.trim()).filter(Boolean).slice(0,100);save('quotes',quotes);quote();$('quote-editor').hidden=true;$('save-quotes').hidden=true;contentChanged()};
function showPhoto(){const item=gallery[photo];$('gallery-photo').hidden=!item;$('gallery-default').hidden=!!item;if(item)$('gallery-photo').src=item;$('gallery-number').textContent=item?(photo+1)+' / '+gallery.length:'篮球 · 原创线稿';}showPhoto();
$('gallery-next').onclick=()=>{photo=(photo+1)%Math.max(1,gallery.length);showPhoto()};$('gallery-add').onclick=()=>post('action:gallery-add');$('gallery-remove').onclick=()=>{if(gallery.length){gallery.splice(photo,1);photo=0;save('gallery',gallery);showPhoto()}};
function sky(){const now=new Date();if(quoteDay!==now.toDateString())quote();const h=now.getHours()+now.getMinutes()/60,period=h<6||h>=19?'night':h>=17?'dusk':'day';document.querySelectorAll('.landscape').forEach(el=>el.dataset.period=scene==='space'?'night':period);$('sky-time').textContent=scene==='space'?'深空观测站':period==='night'?'夜色渐深':period==='dusk'?'留住暮色':'天空工作台';
 const age=((Date.now()-Date.UTC(2000,0,6,18,14))/86400000%29.530588853+29.530588853)%29.530588853,phase=age/29.530588853,light=(1-Math.cos(2*Math.PI*phase))/2;
 $('moon-label').textContent=(phase<.03||phase>.97?'新月':phase<.24?'娥眉月':phase<.28?'上弦月':phase<.47?'盈凸月':phase<.53?'满月':phase<.72?'亏凸月':phase<.78?'下弦月':'残月')+' · 估算';$('moon-detail').textContent='照亮约 '+Math.round(light*100)+'%';window.dispatchEvent(new CustomEvent('prism-moon',{detail:{phase}}));$('day-progress').value=h/24;$('day-label').textContent=now.toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit'})+' · 今日已过 '+Math.round(h/24*100)+'%';}sky();setInterval(sky,60000);
function updateContent(m){if(!m)return;const mode=$('dock').dataset.mode;
 if(mode==='life'||mode==='weather'){
 const w=m.weather;
 if(w?.ready&&w.sunrise&&w.sunset){const begin=new Date(w.sunrise).getTime(),end=new Date(w.sunset).getTime(),now=Date.now(),p=Math.max(0,Math.min(1,(now-begin)/(end-begin)));$('solar-marker').setAttribute('cx',String(20+280*p));$('solar-marker').setAttribute('cy',String(82-204*p*(1-p)));$('solar-marker').toggleAttribute('hidden',now<begin||now>end);$('solar-status').textContent=now<begin?'日出前':now>end?'已日落':'白昼 '+Math.round((end-begin)/360000)/10+' 小时';}else{$('solar-marker').setAttribute('hidden','');$('solar-status').textContent='日照数据暂不可用';}
 $('sun-times').textContent=w?.ready&&w.sunrise&&w.sunset?'日出 '+w.sunrise.slice(11,16)+' · 日落 '+w.sunset.slice(11,16):'日出日落暂不可用';document.querySelectorAll('.landscape').forEach(el=>el.dataset.code=!w?.ready?'unknown':(w.code>=71&&w.code<=77)||w.code===85||w.code===86?'snow':w.code>=51?'rain':w.code>=45?'fog':w.code===0?'clear':'cloud');$('weather-scene-label').textContent=w?.ready?Math.round(w.temp)+'° · 南昌':'等待实际天气';
}
 if(mode==='life'||mode==='music'){
 $('room-cover').hidden=!m.music.cover;if(m.music.cover&&$('room-cover').getAttribute('src')!==m.music.cover)$('room-cover').src=m.music.cover;document.querySelector('.record').classList.toggle('playing',m.music.playing===true);$('room-track').textContent=m.music.title;$('room-artist').textContent=m.music.artist;document.querySelector('.album').classList.toggle('playing',m.music.playing===true);$('playback-state').textContent=m.music.playing===true?'正在播放':m.music.playing===false?'已暂停':'播放状态未提供';
}
}
window.addEventListener('prism-panel-change',()=>{updateContent(latestWidgets);if($('dock').dataset.mode==='life'){sky();grow();}});
window.chrome?.webview?.addEventListener('message',e=>{const m=e.data;if(m.type==='gallery'){gallery.push(m.url);gallery=gallery.slice(-30);photo=gallery.length-1;save('gallery',gallery);showPhoto()}if(m.type!=='widgets')return;
 latestWidgets=m;updateContent(m);
 const f=m.focus,now=Math.floor(Date.now()/1000);if(f?.available&&lastFocus?.deadline>0&&lastFocus.deadline<=now&&f.remaining===0&&lastAward!==lastFocus.deadline){lastAward=lastFocus.deadline;save('last-award',lastAward);if(lastFocus.total>300&&typeof f.completed!=='number'){completed++;save('completed',completed);syncGarden()}$('focus-complete').hidden=alertMode==='off';if(alertMode==='panel')post('life-open-focus');}lastFocus=f;
 if(typeof f?.completed==='number'&&Number.isFinite(f.completed)&&f.completed>completed){completed=Math.floor(f.completed);save('completed',completed);syncGarden();}
 if(f?.lastCompleted>0&&f.lastCompleted!==lastAward&&now-f.lastCompleted<600){lastAward=f.lastCompleted;save('last-award',lastAward);$('focus-complete').hidden=alertMode==='off';if(alertMode==='panel')post('life-open-focus');}
 if(f?.deadline>now)$('focus-complete').hidden=true;
});
})();
