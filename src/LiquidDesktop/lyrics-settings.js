'use strict';
(()=>{
 const studio=document.getElementById('lyrics-studio'),preview=document.getElementById('lyrics-preview'),status=document.getElementById('lyrics-save-state');
 const post=text=>window.chrome?.webview?.postMessage(text),reduced=matchMedia('(prefers-reduced-motion: reduce)');
 let style=PrismLyrics.normalize(),saveTimer=0,ackTimer=0,pending='',visible=false,paused=false;
 const still=()=>reduced.matches||paused||!visible;
 const signature=value=>JSON.stringify(PrismLyrics.normalize(value));
 function options(field,entries){
  const select=studio.querySelector(`[data-lyric-field="${field}"]`);
  for(const [value,label] of entries){const option=document.createElement('option');option.value=value;option.textContent=label;select.append(option);}
 }
 options('font',Object.entries(PrismLyrics.fonts).map(([key,[label]])=>[key,label]));
 options('effect',Object.entries(PrismLyrics.effects));options('texture',Object.entries(PrismLyrics.textures));
 function render(replay=false){
  for(const control of studio.querySelectorAll('[data-lyric-field]')){
   // Polling and acknowledgements must not take the thumb or caret from the user.
   if(control!==document.activeElement)control.value=String(style[control.dataset.lyricField]);
  }
  for(const output of studio.querySelectorAll('[data-lyric-output]')){
   const key=output.dataset.lyricOutput,value=style[key];
   output.textContent=key==='width'||key==='fontSize'?value+' px':key==='spacing'?(value/100).toFixed(2)+' em':['intensity','glow','secondaryOpacity','speed'].includes(key)?value+'%':value;
  }
  for(const button of studio.querySelectorAll('[data-lyric-preset]')){
   const preset=PrismLyrics.presets[button.dataset.lyricPreset];
   button.setAttribute('aria-pressed',String(signature({...PrismLyrics.defaults,...preset.style})===signature(style)));
  }
  PrismLyrics.apply(preview,style,still());
  if(replay)PrismLyrics.animate(preview,style,still());else if(still())PrismLyrics.cancel(preview);
 }
 function save(){
  clearTimeout(saveTimer);clearTimeout(ackTimer);pending=signature(style);status.textContent='正在保存…';
  saveTimer=setTimeout(()=>{
   saveTimer=0;post('action:lyrics-style:'+pending);
   ackTimer=setTimeout(()=>{status.textContent='尚未保存，请重试';},4000);
  },160);
 }
 for(const [key,preset] of Object.entries(PrismLyrics.presets)){
  const button=document.createElement('button');button.type='button';button.dataset.lyricPreset=key;button.textContent=preset.label;button.style.setProperty('--swatch',preset.style.accent||PrismLyrics.defaults.accent);
  button.addEventListener('click',()=>{style=PrismLyrics.normalize(preset.style);render(true);save();});
  document.getElementById('lyrics-presets').append(button);
 }
 for(const control of studio.querySelectorAll('[data-lyric-field]')){
  control.addEventListener('input',()=>{
   const key=control.dataset.lyricField;
   style=PrismLyrics.normalize({...style,[key]:control.type==='range'?Number(control.value):control.value});
   render(['effect','texture','font','layout'].includes(key));save();
  });
 }
 document.getElementById('lyrics-replay').addEventListener('click',()=>{render(true);if(status.textContent.includes('重试'))save();});
 document.getElementById('lyrics-style-reset').addEventListener('click',()=>{style=PrismLyrics.normalize();render(true);save();});
 studio.querySelectorAll('details').forEach(el=>el.addEventListener('toggle',()=>window.dispatchEvent(new Event('prism-content-change'))));
 window.addEventListener('prism-panel-change',e=>{visible=e.detail.mode==='music';render(visible);});
 reduced.addEventListener('change',()=>render());
 window.chrome?.webview?.addEventListener('message',e=>{
  const m=e.data;
  if(m.type==='motion'){paused=!!m.paused;render();}
  if(m.type==='widgets'&&m.lyrics){
   const received=signature(m.lyrics.appearance);
   if(!pending||received===pending){
    clearTimeout(ackTimer);pending='';style=PrismLyrics.normalize(m.lyrics.appearance);status.textContent='已保存';render();
   }
   const gaps=document.getElementById('lyrics-gaps'),enabled=m.lyrics.holdGaps!==false;
   gaps.setAttribute('aria-checked',String(enabled));gaps.textContent=enabled?'开启':'关闭';
  }
  if(m.type==='action-error'&&pending){clearTimeout(ackTimer);status.textContent='尚未保存，请重试';}
 });
 window.addEventListener('pagehide',()=>{
  if(saveTimer&&pending)post('action:lyrics-style:'+pending);
  clearTimeout(saveTimer);clearTimeout(ackTimer);PrismLyrics.cancel(preview);
 });
 render();
})();
