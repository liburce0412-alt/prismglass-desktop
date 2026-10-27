'use strict';
(()=>{
 const studio=document.getElementById('lyrics-studio'),preview=document.getElementById('lyrics-preview'),status=document.getElementById('lyrics-save-state');
 const post=text=>window.chrome?.webview?.postMessage(text),reduced=matchMedia('(prefers-reduced-motion: reduce)');
 let style=PrismLyrics.normalize(),saveTimer=0,ackTimer=0,pending='',visible=false,paused=false,palette=null;
 const still=()=>reduced.matches||paused||!visible;
 const signature=value=>JSON.stringify(PrismLyrics.normalize(value));
 const textSignature=value=>{const normalized=PrismLyrics.normalize(value);for(const key of ['backgroundTheme','backgroundOpacity','backgroundMotion'])delete normalized[key];return JSON.stringify(normalized);};
 function options(field,entries){
  const select=studio.querySelector(`[data-lyric-field="${field}"]`);
  for(const [value,label] of entries){const option=document.createElement('option');option.value=value;option.textContent=label;select.append(option);}
 }
 options('font',Object.entries(PrismLyrics.fonts).map(([key,[label]])=>[key,label]));
 options('effect',Object.entries(PrismLyrics.effects));options('texture',Object.entries(PrismLyrics.textures));
 function render(replay=false){
  for(const control of studio.querySelectorAll('[data-lyric-field]')){
   // Polling and acknowledgements must not take the thumb or caret from the user.
   if(control.type==='checkbox')control.checked=!!style[control.dataset.lyricField];else if(control!==document.activeElement)control.value=String(style[control.dataset.lyricField]);
  }
  for(const output of studio.querySelectorAll('[data-lyric-output]')){
   const key=output.dataset.lyricOutput,value=style[key];
   output.textContent=key==='width'||key==='fontSize'?value+' px':key==='spacing'?(value/100).toFixed(2)+' em':['intensity','glow','secondaryOpacity','speed','backgroundOpacity'].includes(key)?value+'%':value;
  }
  for(const button of studio.querySelectorAll('[data-lyric-preset]')){
   const preset=PrismLyrics.presets[button.dataset.lyricPreset];
   button.setAttribute('aria-pressed',String(textSignature(preset.style)===textSignature(style)));
  }
  PrismLyrics.apply(preview,style,still());
  PrismLyrics.background(preview,style,palette,still());
  const backgroundStatus=document.getElementById('lyrics-background-status');if(backgroundStatus)backgroundStatus.textContent=style.backgroundTheme==='album'?(palette?.status||'暂无专辑封面 · 跟随壁纸'):style.backgroundTheme==='wallpaper'?'从当前壁纸取色':style.backgroundTheme==='transparent'?'不叠加配色，保留当前玻璃材质':PrismLyrics.backgrounds[style.backgroundTheme]+' · 固定配色';
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
  button.addEventListener('click',()=>{style=PrismLyrics.normalize({...preset.style,backgroundTheme:style.backgroundTheme,backgroundOpacity:style.backgroundOpacity,backgroundMotion:style.backgroundMotion});render(true);save();});
  document.getElementById('lyrics-presets').append(button);
 }
 for(const control of studio.querySelectorAll('[data-lyric-field]')){
  control.addEventListener('input',()=>{
   const key=control.dataset.lyricField;
   style=PrismLyrics.normalize({...style,[key]:control.type==='range'?Number(control.value):control.type==='checkbox'?control.checked:control.value});
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
  if(m.type==='motion'||m.type==='layout'){paused=!!m.paused;render();}
  if(m.type==='widgets'&&m.lyrics){
   palette=m.lyrics.palette||palette;
   const received=signature(m.lyrics.appearance);
   if(!pending||received===pending){
    clearTimeout(ackTimer);pending='';style=PrismLyrics.normalize(m.lyrics.appearance);status.textContent='已保存';render();
   }
   const gaps=document.getElementById('lyrics-gaps'),enabled=m.lyrics.holdGaps!==false;
   gaps.setAttribute('aria-checked',String(enabled));gaps.textContent=enabled?'开启':'关闭';
   render();
  }
  if(m.type==='action-error'&&pending){clearTimeout(ackTimer);status.textContent='尚未保存，请重试';}
 });
 window.addEventListener('pagehide',()=>{
  if(saveTimer&&pending)post('action:lyrics-style:'+pending);
  clearTimeout(saveTimer);clearTimeout(ackTimer);PrismLyrics.cancel(preview);
 });
 render();
})();
