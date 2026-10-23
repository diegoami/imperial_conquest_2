using System.Globalization;
using System.Numerics;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Strength;

namespace IC2.Slice.UI;

/// <summary>
/// The Army menu's four dialogs, Godot-free — each dialog's limits and the command lines it composes.
/// <see cref="ArmyTransferModel"/> backs the Transfer unit dialog (and, reusing its two-list shape, Split
/// army), while <see cref="ChangeUnitsModel"/> carries the single-unit verbs. The Godot controls
/// (<see cref="ArmyTransferDialog"/>, <see cref="SplitArmyDialog"/>, <see cref="ChangeUnitsDialog"/>) own
/// only widgets and submit what these types compose; the test project compiles this file directly, so it
/// must never reference <c>Godot.*</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Task T111</strong> (<c>docs/tasks/T111.md</c>): the Unit map's Army entries, each in its own
/// dialog. All rules live here so a plain xunit test
/// (<c>tests/IC2.Engine.Tests/Ui/ArmyDialogModelsTests.cs</c>) pins every limit and every composed line
/// without the Godot SDK. Every <em>limit</em> is read from the ruleset or the engine — the 198-army cap
/// (<see cref="ArmyManagementRules.MaxArmies"/>), the two-unit split floor
/// (<see cref="ArmyManagementRules.SplitMinUnits"/>), the dialog supply room
/// (<see cref="SupplyCapacity.ArmyDialogCapacityTons"/>) and the purse cap
/// (<see cref="EconomyRules.PurseCapPerUnit"/>) — never a C# literal. Only the confirmed step sizes are
/// named constants.
/// </para>
/// <para>
/// <strong>Regiment quality captions.</strong> The report
/// <c>army-records-and-roman-roster.md</c> maps the roster's tiers 6–9 to <em>average</em>, <em>good</em>,
/// <em>very good</em> and <em>elite</em>, and <c>city-units-army-transfer-and-mercenaries.md</c> supports
/// 5 as <em>poor</em>. A tier below 5 shows its number, the task entry's own instruction, until the
/// original's captions for those tiers are read (<c>[open]</c>).
/// </para>
/// </remarks>
public static class ArmyDialogModels
{
    /// <summary>The 10s stepper's step, supply tons [confirmed: code,
    /// <c>2026-10-03-army-to-army-ok-supply-rebalancing.md</c> item 6].</summary>
    public const int SupplyStepTons = 10;

    /// <summary>The 100s stepper's step, supply tons [confirmed: code, the same report].</summary>
    public const int SupplyLargeStepTons = 100;

    /// <summary>The 10s stepper's step, talents [confirmed: code, the same report].</summary>
    public const int MoneyStepTalents = 10;

    /// <summary>The 100s stepper's step, talents [confirmed: code, the same report].</summary>
    public const int MoneyLargeStepTalents = 100;

    /// <summary>
    /// The message Transfer unit and Join armies show when the game picks no partner — the task entry's
    /// own <c>[designed]</c> wording.
    /// </summary>
    public const string NoPartnerMessage = "There is no army next to this one.";

    /// <summary>
    /// The original's refusal for Split army on a one-unit army — <c>TUnitMap_SplitArmy</c>
    /// <c>[derived: code]</c>.
    /// </summary>
    public const string SplitOneUnitRefusal = "You can not split an army containing only 1 unit.";

    /// <summary>
    /// What the game screen says, and does nothing else, when Split army finds no free land cell for the
    /// new army — the original creates nothing and says nothing, so this one message is the clone's
    /// deliberate difference <c>[designed wording, the user's decision of 2026-10-05]</c>. The engine
    /// keeps its own <c>armies.no-free-adjacent-tile</c> refusal for a <c>split-army</c> that reaches it.
    /// Only an army that could otherwise split (two or more units, below the army cap) reaches this
    /// pre-check; a one-unit or over-cap army opens the dialog and gets T111's own refusal first.
    /// </summary>
    public const string SplitNoFreeTileMessage = "There is no free land next to this army for a new army.";

