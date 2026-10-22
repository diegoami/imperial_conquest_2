namespace IC2.Data.Tests;

/// <summary>
/// A small, valid strategic document built by hand for the T184 codec and policy tests: 334 cities spread
/// over the 16 nations, a land army, an army aboard a launched fleet, a fleet under construction, ten live
/// mercenaries on city tiles and three news lines. Every value is chosen here, never read from a real save.
/// </summary>
internal static class OriginalSaveDocumentFixture
{
    public const int LandArmyX = 5, LandArmyY = 100, FleetX = 7, FleetY = 120;

    public static (int X, int Y) CityTile(int c) => (c % 300, 10 + c / 300 * 5);

    public static OriginalSaveDocument Build()
    {
        var map = new short[OriginalSaveDocument.MapCells];
        void Set(int x, int y, int value) => map[x * OriginalSaveDocument.MapHeight + y] = (short)value;

        var cities = new OriginalCity[334];
        for (var c = 0; c < cities.Length; c++)
        {
            var (x, y) = CityTile(c);
            var owner = c % 16;
            // Names whose byte order is not index order, so the city list's sort is exercised.
            cities[c] = new OriginalCity($"C{(333 - c):D3}", x, y, owner, owner, 70, 100, 50, 30, 60, 5);
            Set(x, y, 20 + owner);
        }

        // Rome's land army (troops < 25,000: band 200) on plain terrain (code 0); its fleet carries a second army.
        Set(LandArmyX, LandArmyY, 200 + 0);
        Set(FleetX, FleetY, 300 + 0);
        var armies = new[]
        {
            new OriginalArmy(LandArmyX, LandArmyY, 0, 4, 0, 50, 300, 70,
                [new OriginalUnitSlot(1, 2, 5000, 7, "First Legion"), new OriginalUnitSlot(2, 0, 3000, 6, "Velites")]),
            new OriginalArmy(FleetX, FleetY, 0, 0, -1, 20, 0, 60, [new OriginalUnitSlot(3, 1, 4000, 8, "Marines")]),
        };
        var fleets = new[]
        {
            new OriginalFleet(FleetX, FleetY, -1, 0, 0, -1, 9, 40, 100, 20, 90, 1, 2),
            new OriginalFleet(0, 0, -1, 0, 1, 24, 0, 0, 0, 10, 1, -1, 0),
        };

        var nations = new OriginalNation[16];
        for (var n = 0; n < 16; n++)
        {
            var owned = Enumerable.Range(0, 334).Where(c => c % 16 == n)
                .OrderBy(c => cities[c].Name, StringComparer.Ordinal).ToArray();
            var recruits = Enumerable.Range(0, 40).Select(e => e == 0
                ? new OriginalRecruitEntry(1, 2, 800, owned[0])
                : new OriginalRecruitEntry(0, 0, 0, 0)).ToArray();
            // Rome and Carthage at war (3), every other pair at peace; symmetric, as the reader requires.
            var relations = Enumerable.Range(0, 16).Select(k => (n, k) is (0, 1) or (1, 0) ? 3 : 0).ToArray();
            var ui = new OriginalNationUi(1, 0, [10, 20, 300, 200], [30, 40, 250, 180], [50, 60, 220, 160], 3, 4, 0, 10, 5);
            nations[n] = new OriginalNation(NationCatalog.Name((ushort)n), $"Leader of {n}", relations, 0x1234 + n, owned, recruits,
                [0x00FF0000u, 0x0000FF00u, 0x000000FFu], 50_000 + n, 40_000, 9_000 + n, 5_000, 450, 50, owned[0],
                owned.Length, owned.Length, 20, 1_000, -1, ui, n == 0 ? 1 : 0);
        }

        var mercenaries = Enumerable.Range(0, 50).Select(s => s < 10
            ? new OriginalMercenary(CityTile(s).X, CityTile(s).Y, s + 1, s % 5, 500 + s, 2)
            : new OriginalMercenary(0, 0, 0, 0, -1, 0)).ToArray();

        var trailer = new OriginalTrailer(Enumerable.Range(0, 16).ToArray(), -1, 0, 0, 0, 3, 270, 1, 10, 20, 600, 800);
        return new OriginalSaveDocument(map, cities, armies, fleets, nations, mercenaries,
            ["Rome declares war on Carthage", "A fleet is damaged in a storm", ""], trailer);
    }

    /// <summary>The document with nation <paramref name="n"/> replaced by <paramref name="change"/> of it.</summary>
    public static OriginalSaveDocument WithNation(this OriginalSaveDocument d, int n, Func<OriginalNation, OriginalNation> change) =>
        d with { Nations = d.Nations.Select((x, i) => i == n ? change(x) : x).ToArray() };

    public static OriginalSaveDocument WithCity(this OriginalSaveDocument d, int c, Func<OriginalCity, OriginalCity> change) =>
        d with { Cities = d.Cities.Select((x, i) => i == c ? change(x) : x).ToArray() };

    public static OriginalSaveDocument WithArmy(this OriginalSaveDocument d, int a, Func<OriginalArmy, OriginalArmy> change) =>
        d with { Armies = d.Armies.Select((x, i) => i == a ? change(x) : x).ToArray() };

    public static OriginalSaveDocument WithFleet(this OriginalSaveDocument d, int f, Func<OriginalFleet, OriginalFleet> change) =>
        d with { Fleets = d.Fleets.Select((x, i) => i == f ? change(x) : x).ToArray() };
}
