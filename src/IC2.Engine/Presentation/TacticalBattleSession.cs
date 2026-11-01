using System.Globalization;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Strength;

namespace IC2.Engine.Presentation;

/// <summary>
/// How the session fights a battle with a human side (T125): <c>FUN_0044AEE4</c>'s branch.
/// </summary>
/// <remarks>
/// A session switch, never a rule: it is not saved and no ruleset names it. A save that holds an open battle
/// reopens it whatever the switch is, because the battle is already being fought.
/// </remarks>
public enum HumanBattleMode
{
    /// <summary>The human's field battle is resolved at once by the instant resolver, exactly as before T125 (the default).</summary>
    Instant = 0,

    /// <summary>A field battle with a human side is fought on the board, order by order.</summary>
    Tactical = 1,
}

/// <summary>
/// The open battle as a UI draws it: the whole board, the 40 slots and who is to move. A snapshot; it changes
/// no rule and the next order replaces it.
/// </summary>
/// <param name="AttackerArmyId">The attacking army.</param>
/// <param name="DefenderArmyId">The defending army.</param>
/// <param name="AttackerNationId">The attacker's nation.</param>
/// <param name="DefenderNationId">The defender's nation.</param>
/// <param name="SideToMove">0 attacker, 1 defender.</param>
/// <param name="Placed">Whether placement is over.</param>
/// <param name="Counter">The half-round counter.</param>
/// <param name="IsOver">Whether the battle has ended and awaits its write-back.</param>
/// <param name="UnitTypeIds">The unit type ids, in ruleset order (a slot's <see cref="TacticalSlot.Type"/> indexes it).</param>
/// <param name="Slots">The 40 slots, attacker's first.</param>
/// <param name="Grid">The 14 × 12 icon codes, indexed <c>x × boardHeight + y</c>.</param>
/// <param name="ComputerControlled">Per side, whether the nation is computer-controlled.</param>
/// <param name="ComputerGeneral">Per side, whether <em>Computer general</em> is on.</param>
/// <param name="Selected">The selected slot, or −1.</param>
public sealed record TacticalBattleView(
    string AttackerArmyId,
    string DefenderArmyId,
    string AttackerNationId,
    string DefenderNationId,
    int SideToMove,
    bool Placed,
    int Counter,
    bool IsOver,
    ValueList<string> UnitTypeIds,
    ValueList<TacticalSlot> Slots,
    ValueList<int> Grid,
    ValueList<bool> ComputerControlled,
    ValueList<bool> ComputerGeneral,
    int Selected);

/// <summary>The outcome of one human order of the battle: whether it was carried out, and the lines that say what happened.</summary>
/// <param name="Accepted">Whether the order was carried out.</param>
/// <param name="Lines">What to print.</param>
public sealed record TacticalBattleOrderResult(bool Accepted, IReadOnlyList<string> Lines)
{
    /// <summary>The immutable batch for this order only, including automatic half-rounds and terminal frames.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ValueList<TacticalBattleFrame> Timeline { get; init; } = ValueList<TacticalBattleFrame>.Empty;
}

/// <summary>
/// A concluded battle: the game after the write-back, the <em>Battle ended</em> window's contents and the
/// events the write-back and the peace dialog raised.
/// </summary>
/// <param name="State">The game after the write-back; <see langword="null"/> when the write-back was refused.</param>
/// <param name="Report">The report; <see langword="null"/> when the write-back was refused.</param>
/// <param name="PeaceOffered">Whether the post-battle peace treaty offer fired.</param>
/// <param name="PeaceDrawn">Whether the peace dialog's <c>Random(5)</c> was drawn (only when the three other tests pass).</param>
/// <param name="Events">The domain events, in order (the news event, then the offer).</param>
/// <param name="Rejection">Why the write-back was refused, or <see langword="null"/>.</param>
public sealed record TacticalBattleConclusion(
    GameState? State,
    TacticalBattleReport? Report,
    bool PeaceOffered,
    bool PeaceDrawn,
    IReadOnlyList<DomainEvent> Events,
    string? Rejection);

/// <summary>
/// One tactical battle fought through the session (T125): its state (T123), its computer-played half-rounds
/// (T124), its draw stream, the human's orders, and its end.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: code: <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c> §"The original's
/// instant battle resolver"; <c>game-design.md</c>, "When the battle screen opens"; designed: the stream, the
/// verbs]</strong>. A battle with a human side is fought on the board; the session opens it from a legal
/// <c>attack-army</c>, the human's orders mutate it one by one, and the computer's half-rounds run at once
/// through <see cref="ComputerGeneral"/>. When it ends, <see cref="Conclude"/> writes it back through
/// <see cref="ApplyTacticalBattleCommandHandler"/> and takes the peace dialog's draw from the same stream.
/// </para>
/// <para>
/// <strong>The draws.</strong> One <see cref="IRng"/> stream per battle, derived from the game state's seed and
/// <see cref="StreamKey"/>. The state's seed does not move while the battle is open (no command is dispatched),
/// and the stream's position is saved with the battle, so a reload is deterministic.
/// </para>
/// </remarks>
public sealed class TacticalBattleSession
{
    private readonly TacticalContext _context;
    private TacticalBattleTimeline _timeline;
    private readonly ITacticalGeneral _general = new ComputerGeneral();
    private readonly SplitMix64Rng _rng;
    private readonly string _attackerNationId;
    private readonly string _defenderNationId;
    private readonly IBattleDraws _draws;

