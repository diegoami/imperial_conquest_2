using System.Globalization;
using IC2.Data;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Engine.Import;

/// <summary>
/// T185: maps a native <see cref="SaveGame"/> onto an <see cref="OriginalSaveDocument"/> the original game
/// can load — the inverse of <see cref="OriginalSaveImporter"/>, through T184's canonical encoder.
/// </summary>
/// <remarks>
/// <para><strong>Supported state.</strong> The shipped <c>classical-mediterranean</c> world and scenario under
/// the shipped <c>classical-faithful</c> ruleset, strategic state only. An open tactical battle, another world,
/// ruleset or scenario, an id the world does not know and any value the original cannot hold are refused
/// with a typed reason (<see cref="OriginalSaveExportRefusal"/>) before anything is written. Nothing is
/// coerced: no turn is advanced, no controller is changed, no value is clamped.</para>
/// <para><strong>Where each field comes from</strong> (research <c>f878614</c>'s gate-2 policy and the SAV atlas,
/// research <c>f83a886</c>):</para>
/// <list type="bullet">
/// <item>Live state — cities, armies, fleets, nations, relations, neighbours, recruitment, mercenaries, news,
/// calendar, turn order and the pending offer — from the native state, in its own record order (the import
/// order, so a fleet's carried-army index points at the army's position in the exported table).</item>
/// <item>Derived fields, recomputed: each nation's city list in city-name byte order and its city count, the
/// city markers (population tier 0–3 at 25/50/100, or 4 for a capital), the army markers (band at 25,000 and
/// 50,000 troops) and the fleet markers (band at 25 and 50 ships), and each army's and launched fleet's
/// covered cell (what its marker hides: a city marker, the rough-sea code 1, or the world's terrain).</item>
/// <item>The map: the world's own terrain with this week's rough sea (code 1, painted on code-0 cells only, as
/// the weekly overlay does) and the markers above.</item>
/// <item>History, presentation only: <c>wealth0</c>, <c>treasury0</c> and <c>cities0</c> from the world's 270 BC
/// start, <c>conqueror</c> from <see cref="NationState.ConqueredBy"/>.</item>
/// <item>Each nation's three battle colours from the world's <c>battleColorsHex</c> (read from an original save's
/// own words).</item>
/// <item>Interface settings — each nation's window geometry, viewport, map filter, hints, battle delays and
/// the main window geometry — from the <see cref="OriginalSaveUiProfile"/> (normally a template save's), unchanged: they are the player's preferences, not game
/// state.</item>
/// <item>Canonical values for what the native state does not hold: no AI fleet waypoint (route x −1, y 0); a
/// fleet under construction with position, moves, supplies, money and cell 0; empty mercenary slots with
/// troops −1 at (0, 0); the INERT nation bytes zero; no stale offer type (0 when no offer is pending).</item>
/// </list>
/// </remarks>
public static class OriginalSaveExporter
{
    /// <summary>The only scenario an export supports: the start the history fields come from.</summary>
    public const string RequiredScenarioId = "classical-mediterranean";

    private const int NationCount = 16;
    private const string MercenaryAliasPrefix = "Mercenary unit (label ";

    /// <summary>Exports a live session without saving JSON, advancing play or changing its native save path.</summary>
    public static OriginalSaveExportResult ToDocument(GameSession session, OriginalSaveUiProfile ui)
    {
        var save = SnapshotForExport(session);
        return ToDocument(save, session.World, session.Ruleset, ui);
    }

    /// <summary>Writes a live strategic session through the same checked converter as a saved JSON.</summary>
    public static OriginalSaveExportFileResult ExportFile(GameSession session, string templatePath, string outputPath)
    {
        var save = SnapshotForExport(session);
        return ExportFile(save, session.World, session.Ruleset, templatePath, outputPath);
    }

