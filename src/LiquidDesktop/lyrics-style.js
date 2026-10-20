'use strict';
// Shared by the real Dock and the music-panel preview. No fabricated word timings.
window.PrismLyrics=(()=>{
 const defaults={font:'sans',layout:'duo',align:'center',effect:'float',texture:'solid',widthMode:'adaptive',color:'#ffffff',secondary:'#dceaf9',accent:'#9edfff',fontSize:26,weight:650,spacing:2,intensity:65,speed:100,glow:30,width:900,secondaryOpacity:80};
 const fonts={
  sans:['现代无衬线','"Segoe UI Variable Display","Microsoft YaHei UI",sans-serif'],
  humanist:['清新人文','"等线","DengXian","Segoe UI",sans-serif'],
  hei:['清晰黑体','"Microsoft YaHei UI","黑体",sans-serif'],
  song:['书卷宋体','"华文中宋","宋体","SimSun",serif'],
  kai:['手写楷体','"楷体","KaiTi","华文楷体",serif'],
  round:['圆润幼圆','"幼圆","YouYuan","Microsoft YaHei UI",sans-serif'],
  mono:['等宽节拍','Consolas,"新宋体","SimSun",monospace'],
  display:['粗黑标题','"方正粗黑宋简体","黑体","Microsoft YaHei UI",sans-serif']
 };
 const effects={float:'轻盈浮起',spring:'弹性入场',fade:'柔和渐显',flip:'立体翻页',slide:'横向滑入',zoom:'镜头推近',blur:'雾化聚焦',wave:'字间波浪',ripple:'中心涟漪',cascade:'逐字落下',stretch:'拉伸回弹',none:'静态'};
 const textures={solid:'纯色',gradient:'双色渐变',shimmer:'流动光泽',neon:'霓虹柔光',outline:'空心描边'};
 const presets={
  ice:{label:'冰蓝白字',style:{}},
  aurora:{label:'极光流彩',style:{color:'#edfff8',accent:'#7df0cc',secondary:'#d4f4ff',texture:'shimmer',effect:'wave',glow:45}},
  rose:{label:'玫瑰霓虹',style:{color:'#fff0f8',accent:'#ff80c8',secondary:'#eadbff',texture:'neon',effect:'spring',glow:65}},
  gold:{label:'琥珀唱片',style:{font:'song',color:'#fff2cb',accent:'#ffbc69',secondary:'#f2dec2',texture:'gradient',effect:'flip',spacing:4}},
  ink:{label:'月白书笺',style:{font:'kai',color:'#fffdf4',accent:'#bdd6ff',secondary:'#e4e8f1',effect:'blur',glow:12,spacing:5,weight:500}},
  mint:{label:'薄荷圆舞',style:{font:'round',color:'#e3ffed',accent:'#81edc5',secondary:'#d6eee5',texture:'gradient',effect:'ripple',glow:40}},
  neon:{label:'电光轮廓',style:{font:'display',color:'#f3f1ff',accent:'#bd9dff',texture:'outline',effect:'stretch',glow:60,weight:800}},
  cinema:{label:'电影字幕',style:{font:'hei',fontSize:24,color:'#ffffff',secondary:'#e0e0e0',effect:'fade',glow:0,weight:600,spacing:0}}
 };
 const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
 function normalize(input={}){
  const s={...defaults},choices={font:Object.keys(fonts),layout:['duo','single','split','stack'],align:['left','center','right'],effect:Object.keys(effects),texture:Object.keys(textures),widthMode:['adaptive','fixed']};
  for(const [key,values] of Object.entries(choices))if(values.includes(input?.[key]))s[key]=input[key];
  for(const key of ['color','secondary','accent'])if(/^#[0-9a-f]{6}$/i.test(input?.[key]))s[key]=input[key].toLowerCase();
  const ranges={fontSize:[18,32],weight:[400,900],spacing:[-2,12],intensity:[0,100],speed:[50,180],glow:[0,100],width:[420,1200],secondaryOpacity:[30,100]};
  for(const [key,[a,b]] of Object.entries(ranges))if(typeof input?.[key]==='number'&&Number.isFinite(input[key]))s[key]=Math.round(clamp(input[key],a,b));
  return s;
 }
 function apply(container,input,still=false){
  const s=normalize(input);
  container.classList.add('lyric-copy');container.classList.toggle('reduced',still);
  container.dataset.layout=s.layout;container.dataset.texture=s.texture;container.dataset.effect=s.effect;
  container.style.fontFamily=fonts[s.font][1];container.style.textAlign=s.align;
  const vars={color:s.color,secondary:s.secondary,accent:s.accent,size:s.fontSize+'px',weight:s.weight,spacing:s.spacing/100+'em','secondary-size':Math.round(s.fontSize*.69)+'px','secondary-opacity':s.secondaryOpacity/100,glow:s.glow/100*12+'px','light-opacity':s.intensity/100*.3,'light-duration':600000/s.speed+'ms'};
  for(const [key,value] of Object.entries(vars))container.style.setProperty('--lyric-'+key,String(value));
  return s;
 }
 function cancel(container){
  for(const a of container.getAnimations({subtree:true}))if(a.id==='lyric-effect')a.cancel();
  for(const text of container.querySelectorAll('.lyric-main,.lyric-secondary'))if(text.querySelector('.lyric-letter'))text.textContent=text.textContent;
 }
 function animate(container,input,still=false){
  cancel(container);const s=normalize(input);
  if(still||s.effect==='none'||s.intensity===0)return;
  const main=container.querySelector('.lyric-main'),line=main?.parentElement,secondary=container.querySelector('.lyric-secondary')?.parentElement;
  if(!main||!line)return;
  const k=s.intensity/100,duration=52000/s.speed,easing='cubic-bezier(.16,1,.3,1)';
  function run(el,frames,options={}){const a=el.animate(frames,{duration,easing,...options});a.id='lyric-effect';return a;}
  const finish={opacity:1,transform:'none',filter:'none'};
  const entrances={
   float:[{opacity:.15,transform:`translateY(${12*k}px) scale(${1-.035*k})`},finish],
   fade:[{opacity:0},finish],
   flip:[{opacity:0,transform:`perspective(500px) rotateX(${75*k}deg) translateY(${5*k}px)`},finish],
   slide:[{opacity:0,transform:`translateX(${-28*k}px)`},finish],
   zoom:[{opacity:0,transform:`scale(${1+.2*k})`},finish],
   blur:[{opacity:0,filter:`blur(${4*k}px)`,transform:`scale(${1+.025*k})`},finish],
   spring:[{opacity:0,transform:`translateY(${16*k}px) scale(${1-.08*k})`},{opacity:1,transform:`translateY(${-4*k}px) scale(${1+.02*k})`,offset:.58},finish],
   stretch:[{opacity:.1,transform:`scale(${1+.22*k},${1-.2*k})`},{opacity:1,transform:`scale(${1-.04*k},${1+.04*k})`,offset:.6},finish]
  };
  if(['wave','ripple','cascade'].includes(s.effect)){
   // Limit individual animations; long lyrics use the normal line entrance.
   const chars=typeof Intl.Segmenter==='function'?[...new Intl.Segmenter(undefined,{granularity:'grapheme'}).segment(main.textContent)].map(p=>p.segment):Array.from(main.textContent);
   if(chars.length<=80){
    main.replaceChildren(...chars.map(char=>{const span=document.createElement('span');span.className='lyric-letter';span.textContent=char;return span;}));
    const letters=[...main.children],sequence=letters.map((span,i)=>{
     const distance=s.effect==='ripple'?Math.abs(i-(chars.length-1)/2):i;
     const delay=distance*Math.min(28,260/Math.max(1,chars.length))*100/s.speed;
     const y=s.effect==='cascade'?-12*k:10*k;
     return run(span,[{opacity:.05,transform:`translateY(${y}px) scale(${1-.12*k})`},{opacity:1,transform:`translateY(${-3*k}px)`,offset:.6},finish],{delay,fill:'backwards'});
    });
    Promise.all(sequence.map(a=>a.finished)).then(()=>{if(main.firstElementChild===letters[0])main.textContent=chars.join('');},()=>{});
   }else run(line,entrances.float);
  }else run(line,entrances[s.effect]||entrances.float);
  if(secondary&&!secondary.hidden)run(secondary,[{opacity:.15,transform:`translateY(${4*k}px)`},finish],{duration:duration*.85,delay:40});
 }
 return {defaults,fonts,effects,textures,presets,normalize,apply,animate,cancel};
})();
