export function validateManifest(value) {
  if (value?.schemaVersion !== 1 || typeof value.generation !== 'string' || !Number.isFinite(Date.parse(value.generatedAt))
      || !Array.isArray(value.clips) || value.clips.length < 1 || value.clips.length > 100) throw Error('Recording playlist is unavailable or invalid.');
  const ids = new Set();
  for (const clip of value.clips) {
    if (typeof clip.id !== 'string' || !/^[a-zA-Z0-9._-]{1,240}$/.test(clip.id) || ids.has(clip.id) || typeof clip.title !== 'string'
        || !Number.isFinite(Date.parse(clip.recordedAt)) || !/^[a-f0-9]{40}$/.test(clip.sourceCommit)
        || !/^\d+$/.test(clip.seed) || !(clip.durationSeconds > 0) || typeof clip.excerpt !== 'boolean')
      throw Error('Recording metadata is incomplete.');
    for (const url of [clip.src,clip.poster]) {
      const parsed = new URL(url);
      if (parsed.protocol !== 'https:' || parsed.username || parsed.password) throw Error('Recording URLs must use HTTPS.');
    }
    ids.add(clip.id);
  }
  return value;
}
export function nextClip(manifest, id) {
  const index = manifest.clips.findIndex(clip => clip.id === id);
  return manifest.clips[(index+1)%manifest.clips.length];
}