    private static SaveGame SnapshotForExport(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var refusals = new List<OriginalSaveExportRefusal>();
        if (session.OpenTacticalBattle is not null)
            refusals.Add(new(OriginalSaveExportRefusal.OpenBattle, "battle", "finish the tactical battle before export"));
        if (session.HasPendingPeaceOffers)
            refusals.Add(new(OriginalSaveExportRefusal.PendingPeaceOffer, "peaceOffer", "answer the pending peace offer before export"));
        if (refusals.Count > 0) throw new OriginalSaveExportException(refusals);

        var state = session.State;
        return new SaveGame(GameDataSchema.CurrentVersion, "live-export", "Live export",
            state.ScenarioId, state.WorldId, state.RulesetId, state,
            ArmyTravelTargets: session.ArmyTravelTargets.Count == 0 ? null : session.ArmyTravelTargets,
            RetreatSupplyPlans: session.RetreatSupplyPlans.Count == 0 ? null : session.RetreatSupplyPlans);
    }

    /// <summary>Maps <paramref name="save"/> to a document, or throws <see cref="OriginalSaveExportException"/>
    /// listing every refusal found.</summary>
    /// <param name="save">The native save (its <see cref="SaveGame.State"/> and any open battle).</param>
    /// <param name="world">The world the save runs on — must be <see cref="OriginalSaveImporter.RequiredWorldId"/>.</param>
    /// <param name="ruleset">The ruleset — must be <see cref="OriginalSaveImporter.RequiredRulesetId"/>.</param>
    /// <param name="ui">The interface settings to write, usually <see cref="OriginalSaveUiProfile.FromTemplate"/> of
    /// an original save.</param>
    public static OriginalSaveExportResult ToDocument(SaveGame save, World world, Ruleset ruleset, OriginalSaveUiProfile ui)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(ui);
        if (ui.Nations is null || ui.Nations.Count != NationCount)
            throw new ArgumentException("The interface profile needs 16 nations.", nameof(ui));

        var refusals = new List<OriginalSaveExportRefusal>();
        void Refuse(string code, string field, string detail) => refusals.Add(new(code, field, detail));
        var state = save.State;

        if (!string.Equals(world.Id, OriginalSaveImporter.RequiredWorldId, StringComparison.Ordinal) ||
            !string.Equals(state.WorldId, OriginalSaveImporter.RequiredWorldId, StringComparison.Ordinal))
            Refuse(OriginalSaveExportRefusal.UnsupportedWorld, "worldId", $"'{state.WorldId}' is not {OriginalSaveImporter.RequiredWorldId}");
        if (!string.Equals(ruleset.Id, OriginalSaveImporter.RequiredRulesetId, StringComparison.Ordinal) ||
            !string.Equals(state.RulesetId, OriginalSaveImporter.RequiredRulesetId, StringComparison.Ordinal))
            Refuse(OriginalSaveExportRefusal.UnsupportedRuleset, "rulesetId", $"'{state.RulesetId}' is not {OriginalSaveImporter.RequiredRulesetId}");
        if (!string.Equals(state.ScenarioId, RequiredScenarioId, StringComparison.Ordinal))
            Refuse(OriginalSaveExportRefusal.UnsupportedScenario, "scenarioId", $"'{state.ScenarioId}' is not {RequiredScenarioId}");
        if (save.Battle is not null)
            Refuse(OriginalSaveExportRefusal.OpenBattle, "battle", "a tactical battle is open; finish it, then export");
        if (world.Nations.Count != NationCount || world.Cities.Count != OriginalSaveDocument.CityCount
            || world.Width != OriginalSaveDocument.MapWidth || world.Height != OriginalSaveDocument.MapHeight)
            Refuse(OriginalSaveExportRefusal.UnsupportedWorld, "world", "the world's shape is not the original's");
        if (refusals.Count > 0) throw new OriginalSaveExportException(refusals);

