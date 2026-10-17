using IC2.Engine.Core;
using System.Text.Json.Serialization;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// What the original's <em>Battle ended</em> window shows (<c>TBattleOver_InitializeForm</c>), and the
/// exchange log the screen showed during the battle.
/// </summary>
/// <param name="WinnerSide">0 attacker, 1 defender.</param>
/// <param name="WinnerArmyId">The winning army.</param>
/// <param name="LoserArmyId">The losing army, deleted by the write-back.</param>
/// <param name="WinnerNationId">The winner's nation (<c>"W's  army  defeats  L's  army."</c>).</param>
/// <param name="LoserNationId">The loser's nation.</param>
/// <param name="WinnerStartByType">The winner's troops at the start (its army before the write-back), per unit type in ruleset order.</param>
/// <param name="WinnerStartTotal">The winner's troops at the start, in total.</param>
/// <param name="WinnerFinishByType">The winner's surviving troops, per unit type.</param>
/// <param name="WinnerFinishTotal">The winner's surviving troops, in total.</param>
/// <param name="LoserStartByType">The loser's troops at the start, per unit type (the loser finishes at 0).</param>
/// <param name="LoserStartTotal">The loser's troops at the start, in total.</param>
/// <param name="CapturedMoney">The loser's money, <c>"The army of W captured N talents."</c> (or <c>"no money."</c> at 0).</param>
/// <param name="CapturedSupplyTons">The loser's supplies, <c>"… N tons of supplies."</c> (or the original's <c>"no suuplies."</c> at 0).</param>
/// <param name="ExchangeLog">The battle's shots, melees, routs and surrender, in order.</param>
/// <param name="Promotions">The write-back's quality step per surviving winner unit, in slot order.</param>
/// <param name="LoserFinishByType">Optional surviving remnant totals; absent under the original policy.</param>
/// <param name="LoserFinishTotal">Surviving remnant troops after retreat and placement; zero under the original policy.</param>
/// <param name="Capped">combat.tactical.halfRoundCap produced an attacker withdrawal, with no ordinary victory rewards.</param>
public sealed record TacticalBattleReport(
    int WinnerSide,
    string WinnerArmyId,
    string LoserArmyId,
    string WinnerNationId,
    string LoserNationId,
    ValueList<int> WinnerStartByType,
    int WinnerStartTotal,
    ValueList<int> WinnerFinishByType,
    int WinnerFinishTotal,
    ValueList<int> LoserStartByType,
    int LoserStartTotal,
    int CapturedMoney,
    int CapturedSupplyTons,
    ValueList<TacticalEvent> ExchangeLog,
    ValueList<TacticalPromotionEvent> Promotions,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ValueList<int>? LoserFinishByType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int LoserFinishTotal = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Capped = false)
{
    /// <summary>Observed aftermath for the remake screen; excluded from legacy report output.</summary>
    [JsonIgnore]
    public TacticalBattleAftermath? Aftermath { get; init; }
}

/// <summary>The write-back's result.</summary>
/// <param name="Game">The game state after steps 1–5.</param>
/// <param name="Battle">The battle, its trace extended by the promotion draws.</param>
/// <param name="Report">The <em>Battle ended</em> window's contents.</param>
public sealed record TacticalWriteBackResult(GameState Game, TacticalBattleState Battle, TacticalBattleReport Report);

