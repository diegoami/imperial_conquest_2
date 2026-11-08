import {validateManifest,nextClip} from './playlist.mjs';
const $ = id => document.getElementById(id);
const film=$('film'),status=$('status'),loading=$('loading');
let manifest,current,hasPlayed=false,failures=0,refreshWarning='';
const playbackStatus=()=> (manifest?.clips.length===1?'This pilot excerpt repeats automatically. Watch the complete campaign on YouTube.':'Recorded observer footage. The next campaign plays automatically.')+refreshWarning;
const date = value => new Intl.DateTimeFormat(undefined,{year:'numeric',month:'short',day:'numeric'}).format(new Date(value));
async function fetchManifest(){const response=await fetch('./manifest.json',{cache:'no-store'});if(!response.ok)throw Error('The recording playlist could not be loaded.');return validateManifest(await response.json());}
function drawPlaylist(){
  const single=manifest.clips.length===1;
  $('next').textContent=single?'Replay excerpt ↻':'Next campaign →';
  $('collection-title').textContent=single?'Pilot recording':'In rotation';
  $('collection-description').textContent=single
    ?'This is an excerpt from our first complete observer campaign. Watch the full campaign on YouTube, or download the desktop game to command a civilization yourself.'
    :'This continuous playlist features recorded observer campaigns across scenarios. Browser controls play the video; download the desktop game to command a civilization yourself.';
  $('playlist').replaceChildren(...manifest.clips.map(clip=>{
    const button=document.createElement('button');button.className='clip';button.type='button';button.dataset.id=clip.id;
    button.setAttribute('aria-pressed',String(clip.id===current?.id));
    const image=document.createElement('img');image.src=clip.poster;image.alt='';image.loading='lazy';
    const title=document.createElement('span');title.textContent=clip.title;
    const description=document.createElement('small');description.textContent=`${clip.excerpt?'Campaign excerpt':'Full campaign'} · ${date(clip.recordedAt)}`;
    button.append(image,title,description);button.addEventListener('click',()=>{failures=0;load(clip,true);});return button;
  }));
  const old=Date.now()-Date.parse(manifest.generatedAt)>14*86400000;
  $('updated').textContent=`Updated ${date(manifest.generatedAt)}${old?' · refresh delayed':''}`;
}
function load(clip,autoplay){
  current=clip;$('title').textContent=clip.title;$('kind').textContent=clip.excerpt?'· excerpt':'· full campaign';
  $('metadata').textContent=`Recorded ${date(clip.recordedAt)} · Build ${clip.sourceCommit.slice(0,8)} · Seed ${clip.seed}`;
  $('full-campaign').href=clip.fullCampaign.youtubeUrl;$('full-campaign').hidden=false;
  film.poster=clip.poster;film.src=clip.src;film.load();loading.textContent='Loading recording…';loading.hidden=false;
  $('retry').hidden=true;$('next').disabled=false;drawPlaylist();status.textContent=playbackStatus();
  if(autoplay)film.play().catch(error=>{
    if(current?.id!==clip.id||error.name==='AbortError'||film.error)return;
    status.textContent=error.name==='NotAllowedError'?'Press Play in the video controls to start watching.':'Playback could not start. Retry or choose another campaign.';
    $('retry').hidden=error.name==='NotAllowedError';loading.hidden=true;
  });
  else{status.textContent='Press Play to watch. Autoplay is off for your reduced-motion preference.';loading.hidden=true;}
}
film.addEventListener('playing',()=>{hasPlayed=true;failures=0;loading.hidden=true;status.textContent=playbackStatus();});
film.addEventListener('loadeddata',()=>{loading.hidden=true;});
film.addEventListener('waiting',()=>{loading.textContent='Buffering recording…';loading.hidden=false;});
film.addEventListener('pause',()=>{loading.hidden=true;});
film.addEventListener('error',()=>{
  loading.hidden=true;failures++;status.textContent='This recording could not be played. Retry or choose another campaign; the desktop releases are available above.';
  $('retry').hidden=false;
  // Never spin through a broken playlist; choosing another clip is a visible user action.
});
$('retry').addEventListener('click',()=>{if(current)load(current,true);});
$('next').addEventListener('click',()=>{if(manifest&&current){failures=0;load(nextClip(manifest,current.id),true);}});
film.addEventListener('ended',async()=>{
  if(!manifest||!current||failures)return;
  const previous=current.id;
  try{const fresh=await fetchManifest();manifest=fresh;refreshWarning='';}catch{refreshWarning=' Refresh is unavailable; continuing the existing collection.';}
  load(nextClip(manifest,previous),hasPlayed);
});
try{manifest=await fetchManifest();load(manifest.clips[0],!matchMedia('(prefers-reduced-motion: reduce)').matches);}
catch(error){loading.textContent='Recordings are temporarily unavailable.';status.textContent=error.message+' You can still download the desktop game or visit the repository.';}
