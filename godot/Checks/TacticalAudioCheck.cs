using Godot;
using IC2.Engine.Assets;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Audio;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>Synthetic public toy scenario; exercises actual MainGameScreen wiring without private fixtures.</summary>
public partial class TacticalAudioCheck : Node
{
    private bool _ok = true;
    private readonly List<Node> _owned = new();
    private StreamWriter? _receipt;
    private static readonly string[] Keys = [AssetKeys.SfxBattleArrows, AssetKeys.SfxBattleJavelin, AssetKeys.SfxBattleMelee];

    public override async void _Ready()
    {
        var sound = SettingsScreen.SoundEnabled;
        var pack = SettingsScreen.SelectedPackId;
        var shot = SettingsScreen.BattleShotDelaySeconds;
        var attack = SettingsScreen.BattleAttackDelaySeconds;
        try
        {
            var hearing = OS.GetCmdlineUserArgs().Contains("--hear");
            if (hearing)
            {
                var path = Path.Combine(GameDataContext.RepositoryRoot, "rendered", "t183-hearing-receipts.log");
                _receipt = new StreamWriter(path, append: true) { AutoFlush = true };
                Receipt("BEGIN hearing " + File.ReadAllText(Path.Combine(GameDataContext.RepositoryRoot, "rendered", "hearing-head.txt")).Trim());
            }
            foreach (var selected in new[] { "authored", "placeholder" })
            {
                var requestedPack = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--hear-pack="));
                if (hearing && requestedPack is not null && requestedPack != "--hear-pack=" + selected) continue;
                SetPack(selected);
                if (hearing)
                {
                    await CheckProduction(selected, 0, false, true, true, "zero-delay");
                    await CheckProduction(selected, 1, false, true, true, "normal");
                    await CheckProduction(selected, 1, false, true, true, "default-pace", attackDelay: 2);
                    await CheckProduction(selected, 1, false, true, true, "advance");
                    await CheckProduction(selected, 1, true, true, true, "skip");
                    await CheckProduction(selected, 1, true, true, true, "finish");
                    await CheckProduction(selected, 1, false, false, true, "mute");
                    await CheckBattleBoundaryRetirement(hearing: true);
                    continue;
                }
                await CheckProduction(selected, 0, false, true);
                await CheckProduction(selected, 1, false, true);
                await CheckProduction(selected, 1, false, true, attackDelay: 2);
                await CheckProduction(selected, 0.3, false, true, attackDelay: 2);
                await CheckProduction(selected, 1, true, true);
                await CheckProduction(selected, 1, true, false);
                await CheckQueue(selected);
            }
            if (!hearing) { await CheckBattleBoundaryRetirement(); CheckMissing(); }
        }
        catch (Exception ex) { GD.PrintErr($"TacticalAudioCheck: {ex}"); _ok = false; }
        finally
        {
            SettingsScreen.SetSoundEnabledForCheck(sound);
            SettingsScreen.SetBattlePauses(shot, attack);
            // Restore the exact nullable selection; no production settings API is added for a check.
            SettingsScreen.SetPackForCheck(pack);
            foreach (var node in _owned.Where(GodotObject.IsInstanceValid))
            {
                Stop(node);
                node.GetParent()?.RemoveChild(node);
                node.QueueFree();
            }
            _owned.Clear();
            Receipt("END hearing/settings restored");
            _receipt?.Dispose();
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        var code = _ok ? 0 : 1;
        GD.Print($"TacticalAudioCheck: exiting with code {code}.");
        CheckExit.Quit(this, code);
    }

    private async Task CheckProduction(string pack, double delay, bool finish, bool enabled, bool hearing = false, string hearingMode = "", double? attackDelay = null)
    {
        SettingsScreen.SetSoundEnabledForCheck(enabled);
        SettingsScreen.SetBattlePauses(delay, attackDelay ?? delay);
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var world = toy.World with { StartingArmies = ValueList.Of(
            new StartingArmy("north-army-1", "north", 3, 2, 68, 100, 500, 5,
                ValueList.Of(new UnitSlot(0, "light_infantry", 9000, 6, "duplicate"), new UnitSlot(1, "archers", 3000, 7, "duplicate"))),
            new StartingArmy("south-army-1", "south", 4, 2, 59, 40, 60, 5,
                ValueList.Of(new UnitSlot(0, "heavy_infantry", 600, 6, "duplicate")))) };
        var session = new GameSession(world, toy.Ruleset, toy.Scenario) { HumanBattles = HumanBattleMode.Tactical };
        var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        main.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(main); _owned.Add(main);
        main.SubmitForCheck("attack-army north-army-1 south-army-1");
        var screen = main.ActiveOverlay as TacticalBattleScreen;
        Require(screen is not null, "production attack opens tactical overlay");
        if (screen is null) return;
        screen.SkipPlayback();
        _ = main.SoundPlayerForCheck.QueuedForCheck;
        if (hearing)
        {
            var description = $"{pack}: {(enabled ? "Sound ON" : "Sound OFF — expect silence")}, shot {delay} / melee {attackDelay ?? delay}, {hearingMode}. Expected: arrows / javelin / melee from displayed exchanges.";
            Receipt(description);
            var layer = new CanvasLayer { Layer = 100 };
            var label = new Label { Text = description, Position = new Vector2(12, 4),
                Size = new Vector2(1000, 70), AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeColorOverride("font_color", Colors.Yellow);
            layer.AddChild(label); main.AddChild(layer);
            screen.ExchangeEntered += frame => Receipt($"DISPLAY {frame.BattleId}/{frame.FrameId} {frame.Kind} actor={frame.Actor?.TypeId} cue={TacticalSoundCues.ForExchange(frame, session.Ruleset)} sound={SettingsScreen.SoundEnabled}");
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        }
        screen.PressToolbarButton("computer_general");
        var frames = session.LastTacticalTimeline;
        var expected = frames.Where(f => f.Event is TacticalShotEvent or TacticalMeleeEvent)
            .Select(f => TacticalSoundCues.ForExchange(f, session.Ruleset)!).ToArray();
        if (hearing) Receipt("EXPECTED ordered keys: " + string.Join(",", expected));
        Require(expected.Length > 0 && Keys.All(expected.Contains), "producer supplies all three actual exchange types");
        if (hearing && !finish && delay > 0)
        {
            var deadline = Time.GetTicksMsec() + 120000;
            while (screen.IsPlayingBack && Time.GetTicksMsec() < deadline)
            {
                if (hearingMode == "advance") screen.AdvancePlayback(0.25);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Require(!screen.IsPlayingBack, "windowed traversal completes within watchdog");
        }
        else if (finish)
        {
            if (hearing) await ToSignal(GetTree().CreateTimer(hearingMode == "skip" ? 0.15 : 0.35), SceneTreeTimer.SignalName.Timeout);
            screen.AdvancePlayback(0.25); screen.SkipPlayback();
        }
        else for (var i = 0; screen.IsPlayingBack && i < 100000; i++) screen.AdvancePlayback(0.1);
        screen.AdvancePlayback(100); screen.SkipPlayback(); screen.SkipPlayback();
        Require(!screen.IsPlayingBack && main.ActiveOverlay is BattleEndedScreen && session.OpenTacticalBattle is null,
            "terminal null-view finishes before result");
        var queued = main.SoundPlayerForCheck.QueuedForCheck;
        Require(queued.SequenceEqual(enabled ? expected : Array.Empty<string>()),
            $"{pack} delay {delay} finish {finish} sound {enabled}: production ordered exactly-once cues, no generic duplicate");
        foreach (var key in Keys) Require(main.SoundPlayerForCheck.StreamForCheck(key)?.Data.Any(sample => sample != 0) == true,
            $"{pack} loads nonzero {key} WAV PCM into the production player");
        if (hearing)
        {
            var log = new List<SoundPlayer.PlaybackLogEntry>();
            var deadline = Time.GetTicksMsec() + 60000;
            do
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                log.AddRange(main.SoundPlayerForCheck.PlaybackLogForCheck);
            } while (enabled && log.Count(e => !e.Started) < expected.Length && Time.GetTicksMsec() < deadline);
            foreach (var entry in log) Receipt($"AUDIO {entry.Key} {(entry.Started ? "start" : "finish")} tick={entry.TickMs}");
            var queueReceipts = main.SoundPlayerForCheck.TacticalReceiptsForCheck;
            foreach (var entry in queueReceipts) Receipt($"QUEUE sequence={entry.Sequence} battle={entry.Cue.BattleId} frame={entry.Cue.FrameId} key={entry.Key} {entry.Action} active={entry.WasActive} reason={entry.Reason} tick={entry.TickMs}");
            Require(log.Where(e => e.Started).Select(e => e.Key).SequenceEqual(enabled ? expected : Array.Empty<string>()), "windowed loaded WAV start order matches displayed exchange order");
            Require(!enabled || log.Count(e => !e.Started) == expected.Length, "windowed loaded WAVs complete before retirement");
            Require(queueReceipts.All(r => r.Action != "cancelled"), "single-battle hearing run cancels no accepted clips");
            CheckAccounting(queueReceipts, main.SoundPlayerForCheck.TacticalOutstandingForCheck, true, "windowed complete FIFO");
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        }
        var player = main.SoundPlayerForCheck;
        var pending = player.TacticalReceiptsForCheck;
        if (!hearing)
        {
            var expectedCues = frames.Where(f => f.Event is TacticalShotEvent or TacticalMeleeEvent)
                .Select(f => new TacticalAudioCue(f.BattleId, f.FrameId, TacticalSoundCues.ForExchange(f, session.Ruleset)!));
            Require(pending.Where(r => r.Action == "accepted").Select(r => r.Cue)
                .SequenceEqual(enabled ? expectedCues : Array.Empty<TacticalAudioCue>()), "production receipts retain stable battle/exchange identities");
            CheckAccounting(pending, player.TacticalOutstandingForCheck, false, "displayed exchanges pending/active");
        }
        RemoveChild(main);
        CheckAccounting(pending.Concat(player.TacticalReceiptsForCheck).ToArray(), player.TacticalOutstandingForCheck, true, "production disposal");
        Stop(main); main.QueueFree();
    }

    private async Task CheckQueue(string pack)
    {
        SettingsScreen.SetSoundEnabledForCheck(true);
        var player = new SoundPlayer(Path.Combine(GameDataContext.RepositoryRoot, "assets", "packs", pack))
            { SoundEnabled = () => SettingsScreen.SoundEnabled };
        AddChild(player); _owned.Add(player);
        // Short synthetic PCM makes headless completion bounded; installed WAV loading is checked above.
        foreach (var key in Keys) player.BindStreamForTest(key, new AudioStreamWav
            { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 44100, Data = new byte[8820] });
        var sequence = new[] { Keys[0], Keys[0], Keys[1], Keys[2] };
        var timer = System.Diagnostics.Stopwatch.StartNew();
        foreach (var key in sequence) player.PlayTactical(key);
        Require(timer.ElapsedMilliseconds < 250, "shared tactical enqueue is nonblocking");
        var log = new List<SoundPlayer.PlaybackLogEntry>();
        var deadline = Time.GetTicksMsec() + 8000;
        do
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            log.AddRange(player.PlaybackLogForCheck);
        } while (log.Count(e => !e.Started) < sequence.Length && Time.GetTicksMsec() < deadline);
        Require(log.Where(e => e.Started).Select(e => e.Key).SequenceEqual(sequence)
            && log.Count == sequence.Length * 2 && log.Select((e, i) => e.Started == (i % 2 == 0)).All(x => x),
            "identical and mixed cues complete FIFO without restart or drop: " + string.Join(",", log.Select(e => e.Key + (e.Started ? "+" : "-"))));
        CheckAccounting(player.TacticalReceiptsForCheck, player.TacticalOutstandingForCheck, true, "identical/mixed completed FIFO");
        _ = player.QueuedForCheck;
        player.PlayTactical(Keys[0]); player.PlayTactical(Keys[1]);
        player.ClearTactical();
        _ = player.PlaybackLogForCheck;
        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);
        Require(!player.PlaybackLogForCheck.Any(e => e.Started), "new battle retirement clears prior-battle queued cues");
        CheckCancellation(player.TacticalReceiptsForCheck, player, "battle-changed");
        _ = player.QueuedForCheck;
        player.PlayTactical(Keys[0]); player.PlayTactical(Keys[0]);
        _ = player.QueuedForCheck; _ = player.PlaybackLogForCheck;
        SettingsScreen.SetSoundEnabledForCheck(false);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        player.PlayTactical(Keys[1]);
        Require(player.QueuedForCheck.Count == 0 && !Keys.Any(player.IsPlayingForTest), "mute clears pending and audible tactical playback");
        SettingsScreen.SetSoundEnabledForCheck(true);
        await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
        Require(!player.PlaybackLogForCheck.Any(e => e.Started), "unmute does not replay stale queue");
        CheckCancellation(player.TacticalReceiptsForCheck, player, "muted");
        player.PlayTactical("missing.key");
        Require(player.QueuedForCheck.Count == 0, "unresolved key is silent");
        player.PlayTactical(Keys[0]); player.PlayTactical(Keys[1]);
        RemoveChild(player);
        CheckCancellation(player.TacticalReceiptsForCheck, player, "disposed");
        Stop(player); player.QueueFree();
    }

