using System.Text;
using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Engine.Presentation;

/// <summary>
/// Everything <see cref="GameSession"/> prints. Split into its own file purely for readability — it is
/// still the same class, and it holds exactly the same rule: nothing here computes a gameplay number,
/// every one is read off <see cref="GameSession.State"/> or <see cref="GameSession.Ruleset"/>. The display
/// labels below (season names, map glyphs) are presentation text, not gameplay data — they carry no cost,
/// threshold or formula, and a custom ruleset with a different <c>SeasonsPerYear</c> or an unrecognised
/// terrain name still renders, just with a plainer fallback label.
/// </summary>
/// <remarks>
/// <strong>Season names come from <see cref="Ruleset.NewsLog"/>'s own table — T23 follow-up
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/100">#100</see> item 2.</strong> This
/// used to carry its own hardcoded four-name array, duplicating
/// <see cref="Model.NewsLogRules.SeasonNames"/>, which T29's export and <see cref="Model.Ruleset.ValidateSeasonNames"/>
/// already keep in step with <see cref="Model.CalendarRules.SeasonsPerYear"/>. A ruleset that ships more
/// or fewer seasons than four now renders correctly here too, instead of silently falling back to
/// "Season N" past a stale literal four.
/// </remarks>
public sealed partial class GameSession
{
    /// <summary>Map glyphs by a tile type's display <see cref="TileType.Name"/> — cosmetic only.</summary>
    private static readonly Dictionary<string, char> TerrainGlyphsByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sea"] = '~',
        ["Plain"] = '.',
        ["Desert"] = ',',
        ["Forest"] = '"',
        ["Mountains"] = '^',
        ["River"] = '=',
    };

    private IReadOnlyList<string> RenderStatus()
    {
        var lines = new List<string>();
        var cal = State.Calendar;
        lines.Add(
            $"Week {cal.Week}, {SeasonName(cal.SeasonIndex)} {cal.YearBc} BC (turn {cal.TurnIndex}). "
            + $"Active seat: {NationDisplay(State.ActiveNationId)}.");

        lines.Add(string.Empty);
        lines.Add("Nations:");
        foreach (var nation in State.Nations)
        {
            var eliminated = nation.Eliminated ? " [eliminated]" : string.Empty;
            lines.Add(
                $"  {nation.Name} ({nation.Id}, {ControlLabel(nation.Control)}): "
                + $"treasury {nation.Treasury}, unity {nation.Unity}, tax {nation.TaxRatePercent}%{eliminated}");
        }

        lines.Add(string.Empty);
        lines.Add("Armies:");
        foreach (var army in State.Armies)
        {
            lines.Add(FormatArmyLine(army));
        }

        lines.Add(string.Empty);
        lines.Add("Cities:");
        var fortifyRule = FortifyRule();
        foreach (var city in State.Cities)
        {
            lines.Add(FormatCityLine(city, fortifyRule));
        }

        return lines;
    }

    /// <summary>
    /// <c>status</c> with no argument, or <c>status mine</c> — <c>docs/tasks/T83.md</c> Done-when 4. Any
    /// other trailing token is a usage error, the same shape every other malformed command in this class
    /// already returns rather than throwing.
    /// </summary>
    private IReadOnlyList<string> RenderStatusCommand(string[] tokens)
    {
        if (tokens.Length == 1)
        {
            return RenderStatus();
        }

        if (tokens.Length == 2 && string.Equals(tokens[1], "mine", StringComparison.OrdinalIgnoreCase))
        {
            return RenderStatusMine();
        }

        return new[] { "Usage: status [mine]" };
    }

    /// <summary>
    /// <c>status mine</c> — <c>docs/tasks/T83.md</c> Done-when 4: "shows only the seat's nation, armies,
    /// fleets and cities", filtered to <see cref="DefaultViewNationId"/>. The full <see cref="RenderStatus"/>
    /// this is a narrower alternative to has no <c>Fleets:</c> section at all (T41/T23 never added one,
    /// and adding one there would move <c>tests/fixtures/cli/demo.golden.txt</c>'s own <c>status</c> lines
    /// — Done-when 5 forbids that); this compact view is new, so it can and does carry one.
    /// </summary>
    private IReadOnlyList<string> RenderStatusMine()
    {
        var nationId = DefaultViewNationId;
        var nation = State.NationById(nationId);
        if (nation is null)
        {
            return new[] { $"Unknown nation '{nationId}'." };
        }

        var eliminated = nation.Eliminated ? " [eliminated]" : string.Empty;
        var lines = new List<string>
        {
            $"Nation: {nation.Name} ({nation.Id}, {ControlLabel(nation.Control)}): "
            + $"treasury {nation.Treasury}, unity {nation.Unity}, tax {nation.TaxRatePercent}%{eliminated}",
            string.Empty,
            "Armies:",
        };

        foreach (var army in State.Armies)
        {
            if (string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                lines.Add(FormatArmyLine(army));
            }
        }

        lines.Add(string.Empty);
        lines.Add("Fleets:");
        foreach (var fleet in State.Fleets)
        {
            if (string.Equals(fleet.Nation, nationId, StringComparison.Ordinal))
            {
                lines.Add(FormatFleetLine(fleet));
            }
        }

        lines.Add(string.Empty);
        lines.Add("Cities:");
        var fortifyRule = FortifyRule();
        foreach (var city in State.Cities)
        {
            if (string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            {
                lines.Add(FormatCityLine(city, fortifyRule));
            }
        }

        return lines;
    }

    /// <summary>
    /// <c>armies [nation]</c> — <c>docs/tasks/T83.md</c> Done-when 4: one section, filtered to
    /// <paramref name="tokens"/>'s nation argument, or <see cref="DefaultViewNationId"/> when none is
    /// given.
    /// </summary>
    private IReadOnlyList<string> RenderArmiesCommand(string[] tokens)
    {
        if (tokens.Length > 2)
        {
            return new[] { "Usage: armies [nation]" };
        }

        var nationId = tokens.Length == 2 ? tokens[1] : DefaultViewNationId;
        if (State.NationById(nationId) is null)
        {
            return new[] { $"Unknown nation '{nationId}'." };
        }

        var lines = new List<string> { $"Armies ({NationDisplay(nationId)}):" };
        foreach (var army in State.Armies)
        {
            if (string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                lines.Add(FormatArmyLine(army));
            }
        }

        return lines;
    }

    /// <summary>
    /// <c>cities [nation]</c> — <c>docs/tasks/T83.md</c> Done-when 4: one section, filtered to
    /// <paramref name="tokens"/>'s nation argument, or <see cref="DefaultViewNationId"/> when none is
    /// given.
    /// </summary>
    private IReadOnlyList<string> RenderCitiesCommand(string[] tokens)
    {
        if (tokens.Length > 2)
        {
            return new[] { "Usage: cities [nation]" };
        }

        var nationId = tokens.Length == 2 ? tokens[1] : DefaultViewNationId;
        if (State.NationById(nationId) is null)
        {
            return new[] { $"Unknown nation '{nationId}'." };
        }

        var lines = new List<string> { $"Cities ({NationDisplay(nationId)}):" };
        var fortifyRule = FortifyRule();
        foreach (var city in State.Cities)
        {
            if (string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            {
                lines.Add(FormatCityLine(city, fortifyRule));
            }
        }

        return lines;
    }

    /// <summary>One "Armies:" line — shared by <see cref="RenderStatus"/> and the compact views.</summary>
    private string FormatArmyLine(ArmyState army)
    {
        var percentFull = SupplyCapacity.PercentFull(army.SupplyTons, army.TotalTroops, Ruleset);
        return $"  {army.Id} ({NationName(army.Nation)}) @ ({army.X},{army.Y}): {army.TotalTroops} troops "
            + $"in {army.Units.Count} units, supply {army.SupplyTons}t ({percentFull}%), "
            + $"morale {army.Morale}, moves {army.Moves}";
    }

    /// <summary>
    /// One "Fleets:" line — new with <c>docs/tasks/T83.md</c>'s compact views; <see cref="RenderStatus"/>
    /// never had a fleets section (see <see cref="RenderStatusMine"/>'s own remarks for why one is not
    /// added there now).
    /// </summary>
    private string FormatFleetLine(FleetState fleet) =>
        $"  {fleet.Id} ({NationName(fleet.Nation)}) @ ({fleet.X},{fleet.Y}): {fleet.Ships} ships, "
        + $"condition {fleet.ConditionPercent}%, supply {fleet.SupplyTons}t, moves {fleet.Moves}";

    /// <summary>One "Cities:" line — shared by <see cref="RenderStatus"/> and the compact views.</summary>
    private string FormatCityLine(CityState city, CityOrderRule? fortifyRule)
    {
        var fortification = fortifyRule is null
            ? "n/a"
            : FortificationCode.FinishedPercent(city.FortificationCode, fortifyRule).ToString(
                System.Globalization.CultureInfo.InvariantCulture) + "%";
        return $"  {city.Id} \"{city.Name}\" @ ({city.X},{city.Y}), owner {NationName(city.Owner)}: "
            + $"supply {city.SupplyTons}t, loyalty {city.Loyalty}, fortification {fortification}";
    }

    private IReadOnlyList<string> RenderMap()
    {
        var terrainCells = World.Terrain.Decode(World.Width, World.Height);
        var grid = new char[World.Height, World.Width];

        for (var y = 0; y < World.Height; y++)
        {
            for (var x = 0; x < World.Width; x++)
            {
                var code = terrainCells[(y * World.Width) + x];
                var tileType = World.TileTypeByCode(code);
                grid[y, x] = tileType is null ? '?' : GlyphFor(tileType);
            }
        }

        foreach (var fleet in State.Fleets)
        {
            if (!fleet.IsUnderConstruction && InBounds(fleet.X, fleet.Y))
            {
                grid[fleet.Y, fleet.X] = char.ToUpperInvariant(NationInitial(fleet.Nation));
            }
        }

        foreach (var army in State.Armies)
        {
            if (army.CoveredTileCode is not null && InBounds(army.X, army.Y))
            {
                grid[army.Y, army.X] = char.ToLowerInvariant(NationInitial(army.Nation));
            }
        }

        foreach (var city in State.Cities)
        {
            if (InBounds(city.X, city.Y))
            {
                grid[city.Y, city.X] = char.ToUpperInvariant(city.Name[0]);
            }
        }

        var lines = new List<string> { $"Map ({World.Width}x{World.Height}):" };
        for (var y = 0; y < World.Height; y++)
        {
            var row = new StringBuilder();
            for (var x = 0; x < World.Width; x++)
            {
                if (x > 0)
                {
                    row.Append(' ');
                }

                row.Append(grid[y, x]);
            }

            lines.Add(row.ToString());
        }

        lines.Add(string.Empty);
        lines.Add("Legend:");
        foreach (var city in State.Cities)
        {
            lines.Add($"  {char.ToUpperInvariant(city.Name[0])} = {city.Name} ({NationName(city.Owner)})");
        }

        foreach (var nation in State.Nations)
        {
            var initial = NationInitial(nation.Id);
            lines.Add($"  {char.ToLowerInvariant(initial)} = {nation.Name} army");
            lines.Add($"  {char.ToUpperInvariant(initial)} = {nation.Name} fleet");
        }

        return lines;
    }

    private IReadOnlyList<string> RenderNews()
    {
        if (State.NewsLog.Slots.Count == 0)
        {
            return new[] { "No news yet." };
        }

        var lines = new List<string> { "News log:" };
        foreach (var entry in State.NewsLog.Slots)
        {
            lines.Add("  " + entry.Text);
        }

        return lines;
    }

    /// <summary>
    /// <c>docs/task-catalogue.md</c> T23 follow-up
    /// <see href="https://github.com/diegoami/imperial_conquest_2/issues/232">#232</see>: this used to end
    /// with a hand-maintained "Not yet implemented: battles, city capture, recruitment, diplomacy, and the
    /// AI" line that a constant string kept true through four merges of exactly those features shipping.
    /// It named nothing that could fail when the claim went stale, so nothing ever did. There is no
    /// replacement banner: every command below is real, dispatched through the same
    /// <see cref="CommandDispatcher"/> a Godot seat will use, and a command this session cannot yet issue
    /// simply is not listed here — the list rots at the same rate as the switch in
    /// <see cref="Submit(string)"/> that backs it, which is to say it cannot rot silently, because a typo
    /// or an omission there is a compile-time or a "Unknown command" runtime fact, not a lonely string.
    /// </summary>
    /// <remarks>
    /// <strong>The three compact views (<c>docs/tasks/T83.md</c> Done-when 4) are listed only in a
    /// <c>--seat</c> session or in watch mode — the user's decision on PR #375's review, replacing this
    /// task's own original "only in a <c>--seat</c> session" choice.</strong> They work in <em>every</em>
    /// session regardless (<see cref="DefaultViewNationId"/> falls back to whichever seat currently has
    /// the turn when no <c>--seat</c> was given), so this is a restriction on where they are
    /// <em>advertised</em>, not on where they run: listing them unconditionally would add lines to the
    /// <c>help</c> command's own output, which <c>tests/fixtures/cli/demo.golden.txt</c> captures (the
    /// demo script's first line is <c>help</c>, run on <c>toy-3city</c> with no <c>--seat</c> and no
    /// all-AI seats) — Done-when 5 requires that golden to reproduce byte for byte, and a session with a
    /// scenario-assigned human seat and no <c>--seat</c> is exactly the one case that still gets the
    /// unadorned list.
    /// </remarks>
    private IReadOnlyList<string> RenderHelp()
    {
        var lines = new List<string>
        {
            "Commands:",
            "  status - show the calendar, nations, armies and cities",
        };

        if (_humanSeatNationId is not null || _isWatchMode)
        {
            lines.Add("  status mine - show only your own nation, armies, fleets and cities");
            lines.Add("  armies [nation] - show one nation's armies (default: yours)");
            lines.Add("  cities [nation] - show one nation's cities (default: yours)");
        }

        lines.AddRange(RenderHelpRemainder());
        return lines;
    }

    private IReadOnlyList<string> RenderHelpRemainder() => new[]
    {
        $"  map - show the {World.Width}x{World.Height} terrain map with city, army and fleet markers",
        "  move <army> <x> <y> - move an army toward (x, y)",
        "  buy <army> <city> <tons> - buy supply for an army at a city (free at your own city, paid abroad)",
        "  attack-army <army> <target-army> - attack another nation's army (declares war first if needed)",
        "  besiege-city <army> <city> - besiege an adjacent enemy city (declares war first if needed)",
        "  attack-fleet <fleet> <target-fleet> - attack another nation's fleet (declares war first if needed)",
        "  disband-army <army> - disband an army near one of your own cities",
        "  join-armies <survivor-army> <absorbed-army> - merge one army into another",
        "  join-units <army> <unit-index> <unit-index> - merge two of an army's own unit slots",
        "  split-army <army> <new-army> <unit-index> - split one unit slot off into a new army",
        "  order-city <city> <order-id> <points> - place a standing order on one of your own cities",
        "  declare-war <nation> - declare war on another nation",
        "  make-peace <nation> - propose peace with a nation you are at war with",
        "  propose-alliance <nation> - propose an alliance to another nation",
        "  propose-trade <nation> - propose a trade agreement to another nation",
        "  accept-offer - accept the pending trade or alliance offer made to you, if any",
        "  peace-yes - accept a pending post-battle peace treaty offer, if any",
        "  peace-no - decline a pending post-battle peace treaty offer, if any",
        "  mobilize <slot-index> <new-army> - mobilize a ready recruitment slot into an army unit",
        "  hire-mercenary <army> <pool-slot-index> - hire a mercenary unit from the mercenary pool",
        "  recruit-standing <city> <unit-type> <troops> - recruit a standing unit at one of your own cities",
        "  move-fleet <fleet> <x> <y> - move a fleet toward (x, y)",
        "  order-fleet <city> <ships> <new-fleet> - order a new fleet built at a coastal city",
        "  repair-fleet <fleet> <points> - spend repair points on a fleet's condition",
        "  scuttle-fleet <fleet> - scuttle one of your own fleets",
        "  split-fleet <fleet> <new-fleet> <ships> - split ships off into a new fleet",
        "  join-fleets <survivor-fleet> <absorbed-fleet> - merge one fleet into another",
        "  embark-army <army> <fleet> - load an army aboard an adjacent fleet",
        "  disembark-army <army> - unload an embarked army",
        "  buy-fleet-supply <fleet> <city> <tons> - buy supply for a fleet at a city",
        "  fleet-transfer <from-fleet> <to-fleet> <ships> <supply-tons> <money> - transfer resources between two of your own fleets",
        "  end - end your turn",
        "  news - show the news log",
        "  help - show this help",
        "  quit - exit",
    };

    private CityOrderRule? FortifyRule()
    {
        foreach (var order in Ruleset.CityOrders.Orders)
        {
            if (string.Equals(order.Id, "fortify", StringComparison.Ordinal))
            {
                return order;
            }
        }

        return null;
    }

    private string NationName(string nationId) => State.NationById(nationId)?.Name ?? nationId;

    private string NationDisplay(string nationId) => $"{NationName(nationId)} ({nationId})";

    private static string ControlLabel(SeatControl control) => control switch
    {
        SeatControl.Human => "human",
        SeatControl.Ai => "ai",
        _ => control.ToString(),
    };

    private string SeasonName(int seasonIndex)
    {
        var seasonNames = Ruleset.NewsLog.SeasonNames;
        return seasonIndex >= 0 && seasonIndex < seasonNames.Count
            ? seasonNames[seasonIndex]
            : $"Season {seasonIndex}";
    }

    private static char GlyphFor(TileType tileType) =>
        TerrainGlyphsByName.TryGetValue(tileType.Name, out var glyph)
            ? glyph
            : tileType.Name.Length > 0 ? char.ToLowerInvariant(tileType.Name[0]) : '?';

    private static char NationInitial(string nationId) => nationId.Length > 0 ? nationId[0] : '?';

    private bool InBounds(int x, int y) => (uint)x < (uint)World.Width && (uint)y < (uint)World.Height;
}