        var nationCode = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < world.Nations.Count; i++) nationCode[world.Nations[i].Id] = i;
        var cityIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < world.Cities.Count; i++) cityIndex[world.Cities[i].Id] = i;

        int Nation(string? id, string field)
        {
            if (id is not null && nationCode.TryGetValue(id, out var code)) return code;
            Refuse(OriginalSaveExportRefusal.UnknownReference, field, $"nation '{id}' is not one of the world's 16");
            return 0;
        }

        int City(string? id, string field)
        {
            if (id is not null && cityIndex.TryGetValue(id, out var index)) return index;
            Refuse(OriginalSaveExportRefusal.UnknownReference, field, $"city '{id}' is not one of the world's 334");
            return 0;
        }

        int TypeCode(string typeId, string field)
        {
            var code = Array.IndexOf(UnitTypeIds, typeId);
            if (code < 0) Refuse(OriginalSaveExportRefusal.UnknownReference, field, $"unit type '{typeId}' has no original code");
            return Math.Max(code, 0);
        }

        // ---- Cities, in the world's (= the original's) table order.
        if (state.Cities.Count != OriginalSaveDocument.CityCount)
            Refuse(OriginalSaveExportRefusal.UnsupportedWorld, "cities", $"{state.Cities.Count} cities, not 334");
        var stateCities = new CityState?[OriginalSaveDocument.CityCount];
        foreach (var city in state.Cities) stateCities[City(city.Id, $"city '{city.Id}'")] = city;
        if (stateCities.Any(c => c is null))
            Refuse(OriginalSaveExportRefusal.UnsupportedWorld, "cities", "the state does not hold every world city");
        if (refusals.Count > 0) throw new OriginalSaveExportException(refusals);

        var capitals = new HashSet<string>(state.Nations.Where(n => n.CapitalCityId is not null).Select(n => n.CapitalCityId!), StringComparer.Ordinal);
        var cities = new OriginalCity[OriginalSaveDocument.CityCount];
        for (var c = 0; c < cities.Length; c++)
        {
            var s = stateCities[c]!;
            cities[c] = new OriginalCity(s.Name, s.X, s.Y, Nation(s.Owner, $"city '{s.Id}'.owner"), Nation(s.Allegiance, $"city '{s.Id}'.allegiance"),
                s.Loyalty, s.SupplyTons, s.FortificationCode, s.PopulationThousands, s.MaxPopulationThousands, s.Tribute);
        }

        // ---- The map: terrain, this week's rough sea, then the city markers.
        var terrain = world.Terrain.Decode(world.Width, world.Height);
        var map = new short[OriginalSaveDocument.MapCells];
        for (var x = 0; x < world.Width; x++)
            for (var y = 0; y < world.Height; y++)
                map[x * world.Height + y] = (short)terrain[y * world.Width + x];
        var roughCode = ruleset.Economy.Weather.RoughSeaTileCode;
        foreach (var cell in state.RoughSeaCells ?? ValueList<RoughSeaCell>.Empty)
            if (OnMap(cell.X, cell.Y) && map[cell.X * world.Height + cell.Y] == 0)
                map[cell.X * world.Height + cell.Y] = (short)roughCode;
        for (var c = 0; c < cities.Length; c++)
        {
            var city = cities[c];
            var variant = capitals.Contains(stateCities[c]!.Id) ? 4
                : (uint)city.Population < 25 ? 0 : (uint)city.Population < 50 ? 1 : (uint)city.Population < 100 ? 2 : 3;
            if (OnMap(city.X, city.Y)) map[city.X * world.Height + city.Y] = (short)(20 + city.Owner + 16 * variant);
        }

        // What each unit marker hides: the map as it stands before any unit marker is drawn.
        var underlay = (short[])map.Clone();

        // ---- Armies, in state order (the exported index).
        var armyIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var a = 0; a < state.Armies.Count; a++) armyIndex[state.Armies[a].Id] = a;
        var fleetCarrying = new Dictionary<string, int>(StringComparer.Ordinal);
        var aliases = new List<OriginalSaveExportAlias>();
        var armies = new OriginalArmy[state.Armies.Count];
        for (var a = 0; a < armies.Length; a++)
        {
            var s = state.Armies[a];
            var owner = Nation(s.Nation, $"army '{s.Id}'.nation");
            var units = new OriginalUnitSlot[s.Units.Count];
            for (var u = 0; u < units.Length; u++)
            {
                var unit = s.Units[u];
                if (unit.Quality is < 4 or > 9)
                    Refuse(OriginalSaveExportRefusal.Unrepresentable, $"army[{a}].unit[{u}].quality",
                        $"quality {unit.Quality} is outside the original live-unit range 4..9");
                var name = ExportName(unit, s.Id, u, aliases);
                units[u] = new OriginalUnitSlot(unit.MercenaryLabel, TypeCode(unit.UnitTypeId, $"army '{s.Id}'.unit[{u}].type"),
                    unit.Troops, unit.Quality, name);
            }

            var aboard = s.AboardFleetId is not null;
            var cell = aboard ? -1 : OnMap(s.X, s.Y) ? underlay[s.X * world.Height + s.Y] : 0;
            if (!aboard && OnMap(s.X, s.Y))
            {
                var troops = s.Units.Sum(x => x.Troops) / 1000;
                map[s.X * world.Height + s.Y] = (short)(owner + (troops < 25 ? 200 : troops < 50 ? 216 : 232));
            }

            armies[a] = new OriginalArmy(s.X, s.Y, owner, s.Moves, cell, s.SupplyTons, s.Money, s.Morale, units);
        }

        // ---- Fleets, in state order.
        var fleets = new OriginalFleet[state.Fleets.Count];
        for (var f = 0; f < fleets.Length; f++)
        {
            var s = state.Fleets[f];
            var owner = Nation(s.Nation, $"fleet '{s.Id}'.nation");
            if (s.ConstructionTicksRemaining is { } ticks)
            {
                // Gate-2: moves 0 and route x −1; position, supplies and money are the record's own words
                // (launch overwrites supplies and condition), kept as the state holds them.
                fleets[f] = new OriginalFleet(s.X, s.Y, -1, 0, owner, ticks, 0, s.SupplyTons, s.Money, s.Ships,
                    City(s.BuildCityId, $"fleet '{s.Id}'.buildCity"), -1, 0);
                continue;
            }

            var carried = -1;
            if (s.CarriedArmyId is { } carriedId)
            {
                if (armyIndex.TryGetValue(carriedId, out var index)) carried = index;
                else Refuse(OriginalSaveExportRefusal.UnknownReference, $"fleet '{s.Id}'.carriedArmy", $"army '{carriedId}' does not exist");
            }

            var cell = OnMap(s.X, s.Y) ? underlay[s.X * world.Height + s.Y] : 0;
            if (OnMap(s.X, s.Y)) map[s.X * world.Height + s.Y] = (short)(owner + (s.Ships < 25 ? 300 : s.Ships < 50 ? 316 : 332));
            fleets[f] = new OriginalFleet(s.X, s.Y, -1, 0, owner, -1, s.Moves, s.SupplyTons, s.Money, s.Ships,
                s.ConditionPercent, carried, cell);
        }

        // ---- Nations, in the world's (= the original's) code order.
        var stateNations = new NationState?[NationCount];
        foreach (var nation in state.Nations) stateNations[Nation(nation.Id, $"nation '{nation.Id}'")] = nation;
        if (stateNations.Any(n => n is null))
            Refuse(OriginalSaveExportRefusal.UnsupportedWorld, "nations", "the state does not hold all 16 nations");
        if (refusals.Count > 0) throw new OriginalSaveExportException(refusals);

        var nations = new OriginalNation[NationCount];
        for (var n = 0; n < NationCount; n++)
        {
            var s = stateNations[n]!;
            var definition = world.Nations[n];
            var owned = Enumerable.Range(0, cities.Length).Where(c => cities[c].Owner == n)
                .OrderBy(c => cities[c].Name, StringComparer.Ordinal).ToArray();
            var relations = Enumerable.Range(0, NationCount).Select(k => state.Relations.Get(s.Id, world.Nations[k].Id)).ToArray();

            if (s.RecruitmentSlots.Count > OriginalSaveDocument.RecruitCount)
                Refuse(OriginalSaveExportRefusal.Unrepresentable, $"nation '{s.Id}'.recruitment", $"{s.RecruitmentSlots.Count} entries; the original holds 40");
            var recruits = new OriginalRecruitEntry[OriginalSaveDocument.RecruitCount];
            for (var e = 0; e < recruits.Length; e++)
            {
                recruits[e] = e < s.RecruitmentSlots.Count
                    ? new OriginalRecruitEntry(s.RecruitmentSlots[e].StateCode, TypeCode(s.RecruitmentSlots[e].UnitTypeId, $"nation '{s.Id}'.recruitment[{e}]"),
                        s.RecruitmentSlots[e].Troops, City(s.RecruitmentSlots[e].TargetCityId, $"nation '{s.Id}'.recruitment[{e}].city"))
                    : new OriginalRecruitEntry(0, 0, 0, 0);
            }

            var startCities = world.Cities.Count(c => string.Equals(c.Owner, definition.Id, StringComparison.Ordinal));
            var conqueror = s.ConqueredBy is { } by ? Nation(by, $"nation '{s.Id}'.conqueredBy") : -1;
            nations[n] = new OriginalNation(
                s.Name, s.LeaderName, relations, NeighbourMask(state, world, s.Id, nationCode), owned,
                recruits, BattleColours(definition, refusals), s.Wealth, definition.Wealth, s.Treasury, definition.Treasury,
                s.Unity, s.MobilizedPercent, s.CapitalCityId is { } capital ? City(capital, $"nation '{s.Id}'.capital") : -1,
                owned.Length, startCities, s.TaxRatePercent, s.TaxBase, conqueror,
                ui.Nations[n], s.Control == SeatControl.Human ? 1 : 0);
        }

        // ---- Mercenaries: each slot at its own index; the rest empty.
        var mercenaries = Enumerable.Range(0, OriginalSaveDocument.MercenaryCount).Select(_ => new OriginalMercenary(0, 0, 0, 0, -1, 0)).ToArray();
        foreach (var m in state.MercenaryPool)
        {
            if (m.SlotIndex is < 0 or >= OriginalSaveDocument.MercenaryCount)
            {
                Refuse(OriginalSaveExportRefusal.Unrepresentable, $"mercenary slot {m.SlotIndex}", "outside the original's 50 slots");
                continue;
            }

            mercenaries[m.SlotIndex] = new OriginalMercenary(m.X, m.Y, m.NameLabel, TypeCode(m.UnitTypeId, $"mercenary[{m.SlotIndex}].type"), m.Troops, m.Quality);
        }

        // ---- Trailer: turn order, offer, calendar; the main window geometry from the template.
        var turnOrder = state.TurnOrder.Select((id, i) => Nation(id, $"turnOrder[{i}]")).ToArray();
        var offer = state.PendingOffer;
        var trailer = new OriginalTrailer(turnOrder,
            offer is null ? -1 : Nation(offer.ProposingNationId, "pendingOffer.from"), offer?.ProposedRelationCode ?? 0,
            turnOrder.Length > state.ActiveSeatIndex && state.ActiveSeatIndex >= 0 ? turnOrder[state.ActiveSeatIndex] : 0,
            state.ActiveSeatIndex, state.Calendar.Week, state.Calendar.YearBc, state.Calendar.SeasonIndex,
            ui.MainLeft, ui.MainTop, ui.MainClientHeight, ui.MainClientWidth);

        if (refusals.Count > 0) throw new OriginalSaveExportException(refusals);
        var document = new OriginalSaveDocument(map, cities, armies, fleets, nations, mercenaries,
            state.NewsLog.Slots.Select(s => s.Text).ToArray(), trailer);

        // Encodability and the gate-2 policy, both before any byte is written by a caller.
        try { OriginalSaveCodec.Validate(document); }
        catch (OriginalSaveEncodingException ex) { throw new OriginalSaveExportException([new(OriginalSaveExportRefusal.Unrepresentable, ex.Field, ex.Reason)]); }
        var violations = OriginalSaveExportPolicy.Check(document);
        if (violations.Count > 0)
            throw new OriginalSaveExportException(violations.Select(v => new OriginalSaveExportRefusal(OriginalSaveExportRefusal.PolicyViolation, v.Where, v.Rule)).ToList());

        var omitted = OriginalSaveExportReport.OmittedNativeMetadata.ToList();
        if (save.ArmyTravelTargets is { Count: > 0 })
            omitted.Add("ArmyTravelTargets: campaign route plans cannot be encoded in the original save");
        if (save.RetreatSupplyPlans is { Count: > 0 })
            omitted.Add("RetreatSupplyPlans: per-unit recovery modes cannot be encoded in the original save");
        return new OriginalSaveExportResult(document, new OriginalSaveExportReport(aliases, omitted));
    }

    /// <summary>
    /// The offline converter's whole operation: reads the template (an original save, for its interface
    /// settings only), maps <paramref name="save"/>, and writes a new file at <paramref name="outputPath"/>
    /// without replacing anything. Refusals throw <see cref="OriginalSaveExportException"/>; nothing is written.
    /// </summary>
    public static OriginalSaveExportFileResult ExportFile(SaveGame save, World world, Ruleset ruleset, string templatePath, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (comparer.Equals(Path.GetFullPath(templatePath), Path.GetFullPath(outputPath)))
            throw new IOException("The output path is the template's.");
        var templateBytes = File.ReadAllBytes(templatePath);
        var template = OriginalSaveCodec.Decode(templateBytes);
        var result = ToDocument(save, world, ruleset, OriginalSaveUiProfile.FromTemplate(template));
        var written = OriginalSaveWriter.WriteDocument(result.Document, outputPath);
        return new OriginalSaveExportFileResult(result, written.DestinationPath, written.OutputSha256, written.Length,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(templateBytes)), OriginalSaveDocumentWriteReport.ProfileId);
    }

    /// <summary>The original's unit type codes 0–4, as <see cref="OriginalSaveImporter"/> reads them.</summary>
    private static readonly string[] UnitTypeIds = ["light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry"];

    private static bool OnMap(int x, int y) =>
        x is >= 0 and < OriginalSaveDocument.MapWidth && y is >= 0 and < OriginalSaveDocument.MapHeight;

    /// <summary>
    /// The unit's name as the original stores it. The one alias T185 allows: a generated mercenary placeholder
    /// <c>Mercenary unit (label N)</c>, N the unit's own nonzero label, too long for the original's 23 bytes,
    /// is written <c>Mercenary (label N)</c>. Every other name is written as it is (and refused if it does not fit).
    /// </summary>
    private static string ExportName(UnitSlot unit, string armyId, int slot, List<OriginalSaveExportAlias> aliases)
    {
        var name = unit.Name;
        if (name.Length <= OriginalSaveCodec.UnitNameBytes - 1 || unit.MercenaryLabel == 0) return name;
        var label = unit.MercenaryLabel.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(name, $"{MercenaryAliasPrefix}{label})", StringComparison.Ordinal)) return name;
        var alias = $"Mercenary (label {label})";
        aliases.Add(new OriginalSaveExportAlias(armyId, slot, name, alias));
        return alias;
    }

    private static int NeighbourMask(GameState state, World world, string nationId, Dictionary<string, int> codes)
    {
        var entry = state.Neighbours?.FirstOrDefault(n => string.Equals(n.NationId, nationId, StringComparison.Ordinal))
            ?? world.StartingNeighbours?.FirstOrDefault(n => string.Equals(n.NationId, nationId, StringComparison.Ordinal));
        var mask = 0;
        foreach (var id in entry?.NeighbourIds ?? ValueList<string>.Empty)
            if (codes.TryGetValue(id, out var code)) mask |= 1 << code;
        return mask;
    }

    /// <summary>The world's three battle colours as the Delphi colour words the record holds (0x00BBGGRR).</summary>
    private static uint[] BattleColours(NationDefinition definition, List<OriginalSaveExportRefusal> refusals)
    {
        var hex = definition.BattleColorsHex;
        if (hex is null || hex.Count != 3)
        {
            refusals.Add(new(OriginalSaveExportRefusal.UnsupportedWorld, $"nation '{definition.Id}'.battleColorsHex", "the world needs three battle colours"));
            return [0, 0, 0];
        }

        return hex.Select(h =>
        {
            var rgb = uint.Parse(h.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
        }).ToArray();
    }
}

