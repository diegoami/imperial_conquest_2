#!/usr/bin/env node
// Production Godot capture + clean FFmpeg export. See docs/design/v011-observer-recordings.md.
import {spawnSync} from 'node:child_process';
import {mkdirSync, readdirSync, writeFileSync, existsSync} from 'node:fs';
import {resolve, dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {fps, freshSeed, seedValue, productionScenarios, rotation, eventsFrom, verifyCaptureProbe, verifyFrameTimes, verifyProbe, sha256, mediaUrl} from './observer-media.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const options = Object.fromEntries(process.argv.slice(2).map(arg => {
  const match = /^--([a-z-]+)=(.+)$/.exec(arg);
  if (!match) throw Error(`Expected --option=value, got ${arg}`);
  return [match[1], match[2]];
}));
for (const key of Object.keys(options))
  if (!['out','godot','ffmpeg','ffprobe','seconds','seed','scenarios'].includes(key)) throw Error(`Unknown option ${key}`);
if (!options.out) throw Error('Usage: node scripts/record-observers.mjs --out=rendered/observer/<new-batch> [--seconds=180]');
const output = resolve(options.out);
if (existsSync(output)) throw Error('Use a new output directory; existing recordings are never overwritten');
const seconds = Number(options.seconds ?? 180);
if (!Number.isInteger(seconds) || seconds < 5 || seconds > 180) throw Error('Recording budget: 5–180 seconds per clip');
const scenarios = productionScenarios(root, readdirSync(join(root, 'data/scenarios')).filter(f => f.endsWith('.json')));
const selected = options.scenarios?.split(',') ?? rotation(scenarios, Math.floor(Date.now() / 604800000));
if (!selected.length || new Set(selected).size !== selected.length || selected.some(id => !scenarios.some(s => s.id === id)))
  throw Error('Choose distinct listed production scenario IDs');
