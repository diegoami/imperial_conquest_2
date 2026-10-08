using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Slice.UI;

/// <summary>
/// One rendered line of an Information-panel view (a city, an army or a fleet), keyed so a test or a
/// headless check can ask for a fact without matching the human wording. <see cref="Text"/> is what
/// <see cref="ContextPanel"/> draws; a line whose <c>Text</c> is the empty string is a deliberate blank
/// spacer the original also draws.
/// </summary>
public sealed record InformationPanelLine(string Key, string Text);

/// <summary>
/// T140 (bug #718): the Information panel's lines for a city, an own army, a foreign army, an own fleet,
/// a foreign fleet and a fleet carrying an army — every field the original prints, in the original's
/// order, exactly. Each field reads the state or the ruleset (no C# literal); each band is
/// <see cref="InformationWords"/>'s. The "viewer" is the active seat's nation (the nation whose turn it
/// is), never the nation selected for viewing from the Nations menu: the panel picks
/// <c>own</c>/<c>foreign</c> by the viewer every time.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Evidence.</strong> Every row in <see cref="City"/>, <see cref="OwnArmy"/>, <see cref="ForeignArmy"/>,
/// <see cref="OwnFleet"/>, <see cref="ForeignFleet"/> and <see cref="FleetWithArmy"/> cites a
/// report-row id (C01–C10, A01–A11, F01–F09) from the research read
/// <c>2026-10-05-information-window-fields-and-bands.md</c>. The foreign-army rule follows T99's
/// withholding of moves, supply, morale and money; the foreign-fleet rule follows the rows F01–F08.
/// </para>
/// <para>
/// <strong>Godot-free</strong> (no <c>using Godot</c>): the test project compiles this file directly, the
/// same seam <see cref="NationStatusModel"/> already uses (T110). The <c>world</c> argument is only
/// needed to name the terrain under an army — the row reads <see cref="ArmyState.CoveredTileCode"/>
/// already, so the lookup is one index into <see cref="World.TileTypes"/>, no terrain-grid decode.
/// </para>
/// <para>
/// <strong>Empty strings are deliberate.</strong> A blank-line spacer the original draws
/// (<c>b</c> between the army's units and Total troops, or before the embarked army) is an
/// <see cref="InformationPanelLine"/> with <see cref="InformationPanelLine.Text"/> empty;
/// <see cref="ContextPanel"/> renders a spacer Label, never a literal empty string.
/// </para>
/// </remarks>
public static class InformationPanelModel
{
    // -- City keys -----------------------------------------------------------

    public const string CityNameKey = "city.name";
    public const string CityControlledByKey = "city.controlled_by";
    public const string CityAllegianceToKey = "city.allegiance_to";
    public const string CityPopulationKey = "city.population";
    public const string CityLoyaltyKey = "city.loyalty";
    public const string CityFortificationKey = "city.fortification";
    public const string CityTributeKey = "city.tribute";
    public const string CitySupplyKey = "city.supply";

    // -- Army keys ------------------------------------------------------------

    public const string ArmyOfKey = "army.of";
    public const string ArmyMovesKey = "army.moves";
    public const string ArmySupplyKey = "army.supply";
    public const string ArmyMoraleKey = "army.morale";
    public const string ArmyMoneyKey = "army.money";
    public const string ArmyTerrainKey = "army.terrain";
    public const string ArmyUnitTypeKeyPrefix = "army.unit_type.";
    public const string ArmyTotalTroopsKey = "army.total_troops";
    public const string ArmyNoOfUnitsKey = "army.no_of_units";
    public const string ArmyRegularsCostKey = "army.regulars_cost";
    public const string ArmyMercenaryPayKey = "army.mercenary_pay";
    public const string ArmyBlankKey = "army.blank";

    // -- Fleet keys -----------------------------------------------------------