/// <summary>A typed reason an export was refused, naming the field.</summary>
public sealed record OriginalSaveExportRefusal(string Code, string Field, string Detail)
{
    public const string UnsupportedWorld = "UNSUPPORTED_WORLD";
    public const string UnsupportedRuleset = "UNSUPPORTED_RULESET";
    public const string UnsupportedScenario = "UNSUPPORTED_SCENARIO";
    public const string OpenBattle = "OPEN_BATTLE";
    public const string PendingPeaceOffer = "PENDING_PEACE_OFFER";
    public const string UnknownReference = "UNKNOWN_REFERENCE";
    public const string Unrepresentable = "UNREPRESENTABLE";
    public const string PolicyViolation = "POLICY_VIOLATION";
}

/// <summary>The export could not be made; nothing was written.</summary>
public sealed class OriginalSaveExportException(IReadOnlyList<OriginalSaveExportRefusal> refusals)
    : Exception("The game cannot be exported as an original save: "
        + string.Join("; ", refusals.Take(8).Select(r => $"{r.Code} {r.Field}: {r.Detail}"))
        + (refusals.Count > 8 ? $" (and {refusals.Count - 8} more)." : "."))
{
    public IReadOnlyList<OriginalSaveExportRefusal> Refusals { get; } = refusals;
}

