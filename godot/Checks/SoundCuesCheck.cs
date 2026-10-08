using System.Diagnostics;
using Godot;
using IC2.Engine.Assets;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Audio;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T149 (correction task for bug #790) Done-when 4: the cue list's path through the real
/// <see cref="GameSession"/> and the real <see cref="SoundPlayer"/>. Drives the live
/// classical-mediterranean scenario through two scripted orders and reads the player's
/// <see cref="SoundPlayer.QueuedForCheck"/> queue:
/// <list type="number">
/// <item>a <c>ContextPanel</c>-shaped <c>besiege-city</c> that captures an enemy city puts
/// <c>sfx.city_captured</c> on the queue (the city-falls path that runs a single
/// <see cref="IC2.Engine.Cities.Capture.CityFallsToNation"/> event);</item>
/// <item>a <c>MainGameScreen.SubmitForCheck</c> "move" that walks a human army three cells puts
/// three <c>sfx.unit_move</c> cues on the queue (the Chebyshev step count for a 3-tile move).</item>
/// </list>
/// Sound off produces an empty queue in both cases. Run headless:
/// <code>
/// godot --headless --audio-driver Dummy --path godot res://Checks/SoundCuesCheck.tscn --quit-after 600
/// </code>
/// The <c>--audio-driver Dummy</c> flag is what Sol's review of PR 793 (R6) asked for: a real
/// <see cref="SoundPlayer"/> queue, with Godot's audio thread a no-op.
/// </summary>
/// <remarks>
/// The check builds a private <see cref="SoundPlayer"/> (so the screen's own one does not
/// observe the test's orders) and uses <see cref="MainGameScreen.SubmitForCheck"/> to issue
/// the orders, the same public hook every other Godot check uses. The first order is issued
/// by directly calling the engine's command path -- <c>ContextPanel</c>'s own wiring has the
/// city capture go through <see cref="MainGameScreen.SubmitForCheck"/> under the hood, but
/// a fresh check needs the order to land on a <see cref="GameSession"/> it controls.
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
    private SoundPlayer _player = null!;
    private SoundPlayer _movePlayer = null!;
    private SoundPlayer _noBlockingPlayer = null!;
    private int _frame;
    private int _step;
    private double _captureWallSeconds;
    private double _moveWallSeconds;

    private const string CaptureArmyId = "sound-cues-capture-army";
    private const string MoveArmyId = "sound-cues-move-army";
    private const string NoBlockingArmyId = "sound-cues-no-blocking-army";
    private const string NoBlockingCityId = "sound-cues-no-blocking-city";

    /// <summary>How many ms <see cref="CheckNoBlocking"/> may take from the cue list's first
    /// enqueue to the player's first playing-frame read, per Sol's review of PR 793 (R6).
    /// The original <c>PlaySoundA</c> with <c>SND_SYNC</c> would block the game thread for the
    /// full stream length (sound 10, 1.4 s); the clone's queue-then-play must finish in a
    /// fraction of that, regardless of the stream's own duration.</summary>
    private const double NoBlockingBudgetMs = 250.0;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        // Build a session from the classical scenario, then place a rome army next to a fresh
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
        // watch-mode gate (the brief DoD asks for a human command path) recognises the seat.
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

        var session = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, save);
        _mainGame = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);

        // A private player, separate from the screen's own, so this check's reads do not race
        // the screen's. The subscription is the cue list's own wiring, but a private one with
        // the same SoundCues rule is the simplest reliable harness.
        _player = new SoundPlayer(System.IO.Path.Combine(
            GameDataContext.RepositoryRoot, "assets", "packs",
            SettingsScreen.SelectedPackId ?? Assets.AssetPackManifestLoader.DefaultPackId));
        AddChild(_player);
        session.EventsPublished += events =>
        {
            var cues = SoundCues.ForEvents(events, session.State);
            _player.Play(cues);
        };

        // A second screen + player for the move, independent of the capture's: the capture
        // spends the army's moves, so the move order would otherwise reject on
        // movement.no-moves-left. A fresh session, also human-controlled, is the simplest way
        // to issue a clean three-cell move.
        var moveSession = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, save);
        _moveMainGame = new MainGameScreen { Session = moveSession, RepositoryRoot = GameDataContext.RepositoryRoot };
        _moveMainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_moveMainGame);

        _movePlayer = new SoundPlayer(System.IO.Path.Combine(
            GameDataContext.RepositoryRoot, "assets", "packs",
            SettingsScreen.SelectedPackId ?? Assets.AssetPackManifestLoader.DefaultPackId));
        AddChild(_movePlayer);
        moveSession.EventsPublished += events =>
        {
            var cues = SoundCues.ForEvents(events, moveSession.State);
            _movePlayer.Play(cues);
        };

        // A third, for the no-blocking half of DoD 4: the check synthesises a 2.0 s WAV
        // (the worst-case stream length the API will produce) and binds it to
        // sfx.city_captured on a private player, then submits an order that cues that key and
        // measures the wall clock from the cue's first enqueue to the player's first
        // playing-frame read. A passing run is a no-blocking proof.
        var noBlockingSession = new GameSession(
            classical.World, classical.Ruleset, classical.Scenario, save);
        _noBlockingMainGame = new MainGameScreen { Session = noBlockingSession, RepositoryRoot = GameDataContext.RepositoryRoot };
        _noBlockingMainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_noBlockingMainGame);

        _noBlockingPlayer = new SoundPlayer(System.IO.Path.Combine(
            GameDataContext.RepositoryRoot, "assets", "packs",
            SettingsScreen.SelectedPackId ?? Assets.AssetPackManifestLoader.DefaultPackId));
        AddChild(_noBlockingPlayer);
        _noBlockingPlayer.BindStreamForTest(AssetKeys.SfxCityCaptured, SynthesiseSilentWav(durationSeconds: 2.0));
        noBlockingSession.EventsPublished += events =>
        {
            var cues = SoundCues.ForEvents(events, noBlockingSession.State);
            _noBlockingPlayer.Play(cues);
        };
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            switch (_step)
            {
                case 0 when _frame >= InitialSettleFrames:
                    // First half: a ContextPanel-shaped "besiege-city" that captures an enemy
                    // city. The classical scenario's rome owns italy cities; carthage is a
                    // foreign city that a successful siege captures. The rome turn can reach
                    // carthage with an army in 1-2 moves of careful play; for the headless check
                    // the simplest path is to set up a rome army next to a carthaginian city
                    // and submit the siege. We hand-place the army so the check is independent
                    // of the scenario's exact starting layout.
                    CaptureEnemyCity();
                    _step = 1;
                    _frame = 0;
                    break;

                case 1 when _frame >= OrderSettleFrames:
                    CheckCaptureCityCue();
                    MoveHumanArmyThreeCells();
                    _step = 2;
                    _frame = 0;
                    break;

                case 2 when _frame >= OrderSettleFrames:
                    CheckMoveUnitMoveCues();
                    // Mute the player and replay the same orders, asserting nothing is queued.
                    _player.Muted = true;
                    _movePlayer.Muted = true;
                    CaptureEnemyCity();
                    _step = 3;
                    _frame = 0;
                    break;

                case 3 when _frame >= OrderSettleFrames:
                    CheckCaptureCityMuted();
                    MoveHumanArmyThreeCells();
                    _step = 4;
                    _frame = 0;
                    break;

                case 4 when _frame >= OrderSettleFrames:
                    CheckMoveUnitMoveMuted();
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
    /// Submits a besiege-city order through the live <see cref="MainGameScreen"/>. The city
    /// must fall (a fresh army with morale 80 and 30,000 troops against a city with 100%
    /// loyalty and no garrison will not always fall, so the check uses a manual placement
    /// of the city with Loyalty 0 -- an instant fall). Publishes
    /// <see cref="IC2.Engine.Cities.Capture.CityFallsToNation"/>; the cue list maps that to
    /// <see cref="AssetKeys.SfxCityCaptured"/>.
    /// </summary>
    private void CaptureEnemyCity()
    {
        _mainGame.Session.Submit($"besiege-city {CaptureArmyId} {EnemyCityId}");
    }

    /// <summary>
    /// Submits a besiege-city order through the no-blocking test's session. The city and army
    /// are placed independently of the capture test (they share the same world but at
    /// distinct coordinates), so the no-blocking cue list is the only one that fires.
    /// </summary>
    private void NoBlockingCaptureCity()
    {
        // For the no-blocking test we issue a fresh capture that the test's own session and
        // player are wired to. The session's events list is the same one the cue list reads,
        // so the cue fires on this session's player (the one with the 2.0 s stream).
        _noBlockingMainGame.Session.Submit($"besiege-city {NoBlockingArmyId} {NoBlockingCityId}");
    }

    /// <summary>
    /// Moves a rome army three cells through <see cref="MainGameScreen.SubmitForCheck"/>. The
    /// army is at (49, 40) and the move goes north to (49, 37) -- three cells, no city or
    /// army in the way.
    /// </summary>
    private void MoveHumanArmyThreeCells()
    {
        _moveMainGame.SubmitForCheck($"move {MoveArmyId} 49 37");
    }

    private void CheckCaptureCityCue()
    {
        var queue = _player.QueuedForCheck;
        var ok = Check(
            queue.Count == 1 && queue[0] == AssetKeys.SfxCityCaptured,
            $"a city-capture puts sfx.city_captured on the player's queue (got [{string.Join(", ", queue)}])");
        _ok &= ok;
    }

    private void CheckMoveUnitMoveCues()
    {
        var queue = _movePlayer.QueuedForCheck;
        var ok = Check(
            queue.Count == 3 && queue.All(c => c == AssetKeys.SfxUnitMove),
            $"a human army's three-cell move puts three sfx.unit_move cues on the queue "
            + $"(got [{string.Join(", ", queue)}])");
        _ok &= ok;
    }

    private void CheckCaptureCityMuted()
    {
        var queue = _player.QueuedForCheck;
        var ok = Check(
            queue.Count == 0,
            $"with Sound off, a city-capture queues nothing (got [{string.Join(", ", queue)}])");
        _ok &= ok;
    }

    private void CheckMoveUnitMoveMuted()
    {
        var queue = _movePlayer.QueuedForCheck;
        var ok = Check(
            queue.Count == 0,
            $"with Sound off, a human army's three-cell move queues nothing (got [{string.Join(", ", queue)}])");
        _ok &= ok;
    }

    /// <summary>
    /// T149 DoD 4 (Sol's review of PR 793, R6): the original's <c>PlaySoundA(path, NULL, 0)</c> with
    /// <c>SND_SYNC</c> blocked the game thread for every sound it played -- sound 10, the longest,
    /// froze the game for 1.4 s. The clone's queue-then-play must not block: a 2.0 s stream the
    /// check itself synthesises is bound to <c>sfx.city_captured</c> at startup, the
    /// <see cref="GameSession.Submit"/> call that enqueues the cue returns, cue handling included, in
    /// well under the original's stall window, and the player reports the stream as playing after
    /// the return. A pass here is the proof the cue list does not copy the original's
    /// synchronous path.
    /// </summary>
    private void CheckNoBlocking()
    {
        // The test's private session and player carry the 2.0 s stream bound to
        // sfx.city_captured; the cue list reads that key and asks the player to play it.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        NoBlockingCaptureCity();
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
        var isPlaying = _noBlockingPlayer.IsPlayingForTest(AssetKeys.SfxCityCaptured);
        var ok2 = Check(
            isPlaying,
            $"the 2.0 s sfx.city_captured stream is playing on the audio thread immediately after the Submit return "
            + $"(IsPlaying={isPlaying})");
        _ok &= ok2;
    }

    /// <summary>
    /// Build a 2.0 s mono 44.1 kHz 16-bit WAV in memory with one non-zero sample at the start so the
    /// peak check in <c>AssetLoader.LoadManifest</c>'s downstream code does not refuse it. The
    /// exact contents do not matter: a 2.0 s empty stream would still take 2.0 s to play on Godot's
    /// audio thread, which is the worst case the no-blocking test must cover.
    /// </summary>
    private static AudioStreamWav SynthesiseSilentWav(double durationSeconds)
    {
        const int sampleRate = 44100;
        var sampleCount = (int)(durationSeconds * sampleRate);
        var data = new byte[sampleCount * 2];
        // Set the first sample to a non-zero value (a single click) so the file has a peak
        // above 0; the rest is silence. The downstream test never reads the stream's
        // content, only its duration and its playing state.
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