/// <summary>
/// The write-back, steps 1–5 of <c>TBattleOver_OK</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §9]</strong>; <c>docs/game-design.md</c>,
/// "Write-back".
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description>
/// The winner army's units are zeroed, and each surviving battle slot, in slot order, is copied into
/// units 0, 1, … (origin, type, troops, name), each with <c>quality = max(6, quality)</c> and then, on
/// <c>Random(4) = 0</c>, <c>quality = min(9, quality + 1)</c>: one draw per survivor.
/// </description></item>
/// <item><description>
/// The winner's money gains the loser's money; its supplies gain the loser's and are then capped at
/// <c>totalTroops div 100</c>.
/// </description></item>
/// <item><description>The loser army is deleted (and any fleet's claim on it cleared).</description></item>
/// <item><description>Unity: the winner <c>min(990, unity + 25)</c>, the loser <c>−25</c>.</description></item>
/// <item><description>News: <c>"W destroys army of L."</c> (<see cref="BattleArmyDestroyed"/>).</description></item>
/// </list>
/// <para>
/// <strong>Step 6, the peace dialog, is not here</strong>: its <c>Random(5)</c> comes after the
/// promotions and its routine is diplomacy's. The caller keeps drawing from the same
/// <see cref="IBattleDraws"/> it passed in, which is the post-promotion draw seam T125 takes that draw
/// from. The constants are the ruleset's (<c>combat.qualityFloor</c>, <c>qualityCap</c>,
/// <c>promotionChanceDenominator</c>, <c>absorbedSupplyTroopDivisor</c>, <c>unitySwing</c>,
/// <c>economy.unityCap</c>); the arithmetic is duplicated from the instant resolver's, never shared,
/// so this path cannot move a shipped outcome.
/// </para>
/// <para>
/// When both sides are emptied the defender "wins" with no units (report §8, <strong>[derived]</strong>):
/// its army is kept, with no unit records, as the original's zeroed army record is.
/// </para>
/// </remarks>
public static class TacticalWriteBack
{
    /// <summary>Applies an ended battle to the game.</summary>
    public static TacticalWriteBackResult Apply(
        GameState game,
        TacticalBattleState battle,
        TacticalContext context,
        IBattleDraws draws,
        IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentNullException.ThrowIfNull(events);
        if (!battle.IsOver)
        {
            throw new ArgumentException("The battle is not over.", nameof(battle));
        }

        // combat.tactical.halfRoundCap has its own no-reward write-back; natural victories retain the original below.
        if (battle.Capped) return ApplyCapped(game, battle, context);
        var combat = context.Combat;
        var winnerSide = TacticalSurrender.Winner(battle);
        var winnerId = winnerSide == TacticalBattleState.AttackerSide ? battle.AttackerArmyId : battle.DefenderArmyId;
        var loserId = winnerSide == TacticalBattleState.AttackerSide ? battle.DefenderArmyId : battle.AttackerArmyId;
        var winner = game.ArmyById(winnerId) ?? throw new ArgumentException($"'{winnerId}' is not a known army.", nameof(battle));
        var loser = game.ArmyById(loserId) ?? throw new ArgumentException($"'{loserId}' is not a known army.", nameof(battle));

        // Step 1: compaction and promotion, one Random(4) per survivor in slot order.
        var board = TacticalBoard.Thaw(battle, context, draws);
        var units = new List<UnitSlot>();
        var promotions = new List<TacticalPromotionEvent>();
        var first = board.FirstSlotOf(winnerSide);
        for (var s = first; s < first + board.SlotsPerSide; s++)
        {
            var slot = board.Slots[s];
            if (slot.Troops <= 0)
            {
                continue;
            }

            var quality = Math.Max(combat.QualityFloor, slot.Quality);
            if (board.Draw(combat.PromotionChanceDenominator) == 0)
            {
                quality = Math.Min(combat.QualityCap, quality + 1);
            }

            var promotion = new TacticalPromotionEvent(s, units.Count, slot.Quality, quality);
            promotions.Add(promotion);
            board.Log.Add(promotion);
            units.Add(new UnitSlot(slot.Origin, context.TypeOf(slot.Type).Id, slot.Troops, quality, slot.Name));
        }

        // Step 2: money, and supplies capped at the survivors' troops div 100.
        var totalTroops = units.Sum(u => u.Troops);
        var newWinner = winner with
        {
            Units = ValueList.From(units),
            Money = unchecked(winner.Money + loser.Money),
            SupplyTons = Math.Min(unchecked(winner.SupplyTons + loser.SupplyTons), totalTroops / combat.AbsorbedSupplyTroopDivisor),
        };

        // Step 3: the loser army is deleted, and nothing may still point at it.
        var armies = new List<ArmyState>();
        foreach (var army in game.Armies)
        {
            if (string.Equals(army.Id, loser.Id, StringComparison.Ordinal))
            {
                continue;
            }

            armies.Add(string.Equals(army.Id, winner.Id, StringComparison.Ordinal) ? newWinner : army);
        }

        var fleets = game.Fleets.Select(f =>
            f.CarriedArmyId is { } carried && string.Equals(carried, loser.Id, StringComparison.Ordinal)
                ? f with { CarriedArmyId = null }
                : f);

        // Step 4: unity, the winner capped, the loser unfloored.
        var nations = game.Nations.Select(n =>
            string.Equals(n.Id, winner.Nation, StringComparison.Ordinal)
                ? n with { Unity = Math.Min(context.Ruleset.Economy.UnityCap, n.Unity + combat.UnitySwing) }
                : string.Equals(n.Id, loser.Nation, StringComparison.Ordinal)
                    ? n with { Unity = n.Unity - combat.UnitySwing }
                    : n);

        var newGame = game with
        {
            Armies = ValueList.From(armies),
            Fleets = ValueList.From(fleets),
            Nations = ValueList.From(nations),
        };

        // Step 5: the news line.
        // combat.tactical.retreatLosses defers the news until actual escape/destruction is known.
        if (!context.Rules.RetreatLosses || !SideSlots(battle, 1 - winnerSide).Any(s => s.RoutedTroops > 0))
            events.Publish(new BattleArmyDestroyed(NationName(game, winner.Nation), NationName(game, loser.Nation)));

        var finalBattle = board.Freeze();
        var report = new TacticalBattleReport(
            winnerSide,
            winner.Id,
            loser.Id,
            winner.Nation,
            loser.Nation,
            ByType(winner.Units, context),
            winner.TotalTroops,
            ByType(units, context),
            totalTroops,
            ByType(loser.Units, context),
            loser.TotalTroops,
            loser.Money,
            loser.SupplyTons,
            ValueList.From(finalBattle.Log.Where(IsExchange)),
            ValueList.From(promotions));

        report = report with { Aftermath = Capture(game, newGame, finalBattle, report, context) };
        return new TacticalWriteBackResult(newGame, finalBattle, report);
    }