    /// <summary>
    /// #1288 (F5 of #1280): one unit as the player reads it — "2,000 Light infantry (poor)": the troops
    /// with a thousands separator, the ruleset's own unit-type name (the id when the ruleset lacks it),
    /// the regiment quality caption when given, and " — name" for a named unit.
    /// </summary>
    public static string UnitLine(Ruleset? ruleset, int troops, string unitTypeId, int? quality = null, string? name = null) =>
        $"{troops.ToString("N0", CultureInfo.InvariantCulture)} {ruleset?.UnitTypeById(unitTypeId)?.Name ?? unitTypeId}"
        + (quality is { } q ? $" ({QualityCaption(q)})" : string.Empty)
        + (string.IsNullOrEmpty(name) ? string.Empty : $" — {name}");

    /// <summary>
    /// The quality caption the army panel and the unit list print for a raw quality tier: the original's word
    /// table (#741): "not ready" for 0–3, "very poor" for 4, poor … elite for 5–9, and the number otherwise.
    /// </summary>
    public static string QualityCaption(int quality) => quality switch
    {
        >= 0 and <= 3 => "not ready",
        4 => "very poor",
        5 => "poor",
        6 => "average",
        7 => "good",
        8 => "very good",
        9 => "elite",
        _ => quality.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Join armies composes nothing without a partner (Done-when 1); with one, the selected army is the
    /// survivor and the partner is absorbed.
    /// </summary>
    public static string? JoinArmiesLine(string selectedArmyId, ArmyState? partner) =>
        partner is null ? null : $"join-armies {selectedArmyId} {partner.Id}";

    /// <summary>Disband army, after the confirmation prompt answers Yes.</summary>
    public static string DisbandArmyLine(string armyId) => $"disband-army {armyId}";
}

/// <summary>
/// The Transfer unit dialog's Godot-free model — two armies' unit lists, the units staged each way, and
/// the supply and money spinners' limits. One dialog <c>OK</c> composes <em>exactly one</em>
/// <c>army-transfer</c> (<c>docs/tasks/T117.md</c>), with the selected army first.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The units and both directions.</strong> The dialog's first list is the selected army (A) and
/// its second is the game's pick, the partner (B). A unit staged from A is carried as <c>units=</c>, a
/// unit staged back from B as <c>back-units=</c>; the indexes are into each source list as it stands when
/// the dialog opened.
/// </para>
/// <para>
/// <strong>The supply and money spinners' limits</strong> <c>[confirmed: code, FUN_00442310 and
/// 2026-10-03-army-to-army-ok-supply-rebalancing.md item 6]</c>: the supply spinner moves
/// <c>min(step, receiver's room, giver's supply)</c> and the receiver's room is
/// <c>troops div 100 + 1 − supply</c> — exactly <see cref="SupplyCapacity.ArmyDialogCapacityTons"/> minus
/// the receiver's stock, floored at 0. The money spinner moves
/// <c>min(step, 1000 − receiver's money, giver's money)</c>, the 1,000 read from
/// <see cref="EconomyRules.PurseCapPerUnit"/>. The spinners share one signed net per resource, so the
/// command carries a single <c>supply=</c>/<c>back-supply=</c> and <c>money=</c>/<c>back-money=</c>.
/// </para>
/// </remarks>
public sealed class ArmyTransferModel
{
    private readonly Ruleset _ruleset;
    private readonly ArmyState _selected;
    private readonly ArmyState _partner;
    private readonly List<int> _unitsToPartner = new();
    private readonly List<int> _unitsBack = new();
    private readonly GameState? _state;
    private readonly World? _world;
    private int _supplyNet;
    private int _moneyNet;
    private CommandDispatcher? _dispatcher;

    private ArmyTransferModel(ArmyState selected, ArmyState partner, Ruleset ruleset, GameState? state, World? world)
    {
        _selected = selected;
        _partner = partner;
        _ruleset = ruleset;
        _state = state;
        _world = world;
    }

    /// <summary>
    /// Builds the dialog's model for the selected army A and its game-picked partner B. When the live
    /// <paramref name="state"/> and <paramref name="world"/> are given, the supply "after" amounts are what
    /// the real <c>army-transfer</c> command would leave (its post-transfer rebalancing included).
    /// </summary>
    public static ArmyTransferModel ForArmies(
        ArmyState selected, ArmyState partner, Ruleset ruleset, GameState? state = null, World? world = null)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(ruleset);
        return new ArmyTransferModel(selected, partner, ruleset, state, world);
    }

