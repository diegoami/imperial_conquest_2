namespace IC2.Slice.UI;

/// <summary>
/// The number-to-word bands the original Information window reads from the DAT — T140 (bug #718). Each
/// band's first edge is paired with its last below it in the research read, and the table reads
/// <c>unity / 100</c>, <c>loyalty / 10</c>, <c>(m - 51) sar 2</c> for morale, a direct index for the other
/// adjectives and <c>value &gt; 0</c> for relations. A value above the last band produces an empty
/// string; a value below the first band falls back to the first word. See the research read for the
/// evidence per edge
/// (<c>2026-10-05-information-window-fields-and-bands.md</c>, "Band tables").
/// </summary>
/// <remarks>
/// <para>
/// <strong>Godot-free</strong> (no <c>using Godot</c>): the test project compiles this file directly, the
/// same seam <c>RecruitmentPanelViewModel.cs</c> and <c>NationStatusModel.cs</c> already use (T110 /
/// Fix #513 — see <c>tests/IC2.Engine.Tests/IC2.Engine.Tests.csproj</c>'s <c>Compile Include</c> block).
/// </para>
/// <para>
/// The bands are fixed by the original's DAT and are unchanged between presets — they are display text,
/// not ruleset keys. The "Quality" word (which belongs here for the record but is not this task's to
/// render) lives on <c>UnitCatalog.QualityName</c> under <c>src/**</c>, since the unit lists that show it
/// (T99 / T111 / T113) ship it from the engine.
/// </para>
/// </remarks>
public static class InformationWords
{
    // ---- Unity ----

    /// <summary><c>trunc(unity / 100)</c> into a six-word table, the bands the research read's
    /// "Unity" section pins row by row.</summary>
    public static string Unity(int unity)
    {
        if (unity >= 1000)
        {
            return string.Empty;
        }

        if (unity < 500)
        {
            return "very low";
        }

        if (unity < 600)
        {
            return "low";
        }

        if (unity < 700)
        {
            return "normal";
        }

        if (unity < 800)
        {
            return "high";
        }

        if (unity < 900)
        {
            return "very high";
        }

        return "excellent";
    }

    // ---- Loyalty ----

    /// <summary><c>trunc(loyalty / 10)</c> into the same six-word table.</summary>
    public static string Loyalty(int loyalty)
    {
        if (loyalty >= 100)
        {
            return string.Empty;
        }

        if (loyalty < 50)
        {
            return "very low";
        }

        if (loyalty < 60)
        {
            return "low";
        }

        if (loyalty < 70)
        {
            return "normal";
        }

        if (loyalty < 80)
        {
            return "high";
        }

        if (loyalty < 90)
        {
            return "very high";
        }

        return "excellent";
    }

    // ---- Morale ----

    /// <summary>
    /// Morale's six bands — the original uses <c>((m - 51) sar 2)</c> (or <c>(m - 48)</c> for
    /// <c>m &lt; 51</c>) to pick a table entry, so the bands are <em>not</em> a clean 4-wide stride —
    /// the research read documents 55–58 / 59–62 / 63–66 / 67–70 / 71–74 and saves reach 73
    /// (<c>design-audit.md §2.9a</c>). Six bands, not the five a 51–70 reading would give.
    /// </summary>
    public static string Morale(int morale)
    {
        if (morale >= 75)
        {
            return string.Empty;
        }

        if (morale < 40)
        {
            // The original reads neighbouring memory below this, but the task's [designed] default
            // applies: a value below the first band prints the first word.
            return "very low";
        }

        if (morale < 55)
        {
            return "very low";
        }

        if (morale < 59)
        {
            return "low";
        }

        if (morale < 63)
        {
            return "normal";
        }

        if (morale < 67)
        {
            return "high";
        }

        if (morale < 71)
        {
            return "very high";
        }

        return "excellent";
    }

    // ---- Tribute (a foreign city's raw tribute) ----

    /// <summary>
    /// The tribute of a city the viewer does not control, from the raw <see cref="Model.CityState.Tribute"/>.
    /// Above 10,000 the original prints no word and the talents number text stays
    /// (the research read rows C09 / Tribute edge pair 10000/10001).
    /// </summary>
    public static string Tribute(int tributeRaw)
    {
        if (tributeRaw <= 10)
        {
            return "poor";
        }

        if (tributeRaw <= 30)
        {
            return "moderate";
        }

        if (tributeRaw <= 100)
        {
            return "rich";
        }

        if (tributeRaw <= 10_000)
        {
            return "very rich";
        }

        return string.Empty;
    }

    // ---- Relations ----

    /// <summary>
    /// Nation-to-other relation row's word — the original's <c>DAT_004794C8</c> table, with peace and
    /// every value at or below 0 printing nothing (the researched edge pair 0/1 and 3/4).
    /// </summary>
    public static string Relation(int relation)
    {
        if (relation <= 0)
        {
            return string.Empty;
        }

        if (relation == 1)
        {
            return "trade";
        }

        if (relation == 2)
        {
            return "ally";
        }

        if (relation == 3)
        {
            return "war";
        }

        return string.Empty;
    }

    // ---- Sea ----

    /// <summary>
    /// The fleet panel's Sea word — <see cref="Model.FleetState.CoveredTileCode"/> == 0 is <c>calm</c>,
    /// any other value (also negative) is <c>rough</c>.
    /// </summary>
    public static string Sea(int coveredTileCode) =>
        coveredTileCode == 0 ? "calm" : "rough";
}
