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
public sealed partial class GameSession
{
    /// <summary>Season display names, purely a text label — falls back to "Season N" past this list.</summary>
    private static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

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
            var percentFull = SupplyCapacity.PercentFull(army.SupplyTons, army.TotalTroops, Ruleset);
            lines.Add(
                $"  {army.Id} ({NationName(army.Nation)}) @ ({army.X},{army.Y}): {army.TotalTroops} troops "
                + $"in {army.Units.Count} units, supply {army.SupplyTons}t ({percentFull}%), "
                + $"morale {army.Morale}, moves {army.Moves}");
        }

        lines.Add(string.Empty);
        lines.Add("Cities:");
        var fortifyRule = FortifyRule();
        foreach (var city in State.Cities)
        {
            var fortification = fortifyRule is null
                ? "n/a"
                : FortificationCode.FinishedPercent(city.FortificationCode, fortifyRule).ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + "%";
            lines.Add(
                $"  {city.Id} \"{city.Name}\" @ ({city.X},{city.Y}), owner {NationName(city.Owner)}: "
                + $"supply {city.SupplyTons}t, loyalty {city.Loyalty}, fortification {fortification}");
        }

        return lines;
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

    private IReadOnlyList<string> RenderHelp() => new[]
    {
        "Commands:",
        "  status - show the calendar, nations, armies and cities",
        $"  map - show the {World.Width}x{World.Height} terrain map with city, army and fleet markers",
        "  move <army> <x> <y> - move an army toward (x, y)",
        "  buy <army> <city> <tons> - buy supply for an army at a city (free at your own city, paid abroad)",
        "  end - end your turn",
        "  news - show the news log",
        "  help - show this help",
        "  quit - exit",
        "Not yet implemented: battles, city capture, recruitment, diplomacy, and the AI -- a seat with no",
        "human player simply passes with no orders.",
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

    private static string SeasonName(int seasonIndex) =>
        seasonIndex >= 0 && seasonIndex < SeasonNames.Length
            ? SeasonNames[seasonIndex]
            : $"Season {seasonIndex}";

    private static char GlyphFor(TileType tileType) =>
        TerrainGlyphsByName.TryGetValue(tileType.Name, out var glyph)
            ? glyph
            : tileType.Name.Length > 0 ? char.ToLowerInvariant(tileType.Name[0]) : '?';

    private static char NationInitial(string nationId) => nationId.Length > 0 ? nationId[0] : '?';

    private bool InBounds(int x, int y) => (uint)x < (uint)World.Width && (uint)y < (uint)World.Height;
}
