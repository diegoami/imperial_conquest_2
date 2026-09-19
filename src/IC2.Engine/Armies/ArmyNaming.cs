using System.Text.RegularExpressions;
using IC2.Engine.Model;

namespace IC2.Engine.Armies;

/// <summary>
/// The regular-unit auto-naming scheme — <c>docs/task-catalogue.md</c> "T15 Army and unit management",
/// Done-when 5: <c>"Nth Foot/Guards/Bowmen/Lancers/Dragoons Battalion"</c>, the ordinal being the next
/// free one for that unit type across the whole nation's armies and its cities' garrisons.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong> — the unit-level split
/// helper (<c>TSplitArmyUnit_OK</c>, <c>0x004444CC</c>): "A split unit inherits type and quality and is
/// auto-named with the next free ordinal for its type across all of the nation's armies and the city
/// garrison (1st/2nd/3rd/Nth + Foot/Guards/Bowmen/Lancers/Dragoons + Battalion) — which is exactly the
/// naming pattern seen in every roster in <c>army-records-and-roman-roster.md</c>." That report's own
/// 13-unit Roman roster is the corpus this task's Done-when 5 names, reproduced verbatim in
/// <c>ArmyNamingTests</c>: <c>1st</c>/<c>2nd Foot</c>, <c>1st</c>–<c>8th Guards</c>, <c>1st</c>/<c>2nd
/// Dragoons</c>, and a lone <c>2nd Lancers</c> with no <c>1st</c> present (this task's own free-ordinal
/// algorithm reads that gap as ordinal 1 being free — see <see cref="NextOrdinal"/>'s remarks).
/// </para>
/// <para>
/// <strong>"Free", read literally.</strong> No report decompiles the ordinal-selection loop itself —
/// only this sentence describing its outcome — so the exact algorithm (smallest unused integer, versus
/// one past the highest used) is <c>[derived]</c> from the word "free" alone. This class takes "free" at
/// its plain-English meaning: the smallest positive ordinal not currently in use by that type anywhere in
/// the nation, so a disbanded unit's ordinal is available for reuse rather than the numbering only ever
/// growing. What was searched and came up empty for the loop's own instructions:
/// <c>decompiled-unit-map-orders-and-record-fields.md</c> (the source sentence above, prose only) and
/// every other report in <c>tests/fixtures/known-reports.json</c> naming <c>TSplitArmyUnit</c>,
/// <c>TChangeArmyUnits</c> or a battalion name.
/// </para>
/// <para>
/// <strong>The label word is not ruleset data.</strong> This task's Owns list
/// (<c>src/IC2.Engine/Armies/**</c>) does not include <c>src/IC2.Engine/Model/Ruleset.cs</c>, so the
/// five label words below are a private lookup keyed on the five unit-type ids every shipped ruleset
/// uses (<c>data/rulesets/toy-ruleset.json</c>, and the same five the classical-mediterranean export
/// will carry), not a new <c>UnitTypeRules</c> field. The mapping itself — light infantry → Foot, heavy
/// infantry → Guards, light cavalry → Lancers, heavy cavalry → Dragoons — is read directly off the
/// roster above; archers → Bowmen is the one label the roster does not exercise (no archer unit appears
/// in it), so it is placed by elimination against Done-when 5's own enumeration
/// ("Foot/Guards/Bowmen/Lancers/Dragoons"), the only word left once the other four are assigned.
/// </para>
/// </remarks>
public static class ArmyNaming
{
    private static readonly IReadOnlyDictionary<string, string> BattalionLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["light_infantry"] = "Foot",
        ["heavy_infantry"] = "Guards",
        ["archers"] = "Bowmen",
        ["light_cavalry"] = "Lancers",
        ["heavy_cavalry"] = "Dragoons",
    };

    /// <summary>
    /// The same five ids as <see cref="BattalionLabels"/>, in a fixed, explicitly ordered list — kept
    /// separately so an error message never enumerates the dictionary's own (unordered, for this guard's
    /// purposes) <c>Keys</c>, which <c>DeterminismGuardTests</c> flags anywhere under <c>src/</c>.
    /// </summary>
    private static readonly string[] KnownUnitTypeIds =
    {
        "light_infantry", "heavy_infantry", "archers", "light_cavalry", "heavy_cavalry",
    };

    /// <summary>
    /// The full auto-generated name for the next unit of <paramref name="unitTypeId"/> raised anywhere
    /// in <paramref name="nationId"/>'s armies or city garrisons.
    /// </summary>
    /// <param name="state">The live state to scan.</param>
    /// <param name="nationId">The nation whose armies and cities are scanned.</param>
    /// <param name="unitTypeId">
    /// The new unit's type — must be one of the five ids <see cref="BattalionLabels"/> knows.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="unitTypeId"/> is not one of the five known regular unit types. This is a
    /// programming invariant, not a gameplay rejection: every shipped ruleset's regular unit types are
    /// exactly these five, so a caller passing anything else has a bug, not an illegal player order.
    /// </exception>
    public static string NextName(GameState state, string nationId, string unitTypeId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nationId);
        ArgumentNullException.ThrowIfNull(unitTypeId);

        if (!BattalionLabels.TryGetValue(unitTypeId, out var label))
        {
            throw new ArgumentException(
                $"'{unitTypeId}' has no auto-naming label; the five known ids are "
                + string.Join(", ", KnownUnitTypeIds), nameof(unitTypeId));
        }

        var ordinal = NextOrdinal(state, nationId, unitTypeId, label);
        return $"{Ordinal(ordinal)} {label} Battalion";
    }

    /// <summary>
    /// The smallest positive ordinal not already in use by a regular unit of <paramref name="unitTypeId"/>
    /// (named with <paramref name="label"/>) across <paramref name="nationId"/>'s armies and city
    /// garrisons.
    /// </summary>
    /// <remarks>
    /// A gap is filled before the count grows: if the nation's only <c>Lancers</c> unit is <c>"2nd Lancers
    /// Battalion"</c> (ordinal 1 currently unused, exactly the Roman roster's own shape — see this class's
    /// remarks), the next one raised is named <c>"1st Lancers Battalion"</c>, not <c>"3rd"</c>.
    /// </remarks>
    private static int NextOrdinal(GameState state, string nationId, string unitTypeId, string label)
    {
        var used = new HashSet<int>();

        foreach (var army in state.Armies)
        {
            if (!string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                continue;
            }

            CollectOrdinals(army.Units, unitTypeId, label, used);
        }

        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            {
                continue;
            }

            CollectOrdinals(city.Garrison, unitTypeId, label, used);
        }

        var candidate = 1;
        while (used.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
    }

    private static void CollectOrdinals(ValueList<UnitSlot> units, string unitTypeId, string label, HashSet<int> used)
    {
        foreach (var unit in units)
        {
            if (!string.Equals(unit.UnitTypeId, unitTypeId, StringComparison.Ordinal))
            {
                continue;
            }

            var match = NamePattern(label).Match(unit.Name);
            if (match.Success)
            {
                used.Add(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }

    private static Regex NamePattern(string label) =>
        new($@"^(\d+)(?:st|nd|rd|th) {Regex.Escape(label)} Battalion$", RegexOptions.CultureInvariant);

    /// <summary>Formats a positive integer with its English ordinal suffix — <c>1 → "1st"</c>, <c>12 → "12th"</c>.</summary>
    private static string Ordinal(int n)
    {
        if (n <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "An ordinal must be positive.");
        }

        // 11th-13th are "th" regardless of the last digit; every other number follows the last digit.
        var suffix = (n % 100) is >= 11 and <= 13
            ? "th"
            : (n % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };

        return $"{n}{suffix}";
    }
}
