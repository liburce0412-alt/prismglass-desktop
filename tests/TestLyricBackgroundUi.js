// Called only in an isolated WebView running the real desktop page.
window.runLyricBackgroundChecks = async function () {
 const results=[],wait=ms=>new Promise(resolve=>setTimeout(resolve,ms)),check=(value,name)=>{if(!value)throw new Error(name);results.push(name);};
 const message=data=>chrome.webview.dispatchEvent(new MessageEvent('message',{data}));
 const colors={album:['#a1d60a','#4b811b','#658e20'],wallpaper:['#245c9b','#366897','#55427d'],status:'使用当前专辑的配色'};
 const style={backgroundTheme:'album',backgroundOpacity:75,backgroundMotion:true,effect:'none'};
 message({type:'layout',width:1200,height:800,theme:'Sky',paused:false,cards:[{name:'Dock',x:65,y:620,w:1070,h:100,r:28,insetY:16}]});
 const lyric=appearance=>message({type:'lyrics',show:true,current:'把这一刻，留在光里',next:'下一句 · 风从身边经过',motionPaused:false,appearance:{...style,...appearance},palette:colors});
 lyric();await wait(1400);const pill=document.querySelector('#dock-lyrics'),layer=pill.querySelector('.lyric-background'),paint=layer.lastElementChild;
 check(pill.dataset.background==='album'&&paint.style.backgroundImage.includes('rgb(161, 214, 10)'),'actual Dock receives album palette colors');
 check(pill.querySelector('#lyric-current').textContent==='把这一刻，留在光里','background coloring preserves lyric text');
 const firstTransform=getComputedStyle(paint).backgroundPosition;await wait(140);check(getComputedStyle(paint).backgroundPosition!==firstTransform,'background colors flow in the real Dock');
 const running=layer.getAnimations({subtree:true});for(const animation of running)animation.currentTime=8000;
 const at8=getComputedStyle(paint).backgroundPosition;for(const animation of running)animation.currentTime=16000;check(getComputedStyle(paint).backgroundPosition!==at8,'same palette changes smoothly across the 8 and 16 second positions');
 const nextPalette={...colors,album:['#ab618a','#5585ad','#84769b']};message({type:'lyrics',show:true,current:'把这一刻，留在光里',next:'下一句 · 风从身边经过',motionPaused:false,appearance:style,palette:nextPalette});await wait(80);
 check(layer.getAnimations({subtree:true}).some(a=>a.id==='lyric-background-transition'),'album changes crossfade instead of replacing colors immediately');
 message({type:'lyrics',show:true,current:'把这一刻，留在光里',next:'下一句 · 风从身边经过',motionPaused:false,appearance:style,palette:{...colors,album:['#bb3c36','#318d81','#4b624d']}});await wait(60);
 check(layer.getAnimations({subtree:true}).filter(a=>a.id==='lyric-background-transition').length===1,'rapid track changes cancel the superseded transition');
 message({type:'lyrics',show:true,current:'把这一刻，留在光里',next:'下一句 · 风从身边经过',motionPaused:true,appearance:style,palette:colors});await wait(100);
 const frozen=getComputedStyle(paint).backgroundPosition;await wait(120);check(getComputedStyle(paint).backgroundPosition===frozen&&!layer.getAnimations({subtree:true}).some(a=>a.playState==='running'),'runtime pause stops background movement and transitions');
 lyric({backgroundTheme:'transparent'});await wait(60);check(layer.hidden&&Number(pill.style.opacity)===1,'transparent palette removes only coloring and preserves visible lyrics');
 lyric({backgroundTheme:'pine',backgroundOpacity:0});await wait(60);check(layer.hidden,'zero background strength hides color without hiding the Dock');
 lyric({backgroundTheme:'ocean',backgroundOpacity:80,backgroundMotion:false});await wait(60);check(!layer.hidden&&layer.style.opacity==='0.8'&&!layer.classList.contains('flowing'),'fixed theme and independent motion toggle apply to actual capsule');
 for(const theme of ['wallpaper','pine','sand','violet']){lyric({backgroundTheme:theme,backgroundMotion:false});await wait(20);check(pill.dataset.background===theme,'theme '+theme+' applies without changing materials');}
 message({type:'pointer',x:600,y:650,down:false});await wait(150);check(pill.getAttribute('aria-hidden')==='true','pointer approach still restores application Dock');
 return results;
};
