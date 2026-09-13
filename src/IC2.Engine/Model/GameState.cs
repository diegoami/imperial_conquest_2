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
    NewsLog NewsLog) : IVersionedDocument
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
}

/// <summary>Where the calendar currently stands.</summary>
/// <param name="Week">The current week value, advanced by <see cref="CalendarRules.WeekStep"/>.</param>
/// <param name="SeasonIndex">Zero-based season within the year.</param>
/// <param name="YearBc">The year, counting <em>down</em> (270 BC start).</param>
/// <param name="TurnIndex">How many turn cycles have elapsed since the scenario began.</param>
public sealed record CalendarState(int Week, int SeasonIndex, int YearBc, int TurnIndex);

/// <summary>A nation's live state.</summary>
/// <param name="Wealth">
/// The nation's wealth field, which the reparation formula reads
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>.
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
    int TaxRatePercent,
    int Population,
    int PopulationAtStart,
    int TreasuryAtStart,
    int CityCountAtStart,
    bool Eliminated);

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
