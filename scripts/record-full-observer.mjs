#!/usr/bin/env node
// Records exactly one completed production campaign. Never uploads or retries an upload.
import {spawn,spawnSync} from 'node:child_process';
import {mkdirSync,existsSync,readFileSync,writeFileSync,appendFileSync,readdirSync} from 'node:fs';
import {resolve,dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {fps,seedValue,eventsFrom,productionScenarios,mediaUrl} from './observer-media.mjs';
import {encodeObserver} from './observer-encoding.mjs';

const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const options=Object.fromEntries(process.argv.slice(2).map(arg=>{const m=/^--([a-z-]+)=(.+)$/.exec(arg);if(!m)throw Error('Expected --option=value');return[m[1],m[2]];}));
for(const key of Object.keys(options))if(!['out','godot','ffmpeg','ffprobe','seed','scenario','max-seconds','excerpt-seconds'].includes(key))throw Error(`Unknown option ${key}`);
if(!options.out)throw Error('Use --out=<new-folder>; one full campaign is recorded, no upload is performed');
const output=resolve(options.out),scenarioId=options.scenario??'classical-mediterranean',seed=seedValue(options.seed??'44');
if(existsSync(output))throw Error('Output exists; preserve the prior attempt and choose a new folder');
const maximum=Number(options['max-seconds']??21600),excerptSeconds=Number(options['excerpt-seconds']??180);
if(!Number.isInteger(maximum)||maximum<5||maximum>21600||!Number.isInteger(excerptSeconds)||excerptSeconds<5||excerptSeconds>180)throw Error('Bound capture to 5–21600 seconds and excerpt to 5–180 seconds');
const scenario=productionScenarios(root,readdirSync(join(root,'data/scenarios')).filter(f=>f.endsWith('.json'))).find(s=>s.id===scenarioId);
if(!scenario)throw Error('Choose a listed production scenario, not a shortened test campaign');
const tools={godot:options.godot??'godot',ffmpeg:options.ffmpeg??'ffmpeg',ffprobe:options.ffprobe??'ffprobe',dotnet:'dotnet',pwsh:'pwsh',git:'git'};
function sync(tool,args,allowed=[0]){const result=spawnSync(tools[tool],args,{cwd:root,encoding:'utf8',windowsHide:true,maxBuffer:4*1024*1024,timeout:180000});if(result.error||!allowed.includes(result.status))throw Error(`${tool} failed: ${result.error?.message??result.stderr}`);return result.stdout;}
const commit=sync('git',['rev-parse','HEAD']).trim();
if(sync('git',['status','--porcelain','--untracked-files=no']).trim())throw Error('Commit tracked source before capturing');
sync('dotnet',['build',join(root,'godot/IC2.MapViewer.csproj'),'--nologo','-v','q','-clp:ErrorsOnly']);
sync('godot',['--headless','--path',join(root,'godot'),'--import']);
console.log(sync('pwsh',['-NoProfile','-File',join(root,'scripts/check-godot-churn.ps1')],[0,1]).trim());
if(sync('git',['status','--porcelain','--untracked-files=no']).trim())throw Error('Import left a real tracked change; capture refused');
mkdirSync(dirname(output),{recursive:true});mkdirSync(output);mkdirSync(join(output,'profile'));
const logPath=join(output,'capture.log'),source=join(output,'campaign.ogv');
const request={scenarioId,seed:String(seed),sourceCommit:commit,maximumSeconds:maximum,fps,nativeWidth:1920,nativeHeight:1080,
  startedAt:new Date().toISOString(),uploadAuthorizedCount:1,channelId:'UCQHMx6rNOKfLxFRI4_Tf6cA',profile:'lynxsupercluster',playlistTitle:'Open Imperial Conquest'};
writeFileSync(join(output,'capture-request.json'),JSON.stringify(request,null,2)+'\n');
console.log(`Capturing one complete ${scenario.name} campaign, seed ${seed}, source ${commit}. An exhausted budget will NOT count as completion.`);
const args=['--path',join(root,'godot'),'--windowed','--resolution','1920x1080','--disable-vsync','--rendering-method','gl_compatibility','--write-movie',source,
  '--fixed-fps',String(fps),'--quit-after',String(maximum*fps),'--log-file',join(output,'profile/engine.log'),'--','--observe-loop',
  `--observe-scenario=${scenarioId}`,`--observe-seed=${seed}`,`--observe-recording=${join(output,'profile')}`,'--observe-complete'];
await new Promise((resolveCapture,reject)=>{
  const child=spawn(tools.godot,args,{cwd:root,windowsHide:true,env:{...process.env,XDG_DATA_HOME:join(output,'profile'),XDG_CONFIG_HOME:join(output,'profile'),XDG_CACHE_HOME:join(output,'profile')}});
  const deadline=setTimeout(()=>{child.kill();reject(Error('Full capture exceeded its bounded wall-clock runtime; no upload is allowed'));},maximum*3000+180000);
  child.on('error',error=>{clearTimeout(deadline);reject(error);});
  let buffer='';
  child.stdout.on('data',data=>{
    appendFileSync(logPath,data);buffer+=data.toString();const lines=buffer.split(/\r?\n/);buffer=lines.pop();
    for(const line of lines)if(line.startsWith('OBSERVER_PROGRESS ')){
      const progress=JSON.parse(line.slice('OBSERVER_PROGRESS '.length));writeFileSync(join(output,'progress.json'),JSON.stringify(progress,null,2)+'\n');
      if(progress.turn%24===0)console.log(`Campaign round ${progress.turn}, ${progress.yearBc} BC · ${Math.round(progress.frame/fps/60)} recorded minutes`);
    }
  });
  child.stderr.on('data',data=>appendFileSync(logPath,data));
  child.on('close',code=>{clearTimeout(deadline);if(code!==0)reject(Error(`Godot exited ${code}; preserve diagnostics and do not upload`));else resolveCapture();});
});
const log=readFileSync(logPath,'utf8'),campaigns=eventsFrom(log,'OBSERVER_CAMPAIGN'),endings=eventsFrom(log,'OBSERVER_END');
if(/^ERROR:/m.test(log)||/Observation stopped:/i.test(log)||!log.split(/\r?\n/).some(line=>line.startsWith('IC2 diagnostics ')&&line.includes(`+${commit} `)))throw Error('Capture errors/build mismatch; no complete-video claim or upload allowed');
if(campaigns.length!==1||endings.length!==1||campaigns[0].seed!==String(seed)||campaigns[0].scenarioId!==scenarioId||campaigns[0].pack!=='authored')throw Error('A single real terminal event is required. Budget exhaustion or a second campaign is not a full campaign.');
const routes=eventsFrom(log,'OBSERVER_ROUTE'),viewpoints=eventsFrom(log,'OBSERVER_VIEW');
const frameMatch=/^(\d+) frames at 30 FPS/m.exec(log);if(!frameMatch)throw Error('Missing native movie frame counter');
const frames=Number(frameMatch[1]),webFrames=Math.min(frames,excerptSeconds*fps);
const id=`${scenarioId}-${seed}-${commit.slice(0,12)}`;
const videoFile=id+'-excerpt.mp4',uploadFile=id+'-youtube.mp4',posterFile=id+'.jpg';
const verification=await encodeObserver({ffmpeg:tools.ffmpeg,ffprobe:tools.ffprobe,source,log,
  webPath:join(output,videoFile),uploadPath:join(output,uploadFile),posterPath:join(output,posterFile),excerptFrames:webFrames});
const clip={id,scenarioId,title:scenario.name,seed:String(seed),sourceCommit:commit,recordedAt:new Date().toISOString(),excerpt:webFrames<frames,
  excerptStartSeconds:0,durationSeconds:verification.webSeconds,width:1920,height:1080,fps,
  videoFile,posterFile,src:mediaUrl(videoFile),poster:mediaUrl(posterFile),sha256:verification.webSha256,posterSha256:verification.posterSha256,
  uploadMaster:{file:uploadFile,src:mediaUrl(uploadFile),sha256:verification.uploadSha256,bytes:verification.uploadBytes,width:1920,height:1080,fps,decodedFrames:frames,nativeWidth:1920,nativeHeight:1080},
  fullCampaign:{completed:true,durationSeconds:verification.seconds,ending:endings[0],youtubeUrl:null},campaigns,routes,viewpoints,verification};
writeFileSync(join(output,id+'.json'),JSON.stringify(clip,null,2)+'\n');
writeFileSync(join(output,'pilot.json'),JSON.stringify({schemaVersion:1,sourceCommit:commit,clip,request},null,2)+'\n');
writeFileSync(join(output,'upload-request.json'),JSON.stringify({profile:request.profile,channelId:request.channelId,playlistTitle:request.playlistTitle,
  privacy:'unlisted',maximumNewUploads:1,file:join(output,uploadFile),sha256:verification.uploadSha256,
  title:`Open Imperial Conquest | ${scenario.name} | Full observer campaign`,
  description:`Repository: https://github.com/diegoami/imperial_conquest_2\nDownload: https://github.com/diegoami/imperial_conquest_2/releases\n\nOne complete computer-controlled campaign, from its production start to ${endings[0].ending}.\nScenario: ${scenarioId}; seed ${seed}; build ${commit}.\nNative 1920×1080, constant 30 fps. Authored assets; automated tactical observer battles.\nNo human orders, narration or original-game media.`,
  categoryId:'20',madeForKids:false},null,2)+'\n');
console.log(`COMPLETE PILOT READY: ${join(output,uploadFile)}. Verified ${frames} native/MP4 frames at 1080p30. No upload has been performed.`);
