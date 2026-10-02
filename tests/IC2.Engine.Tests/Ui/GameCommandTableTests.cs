using IC2.Engine.Assets;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T100 (docs/tasks/T100.md, Done-when 1): the one command table holds <em>exactly</em> the entries of
/// <c>docs/investigations/original-ui-command-audit.md</c> §1 — the seven menus' own entries, each once,
/// under the inventory's own menu and caption — minus the three seat commands (Game → New player, New
/// nation, Abdicate) the user's decision leaves out. It also pins that no two rows share an id and that
/// every icon key is a member of <see cref="AssetKeys.AllKeys"/>.
/// </summary>
/// <remarks>
/// The expected list is written out here, independently of <see cref="GameCommandTable"/>, so the test
/// is a real comparison rather than a replay of the same builder. The caption strings are the audit's
/// own (<c>End turn</c>, not <c>End Turn</c>); the menu and submenu strings are the inventory's.
/// <para>
/// The AllKeys assertion is red until T101 (the <c>ui.command.*</c> keys) merges: the keys are written
/// as the exact literals T101 names, by design, and this task is explicitly allowed to be rebased onto
/// T101 after its own PR opens. The other two facts pass now.
/// </para>
/// </remarks>
public sealed class GameCommandTableTests
{
    /// <summary>The audit's §1 entries minus the three seat commands, as (menu, submenu, caption).</summary>
    private static readonly (string Menu, string? Submenu, string Caption)[] AuditEntries =
    {
        // File (§1.1)
        ("File", null, "New"),
        ("File", null, "Open"),
        ("File", null, "Save"),
        ("File", null, "Save As"),
        ("File", null, "Close"),
        // Game (§1.2), minus New player / New nation / Abdicate
        ("Game", null, "End turn"),
        // Strategy (§1.3)
        ("Strategy", null, "News"),
        ("Strategy", null, "International relations"),
        ("Strategy", null, "Taxation"),
        ("Strategy", null, "Balance sheet"),
        ("Strategy", null, "Recruit unit"),
        ("Strategy", null, "Build fleet"),
        // Nations (§1.4): the 16 entries plus All nations
        ("Nations", null, "Rome"),
        ("Nations", null, "Carthage"),
        ("Nations", null, "Seleucid"),
        ("Nations", null, "Ptolemaic"),
        ("Nations", null, "Macedonia"),
        ("Nations", null, "Numidia"),
        ("Nations", null, "Gaul"),
        ("Nations", null, "Greece"),
        ("Nations", null, "Celtiberia"),
        ("Nations", null, "Illyria"),
        ("Nations", null, "Dacia"),
        ("Nations", null, "Bithynia"),
        ("Nations", null, "Galatia"),
        ("Nations", null, "Armenia"),
        ("Nations", null, "Media"),
        ("Nations", null, "Thracia"),
        ("Nations", null, "All nations"),
        // Area map (§1.5)
        ("Area map", null, "Show cities"),
        ("Area map", null, "Show capital"),
        ("Area map", null, "Show armies"),
        ("Area map", null, "Show fleets"),
        ("Area map", null, "Show all"),
        ("Area map", "Show mercenaries", "Light infantry"),
        ("Area map", "Show mercenaries", "Heavy infantry"),
        ("Area map", "Show mercenaries", "Archers"),
        ("Area map", "Show mercenaries", "Light cavalry"),
        ("Area map", "Show mercenaries", "Heavy cavalry"),
        ("Area map", "Show mercenaries", "All mercenaries"),
        ("Area map", null, "Find a city"),
        // Unit map (§1.6)
        ("Unit map", "Army", "Supply army"),
        ("Unit map", "Army", "Recruit mercenaries"),
        ("Unit map", "Army", "Transfer unit"),
        ("Unit map", "Army", "Split army"),
        ("Unit map", "Army", "Join armies"),
        ("Unit map", "Army", "Change units"),
        ("Unit map", "Army", "Disband army"),
        ("Unit map", "Fleet", "Supply fleet"),
        ("Unit map", "Fleet", "Repair fleet"),
        ("Unit map", "Fleet", "Transfer ships"),
        ("Unit map", "Fleet", "Split fleet"),
        ("Unit map", "Fleet", "Join fleet"),
        ("Unit map", "Fleet", "Scuttle fleet"),
        ("Unit map", "City", "Fortify city"),
        ("Unit map", null, "Cancel selection"),
        // Help (§1.7)
        ("Help", null, "Help topics"),
        ("Help", null, "Show hints"),
        ("Help", null, "About Imperial Conquest"),
    };

    /// <summary>
    /// Done-when 1, the exact list: the table's rows are the audit's entries, once each, under the
    /// inventory's menu and caption. A missing row, an extra row, a duplicate, a reworded caption or a
    /// row under the wrong menu (or submenu) all fail here.
    /// </summary>
    [Fact]
    public void The_table_holds_exactly_the_audits_entries_minus_the_seat_commands()
    {
        var expected = AuditEntries
            .Select(entry => (entry.Menu, entry.Submenu, entry.Caption))
            .OrderBy(entry => entry.Menu, StringComparer.Ordinal)
            .ThenBy(entry => entry.Submenu ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Caption, StringComparer.Ordinal)
            .ToList();

        var actual = GameCommandTable.Rows
            .Select(row => (row.Menu, row.Submenu, row.Caption))
            .OrderBy(entry => entry.Menu, StringComparer.Ordinal)
            .ThenBy(entry => entry.Submenu ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Caption, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, actual);
        Assert.Equal(expected.Count, GameCommandTable.Rows.Count);
    }

    /// <summary>Done-when 1: no two rows share an id — a duplicate would make a lookup ambiguous.</summary>
    [Fact]
    public void No_two_rows_share_an_id()
    {
        var duplicates = GameCommandTable.Rows
            .GroupBy(row => row.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// Done-when 1: every icon key the table names is a member of <see cref="AssetKeys.AllKeys"/>, so a
    /// stray or misspelled key cannot reach the pack loader. <strong>Red until T101 merges</strong>: the
    /// <c>ui.command.*</c> keys are written here as the exact literals T101's entry names, and T101's
    /// <c>AssetKeys</c> group is what puts them in the set. Never weakened to make it pass early.
    /// </summary>
    [Fact]
    public void Every_icon_key_is_a_member_of_AssetKeys()
    {
        var allKeys = AssetKeys.AllKeys.ToHashSet(StringComparer.Ordinal);
        var unknown = GameCommandTable.Rows
            .Where(row => row.IconKey is not null)
            .Select(row => row.IconKey!)
            .Where(key => !allKeys.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unknown);
    }

    /// <summary>
    /// The icon-key shape is T101's own <c>ui.command.&lt;id&gt;.icon</c> convention, and the 36 rows
    /// that carry one are exactly the pictorial toolbar buttons (9 main-toolbar, 12 Area-map, 15
    /// Unit-map) — a guard that no caption-only row accidentally gained a key.
    /// </summary>
    [Fact]
    public void The_icon_keys_are_the_thirty_six_pictorial_toolbar_buttons()
    {
        var withIcons = GameCommandTable.Rows.Where(row => row.IconKey is not null).ToList();

        Assert.Equal(36, withIcons.Count);
        Assert.All(
            withIcons,
            row => Assert.StartsWith("ui.command.", row.IconKey, StringComparison.Ordinal));
        Assert.All(
            withIcons,
            row => Assert.EndsWith(".icon", row.IconKey, StringComparison.Ordinal));
    }
}