    /// <summary>A, the selected army — the command's first id.</summary>
    public string SelectedArmyId => _selected.Id;

    /// <summary>B, the game-picked partner — the command's second id.</summary>
    public string PartnerArmyId => _partner.Id;

    /// <summary>Indexes into A's own list, moved A → B as <c>units=</c>.</summary>
    public IReadOnlyList<int> UnitsToPartner => _unitsToPartner;

    /// <summary>Indexes into B's own list, moved B → A as <c>back-units=</c>.</summary>
    public IReadOnlyList<int> UnitsBack => _unitsBack;

    /// <summary>The staged supply net: positive is A → B, negative is B → A.</summary>
    public int SupplyNet => _supplyNet;

    /// <summary>The staged money net: positive is A → B, negative is B → A.</summary>
    public int MoneyNet => _moneyNet;

    /// <summary>
    /// The troops now listed under A: A's units that are not staged to B, plus B's units staged back
    /// (the sum of the troops of the units the dialog shows in A's list).
    /// </summary>
    public int SelectedTroopTotal =>
        _selected.Units.Where((_, i) => !_unitsToPartner.Contains(i)).Sum(u => u.Troops)
        + _partner.Units.Where((_, i) => _unitsBack.Contains(i)).Sum(u => u.Troops);

    /// <summary>The troops now listed under B: B's units not staged back, plus A's units staged to B.</summary>
    public int PartnerTroopTotal =>
        _partner.Units.Where((_, i) => !_unitsBack.Contains(i)).Sum(u => u.Troops)
        + _selected.Units.Where((_, i) => _unitsToPartner.Contains(i)).Sum(u => u.Troops);

    /// <summary>A's supply in tons if OK were pressed now: the real command's result (0 if A is disbanded).</summary>
    public int SelectedSupplyAfter => Preview().Selected;

    /// <summary>B's supply in tons if OK were pressed now: the real command's result (0 if B is disbanded).</summary>
    public int PartnerSupplyAfter => Preview().Partner;

