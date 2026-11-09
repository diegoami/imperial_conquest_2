import {spawn,spawnSync} from 'node:child_process';
import {createReadStream} from 'node:fs';
import {createHash} from 'node:crypto';
import {createInterface} from 'node:readline';
import {fps,verifyCaptureProbe,verifyVideoMetadata} from './observer-media.mjs';

export async function hashFile(file){const hash=createHash('sha256');for await(const chunk of createReadStream(file))hash.update(chunk);return hash.digest('hex');}
export function movieFrames(log){
  const matches=[...log.matchAll(/^(\d+) frames at (\d+) FPS/mg)];
  if(matches.length!==1||Number(matches[0][2])!==fps)throw Error('Movie Maker must report exactly one 30 fps capture');
  const frames=Number(matches[0][1]);if(!Number.isSafeInteger(frames)||frames<=0)throw Error('Invalid capture frame count');return frames;
}
function run(executable,args,timeout=180000){
  const result=spawnSync(executable,args,{encoding:'utf8',timeout:Math.ceil(timeout),maxBuffer:4*1024*1024,windowsHide:true});
  if(result.error||result.status!==0)throw Error(`${executable} failed: ${result.error?.message??result.stderr?.slice(-3000)??result.status}`);
  if(result.stderr?.trim())throw Error('Media inspection/encoding reported an error: '+result.stderr.slice(-3000));
  return result.stdout;
}
export function probeMedia(ffprobe,file){return JSON.parse(run(ffprobe,['-v','error','-show_streams','-show_format','-of','json',file]));}

// Stream timestamp verification so multi-hour full campaigns do not require
// hundreds of megabytes of ffprobe JSON or whole-video buffers in memory.
export function checkFrameStream(ffprobe,file,frames,{native=false}={}){
  return new Promise((resolve,reject)=>{
    const child=spawn(ffprobe,['-v','error','-select_streams','v:0','-show_frames','-show_entries','frame=best_effort_timestamp_time','-of','csv=p=0',file],{windowsHide:true});
    const lines=createInterface({input:child.stdout});let count=0,previous=-1,errorText='',invalid;
    const deadline=setTimeout(()=>{invalid=Error('Frame verification exceeded its bounded runtime');child.kill();},Math.max(120000,frames/fps*1000+120000));
    child.stderr.on('data',data=>{errorText=(errorText+data).slice(-3000);});
    child.on('error',error=>{clearTimeout(deadline);reject(error);});
    lines.on('line',line=>{
      if(!line.trim()||invalid)return;
      const time=Number(line.split(',')[0]),tick=time*fps;
      if(!Number.isFinite(time)||time<0||time<=previous||time>=frames/fps
          ||(native?Math.abs(tick-Math.round(tick))>0.0006:Math.abs(time-count/fps)>0.00002)){
        invalid=Error(`Invalid ${native?'capture':'CFR MP4'} timestamp at decoded image ${count}`);child.kill();return;
      }
      count++;previous=time;
    });
    child.on('close',code=>{
      clearTimeout(deadline);
      if(invalid||code!==0||errorText.trim()||!count||(!native&&count!==frames))reject(invalid??Error(`Frame decode failed/count mismatch: ${count}/${frames} ${errorText}`));
      else resolve(count);
    });
  });
}

export async function encodeObserver({ffmpeg,ffprobe,source,log,webPath,uploadPath,posterPath,excerptFrames,excerptStartFrame=0}){
  const frames=movieFrames(log),seconds=frames/fps;
  const nativeProbe=probeMedia(ffprobe,source),native=verifyCaptureProbe(nativeProbe);
  if(native.codec_name!=='theora'||Math.abs(Number(native.duration)-seconds)>0.00002)throw Error('Native 30 Hz capture duration differs from Movie Maker counter');
  const nativeDecodedImages=await checkFrameStream(ffprobe,source,frames,{native:true});
  const webFrames=excerptFrames??frames;
  if(!Number.isSafeInteger(webFrames)||webFrames<=0||excerptStartFrame<0||excerptStartFrame+webFrames>frames)throw Error('Excerpt lies outside the actual captured campaign');
  // Reify Theora's zero-byte still-image repeats at the recorded 30 Hz cadence.
  // Padding is bounded by the proven capture frame counter, never an invented FPS.
  const common=['-pix_fmt','yuv420p','-vf',`fps=${fps},tpad=stop_mode=clone:stop=-1`,'-r',String(fps),'-fps_mode','cfr',
    '-c:a','aac','-ar','48000','-ac','2','-movflags','+faststart'];
  const timeout=Math.max(180000,seconds*4000+120000);
  run(ffmpeg,['-hide_banner','-loglevel','error','-n','-ss',String(excerptStartFrame/fps),'-i',source,
    '-c:v','libx264','-crf','23','-preset','fast',...common,'-frames:v',String(webFrames),'-t',String(webFrames/fps),'-b:a','128k',webPath],timeout);
  run(ffmpeg,['-hide_banner','-loglevel','error','-n','-i',source,'-c:v','libx264','-profile:v','high','-level:v','4.1',
    '-crf','18','-preset','medium','-bf','2','-g','15','-keyint_min','15','-sc_threshold','0','-flags','+cgop',...common,
    '-profile:a','aac_low','-b:a','384k','-frames:v',String(frames),'-t',String(seconds),uploadPath],timeout);
  const webProbe=probeMedia(ffprobe,webPath),uploadProbe=probeMedia(ffprobe,uploadPath);
  verifyVideoMetadata(webProbe,webFrames/fps);verifyVideoMetadata(uploadProbe,seconds);
  await checkFrameStream(ffprobe,webPath,webFrames);await checkFrameStream(ffprobe,uploadPath,frames);
  const high=uploadProbe.streams.find(s=>s.codec_type==='video');
  if(high.profile!=='High')throw Error('YouTube upload master is not H.264 High');
  run(ffmpeg,['-hide_banner','-loglevel','error','-n','-ss',String(Math.min(2,Math.max(0,webFrames/fps-0.5))),'-i',webPath,'-frames:v','1','-q:v','3',posterPath]);
  return {frames,seconds,webFrames,webSeconds:webFrames/fps,nativeDecodedImages,width:native.width,height:native.height,fps,
    webBytes:Number(webProbe.format.size),uploadBytes:Number(uploadProbe.format.size),
    webSha256:await hashFile(webPath),uploadSha256:await hashFile(uploadPath),posterSha256:await hashFile(posterPath)};
}
