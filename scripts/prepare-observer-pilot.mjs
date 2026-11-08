#!/usr/bin/env node
// Offline pairing only; the receipt must identify the sole accepted Studio upload.
import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {youtubeCampaignUrl,validateManifest} from '../web/observer/playlist.mjs';
import {hashFile} from './observer-encoding.mjs';
const options=Object.fromEntries(process.argv.slice(2).map(arg=>{const m=/^--(pilot|receipt)=(.+)$/.exec(arg);if(!m)throw Error('Use --pilot=<completed-folder> --receipt=<verified-upload.json>');return[m[1],m[2]];}));
if(!options.pilot||!options.receipt)throw Error('Completed pilot and verified receipt required; this script never uploads');
const folder=resolve(options.pilot),pilot=JSON.parse(readFileSync(join(folder,'pilot.json'),'utf8'));
const receipt=JSON.parse(readFileSync(resolve(options.receipt),'utf8')),clip=pilot.clip;
if(pilot.request.uploadAuthorizedCount!==1||clip.fullCampaign?.completed!==true||!clip.fullCampaign.ending)throw Error('Only the single completed pilot may be paired');
if(receipt.channelId!=='UCQHMx6rNOKfLxFRI4_Tf6cA'||receipt.sourceSha256!==clip.uploadMaster.sha256
    ||(receipt.privacy!=='unlisted'&&receipt.privacy!=='public')||receipt.processingStatus!=='succeeded'
    ||receipt.hdVerified!==true||receipt.width!==1920||receipt.height!==1080||receipt.fps!==30
    ||!Number.isFinite(Date.parse(receipt.verifiedAt)))throw Error('Need a matching channel/source/HD-processing receipt');
const url=youtubeCampaignUrl(receipt.youtubeUrl);
if(await hashFile(join(folder,clip.uploadMaster.file))!==receipt.sourceSha256)throw Error('Upload master changed');
const manifestPath=join(folder,'manifest.json');
if(existsSync(manifestPath)){
  const previous=validateManifest(JSON.parse(readFileSync(manifestPath,'utf8')));
  if(previous.clips.length!==1||previous.clips[0].id!==clip.id||previous.clips[0].fullCampaign.youtubeUrl!==url)
    throw Error('A different pilot/video is paired; do not reupload or overwrite its identity');
  console.log('Same pilot already paired; no upload or new video is needed.');
}else{
  clip.fullCampaign.youtubeUrl=url;clip.fullCampaign.processingVerifiedAt=receipt.verifiedAt;
  delete clip.uploadMaster.src;
  const manifest=validateManifest({schemaVersion:1,generation:`pilot-${clip.id}`,generatedAt:new Date().toISOString(),sourceCommit:pilot.sourceCommit,clips:[clip]});
  writeFileSync(join(folder,clip.id+'.json'),JSON.stringify(clip,null,2)+'\n');
  writeFileSync(manifestPath,JSON.stringify(manifest,null,2)+'\n',{flag:'wx'});
  console.log(`Paired one completed campaign: ${url}. No further upload is authorized.`);
}
