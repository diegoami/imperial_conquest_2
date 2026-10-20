# Environment

What this machine provides to the build, beyond the toolchain that [operating-guide.md](operating-guide.md) describes.

## ElevenLabs (sound generation)

`scripts/generate-sounds.ps1` ([T149](https://github.com/diegoami/imperial_conquest_2/blob/local/docs/tasks/T149.md)) generates the authored pack's `sfx.*` sounds through
ElevenLabs' sound-generation endpoint, with the key in the user environment variable `ELEVENLABS_API_KEY`. It is
read like `OPENROUTER_API_KEY`: the process environment first, then the Windows user scope. It is never printed,
logged, written to a file or passed on a command line.

- **A real run spends the owner's credit**: `-Key <sfx key>` or `-All`. The default is a dry run that prints the
  prompts and makes no request. `-PostprocessOnly <key>` re-trims and re-normalises one key offline.
- **Show the no-key stop with `-NoKeyCheck`**, never by clearing the variable: the user-scope fallback finds
  the key, and a "no key" run made real, billable calls twice during T149 (the wiki's Agent-failures page).
  `-NoKeyCheck` stubs both key sources, refuses every network call, and spends nothing. `-SelfCheck` tests the
  conversion offline.

> Removed in the cloud cleanup (2026-10-20), kept on the `local` branch: the model quota tracker section.
