using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// The gate-2 policy port: the hand-built document is clean, and each mutation research's
/// <c>gate2-policy-adversarial.py</c> applies (plus the atlas invariants it relies on) is refused by its rule.
/// </summary>
public sealed class OriginalSaveExportPolicyTests
{
    [Fact]
    public void The_fixture_breaks_no_rule()
    {
        Assert.Empty(OriginalSaveExportPolicy.Check(OriginalSaveDocumentFixture.Build()));
    }

    public static IEnumerable<object[]> Mutations()
    {
        var d = OriginalSaveDocumentFixture.Build();
        var romeFirst = d.Nations[0].CityList[0];
        yield return ["no_army_tombstones", d.WithArmy(0, a => a with { Owner = -1 })];
        yield return ["no_fleet_tombstones", d.WithFleet(1, f => f with { Owner = -1 })];
        yield return ["city_allegiance_in_range", d.WithCity(3, c => c with { Allegiance = -1 })];
        yield return ["city_allegiance_in_range", d.WithCity(3, c => c with { Allegiance = 16 })];
        yield return ["city_marker", d.WithCity(3, c => c with { Owner = 4 })];
        yield return ["fort_encoding", d.WithCity(3, c => c with { Fort = -1 })];
        yield return ["building_fleet_moves_0_route_x_minus1", d.WithFleet(1, f => f with { Moves = 5 })];
        yield return ["building_fleet_moves_0_route_x_minus1", d.WithFleet(1, f => f with { RouteX = 0 })];
        yield return ["fleet_countdown", d.WithFleet(1, f => f with { Countdown = 23 })];
        yield return ["fleet_building_city", d.WithFleet(1, f => f with { CityOrCondition = 334 })];
        yield return ["fleet_condition", d.WithFleet(0, f => f with { CityOrCondition = 101 })];
        yield return ["fleet_marker", d.WithFleet(0, f => f with { Ships = 30 })];
        yield return ["carried_army_on_fleet_tile", d.WithArmy(1, a => a with { X = a.X + 1 })];
        yield return ["aboard_link_fleet_side", d.WithArmy(1, a => a with { Owner = 2 })];
        yield return ["aboard_link_army_side", d.WithFleet(0, f => f with { Army = -1 })];
        yield return ["army_marker", d.WithArmy(0, a => a with { Y = a.Y + 1 })];
        yield return ["army_cell_terrain_or_city", d.WithArmy(0, a => a with { Cell = 12 })];
        yield return ["city_list_sorted", d.WithNation(0, n => n with { CityList = n.CityList.Reverse().ToArray() })];
        yield return ["city_list_equals_owned", d.WithNation(0, n => n with { CityList = n.CityList.Skip(1).ToArray() })];
        yield return ["city_count", d.WithNation(0, n => n with { Cities = n.Cities + 1 })];
        yield return ["capital_owned", d.WithNation(0, n => n with { Capital = d.Nations[1].CityList[0] })];
        yield return ["recruit_city_index", d.WithNation(0, n => n with { Recruits = n.Recruits.Select((r, i) => i == 0 ? r with { City = 400 } : r).ToArray() })];
        yield return ["merc_live_on_city", d with { Mercenaries = d.Mercenaries.Select((m, i) => i == 0 ? m with { Y = 99 } : m).ToArray() }];
        yield return ["inert_bytes_carried_or_zero", d.WithNation(5, n => n with { Inert = new OriginalInertBytes(new byte[27], 7, new byte[3]) })];
        _ = romeFirst;
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public void Each_mutation_is_refused_by_its_rule(string rule, OriginalSaveDocument document)
    {
        Assert.Contains(OriginalSaveExportPolicy.Check(document), v => v.Rule == rule);
    }

    [Fact]
    public void An_eliminated_nations_stale_count_is_allowed_as_the_original_writes_it()
    {
        var d = OriginalSaveDocumentFixture.Build();
        var lost = d.Nations[7].CityList;
        // Nation 7 lost every city to nation 6: its list is empty, its capital −1, its count stale.
        var moved = lost.Aggregate(d, (doc, c) => doc.WithCity(c, x => x with { Owner = 6 }));
        var map = moved.Map.ToArray();
        foreach (var c in lost) map[moved.Cities[c].X * 140 + moved.Cities[c].Y] = 20 + 6;
        var six = moved.Cities.Select((c, i) => (c, i)).Where(p => p.c.Owner == 6).OrderBy(p => p.c.Name, StringComparer.Ordinal).Select(p => p.i).ToArray();
        var after = (moved with { Map = map })
            .WithNation(7, n => n with { CityList = [], Capital = -1 })
            .WithNation(6, n => n with { CityList = six, Cities = six.Length });
        Assert.Empty(OriginalSaveExportPolicy.Check(after));
    }

    [Fact]
    public void With_a_source_the_building_fleet_leftovers_and_inert_bytes_are_allowed()
    {
        var d = OriginalSaveDocumentFixture.Build().WithFleet(1, f => f with { Moves = 5 })
            .WithNation(5, n => n with { Inert = new OriginalInertBytes(new byte[27], 7, new byte[3]) });
        Assert.Empty(OriginalSaveExportPolicy.Check(d, fromScratch: false));
    }
}
