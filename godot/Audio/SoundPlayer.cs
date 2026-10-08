using Godot;
using IC2.Engine.Assets;
using IC2.Slice.Assets;

namespace IC2.Slice.Audio;

/// <summary>
/// T149: a small Godot audio player for the ten <c>sfx.*</c> keys. Loads the chosen asset pack's
/// WAVs once on construction and plays them asynchronously through Godot's
/// <see cref="AudioStreamPlayer"/>s — one player per key, each independent of the others, none of
/// them blocking the game thread. The original's <c>PlaySoundA(path, NULL, 0)</c> with
/// <c>SND_SYNC</c> stalled the engine for every sound it played (the longest, sound 10, froze the
/// game for 1.4 s, and an army walk played sound 1 once per tile). The clone's sounds therefore
/// queue and play on Godot's own audio thread, and the cue list's cap of 12 step cues per order
/// keeps one Submit's own step queue from running forever.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One player per key.</strong> Godot's <see cref="AudioStreamPlayer.Play"/> restarts a
/// stream from the beginning, so a cue for the same key while the player is already playing would
/// restart it. The clone's cue list deduplicates the repeat by only enqueueing one cue per step
/// (it raises one cue per tile the walker crosses, and the player runs the next step's cue only
/// after the previous one finished). For the case where a same-key cue arrives while the previous
/// is still playing — a fleet battle and a fleet scuttle in the same Submit, for example — the
/// player drops the second cue. The original drops it too (it never restarts a sound mid-play).
/// </para>
/// <para>
/// <strong>Steps queue at 70 ms apart.</strong> The original plays the step sound once per tile as
/// the walker crosses it, and the walker does so at the game's own pace — the time between
/// consecutive tile entries, in the original, is whatever the engine spends on a step. A
/// <c>70 ms</c> gap is the cue list's reading of that pace: long enough to be heard as two
/// distinct clicks on the unit_move key, short enough that a 12-step walk reaches the end in
/// under a second, well under the 1.4 s the original's own sound 10 took. <c>[designed]</c>: the
/// report does not time the original's own step gap; the 70 ms here is the gap the
/// <c>sfx.unit_move</c> cue (0.05 s) ends and the next begins, with a 20 ms breath.
/// </para>
/// <para>
/// <strong>Missing packs.</strong> A key the pack lacks is silent — never a Godot default beep, never
/// a fallback to the placeholder. The constructor logs the missing key through Godot's
/// <c>GD.PushWarning</c>, the same channel <see cref="AssetPackTextureLoader"/> already uses.
/// </para>
/// </remarks>
public sealed partial class SoundPlayer : Node
{
    /// <summary>The wall-clock gap between consecutive step cues (sfx.unit_move, sfx.fleet_move).</summary>
    public const double StepGapSeconds = 0.07;

    private readonly Dictionary<string, AudioStreamPlayer> _players = new(StringComparer.Ordinal);
    private readonly Queue<QueuedCue> _stepQueue = new();
    private readonly HashSet<string> _currentlyPlaying = new(StringComparer.Ordinal);
    private readonly List<string> _queuedForCheck = new();
    private readonly Node _stepTimerParent;
    private SceneTreeTimer? _stepTimer;
    private bool _muted;

    /// <summary>Whether sound is on. Mirrors <see cref="SettingsScreen.SoundEnabled"/>.</summary>
    public bool Muted
    {
        get => _muted;
        set => _muted = value;
    }

    /// <summary>
    /// T149 DoD 4 (Sol's review of PR 793, R6): the headless <c>--audio-driver Dummy</c> check
    /// synthesises a 2.0 s stream and binds it to <c>sfx.city_captured</c> through this method,
    /// then times the <see cref="GameSession.Submit"/> call's wall clock to assert the
    /// player's no-blocking property. The production player never calls this; the test does.
    /// A key the player does not know about (no <see cref="AudioStreamPlayer"/> for it) silently
    /// no-ops, exactly as <see cref="Play"/> does.
    /// </summary>
    public void BindStreamForTest(string key, AudioStreamWav stream)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(stream);

