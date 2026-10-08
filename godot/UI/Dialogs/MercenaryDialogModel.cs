using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Slice.UI;

/// <summary>
/// One mercenary offer as the Recruit mercenaries dialog and the city's right-click listing show it —
/// the pool row's own slot index, the offer's unit type id (so the dialog can resolve its quarterly
/// price against the ruleset), the troops, quality, the unit type's display name and the dialog's
/// "Quarterly cost" box figure.
/// </summary>
/// <param name="SlotIndex">
/// The offer's <see cref="Model.MercenaryPoolSlot.SlotIndex"/> — the value the engine's
/// <c>hire-mercenary &lt;army&gt; &lt;slot&gt;</c> command names and the one the dialog's "Recruit unit"
/// button submits.
/// </param>
/// <param name="UnitTypeId">
/// The offer's <see cref="Model.MercenaryPoolSlot.UnitTypeId"/>, exactly as the engine carries it.
/// Resolved against <see cref="IC2.Engine.Model.Ruleset.UnitTypes"/> for the type's
/// <see cref="IC2.Engine.Model.UnitTypeRules.QuarterlyPrice"/>.
/// </param>
/// <param name="Troops">The offer's troop count.</param>
/// <param name="Quality">The offer's quality tier.</param>
/// <param name="TypeName">
/// The unit-type's display name (<see cref="IC2.Engine.Model.UnitTypeRules.Name"/>), for the dialog and
/// the city's listing. Falls back to <paramref name="UnitTypeId"/> when the type is not in the
/// ruleset, so the dialog still shows a line rather than nothing.
/// </param>
/// <param name="QuarterlyCostTalents">
/// The figure the dialog's "Quarterly cost" box shows — the original's <em>displayed</em>
/// <c>(troops × price × quality) div 1000</c>
/// <strong>[derived: code, the same report, the dialog's <em>displayed</em> shape; <c>tests/fixtures/corpus.json</c>
/// <c>mercenary.hireCostFormula</c>]</strong>. The hire's <em>gate</em>, <c>(troops × price div 1000) × quality</c>
/// (the original refuses the hire below it, see <see cref="IC2.Engine.Recruitment.MercenaryHireCost"/>),
/// differs: Samnite LI 3,868 q8 gives gate 24 / display 30, Etruscan HC 960 q9 gives gate 27 / display 34
/// (the user's decision of 2026-10-05, relayed on PR #758, has the dialog show the displayed figure, not
/// the actual quarterly upkeep).
/// </param>
public sealed record MercenaryOfferLine(
    int SlotIndex,
    string UnitTypeId,
    int Troops,
    int Quality,
    string TypeName,
    int QuarterlyCostTalents);

/// <summary>
/// The Recruit mercenaries dialog and the city's right-click listing, Godot-free — the offers the
/// dialog shows, the per-hire gate and cap, and the command line a hire submits. The Godot control
/// (<see cref="RecruitMercenariesDialog"/>) owns widgets and delegates to this type; the test project
/// compiles this file directly, so it must never reference <c>Godot.*</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The hired-slot sentinel is <c>troops == 0xFFFF</c></strong> — the <c>decompiled-fleet-tax-and-mercenary-formulas.md</c>
/// sentinel the engine writes on a successful hire. The dialog and the city listing both exclude it, so
/// the player's view and the dialog's row count stay in step with the live pool (the same "empty slots are
/// simply absent" convention <see cref="Model.MercenaryPoolSlot"/>'s remarks describe for this model).
/// </para>
/// <para>
/// <strong>The 20-unit cap.</strong> The clone's packed unit list applies T15's
/// <see cref="IC2.Engine.Model.ArmyManagementRules.MaxUnitsPerArmy"/>, so the dialog's own 20-unit
/// refusal is the engine's 20-unit cap. A hire that would push the army to 20 still passes the engine's
/// cap (the cap is inclusive); the dialog then closes because the brief says so
/// <strong>[derived: code, <c>TRecruitMercs_RecruitMercUnit</c> :43686, <c>if (iVar3 == 0x13 …) TRecruitMercs_OK</c>]</strong>.
/// With an army already at 20 the dialog does not open — a checked refusal <em>and</em> a no-open rule.
/// </para>
/// <para>
/// <strong>The hire gate</strong> is <see cref="IC2.Engine.Recruitment.MercenaryHireCost"/>'s own value, not
/// a charge. T143 (bug #755) makes the engine charge nothing at hire; the gate only refuses the hire when
/// the army's own purse is below it. The dialog reports the engine's gate and lets the engine refuse.
/// </para>
/// </remarks>
public sealed class MercenaryDialogModel
{
    /// <summary>
    /// The sentinel the engine writes on a hired slot — the value the dialog and the city listing must
    /// skip when reading the pool. Modelled here rather than imported from a constant so the dialog test
    /// can pin it exactly
    /// <strong>[confirmed: <c>decompiled-fleet-tax-and-mercenary-formulas.md</c>, <c>0xFFFF</c> write;
    /// <c>tests/fixtures/corpus.json</c> <c>mercenary.felsina.sentinelAfterHire</c> = 65535]</strong>.
    /// </summary>
    public const int HiredSlotSentinelTroops = 0xFFFF;