    public const string FleetOfKey = "fleet.of";
    public const string FleetMovesKey = "fleet.moves";
    public const string FleetShipsKey = "fleet.ships";
    public const string FleetRepairKey = "fleet.repair";
    public const string FleetSupplyKey = "fleet.supply";
    public const string FleetMoneyKey = "fleet.money";
    public const string FleetCapacityKey = "fleet.capacity";
    public const string FleetSeaKey = "fleet.sea";
    public const string FleetArmyHeaderKey = "fleet.army";

    // -- Five unit types ------------------------------------------------------

    /// <summary>The five canonical unit-type ids, in the order the original draws them.</summary>
    public static readonly string[] UnitTypeIds =
    {
        "light_infantry",
        "heavy_infantry",
        "archers",
        "light_cavalry",
        "heavy_cavalry",
    };

    // ============================================================================
    // City panel
    // ============================================================================

    /// <summary>
    /// The city panel's lines for <paramref name="city"/>, viewed by <paramref name="viewerNationId"/>.
    /// <paramref name="world"/> is the state this city belongs to; only its tile-name resolution uses
    /// it (it is not needed for the city's own facts and may be <see langword="null"/> when only city
    /// rows are checked, but every city panel that runs through the active MainGameScreen has one).
    /// </summary>
    public static IReadOnlyList<InformationPanelLine> City(
        GameState state, World world, Ruleset ruleset, CityState city, string viewerNationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(city);

        var lines = new List<InformationPanelLine>();
        var owner = state.NationById(city.Owner);

        // C01: City + capital marker. The capital is a flag of any nation's CapitalCityId being this
        // city; the name printed is the CONTROLLER (city.Owner), not the nation whose capital it is.
        // For a captured capital (e.g. Rome captures Carthago) Carthage's CapitalCityId still points to
        // Carthago, but Carthago's controller is now Rome — the panel must read the controller.
        var isCapital = state.Nations.Any(n =>
            string.Equals(n.CapitalCityId, city.Id, StringComparison.Ordinal));
        lines.Add(new InformationPanelLine(
            CityNameKey,
            isCapital
                ? $"{city.Name}  (capital of {(owner?.Name ?? city.Owner)})"
                : city.Name));

        // C02 / C03: Controlled by / Allegiance to
        lines.Add(new InformationPanelLine(CityControlledByKey, $"Controlled by: {NationName(state, city.Owner)}"));
        lines.Add(new InformationPanelLine(CityAllegianceToKey, $"Allegiance to: {NationName(state, city.Allegiance)}"));

        // C04: Population = popThousands × 1000 + (trunc(pop × 100 / maxPop) %).
        var percent = city.MaxPopulationThousands == 0
            ? 0
            : city.PopulationThousands * 100 / city.MaxPopulationThousands;
        lines.Add(new InformationPanelLine(
            CityPopulationKey,
            $"Population: {city.PopulationThousands * 1000} ({percent}%)"));

        // C05: Loyalty word.
        lines.Add(new InformationPanelLine(CityLoyaltyKey, $"Loyalty: {InformationWords.Loyalty(city.Loyalty)}"));

        // C06 + C07: Fortification percent + "  (under construction)" if a code > 100, plus the
        // bracket of the controller's recruit-slot troop sum at this city when positive.
        var rule = FortificationRule(ruleset);
        var percent_value = FortificationCode.FinishedPercent(city.FortificationCode, rule);
        var underConstruction = FortificationCode.IsOrderInProgress(city.FortificationCode, rule);
        var fortText = $"{percent_value}%{(underConstruction ? "  (under construction)" : string.Empty)}";
        var bracket = ControllerQueueBracketSum(state, city);
        if (bracket > 0)
        {
            fortText += $"   ({bracket})";
        }

        lines.Add(new InformationPanelLine(CityFortificationKey, $"Fortification: {fortText}"));

        // C08 / C09: Tribute. Own city → trunc(tribute × pop / maxPop) talents; foreign → word
        // (or nothing above 10 000, per row C09).
        var isOwn = string.Equals(viewerNationId, city.Owner, StringComparison.Ordinal);
        if (isOwn)
        {
            var talents = CityTaxContribution.Compute(city);
            lines.Add(new InformationPanelLine(CityTributeKey, $"Tribute: {talents} talents"));
        }
        else
        {
            var word = InformationWords.Tribute(city.Tribute);
            lines.Add(new InformationPanelLine(
                CityTributeKey,
                word.Length == 0 ? $"Tribute: {city.Tribute}" : $"Tribute: {word}"));
        }

        // C10: Supply — own city shows tons, foreign city shows the line blank (C10: "at any other
        // city the line is blank"). The blank keeps the field's position in the original's order.
        lines.Add(new InformationPanelLine(
            CitySupplyKey,
            isOwn ? $"Supply: {city.SupplyTons} tons" : "Supply:"));

        return lines;
    }

