// Isolate the production shader's border contribution; measure real GPU pixels, not frame rate.
(() => {
 const canvas=document.querySelector('#scene'),gl=canvas.getContext('webgl'),original=gl.getParameter(gl.CURRENT_PROGRAM);
 const shaders=gl.getAttachedShaders(original),fragment=shaders.find(s=>gl.getShaderParameter(s,gl.SHADER_TYPE)===gl.FRAGMENT_SHADER);
 const source=gl.getShaderSource(fragment),edge=source.replace(/c\+=mix\(flowingColor,[^;]+?\)\*following\*hover\*([^;]+);/,'c=vec3(following*hover*$1);');
 if(edge===source)throw Error('Production edge contribution not found');
 const shader=gl.createShader(gl.FRAGMENT_SHADER),program=gl.createProgram();
 try {
  gl.shaderSource(shader,edge);gl.compileShader(shader);
  if(!gl.getShaderParameter(shader,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(shader));
  gl.attachShader(program,shader);gl.attachShader(program,shaders.find(s=>s!==fragment));gl.bindAttribLocation(program,0,'a');gl.linkProgram(program);
  if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(program));
  gl.useProgram(program);gl.disable(gl.SCISSOR_TEST);gl.disable(gl.BLEND);
  for(let i=0;i<gl.getProgramParameter(original,gl.ACTIVE_UNIFORMS);i++){
   const u=gl.getActiveUniform(original,i),at=gl.getUniformLocation(program,u.name);if(at===null)continue;
   const value=gl.getUniform(original,gl.getUniformLocation(original,u.name));
   if(u.type===gl.FLOAT)gl.uniform1f(at,value);
   else if(u.type===gl.FLOAT_VEC2)gl.uniform2fv(at,value);
   else if(u.type===gl.FLOAT_VEC4)gl.uniform4fv(at,value);
   else if(u.type===gl.SAMPLER_2D)gl.uniform1i(at,value);
   else throw Error('Unhandled production uniform '+u.name);
  }
  const one=(n,v)=>gl.uniform1f(gl.getUniformLocation(program,n),v),two=(n,x,y)=>gl.uniform2f(gl.getUniformLocation(program,n),x,y);
  two('center',640,180);two('halfSize',540,90);two('lean',0,0);one('radius',24);one('fixedShape',1);one('liquidAmplitude',0);one('hover',1);one('press',0);one('material',1);one('styleEnabled',0);one('surfaceOpacity',1);one('opaqueSurface',0);
  const draw=(x,y)=>{two('pointer',(x-640)/540,(y-180)/90);gl.clear(gl.COLOR_BUFFER_BIT);gl.drawArrays(gl.TRIANGLES,0,6);};
  const points=[];
  for(const [x,y] of [[420,132],[760,132],[430,142],[640,180]]){
   draw(x,y);const row=new Uint8Array(1000*4),strip=new Uint8Array(1000*8*4);
   gl.readPixels(140,canvas.height-98,1000,8,gl.RGBA,gl.UNSIGNED_BYTE,strip);
   for(let j=0;j<8;j++)for(let i=0;i<1000;i++)row[i*4]=Math.max(row[i*4],strip[(j*1000+i)*4]);
   let peak=0;for(let i=0;i<1000;i++)peak=Math.max(peak,row[i*4]);
   const hot=[];for(let i=0;i<1000;i++)if(row[i*4]>=peak-1)hot.push(i+140.5);
   const center=hot.reduce((a,b)=>a+b,0)/hot.length;
   if(peak<95||Math.abs(center-x)>3)throw Error('Border did not follow pointer: '+JSON.stringify({x,y,center,peak}));
   points.push({x,y,center,peak});
  }
  draw(640,94);const band=[];
  for(let y=89;y<=112;y++){const p=new Uint8Array(4);gl.readPixels(640,canvas.height-y-1,1,1,gl.RGBA,gl.UNSIGNED_BYTE,p);band.push(p[0]);}
  const peak=Math.max(...band),width=band.filter(v=>v>=peak/2).length;
  if(peak<150||width<1||width>3)throw Error('Border became invisible or too broad: '+JSON.stringify({peak,width,band}));
  return {points,halfMaximumWidthPixels:width,peak,profile:band,presentationMeasured:false};
 } finally {gl.useProgram(original);gl.deleteProgram(program);gl.deleteShader(shader);}
})()
