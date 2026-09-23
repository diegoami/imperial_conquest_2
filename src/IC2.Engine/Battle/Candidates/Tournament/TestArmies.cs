using IC2.Engine.Model;

namespace IC2.Engine.Battle.Candidates.Tournament;

/// <summary>The two army scales of §8.0.</summary>
public enum ArmyScale
{
    /// <summary>Shares of troops, 40,000 troops per army (CS-T, UR).</summary>
    Troops,

    /// <summary>Shares of weighted power, <c>Σ weight × troops = 2,400,000</c> per army (CS-P, NT, CD, EN, SV).</summary>
    Power,
}

/// <summary>One of §8.0's 21 compositions: a percentage share per unit type, in <c>typeEffectivenessOrder</c>.</summary>
/// <param name="Index">Its index in Σ, the order §8.0 lists them in (the seed formula uses it).</param>
/// <param name="Label">A short label, e.g. <c>LI</c>, <c>LI+HC</c>, <c>uniform</c>, <c>HI-heavy</c>.</param>
/// <param name="Shares">Percent per type; sums to 100.</param>
public sealed record Composition(int Index, string Label, int[] Shares);

/// <summary>
/// §8.0's test armies, seats and seeds, shared by every metric (D50, [designed] bands and inputs; <c>M = 59</c>
/// is K29 and the battalion sizes are K07).
/// </summary>
public static class TestArmies
{
    /// <summary>§8.0: quality 6 ("average") everywhere.</summary>
    public const int Quality = 6;

    /// <summary>§8.0: <c>M = 59</c>, the confirmed new-army strategic morale (K29).</summary>
    public const int Morale = 59;

    /// <summary>§8.0 T-scale: 40,000 troops per army.</summary>
    public const long TroopScaleTotal = 40_000;

    /// <summary>§8.0 P-scale: <c>Σ combatPowerWeight × troops = 2,400,000</c> per army.</summary>
    public const long PowerScaleTotal = 2_400_000;

    /// <summary>§8.0: 200 seeds per matchup.</summary>
    public const int SeedsPerMatchup = 200;

    /// <summary>§8.0: the seed is <c>1,000,003 × i + k</c>.</summary>
    public const ulong SeedMultiplier = 1_000_003;

    /// <summary>§8.3: <c>κ</c> in hundredths.</summary>
    public static IReadOnlyList<int> UpsetKappaPercent { get; } = new[] { 90, 80, 67, 50 };

    /// <summary>§8.3: the mirror compositions, the 5 pure ones and the uniform mix.</summary>
    public static IReadOnlyList<int> UpsetCompositions { get; } = new[] { 0, 1, 2, 3, 4, 15 };

    /// <summary>The five type abbreviations, in <c>typeEffectivenessOrder</c>.</summary>
    public static IReadOnlyList<string> TypeLabels { get; } = new[] { "LI", "HI", "A", "LC", "HC" };

    /// <summary>
    /// Σ, in §8.0's order: 5 pure; the 10 two-type 50/50 pairs; the uniform mix; the 5 one-heavy mixes
    /// (60% of one type, 10% of each other).
    /// </summary>
    public static IReadOnlyList<Composition> Compositions { get; } = BuildCompositions();

    /// <summary>The matchup index <c>i = 21 × index(A) + index(B)</c>.</summary>
    public static int MatchupIndex(int attackerComposition, int defenderComposition) =>
        (Compositions.Count * attackerComposition) + defenderComposition;

    /// <summary>The seed for matchup <paramref name="matchup"/>, repetition <paramref name="repetition"/>.</summary>
    public static ulong Seed(int matchup, int repetition) => (SeedMultiplier * (ulong)matchup) + (ulong)repetition;

    /// <summary>Builds a composition's army at a scale, split by §8.0's rule.</summary>
    /// <param name="composition">The mix.</param>
    /// <param name="scale">T-scale or P-scale.</param>
    /// <param name="tables">The ruleset's tables (weights K01, battalion sizes K07, floors K08).</param>
    /// <param name="kappaPercent">§8.3's size factor in hundredths (100 = full size); T-scale only.</param>
    public static CandidateArmy Build(Composition composition, ArmyScale scale, CandidateTables tables, int kappaPercent = 100)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(tables);