    /// <summary>
    /// Runs the very command OK would send on the live state through the engine's own dispatcher and reads
    /// both armies' supply back, so the caption can never disagree with the rebalancing. Without a state, or
    /// when the engine would refuse, falls back to the staged net alone.
    /// </summary>
    private (int Selected, int Partner) Preview()
    {
        var netOnly = (_selected.SupplyTons - _supplyNet, _partner.SupplyTons + _supplyNet);
        if (_state is null || _world is null)
        {
            return netOnly;
        }

        var staged = _unitsToPartner.Count > 0 || _unitsBack.Count > 0 || _supplyNet != 0 || _moneyNet != 0;
        if (!staged)
        {
            return (_selected.SupplyTons, _partner.SupplyTons);
        }

        var command = new ArmyTransferCommand(
            _selected.Nation,
            _selected.Id,
            _partner.Id,
            ValueList.From(_unitsToPartner.OrderBy(i => i)),
            Math.Max(0, _supplyNet),
            Math.Max(0, _moneyNet),
            ValueList.From(_unitsBack.OrderBy(i => i)),
            Math.Max(0, -_supplyNet),
            Math.Max(0, -_moneyNet));
        _dispatcher ??= new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), _ruleset, _world, NullEventSink.Instance);
        var result = _dispatcher.Dispatch(_state, command);
        if (result.IsRejected)
        {
            return netOnly;
        }

        return (
            result.State.ArmyById(_selected.Id)?.SupplyTons ?? 0,
            result.State.ArmyById(_partner.Id)?.SupplyTons ?? 0);
    }

    /// <summary>A's supply in tons as the dialog opened.</summary>
    public int SelectedSupplyNow => _selected.SupplyTons;

    /// <summary>B's supply in tons as the dialog opened.</summary>
    public int PartnerSupplyNow => _partner.SupplyTons;

    /// <summary>
    /// The supply row's caption: which army sends to which and how much, then each side's stock now and
    /// after the pending change, e.g. <c>Supply: 10 t from A1 to A2  ·  A1 40 → 30 t  ·  A2 5 → 15 t</c>.
    /// </summary>
    public string SupplyCaption()
    {
        var a = SelectedArmyId;
        var b = PartnerArmyId;
        var move = _supplyNet switch
        {
            > 0 => $"{_supplyNet} t from {a} to {b}",
            < 0 => $"{-_supplyNet} t from {b} to {a}",
            _ => $"none moved (up to {MaxSupplyToPartner} t from {a} to {b}, {MaxSupplyBack} t from {b} to {a})",
        };
        return $"Supply: {move}  ·  {a} {SelectedSupplyNow} → {SelectedSupplyAfter} t  ·  {b} {PartnerSupplyNow} → {PartnerSupplyAfter} t";
    }

    /// <summary>The most supply A can send B: <c>min(A's supply, B's room)</c>.</summary>
    public int MaxSupplyToPartner =>
        Math.Max(0, Math.Min(_selected.SupplyTons, Room(_partner)));

    /// <summary>The most supply B can send A: <c>min(B's supply, A's room)</c>.</summary>
    public int MaxSupplyBack =>
        Math.Max(0, Math.Min(_partner.SupplyTons, Room(_selected)));

    /// <summary>The most money A can send B: <c>min(A's money, 1000 − B's money)</c>.</summary>
    public int MaxMoneyToPartner =>
        Math.Max(0, Math.Min(_selected.Money, PurseCap - _partner.Money));

    /// <summary>The most money B can send A: <c>min(B's money, 1000 − A's money)</c>.</summary>
    public int MaxMoneyBack =>
        Math.Max(0, Math.Min(_partner.Money, PurseCap - _selected.Money));

    /// <summary>Stages one of A's units for the move to B; an already-staged index is ignored.</summary>
    public void StageUnitToPartner(int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= _selected.Units.Count)
        {
            return;
        }

        if (!_unitsToPartner.Contains(selectedIndex))
        {
            _unitsToPartner.Add(selectedIndex);
        }
    }

    /// <summary>Stages one of B's units for the move back to A; an already-staged index is ignored.</summary>
    public void StageUnitBack(int partnerIndex)
    {
        if (partnerIndex < 0 || partnerIndex >= _partner.Units.Count)
        {
            return;
        }

        if (!_unitsBack.Contains(partnerIndex))
        {
            _unitsBack.Add(partnerIndex);
        }
    }

    /// <summary>Puts back one of A's staged units (the dialog's second Transfer on it).</summary>
    public void UnstageUnitToPartner(int selectedIndex) => _unitsToPartner.Remove(selectedIndex);

    /// <summary>Puts back one of B's staged units (the dialog's second Transfer on it).</summary>
    public void UnstageUnitBack(int partnerIndex) => _unitsBack.Remove(partnerIndex);

    /// <summary>
    /// One press of a supply arrow: moves <paramref name="delta"/>, clamped to the giver's own stock and
    /// the receiver's room on that side. A press of 0 does nothing.
    /// </summary>
    public void AdjustSupply(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        _supplyNet = Math.Clamp(_supplyNet + delta, -MaxSupplyBack, MaxSupplyToPartner);
    }

    /// <summary>
    /// One press of a money arrow: moves <paramref name="delta"/>, clamped to the giver's own purse and
    /// the receiver's remaining room under the purse cap on that side.
    /// </summary>
    public void AdjustMoney(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        _moneyNet = Math.Clamp(_moneyNet + delta, -MaxMoneyBack, MaxMoneyToPartner);
    }

    /// <summary>
    /// <c>OK</c>: exactly one <c>army-transfer</c> for the whole dialog, or <see langword="null"/> when
    /// nothing is staged. The selected army is the first id, the units moved to the partner are
    /// <c>units=</c>, the units moved back are <c>back-units=</c>, and each resource is netted to a single
    /// direction.
    /// </summary>
    public string? ComposeOk()
    {
        var line = $"army-transfer {SelectedArmyId} {PartnerArmyId}";
        var any = false;

        if (_unitsToPartner.Count > 0)
        {
            line += $" units={Join(_unitsToPartner)}";
            any = true;
        }

        if (_unitsBack.Count > 0)
        {
            line += $" back-units={Join(_unitsBack)}";
            any = true;
        }

        if (_supplyNet > 0)
        {
            line += $" supply={_supplyNet}";
            any = true;
        }
        else if (_supplyNet < 0)
        {
            line += $" back-supply={-_supplyNet}";
            any = true;
        }

        if (_moneyNet > 0)
        {
            line += $" money={_moneyNet}";
            any = true;
        }
        else if (_moneyNet < 0)
        {
            line += $" back-money={-_moneyNet}";
            any = true;
        }

        return any ? line : null;
    }

    /// <summary><c>Cancel</c>: nothing is submitted.</summary>
    public string? Cancel() => null;

    private int PurseCap => _ruleset.Economy.PurseCapPerUnit;

    /// <summary>The receiver's dialog room: <c>troops div 100 + 1 − stock</c>, floored at 0.</summary>
    private int Room(ArmyState receiver) =>
        Math.Max(0, SupplyCapacity.ArmyDialogCapacityTons(receiver.TotalTroops, _ruleset) - receiver.SupplyTons);

    private static string Join(IEnumerable<int> indexes) =>
        string.Join(',', indexes.Select(i => i.ToString(CultureInfo.InvariantCulture)));
}

