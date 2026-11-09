#!/usr/bin/env node
// Uses ai-player's installed Playwright when --playwright-root points at that project.
import {createRequire} from 'node:module';
import {resolve,join} from 'node:path';
import {mkdirSync,readFileSync} from 'node:fs';
import assert from 'node:assert/strict';
const options=Object.fromEntries(process.argv.slice(2).map(arg=>{const m=/^--([a-z-]+)=(.+)$/.exec(arg);if(!m)throw Error('Use --url=... --playwright-root=... --out=...');return[m[1],m[2]];}));
if(!options.url)throw Error('A running static preview URL is required');
const require=createRequire(join(resolve(options['playwright-root']??'.'),'package.json'));
const {chromium,firefox}=require('playwright');
const fixture=options.fixture?JSON.parse(readFileSync(resolve(options.fixture),'utf8')):null;
async function fixtureRoutes(context){
  if(!fixture)return;
  await context.route('**/manifest.json',route=>route.fulfill({json:fixture.manifest}));
  await context.route(fixture.manifest.clips[0].src,route=>{
    const bytes=readFileSync(resolve(fixture.video)),range=/^bytes=(\d+)-(\d*)$/.exec(route.request().headers().range??'');
    if(!range)return route.fulfill({contentType:'video/mp4',headers:{'accept-ranges':'bytes'},body:bytes});
    const start=Number(range[1]),end=Math.min(bytes.length-1,range[2]?Number(range[2]):bytes.length-1);
    if(start>end)return route.fulfill({status:416,headers:{'content-range':`bytes */${bytes.length}`}});
    return route.fulfill({status:206,contentType:'video/mp4',headers:{'accept-ranges':'bytes','content-range':`bytes ${start}-${end}/${bytes.length}`},body:bytes.subarray(start,end+1)});
  });
  await context.route(fixture.manifest.clips[0].poster,route=>route.fulfill({contentType:'image/jpeg',body:readFileSync(resolve(fixture.poster))}));
  await context.addInitScript(()=>document.addEventListener('DOMContentLoaded',()=>{
    const note=document.createElement('aside');note.textContent='LOCAL PLAYER QA FIXTURE — not a published full campaign';
    note.style.cssText='padding:12px;text-align:center;background:#603800;color:white';document.body.prepend(note);
  }));
}
const output=resolve(options.out??'rendered/observer-web');mkdirSync(output,{recursive:true});
for(const [name,type] of [['chromium',chromium],['firefox',firefox]]){
  const browser=await type.launch({headless:true});
  try{
    const context=await browser.newContext({viewport:{width:1440,height:1100}});
    await fixtureRoutes(context);
    const page=await context.newPage();
    page.on('pageerror',error=>console.error(`${name} player error: ${error.message}`));
    await page.goto(options.url);await page.waitForFunction(()=>document.querySelector('video').readyState>=2,null,{timeout:60000});
    assert.equal(await page.locator('nav a').count(),2);
    assert.match(await page.locator('nav .primary').getAttribute('href'),/\/releases$/);
    const clipCount=await page.locator('.clip').count();assert.ok(clipCount>=1);
    assert.match(await page.locator('#full-campaign').getAttribute('href'),/^https:\/\/www\.youtube\.com\/watch\?v=/);
    const original=await page.locator('#title').textContent();
    await page.locator('video').evaluate(v=>v.pause());
    const time=await page.locator('video').evaluate(v=>v.currentTime);await page.waitForTimeout(400);
    assert.ok(Math.abs(await page.locator('video').evaluate(v=>v.currentTime)-time)<0.1,'pause holds video');
    await page.locator('video').evaluate(async v=>{v.currentTime=Math.min(2,v.duration-0.5);await v.play();});
    await page.waitForFunction(()=>document.querySelector('video').currentTime>=1.5);
    await page.locator('video').evaluate(v=>{window.observerTestLoads=0;v.addEventListener('loadstart',()=>window.observerTestLoads++);});
    await page.locator('#next').click();await page.waitForFunction(()=>window.observerTestLoads>=1);
    if(clipCount>1)await page.waitForFunction(title=>document.querySelector('#title').textContent!==title,original);
    await page.waitForFunction(()=>document.querySelector('video').readyState>=2,null,{timeout:60000});
    await page.locator('video').evaluate(async v=>{v.currentTime=v.duration-0.3;await v.play();});
    await page.waitForFunction(()=>window.observerTestLoads>=2,null,{timeout:30000}).catch(async error=>{
      console.error(await page.locator('video').evaluate(v=>({time:v.currentTime,duration:v.duration,ended:v.ended,paused:v.paused,error:v.error?.message,loads:window.observerTestLoads,status:document.querySelector('#status').textContent})));
      throw error;
    });
    if(clipCount<=2)await page.waitForFunction(title=>document.querySelector('#title').textContent===title,original,{timeout:30000});
    await page.locator('video').evaluate(v=>v.pause());
    await page.screenshot({path:join(output,`${name}-desktop.png`),fullPage:true});
    await page.setViewportSize({width:390,height:844});
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'narrow layout does not overflow');
    await page.screenshot({path:join(output,`${name}-mobile.png`),fullPage:true});
    await context.close();
    const reduced=await browser.newContext({reducedMotion:'reduce'});await fixtureRoutes(reduced);const quiet=await reduced.newPage();await quiet.goto(options.url);
    await quiet.waitForFunction(()=>document.querySelector('#title').textContent!=='Preparing the collection');
    assert.equal(await quiet.locator('video').evaluate(v=>v.paused),true,'reduced motion does not force autoplay');await reduced.close();
    const broken=await browser.newPage();await broken.route('**/manifest.json',route=>route.fulfill({json:{schemaVersion:1,generation:'bad',generatedAt:new Date().toISOString(),clips:[]}}));
    await broken.goto(options.url);await broken.waitForFunction(()=>document.querySelector('#status').textContent.includes('unavailable'));
    assert.equal(await broken.locator('nav a').count(),2,'download/repository remain reachable on playlist failure');await broken.close();
    console.log(`PASS ${name}: ${fixture?'local captured MP4 fixture':'public MP4'} load, pause, seek, next, automatic wrap, narrow viewport, reduced motion and failed playlist.`);
  }finally{await browser.close();}
}
