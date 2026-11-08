'use strict';
(()=>{
 const element=document.querySelector('.weather-environment');if(!element)return;
 const names={clear:'晴空',cloud:'云层',rain:'雨幕',snow:'飘雪',fog:'薄雾',unknown:'等待天气数据'};
 function solarTime(value,offset){if(typeof value!=='string'||!value)return NaN;return Number.isFinite(offset)&&/^\d{4}-\d\d-\d\dT\d\d:\d\d$/.test(value)?Date.parse(value+'Z')-offset*1000:Date.parse(value);}
 function describe(weather,available,now=Date.now()){
  const w=weather||{},code=w.code,valid=available&&w.ready===true&&Number.isFinite(w.temp)&&Number.isFinite(code);
  const kind=!valid?'unknown':code===0?'clear':code>=1&&code<=3?'cloud':code===45||code===48?'fog':code>=71&&code<=77||code===85||code===86?'snow':code>=51&&code<=67||code>=80&&code<=82||code>=95&&code<=99?'rain':'unknown';
  const begin=solarTime(w.sunrise,w.utcOffsetSeconds),end=solarTime(w.sunset,w.utcOffsetSeconds),solar=Number.isFinite(begin)&&Number.isFinite(end)&&end>begin&&now>=begin-86400000&&now<=end+86400000;
  const hour=Number.isFinite(w.utcOffsetSeconds)?new Date(now+w.utcOffsetSeconds*1000).getUTCHours():new Date(now).getHours();
  const night=solar?now<begin||now>=end:typeof w.isDay==='boolean'?!w.isDay:hour<6||hour>=19;
  const position=solar?Math.max(0,Math.min(1,(now-begin)/(end-begin))):.65;
  const period=night?'night':solar&&(position<.08||position>.9)?'dusk':'day';
  return {kind,period,position,label:kind==='unknown'?names.unknown:(w.stale?'上次天气 · ':'')+(night?'夜间 · ':'')+names[kind]};
 }
 const precipitation=element.querySelector('.weather-precipitation');
 for(let i=0;i<18;i++){const drop=document.createElement('i');drop.style.setProperty('--x',((i*37+11)%100)+'%');drop.style.setProperty('--delay',(-i*.47)+'s');drop.style.setProperty('--duration',(1.05+i%5*.16)+'s');precipitation.append(drop);}
 let paused=false,reduced=false,visible=true;
 function activity(){element.dataset.active=String(!document.hidden&&visible&&!paused&&!reduced&&document.querySelector('#dock')?.dataset.mode==='weather');}
 function update(weather,available,now){const state=describe(weather,available,now);element.dataset.code=state.kind;element.dataset.period=state.period;element.style.setProperty('--sun-x',(18+state.position*64)+'%');document.querySelector('#weather-scene-label').textContent=state.label;activity();}
 window.PrismWeatherScene={update,describe};
 window.addEventListener('prism-panel-change',activity);document.addEventListener('visibilitychange',activity);
 window.addEventListener('prism-preferences',e=>{reduced=!!e.detail?.reducedMotion;activity();});
 window.chrome?.webview?.addEventListener('message',e=>{const m=e.data;if(m.type==='motion'||m.type==='layout'){if(typeof m.paused==='boolean')paused=m.paused;activity();}if(m.type==='visibility'){visible=m.visible!==false;activity();}});
 update(null,false);
})();