        var troops = new long[tables.TypeCount];
        for (var t = 0; t < tables.TypeCount; t++)
        {
            troops[t] = scale == ArmyScale.Troops
                ? TroopScaleTotal * kappaPercent / 100 * composition.Shares[t] / 100
                : PowerScaleTotal * composition.Shares[t] / 100 / tables.Weight[t];
        }

        return Split(troops, tables);
    }

    /// <summary>
    /// §8.0's split: each type's troops into full battalions of <c>standardBattalionSize</c> plus one
    /// remainder unit; a remainder below the type's strength floor (K08) is added to the last full battalion
    /// instead. Slot order LI, HI, A, LC, HC, full battalions before the remainder.
    /// </summary>
    public static CandidateArmy Split(long[] troopsByType, CandidateTables tables)
    {
        ArgumentNullException.ThrowIfNull(troopsByType);
        ArgumentNullException.ThrowIfNull(tables);

        var units = new List<CandidateUnit>();
        for (var t = 0; t < tables.TypeCount; t++)
        {
            var total = troopsByType[t];
            if (total <= 0)
            {
                continue;
            }

            var size = tables.BattalionSize[t];
            var full = total / size;
            var remainder = total % size;
            var sizes = new List<long>();
            for (var i = 0; i < full; i++)
            {
                sizes.Add(size);
            }

            if (remainder > 0)
            {
                if (remainder < tables.RoutFloor[t] && sizes.Count > 0)
                {
                    sizes[^1] += remainder;
                }
                else
                {
                    sizes.Add(remainder);
                }
            }

            foreach (var s in sizes)
            {
                units.Add(new CandidateUnit(tables.TypeIds[t], (int)s, Quality));
            }
        }

        return new CandidateArmy(ValueList.From(units), Morale);
    }

    /// <summary>Troops per type index in <paramref name="army"/>, for the given per-slot troop counts.</summary>
    public static long[] ByType(CandidateArmy army, IReadOnlyList<int> troops, CandidateTables tables)
    {
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(troops);
        ArgumentNullException.ThrowIfNull(tables);
        var result = new long[tables.TypeCount];
        for (var i = 0; i < army.Units.Count; i++)
        {
            result[tables.IndexOf(army.Units[i].UnitTypeId)] += troops[i];
        }

        return result;
    }

    /// <summary>Troops per type index in <paramref name="army"/> at the start.</summary>
    public static long[] StartByType(CandidateArmy army, CandidateTables tables)
    {
        ArgumentNullException.ThrowIfNull(army);
        var troops = new int[army.Units.Count];
        for (var i = 0; i < troops.Length; i++)
        {
            troops[i] = army.Units[i].Troops;
        }

        return ByType(army, troops, tables);
    }

    private static List<Composition> BuildCompositions()
    {
        const int types = 5;
        var list = new List<Composition>();
        for (var t = 0; t < types; t++)
        {
            var shares = new int[types];
            shares[t] = 100;
            list.Add(new Composition(list.Count, TypeLabels[t], shares));
        }

        for (var a = 0; a < types; a++)
        {
            for (var b = a + 1; b < types; b++)
            {
                var shares = new int[types];
                shares[a] = 50;
                shares[b] = 50;
                list.Add(new Composition(list.Count, TypeLabels[a] + "+" + TypeLabels[b], shares));
            }
        }

        list.Add(new Composition(list.Count, "uniform", new[] { 20, 20, 20, 20, 20 }));

        for (var t = 0; t < types; t++)
        {
            var shares = new[] { 10, 10, 10, 10, 10 };
            shares[t] = 60;
            list.Add(new Composition(list.Count, TypeLabels[t] + "-heavy", shares));
        }

        return list;
    }
}