/// <summary>
/// The Split army dialog's Godot-free model. The dialog is the original's army-to-army form
/// (<c>TArmyToArmy</c>, titled <em>"Split army"</em>): A is the selected army and B the new army, which
/// opens with no units, supply or money. The units staged to B's list, and the supply and money moved
/// to it, compose <em>one</em> <c>split-army</c> on <c>OK</c> (<c>docs/tasks/T141.md</c>'s form).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Limits</strong> <c>[confirmed: code, 2026-10-03-army-to-army-ok-supply-rebalancing.md,
/// "Split army uses the same form", and item 6]</c>: the supply spinner moves
/// <c>min(step, B's room, A's supply)</c> with B's room <c>troops div 100 + 1 − supply</c> (B's troops
/// being the staged units', its supply 0), and the money spinner
/// <c>min(step, 1000 − B's money, A's money)</c> with the 1,000 read from
/// <see cref="EconomyRules.PurseCapPerUnit"/>. Both steps are 10 and 100. The selected army keeps at
/// least one unit (the engine's <c>armies.invalid-unit-selection</c>), so the last unit cannot be staged.
/// </para>
/// <para>
/// <strong>An army aboard a fleet</strong> is offered the same split as one on land — the engine carries
/// it out on the carrying fleet's tile (<c>SplitPlacement.ArmyCellFor</c>, T142)
/// <c>[derived: code, 2026-10-05-split-army-aboard-a-fleet.md, rules 1-2]</c>.
/// </para>
/// <para>
/// <strong>Disband</strong> under either list submits <c>disband-unit &lt;army&gt; &lt;index&gt;</c> at
/// once, the index being the selected army's own index of that unit; <see cref="ApplyDisband"/> then
/// re-reads the army and drops a staged unit from the staging.
/// </para>
/// </remarks>
public sealed class SplitArmyModel
{
    private readonly Ruleset _ruleset;
    private readonly int _armiesInPlay;
    private ArmyState _army;
    private readonly List<int> _staged = new();
    private int _supply;
    private int _money;

    private SplitArmyModel(ArmyState army, Ruleset ruleset, string newArmyId, int armiesInPlay)
    {
        _army = army;
        _ruleset = ruleset;
        NewArmyId = newArmyId;
        _armiesInPlay = armiesInPlay;
    }

    /// <summary>Builds the model for <paramref name="army"/>, the new army's id and the nation's live count.</summary>
    public static SplitArmyModel ForArmy(GameState state, ArmyState army, Ruleset ruleset, string newArmyId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(ruleset);
        // #802: the cap counts every army in play (DAT_004a0324 < 0xc6), as SplitArmyCommandHandler does,
        // not only the nation's own.
        return new SplitArmyModel(army, ruleset, newArmyId, state.Armies.Count);
    }

