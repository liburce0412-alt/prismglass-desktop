'use strict';
// Orthographic lunar sphere, north up. Approximate phase; no libration calculation.
// Albedo: NASA Scientific Visualization Studio / LROC, https://svs.gsfc.nasa.gov/4720
(()=>{
const image=new Image();let map=null,lastPhase=0,paintedKey='';
image.onload=()=>{const source=document.createElement('canvas');source.width=image.width;source.height=image.height;const ctx=source.getContext('2d',{willReadFrequently:true});ctx.drawImage(image,0,0);map={data:ctx.getImageData(0,0,image.width,image.height).data,w:image.width,h:image.height};render(lastPhase)};
image.onerror=()=>{document.querySelectorAll('.lunar-sphere').forEach(el=>{el.setAttribute('aria-label','月面图像暂不可用');el.classList.add('unavailable')})};
function render(phase){lastPhase=phase;if(!map)return;const key=phase.toFixed(6);if(key===paintedKey)return;paintedKey=key;
 const size=256,radius=124,cx=128,angle=phase*2*Math.PI,lx=Math.sin(angle),lz=-Math.cos(angle),output=new ImageData(size,size),data=output.data;
 for(let y=0;y<size;y++)for(let x=0;x<size;x++){const nx=(x+.5-cx)/radius,ny=-(y+.5-cx)/radius,q=nx*nx+ny*ny;if(q>1)continue;const nz=Math.sqrt(1-q),lon=Math.atan2(nx,nz),lat=Math.asin(ny),u=Math.min(map.w-1,Math.max(0,Math.floor((.5+lon/(2*Math.PI))*map.w))),v=Math.min(map.h-1,Math.max(0,Math.floor((.5-lat/Math.PI)*map.h))),i=(y*size+x)*4,j=(v*map.w+u)*4,dot=nx*lx+nz*lz,light=.045+.955*Math.pow(Math.max(0,dot),.45);
  for(let c=0;c<3;c++)data[i+c]=Math.min(255,map.data[j+c]*light*1.35);data[i+3]=Math.min(255,Math.max(0,(1-Math.sqrt(q))*radius)*255);
 }
 document.querySelectorAll('.lunar-sphere').forEach(canvas=>{canvas.width=size;canvas.height=size;canvas.getContext('2d').putImageData(output,0,0);canvas.dataset.ready='true';canvas.setAttribute('aria-label','月球表面与当前估算月相，北方朝上')});
 document.body.dataset.moonReady='true';
}
window.addEventListener('prism-moon',e=>render(e.detail.phase));image.src='assets/moon-albedo.jpg';
})();