    /// <summary>The dialog's own per-hire gate, computed against the ruleset.</summary>
    public const string HireCommandVerb = "hire-mercenary";

    private readonly Ruleset _ruleset;
    private readonly List<MercenaryOfferLine> _offers = new();
    private string? _cityId;
    private (int X, int Y) _cityTile;
    private string? _cityName;
    private int _hiringArmyIdSlotCap;

    private MercenaryDialogModel(GameState state, Ruleset ruleset)
    {
        _ruleset = ruleset;
        _hiringArmyIdSlotCap = ruleset.ArmyManagement.MaxUnitsPerArmy;
    }

    /// <summary>
    /// The model the Recruit mercenaries dialog uses: the live offers of the first city at Chebyshev
    /// distance exactly 1 from <paramref name="armyId"/>, in slot order. With no such city the list is
    /// empty and the dialog does not open.
    /// </summary>
    /// <remarks>
    /// The adjacency rule is the engine's own <c>FUN_00449D08</c> — T76's gate — read here rather than
    /// reimplemented. The engine's rule "first live offer, in slot order, at distance exactly 1, on a
    /// city tile" maps onto "first city at distance exactly 1": an offer whose tile holds no city is
    /// skipped in the same scan (<c>HireMercenaryCommandHandler.FindChosenCity</c>'s own loop), so a
    /// city's absence demotes every offer on its tile and the next closer city wins. The model surfaces
    /// the same result the dialog's listing does.
    /// </remarks>
    public static MercenaryDialogModel ForArmy(GameState state, string armyId, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(armyId);
        ArgumentNullException.ThrowIfNull(ruleset);

        var model = new MercenaryDialogModel(state, ruleset);
        model.LoadForArmy(state, armyId);
        return model;
    }

    /// <summary>
    /// The model the city's right click uses: the live offers on <paramref name="cityId"/>'s tile, in
    /// slot order. The dialog and the view share one model so the heading, the columns and the cost
    /// cannot disagree between them.
    /// </summary>
    public static MercenaryDialogModel ForCity(GameState state, string cityId, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cityId);
        ArgumentNullException.ThrowIfNull(ruleset);

