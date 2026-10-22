using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T127.md</c> Done-when 2 and 3 (the part that is about instant battles): the real
/// <see cref="MainGameScreen"/> on scripted states, so the tactical battle screen and the <em>Battle ended</em>
/// window are what the engine's battle shows. Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/TacticalBattleCheck.tscn
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <strong>The scripted state.</strong> The <c>classical-mediterranean</c> world with Rome (human) and the army
/// <c>army-3</c> of an AI nation moved beside Rome's <c>army-0</c>: <c>attack-army army-0 army-3</c> opens the battle.
/// Every battle is deterministic (the session's seed and the battle's own stream), so a second session fed the same
/// lines is the engine's reference for what the screen shows.
/// </para>
/// <para>
/// <strong>The pauses.</strong> The check never waits for real time: it steps the replay with
/// <see cref="TacticalBattleScreen.AdvancePlayback"/>, which is what the frame loop calls.
/// </para>
/// </remarks>
public partial class TacticalBattleCheck : Node
{
    private const string Attack = "attack-army army-0 army-3";

    private bool _ok = true;
    private readonly List<string> _cleanup = new();

    public override async void _Ready()
    {
        if (OS.GetCmdlineUserArgs().Contains("--capture-replay"))
        {
            CaptureReplay();
            return;
        }
        var originalShot = SettingsScreen.BattleShotDelaySeconds;
        var originalAttack = SettingsScreen.BattleAttackDelaySeconds;
        try
        {
            Check(originalShot == 1 && originalAttack == 2, "fresh process starts with designed shot 1 / melee 2 defaults before any setter");
            CheckPauseControlsAndPreservation();
            SettingsScreen.SetBattlePauses(0, 0);
            CheckTheBattleIsFoughtAndEnded();
            CheckAClickTheEngineRefusesShowsItsMessage();
            CheckSaveAsDuringTheBattleReopensIt();
            CheckThePausesAreDisplayOnly();
            CheckTypedFramePlayback();
            CheckAllegianceScreen();
            CheckContinuousStages();
            CheckConsecutiveAiBattles();
            CheckSurrender();
            CheckAnAiPhaseBattleIsFoughtOnTheSameScreen();
            CheckInstantBattlesAreShownAsBefore();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"TacticalBattleCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            SettingsScreen.SetBattlePauses(originalShot, originalAttack);
            foreach (var path in _cleanup)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        }

        // Let queued temporary scenes free and the native asynchronous audio thread release
        // its playback references before process exit. The bounded 2s teardown grace is
        // check-only, not a replay pause or an assertion about original-game timing.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
        var exitCode = _ok ? 0 : 1;
        GD.Print($"TacticalBattleCheck: exiting with code {exitCode}.");
        CheckExit.Quit(this, exitCode);
    }

    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));

    // Windowed only: captures the production screen while the production queue is manually held at each state.
    private async void CaptureReplay()
    {
        var ok = true;
        try
        {
            foreach (var pack in new[] { "authored", "placeholder" })
            {
                var settings = new SettingsScreen();
                AddChild(settings);
                var picker = Descendants(settings).OfType<OptionButton>().First(o => o.Name == SettingsScreen.PackPickerName);
                var index = Enumerable.Range(0, picker.ItemCount).First(i => picker.GetItemText(i) == pack);
                picker.Select(index);
                picker.EmitSignal(OptionButton.SignalName.ItemSelected, index);
                RemoveChild(settings);
                settings.QueueFree();
                await CaptureReadability(pack);
                SettingsScreen.SetBattlePauses(1, 2);
                var main = Build(ClassicalSession());
                main.SubmitForCheck(Attack);
                var screen = (TacticalBattleScreen)main.ActiveOverlay!;
                screen.SetProcess(false);
                await Capture($"{pack}-computer-placement", screen);
                screen.SkipPlayback();
                screen.SelectUnit(0);
                await Capture($"{pack}-human-placement", screen);
                var captured = new HashSet<string>();
                var temporal = new Dictionary<string, long>();
                var enemyMelee = false;
                // Real human turns, including the initial placement End turn. The engine alone drives the enemy.
                for (var round = 0; round < 12 && !captured.Contains("human-after-enemy-melee"); round++)
                {
                    screen.PressToolbarButton("end_turn");
                    for (var step = 0; screen.IsPlayingBack && step < 100000; step++)
                    {
                        var frame = screen.DisplayedFrame!;
                        await CaptureTemporal(pack, screen, temporal, captured);
                        enemyMelee |= frame.Event is TacticalMeleeEvent && frame.Actor?.Side == 1;
                        string? stage = frame.Kind switch
                        {
                            TacticalFrameKind.HalfRound when enemyMelee => "enemy-resolution-before-handoff",
                            TacticalFrameKind.Setup when frame.After.View.Placed && screen.ControllerLabel.Text.Contains("Human")
                                => enemyMelee ? "human-after-enemy-melee" : "human-combat-round",
                            TacticalFrameKind.Rout => "rout",
                            TacticalFrameKind.Cascade => "cascade",
                            TacticalFrameKind.Removal => "removal",
                            _ => null,
                        };
                        if (stage is not null && captured.Add(stage)) await Capture($"{pack}-{stage}", screen);
                        screen.AdvancePlayback(0.05);
                    }
                    if (main.ActiveOverlay != screen) throw new InvalidOperationException("Human fixture ended before normal combat handoff.");
                }
                if (!captured.Contains("human-after-enemy-melee")) throw new InvalidOperationException("Missing real human combat handoff after enemy melee.");
                screen.PressToolbarButton("computer_general");
                for (var step = 0; screen.IsPlayingBack && step < 100000; step++)
                {
                    var frame = screen.DisplayedFrame!;
                    await CaptureTemporal(pack, screen, temporal, captured);
                    string? stage = frame.Kind switch
                    {
                        TacticalFrameKind.Controller when screen.ControllerLabel.Text.Contains("Computer general") => "general-handoff",
                        TacticalFrameKind.Movement => "movement",
                        TacticalFrameKind.Shot => "shot",
                        TacticalFrameKind.Melee => "melee-current",
                        TacticalFrameKind.Rout => "rout",
                        TacticalFrameKind.Cascade => "cascade",
                        TacticalFrameKind.Removal => "removal",
                        TacticalFrameKind.Terminal => "terminal",
                        TacticalFrameKind.Reconciliation when frame.After.View.IsOver => "reconciliation",
                        _ => null,
                    };
                    if (stage is not null && captured.Add(stage)) await Capture($"{pack}-{stage}", screen);
                    screen.AdvancePlayback(0.05);
                }
                if (main.ActiveOverlay is not BattleEndedScreen) throw new InvalidOperationException("Terminal replay did not route to result.");
                await Capture($"{pack}-result", screen);
                foreach (var required in new[] { "human-combat-round", "human-after-enemy-melee", "general-handoff", "movement", "shot", "melee-current", "rout", "terminal", "reconciliation", "travel-0-start", "travel-0-interior", "travel-0-arrival", "travel-0-following-fight", "travel-1-start", "travel-1-interior", "travel-1-arrival", "travel-1-following-fight", "enemy-announcement", "rout-hold", "rout-fade-interior", "rout-removed" })
                    if (!captured.Contains(required)) throw new InvalidOperationException($"Missing visual stage {pack}/{required}");
                GD.Print($"T180 visual stages {pack}: {string.Join(", ", captured.Order())}");
                Free(main);
                var defending = Build(ToyAiAttackSession());
                defending.SubmitForCheck("declare-war south");
                defending.SubmitForCheck("end");
                var defenderScreen = (TacticalBattleScreen)defending.ActiveOverlay!;
                defenderScreen.SetProcess(false);
                defenderScreen.SkipPlayback();
                defenderScreen.SelectUnit(20);
                await Capture($"{pack}-human-defender-placement", defenderScreen);
                defenderScreen.PressToolbarButton("end_turn");
                defenderScreen.SkipPlayback();
                if (!defenderScreen.Model.View.Placed || !defenderScreen.ControllerLabel.Text.Contains("Human"))
                    throw new InvalidOperationException("Missing human defender combat handoff.");
                await Capture($"{pack}-human-defender-combat", defenderScreen);
                Free(defending);
            }
        }
        catch (Exception ex) { GD.PrintErr($"FAIL: capture replay {ex}"); ok = false; }
        SettingsScreen.SetBattlePauses(0, 0);
        GD.Print($"TacticalBattleCheck: exiting with code {(ok ? 0 : 1)}.");
        CheckExit.Quit(this, ok ? 0 : 1);
    }

    private async Task CaptureReadability(string pack)
    {
        SettingsScreen.SetBattlePauses(1, 2);
        var main = Build(ReadabilitySession());
        main.SubmitForCheck("attack-army north-army-1 south-army-1");
        var screen = (TacticalBattleScreen)main.ActiveOverlay!;
        screen.SetProcess(false);
        screen.SkipPlayback();
        foreach (var (shot, melee, name) in new[] { (1.0, 2.0, "defaults"), (0.0, 0.0, "explicit-zero"), (3.0, 3.0, "explicit-max") })
        {
            SettingsScreen.SetBattlePauses(shot, melee);
            screen.PressToolbarButton("change_pauses");
            await Capture($"{pack}-readability-pauses-{name}", screen);
            screen.ClosePausesDialog();
            var settings = new SettingsScreen();
            settings.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            AddChild(settings);
            await Capture($"{pack}-readability-settings-{name}", screen);
            RemoveChild(settings); settings.QueueFree();
        }
        foreach (var button in new[] { "friendly_units", "enemy_units" })
        {
            screen.PressToolbarButton(button);
            await Capture($"{pack}-readability-{button}", screen);
            screen.CloseUnitList();
        }
        SettingsScreen.SetBattlePauses(1, 2);
        screen.PressToolbarButton("computer_general");
        var captured = new HashSet<TacticalFrameKind>();
        for (var step = 0; screen.IsPlayingBack && step < 100000; step++)
        {
            if (screen.DisplayedFrame is { Kind: TacticalFrameKind.Shot or TacticalFrameKind.Melee or TacticalFrameKind.Rout } frame
                && captured.Add(frame.Kind))
                await Capture($"{pack}-readability-{frame.Kind}", screen);
            screen.AdvancePlayback(0.05);
        }
        if (!captured.Contains(TacticalFrameKind.Melee) || !captured.Contains(TacticalFrameKind.Rout))
            throw new InvalidOperationException("Readability capture requires melee and prominent rout.");
        Free(main);
    }

    private async Task CaptureTemporal(string pack, TacticalBattleScreen screen, Dictionary<string, long> selected, HashSet<string> captured)
    {
        var frame = screen.DisplayedFrame!;
        string? key = null;
        if (screen.PlaybackStage == TacticalPlaybackStage.Announcement && screen.Model.View.ComputerControlled[screen.Model.View.SideToMove])
            key = "enemy-announcement";
        if (screen.PlaybackPose is { Departing: false } moving)
        {
            var travel = $"travel-{moving.Glyph.Side}";
            if (!selected.ContainsKey(travel))
            {
                selected[travel] = frame.FrameId;
                selected[travel + "-slot"] = moving.Glyph.Slot;
            }
            if (selected[travel] == frame.FrameId)
                key = screen.PlaybackStage == TacticalPlaybackStage.Arrival ? travel + "-arrival"
                    : screen.PlaybackProgress == 0 ? travel + "-start" : screen.PlaybackProgress is >= 0.45 and <= 0.6 ? travel + "-interior" : null;
        }
        if (screen.PlaybackPose is { Departing: true })
        {
            if (!selected.ContainsKey("rout")) selected["rout"] = frame.FrameId;
            if (selected["rout"] == frame.FrameId)
                key = screen.PlaybackStage == TacticalPlaybackStage.RoutHold ? "rout-hold"
                    : screen.PlaybackProgress == 0 ? "rout-fade-start" : screen.PlaybackProgress is >= 0.45 and <= 0.6 ? "rout-fade-interior" : null;
        }
        if (selected.TryGetValue("rout", out var routed) && frame.FrameId == routed && screen.PlaybackStage == TacticalPlaybackStage.Frame)
            key = "rout-removed";
        if (frame.Event is TacticalShotEvent or TacticalMeleeEvent)
            for (var side = 0; side < 2; side++)
            {
                var travel = $"travel-{side}";
                if (selected.TryGetValue(travel + "-slot", out var slot) && frame.FrameId > selected[travel]
                    && (frame.Actor?.Slot == slot || frame.Target?.Slot == slot) && captured.Contains(travel + "-arrival"))
                {
                    var fight = travel + "-following-fight";
                    if (captured.Add(fight)) await Capture($"{pack}-temporal-{fight}", screen);
                }
            }
        if (key is not null && captured.Add(key)) await Capture($"{pack}-temporal-{key}", screen);
    }

    private async Task Capture(string name, TacticalBattleScreen screen)
    {
        var frame = screen.DisplayedFrame;
        var controller = screen.ControllerLabel.Text;
        var pose = screen.PlaybackPose;
        var transition = $"stage {screen.PlaybackStage} progress {screen.PlaybackProgress:0.000} pose {pose}";
        var position = pose is null ? "no transient glyph" : $"#{pose.Glyph.Slot} at ({pose.X:0.00},{pose.Y:0.00}) opacity {pose.Opacity:0.00}";
        var receipt = UiKit.MakeLabel($"T180 {name}\nframe {frame?.FrameId} {frame?.Kind} · {screen.PlaybackStage} {screen.PlaybackProgress:0%}\n{position}", AppTheme.Body + 6, Colors.White);
        receipt.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        receipt.Size = new Vector2(460, 140);
        // Receipt is check-only evidence metadata; it never changes production captions or snapshot values.
        AddChild(receipt);
        foreach (var size in new[] { new Vector2I(1280, 800), new Vector2I(1024, 768) })
        {
            GetWindow().Size = size;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            receipt.Position = new Vector2(12, GetViewport().GetVisibleRect().Size.Y - 145);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var path = Path.Combine(GameDataContext.RepositoryRoot, "rendered", $"T180-{name}-{size.X}x{size.Y}.png");
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            if (error != Error.Ok) throw new InvalidOperationException($"Capture failed: {error}");
            GD.Print($"T180 capture: {path} | {frame?.BattleId}/{frame?.FrameId} | {controller} | {transition}");
        }
        RemoveChild(receipt);
        receipt.QueueFree();
    }

    // ---- Done-when 2: the battle, its end and the window ----

    private void CheckTheBattleIsFoughtAndEnded()
    {
        var mainGame = Build(ClassicalSession());
        mainGame.SubmitForCheck(Attack);

        var screen = mainGame.ActiveOverlay as TacticalBattleScreen;
        if (!Check(screen is not null, $"the attack opens the battle screen (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})"))
        {
            Free(mainGame);
            return;
        }

        var view = mainGame.Session.OpenTacticalBattle!;
        var rome = view.AttackerNationId;
        Check(screen!.BoardYForEngineY(0) == screen.Model.BoardHeight - 1
            && screen.EngineYForBoardY(screen.Model.BoardHeight - 1) == 0,
            "human attacker is displayed at the bottom and displayed rows map back to engine rows");
        Check(
            !view.Placed && view.SideToMove == TacticalBattleState.AttackerSide && view.Counter == 2,
            $"the defender's placement is done and Rome is to place (placed {view.Placed}, side {view.SideToMove}, counter {view.Counter})");
        Check(rome == "rome", $"Rome attacks (attacker nation '{rome}')");
        Check(
            screen!.CaptionLabel.Text == screen.Model.Caption && screen.CaptionLabel.Text.Contains("Rome to place units."),
            $"the caption names the side to place ('{screen.CaptionLabel.Text}')");
        Check(
            screen.Model.Cells.Count(c => c.IconKey is not null) == view.Slots.Count(s => s.IsLive),
            "the board shows one icon per live unit");

        // A placement reaches the engine: the first unit moves to an empty cell of Rome's home rows.
        var homeRows = mainGame.Session.Ruleset.Combat.Tactical.HomeRows;
        var target = screen.Model.Cells
            .Where(c => c.IconKey is null && c.Y < homeRows)
            .OrderByDescending(c => c.X).ThenBy(c => c.Y)
            .First();
        screen.SelectUnit(0);
        Check(screen.SelectedSlot == 0, "a click on an own unit selects it");
        // Placed with the designed pauses, so the placement's glide is still playing back at the next click.
        var (savedShot, savedAttack) = (SettingsScreen.BattleShotDelaySeconds, SettingsScreen.BattleAttackDelaySeconds);
        SettingsScreen.SetBattlePauses(1, 2);
        screen.ClickBoardCell(target.X, screen.BoardYForEngineY(target.Y));
        var placed = mainGame.Session.OpenTacticalBattle!.Slots[0];
        Check(
            placed.X == target.X && placed.Y == target.Y,
            $"the placement click reached the engine (unit 0 at ({placed.X},{placed.Y}), wanted ({target.X},{target.Y}))");

        // The next click on another own unit selects it at once, even while the placement still plays back.
        if (mainGame.Session.OpenTacticalBattle!.Slots[1].IsLive)
        {
            var playingBefore = screen.IsPlayingBack;
            screen.SelectUnit(1);
            Check(
                playingBefore && screen.SelectedSlot == 1 && !screen.IsPlayingBack,
                $"the first click after a placement, during its playback, ends it and selects the next unit (selected {screen.SelectedSlot}, playback before the click {playingBefore})");
        }

        screen.SkipPlayback();
        SettingsScreen.SetBattlePauses(savedShot, savedAttack);

        // End turn reaches the engine.
        var counterBefore = mainGame.Session.OpenTacticalBattle!.Counter;
        screen.PressToolbarButton("end_turn");
        var afterEnd = mainGame.Session.OpenTacticalBattle;
        Check(
            afterEnd is not null && afterEnd.Counter > counterBefore && afterEnd.Placed,
            $"End turn reached the engine (counter {counterBefore} -> {afterEnd?.Counter}, placed {afterEnd?.Placed})");

        // The reference: the same lines in a second session give the engine's own report.
        var reference = ClassicalSession();
        reference.Submit(Attack);
        reference.BattlePlace(0, target.X, target.Y);
        reference.BattleEnd();
        var referenceOutput = reference.BattleGeneral(true);
        var report = referenceOutput.EndedBattleReport;
        if (!Check(report is not null, "the reference session plays the battle to its end under Computer general"))
        {
            Free(mainGame);
            return;
        }

        screen.PressToolbarButton("computer_general");
        Check(!screen.IsPlayingBack && screen.ExchangesShown > 0, "with pauses at 0 s all typed exchanges traverse without waiting");
        var ended = mainGame.ActiveOverlay as BattleEndedScreen;
        if (!Check(ended is not null, $"Computer general on plays the battle to the end and the Battle ended window opens (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})"))
        {
            Free(mainGame);
            return;
        }

        Check(mainGame.Session.OpenTacticalBattle is null, "the engine has no open battle");
        var expected = BattleEndedViewModel.FromReport(report!, reference.State, reference.Ruleset);
        Check(ended!.WinnerLabel.Text == expected.WinnerLine, $"the winner line equals the engine's ('{ended.WinnerLabel.Text}')");
        Check(ended.MoneyLabel.Text == expected.MoneyLine && ended.SuppliesLabel.Text == expected.SuppliesLine, "the money and supplies lines equal the engine's");
        var cells = new List<string>(BattleEndedViewModel.Headers);
        foreach (var row in expected.Rows)
        {
            cells.AddRange(new[] { row.Label, row.WinnerStart.ToString(), row.WinnerFinish.ToString(), row.LoserStart.ToString() });
        }

        Check(ended.TableTexts.SequenceEqual(cells), "the table equals the engine's TacticalBattleReport");
        Check(
            ended.TableTexts.Contains(report!.WinnerStartTotal.ToString())
            && ended.TableTexts.Contains(report.WinnerFinishTotal.ToString())
            && ended.TableTexts.Contains(report.LoserStartTotal.ToString()),
            $"the totals are the report's ({report.WinnerStartTotal}, {report.WinnerFinishTotal}, {report.LoserStartTotal})");

        ended.Close();
        Check(
            mainGame.ActiveOverlay is null or PeaceOfferScreen,
            $"after OK the map returns, or the peace offer (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
        Check(
            Equals(mainGame.Session.State, reference.State),
            "the game after the battle is the engine's post-battle state");
        Check(
            mainGame.Session.State.ArmyById(report.LoserArmyId) is null,
            "the loser army is gone from the map");
        Free(mainGame);
    }

    private void CheckAClickTheEngineRefusesShowsItsMessage()
    {
        var mainGame = Build(ClassicalSession());
        mainGame.SubmitForCheck(Attack);
        var screen = (TacticalBattleScreen)mainGame.ActiveOverlay!;
        var before = mainGame.Session.OpenTacticalBattle!;

        // Setting a melee target during placement is not legal; the screen sends it and shows the engine's refusal.
        var enemy = before.Slots.Select((s, i) => (s, i)).First(p => p.s.IsLive && p.i >= 20);
        screen.SelectUnit(0);
        screen.ClickCell(enemy.s.X, enemy.s.Y);

        Check(
            screen.MessageLabel.Text.Contains("Refused"),
            $"a click the engine refuses shows its message ('{screen.MessageLabel.Text}')");
        Check(Equals(before, mainGame.Session.OpenTacticalBattle), "the refused click changed nothing");
        Free(mainGame);
    }

    // ---- Done-when 2: Save As in a battle, reload through the Load screen ----

    private void CheckSaveAsDuringTheBattleReopensIt()
    {
        var mainGame = Build(ClassicalSession());
        mainGame.SubmitForCheck(Attack);
        var screen = (TacticalBattleScreen)mainGame.ActiveOverlay!;
        screen.PressToolbarButton("end_turn");
        var saved = mainGame.Session.OpenTacticalBattle!;

        var path = Path.Combine(ProjectSettings.GlobalizePath("user://saves"), "t127-tacticalbattlecheck.json");
        _cleanup.Add(path);
        mainGame.SaveAsForCheck(path);
        Check(File.Exists(path), "Save As during the battle writes a save");
        Check(
            mainGame.ActiveOverlay is TacticalBattleScreen && screen.MessageLabel.Text.Contains("Saved to"),
            $"the battle screen stays and says the game was saved ('{screen.MessageLabel.Text}')");
        Check(Equals(saved, mainGame.Session.OpenTacticalBattle), "saving changed nothing in the battle");

        GameSession? resumed = null;
        var load = new LoadGameScreen();
        load.GameResumed += s => resumed = s;
        AddChild(load);
        load.ContinueForCheck(path);
        RemoveChild(load);
        load.QueueFree();
        if (!Check(resumed is not null, "the Load screen resumes the save"))
        {
            Free(mainGame);
            return;
        }

        Check(resumed!.HumanBattles == HumanBattleMode.Tactical, "the resumed session fights human battles on the board");
        var second = Build(resumed);
        var reopened = second.ActiveOverlay as TacticalBattleScreen;
        if (!Check(reopened is not null, $"the loaded mid-battle save reopens the battle screen (got {second.ActiveOverlay?.GetType().Name ?? "null"})"))
        {
            Free(second);
            Free(mainGame);
            return;
        }

        var view = resumed.OpenTacticalBattle!;
        Check(
            view.SideToMove == saved.SideToMove && view.Placed == saved.Placed,
            $"it reopens at the side to move (side {view.SideToMove}, placed {view.Placed})");
        Check(view.Counter == saved.Counter + 1, $"at the half-round's setup (counter {saved.Counter} -> {view.Counter})");
        Check(
            view.Slots.Select(s => (s.X, s.Y, s.Troops, s.Type)).SequenceEqual(saved.Slots.Select(s => (s.X, s.Y, s.Troops, s.Type))),
            "the units are where they were");
        var rules = resumed.Ruleset;
        Check(
            view.Slots.Skip(view.SideToMove * 20).Take(20).Where(s => s.IsLive).All(s => s.Moves == rules.UnitTypes[s.Type].Moves && s.Target < 0),
            "the side to move has its moves refilled and no targets");
        Check(reopened!.Model.Caption == TacticalBattleViewModel.From(view, BattleDisplay.For(view, resumed.State, rules)).Caption, "the screen draws the reopened battle");
        Free(second);
        Free(mainGame);
    }

    private void CheckNoPersistentListsOrHistory(TacticalBattleScreen screen)
    {
        var nodes = Descendants(screen).ToArray();
        Check(!nodes.OfType<Label>().Any(label => label.Text == "Previous exchange" || label.Name == "PreviousExchangeLabel"), "previous exchange controls are absent from actual tree");
        Check(!screen.UnitListOpen && screen.UnitListTexts.Count == 0, "no persistent roster is visible");
        Check(!nodes.OfType<Button>().Any(button => button.Text == "Close unit list"), "closed deliberate list has no row controls in actual tree");
        Check(nodes.OfType<RichTextLabel>().Contains(screen.ExchangeLabel) && screen.ExchangeLabel.Visible,
            "current exchange rich control exists and is visible");
    }

    private void CheckPauseControlsAndPreservation()
    {
        foreach (var (shot, attack) in new[] { (1.0, 2.0), (0.0, 0.0), (0.3, 2.0), (3.0, 3.0) })
        {
            if (shot != 1 || attack != 2) SettingsScreen.SetBattlePauses(shot, attack);
            for (var reopen = 0; reopen < 2; reopen++)
            {
                var settings = new SettingsScreen();
                AddChild(settings);
                CheckPauseSliders(settings, shot, attack, "Settings reopen");
                RemoveChild(settings); settings.QueueFree();
                var host = Build(ClassicalSession());
                host.SubmitForCheck(Attack);
                var screen = (TacticalBattleScreen)host.ActiveOverlay!;
                screen.SkipPlayback();
                screen.PressToolbarButton("change_pauses");
                Check(screen.PausesDialogOpen, "Change pauses opens after settings navigation");
                CheckPauseSliders(screen, shot, attack, "Change pauses reopen");
                screen.ClosePausesDialog();
                Check(SettingsScreen.BattleShotDelaySeconds == shot && SettingsScreen.BattleAttackDelaySeconds == attack,
                    "screen creation/reopen/navigation preserves explicit pauses including zero");
                Free(host);
            }
        }
    }

    private void CheckPauseSliders(Node root, double shot, double attack, string context)
    {
        var sliders = Descendants(root).OfType<HSlider>().Where(s => s.Name != SettingsScreen.VolumeSliderName).ToArray();
        Check(sliders.Length == 2, context + " has both pause controls");
        if (sliders.Length != 2) return;
        Check(sliders[0].Value == shot && sliders[1].Value == attack, context + " shows selected pauses");
        Check(sliders.All(s => s.MinValue == 0 && s.MaxValue == 3)
            && sliders[0].Step == 0.1 && sliders[1].Step == 1, context + " retains ranges and grids");
    }

    // ---- Done-when 2: the pauses ----

    private void CheckThePausesAreDisplayOnly()
    {
        // 0 s: the whole battle is played with nothing to wait for.
        SettingsScreen.SetBattlePauses(0, 0);
        var instant = Build(ClassicalSession());
        instant.SubmitForCheck(Attack);
        var instantScreen = (TacticalBattleScreen)instant.ActiveOverlay!;
        instantScreen.PressToolbarButton("computer_general");
        Check(!instantScreen.IsPlayingBack && instantScreen.ExchangesShown > 0, "pauses at 0 s: every exchange enters without delay");
        Check(instant.ActiveOverlay is BattleEndedScreen, "pauses at 0 s: the Battle ended window is up at once");
        Free(instant);

        // 1.0 s: the engine's state is final at once and the screen replays the exchanges, one wait each.
        SettingsScreen.SetBattlePauses(1.0, 1.0);
        Check(
            SettingsScreen.BattleShotDelaySeconds == 1.0 && SettingsScreen.BattleAttackDelaySeconds == 1.0,
            "the pauses setting keeps 1.0 s");
        var slow = Build(ClassicalSession());
        slow.SubmitForCheck(Attack);
        var slowScreen = (TacticalBattleScreen)slow.ActiveOverlay!;
        slowScreen.SkipPlayback(); // Opening placement frames also use the production traversal.
        slowScreen.PressToolbarButton("computer_general");

        Check(slowScreen.IsPlayingBack, "pauses at 1.0 s: the screen is replaying");
        Check(slow.Session.OpenTacticalBattle is null, "pauses at 1.0 s: the engine's battle is already over");
        Check(slow.ActiveOverlay is TacticalBattleScreen, "pauses at 1.0 s: the battle screen still holds the window");
        while (slowScreen.IsPlayingBack && slowScreen.ExchangesShown == 0) slowScreen.AdvancePlayback(0.01);
        var firstShown = slowScreen.ExchangesShown;
        var firstText = slowScreen.ExchangeLabel.Text;
        Check(firstShown == 1 && firstText.Length > 0, $"the first exchange is shown ('{firstText.Replace("\n", " / ")}')");
        slowScreen.AdvancePlayback(0.5);
        Check(slowScreen.ExchangesShown == firstShown && slowScreen.ExchangeLabel.Text == firstText, "it waits between exchanges (after 0.5 s: still the first)");
        slowScreen.AdvancePlayback(10);
        Check(slowScreen.ExchangesShown > firstShown || slowScreen.PlaybackStage is TacticalPlaybackStage.Travel or TacticalPlaybackStage.Arrival or TacticalPlaybackStage.Announcement or TacticalPlaybackStage.RoutHold or TacticalPlaybackStage.RoutFade || !slowScreen.IsPlayingBack, "oversized elapsed time reaches a protected visible stage or subsequent exchange");

        // A click on the side panel during the replay ends it.
        slowScreen.PanelClicked();
        Check(!slowScreen.IsPlayingBack && slow.ActiveOverlay is BattleEndedScreen, "a click on the side panel ends the replay and the Battle ended window opens");
        var window = (BattleEndedScreen)slow.ActiveOverlay!;
        var reference = ClassicalSession();
        reference.Submit(Attack);
        var report = reference.BattleGeneral(true).EndedBattleReport!;
        Check(
            window.TableTexts.Contains(report.WinnerFinishTotal.ToString()) && Equals(slow.Session.State, reference.State),
            "the pauses changed nothing the engine decided (same report, same state as at 0 s)");
        Free(slow);

        // The setting is a player setting only: it is in no save.
        SettingsScreen.SetBattlePauses(0, 0);
        Check(
            SettingsScreen.BattleShotDelaySeconds == 0 && SettingsScreen.BattleAttackDelaySeconds == 0,
            "the explicit zero setting is retained");

        // Change pauses opens the dialog and writes the player setting.
        var dialogHost = Build(ClassicalSession());
        dialogHost.SubmitForCheck(Attack);
        var dialogScreen = (TacticalBattleScreen)dialogHost.ActiveOverlay!;
        dialogScreen.PressToolbarButton("change_pauses");
        Check(dialogScreen.PausesDialogOpen, "Change pauses opens the pauses dialog");
        dialogScreen.ClosePausesDialog();
        Check(!dialogScreen.PausesDialogOpen, "the dialog closes");
        Free(dialogHost);
    }

    private void CheckTypedFramePlayback()
    {
        SettingsScreen.SetBattlePauses(0, 0);
        var main = Build(ClassicalSession());
        main.SubmitForCheck(Attack);
        var screen = (TacticalBattleScreen)main.ActiveOverlay!;
        var opening = screen.DisplayedFrame;
        Check(opening is not null && screen.Model.View == opening.After.View, "opening batch reaches production screen via retained timeline");
        Check(screen.ControllerLabel.Text.Contains("Human") && screen.ControllerLabel.Text.Contains("placement"), "human placement controller is explicit");
        CheckNoPersistentListsOrHistory(screen);
        foreach (var (button, side) in new[] { ("friendly_units", screen.Model.View.SideToMove), ("enemy_units", 1 - screen.Model.View.SideToMove) })
        {
            screen.PressToolbarButton(button);
            Check(screen.UnitListOpen && screen.UnitListTexts.Count == screen.Model.RosterRows(side).Count + 1,
                button + " deliberately opens only the displayed side's units");
            foreach (var row in screen.Model.RosterRows(side))
                Check(screen.UnitListTexts.Any(text => text.Contains(string.Join(" · ", row.Cells))), button + " rows match displayed snapshot");
            screen.CloseUnitList();
            CheckNoPersistentListsOrHistory(screen);
        }
        var seen = new List<TacticalBattleFrame>();
        var cues = new List<TacticalBattleFrame>();
        var terminalSeen = false;
        var reconciled = false;
        screen.FrameEntered += frame =>
        {
            seen.Add(frame);
            Check(main.ActiveOverlay == screen, "result remains behind every entered frame");
            var displayed = screen.PlaybackStage is TacticalPlaybackStage.Travel or TacticalPlaybackStage.RoutHold or TacticalPlaybackStage.RoutFade ? frame.Before : frame.After;
            Check(screen.Model.View == displayed.View, "board and panels read displayed transition snapshot");
            Check(screen.Model.Cells.Select(c => c.Code).SequenceEqual(displayed.View.Grid), "complete displayed grid equals producer grid");
            for (var side = 0; side < 2; side++)
                foreach (var row in screen.Model.RosterRows(side))
                    Check(row.Cells[2] == displayed.View.Slots[row.Slot].Troops.ToString(), "roster troops equal displayed snapshot");
            if (frame.Kind == TacticalFrameKind.HalfRound)
                Check(screen.ControllerLabel.Text.StartsWith("Finishing the previous half-round", StringComparison.Ordinal) && !screen.CaptionLabel.Text.Contains("to move units"), "pre-melee handoff does not announce next human turn");
            if (frame.Kind == TacticalFrameKind.Terminal) terminalSeen = true;
            if (terminalSeen && frame.Kind == TacticalFrameKind.Reconciliation) reconciled = true;
        };
        screen.ExchangeEntered += frame =>
        {
            cues.Add(frame);
            Check(screen.ExchangePlainText == string.Join("\n", TacticalBattleViewModel.PresentExchange(frame, BattleDisplay.For(screen.Model.View, main.Session.State, main.Session.Ruleset, main.Session.World)).PlainLines), "current typed exchange text agrees with entry notification");
            Check(screen.ExchangeLabel.GetParsedText() == screen.ExchangePlainText, "rich exchange parses to exact accessible plain text including arbitrary brackets");
            Check(screen.ExchangeLabel.Text.Count(c => c == '[') >= 4, "current exchange has independent owner colour spans");
            Check(screen.ActorLabel.Text.Contains("#" + frame.Actor!.Slot) && screen.TargetLabel.Text.Contains("#" + frame.Target!.Slot), "participant panels use stable actor/target slots");
        };
        SettingsScreen.SetBattlePauses(0.2, 1);
        screen.SelectUnit(0);
        screen.PressToolbarButton("computer_general");
        Check(screen.IsPlayingBack && main.ActiveOverlay == screen, "terminal output cannot bypass replay");
        var path = Path.Combine(ProjectSettings.GlobalizePath("user://saves"), "t180-authoritative-during-replay.json");
        _cleanup.Add(path);
        main.SaveAsForCheck(path);
        Check(main.ActiveOverlay == screen && screen.IsPlayingBack, "Save callback cannot open peace/result ahead of terminal playback");
        GameSession? saved = null;
        var load = new LoadGameScreen();
        load.GameResumed += resumed => saved = resumed;
        AddChild(load);
        load.ContinueForCheck(path);
        RemoveChild(load);
        load.QueueFree();
        Check(saved is not null && saved.OpenTacticalBattle is null && saved.State == main.Session.State, "Save during intermediate playback serializes final authoritative session, never display snapshot");
        for (var i = 0; screen.IsPlayingBack && cues.Count < 2 && i < 10000; i++) screen.AdvancePlayback(0.05);
        Check(cues.Count >= 2 && screen.ExchangePlainText.Length > 0, "current exchange stays visible across exchanges");
        CheckNoPersistentListsOrHistory(screen);
        screen.SkipPlayback();
        Check(terminalSeen && reconciled && main.ActiveOverlay is BattleEndedScreen, "terminal and final reconciliation precede result routing");
        Check(seen.Select(f => (f.BattleId, f.FrameId)).Distinct().Count() == seen.Count, "every production frame entered once");
        Check(cues.Count == seen.Count(f => f.Event is TacticalShotEvent or TacticalMeleeEvent), "one typed exchange notification at every delay and skipped wait");
        var count = seen.Count;
        screen.SkipPlayback();
        screen.AdvancePlayback(100);
        Check(seen.Count == count, "finish and advance after completion repeat no frames");
        Free(main);
        SettingsScreen.SetBattlePauses(0, 0);
    }

    private void CheckAllegianceScreen()
    {
        SettingsScreen.SetBattlePauses(0, 0);
        var main = Build(ReadabilitySession());
        main.SubmitForCheck("attack-army north-army-1 south-army-1");
        var screen = (TacticalBattleScreen)main.ActiveOverlay!;
        var exchanges = 0;
        screen.ExchangeEntered += frame =>
        {
            exchanges++;
            Check(screen.ExchangePlainText.Contains("SELEUCID") && screen.ExchangePlainText.Contains("PTOLEMEAN"),
                "actual screen identifies both stable world owners despite renamed nation/unit text");
            Check(screen.ExchangeLabel.GetParsedText() == screen.ExchangePlainText, "actual rich text preserves literal bracket tags");
            var colors = new[] { frame.Actor!.NationId, frame.Target!.NationId }
                .Select(id => BattleAllegiance.For(id, "ignored", main.Session.World).ColorHex).ToArray();
            Check(colors.Distinct().Count() == 2 && colors.All(color => screen.ExchangeLabel.Text.Contains("[color=" + color + "]")),
                "actor and target have independent own-palette spans");
            CheckNoPersistentListsOrHistory(screen);
        };
        screen.PressToolbarButton("computer_general");
        Check(exchanges > 0 && main.Session.OpenTacticalBattle is null && main.ActiveOverlay is BattleEndedScreen,
            "alias/escaped screen survives removed combatants and terminal null authoritative view");
        Free(main);
    }

    private void CheckContinuousStages()
    {
        SettingsScreen.SetBattlePauses(0, 0);
        var main = Build(ClassicalSession());
        main.SubmitForCheck(Attack);
        var screen = (TacticalBattleScreen)main.ActiveOverlay!;
        screen.SetProcess(false);
        SettingsScreen.SetBattlePauses(1, 1);
        screen.PressToolbarButton("computer_general");
        var visits = new HashSet<string>();
        var entered = new List<long>();
        screen.FrameEntered += f => entered.Add(f.FrameId);
        var routedFrame = -1L;
        for (var i = 0; screen.IsPlayingBack && i < 100000; i++)
        {
            var frame = screen.DisplayedFrame!;
            var model = screen.Model;
            Check(model.View == screen.DisplayedSnapshot.View, "progress retains one shared displayed view");
            Check(model.Cells.Select(c => c.Code).SequenceEqual(screen.DisplayedSnapshot.View.Grid), "progress board grid and panels agree");
            Check(model.VisualUnits.Where(u => u.Cell.Slot >= 0).GroupBy(u => u.Cell.Slot).All(g => g.Count() == 1), "no stationary duplicate during travel or departure");
            if (screen.PlaybackStage == TacticalPlaybackStage.Announcement && model.View.ComputerControlled[model.View.SideToMove])
            {
                Check(screen.ControllerLabel.Text.Contains("TURN BEGINS") && screen.ControllerLabel.Text.Contains("Computer nation"), "enemy controller has a visible turn announcement");
                visits.Add("enemy");
            }
            if (screen.PlaybackPose is { } pose)
            {
                var glyph = model.VisualUnits.Single(u => u.Cell.Slot == pose.Glyph.Slot);
                Check(glyph.X == pose.X && glyph.Y == pose.Y && glyph.Opacity == pose.Opacity, "production board/minimap consume shared interpolated pose");
                Check(model.ParticipantLines(pose.Glyph.Slot, "unit").Any(l => l.Contains(model.PositionText(pose.Glyph.Slot))), "participant position follows displayed pose");
                if (!pose.Departing)
                {
                    var phase = screen.PlaybackStage == TacticalPlaybackStage.Arrival ? "arrival" : screen.PlaybackProgress == 0 ? "start" : "interior";
                    visits.Add($"side{pose.Glyph.Side}-{phase}");
                    Check(screen.DisplayedSnapshot == (screen.PlaybackStage == TacticalPlaybackStage.Arrival ? frame.After : frame.Before), "travel commits complete snapshot only at arrival");
                }
                else
                {
                    Check(screen.ExchangeLabel.Text.Contains("UNIT ROUTED") || screen.ExchangeLabel.Text.Contains("ROUT CASCADE REMOVAL"), "prominent rout identity remains beside numeric exchange losses");
                    Check(screen.ExchangeLabel.Text.Contains("LOSSES"), "rout retains numeric current-exchange losses");
                    Check(glyph.X == pose.Glyph.X && glyph.Y == pose.Glyph.Y, "rout fades at last displayed position");
                    routedFrame = frame.FrameId;
                    if (screen.PlaybackStage == TacticalPlaybackStage.RoutHold) visits.Add("hold");
                    if (screen.PlaybackStage == TacticalPlaybackStage.RoutFade && pose.Opacity is > 0 and < 1) visits.Add("fade");
                }
            }
            if (frame.FrameId == routedFrame && screen.PlaybackStage == TacticalPlaybackStage.Frame)
            {
                Check(screen.PlaybackPose is null && screen.DisplayedSnapshot == frame.After, "fade completion atomically removes presentation glyph and commits After");
                Check(!model.RosterRows(frame.Actor!.Side).Any(r => r.Slot == frame.Actor.Slot), "rout commit removes roster row with board glyph");
                visits.Add("removed");
            }
            screen.AdvancePlayback(1000);
        }
        Check(!screen.IsPlayingBack && main.ActiveOverlay is BattleEndedScreen, "all protected transitions and reconciliation precede result");
        foreach (var required in new[] { "enemy", "side0-start", "side0-interior", "side0-arrival", "side1-start", "side1-interior", "side1-arrival", "hold", "fade", "removed" })
            Check(visits.Contains(required), "large-delta production traversal observes " + required);
        Check(entered.Distinct().Count() == entered.Count, "progress ticks never duplicate frame entries");
        Free(main);
        SettingsScreen.SetBattlePauses(0, 0);
    }

    private void CheckConsecutiveAiBattles()
    {
        SettingsScreen.SetBattlePauses(0, 0);
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        StartingArmy Army(string id, string nation, int x, int y, int troops) => new(id, nation, x, y,
            Morale: nation == "north" ? 68 : 59, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(0, nation == "north" ? "light_infantry" : "heavy_infantry", troops, 6, "duplicate")));
        var world = toy.World with { StartingArmies = ValueList.Of(
            Army("north-army-1", "north", 3, 2, 400), Army("north-army-2", "north", 6, 4, 400),
            Army("south-army-1", "south", 4, 2, 6000), Army("south-army-2", "south", 6, 5, 6000)) };
        var main = Build(new GameSession(world, toy.Ruleset, toy.Scenario) { HumanBattles = HumanBattleMode.Tactical });
        main.SubmitForCheck("declare-war south");
        main.SubmitForCheck("end");
        var first = main.ActiveOverlay as TacticalBattleScreen;
        if (!Check(first is not null, "two-attack AI lap opens its first battle")) { Free(main); return; }
        var firstId = first!.DisplayedFrame!.BattleId;
        first.PressToolbarButton("computer_general");
        var opening = main.Session.LastTacticalTimeline.Where(f => f.Kind == TacticalFrameKind.Initial).ToArray();
        if (!Check(opening.Length == 1 && main.Session.OpenTacticalBattle is not null, "first terminal submission also opens second battle")) { Free(main); return; }
        var nextId = opening[0].BattleId;
        Check(first.DisplayedFrame!.BattleId == firstId && first.DisplayedFrame.Kind == TacticalFrameKind.Reconciliation, "first screen consumes only its own terminal batch");
        Check(main.ActiveOverlay is BattleEndedScreen, "first result precedes second battlefield");
        // A later empty read-only submission clears Last. The result's retained opening must survive it.
        main.Session.Submit("help");
        Check(main.Session.LastTacticalTimeline.Count == 0, "read-only command replaces retained producer batch");
        ((BattleEndedScreen)main.ActiveOverlay!).Close();
        var next = main.ActiveOverlay as TacticalBattleScreen;
        Check(next is not null && next.DisplayedFrame?.BattleId == nextId && nextId != firstId, "next screen receives retained opening once with distinct battle identity");
        if (next is not null)
        {
            Check(next.ExchangePlainText.Length == 0 && !next.UnitListOpen, "next battle has no stale exchange history");
            Check(next.ControllerLabel.Text.Contains("Human") && next.ControllerLabel.Text.Contains("placement"), "human defender controller is explicit after AI battle handoff");
            next.PressToolbarButton("computer_general");
            Check(main.ActiveOverlay is BattleEndedScreen, "second battle independently reaches its result");
        }
        Free(main);
    }

    // ---- surrender ----

    private void CheckSurrender()
    {
        var mainGame = Build(ClassicalSession());
        mainGame.SubmitForCheck(Attack);
        var screen = (TacticalBattleScreen)mainGame.ActiveOverlay!;
        var before = mainGame.Session.OpenTacticalBattle!;
        var aiNationName = mainGame.Session.State.NationById(before.DefenderNationId)!.Name;

        screen.PressToolbarButton("surrender");
        Check(screen.SurrenderPromptOpen, "Surrender asks for confirmation");
        screen.AnswerSurrender(false);
        Check(!screen.SurrenderPromptOpen && Equals(before, mainGame.Session.OpenTacticalBattle), "No leaves the battle unchanged");

        screen.PressToolbarButton("surrender");
        screen.AnswerSurrender(true);
        var ended = mainGame.ActiveOverlay as BattleEndedScreen;
        Check(ended is not null, $"Yes surrenders and the Battle ended window opens (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
        if (ended is not null)
        {
            Check(
                ended.WinnerLabel.Text.StartsWith($"{aiNationName}'s", StringComparison.Ordinal),
                $"the other side won ('{ended.WinnerLabel.Text}')");
            ended.Close();
        }

        Check(mainGame.Session.OpenTacticalBattle is null, "the battle is over after the surrender");
        Free(mainGame);
    }

    // ---- T126: an AI seat's attack on the human opens the battle on the same screen ----

    private void CheckAnAiPhaseBattleIsFoughtOnTheSameScreen()
    {
        var mainGame = Build(ToyAiAttackWithNeutralBattleSession());
        mainGame.SubmitForCheck("declare-war south");
        var turnBefore = mainGame.Session.State.Calendar.TurnIndex;
        mainGame.SubmitForCheck("end");

        var screen = mainGame.ActiveOverlay as TacticalBattleScreen;
        if (!Check(screen is not null, $"an AI seat's attack opens the battle screen at the end of the turn (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})"))
        {
            Free(mainGame);
            return;
        }

        var view = mainGame.Session.OpenTacticalBattle!;
        Check(view.SideToMove == TacticalBattleState.DefenderSide && !view.Placed, "the human defender places first");
        Check(view.DefenderNationId == "north", "the human is the defender");
        Check(screen!.BoardYForEngineY(0) == 0 && screen.EngineYForBoardY(0) == 0,
            "human defender remains at the bottom with engine high-Y rows displayed low");
        var defenderUnit = view.Slots.Select((slot, index) => (slot, index))
            .First(pair => pair.index >= 20 && pair.slot.IsLive);
        Check(defenderUnit.slot.Y >= screen.Model.BoardHeight / 2,
            $"the clicked human defender occupies a high engine row ({defenderUnit.slot.Y})");
        screen.ClickBoardCell(defenderUnit.slot.X, screen.BoardYForEngineY(defenderUnit.slot.Y));
        Check(screen.SelectedSlot == defenderUnit.index,
            "a human defender board click maps the displayed row back to the correct engine unit");

        // Save is refused by the engine, and the screen shows only its message.
        mainGame.PressSaveForCheck("t127-aiphase");
        Check(
            screen!.MessageLabel.Text == "Finish the battle first: the turn is in progress.",
            $"Save during an AI-phase battle shows the engine's message and nothing else ('{screen.MessageLabel.Text}')");
        Check(mainGame.ActiveOverlay is TacticalBattleScreen, "the battle screen stays");

        SettingsScreen.SetBattlePauses(0.2, 1);
        screen.PressToolbarButton("computer_general");
        var originalBattles = mainGame.Session.LastBattles.ToArray();
        Check(originalBattles.Length > 0 && screen.IsPlayingBack, "AI completion retains battle-result metadata while playback is pending");
        var path = Path.Combine(ProjectSettings.GlobalizePath("user://saves"), "t180-ai-completion-metadata.json");
        _cleanup.Add(path);
        mainGame.SaveAsForCheck(path);
        Check(mainGame.Session.LastBattles.Count == 0 && mainGame.ActiveOverlay == screen, "Save replaces LastBattles without bypassing replay");
        screen.SkipPlayback();
        var ended = mainGame.ActiveOverlay as BattleEndedScreen;
        Check(ended is not null, $"Computer general plays it to the end (got {mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
        ended?.Close();
        var shown = new List<BattleResultViewModel>();
        while (mainGame.ActiveOverlay is BattleResultScreen result)
        {
            shown.Add(result.Result);
            result.Close();
        }
        Check(originalBattles.All(b => shown.Any(r => r.AttackerId == b.AttackerId && r.DefenderId == b.DefenderId)), "original AI completion battle facts survive intervening Save and route after result dismissal");
        Check(mainGame.Session.OpenTacticalBattle is null, "the battle is over");
        Check(mainGame.Session.State.Calendar.TurnIndex > turnBefore, "the turn goes on after the battle");
        Free(mainGame);
        SettingsScreen.SetBattlePauses(0, 0);
    }

    // ---- Done-when 3: instant battles are shown as before ----

    private void CheckInstantBattlesAreShownAsBefore()
    {
        // A siege.
        var siege = Build(ToySiegeSession());
        siege.SubmitForCheck("besiege-city north-army-1 meridia");
        Check(
            siege.ActiveOverlay is BattleResultScreen,
            $"a siege shows the battle-result window as on main (got {siege.ActiveOverlay?.GetType().Name ?? "null"})");
        Check(siege.Session.OpenTacticalBattle is null, "a siege opens no battle screen");
        Free(siege);

        // An AI-versus-AI battle played within an end turn.
        var aiVsAi = Build(ClassicalAiVersusAiSession());
        aiVsAi.SubmitForCheck("end");
        if (aiVsAi.StrategicTravel.IsActive)
            Check(aiVsAi.ActiveOverlay is null, "strategic AI travel withholds the result until arrival");
        aiVsAi.StrategicTravel.Skip();
        var battles = aiVsAi.Session.LastBattles;
        var neutral = battles.Where(b => aiVsAi.Session.State.NationById(b.AttackerNationId)?.Control == SeatControl.Ai
                                         && aiVsAi.Session.State.NationById(b.DefenderNationId)?.Control == SeatControl.Ai).ToList();
        if (Check(neutral.Count > 0, $"the scripted AI phase fights an AI-versus-AI battle ({battles.Count} battle(s) in all)"))
        {
            var shown = new List<BattleResultViewModel>();
            while (aiVsAi.ActiveOverlay is BattleResultScreen window)
            {
                shown.Add(window.Result);
                window.Close();
            }

            Check(
                neutral.All(n => shown.Any(w => w.AttackerId == n.AttackerId && w.DefenderId == n.DefenderId)),
                "the AI-versus-AI battle is shown in the battle-result window as on main");
        }

        Check(aiVsAi.Session.OpenTacticalBattle is null, "an AI-versus-AI battle opens no battle screen");
        Free(aiVsAi);
    }

    // ---- fixtures ----

    private static GameSession ClassicalSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var ruleset = resolved.Ruleset with { Flags = resolved.Ruleset.Flags with { NewGameTurnOrder = NewGameTurnOrderPolicy.Fixed } };
        var armies = resolved.World.StartingArmies.Select(a => a.Id == "army-3" ? a with { X = 101, Y = 37, Moves = 8 } : a).ToList();
        var world = resolved.World with { StartingArmies = ValueList.From(armies) };
        return new GameSession(world, ruleset, resolved.Scenario, null, "rome") { HumanBattles = HumanBattleMode.Tactical };
    }

    // Designed synthetic presentation fixture: real immutable nation identities/palettes on a small public world.
    // All gameplay still comes from the ordinary producer; names deliberately contain literal rich-text tags.
    private static GameSession ReadabilitySession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var classical = GameDataContext.Repository.Resolve("classical-mediterranean");
        string Owner(string id) => id == "north" ? "seleucid" : "ptolemaic";
        var ids = ValueList.Of("seleucid", "ptolemaic");
        var world = toy.World with
        {
            Nations = ValueList.From(toy.World.Nations.Select(n => classical.World.NationById(Owner(n.Id))! with
                { Name = "Renamed [b] faction", CapitalCityId = n.CapitalCityId })),
            Cities = ValueList.From(toy.World.Cities.Select(c => c with { Owner = Owner(c.Owner), Allegiance = Owner(c.Allegiance) })),
            StartingArmies = ValueList.Of(
                new StartingArmy("north-army-1", "seleucid", 3, 2, 68, 100, 500, 5,
                    ValueList.Of(new UnitSlot(0, "light_infantry", 9000, 6, "duplicate [color=red]"), new UnitSlot(1, "archers", 3000, 7, "duplicate [color=red]"))),
                new StartingArmy("south-army-1", "ptolemaic", 4, 2, 59, 40, 60, 5,
                    ValueList.Of(new UnitSlot(0, "heavy_infantry", 600, 6, "duplicate [color=red]")))),
            StartingFleets = ValueList<StartingFleet>.Empty,
            TurnOrder = ids,
            StartingRelations = DiplomaticRelations.Uniform(ids, toy.Ruleset.Diplomacy.StateCodes.War),
        };
        var scenario = toy.Scenario with { Seats = ValueList.From(toy.Scenario.Seats.Select(seat => seat with { Nation = Owner(seat.Nation) })) };
        return new GameSession(world, toy.Ruleset, scenario, seedOverride: 1, humanSeatNationId: "seleucid") { HumanBattles = HumanBattleMode.Tactical };
    }

    private static GameSession ToyAiAttackWithNeutralBattleSession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var east = toy.World.NationById("south")! with { Id = "east", Name = "Eastern League", CapitalCityId = "orient" };
        var nations = ValueList.Of("north", "south", "east");
        var codes = toy.Ruleset.Diplomacy.StateCodes;
        StartingArmy Army(string id, string nation, int x, int y, int troops) => new(id, nation, x, y,
            Morale: 68, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(0, nation == "south" ? "heavy_infantry" : "light_infantry", troops, 6, id)));
        var world = toy.World with
        {
            Nations = ValueList.Of(toy.World.NationById("north")!, toy.World.NationById("south")!, east),
            Cities = ValueList.From(toy.World.Cities.Concat(new[]
            {
                toy.World.CityById("meridia")! with { Id = "orient", Name = "Orient", X = 7, Y = 0, Owner = "east", Allegiance = "east" },
            })),
            StartingArmies = ValueList.Of(Army("north-army-1", "north", 3, 2, 400), Army("south-army-1", "south", 4, 2, 6000),
                Army("south-army-2", "south", 6, 5, 6000), Army("east-army-1", "east", 6, 4, 400)),
            StartingFleets = ValueList<StartingFleet>.Empty,
            TurnOrder = nations,
            StartingRelations = DiplomaticRelations.Uniform(nations, codes.Peace).WithRelation("south", "east", codes.War),
        };
        var scenario = toy.Scenario with { Seats = ValueList.From(toy.Scenario.Seats.Concat(new[] { toy.Scenario.Seats[1] with { Nation = "east" } })) };
        return new GameSession(world, toy.Ruleset, scenario, seedOverride: 1) { HumanBattles = HumanBattleMode.Tactical };
    }

    private static GameSession ToyAiAttackSession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with
        {
            X = 4,
            Y = 2,
            Units = ValueList.Of(new UnitSlot(0, "heavy_infantry", Troops: 50_000, Quality: 6, Name: "1st Guards Battalion")),
        };
        var world = toy.World with
        {
            StartingArmies = ValueList.From(toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a)),
        };
        return new GameSession(world, toy.Ruleset, toy.Scenario) { HumanBattles = HumanBattleMode.Tactical };
    }

    private static GameSession ToySiegeSession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var armies = new List<StartingArmy>
        {
            new("north-army-1", "north", X: 3, Y: 3, Morale: 68, Money: 0, SupplyTons: 0, Moves: 5,
                Units: ValueList.Of(new UnitSlot(0, "light_infantry", Troops: 15_000, Quality: 6, Name: "1st Foot"))),
            new("south-army-1", "south", X: 6, Y: 5, Morale: 59, Money: 0, SupplyTons: 0, Moves: 5,
                Units: ValueList.Of(new UnitSlot(0, "heavy_infantry", Troops: 600, Quality: 6, Name: "unit"))),
        };
        var world = toy.World with { StartingArmies = ValueList.From(armies) };
        return new GameSession(world, toy.Ruleset, toy.Scenario) { HumanBattles = HumanBattleMode.Tactical };
    }

    private const int PocketX = 67;
    private const int PocketY = 4;
    private const ulong Seed = 20261003UL;

    /// <summary>
    /// Rome the only human seat; carthage at war with seleucid, one strong carthage army beside one weak seleucid army
    /// in an isolated all-land pocket, so carthage's own turn attacks it: a battle with no human side.
    /// </summary>
    private static GameSession ClassicalAiVersusAiSession()
    {
        var resolved = GameDataContext.Repository.Resolve("example-classical-improved");
        var warCode = resolved.Ruleset.Diplomacy.StateCodes.War;
        var scenario = resolved.Scenario with
        {
            Seats = ValueList.From(resolved.Scenario.Seats.Select(seat => seat with
            {
                Control = seat.Nation == "rome" ? SeatControl.Human : SeatControl.Ai,
            })),
        };
        var relations = resolved.World.StartingRelations!
            .WithRelation("carthage", "seleucid", warCode);

        var armies = resolved.World.StartingArmies
            .Select(army => army.Id switch
            {
                "army-4" => army with
                {
                    X = PocketX,
                    Y = PocketY,
                    Morale = 45,
                    Units = ValueList.Of(new UnitSlot(0, "light_infantry", Troops: 5000, Quality: 4, Name: "Weak Militia")),
                },
                "army-2" => army with
                {
                    X = PocketX + 1,
                    Y = PocketY,
                    Morale = 60,
                    Units = ValueList.Of(new UnitSlot(0, "light_infantry", Troops: 10_000, Quality: 6, Name: "Check Warband")),
                },
                _ => army,
            })
            .ToList();

        var world = resolved.World with { StartingRelations = relations, StartingArmies = ValueList.From(armies) };
        return new GameSession(world, resolved.Ruleset, scenario, seedOverride: Seed) { HumanBattles = HumanBattleMode.Tactical };
    }

    private MainGameScreen Build(GameSession session)
    {
        var mainGame = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        mainGame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(mainGame);
        return mainGame;
    }

    private void Free(MainGameScreen mainGame)
    {
        RemoveChild(mainGame);
        mainGame.QueueFree();
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