    private TacticalBattleSession(
        TacticalContext context,
        TacticalBattleState battle,
        SplitMix64Rng rng,
        string attackerNationId,
        string defenderNationId)
    {
        // Keep the established five-argument construction seam used by deterministic golden replays.
        // Open/Reopen hand off their already-running collector before exposing the session.
        _timeline = new TacticalBattleTimeline(context, $"{battle.AttackerArmyId}:{battle.DefenderArmyId}",
            attackerNationId, defenderNationId);
        _context = context;
        Battle = battle;
        _rng = rng;
        _draws = new RngBattleDraws(rng);
        _attackerNationId = attackerNationId;
        _defenderNationId = defenderNationId;
    }

    /// <summary>The battle's state.</summary>
    public TacticalBattleState Battle { get; private set; }

    /// <summary>Whether the battle has ended and awaits <see cref="Conclude"/>.</summary>
    public bool IsOver => Battle.IsOver;

    /// <summary>The raw state of the battle's draw stream, the position a save records.</summary>
    public ulong DrawState => _rng.State;

    /// <summary>Exact in-memory checkpoint; unlike native faithful saves it retains the current arena and log.</summary>
    public TacticalBattleCheckpoint CaptureCheckpoint() => new(Battle, _rng.State);

    /// <summary>Restore without setup, automatic orders or random draws, using a fresh presentation epoch.</summary>
    public TacticalBattleSession RestoreCheckpoint(TacticalBattleCheckpoint checkpoint, string battleId)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(battleId);
        if (checkpoint.State.AttackerArmyId != Battle.AttackerArmyId || checkpoint.State.DefenderArmyId != Battle.DefenderArmyId)
            throw new ArgumentException("Checkpoint belongs to a different encounter.", nameof(checkpoint));
        var context = TacticalContext.From(_context.Ruleset);
        var timeline = new TacticalBattleTimeline(context, battleId, _attackerNationId, _defenderNationId);
        context.Observer = timeline.Observe;
        timeline.Observe(checkpoint.State, null, new TacticalCaptureBoundary(TacticalFrameKind.Reconciliation));
        return new TacticalBattleSession(context, checkpoint.State, new SplitMix64Rng(checkpoint.DrawState),
            _attackerNationId, _defenderNationId) { _timeline = timeline };
    }

    /// <summary>
    /// Whether no order of the current half-round has been given since its setup: the log, read backwards,
    /// reaches a setup before it reaches an order. An exact-restored battle with an empty log is at the start.
    /// </summary>
    public bool AtHalfRoundStart
    {
        get
        {
            for (var i = Battle.Log.Count - 1; i >= 0; i--)
            {
                switch (Battle.Log[i])
                {
                    case TacticalSetUpEvent:
                        return true;
                    case TacticalPlacedEvent or TacticalMovedEvent or TacticalMoveForfeitedEvent or TacticalTargetSetEvent
                        or TacticalTargetClearedEvent or TacticalShotEvent or TacticalMeleeEvent or TacticalRoutedEvent
                        or TacticalCascadeRemovedEvent or TacticalSurrenderedEvent:
                        return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// The name of the battle's draw stream: <c>battle.tactical:&lt;attackerArmyId&gt;:&lt;defenderArmyId&gt;:&lt;turnIndex&gt;</c>
    /// <strong>[designed]</strong>.
    /// </summary>
    /// <remarks>
    /// Searched and found empty: the original has no named streams at all. <c>2026-10-04-decompiled-tactical-battle-rules.md</c> (the section "Every Random call, in
    /// draw order") and <c>2026-10-04-battle-exchange-hook.md</c> (the 14 <c>Random</c> call sites of the battle module and TBattleOver) show
    /// one global <c>RandSeed</c> that every draw advances and that <c>TBattlePols</c> reseeds (<c>2026-09-29-loading-a-save-does-not-reseed.md</c>,
    /// <c>RandSeed = winner + loser</c>); <c>2026-10-04-tactical-battle-sweep.md</c> only bakes a lab seed in. No report names a per-battle key, so
    /// the key is this engine's default: it follows the engine's rule of one derived <c>IRng</c> stream per random event, built from the state's seed
    /// and a stable key made of the two armies and the turn index, which tells two battles of one game apart. The draw ORDER inside the
    /// stream is confirmed by the first report; only the key is designed.
    /// </remarks>
    /// <param name="attackerArmyId">The attacking army.</param>
    /// <param name="defenderArmyId">The defending army.</param>
    /// <param name="turnIndex">The calendar's turn index when the battle opened.</param>
    /// <returns>The stream name.</returns>
    public static string StreamKey(string attackerArmyId, string defenderArmyId, int turnIndex) =>
        string.Create(CultureInfo.InvariantCulture, $"battle.tactical:{attackerArmyId}:{defenderArmyId}:{turnIndex}");

    /// <summary>
    /// Whether <c>combat.tactical.midBattleSave</c> selects the half-round-start save and exact-restore policy.
    /// The default <c>anyTime</c> policy retains setup on every reload.
    /// </summary>
    /// <param name="ruleset">The ruleset the game runs under.</param>
    /// <returns>Whether the policy is <c>halfRoundStart</c>.</returns>
    public static bool RulesetAsksForExactRestore(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return ruleset.Combat.Tactical.MidBattleSave == "halfRoundStart";
    }

    /// <summary>
    /// Whether <c>combat.tactical.midBattleSave</c> permits saving now: <c>anyTime</c>, or before the
    /// current half-round's first order under <c>halfRoundStart</c>.
    /// </summary>
    public bool CanSave => !RulesetAsksForExactRestore(_context.Ruleset) || AtHalfRoundStart;

    /// <summary>
    /// Whether a field battle between these two armies can be fought on the board: both exist, belong to different
    /// nations, have a live unit, and fit the battle's 20 slots a side. Anything else is left to the instant path.
    /// </summary>
    /// <param name="game">The game.</param>
    /// <param name="attackerArmyId">The attacking army.</param>
    /// <param name="defenderArmyId">The defending army.</param>
    /// <param name="ruleset">The ruleset.</param>
    /// <returns>Whether the board can host the battle.</returns>
    public static bool CanBeFought(GameState game, string attackerArmyId, string defenderArmyId, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(ruleset);
        var attacker = game.ArmyById(attackerArmyId);
        var defender = game.ArmyById(defenderArmyId);
        if (attacker is null || defender is null)
        {
            return false;
        }

        var slots = ruleset.ArmyManagement.MaxUnitsPerArmy;
        return attacker.Units.Count <= slots
               && defender.Units.Count <= slots
               && attacker.Units.Any(u => u.Troops > 0)
               && defender.Units.Any(u => u.Troops > 0)
               && attacker.Units.Concat(defender.Units).All(u => ruleset.UnitTypes.Any(t => t.Id == u.UnitTypeId));
    }

    /// <summary>
    /// Opens a battle: the attacker's moves are zeroed, T123's copy-in runs (the computer side's +3 and sort
    /// applied to its army), and the computer's half-rounds that come first (the defender's placement) are played.
    /// </summary>
    /// <param name="game">The game the attack was ordered in.</param>
    /// <param name="attackerArmyId">The attacking army.</param>
    /// <param name="defenderArmyId">The defending army.</param>
    /// <param name="ruleset">The ruleset.</param>
    /// <param name="opened">The game with the attacker's moves spent and the computer side's army changed.</param>
    /// <returns>The open battle.</returns>
    public static TacticalBattleSession Open(
        GameState game, string attackerArmyId, string defenderArmyId, Ruleset ruleset, out GameState opened, string? battleId = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(ruleset);

        var attacker = game.ArmyById(attackerArmyId)
                       ?? throw new ArgumentException($"'{attackerArmyId}' is not a known army.", nameof(attackerArmyId));
        var defender = game.ArmyById(defenderArmyId)
                       ?? throw new ArgumentException($"'{defenderArmyId}' is not a known army.", nameof(defenderArmyId));

        var context = TacticalContext.From(ruleset);
        var timeline = new TacticalBattleTimeline(context,
            battleId ?? StreamKey(attackerArmyId, defenderArmyId, game.Calendar.TurnIndex), attacker.Nation, defender.Nation);
        context.Observer = timeline.Observe;
        var rng = SplitMix64Rng.ForStream(game.RandomSeed, StreamKey(attackerArmyId, defenderArmyId, game.Calendar.TurnIndex));
        var draws = new RngBattleDraws(rng);

        // The attack spends the attacker's whole move whatever the outcome (the instant path does the same).
        var spent = game with
        {
            Armies = ValueList.From(game.Armies.Select(a =>
                string.Equals(a.Id, attacker.Id, StringComparison.Ordinal) ? a with { Moves = 0 } : a)),
        };

        var start = TacticalCopyIn.Start(spent, attackerArmyId, defenderArmyId, context, draws);
        var battle = TacticalDriver.Run(start.Battle, context, draws, new ComputerGeneral());

        timeline.Observe(battle, null, new TacticalCaptureBoundary(TacticalFrameKind.Reconciliation));
        opened = start.Game;
        return new TacticalBattleSession(context, battle, rng, attacker.Nation, defender.Nation) { _timeline = timeline };
    }

    /// <summary>
    /// Reopens a saved battle. Under <c>classical-faithful</c> this runs T123's <c>Resume</c>, the half-round setup of
    /// the side to move (<c>FUN_00439968</c>): moves refilled, targets cleared, the counter incremented, a pending
    /// melee lost, as the original resumes. The exact-restore path rebuilds the state as saved, without
    /// <c>Resume</c>, and is taken only when <paramref name="exactRestore"/> is set and the section says
    /// <see cref="SavedTacticalBattle.AtHalfRoundStart"/>.
    /// </summary>
    /// <param name="game">The loaded game (its armies are the battle's).</param>
    /// <param name="ruleset">The ruleset.</param>
    /// <param name="saved">The saved battle block.</param>
    /// <param name="exactRestore">Whether the ruleset asks for the exact-restore reload.</param>
    /// <returns>The reopened battle.</returns>
    public static TacticalBattleSession Reopen(GameState game, Ruleset ruleset, SavedTacticalBattle saved, bool exactRestore, string? battleId = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(saved);

        var context = TacticalContext.From(ruleset);
        var attacker = game.ArmyById(saved.AttackerArmyId)
                       ?? throw new ArgumentException($"The saved battle's army '{saved.AttackerArmyId}' is not in the game.", nameof(saved));
        var defender = game.ArmyById(saved.DefenderArmyId)
                       ?? throw new ArgumentException($"The saved battle's army '{saved.DefenderArmyId}' is not in the game.", nameof(saved));

        var timeline = new TacticalBattleTimeline(context,
            battleId ?? StreamKey(saved.AttackerArmyId, saved.DefenderArmyId, game.Calendar.TurnIndex), attacker.Nation, defender.Nation);
        context.Observer = timeline.Observe;
        var state = FromSaved(saved);
        timeline.Observe(state, null);
        var rng = new SplitMix64Rng(saved.DrawState);
        var battle = exactRestore && saved.AtHalfRoundStart
            ? ExactRestore(state)
            : TacticalHalfRound.Resume(state, context);

        // A save is made while a human is to move, so the driver has nothing to play; run it anyway so that a
        // block saved with a computer-driven side to move (a hand-made one) is not left stuck.
        if (!battle.IsOver && battle.IsComputerDriven(battle.SideToMove))
        {
            battle = TacticalDriver.Run(battle, context, new RngBattleDraws(rng), new ComputerGeneral());
        }

        timeline.Observe(battle, null, new TacticalCaptureBoundary(TacticalFrameKind.Reconciliation));
        return new TacticalBattleSession(context, battle, rng, attacker.Nation, defender.Nation) { _timeline = timeline };
    }

    /// <summary>
    /// The exact-restore path: the state as saved (counter, moves, targets, claims), with <c>Resume</c> not called
    /// <strong>[designed]</strong>. Exposed so T130 and the tests can take it directly.
    /// </summary>
    /// <param name="state">The state rebuilt from a saved block.</param>
    /// <returns>The same state.</returns>
    public static TacticalBattleState ExactRestore(TacticalBattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state;
    }

    /// <summary>The state a saved block describes, with an empty trace.</summary>
    /// <param name="saved">The saved block.</param>
    /// <returns>The battle state, as saved.</returns>
    public static TacticalBattleState FromSaved(SavedTacticalBattle saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        return new TacticalBattleState(
            saved.AttackerArmyId,
            saved.DefenderArmyId,
            saved.SideToMove,
            saved.Placed,
            saved.Counter,
            IsOver: false,
            ValueList.From(saved.Slots.Select(s => new TacticalSlot(
                s.X, s.Y, s.Origin, s.Type, s.Troops, s.Quality, s.Morale, s.Moves, s.Shots, s.Target, s.Name, s.RoutedTroops))),
            saved.Grid,
            saved.ComputerControlled,
            saved.ComputerGeneral,
            saved.GeneralClaims,
            saved.Selected,
            ValueList<TacticalEvent>.Empty);
    }

    /// <summary>The battle as a save block: the report's fields, the stream's position and the half-round-start flag.</summary>
    /// <returns>The block.</returns>
    public SavedTacticalBattle ToSaved() =>
        new(
            Battle.AttackerArmyId,
            Battle.DefenderArmyId,
            Battle.SideToMove,
            Battle.Placed,
            Battle.Counter,
            ValueList.From(Battle.Slots.Select(s => new SavedTacticalSlot(
                s.X, s.Y, s.Origin, s.Type, s.Troops, s.Quality, s.Morale, s.Moves, s.Shots, s.Target, s.Name, s.RoutedTroops))),
            Battle.Grid,
            Battle.ComputerControlled,
            Battle.ComputerGeneral,
            Battle.GeneralClaims,
            Battle.Selected,
            _rng.State,
            AtHalfRoundStart);

    /// <summary>The battle as a UI draws it.</summary>
    /// <returns>A snapshot.</returns>
    public TacticalBattleView View() =>
        new(
            Battle.AttackerArmyId,
            Battle.DefenderArmyId,
            _attackerNationId,
            _defenderNationId,
            Battle.SideToMove,
            Battle.Placed,
            Battle.Counter,
            Battle.IsOver,
            ValueList.From(_context.Ruleset.UnitTypes.Select(t => t.Id)),
            Battle.Slots,
            Battle.Grid,
            Battle.ComputerControlled,
            Battle.ComputerGeneral,
            Battle.Selected);

    // ---- the human's orders ----

    /// <summary><c>battle place</c>: moves an own unit to an empty cell of its home rows during placement.</summary>
    /// <param name="slot">The unit's slot.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The outcome.</returns>
    public TacticalBattleOrderResult Place(int slot, int x, int y) =>
        Apply(() => TacticalOrders.Place(Battle, slot, x, y, _context));

    /// <summary><c>battle move</c>: walks an own unit toward an empty cell.</summary>
    /// <param name="slot">The unit's slot.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The outcome.</returns>
    public TacticalBattleOrderResult Move(int slot, int x, int y) =>
        Apply(() => TacticalOrders.Move(Battle, slot, x, y, _context, _draws));

    /// <summary><c>battle attack</c>: sets an adjacent enemy as the unit's melee target.</summary>
    /// <param name="slot">The unit's slot.</param>
    /// <param name="enemy">The enemy's slot.</param>
    /// <returns>The outcome.</returns>
    public TacticalBattleOrderResult Attack(int slot, int enemy) =>
        Apply(() => TacticalOrders.SetTarget(Battle, slot, enemy, _context));

    /// <summary><c>battle shoot</c>: shoots an enemy within range.</summary>
    /// <param name="slot">The shooter's slot.</param>
    /// <param name="enemy">The enemy's slot.</param>
    /// <returns>The outcome.</returns>
    public TacticalBattleOrderResult Shoot(int slot, int enemy) =>
        Apply(() => TacticalOrders.Shoot(Battle, slot, enemy, _context, _draws));

    /// <summary><c>battle clear</c>: clears an own unit's melee target.</summary>
    /// <param name="slot">The unit's slot.</param>
    /// <returns>The outcome.</returns>
    public TacticalBattleOrderResult Clear(int slot) =>
        Apply(() => TacticalOrders.ClearTarget(Battle, slot, _context));

    /// <summary><c>battle end</c>: ends the half-round; the computer's half-rounds that follow are played.</summary>
    /// <returns>The outcome.</returns>
    public TacticalBattleOrderResult EndHalfRound() =>
        Apply(() => TacticalOrders.EndHalfRound(Battle, _context, _draws, _general));

    /// <summary><c>battle general on</c>: <em>Computer general</em> plays the side to move, and every half-round after it that is the computer's.</summary>
    /// <returns>The outcome.</returns>
    public TacticalBattleOrderResult GeneralOn() =>
        Apply(() => TacticalOrders.ComputerGeneralOn(Battle, _context, _draws, _general));

    /// <summary>
    /// <c>battle general off</c>: turns <em>Computer general</em> off for every human side that has it on, so the next
    /// half-round of that side is the human's again.
    /// </summary>
    /// <returns>The outcome.</returns>
    public TacticalBattleOrderResult GeneralOff()
    {
        _timeline.BeginCommand();
        if (Battle.IsOver)
        {
            return new TacticalBattleOrderResult(false, new[] { "Refused: The battle is over." });
        }

        var state = Battle;
        var changed = false;
        for (var side = 0; side < 2; side++)
        {
            if (state.ComputerGeneral[side] && !state.ComputerControlled[side])
            {
                state = TacticalOrders.ComputerGeneralOff(state, side, _context).State;
                changed = true;
            }
        }

        if (!changed)
        {
            return new TacticalBattleOrderResult(false, new[] { "Refused: Computer general is not on for a human side." });
        }

        return ApplyResult(new TacticalOrderResult(state, null));
    }

    /// <summary><c>battle surrender</c>: the side to move surrenders and its units are removed.</summary>
    /// <returns>The outcome.</returns>
    public TacticalBattleOrderResult Surrender() =>
        Apply(() => TacticalOrders.Surrender(Battle, _context, _draws));

    /// <summary>The <c>battle show</c> lines: the header, both sides' units and the board.</summary>
    /// <returns>The lines.</returns>
    public IReadOnlyList<string> ShowLines()
    {
        var rules = _context.Rules;
        var lines = new List<string>
        {
            $"Battle: {Battle.AttackerArmyId} ({_attackerNationId}) attacks {Battle.DefenderArmyId} ({_defenderNationId}).",
            Battle.IsOver
                ? $"Half-round {Battle.Counter}: the battle is over."
                : $"Half-round {Battle.Counter}: {(Battle.Placed ? "movement" : "placement")}; the {SideName(Battle.SideToMove)} "
                  + $"({NationOf(Battle.SideToMove)}) to {(Battle.Placed ? "move" : "place")}"
                  + (Battle.ComputerGeneral[Battle.SideToMove] ? ", Computer general on." : "."),
        };

        for (var side = 0; side < 2; side++)
        {
            lines.Add($"{(side == 0 ? "Attacker" : "Defender")} units (slot: type, troops, quality, morale, moves, shots, cell, target):");
            var first = side * Battle.SlotsPerSide;
            for (var s = first; s < first + Battle.SlotsPerSide; s++)
            {
                var unit = Battle.Slots[s];
                if (!unit.IsLive)
                {
                    continue;
                }

                var target = unit.Target >= 0 ? string.Create(CultureInfo.InvariantCulture, $"target {unit.Target}") : "no target";
                lines.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"  {s}: {_context.TypeOf(unit.Type).Id}, {unit.Troops} troops, q{unit.Quality}, morale {unit.Morale}, moves {unit.Moves}, shots {unit.Shots}, ({unit.X},{unit.Y}), {target} \"{unit.Name}\""));
            }
        }

        lines.Add(
            "Board (attacker "
            + string.Join(string.Empty, Enumerable.Range(0, _context.TypeCount).Select(t => (char)('0' + t)))
            + ", defender "
            + string.Join(string.Empty, Enumerable.Range(0, _context.TypeCount).Select(t => (char)('a' + t)))
            + " = "
            + string.Join(", ", _context.Ruleset.UnitTypes.Select(t => t.Id))
            + "; . empty):");
        lines.Add("    " + string.Join(string.Empty, Enumerable.Range(0, rules.BoardWidth).Select(x => x.ToString(CultureInfo.InvariantCulture)[^1..])));
        for (var y = 0; y < rules.BoardHeight; y++)
        {
            var row = new System.Text.StringBuilder();
            row.Append(string.Create(CultureInfo.InvariantCulture, $"{y,3} "));
            for (var x = 0; x < rules.BoardWidth; x++)
            {
                var code = Battle.Grid[(x * rules.BoardHeight) + y];
                if (code == TacticalIcon.Empty)
                {
                    row.Append('.');
                    continue;
                }

                var defender = code >= TacticalIcon.DefenderOffset;
                var type = (code % TacticalIcon.DefenderOffset) / TacticalIcon.SizesPerType;
                row.Append((char)((defender ? 'a' : '0') + type));
            }

            lines.Add(row.ToString());
        }

        return lines;
    }

    // ---- the end ----

    /// <summary>
    /// Writes the ended battle back through <see cref="ApplyTacticalBattleCommandHandler"/> (T123's steps 1 to 5), then
    /// takes step 6, the peace dialog, from the battle's own stream after the promotions: the treaty is offered between
    /// a human and an AI side when <c>strength(winner) &lt; strength(loser)</c>, <c>unity(loser) &gt; 500</c> and
    /// <c>cities(loser) &gt; 7</c> hold, and only then is <c>Random(5) &lt; 2</c> drawn (<c>TBattleOver_OK</c>,
    /// report §9; the constants are the ruleset's <c>autoPeace*</c> fields).
    /// </summary>
    /// <remarks>
    /// <c>strength(n)</c> is the decompile's <c>armies(n)</c>: <c>FUN_0044A8CC</c>'s army power summed over the
    /// nation's remaining armies after the loser's deletion, the sum the instant path compares
    /// (<c>InstantBattleResolver.TotalArmyPower</c>, private to it, so the sum is written again here over the same
    /// <see cref="ArmyPower.Compute"/>). Between two human seats nothing is offered, as in the instant path: the
    /// original's <c>THVHBatPols</c> writes no relation.
    /// </remarks>
    /// <param name="game">The game the battle was fought in.</param>
    /// <param name="ruleset">The ruleset.</param>
    /// <param name="draws">Optional draws for this conclusion only; otherwise the live battle stream continues.</param>
    /// <returns>The conclusion.</returns>
    /// <param name="world">The session world, needed only when a tactical remnant scatters.</param>
    public TacticalBattleConclusion Conclude(GameState game, Ruleset ruleset, IBattleDraws? draws = null, World? world = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(ruleset);

        if (!Battle.IsOver)
        {
            return new TacticalBattleConclusion(null, null, false, false, Array.Empty<DomainEvent>(), "The battle has not ended.");
        }

        // The new survival/cap policies use the same command boundary, with retreat after the ordinary peace draw.
        if (Battle.Capped || _context.Rules.RetreatLosses || _context.Rules.WinnerRoutedUnitsRejoin)
            return ConcludeSurvivors(game, ruleset, draws ?? _draws, world);

        // The promotions and the following peace draw share the supplied generator, or the live battle stream.
        var conclusionDraws = draws ?? _draws;
        TacticalWriteBackResult? written = null;
        var sink = new RecordingEventSink();
        var command = new ApplyTacticalBattleCommand(
            game.ActiveNationId,
            Battle.AttackerArmyId,
            Battle.DefenderArmyId,
            (state, events) =>
            {
                written = TacticalWriteBack.Apply(state, Battle, _context, conclusionDraws, events);
                return written.Game;
            });
        var outcome = new ApplyTacticalBattleCommandHandler().Handle(command, game, sink);
        if (outcome.Rejection is not null || written is null)
        {
            return new TacticalBattleConclusion(
                null, null, false, false, Array.Empty<DomainEvent>(), outcome.Rejection?.Message ?? "The write-back did not run.");
        }

        var after = IC2.Engine.Armies.ArmyTable.Record(game,
            Barbarians.BarbarianFaction.EnforceHostility(outcome.State!, ruleset), ruleset);
        var report = written.Report;
        var events = new List<DomainEvent>(sink.Events);

        var combat = ruleset.Combat;
        var winner = after.NationById(report.WinnerNationId);
        var loser = after.NationById(report.LoserNationId);
        var offered = false;
        var drawn = false;
        if (winner is not null && loser is not null && !winner.IsBarbarian && !loser.IsBarbarian
            && (winner.Control == SeatControl.Human) != (loser.Control == SeatControl.Human))
        {
            var gates = TotalArmyPower(after, winner.Id, ruleset) < TotalArmyPower(after, loser.Id, ruleset)
                        && loser.Unity > combat.AutoPeaceLoserUnityThreshold
                        && after.CountCitiesOwnedBy(loser.Id) > combat.AutoPeaceLoserCityThreshold;

            // The draw comes from the battle's stream after the promotions, and only when the three tests pass.
            if (gates)
            {
                drawn = true;
                offered = conclusionDraws.Random(combat.AutoPeaceChanceDenominator) < combat.AutoPeaceChanceNumerator;
            }

            if (offered)
            {
                events.Add(new PeaceTreatyOffered(winner.Id, loser.Id));
            }
        }

        return new TacticalBattleConclusion(after, report, offered, drawn, events, null);
    }

    /// <summary>combat.tactical survival/cap policies: promotion, optional peace, rejoining/retreat, scatter.</summary>
    private TacticalBattleConclusion ConcludeSurvivors(GameState game, Ruleset ruleset, IBattleDraws draws, World? world)
    {
        TacticalWriteBackResult? written = null;
        var offered = false;
        var drawn = false;
        var sink = new RecordingEventSink();
        var command = new ApplyTacticalBattleCommand(game.ActiveNationId, Battle.AttackerArmyId, Battle.DefenderArmyId, (state, events) =>
        {
            written = TacticalWriteBack.Apply(state, Battle, _context, draws, events);
            var afterVictory = written.Game;
            var report = written.Report;
            var winner = afterVictory.NationById(report.WinnerNationId)!;
            var loser = afterVictory.NationById(report.LoserNationId)!;
            var combat = ruleset.Combat;
            if (!Battle.Capped && !winner.IsBarbarian && !loser.IsBarbarian
                && (winner.Control == SeatControl.Human) != (loser.Control == SeatControl.Human)
                && TotalArmyPower(afterVictory, winner.Id, ruleset) < TotalArmyPower(afterVictory, loser.Id, ruleset)
                && loser.Unity > combat.AutoPeaceLoserUnityThreshold
                && afterVictory.CountCitiesOwnedBy(loser.Id) > combat.AutoPeaceLoserCityThreshold)
            {
                drawn = true;
                offered = draws.Random(combat.AutoPeaceChanceDenominator) < combat.AutoPeaceChanceNumerator;
            }
            written = TacticalWriteBack.CompleteSurvivors(written, state, _context, draws, world, events);
            return written.Game;
        });
        var outcome = new ApplyTacticalBattleCommandHandler().Handle(command, game, sink);
        if (outcome.Rejection is not null || written is null)
            return new TacticalBattleConclusion(null, null, false, false, Array.Empty<DomainEvent>(), outcome.Rejection?.Message ?? "The write-back did not run.");
        var resultEvents = new List<DomainEvent>(sink.Events);
        if (offered) resultEvents.Add(new PeaceTreatyOffered(written.Report.WinnerNationId, written.Report.LoserNationId));
        return new TacticalBattleConclusion(
            IC2.Engine.Armies.ArmyTable.Record(game,
                Barbarians.BarbarianFaction.EnforceHostility(outcome.State!, ruleset), ruleset), written.Report, offered, drawn, resultEvents, null);
    }

    /// <summary>The lines of the <em>Battle ended</em> window (<c>TBattleOver_InitializeForm</c>).</summary>
    /// <param name="report">The report.</param>
    /// <param name="game">The game after the write-back (for the nations' names).</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> ReportLines(TacticalBattleReport report, GameState game)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(game);
        var winner = game.NationById(report.WinnerNationId)?.Name ?? report.WinnerNationId;
        var loser = game.NationById(report.LoserNationId)?.Name ?? report.LoserNationId;
        return new[]
        {
            "Battle ended.",
            report.Capped ? $"{loser}'s army withdraws at the half-round cap; {winner}'s army holds."
                : $"{winner}'s army defeats {loser}'s army.",
            $"Winner: {report.WinnerStartTotal} troops at the start, {report.WinnerFinishTotal} at the end.",
            $"Loser: {report.LoserStartTotal} troops at the start, {report.LoserFinishTotal} at the end.",
            report.CapturedMoney > 0
                ? $"The army of {winner} captured {report.CapturedMoney} talents."
                : $"The army of {winner} captured no money.",
            report.CapturedSupplyTons > 0
                ? $"The army of {winner} captured {report.CapturedSupplyTons} tons of supplies."
                : $"The army of {winner} captured no supplies.",
        };
    }

    // ---- internals ----

    private static long TotalArmyPower(GameState game, string nationId, Ruleset ruleset)
    {
        long total = 0;
        foreach (var army in game.Armies)
        {
            if (string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                total += ArmyPower.Compute(army.Units, army.Morale, ruleset);
            }
        }

        return total;
    }

    /// <summary>Transfers opening/reopening frames exactly once; orders transfer their own batch in the result.</summary>
    public ValueList<TacticalBattleFrame> DrainTimeline() => _timeline.Drain();

    /// <summary>Non-consuming immutable opening frames; reading these never changes the next order's batch.</summary>
    internal ValueList<TacticalBattleFrame> PendingOpeningTimeline => _timeline.PendingOpeningFrames;

    private TacticalBattleOrderResult Apply(Func<TacticalOrderResult> order)
    {
        _timeline.BeginCommand();
        return ApplyResult(order());
    }

    private TacticalBattleOrderResult ApplyResult(TacticalOrderResult result)
    {
        if (!result.Accepted)
        {
            return new TacticalBattleOrderResult(false, new[] { "Refused: " + result.Rejection });
        }

        var before = Battle.Log.Count;
        Battle = result.State;
        _timeline.Observe(Battle, null, new TacticalCaptureBoundary(TacticalFrameKind.Reconciliation));
        var lines = new List<string>();
        Describe(Battle.Log.Skip(before), lines);
        return new TacticalBattleOrderResult(true, lines) { Timeline = _timeline.Drain() };
    }

    private string SideName(int side) => side == TacticalBattleState.AttackerSide ? "attacker" : "defender";

    private string NationOf(int side) => side == TacticalBattleState.AttackerSide ? _attackerNationId : _defenderNationId;

    private void Describe(IEnumerable<TacticalEvent> events, List<string> lines)
    {
        TacticalMovedEvent? run = null;
        void FlushRun()
        {
            if (run is not null)
            {
                lines.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Unit {run.Slot} moved to ({run.ToX},{run.ToY}), {run.MovesLeft} moves left."));
                run = null;
            }
        }

        foreach (var e in events)
        {
            if (e is TacticalMovedEvent moved)
            {
                if (run is not null && run.Slot != moved.Slot)
                {
                    FlushRun();
                }

                run = moved;
                continue;
            }

            FlushRun();
            switch (e)
            {
                case TacticalPlacedEvent placed:
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"Unit {placed.Slot} placed at ({placed.ToX},{placed.ToY})."));
                    break;
                case TacticalTargetSetEvent set:
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"Unit {set.Slot} will attack unit {set.Target} in melee."));
                    break;
                case TacticalTargetClearedEvent cleared:
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"Unit {cleared.Slot} has no melee target."));
                    break;
                case TacticalShotEvent shot:
                    lines.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Unit {shot.Shooter} shoots unit {shot.Target}: {shot.Loss} troops lost, {shot.TargetTroops} left, morale {shot.TargetMorale}."));
                    break;
                case TacticalMeleeEvent melee:
                    lines.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Unit {melee.Attacker} fights unit {melee.Defender}: attacker loses {melee.AttackerLoss} "
                            + $"({melee.AttackerTroops} left, morale {melee.AttackerMorale}), defender loses {melee.DefenderLoss} "
                            + $"({melee.DefenderTroops} left, morale {melee.DefenderMorale})."));
                    break;
                case TacticalRoutedEvent routed:
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"Unit {routed.Slot} routs."));
                    break;
                case TacticalCascadeRemovedEvent cascade:
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"Unit {cascade.Slot} breaks and flees."));
                    break;
                case TacticalSurrenderedEvent surrendered:
                    lines.Add($"The {SideName(surrendered.Side)} ({NationOf(surrendered.Side)}) surrenders.");
                    break;
                case TacticalComputerGeneralEvent general:
                    lines.Add($"Computer general is {(general.On ? "on" : "off")} for the {SideName(general.Side)}.");
                    break;
                case TacticalCapReachedEvent capped:
                    lines.Add($"Half-round cap reached after {capped.Counter} half-rounds; the attacker withdraws.");
                    break;
                case TacticalBattleOverEvent:
                    lines.Add("The battle is over.");
                    break;
                case TacticalSetUpEvent setup:
                    lines.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Half-round {setup.Counter}: the {SideName(setup.Side)} ({NationOf(setup.Side)}) to {(Battle.Placed ? "move" : "place")}."));
                    break;
            }
        }

        FlushRun();
    }
}
