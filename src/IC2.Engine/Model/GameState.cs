using System.Text.Json.Serialization;

namespace IC2.Engine.Model;

/// <summary>
/// The live state tree a <see cref="Scenario"/> produces: everything that changes as the game is
/// played, and nothing that does not.
/// </summary>
/// <remarks>
/// Fully serializable and fully value-equal: every collection is a <see cref="ValueList{T}"/> and every
/// node is a record, so <c>serialize → deserialize</c> produces a tree that compares equal with
/// <c>==</c>. Static data (terrain, tile types, unit-type stats, every constant) is <em>not</em> copied
/// in here — the state references its world and ruleset by id, exactly as
/// <c>docs/game-design.md</c> describes a <c>SaveGame</c> doing.
/// </remarks>
public sealed record GameState(
    int SchemaVersion,
    string WorldId,
    string RulesetId,
    string ScenarioId,
    CalendarState Calendar,
    ValueList<string> TurnOrder,
    int ActiveSeatIndex,
    ulong RandomSeed,
    ValueList<NationState> Nations,
    ValueList<CityState> Cities,
    ValueList<ArmyState> Armies,
    ValueList<FleetState> Fleets,
    ValueList<MercenaryPoolSlot> MercenaryPool,
    DiplomaticRelations Relations,
    NewsLog NewsLog,
    PendingDiplomaticOffer? PendingOffer) : IVersionedDocument
{
    /// <summary>Finds a nation by id, or <see langword="null"/>.</summary>
    public NationState? NationById(string id) => Nations.FindById(n => n.Id, id);

    /// <summary>Finds a city by id, or <see langword="null"/>.</summary>
    public CityState? CityById(string id) => Cities.FindById(c => c.Id, id);

    /// <summary>Finds an army by id, or <see langword="null"/>.</summary>
    public ArmyState? ArmyById(string id) => Armies.FindById(a => a.Id, id);

    /// <summary>Finds a fleet by id, or <see langword="null"/>.</summary>
    public FleetState? FleetById(string id) => Fleets.FindById(f => f.Id, id);

    /// <summary>The nation id whose seat is currently active.</summary>
    [JsonIgnore]
    public string ActiveNationId => TurnOrder[ActiveSeatIndex];

    /// <summary>
    /// Counts cities <see cref="CityState.Owner"/> currently attributes to <paramref name="nationId"/>.
    /// Shared by the quarterly treasury credit's per-city term (<c>cityCount × 7</c>,
    /// <c>nation-tax-base-and-city-economy-fields.md</c>) and <c>VictoryEvaluator</c>'s own city count —
    /// two different call sites over the same live <see cref="Cities"/> list, kept as one linear scan
    /// here rather than two (follow-up #106 item 1's live-<see cref="GameState"/> half; the
    /// <c>GameStateFactory</c> count at scenario start reads <see cref="World.Cities"/> instead, a
    /// different element type, so it is not unified with this one — see that method's own remarks).
    /// </summary>
    public int CountCitiesOwnedBy(string nationId)
    {
        var count = 0;
        foreach (var city in Cities)
        {
            if (string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>Where the calendar currently stands.</summary>
/// <param name="Week">The current week value, advanced by <see cref="CalendarRules.WeekStep"/>.</param>
/// <param name="SeasonIndex">Zero-based season within the year.</param>
/// <param name="YearBc">The year, counting <em>down</em> (270 BC start).</param>
/// <param name="TurnIndex">How many turn cycles have elapsed since the scenario began.</param>
public sealed record CalendarState(int Week, int SeasonIndex, int YearBc, int TurnIndex);

/// <summary>A nation's live state.</summary>
/// <param name="Wealth">
/// Nation record <c>+0x430</c>: <c>Σ population × 3000</c> over the nation's cities, rebuilt every
/// quarter alongside <see cref="TaxBase"/> and adjusted at every city-ownership change
/// <strong>[confirmed: nation-tax-base-and-city-economy-fields.md]</strong>. This is <em>not</em> the
/// field the reparation formula reads — that is <see cref="TaxBase"/> (<c>+0x44C</c>); an earlier
/// reading of <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c> called <c>+0x44C</c>
/// "wealth", which the tax-base report corrects.
/// </param>
/// <param name="TaxBase">
/// Nation record <c>+0x44C</c>, the signed 16-bit <c>nationTaxBase</c> tax's income formula multiplies
/// by the tax rate: <em>persisted</em> state, zeroed and rebuilt from the nation's cities every quarter
/// and adjusted at every city-ownership change — never recomputed on demand, so a stored value
/// legitimately differs from a fresh rebuild between quarters (the reparation formula and the
/// trade-partner selection both read this field, not <see cref="Wealth"/>)
/// <strong>[confirmed: nation-tax-base-and-city-economy-fields.md]</strong>.
/// </param>
/// <param name="MobilizedPercent">
/// Nation record <c>+0x442</c>, <c>IC2.Data</c>'s <c>MobilizedPercent</c>: 0–100, decaying by
/// <see cref="EconomyRules.MobilizationDecayPerQuarter"/> every quarter (floored at 0) and read by
/// population growth, the unity update, and city supply production
/// <strong>[confirmed: city-population-growth.md]</strong>.
/// <para>
/// <strong>What raises it is placing a recruitment order — not mobilizing</strong> (issue #183, research
/// plan item 17, both answered by <c>decompiled-mobilization-and-mercenary-restock.md</c> §5 and
/// implemented by T55). <c>TArmyRecruits_RecruitUnit</c> applies
/// <c>min(cap, mobilized + step + (troops × scale) / wealth)</c> and
/// <c>TArmyRecruits_DisbandUnits</c> the symmetric decrease; the cap is
/// <see cref="RecruitmentRules.MobilizationCapPercent"/> and is enforced there, in
/// <see cref="Recruitment.MobilizationRate"/>. <c>FUN_0044a4e0</c> does not touch this field at all, and
/// the corpus pair confirms it: Rome stayed at 62 % across a mobilization of eleven recruits. The
/// earlier note here — that no increment had been found and the cap was therefore unenforced — was a
/// correct reading of the evidence available to T13 and T50, and is superseded by that report.
/// </para>
/// </param>
/// <param name="RecruitmentSlots">
/// The nation's active standing-recruitment queue — <c>IC2.Data</c>'s <c>SaveRecruitmentTable</c> fields
/// only (target city, unit type, troop count, readiness state code), no invented ones. Empty slots are
/// simply absent, exactly like <see cref="MercenaryPoolSlot"/>.
/// </param>
/// <param name="PopulationAtStart">Scorecard baseline — the game-over screen reports start versus end.</param>
public sealed record NationState(
    string Id,
    string Name,
    string ColorHex,
    string LeaderName,
    string? CapitalCityId,
    SeatControl Control,
    AiPersonality? Personality,
    int Treasury,
    int Unity,
    int Wealth,
    int TaxBase,
    int TaxRatePercent,
    int MobilizedPercent,
    int Population,
    int PopulationAtStart,
    int TreasuryAtStart,
    int CityCountAtStart,
    ValueList<RecruitmentSlot> RecruitmentSlots,
    bool Eliminated);

/// <summary>
/// One occupied slot in a nation's standing-recruitment queue — <c>IC2.Data</c>'s
/// <c>SaveRecruitmentTable</c> shape, and no more: a unit being trained at a city, the type it will be,
/// its troop count so far, and its readiness state code. Empty slots are simply absent from
/// <see cref="NationState.RecruitmentSlots"/>, the same convention <see cref="MercenaryPoolSlot"/> uses.
/// </summary>
/// <param name="TargetCityId">The city training this unit.</param>
/// <param name="UnitTypeId">Key into <see cref="Ruleset.UnitTypes"/>, the type being recruited.</param>
/// <param name="Troops">Troops raised so far.</param>
/// <param name="StateCode">
/// The raw readiness counter <see cref="IC2.Engine.Calendar.CityUnitStateCode"/> steps once per week — <c>0–24</c>,
/// climbing by <c>+2</c> and holding at the cap, never resetting.
/// </param>
public sealed record RecruitmentSlot(
    string TargetCityId,
    string UnitTypeId,
    int Troops,
    int StateCode);

/// <summary>
/// The single pending diplomatic offer a human seat's turn can start with — e.g. "Greece wants to trade
/// with Rome." At most one at a time; <see cref="GameState.PendingOffer"/> is <see langword="null"/> when
/// none is pending
/// <strong>[confirmed: pending-offer-block-army-split-and-naupactus.md]</strong>.
/// </summary>
/// <param name="ProposingNationId">The nation proposing the relation change.</param>
/// <param name="ProposedRelationCode">
/// The relation state being proposed, reusing <see cref="RelationStateCodes"/>' encoding (trade or
/// alliance).
/// </param>
public sealed record PendingDiplomaticOffer(
    string ProposingNationId,
    int ProposedRelationCode);

/// <summary>A city's live state.</summary>
/// <param name="FortificationCode">
/// The raw fortification word with its dual encoding; decode through <see cref="Model.FortificationCode"/>.
/// </param>
/// <param name="UnderSiege">
/// Whether the city is currently besieged, which blocks a fortify order
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong>.
/// </param>
public sealed record CityState(
    string Id,
    string Name,
    int X,
    int Y,
    string Owner,
    string Allegiance,
    int Loyalty,
    int SupplyTons,
    int FortificationCode,
    int PopulationThousands,
    int MaxPopulationThousands,
    int Tribute,
    bool UnderSiege,
    ValueList<UnitSlot> Garrison);

/// <summary>An army's live state.</summary>
/// <param name="Morale">
/// The <em>strategic</em> army morale — army record <c>+14</c>, a direct multiplier in both army-power
/// formulas <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong>. This is not
/// the per-unit tactical morale array; <c>docs/design-audit.md</c> §2.9 warns explicitly against
/// merging the two, so the tactical value is deliberately absent from the persisted model.
/// </param>
/// <param name="Money">
/// The army's own money purse, which supply purchases and mercenary hires are paid from
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong>. Its cap is
/// <see cref="EconomyRules.PurseCapPerUnit"/>, never a literal.
/// </param>
/// <param name="SupplyTons">The army's own supply stock.</param>
/// <param name="CoveredTileCode">
/// The map cell this army's marker covers (army record <c>+8</c>), or <see langword="null"/> when the
/// army is aboard a fleet and therefore off the map — the original's <c>-1</c> sentinel, modelled as
/// an absent value so no magic number is needed.
/// </param>
/// <param name="AboardFleetId">The fleet carrying this army, or <see langword="null"/>.</param>
public sealed record ArmyState(
    string Id,
    string Nation,
    int X,
    int Y,
    int Moves,
    int Morale,
    int Money,
    int SupplyTons,
    int? CoveredTileCode,
    string? AboardFleetId,
    ValueList<UnitSlot> Units)
{
    /// <summary>Total troops across every unit slot.</summary>
    [JsonIgnore]
    public int TotalTroops
    {
        get
        {
            var total = 0;
            foreach (var unit in Units)
            {
                total += unit.Troops;
            }

            return total;
        }
    }

    /// <summary>Whether this army is currently embarked on a fleet.</summary>
    [JsonIgnore]
    public bool IsEmbarked => AboardFleetId is not null;
}

/// <summary>A fleet's live state.</summary>
/// <param name="ConditionPercent">
/// Fleet record <c>+20</c> once launched: a 0–100% multiplier on naval combat strength, damaged by
/// battle and by storms, repaired at one of your own cities
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong>.
/// </param>
/// <param name="Money">The fleet's own money purse, the naval twin of <see cref="ArmyState.Money"/>.</param>
/// <param name="ConstructionTicksRemaining">
/// Ticks left on the construction countdown, or <see langword="null"/> once the fleet is launched and
/// on the map (the original's <c>0xFFFF</c> sentinel, modelled as an absent value).
/// </param>
/// <param name="BuildCityId">The city building this fleet while it is under construction.</param>
/// <param name="CarriedArmyId">The one army aboard, or <see langword="null"/>.</param>
public sealed record FleetState(
    string Id,
    string Nation,
    int X,
    int Y,
    int Moves,
    int Ships,
    int ConditionPercent,
    int Money,
    int SupplyTons,
    int? ConstructionTicksRemaining,
    string? BuildCityId,
    string? CarriedArmyId,
    int? CoveredTileCode)
{
    /// <summary>Whether this fleet is still being built and therefore not yet on the map.</summary>
    [JsonIgnore]
    public bool IsUnderConstruction => ConstructionTicksRemaining is not null;

    /// <summary>Whether this fleet is carrying an army.</summary>
    [JsonIgnore]
    public bool IsCarryingArmy => CarriedArmyId is not null;
}

/// <summary>
/// One occupied slot of the mercenary pool. Empty slots are simply absent; the pool's total slot count
/// is <see cref="RecruitmentRules.MercenaryPoolSlots"/>, ruleset data rather than a constant here.
/// </summary>
/// <param name="NameLabel">
/// The mercenary name-table index, copied into the hired unit's <see cref="UnitSlot.MercenaryLabel"/>.
/// </param>
public sealed record MercenaryPoolSlot(
    int SlotIndex,
    int NameLabel,
    string UnitTypeId,
    int Troops,
    int Quality);

/// <summary>
/// The symmetric N×N diplomatic relation matrix
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>.
/// </summary>
/// <remarks>
/// Cell values are the state codes from <see cref="RelationStateCodes"/>, and <em>negative values are
/// cooldown counters</em> that must climb back to zero before trade or alliance is possible again.
/// The matrix is symmetric by construction: the original's single setter writes both <c>[a][b]</c> and
/// <c>[b][a]</c>, and <see cref="WithRelation"/> does the same.
/// </remarks>
/// <param name="NationIds">Row/column order. N is the world's nation count, not fixed at 16.</param>
/// <param name="Matrix">Row-major N×N cells.</param>
public sealed record DiplomaticRelations(
    ValueList<string> NationIds,
    ValueList<ValueList<int>> Matrix)
{
    /// <summary>The index of a nation id in the matrix, or <c>-1</c>.</summary>
    public int IndexOf(string nationId) => NationIds.IndexOfId(id => id, nationId);

    /// <summary>The relation value between two nations.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Either nation is not in the matrix.</exception>
    public int Get(string a, string b)
    {
        var (i, j) = Resolve(a, b);
        return Matrix[i][j];
    }

    /// <summary>
    /// Returns a copy with the relation between two nations set, writing both <c>[a][b]</c> and
    /// <c>[b][a]</c> so the matrix stays symmetric.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Either nation is not in the matrix.</exception>
    public DiplomaticRelations WithRelation(string a, string b, int value)
    {
        var (i, j) = Resolve(a, b);
        var rows = new ValueList<int>[Matrix.Count];
        for (var r = 0; r < Matrix.Count; r++)
        {
            if (r != i && r != j)
            {
                rows[r] = Matrix[r];
                continue;
            }

            var cells = new int[Matrix[r].Count];
            for (var c = 0; c < cells.Length; c++)
            {
                cells[c] = Matrix[r][c];
            }

            if (r == i)
            {
                cells[j] = value;
            }

            if (r == j)
            {
                cells[i] = value;
            }

            rows[r] = ValueList<int>.Of(cells);
        }

        return this with { Matrix = ValueList<ValueList<int>>.Of(rows) };
    }

    /// <summary>Whether the matrix is square, sized to <see cref="NationIds"/>, and symmetric.</summary>
    public bool IsWellFormed()
    {
        var n = NationIds.Count;
        if (Matrix.Count != n)
        {
            return false;
        }

        for (var i = 0; i < n; i++)
        {
            if (Matrix[i].Count != n)
            {
                return false;
            }
        }

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                if (Matrix[i][j] != Matrix[j][i])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Builds an all-at-one-value matrix for the given nations.</summary>
    public static DiplomaticRelations Uniform(ValueList<string> nationIds, int value)
    {
        var rows = new ValueList<int>[nationIds.Count];
        for (var i = 0; i < nationIds.Count; i++)
        {
            var cells = new int[nationIds.Count];
            for (var j = 0; j < cells.Length; j++)
            {
                cells[j] = value;
            }

            rows[i] = ValueList<int>.Of(cells);
        }

        return new DiplomaticRelations(nationIds, ValueList<ValueList<int>>.Of(rows));
    }

    private (int I, int J) Resolve(string a, string b)
    {
        var i = IndexOf(a);
        var j = IndexOf(b);
        if (i < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(a), a, "Nation is not in the relation matrix.");
        }

        if (j < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(b), b, "Nation is not in the relation matrix.");
        }

        return (i, j);
    }
}

/// <summary>
/// The news log's storage: a ring buffer whose oldest entry is dropped once full
/// <strong>[confirmed: decompiled-news-log-identified.md]</strong>.
/// </summary>
/// <param name="MostRecentSlot">
/// The index of the most recently written slot — the original stores this rather than a record count,
/// which is why its save file loops <c>count + 1</c> times. <c>-1</c> for an empty log.
/// </param>
/// <param name="Slots">
/// Slots <c>0</c> through <see cref="MostRecentSlot"/> inclusive. The buffer's capacity is
/// <see cref="NewsLogRules.RingBufferSlots"/>, ruleset data rather than a constant here.
/// </param>
public sealed record NewsLog(int MostRecentSlot, ValueList<NewsEntry> Slots)
{
    /// <summary>An empty log.</summary>
    [JsonIgnore]
    public static NewsLog Empty { get; } = new(-1, ValueList<NewsEntry>.Empty);

    /// <summary>
    /// Appends a message, shifting the oldest entry out once the buffer is full, exactly as
    /// <c>FUN_00449240</c> does.
    /// </summary>
    /// <param name="entry">The message to append.</param>
    /// <param name="rules">The ring-buffer geometry, from the loaded ruleset.</param>
    public NewsLog Append(NewsEntry entry, NewsLogRules rules)
    {
        var capacity = rules.RingBufferSlots;
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rules), capacity, "The news ring buffer needs at least one slot.");
        }

        // Fullness is decided from the number of slots actually held, not from MostRecentSlot: the two
        // agree for any log this type produced (and GameDataValidation requires them to agree for any
        // log it loaded), but keying off the payload rather than the index means a log that somehow
        // arrives inconsistent still cannot grow past the ring buffer's capacity.
        var current = Slots.ToList();
        while (current.Count >= capacity)
        {
            current.RemoveAt(0);
        }

        current.Add(entry);
        return new NewsLog(current.Count - 1, ValueList.From(current));
    }

    /// <summary>
    /// Whether <see cref="MostRecentSlot"/> addresses the last of <see cref="Slots"/>, which is the
    /// invariant every log this type produces holds and the one the loader enforces.
    /// </summary>
    public bool IsConsistent() => MostRecentSlot == Slots.Count - 1;
}

/// <summary>One news-log message.</summary>
public sealed record NewsEntry(string Text);