    /// <summary>The army being split (A).</summary>
    public string ArmyId => _army.Id;

    /// <summary>The new army's id (B), chosen by the dialog.</summary>
    public string NewArmyId { get; }

    /// <summary>The split floor, from the ruleset's own <see cref="ArmyManagementRules.SplitMinUnits"/>.</summary>
    public int MinUnits => _ruleset.ArmyManagement.SplitMinUnits;

    /// <summary>The army's live unit count.</summary>
    public int UnitCount => _army.Units.Count;

    /// <summary>
    /// Whether the split can be offered: at least <see cref="MinUnits"/> units and the armies in play below
    /// the <see cref="ArmyManagementRules.MaxArmies"/> cap.
    /// </summary>
    public bool CanSplit =>
        _army.Units.Count >= MinUnits && _armiesInPlay < _ruleset.ArmyManagement.MaxArmies;

    /// <summary>The refusal the dialog shows when <see cref="CanSplit"/> is false, or <see langword="null"/>.</summary>
    public string? RefusalMessage =>
        CanSplit
            ? null
            : _army.Units.Count < MinUnits
                ? ArmyDialogModels.SplitOneUnitRefusal
                : "You have reached your limit of armies.";

    /// <summary>Indexes into A's own list (as it stands now) staged for the new army, ascending.</summary>
    public IReadOnlyList<int> StagedUnits => _staged;

    /// <summary>Projected field strength of A after the currently staged whole units move.</summary>
    public int ParentPower => ArmyPower.Compute(
        _army.Units.Where((_, i) => !_staged.Contains(i)), _army.Morale, _ruleset);

    /// <summary>Projected field strength of B at the ruleset's new-army morale.</summary>
    public int NewArmyPower => ArmyPower.Compute(
        _staged.Select(i => _army.Units[i]), _ruleset.ArmyManagement.NewArmyMorale, _ruleset);

    /// <summary>
    /// Stages a legal whole-unit split with the smallest projected power difference. Equal power
    /// differences use troop difference, then the earliest ascending source-slot list. The 20-unit
    /// original cap keeps exhaustive subset search bounded; the weighted terms use precisely the two
    /// truncations in <see cref="ArmyPower.Compute"/>.
    /// </summary>
    public bool SuggestBalancedSplit()
    {
        var count = _army.Units.Count;
        if (!CanSplit || count is < 2 or > 20) return false;

        var weights = new int[count];
        var troops = new int[count];
        for (var i = 0; i < count; i++)
        {
            var unit = _army.Units[i];
            var type = _ruleset.UnitTypeById(unit.UnitTypeId)
                ?? throw new ArgumentException($"Unit type '{unit.UnitTypeId}' is not defined in ruleset '{_ruleset.Id}'.");
            weights[i] = (type.CombatPowerWeight * unit.Troops) / _ruleset.Combat.PowerTroopDivisor;
            troops[i] = unit.Troops;
        }

        var limit = 1 << count;
        var weightedByMask = new int[limit];
        var troopsByMask = new int[limit];
        for (var mask = 1; mask < limit; mask++)
        {
            var withoutLast = mask & (mask - 1);
            var last = BitOperations.TrailingZeroCount((uint)(mask ^ withoutLast));
            weightedByMask[mask] = weightedByMask[withoutLast] + weights[last];
            troopsByMask[mask] = troopsByMask[withoutLast] + troops[last];
        }

        var all = limit - 1;
        var bestMask = 0;
        var bestPowerDifference = long.MaxValue;
        var bestTroopDifference = long.MaxValue;
        for (var mask = 1; mask < all; mask++)
        {
            var parentPower = (weightedByMask[all ^ mask] / _ruleset.Combat.PowerDivisor) * _army.Morale;
            var newPower = (weightedByMask[mask] / _ruleset.Combat.PowerDivisor) * _ruleset.ArmyManagement.NewArmyMorale;
            var powerDifference = Math.Abs((long)parentPower - newPower);
            var troopDifference = Math.Abs((long)troopsByMask[all ^ mask] - troopsByMask[mask]);
            if (powerDifference > bestPowerDifference
                || powerDifference == bestPowerDifference && troopDifference > bestTroopDifference
                || powerDifference == bestPowerDifference && troopDifference == bestTroopDifference
                   && bestMask != 0 && !EarlierSlotList(mask, bestMask))
                continue;

            bestMask = mask;
            bestPowerDifference = powerDifference;
            bestTroopDifference = troopDifference;
        }

        _staged.Clear();
        for (var i = 0; i < count; i++)
            if ((bestMask & (1 << i)) != 0) _staged.Add(i);
        Reclamp();
        return true;
    }

