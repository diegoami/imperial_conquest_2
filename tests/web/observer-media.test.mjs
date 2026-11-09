import {test} from 'node:test';
import assert from 'node:assert/strict';
import {seedValue,rotation,eventsFrom,verifyProbe,verifyCaptureProbe,verifyFrameTimes,verifyNativeCapture,mediaUrl} from '../../scripts/observer-media.mjs';
import {validateManifest,nextClip,youtubeCampaignUrl} from '../../web/observer/playlist.mjs';

test('64-bit recording seeds do not lose precision through JavaScript numbers',()=>{
  assert.equal(String(seedValue('18446744073709551615')),'18446744073709551615');
  for(const invalid of ['-1','1.5','18446744073709551616','0xff',''])assert.throws(()=>seedValue(invalid));
});
test('rotation always includes Classical and eventually covers every other scenario',()=>{
  const scenarios=[{id:'classical-mediterranean'},{id:'italy-500bc'},{id:'near-east-1350bc'},{id:'near-east-750bc'}];
  const seen=new Set();for(let week=0;week<3;week++){const batch=rotation(scenarios,week);assert.equal(batch[0],'classical-mediterranean');seen.add(batch[1]);}
  assert.equal(seen.size,3);assert.throws(()=>rotation([{id:'classical-mediterranean'}],0));
});
test('capture provenance parses only explicit structured events',()=>{
  assert.deepEqual(eventsFrom('Godot startup\nOBSERVER_VIEW {"nationId":"rome","frame":12}\nother output','OBSERVER_VIEW'),[{nationId:'rome',frame:12}]);
  assert.throws(()=>eventsFrom('OBSERVER_VIEW not-json','OBSERVER_VIEW'));
});
test('media references cannot escape the public release asset path',()=>{
  assert.match(mediaUrl('campaign-42.mp4'),/releases\/download\/observer-media\/campaign-42.mp4$/);
  for(const value of ['../secret','https://example.com/a','a?token=x','a/b'])assert.throws(()=>mediaUrl(value));
});
const probe=()=>({streams:[{codec_type:'video',codec_name:'h264',pix_fmt:'yuv420p',width:1920,height:1080,r_frame_rate:'30/1',avg_frame_rate:'30/1',field_order:'progressive',display_aspect_ratio:'16:9',sample_aspect_ratio:'1:1'},
  {codec_type:'audio',codec_name:'aac',sample_rate:'48000',channels:2}],format:{duration:'180.000',size:'5000000'},
  frames:Array.from({length:5400},(_,i)=>({media_type:'video',best_effort_timestamp_time:String(i/30)}))});
test('delivery inspection rejects the wrong codec, truncated capture and unreadable dimensions',()=>{
  assert.equal(verifyProbe(probe(),180).width,1920);
  for(const change of [p=>p.streams[0].codec_name='theora',p=>p.streams[0].pix_fmt='yuv444p',p=>p.streams[0].width=320,p=>p.format.duration='3',p=>p.streams.pop()]){
    const bad=probe();change(bad);assert.throws(()=>verifyProbe(bad,180));
  }
});
const clip=id=>({id,title:'Classical Mediterranean',scenarioId:'classical-mediterranean',seed:'18446744073709551615',sourceCommit:'a'.repeat(40),recordedAt:'2026-11-08T12:00:00Z',durationSeconds:180,excerpt:true,src:mediaUrl(id+'.mp4'),poster:mediaUrl(id+'.jpg'),
  fullCampaign:{completed:true,durationSeconds:3600,youtubeUrl:'https://www.youtube.com/watch?v=AbCdEf12345'}});
const manifest=()=>({schemaVersion:1,generation:'batch-1',generatedAt:'2026-11-08T12:00:00Z',clips:[clip('a'),clip('b')]});
test('native resolution and every decoded timestamp establish actual 1080p30',()=>{
  const source=probe();verifyCaptureProbe(source);assert.equal(verifyFrameTimes(source.frames,180),5400);
  const low=probe();low.streams[0].height=850;assert.throws(()=>verifyCaptureProbe(low));
  const inflated=probe();inflated.streams[0].avg_frame_rate='24/1';assert.throws(()=>verifyProbe(inflated,180));
  const vfr=probe();vfr.frames[2].best_effort_timestamp_time='0.09';assert.throws(()=>verifyFrameTimes(vfr.frames,180));
  assert.throws(()=>verifyFrameTimes(source.frames.slice(1),180));
});
test('Theora duplicate still packets do not falsely reduce logical capture rate',()=>{
  const source=probe();source.streams[0].codec_name='theora';source.streams[0].duration='180.000000';
  source.frames=source.frames.filter((_,i)=>i%3===0);
  verifyNativeCapture(source,'5400 frames at 30 FPS',180);
  assert.throws(()=>verifyNativeCapture(source,'4320 frames at 24 FPS',180));
  source.frames[1].best_effort_timestamp_time='0.11';assert.throws(()=>verifyNativeCapture(source,'5400 frames at 30 FPS',180));
});
test('playlist advances, wraps, and starts a fresh generation when the prior clip disappears',()=>{
  const value=validateManifest(manifest());assert.equal(nextClip(value,'a').id,'b');assert.equal(nextClip(value,'b').id,'a');assert.equal(nextClip(value,'old-generation').id,'a');
});
test('a Pages excerpt requires a completed full-campaign YouTube link',()=>{
  assert.equal(youtubeCampaignUrl('https://www.youtube.com/watch?v=AbCdEf12345'),'https://www.youtube.com/watch?v=AbCdEf12345');
  for(const url of ['https://example.com/watch?v=AbCdEf12345','javascript:alert(1)','https://www.youtube.com/watch?v=short','https://www.youtube.com/watch?v=AbCdEf12345&token=private','https://user:secret@www.youtube.com/watch?v=AbCdEf12345','https://www.youtube.com/watch?v=AbCdEf12345&v=ZyxWvu12345'])assert.throws(()=>youtubeCampaignUrl(url));
  const missing=manifest();delete missing.clips[0].fullCampaign;assert.throws(()=>validateManifest(missing));
  const incomplete=manifest();incomplete.clips[0].fullCampaign.completed=false;assert.throws(()=>validateManifest(incomplete));
});
test('a failed or unsafe manifest cannot replace a working playlist',()=>{
  const current=manifest();validateManifest(current);
  for(const change of [v=>v.clips=[],v=>v.clips[1].id='a',v=>v.clips[0].src='javascript:alert(1)',v=>v.generatedAt='invalid',v=>v.clips[0].sourceCommit='unknown']){
    const bad=structuredClone(current);change(bad);assert.throws(()=>validateManifest(bad));
  }
  assert.equal(current.clips.length,2);
});