        if (_players.TryGetValue(key, out var player))
        {
            player.Stream = stream;
        }
        else
        {
            var newPlayer = new AudioStreamPlayer { Name = key, Stream = stream, Bus = "Master" };
            AddChild(newPlayer);
            _players[key] = newPlayer;
        }
    }

    /// <summary>
    /// T149 DoD 4 (Sol's review of PR 793, R6): <see langword="true"/> when the
    /// <see cref="AudioStreamPlayer"/> for <paramref name="key"/> exists and Godot's audio
    /// thread currently reports it as playing. The check reads this after its <c>Submit</c>
    /// returns to confirm a 2.0 s stream the cue list just enqueued really did start playing,
    /// rather than being dropped or scheduled on a future frame.
    /// </summary>
    public bool IsPlayingForTest(string key)
    {
        if (!_players.TryGetValue(key, out var player))
        {
            return false;
        }

        // Godot's AudioStreamPlayer.Playing is true while a stream is rendering on the audio
        // thread. A still-finished stream flips it back to false; the check asserts true here
        // because the 2.0 s stream is still rendering at the moment the read happens.
        var playingProperty = player.GetType().GetProperty("Playing");
        return playingProperty?.GetValue(player) as bool? ?? false;
    }

    /// <summary>
    /// T149 DoD 4: every cue <see cref="Play"/> has enqueued since the last call, in enqueue
    /// order. Aimed at the headless Godot check <c>godot/Checks/SoundCuesCheck.cs</c>, which
    /// needs a way to read "what would have been played" without depending on Godot's audio
    /// output (the headless audio driver is <c>Dummy</c>). Drains as it reads, so a second
    /// call after a quiet command returns an empty list. Production callers should not touch
    /// this; the player plays the cues regardless of the queue, so reading the queue is a
    /// pure assertion tool.
    /// </summary>
    public IReadOnlyList<string> QueuedForCheck
    {
        get
        {
            lock (_queuedForCheck)
            {
                var snapshot = _queuedForCheck.ToArray();
                _queuedForCheck.Clear();
                return snapshot;
            }
        }
    }

    /// <summary>
    /// Creates a player over the given pack. The pack directory is read directly (it is the same
    /// <c>assets/packs/{authored,placeholder}</c> directory <see cref="GameMapView"/>'s own
    /// <see cref="AssetPackTextureLoader"/> reads from). Loads every <c>sfx.*</c> key the manifest
    /// resolves; missing keys are warned once and left silent, never substituted.
    /// </summary>
    public SoundPlayer(string packDirectory)
    {
        ArgumentNullException.ThrowIfNull(packDirectory);

        // Step cues (sfx.unit_move, sfx.fleet_move) queue 70 ms apart; the player keeps a small
        // parent node only so the timer can be cancelled cleanly when the player is freed. The
        // Godot scene tree owns the timer; we own nothing of its lifetime beyond AddChild.
        _stepTimerParent = new Node { Name = "SoundPlayerStepTimer" };
        AddChild(_stepTimerParent);

        LoadPack(packDirectory);
    }

    private void LoadPack(string packDirectory)
    {
        var manifestPath = Path.Combine(packDirectory, "manifest.json");
        var pack = AssetPackManifestLoader.TryLoad(manifestPath, out var failure);
        if (pack is null)
        {
            GD.PushWarning(failure is null
                ? $"T149 SoundPlayer: no manifest at '{manifestPath}'; every cue is silent."
                : $"T149 SoundPlayer: {failure}; every cue is silent.");
            return;
        }

        var resolver = new AssetKeyResolver(
            pack, packDirectory, key => GD.PushWarning($"T149 SoundPlayer: pack cannot resolve '{key}'; that cue is silent."));

        foreach (var key in AssetKeys.AllKeys)
        {
            if (!key.StartsWith("sfx.", StringComparison.Ordinal))
            {
                continue;
            }

            if (!resolver.TryResolveFile(key, out var fullPath))
            {
                continue;
            }

            var stream = LoadWavStream(fullPath);
            if (stream is null)
            {
                GD.PushWarning($"T149 SoundPlayer: '{fullPath}' is not a loadable WAV; silent.");
                continue;
            }

            var player = new AudioStreamPlayer { Name = key, Stream = stream, Bus = "Master" };
            AddChild(player);
            _players[key] = player;
        }
    }

    private static AudioStreamWav? LoadWavStream(string fullPath)
    {
        var bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length < 44)
        {
            return null;
        }

        if (System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF"
            || System.Text.Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
        {
            return null;
        }

        var fmtIndex = IndexOf(bytes, "fmt "u8.ToArray());
        if (fmtIndex < 0)
        {
            return null;
        }

        // 16-bit PCM: AudioStreamWav.Format.Format16Bits. mono + 44.1 kHz, as
        // docs/asset-specification.md §1.4 requires — no other format is shipped, and any deviation
        // is the generator's bug, surfaced through AuthoredPackConformanceTests.
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            Stereo = false,
            MixRate = 44100,
            Data = bytes,
        };
        return stream;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Plays <paramref name="cues"/> in order. Step cues (sfx.unit_move, sfx.fleet_move) queue
    /// 70 ms apart so consecutive steps are heard as distinct ticks; every other cue plays as
    /// soon as its own player is free. A key whose player has no loaded stream is silently
    /// skipped, never substituted.
    /// </summary>
    public void Play(IReadOnlyList<string> cues)
    {
        if (_muted || cues is null || cues.Count == 0)
        {
            return;
        }

        foreach (var key in cues)
        {
            EnqueueCue(key);
        }
    }

    private void EnqueueCue(string key)
    {
        if (!_players.TryGetValue(key, out var player))
        {
            return;
        }

        if (IsStepKey(key))
        {
            // A step cue is part of a run: it always queues, never restarts a playing step. The
            // timer fires one cue at a time so consecutive steps are 70 ms apart.
            lock (_queuedForCheck)
            {
                _queuedForCheck.Add(key);
            }
            _stepQueue.Enqueue(new QueuedCue(key, player));
            EnsureStepTimer();
            return;
        }

        // A one-shot cue (everything except the two step keys). If a previous same-key cue is
        // still playing, drop the new one -- the original's PlaySoundA never restarted a sound
        // mid-play, so this matches its behaviour rather than overlap-stacking.
        if (_currentlyPlaying.Contains(key))
        {
            return;
        }

        lock (_queuedForCheck)
        {
            _queuedForCheck.Add(key);
        }
        _currentlyPlaying.Add(key);
        player.Play();
        // A one-shot cue plays out on Godot's own audio thread; we approximate the end with the
        // stream's own length so a second same-key cue that arrives before the first finished
        // is dropped, exactly as the original would. A still-playing player reports
        // !_players[key].Playing == false (it is playing), so the check above handles that.
        // The SetPlayingFalse timeout is therefore unnecessary; a finished cue simply leaves
        // the set and the next arrival plays.
        var endTimer = GetTree().CreateTimer(player.Stream?.GetLength() ?? 0.0);
        endTimer.Timeout += () => _currentlyPlaying.Remove(key);
    }

    private void EnsureStepTimer()
    {
        if (_stepTimer is { TimeLeft: > 0 })
        {
            return;
        }

        _stepTimer = GetTree().CreateTimer(StepGapSeconds, processInPhysics: false, processAlways: false);
        _stepTimer.Timeout += StepTick;
    }

    private void StepTick()
    {
        if (_muted || _stepQueue.Count == 0)
        {
            return;
        }

        var next = _stepQueue.Dequeue();
        next.Player.Play();
        if (_stepQueue.Count > 0)
        {
            EnsureStepTimer();
        }
    }

    private static bool IsStepKey(string key) =>
        key == AssetKeys.SfxUnitMove || key == AssetKeys.SfxFleetMove;

    private readonly record struct QueuedCue(string Key, AudioStreamPlayer Player);
}