    private static bool EarlierSlotList(int candidate, int current)
    {
        while (candidate != 0 && current != 0)
        {
            var a = BitOperations.TrailingZeroCount((uint)candidate);
            var b = BitOperations.TrailingZeroCount((uint)current);
            if (a != b) return a < b;
            candidate &= candidate - 1;
            current &= current - 1;
        }

        return current != 0;
    }

    /// <summary>The supply staged toward the new army, in tons.</summary>
    public int Supply => _supply;

    /// <summary>The money staged toward the new army, in talents.</summary>
    public int Money => _money;

    /// <summary>The new army's room for supply: <c>troops div 100 + 1 − 0</c>, from the staged units.</summary>
    public int NewArmyRoom =>
        _staged.Count == 0
            ? 0
            : SupplyCapacity.ArmyDialogCapacityTons(
                _staged.Sum(i => _army.Units[i].Troops), _ruleset);

    /// <summary>The most supply that can move: <c>min(A's supply, B's room)</c>.</summary>
    public int MaxSupply => Math.Max(0, Math.Min(_army.SupplyTons, NewArmyRoom));

    /// <summary>The most money that can move: <c>min(A's money, 1000 − B's money)</c> (B's money is 0).</summary>
    public int MaxMoney => Math.Max(0, Math.Min(_army.Money, _ruleset.Economy.PurseCapPerUnit));

    /// <summary>
    /// Stages one of A's units for the new army. Out-of-range and already-staged indexes are ignored,
    /// and so is the unit that would leave A empty.
    /// </summary>
    public void StageUnit(int index)
    {
        if (index < 0 || index >= _army.Units.Count || _staged.Contains(index)
            || _staged.Count + 1 >= _army.Units.Count)
        {
            return;
        }

        _staged.Add(index);
        _staged.Sort();
        Reclamp();
    }

    /// <summary>Puts a staged unit back in A's list.</summary>
    public void UnstageUnit(int index)
    {
        _staged.Remove(index);
        Reclamp();
    }

    /// <summary>One press of a supply arrow, clamped to <c>[0, <see cref="MaxSupply"/>]</c>.</summary>
    public void AdjustSupply(int delta) => _supply = Math.Clamp(_supply + delta, 0, MaxSupply);

    /// <summary>One press of a money arrow, clamped to <c>[0, <see cref="MaxMoney"/>]</c>.</summary>
    public void AdjustMoney(int delta) => _money = Math.Clamp(_money + delta, 0, MaxMoney);