        var model = new MercenaryDialogModel(state, ruleset);
        model.LoadForCity(state, cityId);
        return model;
    }

    /// <summary>The ordered offers the dialog or the listing renders, one row each.</summary>
    public IReadOnlyList<MercenaryOfferLine> Offers => _offers;

    /// <summary>The hiring army's id (the dialog's view) — <see langword="null"/> for the city's view.</summary>
    public string? ArmyId { get; private set; }

    /// <summary>
    /// The city whose offers the dialog lists — the chosen city, in the dialog's view (T76's adjacency
    /// rule), or the right-clicked city (the 2026-10-05 amendment).
    /// </summary>
    public string? CityId => _cityId;

    /// <summary>The city's name for the dialog and the view's heading — never the id, never blank.</summary>
    public string? CityName => _cityName;

    /// <summary>The army's pre-hire unit count, read when the model was built (only the dialog view).</summary>
    public int ArmyUnitsBefore { get; private set; }

    /// <summary>The army's pre-hire unit cap, <see cref="IC2.Engine.Model.ArmyManagementRules.MaxUnitsPerArmy"/>.</summary>
    public int MaxUnitsPerArmy => _hiringArmyIdSlotCap;

    /// <summary>
    /// The dialog's "quarterly cost" box figure for <paramref name="offer"/> — the original's
    /// <em>displayed</em> shape <c>(troops × price × quality) div 1000</c>, computed here against the same
    /// ruleset <see cref="IC2.Engine.Recruitment.MercenaryHireCost"/> reads.
    /// </summary>
    public static int DisplayedQuarterlyCost(int troops, int price, int quality) =>
        (troops * price * quality) / _RulesetMercenaryHireTroopDivisor;

    /// <summary>The hire's gate, <c>(troops × price div 1000) × quality</c> — the engine's refusal threshold.</summary>
    public int HireGate(int troops, int price, int quality) =>
        (troops * price) / _RulesetMercenaryHireTroopDivisor * quality;

    /// <summary>
    /// Whether the dialog opens for this army: at least one offer in reach, and the army has not yet
    /// reached the unit cap. T76's "nothing in reach" returns an empty list and the dialog does not open
    /// (the brief's "With none, nothing happens"). The 20-unit refusal is the engine's own cap
    /// (T76's engine order — exception 7 in the brief's stacking), the dialog adds nothing for it.
    /// </summary>
    public bool DialogOpens =>
        ArmyId is not null && _offers.Count > 0 && ArmyUnitsBefore < _hiringArmyIdSlotCap;

    /// <summary>
    /// Whether a hire of <paramref name="offer"/> would push the army to <see cref="MaxUnitsPerArmy"/>
    /// exactly — the dialog's "hire closes" rule
    /// <strong>[derived: code, <c>TRecruitMercs_RecruitMercUnit</c> :43686, the same report]</strong>.
    /// Called on every Recruit unit press; the dialog closes itself and the screen hides the overlay.
    /// </summary>
    public bool HireFillsCap(MercenaryOfferLine offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return ArmyUnitsBefore + 1 >= _hiringArmyIdSlotCap;
    }

    /// <summary>
    /// The command line the dialog submits for <paramref name="offer"/>. The handler
    /// (<see cref="IC2.Engine.Recruitment.Commands.HireMercenaryCommandHandler"/>) does the rest,
    /// including its own refusals (purse gate, enemy city, 100,000 troops, fleet space, supply floor).
    /// The dialog never composes a second verb on the click.
    /// </summary>
    public string HireCommandLine(MercenaryOfferLine offer, string armyId)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentException.ThrowIfNullOrEmpty(armyId);
        return $"{HireCommandVerb} {armyId} {offer.SlotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>The dotted payment rate, e.g. <c>"8/quarter"</c>, the header the dialog shows with the type line.</summary>
    public static string QualityCaption(int quality) => ArmyDialogModels.QualityCaption(quality);

    private void LoadForArmy(GameState state, string armyId)
    {
        var army = state.ArmyById(armyId);
        if (army is null)
        {
            return;
        }

        ArmyId = army.Id;
        ArmyUnitsBefore = army.Units.Count;
        var chosen = FindChosenCity(state, army);
        if (chosen is null)
        {
            return;
        }

        AdoptCity(state, chosen);
        LoadOffers(state);
    }

    private void LoadForCity(GameState state, string cityId)
    {
        var city = state.CityById(cityId);
        if (city is null)
        {
            return;
        }

        ArmyId = null;
        AdoptCity(state, city);
        LoadOffers(state);
    }

    private void AdoptCity(GameState state, CityState city)
    {
        _cityId = city.Id;
        _cityTile = (city.X, city.Y);
        _cityName = string.IsNullOrEmpty(city.Name) ? city.Id : city.Name;
    }

    private void LoadOffers(GameState state)
    {
        _offers.Clear();
        foreach (var slot in state.MercenaryPool)
        {
            if (slot.X != _cityTile.X || slot.Y != _cityTile.Y)
            {
                continue;
            }

            // The hired slot's troops sentinel (0xFFFF) is the engine's own way to leave the slot's X/Y
            // intact (an empty slot's "absent" value); skip it so the player sees a live offer only,
            // and the dialog's row count matches the city's actual hires
            // [confirmed: decompiled-fleet-tax-and-mercenary-formulas.md §0xFFFF sentinel; corpus
            // mercenary.felsina.sentinelAfterHire].
            if (slot.Troops == HiredSlotSentinelTroops)
            {
                continue;
            }

            var typeName = _ruleset.UnitTypeById(slot.UnitTypeId)?.Name ?? slot.UnitTypeId;
            var price = _ruleset.UnitTypeById(slot.UnitTypeId)?.QuarterlyPrice ?? 0;
            _offers.Add(new MercenaryOfferLine(
                slot.SlotIndex,
                slot.UnitTypeId,
                slot.Troops,
                slot.Quality,
                typeName,
                DisplayedQuarterlyCost(slot.Troops, price, slot.Quality)));
        }
    }

    /// <summary>
    /// The first city at Chebyshev distance exactly 1 that has any live offer, in slot order — the
    /// engine's own <see cref="IC2.Engine.Recruitment.Commands.HireMercenaryCommandHandler.FindChosenCity"/>
    /// shape with the same skip rule ("an offer whose tile holds no city is skipped"), so the dialog and
    /// the engine cannot disagree on which city was picked.
    /// </summary>
    private static CityState? FindChosenCity(GameState state, ArmyState army)
    {
        MercenaryPoolSlot? chosen = null;
        foreach (var slot in state.MercenaryPool)
        {
            if (slot.Troops == HiredSlotSentinelTroops)
            {
                continue;
            }

            if (chosen is not null && slot.SlotIndex >= chosen.SlotIndex)
            {
                continue;
            }

            if (LandingTile.ChebyshevDistance(
                    new GridPoint(army.X, army.Y), new GridPoint(slot.X, slot.Y)) != 1)
            {
                continue;
            }

            if (CityAt(state, slot.X, slot.Y) is not null)
            {
                chosen = slot;
            }
        }

        return chosen is null ? null : CityAt(state, chosen.X, chosen.Y);
    }

    private static CityState? CityAt(GameState state, int x, int y)
    {
        foreach (var city in state.Cities)
        {
            if (city.X == x && city.Y == y)
            {
                return city;
            }
        }

        return null;
    }

    // Local copy of the divisor the engine reads as MercenaryHireTroopDivisor (1000, from
    // RecruitmentRules) — the same value the gate formula MercenaryHireCost divides by. The model's own
    // DisplayedQuarterlyCost reads it directly to keep the (troops × price × quality) / divisor shape the
    // brief pins in Done-when 2.
    private const int _RulesetMercenaryHireTroopDivisor = 1000;
}
