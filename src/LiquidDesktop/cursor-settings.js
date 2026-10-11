'use strict';
(()=>{
 const host=document.getElementById('cursor-settings');
 const arrow='<path d="M6 3.5C3.5 2.3 2.6 3.8 3.4 6.1L9.2 25.5C9.8 27.5 11.9 27.4 12.7 25.4L15.4 18.8Q15.7 18 16.7 17.6L24.1 15C26.4 14.2 26.4 12.1 24.2 11.1Z"/>';
 const classic='<path d="M5 3L5 25L11 19L15 28L19 26L15 17L24 17Z"/>';
 const icon=(body,cls='')=>`<svg viewBox="0 0 32 32" class="${cls}" aria-hidden="true">${body}</svg>`;
 const styles=[['rounded-dark','圆润黑'],['rounded-light','圆润白'],['macos','macOS'],['windows','Windows 原样']];
 const trails=[['particles','微光粒子'],['stars','星光'],['ribbon','流线']];
 const marks={particles:'<circle cx="7" cy="24" r="1"/><circle cx="11" cy="19" r="1.3"/><circle cx="17" cy="17" r="1.7"/><circle cx="20" cy="10" r="2"/>',stars:'<path d="m11 14 1.5 4.5L17 20l-4.5 1.5L11 26l-1.5-4.5L5 20l4.5-1.5Zm11-9 1.5 4.5L28 11l-4.5 1.5L22 17l-1.5-4.5L16 11l4.5-1.5Z"/>',ribbon:'<path d="M4 25C8 15 16 29 19 15S25 8 28 4" fill="none" stroke="currentColor" stroke-width="2"/>'};
 let settings={style:'rounded-dark',trailEnabled:true,trailStyle:'particles',trailColor:'#79C3E3'},scale=100,busy=false;
 host.innerHTML=`<div class="cursor-heading"><h2>鼠标与拖尾</h2><span id="cursor-save-state" role="status">自动保存</span></div>
  <div class="cursor-preview"><svg id="cursor-demo" viewBox="0 0 140 58" aria-hidden="true"></svg><div><b id="cursor-description"></b><small id="cursor-description-detail"></small></div></div>
  <h3 class="manage-heading">指针样式</h3><div id="cursor-styles" class="cursor-choices" role="group" aria-label="指针样式"></div>
  <div class="cursor-size-title"><h3 class="manage-heading">鼠标大小</h3><span id="cursor-system-size"></span></div><div id="cursor-sizes" class="cursor-sizes" role="group" aria-label="鼠标大小"></div>
  <button id="cursor-trail-toggle" class="manage-row cursor-toggle" type="button" role="switch" aria-checked="true"><span>拖尾效果<small>停留后自然消散</small></span><i aria-hidden="true"></i></button>
  <div id="cursor-trail-options"><h3 class="manage-heading">拖尾样式</h3><div id="cursor-trails" class="cursor-choices" role="group" aria-label="拖尾样式"></div>
  <div class="cursor-color-title"><h3 class="manage-heading">拖尾颜色</h3><span id="cursor-color-label"></span></div><div class="cursor-colors" role="group" aria-label="拖尾颜色"></div>
  <form id="cursor-color-form"><label for="cursor-custom-color">自定义色值</label><input id="cursor-custom-color" value="#79C3E3" maxlength="7" pattern="#[0-9a-fA-F]{6}" aria-label="自定义拖尾颜色，六位十六进制色值" spellcheck="false" required><button type="submit">使用</button></form></div>
  <p class="cursor-footnote">即时应用 · 退出美化恢复原 Windows 指针</p>`;
 const $=id=>document.getElementById(id);
 function send(action,value){if(busy)return;busy=true;render();window.chrome?.webview?.postMessage(JSON.stringify({action,value}));}
 function choice(container,value,label,body,action){const button=document.createElement('button');button.type='button';button.dataset.value=value;button.innerHTML=body;const text=document.createElement('span');text.textContent=label;button.append(text);button.onclick=()=>send(action,value);$(container).append(button);}
 for(const [value,label] of styles)choice('cursor-styles',value,label,icon(value.startsWith('rounded')?arrow:classic,'cursor-arrow '+value),'cursor-style');
 for(const size of [75,100,125,150,200])choice('cursor-sizes',String(size),size+'%','','cursor-size');
 for(const [value,label] of trails)choice('cursor-trails',value,label,icon(marks[value],'trail-mark'),'cursor-trail-style');
 for(const [color,name] of [['#79C3E3','冰蓝'],['#B29EEA','鸢紫'],['#7BD6BB','薄荷'],['#E7BD75','暖金']]){
  const button=document.createElement('button');button.type='button';button.dataset.color=color;button.style.setProperty('--swatch',color);button.setAttribute('aria-label',name);button.title=name;button.innerHTML='<i aria-hidden="true"></i>';button.onclick=()=>send('cursor-trail-color',color);host.querySelector('.cursor-colors').append(button);
 }
 $('cursor-trail-toggle').onclick=()=>send('cursor-trail',String(!settings.trailEnabled));
 $('cursor-color-form').onsubmit=event=>{event.preventDefault();const value=$('cursor-custom-color').value.trim();if(/^#[0-9a-f]{6}$/i.test(value))send('cursor-trail-color',value.toUpperCase());};
 function selected(container,value){for(const button of $(container).querySelectorAll('button'))button.setAttribute('aria-pressed',String(button.dataset.value===String(value)));}
 function render(){
  host.style.setProperty('--trail-color',settings.trailColor);host.setAttribute('aria-busy',String(busy));
  selected('cursor-styles',settings.style);selected('cursor-sizes',scale);selected('cursor-trails',settings.trailStyle);
  for(const control of host.querySelectorAll('button,input'))control.disabled=busy;
  const system=settings.style==='windows';for(const button of $('cursor-sizes').querySelectorAll('button'))button.disabled=busy||system;
  $('cursor-system-size').textContent=system?'跟随 Windows 系统大小':'';
  $('cursor-trail-toggle').setAttribute('aria-checked',String(settings.trailEnabled));$('cursor-trail-toggle').setAttribute('aria-pressed',String(settings.trailEnabled));
  for(const control of $('cursor-trail-options').querySelectorAll('button,input'))control.disabled=busy||!settings.trailEnabled;
  $('cursor-trail-options').classList.toggle('is-off',!settings.trailEnabled);
  for(const button of host.querySelectorAll('[data-color]'))button.setAttribute('aria-pressed',String(button.dataset.color===settings.trailColor.toUpperCase()));
  if(document.activeElement!==$('cursor-custom-color'))$('cursor-custom-color').value=settings.trailColor;
  $('cursor-color-label').textContent=settings.trailColor.toUpperCase();$('cursor-save-state').textContent=busy?'正在应用…':'自动保存';
  $('cursor-description').textContent=styles.find(item=>item[0]===settings.style)?.[1]||'圆润黑';
  $('cursor-description-detail').textContent=settings.trailEnabled?(trails.find(item=>item[0]===settings.trailStyle)?.[1]||'微光粒子')+' · 已开启':'拖尾已关闭';
  $('cursor-demo').innerHTML=(settings.trailEnabled?`<g class="trail-mark" transform="translate(24 15) scale(1.5)">${marks[settings.trailStyle]||marks.particles}</g>`:'')+`<g class="cursor-arrow ${settings.style}" transform="translate(86 7) scale(1.4)">${settings.style.startsWith('rounded')?arrow:classic}</g>`;
 }
 window.PrismCursorSettings={update(next,size,isBusy){if(next)settings={...settings,...next};if(size)scale=size;if(typeof isBusy==='boolean')busy=isBusy;render();}};
 render();
})();