    /// <summary>
    /// <c>OK</c>: exactly one <c>split-army</c> with every staged unit and the staged supply and money,
    /// or <see langword="null"/> when the split is refused or no unit is staged.
    /// </summary>
    public string? ComposeOk()
    {
        if (!CanSplit || _staged.Count == 0)
        {
            return null;
        }

        var line = $"split-army {ArmyId} {NewArmyId} "
            + string.Join(',', _staged.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        if (_supply > 0)
        {
            line += $" supply={_supply}";
        }

        if (_money > 0)
        {
            line += $" money={_money}";
        }

        return line;
    }

    /// <summary><c>Cancel</c>: nothing is submitted.</summary>
    public string? Cancel() => null;

    /// <summary>
    /// A Disband under either list: <c>disband-unit</c> naming the selected army's own index of the unit.
    /// </summary>
    public string DisbandLine(int armyIndex) => $"disband-unit {ArmyId} {armyIndex}";

    /// <summary>
    /// After a <c>disband-unit</c> on <paramref name="removedIndex"/> succeeded: takes the refreshed army,
    /// drops the unit from the staging if it was staged, and shifts the later staged indexes down.
    /// </summary>
    public void ApplyDisband(int removedIndex, ArmyState refreshed)
    {
        ArgumentNullException.ThrowIfNull(refreshed);
        _army = refreshed;
        _staged.Remove(removedIndex);
        for (var i = 0; i < _staged.Count; i++)
        {
            if (_staged[i] > removedIndex)
            {
                _staged[i]--;
            }
        }

        _staged.RemoveAll(i => i >= _army.Units.Count);
        Reclamp();
    }

    private void Reclamp()
    {
        _supply = Math.Clamp(_supply, 0, MaxSupply);
        _money = Math.Clamp(_money, 0, MaxMoney);
    }
}

/// <summary>
/// The Change units dialog's Godot-free model — the selected army's units, the selection the verbs act
/// on, and the command lines T107's <c>rename-unit</c>, <c>split-unit</c> and <c>disband-unit</c> plus the
/// existing <c>join-units</c> compose.
/// </summary>
/// <remarks>
/// The original's <c>TChangeArmyUnits</c> has Rename unit, Split unit, Join units, Disband, OK and
/// Cancel. This engine exposes each verb as its own command, so each button composes and submits the one
/// command it names; OK and Cancel simply close the dialog.
/// </remarks>
public sealed class ChangeUnitsModel
{
    private readonly ArmyState _army;

    private ChangeUnitsModel(ArmyState army)
    {
        _army = army;
    }

    /// <summary>Builds the model for the army whose units the dialog lists.</summary>
    public static ChangeUnitsModel ForArmy(ArmyState army)
    {
        ArgumentNullException.ThrowIfNull(army);
        return new ChangeUnitsModel(army);
    }

    /// <summary>The army whose units are listed.</summary>
    public string ArmyId => _army.Id;

    /// <summary>The army's units, in the engine's own order.</summary>
    public IReadOnlyList<UnitSlot> Units => _army.Units;

    /// <summary>#721 R36: Rename unit with more than one unit selected.</summary>
    public const string RenameOneAtATime = "You can only rename 1 unit at a time.";

    /// <summary>#802 R38 (play CU03): Split unit with more than one unit selected.</summary>
    public const string SplitOneAtATime = "You can only split 1 unit at a time.";

    /// <summary>Rename unit composes <c>rename-unit</c>.</summary>
    public string RenameLine(int unitIndex, string name) => $"rename-unit {ArmyId} {unitIndex} {name}";

    /// <summary>Split unit composes <c>split-unit</c>.</summary>
    public string SplitUnitLine(int unitIndex, int troops) => $"split-unit {ArmyId} {unitIndex} {troops}";

    /// <summary>Join units composes <c>join-units</c> over every selected unit, the first index surviving.</summary>
    public string JoinUnitsLine(params int[] unitIndexes) =>
        $"join-units {ArmyId} {string.Join(' ', unitIndexes)}";

    /// <summary>Disband composes <c>disband-unit</c>.</summary>
    public string DisbandUnitLine(int unitIndex) => $"disband-unit {ArmyId} {unitIndex}";

    /// <summary>
    /// The prompt the Disband button asks before it acts: the original's own text, singular for one
    /// selected unit and plural above it <c>[derived: code,
    /// 2026-10-05-refusal-texts-and-conditions.md, row P08]</c>.
    /// </summary>
    public static string DisbandPromptText(int selectedCount) =>
        $"Are you sure you want to disband {selectedCount} unit{(selectedCount > 1 ? "s" : string.Empty)}.";

    /// <summary>
    /// The orders the Disband button submits once its prompt is answered: one <c>disband-unit</c> per
    /// selected unit, <em>highest index first</em> so an earlier removal never shifts a later index, and
    /// none at all when the player answered No. The engine still refuses each order it would refuse today
    /// (a last unit, an unknown army).
    /// </summary>
    public IReadOnlyList<string> DisbandOrders(IReadOnlyList<int> selectedIndexes, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(selectedIndexes);
        return confirmed
            ? selectedIndexes
                .Distinct()
                .OrderByDescending(unitIndex => unitIndex)
                .Select(DisbandUnitLine)
                .ToArray()
            : Array.Empty<string>();
    }
}
