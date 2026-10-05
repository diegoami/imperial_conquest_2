using System.Globalization;
using IC2.Engine.Economy;
using IC2.Engine.Model;

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
    /// The quality caption the army panel and the unit list print for a raw quality tier — the roster's
    /// own words for 5–9, and the tier's number below 5 (see this class's remarks).
    /// </summary>
    public static string QualityCaption(int quality) => quality switch
    {
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
    private int _supplyNet;
    private int _moneyNet;

    private ArmyTransferModel(ArmyState selected, ArmyState partner, Ruleset ruleset)
    {
        _selected = selected;
        _partner = partner;
        _ruleset = ruleset;
    }

    /// <summary>Builds the dialog's model for the selected army A and its game-picked partner B.</summary>
    public static ArmyTransferModel ForArmies(ArmyState selected, ArmyState partner, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(partner);
        ArgumentNullException.ThrowIfNull(ruleset);
        return new ArmyTransferModel(selected, partner, ruleset);
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
/// The Split army dialog's Godot-free model. It needs at least two units
/// (<see cref="ArmyManagementRules.SplitMinUnits"/>); the original's refusal is
/// <see cref="ArmyDialogModels.SplitOneUnitRefusal"/>. The new army is placed by the engine
/// (<c>docs/tasks/T111.md</c>: the dialog doesn't place it), and the engine's <c>split-army</c> CLI takes
/// a single unit index, so the dialog moves the one unit named here.
/// </summary>
public sealed class SplitArmyModel
{
    private readonly Ruleset _ruleset;
    private readonly ArmyState _army;
    private readonly int _nationArmyCount;
    private int? _unitIndexToNewArmy;

    private SplitArmyModel(ArmyState army, Ruleset ruleset, string newArmyId, int nationArmyCount)
    {
        _army = army;
        _ruleset = ruleset;
        NewArmyId = newArmyId;
        _nationArmyCount = nationArmyCount;
    }

    /// <summary>Builds the model for <paramref name="army"/>, the new army's id and the nation's live count.</summary>
    public static SplitArmyModel ForArmy(GameState state, ArmyState army, Ruleset ruleset, string newArmyId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(ruleset);
        var count = state.Armies.Count(a => string.Equals(a.Nation, army.Nation, StringComparison.Ordinal));
        return new SplitArmyModel(army, ruleset, newArmyId, count);
    }

    /// <summary>The army being split.</summary>
    public string ArmyId => _army.Id;

    /// <summary>The new army's id, chosen by the dialog.</summary>
    public string NewArmyId { get; }

    /// <summary>The split floor, from the ruleset's own <see cref="ArmyManagementRules.SplitMinUnits"/>.</summary>
    public int MinUnits => _ruleset.ArmyManagement.SplitMinUnits;

    /// <summary>The army's live unit count.</summary>
    public int UnitCount => _army.Units.Count;

    /// <summary>
    /// Whether the split can be offered: at least <see cref="MinUnits"/> units and the nation below its
    /// <see cref="ArmyManagementRules.MaxArmies"/> cap.
    /// </summary>
    public bool CanSplit =>
        _army.Units.Count >= MinUnits && _nationArmyCount < _ruleset.ArmyManagement.MaxArmies;

    /// <summary>The refusal the dialog shows when <see cref="CanSplit"/> is false, or <see langword="null"/>.</summary>
    public string? RefusalMessage =>
        CanSplit
            ? null
            : _army.Units.Count < MinUnits
                ? ArmyDialogModels.SplitOneUnitRefusal
                : "You have reached your limit of armies.";

    /// <summary>Stages one unit (index into the army's list) for the new army; the last call wins.</summary>
    public void StageUnit(int index)
    {
        if (index < 0 || index >= _army.Units.Count)
        {
            return;
        }

        _unitIndexToNewArmy = index;
    }

    /// <summary>The staged unit index, or <see langword="null"/> when none is staged.</summary>
    public int? UnitIndexToNewArmy => _unitIndexToNewArmy;

    /// <summary><c>OK</c>: one <c>split-army</c> with the staged unit, or <see langword="null"/> when none is staged.</summary>
    public string? ComposeOk() =>
        _unitIndexToNewArmy is { } index ? $"split-army {ArmyId} {NewArmyId} {index}" : null;

    /// <summary><c>Cancel</c>: nothing is submitted.</summary>
    public string? Cancel() => null;
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

    /// <summary>Rename unit composes <c>rename-unit</c>.</summary>
    public string RenameLine(int unitIndex, string name) => $"rename-unit {ArmyId} {unitIndex} {name}";

    /// <summary>Split unit composes <c>split-unit</c>.</summary>
    public string SplitUnitLine(int unitIndex, int troops) => $"split-unit {ArmyId} {unitIndex} {troops}";

    /// <summary>Join units composes <c>join-units</c>, the first index surviving.</summary>
    public string JoinUnitsLine(int firstUnitIndex, int secondUnitIndex) =>
        $"join-units {ArmyId} {firstUnitIndex} {secondUnitIndex}";

    /// <summary>Disband composes <c>disband-unit</c>.</summary>
    public string DisbandUnitLine(int unitIndex) => $"disband-unit {ArmyId} {unitIndex}";
}