const baseSeed = options.seed === undefined ? freshSeed() : seedValue(options.seed);
const executable = {godot: options.godot ?? 'godot', ffmpeg: options.ffmpeg ?? 'ffmpeg', ffprobe: options.ffprobe ?? 'ffprobe', dotnet:'dotnet'};
function run(tool, args, env = {}, timeout = 180000) {
  const result = spawnSync(executable[tool], args, {cwd: root, env: {...process.env, ...env}, encoding: 'utf8', timeout, maxBuffer: 16 * 1024 * 1024, windowsHide: true});
  if (result.error || result.status !== 0) throw Error(`${tool} failed: ${result.error?.message ?? result.stderr?.slice(-3000) ?? result.status}`);
  return (result.stdout ?? '') + (result.stderr ?? '');
}
const commit = spawnSync('git', ['rev-parse','HEAD'], {cwd:root,encoding:'utf8'}).stdout?.trim();
if (!/^[a-f0-9]{40}$/.test(commit ?? '')) throw Error('Need a committed source checkout');
const dirty = spawnSync('git', ['status','--porcelain','--untracked-files=no'], {cwd:root,encoding:'utf8'});
if (dirty.status !== 0 || dirty.stdout.trim()) throw Error('Commit tracked source changes before recording; provenance must identify exact bytes');
console.log(`Building exact source ${commit}`);
run('dotnet',['build',join(root,'godot/IC2.MapViewer.csproj'),'--nologo','-v','q','-clp:ErrorsOnly']);
run('godot',['--headless','--path',join(root,'godot'),'--import']);
const imported = spawnSync('git',['status','--porcelain','--untracked-files=no'],{cwd:root,encoding:'utf8'});
if (imported.status !== 0 || imported.stdout.trim()) throw Error('Resource import changed tracked files; review churn before recording');
mkdirSync(dirname(output), {recursive:true}); mkdirSync(output);
const stamp = new Date().toISOString().replace(/[-:.]/g,'').replace('Z','z');
const clips = [];
for (const [index, scenarioId] of selected.entries()) {
  let clip;
  for (let attempt = 0; attempt < 3 && !clip; attempt++) {
    const seed = BigInt.asUintN(64, baseSeed + BigInt(index * 3 + attempt));
    const id = `${scenarioId}-${seed}-${commit.slice(0,12)}-${stamp}`;
    const folder = join(output,id); mkdirSync(folder);
    const profile = join(folder,'profile'); mkdirSync(profile);
    const source = join(folder,'capture.ogv');
    console.log(`Recording ${scenarioId}, seed ${seed}, ${seconds}s (attempt ${attempt+1}/3)`);
    const log = run('godot', ['--path',join(root,'godot'),'--windowed','--rendering-method','gl_compatibility',
      '--write-movie',source,'--fixed-fps',String(fps),'--quit-after',String(seconds*fps),
      '--log-file',join(profile,'engine.log'),'--','--observe-loop',`--observe-scenario=${scenarioId}`,
      `--observe-seed=${seed}`,`--observe-recording=${profile}`],
      {XDG_DATA_HOME:profile,XDG_CONFIG_HOME:profile,XDG_CACHE_HOME:profile}, seconds*10000+120000);
    writeFileSync(join(folder,'capture.log'),log);
    if (!log.split(/\r?\n/).some(line=>line.startsWith('IC2 diagnostics ')&&line.includes(`+${commit} `)))
      throw Error('Godot assembly identity does not match the recorded source commit');
    if (/^ERROR:/m.test(log) || /Observation stopped:/i.test(log)) throw Error(`Godot reported a capture error: ${folder}/capture.log`);
    const campaigns = eventsFrom(log,'OBSERVER_CAMPAIGN');
    const viewpoints = eventsFrom(log,'OBSERVER_VIEW');
    const routes = eventsFrom(log,'OBSERVER_ROUTE');
    if (!campaigns.length || campaigns[0].seed !== String(seed) || campaigns[0].scenarioId !== scenarioId || campaigns.some(c=>c.pack!=='authored'))
      throw Error('Capture lacks matching authored-only provenance');
    if (!routes.length) { console.warn('No watched movement in this excerpt; retrying with a fresh seed.'); continue; }
    const nativeProbe=inspect(source);verifyFrameTimes(nativeProbe.frames,seconds);
    const native=verifyCaptureProbe(nativeProbe);
    const videoName = id+'.mp4', posterName = id+'.jpg', uploadName=id+'-youtube.mp4';
    const videoPath = join(output,videoName), posterPath = join(output,posterName), uploadPath=join(output,uploadName);
    run('ffmpeg',['-hide_banner','-loglevel','error','-n','-i',source,'-c:v','libx264','-crf','23','-preset','fast',
      '-pix_fmt','yuv420p','-r',String(fps),'-fps_mode','cfr','-movflags','+faststart','-c:a','aac','-ar','48000','-ac','2','-b:a','128k',videoPath],{},seconds*4000+120000);
    // Separate upload master; no scaling or frame-rate inflation.
    run('ffmpeg',['-hide_banner','-loglevel','error','-n','-i',source,'-c:v','libx264','-profile:v','high','-level:v','4.1',
      '-crf','18','-preset','medium','-bf','2','-g','15','-keyint_min','15','-sc_threshold','0','-flags','+cgop',
      '-pix_fmt','yuv420p','-r',String(fps),'-fps_mode','cfr','-c:a','aac','-profile:a','aac_low','-ar','48000','-ac','2','-b:a','384k',
      '-movflags','+faststart',uploadPath],{},seconds*6000+120000);
    function inspect(file){return JSON.parse(run('ffprobe',['-v','error','-show_frames','-show_entries','frame=media_type,best_effort_timestamp_time',
      '-show_streams','-show_format','-of','json',file],{},seconds*1000+120000));}
    const probe = inspect(videoPath);
    const video = verifyProbe(probe,seconds);
    const uploadProbe=inspect(uploadPath);verifyProbe(uploadProbe,seconds);
    console.log(`Verified native capture and both MP4s: ${video.width}×${video.height}, constant ${fps} fps, ${seconds*fps} decoded frames each.`);
    const posterAt = Math.min(seconds-0.5,Math.max(0,Number(routes[0].frame)/fps+0.25));
    run('ffmpeg',['-hide_banner','-loglevel','error','-n','-ss',String(posterAt),'-i',videoPath,'-frames:v','1','-q:v','3',posterPath]);
    clip = {id,scenarioId,title:scenarios.find(s=>s.id===scenarioId).name,seed:String(seed),sourceCommit:commit,
      recordedAt:new Date().toISOString(),excerpt:true,durationSeconds:seconds,width:video.width,height:video.height,
      fps,bytes:Number(probe.format.size),sha256:sha256(videoPath),posterSha256:sha256(posterPath),
      src:mediaUrl(videoName),poster:mediaUrl(posterName),videoFile:videoName,posterFile:posterName,
      encoding:{container:'mp4',video:'h264',pixelFormat:'yuv420p',audio:'aac',fastStart:true},campaigns,viewpoints,routes};
    clip.uploadMaster={src:mediaUrl(uploadName),file:uploadName,sha256:sha256(uploadPath),bytes:Number(uploadProbe.format.size),
      profile:'High',crf:18,audioBitrate:384000,width:video.width,height:video.height,fps,
      decodedFrames:seconds*fps,nativeWidth:native.width,nativeHeight:native.height};
    writeFileSync(join(output,id+'.json'),JSON.stringify(clip,null,2)+'\n');
    clips.push(clip);
  }
  if (!clip) throw Error(`No publishable moving-unit excerpt after three seeds: ${scenarioId}. Prior playlist must remain intact.`);
}
writeFileSync(join(output,'manifest.json'),JSON.stringify({schemaVersion:1,generation:stamp,generatedAt:new Date().toISOString(),sourceCommit:commit,clips},null,2)+'\n');
console.log(`Validated ${clips.length} clips. Manifest: ${join(output,'manifest.json')}`);
