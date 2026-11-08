#!/usr/bin/env node
import {spawnSync} from 'node:child_process';
import {existsSync,readFileSync,mkdirSync,writeFileSync,copyFileSync} from 'node:fs';
import {resolve,dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {repository,mediaTag,sha256,mediaUrl} from './observer-media.mjs';
import {validateManifest} from '../web/observer/playlist.mjs';
const options=Object.fromEntries(process.argv.slice(2).map(arg=>{const m=/^--(batch|site)=(.+)$/.exec(arg);if(!m)throw Error('Use --batch=<validated-batch> --site=<new-static-output>');return[m[1],m[2]];}));
if(!options.batch||!options.site)throw Error('Both --batch and --site are required');
const batch=resolve(options.batch),site=resolve(options.site),root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
if(existsSync(site))throw Error('Static output must be a new directory');
const manifest=validateManifest(JSON.parse(readFileSync(join(batch,'manifest.json'),'utf8')));
if(!/^[a-zA-Z0-9_-]+$/.test(manifest.generation)||!/^[a-f0-9]{40}$/.test(manifest.sourceCommit))throw Error('Invalid generation/source identity');
const assets=[];
for(const clip of manifest.clips){
  if(clip.src!==mediaUrl(clip.videoFile)||clip.poster!==mediaUrl(clip.posterFile))throw Error('Media URL differs from the validated release asset');
  if(sha256(join(batch,clip.videoFile))!==clip.sha256||sha256(join(batch,clip.posterFile))!==clip.posterSha256)throw Error('Media checksum differs from validated capture');
  if(clip.sourceCommit!==manifest.sourceCommit||!clip.routes?.length||clip.campaigns?.some(c=>c.pack!=='authored'))throw Error('Capture provenance is incomplete');
  if(!clip.uploadMaster||clip.uploadMaster.src!==mediaUrl(clip.uploadMaster.file)||sha256(join(batch,clip.uploadMaster.file))!==clip.uploadMaster.sha256)
    throw Error('YouTube upload master is missing or differs from its verified hash');
  assets.push(join(batch,clip.videoFile),join(batch,clip.posterFile),join(batch,clip.id+'.json'));
  assets.push(join(batch,clip.uploadMaster.file));
}
const archiveName=`manifest-${manifest.generation}.json`;
copyFileSync(join(batch,'manifest.json'),join(batch,archiveName));assets.push(join(batch,archiveName));
function gh(args){const result=spawnSync('gh',args,{cwd:root,encoding:'utf8',windowsHide:true,maxBuffer:4*1024*1024});if(result.error||result.status!==0)throw Error(result.error?.message??result.stderr);return result.stdout;}
const existing=spawnSync('gh',['api',`repos/${repository}/releases/tags/${mediaTag}`],{encoding:'utf8',windowsHide:true});
if(existing.status!==0){
  if(!existing.stderr?.includes('HTTP 404'))throw Error('Could not determine media-release state: '+existing.stderr);
  gh(['release','create',mediaTag,'--repo',repository,'--target',manifest.sourceCommit,'--prerelease','--latest=false',
    '--title','Observer recordings · development footage','--notes','Regularly refreshed, authored-only recordings from Open Imperial Conquest. These are recorded excerpts, not a playable browser build or a versioned game release. Each asset has scenario/seed/build/date/checksum provenance.']);
}
// Unique names, no --clobber and no deletion of previous generations. Partial upload never deploys.
gh(['release','upload',mediaTag,'--repo',repository,...assets]);
for(const clip of manifest.clips){
  const response=await fetch(clip.src,{headers:{Range:'bytes=0-1023'},redirect:'follow'});
  if(!response.ok)throw Error(`Public media unavailable: HTTP ${response.status}`);
  const bytes=new Uint8Array(await response.arrayBuffer());
  const local=readFileSync(join(batch,clip.videoFile));
  if(bytes.length<32||!Buffer.from(bytes.slice(0,Math.min(1024,bytes.length))).equals(local.subarray(0,Math.min(1024,bytes.length))))throw Error('Public media content differs from validated MP4');
  if(response.status!==206)console.warn('Media host ignored Range; actual browser seek must be verified before acceptance.');
  const poster=await fetch(clip.poster,{redirect:'follow'});
  if(!poster.ok)throw Error(`Public poster unavailable: HTTP ${poster.status}`);
}
mkdirSync(site,{recursive:true});
for(const file of ['index.html','style.css','playlist.mjs','player.mjs'])copyFileSync(join(root,'web/observer',file),join(site,file));
writeFileSync(join(site,'manifest.json'),JSON.stringify(manifest,null,2)+'\n');
writeFileSync(join(site,'.nojekyll'),'');
console.log(`Public media verified. Static Pages payload: ${site}`);
