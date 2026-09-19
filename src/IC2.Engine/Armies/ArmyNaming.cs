using System.Text.RegularExpressions;
using IC2.Engine.Model;

namespace IC2.Engine.Armies;

/// <summary>
/// The regular-unit auto-naming scheme — <c>docs/task-catalogue.md</c> "T15 Army and unit management",
/// Done-when 5: <c>"Nth Foot/Guards/Bowmen/Lancers/Dragoons Battalion"</c>, the ordinal being
/// <strong>one past the highest</strong> already borne by that unit type across the whole nation's
/// armies and its cities' garrisons.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong> — the unit-level split
/// helper (<c>TSplitArmyUnit_OK</c>, <c>0x004444CC</c>): "A split unit inherits type and quality and is
/// auto-named with the next free ordinal for its type across all of the nation's armies and the city
/// garrison (1st/2nd/3rd/Nth + Foot/Guards/Bowmen/Lancers/Dragoons + Battalion)." The same scan is
/// <c>FUN_0044a218</c> on the mobilization path
/// (<c>decompiled-mobilization-and-mercenary-restock.md</c> §3), which adds that it is nation-wide and
/// skips mercenaries — every unit whose origin label is non-zero, which is why a hired unit never takes
/// a battalion number.
/// </para>
/// <para>
/// <strong>"Next free" means one past the highest, and that is now <c>[confirmed]</c> rather than
/// derived.</strong> No report decompiles the selection loop itself, and an earlier revision of this
/// class said so and chose the other reading — <em>"the exact algorithm (smallest unused integer,
/// versus one past the highest used) is [derived] from the word 'free' alone"</em>, resolved in favour
/// of smallest-unused. <strong>The save pair behind T55's Done-when 8 settles it, against that
/// reading</strong>: in <c>1_rome_270_autumn_1.sav</c> Rome owns exactly one army, whose only
/// light-cavalry unit is <c>2nd Lancers</c> — there is no <c>1st</c> anywhere in the nation — and in
/// <c>1_rome_270_autumn_3.sav</c> the unit mobilized into the new army is named
/// <strong><c>3rd Lancers Battalion</c></strong>. Smallest-unused would have produced <c>1st</c>. Every
/// other name in that pair is a dense series where the two readings agree, so the lone <c>2nd
/// Lancers</c> with no <c>1st</c> is the sole discriminating case in the corpus, and
/// <c>RomeAutumnMobilizationReplayTests</c> reproduces it end to end.
/// <a href="https://github.com/diegoami/imperial_conquest_2/issues/243">#243</a>.
/// </para>
/// <para>
/// <strong>The ordinal is read out of the name, because the original has nowhere else to keep it.</strong>
/// The army unit slot is 32 bytes and every field is accounted for — <c>+0</c> origin label, <c>+2</c>
/// type, <c>+4</c> troops, <c>+6</c> quality, <c>+8</c> a 24-byte name
/// (<c>decompiled-mobilization-and-mercenary-restock.md</c> §1, <c>army-records-and-roman-roster.md</c>)
/// — so there is no stored ordinal for the scan to read and no room to add one without changing the
/// record this engine must round-trip. Parsing the name is therefore not a shortcut here; it is what the
/// original must itself be doing.
/// </para>
/// <para>
/// <strong>And the parse must tolerate the spacing the saves actually contain</strong>
/// <c>[confirmed]</c>, second defect of #243. The corpus stores several names with a <em>double</em>
/// space before "Battalion" — <c>"1st Foot  Battalion"</c>, <c>"2nd Lancers  Battalion"</c>,
/// <c>"1st Dragoons  Battalion"</c> — while every unit created during play uses a single one. An
/// earlier revision of <see cref="NamePattern"/> required exactly one space, which silently discarded
/// those ordinals. <strong>The Dragoons in that same pair prove the original does not:</strong> Rome's
/// only two heavy-cavalry units are <c>"1st Dragoons  Battalion"</c> and <c>"2nd Dragoons  Battalion"</c>,
/// <em>both</em> double-spaced, and the mobilized one comes out <c>3rd Dragoons Battalion</c> — which
/// is unreachable unless both ordinals were seen. This also corroborates the rule above: reading a
/// leading integer out of a loosely-formatted string is exactly what one-past-the-highest needs, where
/// smallest-unused would need the complete set of used ordinals to be recovered correctly.
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
    /// One past the highest ordinal already borne by a regular unit of <paramref name="unitTypeId"/>
    /// (named with <paramref name="label"/>) across <paramref name="nationId"/>'s armies and city
    /// garrisons — <c>1</c> when the nation has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A gap is not filled.</strong> If the nation's only <c>Lancers</c> unit is
    /// <c>"2nd Lancers Battalion"</c> with no <c>1st</c> anywhere — exactly the shape Rome is in at
    /// <c>1_rome_270_autumn_1.sav</c> — the next one raised is <c>"3rd Lancers Battalion"</c>, which is
    /// what that save's successor records. See this class's remarks for the evidence; a disbanded
    /// unit's ordinal is retired, not recycled.
    /// </para>
    /// <para>
    /// <strong>There is no 99 cap here, deliberately.</strong>
    /// <c>decompiled-mobilization-and-mercenary-restock.md</c> §3 records that <c>FUN_0044a218</c>
    /// "picks the lowest ordinal <c>N</c> in <c>1..99</c> … if all of 1..98 are taken it sticks at
    /// 99" — but that sentence describes the <em>smallest-unused</em> loop the same corpus refutes
    /// (see this class's remarks), and under one-past-the-highest there is no search to terminate and
    /// so nothing to stick at. The highest ordinal anywhere in the pair that settles the rule is 9, so
    /// the corpus does not reach the question, and #243 granted these files for its two defects only.
    /// Recorded here rather than left in a pull-request body, which does not survive the merge.
    /// </para>
    private static int NextOrdinal(GameState state, string nationId, string unitTypeId, string label)
    {
        var highest = 0;

        foreach (var army in state.Armies)
        {
            if (!string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                continue;
            }

            highest = Math.Max(highest, HighestOrdinal(army.Units, unitTypeId, label));
        }

        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            {
                continue;
            }

            highest = Math.Max(highest, HighestOrdinal(city.Garrison, unitTypeId, label));
        }

        return highest + 1;
    }

    /// <summary>
    /// The highest ordinal borne by a regular unit of <paramref name="unitTypeId"/> in
    /// <paramref name="units"/>, or <c>0</c> when there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mercenary is skipped by <see cref="UnitSlot.IsRegular"/> rather than by failing to match the
    /// pattern: <c>FUN_0044a218</c>'s own scan tests the origin label, and an ethnic name such as
    /// "Gallic" would not match the battalion pattern either way — but the label is the rule and the
    /// name is a coincidence, so the rule is what is written.
    /// <c>ArmyNamingTests.NextName_SkipsAMercenaryEvenWhenItsNameLooksLikeABattalion</c> separates the
    /// two with a mercenary whose name <em>is</em> battalion-shaped.
    /// </para>
    /// <para>
    /// <strong>The original's scan also filters on <c>troops &gt; 0</c>, and this one does not.</strong>
    /// It has to: the original's army record is a fixed 20-slot array that retains whatever the slot
    /// last held, and the corpus shows it plainly — in <c>1_rome_270_autumn_1.sav</c> army 0, slot 13
    /// still reads <c>"4th Guards  Battalion"</c> with 0 troops behind twelve live units, and slots
    /// 14–19 hold uninitialised bytes (origin label <c>-1800</c>, quality <c>514</c>, unreadable
    /// names). Here a vacated slot is normally absent from <see cref="ArmyState.Units"/> altogether, so
    /// there is usually nothing to filter — but the model does not forbid a zero-troop
    /// <see cref="UnitSlot"/>, and <c>MobilizationReceivingArmy.FirstFreeUnitSlot</c> reads one as a
    /// hole, so one carrying a stale battalion name would be counted here where the original would
    /// skip it. No engine path produces that today; it is reported for the bug list rather than fixed
    /// under a grant that covers #243's two defects, and it is stated rather than assumed away.
    /// </para>
    /// </remarks>
    private static int HighestOrdinal(ValueList<UnitSlot> units, string unitTypeId, string label)
    {
        var highest = 0;
        foreach (var unit in units)
        {
            if (!unit.IsRegular || !string.Equals(unit.UnitTypeId, unitTypeId, StringComparison.Ordinal))
            {
                continue;
            }

            var match = NamePattern(label).Match(unit.Name);
            if (match.Success)
            {
                highest = Math.Max(
                    highest,
                    int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return highest;
    }

    /// <summary>
    /// The battalion-name pattern, tolerant of the whitespace the corpus actually holds: one or more
    /// spaces between the ordinal, the label and "Battalion", and any surrounding padding. See this
    /// class's remarks for why (the saves' own <c>"1st Dragoons  Battalion"</c>), and
    /// <c>ArmyNamingTests.NextName_ReadsAnOrdinalOutOfTheDoubleSpacedFormTheSavesHold</c> for the test
    /// that visits it.
    /// </summary>
    private static Regex NamePattern(string label) =>
        new($@"^\s*(\d+)(?:st|nd|rd|th)\s+{Regex.Escape(label)}\s+Battalion\s*$", RegexOptions.CultureInvariant);

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
