import {createHash, randomBytes} from 'node:crypto';
import {readFileSync} from 'node:fs';

export const repository = 'diegoami/imperial_conquest_2';
export const mediaTag = 'observer-media';
export const fps = 30;
export const width = 1920, height = 1080;
const rate = value => {const [n,d]=String(value).split('/').map(Number);return n/d;};
export function verifyCaptureProbe(probe) {
  const video=probe.streams.find(s=>s.codec_type==='video');
  if(!video||video.width!==width||video.height!==height||rate(video.r_frame_rate)!==fps||rate(video.avg_frame_rate)!==fps)
    throw Error('Capture must be native 1920×1080 at 30 fps; upscaling is not accepted');
  return video;
}
export function verifyFrameTimes(frames,seconds) {
  const video=frames.filter(f=>f.media_type==='video');
  if(video.length!==seconds*fps)throw Error('Decoded frame count does not match the capture budget');
  for(let i=0;i<video.length;i++){
    const time=Number(video[i].best_effort_timestamp_time);
    if(!Number.isFinite(time)||Math.abs(time-i/fps)>0.00002)throw Error('Decoded timestamps are not constant 30 fps');
  }
  return video.length;
}
export function verifyNativeCapture(probe,log,seconds) {
  const stream=verifyCaptureProbe(probe);
  // Theora represents repeated still frames with zero-byte duplicate packets;
  // ffprobe's decoded-image count can omit these. Validate the 30 Hz granule
  // duration plus Movie Maker's frame counter, then strictly decode the MP4s.
  // https://www.theora.org/doc/Theora.pdf §7.11; libtheora TH_DUPFRAME.
  if(stream.codec_name!=='theora'||Math.abs(Number(stream.duration)-seconds)>0.00002
      || !log.includes(`${seconds*fps} frames at ${fps} FPS`))throw Error('Native capture frame budget/duration is unproved');
  let previous=-1;
  for(const frame of probe.frames.filter(f=>f.media_type==='video')){
    const time=Number(frame.best_effort_timestamp_time),tick=time*fps;
    if(!Number.isFinite(time)||time<0||time<=previous||time>=seconds||Math.abs(tick-Math.round(tick))>0.0006)
      throw Error('Native decoded images do not lie on the captured 30 Hz timeline');
    previous=time;
  }
  if(previous<0)throw Error('Native capture has no decodable images');
  return stream;
}
export function sha256(file) { return createHash('sha256').update(readFileSync(file)).digest('hex'); }
export function seedValue(value) {
  if (!/^\d+$/.test(String(value))) throw Error('Seed must be an unsigned 64-bit integer');
  const result = BigInt(value);
  if (result > 18446744073709551615n) throw Error('Seed exceeds unsigned 64-bit range');
  return result;
}
export function freshSeed() { return randomBytes(8).readBigUInt64LE(); }
export function productionScenarios(root, files) {
  return files.map(file => JSON.parse(readFileSync(`${root}/data/scenarios/${file}`, 'utf8')))
    .filter(s => s.listed === true || s.id === 'classical-mediterranean')
    .sort((a,b) => a.id.localeCompare(b.id));
}
export function rotation(scenarios, week) {
  const historical = scenarios.filter(s => s.id !== 'classical-mediterranean');
  if (!historical.length || !scenarios.some(s => s.id === 'classical-mediterranean')) throw Error('Need Classical and another production scenario');
  return ['classical-mediterranean', historical[((week % historical.length) + historical.length) % historical.length].id];
}
export function eventsFrom(log, prefix) {
  return log.split(/\r?\n/).filter(line => line.startsWith(prefix + ' ')).map(line => JSON.parse(line.slice(prefix.length + 1)));
}
export function verifyVideoMetadata(probe, seconds) {
  const video = probe.streams.find(s => s.codec_type === 'video');
  const audio = probe.streams.find(s => s.codec_type === 'audio');
  verifyCaptureProbe(probe);
  if (!video || video.codec_name !== 'h264' || video.pix_fmt !== 'yuv420p' || video.field_order !== 'progressive'
      || video.display_aspect_ratio !== '16:9' || video.sample_aspect_ratio !== '1:1')
    throw Error('Expected progressive 1080p30 H.264/yuv420p with square pixels');
  if (!audio || audio.codec_name !== 'aac' || audio.sample_rate !== '48000' || audio.channels !== 2)
    throw Error('Expected stereo 48 kHz AAC game-audio track');
  if (Math.abs(Number(probe.format.duration) - seconds) > 0.15) throw Error('Recording duration does not match frame budget');
  if (Number(probe.format.size) > seconds * 2_000_000) throw Error('Recording exceeds delivery size budget');
  return video;
}
export function verifyProbe(probe,seconds) {
  const video=verifyVideoMetadata(probe,seconds);verifyFrameTimes(probe.frames,seconds);return video;
}
export function mediaUrl(name) {
  if (!/^[a-zA-Z0-9._-]+$/.test(name)) throw Error('Invalid media asset name');
  return `https://github.com/${repository}/releases/download/${mediaTag}/${name}`;
}