/// <summary>One unit name written under the mercenary alias, with its army and slot.</summary>
public sealed record OriginalSaveExportAlias(string ArmyId, int Slot, string NativeName, string ExportedName);

/// <summary>What <see cref="OriginalSaveExporter.ExportFile"/> wrote.</summary>
public sealed record OriginalSaveExportFileResult(
    OriginalSaveExportResult Export, string OutputPath, string OutputSha256, int Length, string TemplateSha256, string ProfileId);

/// <summary>The document and what the export did beyond a field-for-field copy.</summary>
public sealed record OriginalSaveExportResult(OriginalSaveDocument Document, OriginalSaveExportReport Report);

/// <summary>
/// What an export wrote under an alias and what native metadata the original format cannot hold.
/// </summary>
public sealed record OriginalSaveExportReport(IReadOnlyList<OriginalSaveExportAlias> Aliases, IReadOnlyList<string> Omitted)
{
    /// <summary>Native fields with no original equivalent, never written.</summary>
    public static IReadOnlyList<string> OmittedNativeMetadata { get; } =
    [
        "schema and envelope versions, save id and label, provenance",
        "world, ruleset and scenario ids (an original save is always classical-mediterranean under classical-faithful)",
        "Calendar.TurnIndex (the original keeps no elapsed-turn counter)",
        "RandomSeed and draw positions: the original does not restore a random state on load, so future dice differ",
        "AI personalities, seat colours and the leader-name pools",
        "NationState.Population (native cached total; the original derives population from cities)",
        "PopulationAtStart, TreasuryAtStart and CityCountAtStart (score baselines); the original's own start fields get the 270 BC values",
        "CityState.Garrison (the original city record has no independent garrison unit table)",
        "the first-end news bookkeeping (FirstEndNewsOwed)",
        "runtime settings: Computer general, battle speed, display pacing",
        "army covered-tile codes, recomputed from the map rather than copied",
        "recruitment slot positions (the native state keeps the entries in order; they are written packed from slot 0)",
    ];
}
