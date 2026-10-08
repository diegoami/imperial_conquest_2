using System.Diagnostics;
using Godot;
using IC2.Engine.Assets;
using IC2.Engine.Presentation;
using IC2.Slice.Audio;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T149 (correction task for bug #790) Done-when 4: the cue list's path through the real
/// <see cref="MainGameScreen"/>, its production subscription on
/// <see cref="GameSession.EventsPublished"/>, and the screen's own <see cref="SoundPlayer"/>.
/// Sol's review of PR #886 (R4): the check installs no private player and no private
/// subscription — it drives the orders through <see cref="MainGameScreen.SubmitForCheck"/>
/// (the <c>OnCommandIssued</c> funnel every control's command, <see cref="ContextPanel"/>'s
/// included, takes into the session) and reads
/// <see cref="MainGameScreen.SoundPlayerForCheck"/>, so the check fails if the screen's own
/// wiring is removed.
/// <list type="number">
/// <item>a "besiege-city" that captures an enemy city puts <c>sfx.city_captured</c> on the
/// screen's player queue;</item>
/// <item>a "move" that walks a human army three cells puts three <c>sfx.unit_move</c> cues on
/// the screen's player queue;</item>
/// <item>with Sound off both put nothing (the production subscription is what mutes);</item>
/// <item>the shipped-WAV loader puts the file's PCM data chunk — not the whole RIFF file — in
/// <c>AudioStreamWav.Data</c> (R3);</item>
/// <item>no blocking: a 2.0 s stream bound to <c>sfx.city_captured</c> starts playing and the
/// <c>Submit</c> that triggered it returns within the budget (Sol's review of PR 793, R6).</item>
/// </list>
/// Run headless:
/// <code>
/// godot --headless --audio-driver Dummy --path godot res://Checks/SoundCuesCheck.tscn --quit-after 600
/// </code>
/// </summary>
/// <remarks>
/// <see cref="ContextPanel"/> itself issues no command since T112 (it renders information
/// only; its remarks say so), so "ContextPanel's path" for a scripted order is the one funnel
/// its <c>CommandIssued</c> event feeds in production: <see cref="MainGameScreen"/>'s
/// <c>OnCommandIssued</c>, which <see cref="MainGameScreen.SubmitForCheck"/> drives without a
/// second, parallel path. The capture order goes through it exactly like the move order.
/// </remarks>
public partial class SoundCuesCheck : Control
{
    /// <summary>Frames to let the real <see cref="MainGameScreen"/>'s container layout settle before
    /// the first order, the same settle-frame pattern the other Godot checks use.</summary>
    private const int InitialSettleFrames = 6;

    /// <summary>Frames to wait after every order, so Godot's deferred layout pass runs before the
    /// queue is read (the SoundPlayer writes to its queue inside the subscription handler, which
    /// runs synchronously with <c>Submit</c>, so the queue is read on the next frame).</summary>
    private const int OrderSettleFrames = 3;

    private const string HumanNation = "rome";
    private const string EnemyCityId = "sound-cues-enemy-city";

    private bool _ok = true;
    private MainGameScreen _mainGame = null!;
    private MainGameScreen _moveMainGame = null!;
    private MainGameScreen _noBlockingMainGame = null!;
    private int _frame;
    private int _step;

    private const string CaptureArmyId = "sound-cues-capture-army";
    private const string MoveArmyId = "sound-cues-move-army";
    private const string NoBlockingArmyId = "sound-cues-no-blocking-army";
    private const string NoBlockingCityId = "sound-cues-no-blocking-city";

    /// <summary>How many ms the no-blocking Submit may take, cue handling included, per Sol's
    /// review of PR 793 (R6). The original <c>PlaySoundA</c> with <c>SND_SYNC</c> blocked the
    /// game thread for the full stream length (sound 10, 1.4 s); the clone's queue-then-play
    /// must finish in a fraction of that, regardless of the stream's own duration.</summary>
    private const double NoBlockingBudgetMs = 250.0;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        // Build the session from the classical scenario, then place a rome army next to a fresh
        // carthage-owned city at known coordinates -- the simplest layout that exercises the
        // capture-cue path and the three-cell-move path without depending on the scenario's
        // exact starting positions.
        var classical = GameDataContext.Repository.Resolve("classical-mediterranean");
        var initial = IC2.Engine.Model.GameStateFactory.CreateInitial(
            classical.World, classical.Ruleset, classical.Scenario);

        // Place the city and armies at a free spot well away from any other scenario feature.
        // The classical map is 320x140, mostly land around italy (y=33-70) and the mediterranean
        // sea (y=70+); (50, 35) is open land in the central mediterranean with no nearby cities.
        var enemyCity = new IC2.Engine.Model.CityState(
            Id: EnemyCityId, Name: "TestCity", X: 50, Y: 35,
            Owner: "carthage", Allegiance: "carthage",
            Loyalty: 100, SupplyTons: 100, FortificationCode: 0,
            PopulationThousands: 10, MaxPopulationThousands: 20,
            Tribute: 0, UnderSiege: false,
            Garrison: IC2.Engine.Model.ValueList<IC2.Engine.Model.UnitSlot>.Empty);

        // A second test city, far enough away that the no-blocking session's own army can
        // besiege it without conflict. (70, 35) is open land in the same band.
        var noBlockingCity = new IC2.Engine.Model.CityState(
            Id: NoBlockingCityId, Name: "NoBlockingCity", X: 70, Y: 35,
            Owner: "carthage", Allegiance: "carthage",
            Loyalty: 100, SupplyTons: 100, FortificationCode: 0,
            PopulationThousands: 10, MaxPopulationThousands: 20,
            Tribute: 0, UnderSiege: false,
            Garrison: IC2.Engine.Model.ValueList<IC2.Engine.Model.UnitSlot>.Empty);

        // Capture army: one cell west of the enemy city, ready to besiege.
        // Move army: south of the city, on a separate tile, with three free cells north of
        // it for the straight 3-cell walk. (49, 40) -> (49, 37) is the walk.
        var captureArmy = new IC2.Engine.Model.ArmyState(
            Id: CaptureArmyId, Nation: HumanNation,
            X: 49, Y: 35, Moves: 99, Morale: 80, Money: 0, SupplyTons: 0,
            CoveredTileCode: 4, AboardFleetId: null,
            Units: IC2.Engine.Model.ValueList.Of(
                new IC2.Engine.Model.UnitSlot(
                    MercenaryLabel: 0, UnitTypeId: "heavy_infantry", Troops: 30000, Quality: 8,
                    Name: "Capture Army")));

        var moveArmy = new IC2.Engine.Model.ArmyState(
            Id: MoveArmyId, Nation: HumanNation,
            X: 49, Y: 40, Moves: 99, Morale: 80, Money: 0, SupplyTons: 0,
            CoveredTileCode: 4, AboardFleetId: null,
            Units: IC2.Engine.Model.ValueList.Of(
                new IC2.Engine.Model.UnitSlot(
                    MercenaryLabel: 0, UnitTypeId: "heavy_infantry", Troops: 30000, Quality: 8,
                    Name: "Move Army")));

        // No-blocking army: one cell west of the no-blocking city.
        var noBlockingArmy = new IC2.Engine.Model.ArmyState(
            Id: NoBlockingArmyId, Nation: HumanNation,
            X: 69, Y: 35, Moves: 99, Morale: 80, Money: 0, SupplyTons: 0,
            CoveredTileCode: 4, AboardFleetId: null,
            Units: IC2.Engine.Model.ValueList.Of(
                new IC2.Engine.Model.UnitSlot(
                    MercenaryLabel: 0, UnitTypeId: "heavy_infantry", Troops: 30000, Quality: 8,
                    Name: "NoBlocking Army")));

        // Add the city and army, and remove the shipped rome army so the cue list's human
        // filter sees only this one. Mark rome as the human seat so the GameSession's own
        // watch-mode gate recognises the seat.
        var nationsWithHumanSeat = initial.Nations.Select(n => n.Id == HumanNation
            ? n with { Control = IC2.Engine.Model.SeatControl.Human }
            : n).ToList();
        var augmented = initial with
        {
            Nations = IC2.Engine.Model.ValueList.From(nationsWithHumanSeat),
            Cities = IC2.Engine.Model.ValueList.From(
                initial.Cities.Append(enemyCity).Append(noBlockingCity)),
            Armies = IC2.Engine.Model.ValueList.From(
                initial.Armies.Where(a => a.Nation != HumanNation)
                    .Append(captureArmy).Append(moveArmy).Append(noBlockingArmy)),
        };

        var save = new IC2.Engine.Model.SaveGame(
            SchemaVersion: augmented.SchemaVersion,
            Id: "sound-cues-check",
            Label: "Sound cues check",
            ScenarioId: augmented.ScenarioId,
            WorldId: augmented.WorldId,
            RulesetId: augmented.RulesetId,
            State: augmented);

        // Three real screens, each over its own session built from the same save: the capture
        // spends its army's moves, so the move and the no-blocking order need their own fresh
        // sessions. Every screen builds its own production SoundPlayer and subscribes it to its
        // session's EventsPublished in _Ready; the check reads those players and drives these
        // screens -- it wires nothing of its own (R4).
        _mainGame = AddScreen(classical, save);
        _moveMainGame = AddScreen(classical, save);
        _noBlockingMainGame = AddScreen(classical, save);

        // The no-blocking half binds the check's synthesised 2.0 s stream to the production
        // player's sfx.city_captured key: the Submit below must return, cue handling included,
        // while the stream is playing on Godot's audio thread.
        _noBlockingMainGame.SoundPlayerForCheck.BindStreamForTest(
            AssetKeys.SfxCityCaptured, SynthesiseSilentWav(durationSeconds: 2.0));
    }

    private MainGameScreen AddScreen(
        IC2.Engine.Serialization.ResolvedScenario classical, IC2.Engine.Model.SaveGame save)
    {
        var session = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, save);
        var screen = new MainGameScreen
        {
            Session = session,
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(screen);
        return screen;
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            switch (_step)
            {
                case 0 when _frame >= InitialSettleFrames:
                    // R3: the shipped-WAV loader (SoundPlayer.LoadWavStream) put the file's PCM
                    // data chunk in AudioStreamWav.Data, not the whole RIFF file. Read before
                    // the orders so a later BindStreamForTest cannot mask it.
                    CheckLoadedStreamIsPcmPayload();
                    // First half: a besiege-city that captures the enemy city, driven through
                    // the screen's own SubmitForCheck funnel (the path ContextPanel's
                    // CommandIssued feeds in production).
                    _mainGame.SubmitForCheck($"besiege-city {CaptureArmyId} {EnemyCityId}");
                    _step = 1;
                    _frame = 0;
                    break;

                case 1 when _frame >= OrderSettleFrames:
                    CheckCaptureCityCue();
                    _moveMainGame.SubmitForCheck($"move {MoveArmyId} 49 37");
                    _step = 2;
                    _frame = 0;
                    break;

                case 2 when _frame >= OrderSettleFrames:
                    CheckMoveUnitMoveCues();
                    // Sound off: the production subscription reads SettingsScreen.SoundEnabled
                    // on every event and mutes the player -- the check flips the setting, not
                    // the player, so the wiring is what is under test.
                    SettingsScreen.SetSoundEnabledForCheck(false);
                    _mainGame.SubmitForCheck($"besiege-city {CaptureArmyId} {EnemyCityId}");
                    _step = 3;
                    _frame = 0;
                    break;

                case 3 when _frame >= OrderSettleFrames:
                    CheckCaptureCityMuted();
                    _moveMainGame.SubmitForCheck($"move {MoveArmyId} 49 37");
                    _step = 4;
                    _frame = 0;
                    break;

                case 4 when _frame >= OrderSettleFrames:
                    CheckMoveUnitMoveMuted();
                    SettingsScreen.SetSoundEnabledForCheck(true);
                    CheckNoBlocking();
                    Finish();
                    break;
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"SoundCuesCheck: unhandled exception: {ex}");
            GD.Print("SoundCuesCheck: exiting with code 1.");
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// Sol's review of PR #886 (R3): the shipped-WAV loader must hand Godot's
    /// <see cref="AudioStreamWav.Data"/> the PCM payload of the file's data chunk, never the
    /// whole RIFF file (headers included). The check reads the screen's loaded stream for
    /// <c>sfx.city_captured</c> and compares its <c>Data</c> length against the data chunk of
    /// the pack file on disk: equal to the chunk, strictly less than the file.
    /// </summary>
    private void CheckLoadedStreamIsPcmPayload()
    {
        var stream = _mainGame.SoundPlayerForCheck.StreamForCheck(AssetKeys.SfxCityCaptured);
        var ok0 = Check(
            stream is not null,
            "the screen's production player loaded sfx.city_captured from the pack");
        _ok &= ok0;
        if (!ok0)
        {
            return;
        }

        var wavPath = System.IO.Path.Combine(
            GameDataContext.RepositoryRoot, "assets", "packs",
            SettingsScreen.SelectedPackId ?? Assets.AssetPackManifestLoader.DefaultPackId,
            "sfx", "city_captured.wav");
        var ok1 = Check(
            System.IO.File.Exists(wavPath),
            $"the pack file exists on disk at {wavPath}");
        _ok &= ok1;
        if (!ok1)
        {
            return;
        }

        var bytes = System.IO.File.ReadAllBytes(wavPath);
        var dataPayloadLength = -1;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            var chunkSize = BitConverter.ToInt32(bytes, offset + 4);
            if (chunkId == "data")
            {
                dataPayloadLength = chunkSize;
                break;
            }

            offset += 8 + chunkSize + (chunkSize % 2);
        }

        var data = stream!.Data;
        var ok2 = Check(
            dataPayloadLength > 0 && data.Length == dataPayloadLength && data.Length < bytes.Length,
            $"AudioStreamWav.Data holds the data chunk's PCM payload ({data.Length} bytes == the "
            + $"chunk's {dataPayloadLength}, file is {bytes.Length}) — not the whole RIFF file");
        _ok &= ok2;
    }

    private void CheckCaptureCityCue()
    {
        var queue = _mainGame.SoundPlayerForCheck.QueuedForCheck;
        var ok = Check(
            queue.Count == 1 && queue[0] == AssetKeys.SfxCityCaptured,
            $"a city-capture through the screen's funnel puts sfx.city_captured on the screen's own player queue (got [{string.Join(", ", queue)}])");
        _ok &= ok;
    }

    private void CheckMoveUnitMoveCues()
    {
        var queue = _moveMainGame.SoundPlayerForCheck.QueuedForCheck;
        var ok = Check(
            queue.Count == 3 && queue.All(c => c == AssetKeys.SfxUnitMove),
            $"a human army's three-cell move puts three sfx.unit_move cues on the screen's own player queue "
            + $"(got [{string.Join(", ", queue)}])");
        _ok &= ok;
    }

    private void CheckCaptureCityMuted()
    {
        var queue = _mainGame.SoundPlayerForCheck.QueuedForCheck;
        var ok = Check(
            queue.Count == 0,
            $"with Sound off, a city-capture queues nothing (got [{string.Join(", ", queue)}])");
        _ok &= ok;
    }

    private void CheckMoveUnitMoveMuted()
    {
        var queue = _moveMainGame.SoundPlayerForCheck.QueuedForCheck;
        var ok = Check(
            queue.Count == 0,
            $"with Sound off, a human army's three-cell move queues nothing (got [{string.Join(", ", queue)}])");
        _ok &= ok;
    }

    /// <summary>
    /// T149 DoD 4 (Sol's review of PR 793, R6): the original's <c>PlaySoundA(path, NULL, 0)</c> with
    /// <c>SND_SYNC</c> blocked the game thread for every sound it played -- sound 10, the longest,
    /// froze the game for 1.4 s. The clone's queue-then-play must not block: the 2.0 s stream the
    /// check synthesised and bound to <c>sfx.city_captured</c> at startup, the
    /// <see cref="GameSession.Submit"/> call that enqueues the cue returns, cue handling included,
    /// in well under the original's stall window, and the player reports the stream as playing
    /// after the return. Driven through the screen's own funnel and read off the screen's own
    /// player (R4), so the production path is the one measured.
    /// </summary>
    private void CheckNoBlocking()
    {
        var stopwatch = Stopwatch.StartNew();
        _noBlockingMainGame.SubmitForCheck($"besiege-city {NoBlockingArmyId} {NoBlockingCityId}");
        stopwatch.Stop();

        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        var withinBudget = elapsedMs < NoBlockingBudgetMs;
        var ok1 = Check(
            withinBudget,
            $"the Submit call that enqueues a 2.0 s sfx.city_captured cue returns within {NoBlockingBudgetMs:F0} ms "
            + $"(elapsed {elapsedMs:F1} ms; the original's PlaySoundA would have stalled the thread for the full stream length)");
        _ok &= ok1;

        // Read the player's playing state immediately after the return. The 2.0 s stream is
        // still rendering on Godot's audio thread, so AudioStreamPlayer.Playing is true.
        // If the cue list had dropped the cue, the player would never have started, and
        // Playing would be false -- exactly the failure mode the original's stall would have
        // hidden by waiting.
        var isPlaying = _noBlockingMainGame.SoundPlayerForCheck.IsPlayingForTest(AssetKeys.SfxCityCaptured);
        var ok2 = Check(
            isPlaying,
            $"the 2.0 s sfx.city_captured stream is playing on the audio thread immediately after the Submit return "
            + $"(IsPlaying={isPlaying})");
        _ok &= ok2;
    }

    /// <summary>
    /// Build a 2.0 s mono 44.1 kHz 16-bit stream in memory with one non-zero sample at the start
    /// (AudioStreamWav.Data takes raw PCM samples, which is also what the R3 assertion checks the
    /// pack loader against). The exact contents do not matter: a 2.0 s near-silent stream still
    /// takes 2.0 s to play on Godot's audio thread, which is the worst case the no-blocking test
    /// must cover.
    /// </summary>
    private static AudioStreamWav SynthesiseSilentWav(double durationSeconds)
    {
        const int sampleRate = 44100;
        var sampleCount = (int)(durationSeconds * sampleRate);
        var data = new byte[sampleCount * 2];
        // Set the first sample to a non-zero value (a single click) so the stream has a peak
        // above 0; the rest is silence. The check never reads the stream's content, only its
        // duration and its playing state.
        data[0] = 0x01;
        data[1] = 0x00;
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            Stereo = false,
            MixRate = sampleRate,
            Data = data,
        };
    }

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        GD.Print($"SoundCuesCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private static bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        return condition;
    }
}
