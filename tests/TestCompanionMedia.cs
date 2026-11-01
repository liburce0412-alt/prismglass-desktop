using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Opt-in local media probe: real capture and decoding, muted receiver to prevent feedback.
internal static class TestCompanionMedia {
 static WebView2 view;static Form form;static string root,log;static int exitCode;static bool avSync;static JavaScriptSerializer json=new JavaScriptSerializer();
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool SetProcessDPIAware();
 static void Check(bool pass,string message){File.AppendAllText(log,(pass?"PASS ":"FAIL ")+message+Environment.NewLine);if(!pass)throw new Exception(message);}
 static Task<string> Eval(string source){return view.CoreWebView2.ExecuteScriptAsync(source);}
 static async Task Wait(string condition){for(int i=0;i<160;i++){if(await Eval(condition)=="true")return;await Task.Delay(100);}throw new Exception("Browser media deadline: "+condition);}
 static async Task<string> Offer(string name,string kind){
  await Eval("window.ready=false;window.problem='';window."+name+"=new RTCPeerConnection({iceServers:[]});"+name+".addTransceiver('"+kind+"',{direction:'recvonly'});"+name+".ontrack=e=>{document.getElementById('"+kind+"').srcObject=e.streams[0]||new MediaStream([e.track]);document.getElementById('"+kind+"').play().catch(()=>{});"+(kind=="audio"&&avSync?"syncContext.createMediaStreamSource(document.getElementById('audio').srcObject).connect(syncAnalyser);":"")+"};(async()=>{await "+name+".setLocalDescription(await "+name+".createOffer());while("+name+".iceGatheringState!=='complete')await new Promise(r=>setTimeout(r,50));window.ready=true;})().catch(e=>window.problem=String(e));");
  await Wait("window.ready===true");return json.Deserialize<string>(await Eval(name+".localDescription.sdp"));
 }
 static async Task Answer(string name,string answer){await Eval("window.ready=false;"+name+".setRemoteDescription({type:'answer',sdp:"+json.Serialize(answer)+"}).then(()=>window.ready=true).catch(e=>window.problem=String(e));");await Wait("window.ready===true");}
 static async Task<Dictionary<string,object>> Stats(string name){
  await Eval("window.stats=null;"+name+".getStats().then(s=>{window.stats={};s.forEach(r=>{if(r.type==='inbound-rtp')window.stats={bytes:r.bytesReceived||0,frames:r.framesDecoded||0,samples:r.totalSamplesReceived||0,energy:r.totalAudioEnergy||0,lost:r.packetsLost||0};});});");await Wait("window.stats!==null");return json.Deserialize<Dictionary<string,object>>(await Eval("window.stats"));
 }
 static double Value(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)?Convert.ToDouble(value):0;}
 static Task Tone(){return Task.Run(()=>{using(var wav=new MemoryStream()){using(var writer=new BinaryWriter(wav,Encoding.ASCII,true)){int count=48000;writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(48000);writer.Write(96000);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(count*2);for(int i=0;i<count;i++)writer.Write((short)(Math.Sin(i*2*Math.PI*997/48000)*1000));}wav.Position=0;using(var player=new SoundPlayer(wav))player.PlaySync();}});}
 static async Task MeasureSync(CompanionStreamRuntime runtime){
  string clip="data:video/mp4;base64,"+Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root,"sync.mp4")));
  await Eval("document.getElementById('video').style.display='none';document.body.insertAdjacentHTML('beforeend',\"<video id='stimulus' preload='auto' style='width:320px;height:180px'></video>\");document.getElementById('stimulus').src="+json.Serialize(clip));
  await Wait("document.getElementById('stimulus').readyState>=3||!!document.getElementById('stimulus').error");
  Check(await Eval("!document.getElementById('stimulus').error")=="true","browser decodes the synthetic clip");
  var location=json.Deserialize<Dictionary<string,double>>(await Eval("(()=>{const r=document.getElementById('stimulus').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()"));
  var pixel=view.PointToScreen(new Point((int)location["x"],(int)location["y"]));var bounds=runtime.CaptureBounds;
  Check(bounds.Contains(pixel),"synthetic stimulus is inside the actual captured display");
  double x=(pixel.X-bounds.Left)/(double)bounds.Width,y=(pixel.Y-bounds.Top)/(double)bounds.Height;
  await Task.Delay(500);
  await Eval("window.syncEvidence={audio:[],video:[],source:[]};window.syncCanvas=document.createElement('canvas');syncCanvas.width=syncCanvas.height=1;window.syncDrawing=syncCanvas.getContext('2d',{willReadFrequently:true});window.syncBins=new Float32Array(syncAnalyser.frequencyBinCount);window.syncStates={audio:false,video:false,source:false};window.syncTimer=setInterval(()=>{const now=performance.now(),v=document.getElementById('video'),s=document.getElementById('stimulus');syncDrawing.drawImage(v,Math.floor(v.videoWidth*"+json.Serialize(x)+"),Math.floor(v.videoHeight*"+json.Serialize(y)+"),1,1,0,0,1,1);const p=syncDrawing.getImageData(0,0,1,1).data;syncAnalyser.getFloatFrequencyData(syncBins);const bin=Math.round(997*syncAnalyser.fftSize/syncContext.sampleRate),level=Math.max(syncBins[bin-1],syncBins[bin],syncBins[bin+1]),states={video:p[0]+p[1]+p[2]>600,audio:level>-45,source:s.currentTime%2>=.5&&s.currentTime%2<.7&&!s.paused};for(const key of ['audio','video','source']){if(states[key]&&!syncStates[key])syncEvidence[key].push(now);syncStates[key]=states[key];}},10);document.getElementById('stimulus').play();");
  await Wait("document.getElementById('stimulus').ended");await Task.Delay(1000);
  await Eval("clearInterval(syncTimer);syncContext.close()");
  File.AppendAllText(log,"Flash/tone onsets in receiver performance milliseconds: "+await Eval("JSON.stringify(syncEvidence)")+Environment.NewLine);
  Check(await Eval("syncEvidence.source.length===7&&syncEvidence.video.length===7&&syncEvidence.audio.length===7")=="true","receiver identifies all seven synthetic audio and video pulses");
  File.AppendAllText(log,"Audio minus video milliseconds (negative means audio leads): "+await Eval("JSON.stringify(syncEvidence.audio.map((t,i)=>Math.round(t-syncEvidence.video[i])))")+Environment.NewLine);
  File.AppendAllText(log,"Approximate source-to-video milliseconds: "+await Eval("JSON.stringify(syncEvidence.video.map((t,i)=>Math.round(t-syncEvidence.source[i])))")+Environment.NewLine);
  await Eval("window.syncStats=null;sound.getStats().then(s=>{s.forEach(r=>{if(r.type==='inbound-rtp')window.syncStats={jitter:r.jitter,delay:r.jitterBufferDelay,emitted:r.jitterBufferEmittedCount,target:r.jitterBufferTargetDelay,concealed:r.concealedSamples,samples:r.totalSamplesReceived};})})");await Wait("syncStats!==null");File.AppendAllText(log,"Audio receiver buffering: "+await Eval("JSON.stringify(syncStats)")+Environment.NewLine);
  File.AppendAllText(log,"PC WebView2 measurement only; includes source presentation and polling uncertainty, not Safari or physical speaker latency.\n");
  Check(await Eval("syncEvidence.audio.every((t,i)=>Math.abs(t-syncEvidence.video[i])<200)")=="true","all seven PC pulses including startup stay below 200 ms; Safari acceptance remains separate");
 }
 static async Task Run(){
  await view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"browser-profile"),new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required")));view.NavigateToString("<!doctype html><html><body><p>PrismGlass local media check</p><video id='video' autoplay muted playsinline style='width:320px'></video><audio id='audio' autoplay muted></audio></body></html>");await Wait("!!document.getElementById('audio')");
  using(var runtime=new CompanionStreamRuntime(root)){
   runtime.Diagnostic=message=>{lock(json)File.AppendAllText(log,message+Environment.NewLine);};
   var metadata=json.Deserialize<Dictionary<string,object>>(json.Serialize(runtime.Describe()));var monitors=(ArrayList)metadata["monitors"];Check(monitors.Count>0,"physical screen is available without changing its mode");int monitor=(int)((Dictionary<string,object>)monitors[0])["id"];
   string videoOffer=await Offer("picture","video"),videoAnswer=await Task.Run(()=>runtime.Start("mirror",monitor,"720",videoOffer));await Answer("picture",videoAnswer);await Wait("document.getElementById('video').videoWidth>0");
   // Attach the analyser on track arrival; attaching it after negotiation adds its own startup backlog.
   if(avSync)await Eval("window.syncContext=new AudioContext();window.syncAnalyser=syncContext.createAnalyser();syncAnalyser.fftSize=2048;syncAnalyser.smoothingTimeConstant=0;window.syncQuiet=syncContext.createGain();syncQuiet.gain.value=0;syncAnalyser.connect(syncQuiet).connect(syncContext.destination);syncContext.resume();");
   string audioOffer=await Offer("sound","audio"),audioAnswer=await Task.Run(()=>runtime.StartAudio(audioOffer));await Answer("sound",audioAnswer);await Wait("sound.iceConnectionState==='connected'||sound.iceConnectionState==='completed'");
   if(avSync){await MeasureSync(runtime);await Eval("picture.close();sound.close();document.getElementById('video').srcObject=null;document.getElementById('audio').srcObject=null");await Task.Run(()=>runtime.Stop());Check(!runtime.AudioAlive&&!runtime.Alive,"AV measurement stops both capture runtimes");return;}
   await Eval("window.context=new AudioContext();window.analyser=context.createAnalyser();window.quiet=context.createGain();quiet.gain.value=0;context.createMediaStreamSource(document.getElementById('audio').srcObject).connect(analyser);analyser.connect(quiet).connect(context.destination);window.peak=0;window.sampleTimer=setInterval(()=>{const a=new Float32Array(analyser.fftSize);analyser.getFloatTimeDomainData(a);for(const x of a)window.peak=Math.max(window.peak,Math.abs(x));},20);context.resume();");
   await Tone();await Task.Delay(1500);var audio=await Stats("sound");var video=await Stats("picture");double peak=json.Deserialize<double>(await Eval("window.peak"));await Eval("clearInterval(sampleTimer);context.close()");File.AppendAllText(log,json.Serialize(new{audio=audio,video=video,decodedPeak=peak})+Environment.NewLine);
   Check(Value(audio,"bytes")>0&&Value(audio,"samples")>0&&peak>.0001,"real loopback reaches WebRTC as decoded non-silent audio");Check(Value(video,"frames")>0,"video frames decode alongside audio");
   for(int interval=1;interval<=12;interval++){
    await Task.Delay(10000);var nextAudio=await Stats("sound");var nextVideo=await Stats("picture");
    Check(runtime.AudioAlive&&runtime.Alive&&Value(nextAudio,"samples")>Value(audio,"samples")&&Value(nextVideo,"frames")>Value(video,"frames"),"audio and video continue decoding at "+(interval*10)+" seconds");
    audio=nextAudio;video=nextVideo;
   }
   File.AppendAllText(log,"Sustained receive: "+json.Serialize(new{audio=audio,video=video})+Environment.NewLine);
   await Task.Run(()=>runtime.StopAudio());await Eval("sound.close();document.getElementById('audio').srcObject=null");await Task.Delay(1100);var after=await Stats("picture");Check(!runtime.AudioAlive&&runtime.Alive&&Value(after,"frames")>Value(video,"frames"),"stopping audio releases capture while video continues decoding");
   audioOffer=await Offer("sound","audio");audioAnswer=await Task.Run(()=>runtime.StartAudio(audioOffer));await Answer("sound",audioAnswer);await Task.Delay(2000);var reopened=await Stats("sound");Check(runtime.AudioAlive&&runtime.Alive&&Value(reopened,"samples")>0,"audio reopens and decodes while the original video remains live");
   int captureCount=0;foreach(var capture in Process.GetProcessesByName("PrismAudio"))using(capture){if(!String.Equals(capture.MainModule.FileName,Path.Combine(root,"PrismAudio.exe"),StringComparison.OrdinalIgnoreCase))continue;captureCount++;capture.Kill();capture.WaitForExit(3000);}
   Check(captureCount==1,"failure probe targets only the dedicated fixture audio helper");
   video=await Stats("picture");await Task.Delay(1100);after=await Stats("picture");Check(!runtime.AudioAlive&&runtime.Alive&&Value(after,"frames")>Value(video,"frames"),"unexpected capture exit is detected while video keeps decoding");
   await Eval("sound.close();document.getElementById('audio').srcObject=null");audioOffer=await Offer("sound","audio");audioAnswer=await Task.Run(()=>runtime.StartAudio(audioOffer));await Answer("sound",audioAnswer);await Task.Delay(2000);reopened=await Stats("sound");Check(runtime.AudioAlive&&Value(reopened,"samples")>0,"audio restarts after unexpected helper exit without restarting video");
   await Eval("sound.close();document.getElementById('audio').srcObject=null");
   await Eval("picture.close();document.getElementById('video').srcObject=null");await Task.Run(()=>runtime.Stop());Check(!runtime.AudioAlive&&!runtime.Alive,"stopping media leaves both runtimes inactive");
  }
  File.AppendAllText(log,"PC WebView2 over loopback for 120 seconds plus audio reopen; not Safari, AV-sync, iPad playback, device switching, or extended soak acceptance. No recorded media saved.\n");
 }
 [STAThread]static int Main(string[] args){root=args[0];log=args[1];avSync=args.Length>2&&args[2]=="--av-sync";File.WriteAllText(log,"");SetProcessDPIAware();Application.EnableVisualStyles();using(form=new Form{Text=avSync?"PrismGlass 音画时差测试":"PrismGlass local media check",ClientSize=new Size(400,280),ShowInTaskbar=false,TopMost=avSync,StartPosition=FormStartPosition.CenterScreen})using(view=new WebView2{Dock=DockStyle.Fill}){form.Controls.Add(view);form.Shown+=async(s,e)=>{try{await Run();}catch(Exception error){File.AppendAllText(log,error.ToString());exitCode=1;}finally{form.Close();}};Application.Run(form);}return exitCode;}
}