    /// <summary>combat.tactical.halfRoundCap: no promotion, transfer, unity or peace draw.</summary>
    private static TacticalWriteBackResult ApplyCapped(GameState game, TacticalBattleState battle, TacticalContext context)
    {
        var winner = game.ArmyById(battle.DefenderArmyId)!;
        var loser = game.ArmyById(battle.AttackerArmyId)!;
        var units = ValueList.From(SideSlots(battle, TacticalBattleState.DefenderSide).Where(s => s.IsLive).Select(s => ToUnit(s, s.Troops, context)));
        var newWinner = winner with { Units = units };
        var after = game with
        {
            Armies = ValueList.From(game.Armies.Where(a => a.Id != loser.Id).Select(a => a.Id == winner.Id ? newWinner : a)),
            Fleets = ValueList.From(game.Fleets.Select(f => f.CarriedArmyId == loser.Id ? f with { CarriedArmyId = null } : f)),
        };
        var report = new TacticalBattleReport(TacticalBattleState.DefenderSide, winner.Id, loser.Id, winner.Nation, loser.Nation,
            ByType(winner.Units, context), winner.TotalTroops, ByType(units, context), units.Sum(u => u.Troops),
            ByType(loser.Units, context), loser.TotalTroops, 0, 0, ValueList.From(battle.Log.Where(IsExchange)),
            ValueList<TacticalPromotionEvent>.Empty, Capped: true);
        report = report with { Aftermath = Capture(game, after, battle, report, context) };
        return new TacticalWriteBackResult(after, battle, report);
    }

