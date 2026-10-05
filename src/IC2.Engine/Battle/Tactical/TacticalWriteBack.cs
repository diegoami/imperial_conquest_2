using IC2.Engine.Core;
using IC2.Engine.Model;

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
    ValueList<TacticalPromotionEvent> Promotions);

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

        return new TacticalWriteBackResult(newGame, finalBattle, report);
    }

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