    private void CheckMissing()
    {
        var path = Path.Combine(GameDataContext.RepositoryRoot, "rendered", "t183-missing-pack");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "manifest.json"), "{\"schemaVersion\":1,\"name\":\"missing\",\"assets\":{\"sfx.battle_arrows\":\"absent.wav\"}}");
        try
        {
            var player = new SoundPlayer(path); AddChild(player); _owned.Add(player);
            foreach (var key in Keys) player.PlayTactical(key);
            Require(player.QueuedForCheck.Count == 0, "missing keys and missing WAV files safely no-op");
            Stop(player); RemoveChild(player); player.QueueFree();
        }
        finally { File.Delete(Path.Combine(path, "manifest.json")); Directory.Delete(path); }
    }

    private async Task CheckBattleBoundaryRetirement(bool hearing = false)
    {
        SettingsScreen.SetSoundEnabledForCheck(true);
        SettingsScreen.SetBattlePauses(0, 0);
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        StartingArmy Army(string id, string nation, int x, int y, int troops) => new(id, nation, x, y,
            Morale: nation == "north" ? 68 : 59, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(0, nation == "north" ? "light_infantry" : "heavy_infantry", troops, 6, "duplicate")));
        var world = toy.World with { StartingArmies = ValueList.Of(
            Army("north-army-1", "north", 3, 2, 400), Army("north-army-2", "north", 6, 4, 400),
            Army("south-army-1", "south", 4, 2, 6000), Army("south-army-2", "south", 6, 5, 6000)) };
        var main = new MainGameScreen { Session = new GameSession(world, toy.Ruleset, toy.Scenario)
            { HumanBattles = HumanBattleMode.Tactical }, RepositoryRoot = GameDataContext.RepositoryRoot };
        main.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(main); _owned.Add(main);
        main.SubmitForCheck("declare-war south"); main.SubmitForCheck("end");
        var first = main.ActiveOverlay as TacticalBattleScreen;
        Require(first is not null, "back-to-back production AI lap opens first battlefield");
        if (first is null) return;
        var battleId = first.DisplayedFrame!.BattleId;
        if (hearing)
        {
            var text = $"{SettingsScreen.SelectedPackId}: immediate battle handoff — first clip interrupted, pending clips cancelled; cancellations are not completed sounds.";
            Receipt(text);
            var layer = new CanvasLayer { Layer = 100 };
            layer.AddChild(new Label { Text = text, Position = new Vector2(12, 4), Size = new Vector2(1000, 70),
                AutowrapMode = TextServer.AutowrapMode.WordSmart });
            main.AddChild(layer);
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        }
        _ = main.SoundPlayerForCheck.TacticalReceiptsForCheck;
        first.PressToolbarButton("computer_general");
        first.SkipPlayback();
        // Hearing helper only: allow the first clip to become audible before simulating Close.
        // The production handoff itself never waits; the headless proof closes in the same frame.
        if (hearing) await ToSignal(GetTree().CreateTimer(0.15), SceneTreeTimer.SignalName.Timeout);
        var frames = main.Session.LastTacticalTimeline.Where(f => f.BattleId == battleId
            && f.Event is TacticalShotEvent or TacticalMeleeEvent).ToArray();
        var before = main.SoundPlayerForCheck.TacticalReceiptsForCheck;
        var accepted = before.Where(r => r.Action == "accepted").ToArray();
        Require(accepted.Select(r => r.Cue).SequenceEqual(frames.Select(f =>
                new TacticalAudioCue(f.BattleId, f.FrameId, TacticalSoundCues.ForExchange(f, toy.Ruleset)!))),
            "boundary receipt sequence maps one-to-one to producer exchange identities");
        CheckAccounting(before, main.SoundPlayerForCheck.TacticalOutstandingForCheck, false, "terminal pending/active before Close");
        if (main.ActiveOverlay is not BattleEndedScreen result) { Require(false, "first result precedes next battlefield"); return; }
        var handoff = System.Diagnostics.Stopwatch.StartNew();
        result.Close();
        Require(handoff.ElapsedMilliseconds < 250, "immediate new battlefield never waits for audio completion");
        var after = main.SoundPlayerForCheck.TacticalReceiptsForCheck;
        var records = before.Concat(after).ToArray();
        if (hearing)
        {
            foreach (var r in records) Receipt($"BOUNDARY sequence={r.Sequence} battle={r.Cue.BattleId} frame={r.Cue.FrameId} key={r.Key} {r.Action} active={r.WasActive} reason={r.Reason}");
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
            var later = main.SoundPlayerForCheck.TacticalReceiptsForCheck;
            Require(!later.Any(r => r.Cue.BattleId == battleId), "windowed cancelled battle has no stale playback receipt");
            records = records.Concat(later).ToArray();
        }
        CheckAccounting(records.Where(r => r.Cue.BattleId == battleId).ToArray(),
            main.SoundPlayerForCheck.TacticalOutstandingForCheck.Where(r => r.Cue.BattleId == battleId).ToArray(), true,
            "back-to-back old battle final cancellation");
        Require(after.Where(r => r.Cue.BattleId == battleId).All(r => r.Action == "cancelled" && r.Reason == "battle-changed"),
            "old battle cancellation has explicit boundary reason");
        Require(main.ActiveOverlay is TacticalBattleScreen next && next.DisplayedFrame?.BattleId != battleId,
            "result dismissal opens distinct next production battlefield");
        foreach (var (receipt, index) in accepted.Select((receipt, index) => (receipt, index)))
        {
            var own = records.Where(r => r.Sequence == receipt.Sequence).ToArray();
            var completed = own.Count(r => r.Action == "finished");
            var retired = own.Count(r => r.Action == "cancelled");
            Require(completed + retired == 1, "each accepted prior-battle clip explicitly finishes or cancels once");
            GD.Print($"BOUNDARY: {battleId}/{frames[index].FrameId} sequence={receipt.Sequence} key={receipt.Key} "
                + $"started={own.Count(r => r.Action == "started")} finished={completed} cancelled={retired} "
                + $"active={own.Last().WasActive} reason={own.Last().Reason}");
        }
        // Approved Plan1240 permits this cancellation; it is never audible completion.
        GD.Print($"BOUNDARY: accepted={accepted.Length}, started={before.Count(r => r.Action == "started")}, "
            + $"finished={before.Count(r => r.Action == "finished")}, cancelled={after.Count(r => r.Action == "cancelled")}");
        RemoveChild(main);
        CheckAccounting(records.Concat(main.SoundPlayerForCheck.TacticalReceiptsForCheck).ToArray(),
            main.SoundPlayerForCheck.TacticalOutstandingForCheck, true, "back-to-back final disposal");
        Stop(main); main.QueueFree();
    }

    private void CheckCancellation(IReadOnlyList<SoundPlayer.TacticalAudioReceipt> records, SoundPlayer player, string reason)
    {
        CheckAccounting(records, player.TacticalOutstandingForCheck, true, reason + " cancellation");
        var cancelled = records.Where(r => r.Action == "cancelled").ToArray();
        Require(cancelled.Length == 2 && cancelled.All(r => r.Reason == reason)
            && cancelled.Count(r => r.WasActive) == 1, reason + " distinguishes one active interruption and one pending cancellation");
    }

    private void CheckAccounting(IReadOnlyList<SoundPlayer.TacticalAudioReceipt> records,
        IReadOnlyList<SoundPlayer.TacticalAudioReceipt> outstanding, bool final, string description)
    {
        var accepted = records.Where(r => r.Action == "accepted").ToArray();
        var valid = accepted.Select(r => r.Sequence).Distinct().Count() == accepted.Length
            && accepted.Select(r => (r.Cue.BattleId, r.Cue.FrameId)).Distinct().Count() == accepted.Length
            && records.All(r => accepted.Any(a => a.Sequence == r.Sequence && a.Cue == r.Cue))
            && outstanding.All(r => accepted.Any(a => a.Sequence == r.Sequence && a.Cue == r.Cue));
        foreach (var cue in accepted)
        {
            var own = records.Where(r => r.Sequence == cue.Sequence).ToArray();
            var starts = own.Count(r => r.Action == "started");
            var finishes = own.Count(r => r.Action == "finished");
            var cancelled = own.Where(r => r.Action == "cancelled").ToArray();
            var pending = outstanding.Where(r => r.Sequence == cue.Sequence).ToArray();
            valid &= own[0].Action == "accepted" && starts <= 1 && finishes <= 1 && cancelled.Length <= 1
                && finishes + cancelled.Length <= 1 && (finishes == 0 || starts == 1)
                && own.All(r => r.Action is "accepted" or "started" or "finished" or "cancelled");
            if (cancelled.Length == 1)
                valid &= !string.IsNullOrEmpty(cancelled[0].Reason) && finishes == 0
                    && starts == (cancelled[0].WasActive ? 1 : 0);
            var terminal = finishes + cancelled.Length;
            if (terminal == 0)
                valid &= !final && pending.Length == 1 && pending[0].Action == (starts == 1 ? "active" : "pending")
                    && pending[0].WasActive == (starts == 1);
            else valid &= pending.Length == 0 && own[^1].Action is "finished" or "cancelled";
        }
        Require(valid && (!final || outstanding.Count == 0), description + ": exact identity accounting, pending/active or one finished/cancelled outcome");
    }

    private void SetPack(string pack)
    {
        var settings = new SettingsScreen(); AddChild(settings);
        var picker = Descendants(settings).OfType<OptionButton>().First(o => o.Name == SettingsScreen.PackPickerName);
        var index = Enumerable.Range(0, picker.ItemCount).Single(i => picker.GetItemText(i) == pack);
        picker.Select(index); picker.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        RemoveChild(settings); settings.QueueFree();
    }
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private static void Stop(Node node)
    {
        if (node is SoundPlayer sound) { sound.SoundEnabled = null; sound.Muted = true; }
        if (node is AudioStreamPlayer player) { player.Stop(); player.Stream = null; }
        foreach (var child in node.GetChildren()) Stop(child);
    }
    private void Require(bool condition, string text)
    {
        _ok &= condition;
        var line = $"{(condition ? "PASS" : "FAIL")}: {text}";
        GD.Print(line); _receipt?.WriteLine(line);
    }
    private void Receipt(string text) { if (_receipt is null) return; _receipt.WriteLine(text); GD.Print("HEARING: " + text); }
}