    /// <summary>
    /// combat.tactical.retreatLosses and winnerRoutedUnitsRejoin restore exits after promotion/peace processing.
    /// halfRoundCap also preserves live withdrawing attackers. Existing ScatterPlacement and distance values are reused unchanged.
    /// </summary>
    public static TacticalWriteBackResult CompleteSurvivors(TacticalWriteBackResult initial, GameState before,
        TacticalContext context, IBattleDraws draws, World? world, IEventSink events)
    {
        var battle = initial.Battle;
        var report = initial.Report;
        var t = context.Rules;
        var originalWinner = before.ArmyById(report.WinnerArmyId)!;
        var originalLoser = before.ArmyById(report.LoserArmyId)!;
        var winner = initial.Game.ArmyById(report.WinnerArmyId)!;
        var winnerUnits = new List<UnitSlot>();
        var live = 0;
        foreach (var slot in SideSlots(battle, report.WinnerSide))
        {
            if (slot.IsLive) winnerUnits.Add(winner.Units[live++]);
            else if (t.WinnerRoutedUnitsRejoin && slot.RoutedTroops > 0) winnerUnits.Add(ToUnit(slot, slot.RoutedTroops, context));
        }
        winner = winner with
        {
            Units = ValueList.From(winnerUnits),
            SupplyTons = Math.Min(battle.Capped ? originalWinner.SupplyTons : unchecked(originalWinner.SupplyTons + originalLoser.SupplyTons),
                winnerUnits.Sum(u => u.Troops) / context.Combat.AbsorbedSupplyTroopDivisor),
        };
        var after = initial.Game with { Armies = ValueList.From(initial.Game.Armies.Select(a => a.Id == winner.Id ? winner : a)) };
        var remnant = ValueList.From(SideSlots(battle, 1 - report.WinnerSide)
            .Select(s => ToUnit(s, battle.Capped && s.IsLive ? s.Troops : t.RetreatLosses ? s.RoutedTroops : 0, context))
            .Where(u => u.Troops > 0));
        var beforePursuit = remnant.Sum(u => u.Troops);
        var afterPursuit = beforePursuit;
        var deferredNews = battle.Capped || (t.RetreatLosses && remnant.Count > 0);
        var escaped = ValueList<UnitSlot>.Empty;
        if (remnant.Count > 0)
        {
            if (t.RetreatLosses) remnant = RetreatLosses.Apply(remnant, battle, report.WinnerSide, context);
            afterPursuit = remnant.Sum(u => u.Troops);
            if (remnant.Count > 0)
            {
                if (world is null) throw new ArgumentNullException(nameof(world), "Tactical escape requires the battle's world.");
                var scatter = context.Combat.ScatteredDefeat;
                var distance = scatter.ScatterTilesMin + draws.Random(scatter.ScatterTilesMax - scatter.ScatterTilesMin + 1);
                var location = ScatterPlacement.Find(new GridPoint(originalLoser.X, originalLoser.Y), new GridPoint(winner.X, winner.Y),
                    distance, false, originalLoser.Id, after, world, out var terrain);
                if (location is not null)
                {
                    var survivor = originalLoser with
                    {
                        X = location.ToX, Y = location.ToY, CoveredTileCode = terrain, AboardFleetId = null, Moves = 0,
                        Money = battle.Capped ? originalLoser.Money : 0,
                        SupplyTons = battle.Capped ? Math.Min(originalLoser.SupplyTons, remnant.Sum(u => u.Troops) / context.Combat.AbsorbedSupplyTroopDivisor) : 0,
                        Units = remnant,
                    };
                    var armies = after.Armies.ToList();
                    var originalIndex = before.Armies.ToList().FindIndex(a => a.Id == survivor.Id);
                    armies.Insert(Math.Min(originalIndex, armies.Count), survivor);
                    after = after with { Armies = ValueList.From(armies) };
                    escaped = remnant;
                    deferredNews = false;
                    events.Publish(new ArmyScattered(survivor.Id, survivor.Nation, survivor.X, survivor.Y));
                }
            }
        }
        if (deferredNews)
            events.Publish(new BattleArmyDestroyed(NationName(before, winner.Nation), NationName(before, originalLoser.Nation)));
        return initial with
        {
            Game = after,
            Report = report with { WinnerFinishByType = ByType(winnerUnits, context), WinnerFinishTotal = winner.TotalTroops,
                LoserFinishByType = ByType(escaped, context), LoserFinishTotal = escaped.Sum(u => u.Troops),
                Aftermath = Capture(before, after, battle, report, context, beforePursuit - afterPursuit,
                    afterPursuit > 0 && escaped.Count == 0) },
        };
    }

