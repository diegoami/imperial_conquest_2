#!/usr/bin/env node
import {readFileSync,writeFileSync,copyFileSync,mkdirSync,existsSync} from 'node:fs';
import {resolve,dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {validateManifest} from '../web/observer/playlist.mjs';
import {mediaUrl} from './observer-media.mjs';
const options=Object.fromEntries(process.argv.slice(2).map(arg=>{const m=/^--(manifest|out)=(.+)$/.exec(arg);if(!m)throw Error('Use --manifest=<validated-release-manifest> --out=<new-static-directory>');return[m[1],m[2]];}));
if(!options.manifest||!options.out)throw Error('Manifest and new output are required');
const manifest=validateManifest(JSON.parse(readFileSync(resolve(options.manifest),'utf8'))),output=resolve(options.out);
if(manifest.clips.length!==1)throw Error('Only one pilot is authorized before owner review');
for(const clip of manifest.clips)if(clip.src!==mediaUrl(clip.videoFile)||clip.poster!==mediaUrl(clip.posterFile)
    ||!Number.isFinite(Date.parse(clip.fullCampaign.processingVerifiedAt)))throw Error('Need the published media paths and verified YouTube processing');
if(existsSync(output))throw Error('Do not overwrite an existing static payload');
const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');mkdirSync(output,{recursive:true});
for(const file of ['index.html','style.css','playlist.mjs','player.mjs'])copyFileSync(join(root,'web/observer',file),join(output,file));
writeFileSync(join(output,'manifest.json'),JSON.stringify(manifest,null,2)+'\n');writeFileSync(join(output,'.nojekyll'),'');
console.log(`Assembled one paired pilot: ${output}`);