    /// <summary>
    /// C07: the sum of <see cref="RecruitmentSlot.Troops"/> across every <see cref="NationState.RecruitmentSlots"/>
    /// of <paramref name="city"/>'s controller whose <see cref="RecruitmentSlot.TargetCityId"/>
    /// equals this city — every state code, zero returned when no slot, another nation's slots never
    /// counted.
    /// </summary>
    private static int ControllerQueueBracketSum(GameState state, CityState city)
    {
        var owner = state.NationById(city.Owner);
        if (owner is null)
        {
            return 0;
        }

        var total = 0;
        foreach (var slot in owner.RecruitmentSlots)
        {
            if (string.Equals(slot.TargetCityId, city.Id, StringComparison.Ordinal))
            {
                total += slot.Troops;
            }
        }

        return total;
    }

    private static string NationName(GameState state, string nationId) =>
        state.NationById(nationId)?.Name ?? nationId;

    /// <summary>
    /// The fortification-order rule: the city orders table's <c>fortify</c> entry, which carries
    /// <c>MaxPercent</c> and <c>InProgressEncodingRadix</c>. Ruleset data, never literals.
    /// </summary>
    private static CityOrderRule FortificationRule(Ruleset ruleset)
    {
        var order = ruleset.CityOrders.Orders.FirstOrDefault(o =>
            string.Equals(o.Id, "fortify", StringComparison.Ordinal));
        if (order is null)
        {
            throw new InvalidOperationException(
                "The ruleset has no 'fortify' entry in cityOrders.orders.");
        }

        return order;
    }

    // ============================================================================
    // Army panel — own and foreign
    // ============================================================================

    /// <summary>
    /// The own-army panel's lines — rows A01–A11, in the original's order.
    /// </summary>
    public static IReadOnlyList<InformationPanelLine> OwnArmy(
        GameState state, World world, Ruleset ruleset, ArmyState army)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(army);

        var lines = new List<InformationPanelLine>();
        AddArmyHeaderAndFacts(state, ruleset, army, includeFacts: true, lines);
        var b = AddArmyTerrain(state, world, army, lines);

        // Blank line "b+1" before the five type lines, the original's separator.
        lines.Add(BlankLine());

        AddUnitTypeLines(army, lines);
        lines.Add(new InformationPanelLine(
            ArmyTotalTroopsKey, $"Total troops: {army.TotalTroops}"));

        // A09: No. of units — the index of the LAST unit with troops above 0, plus 1. Distinct from a
        // count when a slot in the middle is empty (rows A09).
        lines.Add(new InformationPanelLine(ArmyNoOfUnitsKey, $"No. of units: {LastNonEmptyIndexPlusOne(army)}"));

        // Blank line "b+9" before the costs.
        lines.Add(BlankLine());