    private static TacticalBattleAftermath Capture(GameState before, GameState after, TacticalBattleState battle,
        TacticalBattleReport report, TacticalContext context, int pursuitLosses = 0, bool scatterFailed = false)
    {
        TacticalArmyAftermath Side(int side, string armyId, string nationId)
        {
            bool OnSide(int slot) => slot / battle.SlotsPerSide == side;
            var casualties = battle.Log.Sum(e => e switch
            {
                TacticalShotEvent shot => OnSide(shot.Target) ? shot.Loss : 0,
                TacticalMeleeEvent melee => (OnSide(melee.Attacker) ? melee.AttackerLoss : 0)
                    + (OnSide(melee.Defender) ? melee.DefenderLoss : 0),
                _ => 0,
            });
            var routed = battle.Log.OfType<TacticalRoutedEvent>().Where(e => OnSide(e.Slot)).Select(e => e.Slot).ToHashSet();
            var cascade = battle.Log.OfType<TacticalCascadeRemovedEvent>().Where(e => OnSide(e.Slot) && !routed.Contains(e.Slot))
                .Select(e => e.Slot).Distinct().Count();
            var army = after.ArmyById(armyId);
            var rejoined = side == report.WinnerSide && context.Rules.WinnerRoutedUnitsRejoin
                ? SideSlots(battle, side).Sum(s => s.RoutedTroops) : 0;
            return new(casualties, routed.Count, cascade, rejoined, army?.TotalTroops ?? 0, army is null,
                before.NationById(nationId)?.Unity ?? 0, after.NationById(nationId)?.Unity ?? 0);
        }
        return new(Side(report.WinnerSide, report.WinnerArmyId, report.WinnerNationId),
            Side(1 - report.WinnerSide, report.LoserArmyId, report.LoserNationId), context.Rules.RetreatLosses,
            scatterFailed, pursuitLosses);
    }
    private static IEnumerable<TacticalSlot> SideSlots(TacticalBattleState battle, int side) =>
        battle.Slots.Skip(side * battle.SlotsPerSide).Take(battle.SlotsPerSide);

    private static UnitSlot ToUnit(TacticalSlot slot, int troops, TacticalContext context) =>
        new(slot.Origin, context.TypeOf(slot.Type).Id, troops, slot.Quality, slot.Name);

    private static bool IsExchange(TacticalEvent e) =>
        e is TacticalShotEvent or TacticalMeleeEvent or TacticalRoutedEvent or TacticalCascadeRemovedEvent
            or TacticalSurrenderedEvent;

    private static ValueList<int> ByType(IEnumerable<UnitSlot> units, TacticalContext context)
    {
        var totals = new int[context.TypeCount];
        foreach (var unit in units)
        {
            var type = context.TypeIndexOf(unit.UnitTypeId);
            if (type >= 0)
            {
                totals[type] += unit.Troops;
            }
        }

        return ValueList.From(totals);
    }

    private static string NationName(GameState game, string nationId) => game.NationById(nationId)?.Name ?? nationId;
}

