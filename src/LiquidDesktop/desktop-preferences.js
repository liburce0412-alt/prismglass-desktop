'use strict';
(()=>{
 let current={textScale:100,glassOpacity:100,reducedMotion:false};
 const original=new WeakMap();let scheduled=0;
 function scaleText(){scheduled=0;const factor=Math.max(.9,Math.min(1.3,(current.textScale||100)/100));
  const changes=[];
  for(const node of document.body.querySelectorAll('button,label,input,select,textarea,h1,h2,h3,h4,p,small,strong,span,summary,output,dd,dt')){
   if(node.closest('svg,#dock-lyrics,#lyrics-preview'))continue;
   let base=original.get(node);
   if(!base){const parent=node.parentElement,ancestor=parent&&original.get(parent),computed=parseFloat(getComputedStyle(node).fontSize);base={size:ancestor&&Math.abs(parseFloat(parent.style.fontSize)-computed)<.01?ancestor.size:computed,inline:node.style.fontSize};original.set(node,base);}
   changes.push([node,base]);
  }
  for(const [node,base] of changes)node.style.fontSize=factor===1?base.inline:(base.size*factor)+'px';
 }
 function schedule(){if(!scheduled)scheduled=requestAnimationFrame(scaleText);}
 function apply(value){current=value||{};document.documentElement.style.setProperty('--prism-text-scale',(current.textScale||100)/100);document.body.classList.toggle('prism-reduced-motion',!!current.reducedMotion);schedule();window.dispatchEvent(new CustomEvent('prism-preferences',{detail:current}));}
 window.chrome?.webview?.addEventListener('message',e=>{if(e.data.type==='preferences')apply(e.data.snapshot);});
 window.addEventListener('prism-settings-applied',e=>apply(e.detail));
 const observer=new MutationObserver(()=>{if(current.textScale!==100)schedule();});observer.observe(document.body,{childList:true,subtree:true});
 window.addEventListener('pagehide',()=>{observer.disconnect();cancelAnimationFrame(scheduled);});window.PrismPreferences={apply};
})();