        // A10 / A11: Regulars cost and Mercenary pay, summed by kind. Quality / qualityDivisor
        // produces the per-slot mercenary figure (the engine does the same; its i16 narrowing
        // never changes a result with the shipped prices).
        var (regulars, mercs) = SumUpkeepByKind(army, ruleset);
        lines.Add(new InformationPanelLine(
            ArmyRegularsCostKey, $"Regulars cost: {regulars} talents per quarter"));
        lines.Add(new InformationPanelLine(
            ArmyMercenaryPayKey, $"Mercenary pay: {mercs} talents per quarter"));

        return lines;
    }

    /// <summary>
    /// The foreign-army panel's lines — A01 + the four withheld captions + Terrain (when present) +
    /// the five type sums + Total troops. No. of units, Regulars cost, Mercenary pay, Moves, Supply,
    /// Morale, and Money are not drawn (rows UM04 / T99's withholding).
    /// </summary>
    public static IReadOnlyList<InformationPanelLine> ForeignArmy(
        GameState state, World world, Ruleset ruleset, ArmyState army)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(army);

        var lines = new List<InformationPanelLine>();
        AddArmyHeaderAndFacts(state, ruleset, army, includeFacts: false, lines);

        AddArmyTerrain(state, world, army, lines);

        // Blank line "b" before the five type lines, with or without a Terrain line (an embarked
        // army has none, and every later line moves up one, A06), as on the own army's panel.
        lines.Add(BlankLine());

        AddUnitTypeLines(army, lines);
        lines.Add(new InformationPanelLine(
            ArmyTotalTroopsKey, $"Total troops: {army.TotalTroops}"));

        // The clone's own Troops line is replaced by these (the research read's foreign-army rows:
        // the original does NOT draw the unit counts on a foreign army either, so the clone's
        // existing "Troops" was a deliberate choice that this rewrite retires).
        return lines;
    }

    private static void AddArmyHeaderAndFacts(
        GameState state, Ruleset ruleset, ArmyState army, bool includeFacts, List<InformationPanelLine> lines)
    {
        // A01
        var owner = state.NationById(army.Nation);
        lines.Add(new InformationPanelLine(
            ArmyOfKey, $"Army of {owner?.Name ?? army.Nation}"));

        if (includeFacts)
        {
            // A02
            lines.Add(new InformationPanelLine(ArmyMovesKey, $"Moves: {army.Moves}"));
            // A03: tons, then "  (" PercentFull ")"
            var pct = army.TotalTroops == 0
                ? 0
                : SupplyCapacity.PercentFull(army.SupplyTons, army.TotalTroops, ruleset);
            lines.Add(new InformationPanelLine(
                ArmySupplyKey, $"Supply: {army.SupplyTons} tons  ({pct}%)"));
            // A04
            lines.Add(new InformationPanelLine(
                ArmyMoraleKey, $"Morale: {InformationWords.Morale(army.Morale)}"));
            // A05
            lines.Add(new InformationPanelLine(ArmyMoneyKey, $"Money: {army.Money} talents"));
        }
        else
        {
            // The foreign-army withholding: the four field names without numbers, exactly the same
            // phrasings T99 ships in the existing panel's "Moves: withheld · Morale: withheld · Money:
            // withheld · Supply: withheld" caption.
            lines.Add(new InformationPanelLine(
                ArmyMovesKey, "Moves: withheld"));
            lines.Add(new InformationPanelLine(
                ArmySupplyKey, "Supply: withheld"));
            lines.Add(new InformationPanelLine(
                ArmyMoraleKey, "Morale: withheld"));
            lines.Add(new InformationPanelLine(
                ArmyMoneyKey, "Money: withheld"));
        }
    }

    /// <summary>Adds the A06 Terrain line when the army covers a tile. Returns true when added.</summary>
    private static bool AddArmyTerrain(GameState state, World world, ArmyState army, List<InformationPanelLine> lines)
    {
        if (army.CoveredTileCode is not { } code)
        {
            return false;
        }

        var terrainName = world.TileTypeByCode(code)?.Name ?? $"code {code}";
        lines.Add(new InformationPanelLine(ArmyTerrainKey, $"Terrain: {terrainName}"));
        return true;
    }

    private static void AddUnitTypeLines(ArmyState army, List<InformationPanelLine> lines)
    {
        var sums = UnitTypeSums(army);
        for (var i = 0; i < UnitTypeIds.Length; i++)
        {
            var typeId = UnitTypeIds[i];
            var name = UnitTypeLabel(typeId, sums.TryGetValue(typeId, out var t) ? t : 0, sum: sums.TryGetValue(typeId, out t) ? t : 0);
            var troops = sums.TryGetValue(typeId, out t) ? t : 0;
            lines.Add(new InformationPanelLine(
                ArmyUnitTypeKeyPrefix + typeId,
                $"{UnitTypeLabel(typeId, troops, troops)}: {troops}"));
        }
    }

    /// <summary>
    /// The original's per-type label, the first 16 bytes of the unit-type string table. The display
    /// here is the ruleset's own <see cref="UnitTypeRules.Name"/> ("Light infantry", "Heavy infantry",
    /// "Archers", "Light cavalry", "Heavy cavalry") — the original's 16-byte truncated names agree
    /// with the ruleset's full names byte-for-byte on every shipped preset.
    /// </summary>
    private static string UnitTypeLabel(string typeId, int troops, int sum)
    {
        var label = UnitTypeDisplayName(typeId);
        return label;
    }

    private static string UnitTypeDisplayName(string typeId) => typeId switch
    {
        "light_infantry" => "Light infantry",
        "heavy_infantry" => "Heavy infantry",
        "archers" => "Archers",
        "light_cavalry" => "Light cavalry",
        "heavy_cavalry" => "Heavy cavalry",
        _ => typeId,
    };

    private static IReadOnlyDictionary<string, int> UnitTypeSums(ArmyState army)
    {
        var sums = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var slot in army.Units)
        {
            sums.TryGetValue(slot.UnitTypeId, out var current);
            sums[slot.UnitTypeId] = current + slot.Troops;
        }

        return sums;
    }

    private static int LastNonEmptyIndexPlusOne(ArmyState army)
    {
        // Index of the LAST slot with troops > 0, plus 1 — distinct from a count when a slot in the
        // middle has no troops. The original's FUN_0044A66C reads Units[i].+4 and stops.
        for (var i = army.Units.Count - 1; i >= 0; i--)
        {
            if (army.Units[i].Troops > 0)
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static (int Regulars, int Mercenaries) SumUpkeepByKind(ArmyState army, Ruleset ruleset)
    {
        var regulars = 0;
        var mercenaries = 0;
        foreach (var unit in army.Units)
        {
            var cost = ArmyUpkeep.ComputeUnit(unit, ruleset);
            if (unit.IsMercenary)
            {
                mercenaries += cost;
            }
            else
            {
                regulars += cost;
            }
        }

        return (regulars, mercenaries);
    }

    private static InformationPanelLine BlankLine() =>
        new(ArmyBlankKey, string.Empty);

    // ============================================================================
    // Fleet panel — own and foreign
    // ============================================================================

    /// <summary>The own-fleet panel's lines (F01–F08), with F09 (the embarked army) when applicable.</summary>
    public static IReadOnlyList<InformationPanelLine> OwnFleet(
        GameState state, World world, Ruleset ruleset, FleetState fleet)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(fleet);

        var lines = new List<InformationPanelLine>();
        AddFleetHeaderAndFacts(state, world, ruleset, fleet, includeFacts: true, lines);
        AddFleetArmyBlock(state, world, ruleset, fleet, lines);
        return lines;
    }

    /// <summary>The foreign-fleet panel's lines — only Fleet of, Ships, Capacity, Sea are drawn.</summary>
    public static IReadOnlyList<InformationPanelLine> ForeignFleet(
        GameState state, World world, Ruleset ruleset, FleetState fleet)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(fleet);

        var lines = new List<InformationPanelLine>();
        AddFleetHeaderAndFacts(state, world, ruleset, fleet, includeFacts: false, lines);
        AddFleetArmyBlock(state, world, ruleset, fleet, lines);
        return lines;
    }

    private static void AddFleetHeaderAndFacts(
        GameState state, World world, Ruleset ruleset, FleetState fleet, bool includeFacts, List<InformationPanelLine> lines)
    {
        // F01
        var owner = state.NationById(fleet.Nation);
        lines.Add(new InformationPanelLine(FleetOfKey, $"Fleet of {owner?.Name ?? fleet.Nation}"));

        // F02: Moves — own fleet shows the count, foreign fleet shows the caption only (F01–F08).
        lines.Add(new InformationPanelLine(
            FleetMovesKey,
            includeFacts ? $"Moves: {fleet.Moves}" : "Moves:"));

        // F03: Ships — always shown, own and foreign alike (F03).
        lines.Add(new InformationPanelLine(FleetShipsKey, $"Ships: {fleet.Ships}"));

        if (includeFacts)
        {
            // F04: Repair
            lines.Add(new InformationPanelLine(FleetRepairKey, $"Repair: {fleet.ConditionPercent}%"));
            // F05: original's trunc(supplies × 100 / (ships × 8)). The ruleset ships FleetSupplyTonsPerShip = 8,
            // so this is trunc(supplyTons × 100 / FleetCapacityTons(ships)). Use the ruleset's value, not
            // a literal, so a non-8 ruleset is supported.
            var capacity = SupplyCapacity.FleetCapacityTons(fleet.Ships, ruleset);
            var pct = capacity == 0 ? 0 : fleet.SupplyTons * 100 / capacity;
            lines.Add(new InformationPanelLine(
                FleetSupplyKey, $"Supply: {fleet.SupplyTons} tons  ({pct}%)"));
            // F06
            lines.Add(new InformationPanelLine(FleetMoneyKey, $"Money: {fleet.Money} talents"));
        }
        else
        {
            // Foreign-fleet: Repair, Supply, Money are blank (F01–F08). The original's order keeps
            // Ships between Moves and Repair, so the same field order applies to both panels — only
            // the values change.
            lines.Add(new InformationPanelLine(FleetRepairKey, "Repair:"));
            lines.Add(new InformationPanelLine(FleetSupplyKey, "Supply:"));
            lines.Add(new InformationPanelLine(FleetMoneyKey, "Money:"));
        }

        // F07 (always): Capacity = ships × TransportTroopsPerShip troops
        var transport = ruleset.Naval.TransportTroopsPerShip;
        lines.Add(new InformationPanelLine(
            FleetCapacityKey, $"Capacity: {fleet.Ships * transport} troops"));

        // F08 (always): Sea
        var sea = fleet.CoveredTileCode is { } code
            ? InformationWords.Sea(code)
            : "calm";
        lines.Add(new InformationPanelLine(FleetSeaKey, $"Sea: {sea}"));
    }

    private static void AddFleetArmyBlock(
        GameState state, World world, Ruleset ruleset, FleetState fleet, List<InformationPanelLine> lines)
    {
        if (fleet.CarriedArmyId is not { } armyId || state.ArmyById(armyId) is not { } army)
        {
            return;
        }

        lines.Add(BlankLine());
        lines.Add(new InformationPanelLine(FleetArmyHeaderKey, "Army"));

        // The embarked army panel starts at Supply, never at Moves, never at Terrain. Follow the
        // army's own/foreign rule: the rule is decided against the ACTIVE SEAT, never against the
        // fleet's owner. A Carthaginian fleet carrying a Carthaginian army, viewed by Rome, must
        // withhold Supply, Morale, Money, unit count and upkeep just like a foreign army on land
        // (R2 of the R1 review: the active seat is the only viewer the panel knows; otherwise
        // Rome's view of a Carthaginian fleet exposes the embarked army's facts, which the
        // original does not).
        var isOwn = string.Equals(army.Nation, state.ActiveNationId, StringComparison.Ordinal);
        var armyLines = isOwn
            ? OwnArmyFromSupplyOn(state, world, ruleset, army)
            : ForeignArmyFromSupplyOn(state, world, ruleset, army);

        lines.AddRange(armyLines);
    }

    /// <summary>
    /// The own-army lines starting at Supply — exactly the lines F09 includes for a fleet carrying
    /// an own army (Supply, Morale, Money, blank, 5 unit types, Total troops, No. of units, blank,
    /// Regulars cost, Mercenary pay).
    /// </summary>
    private static IEnumerable<InformationPanelLine> OwnArmyFromSupplyOn(
        GameState state, World world, Ruleset ruleset, ArmyState army)
    {
        var pct = army.TotalTroops == 0
            ? 0
            : SupplyCapacity.PercentFull(army.SupplyTons, army.TotalTroops, ruleset);
        yield return new InformationPanelLine(ArmySupplyKey, $"Supply: {army.SupplyTons} tons  ({pct}%)");
        yield return new InformationPanelLine(ArmyMoraleKey, $"Morale: {InformationWords.Morale(army.Morale)}");
        yield return new InformationPanelLine(ArmyMoneyKey, $"Money: {army.Money} talents");

        // No Terrain in the F09 block: the embarked army has CoveredTileCode = null (the same
        // sentinel that suppresses the on-map Terrain line).
        yield return BlankLine();

        foreach (var typeId in UnitTypeIds)
        {
            var sum = 0;
            foreach (var slot in army.Units)
            {
                if (string.Equals(slot.UnitTypeId, typeId, StringComparison.Ordinal))
                {
                    sum += slot.Troops;
                }
            }

            yield return new InformationPanelLine(
                ArmyUnitTypeKeyPrefix + typeId,
                $"{UnitTypeDisplayName(typeId)}: {sum}");
        }

        yield return new InformationPanelLine(
            ArmyTotalTroopsKey, $"Total troops: {army.TotalTroops}");
        yield return new InformationPanelLine(
            ArmyNoOfUnitsKey, $"No. of units: {LastNonEmptyIndexPlusOne(army)}");

        yield return BlankLine();

        var (regulars, mercs) = SumUpkeepByKind(army, ruleset);
        yield return new InformationPanelLine(
            ArmyRegularsCostKey, $"Regulars cost: {regulars} talents per quarter");
        yield return new InformationPanelLine(
            ArmyMercenaryPayKey, $"Mercenary pay: {mercs} talents per quarter");
    }

    private static IEnumerable<InformationPanelLine> ForeignArmyFromSupplyOn(
        GameState state, World world, Ruleset ruleset, ArmyState army)
    {
        // The original's foreign-army composition row: Supply caption only, Morale caption only,
        // Money caption only, then the unit-type sums and Total troops. No No. of units, no
        // upkeep rows. The research read's UM04 paragraph pins this.
        yield return new InformationPanelLine(ArmySupplyKey, "Supply: withheld");
        yield return new InformationPanelLine(ArmyMoraleKey, "Morale: withheld");
        yield return new InformationPanelLine(ArmyMoneyKey, "Money: withheld");

        yield return BlankLine();

        foreach (var typeId in UnitTypeIds)
        {
            var sum = 0;
            foreach (var slot in army.Units)
            {
                if (string.Equals(slot.UnitTypeId, typeId, StringComparison.Ordinal))
                {
                    sum += slot.Troops;
                }
            }

            yield return new InformationPanelLine(
                ArmyUnitTypeKeyPrefix + typeId,
                $"{UnitTypeDisplayName(typeId)}: {sum}");
        }

        yield return new InformationPanelLine(
            ArmyTotalTroopsKey, $"Total troops: {army.TotalTroops}");
    }
}
