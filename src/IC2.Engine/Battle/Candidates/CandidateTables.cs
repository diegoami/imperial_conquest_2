using IC2.Engine.Model;

namespace IC2.Engine.Battle.Candidates;

/// <summary>
/// The candidate-local constants of docs/investigations/auto-resolve-approaches.md §3/§4: the numbers the
/// shipped rulesets do not carry and T59 may not add to them (T59 Hazards: no ruleset field). Each is cited to
/// its register row. Everything the ruleset <em>does</em> carry is read from it, through <see cref="CandidateTables"/>.
/// </summary>
public static class CandidateConstants
{
    /// <summary>K08: the rout strength floor is <c>standardBattalionSize / 25</c>.</summary>
    public const int RoutFloorDivisor = 25;

    /// <summary>K09: a unit routs unless <c>m &gt; 19</c>.</summary>
    public const int RoutMoraleFloor = 19;

    /// <summary>K10: safe outright if <c>m &gt; 39</c>.</summary>
    public const int RoutSafeMorale = 39;

    /// <summary>K11: the band check survives if <c>Random(m) + Random(m) &gt; 29</c>.</summary>
    public const int RoutBandThreshold = 29;

    /// <summary>K12: every surviving friend loses 6 <c>m</c> on a rout.</summary>
    public const int CascadeMoraleLoss = 6;

    /// <summary>K13: a friend below 30 after the −6 is removed (one level only).</summary>
    public const int CascadeRemovalBelow = 30;

    /// <summary>K14: every live enemy gains 5 <c>m</c> on a rout.</summary>
    public const int RoutReward = 5;

    /// <summary>K14, K19: the in-battle upper clamp on tactical morale.</summary>
    public const int TacticalMoraleCap = 99;

    /// <summary>D08: the in-battle lower clamp on tactical morale ([designed], never binds).</summary>
    public const int TacticalMoraleFloor = 0;

    /// <summary>D08 [confirmed]: the initial tactical morale is clamped to <c>[60, 90]</c>.</summary>
    public const int InitialMoraleMin = 60;

    /// <summary>D08 [confirmed].</summary>
    public const int InitialMoraleMax = 90;

    /// <summary>K26: <c>m = Random(q × 4) + M</c>.</summary>
    public const int InitialMoraleQualityMultiplier = 4;

    /// <summary>K27: <c>M += 3</c> on battle entry. C2/C5 apply it to BOTH sides (the survey's placeholder).</summary>
    public const int BattleEntryMoraleBonus = 3;

    /// <summary>K17: <c>defFactor = min(4, focusCount)</c>.</summary>
    public const int FocusCap = 4;

    /// <summary>K17: the focus multipliers' denominator, <c>(5 − d)/5</c> and <c>(2d + 5)/5</c>.</summary>
    public const int FocusBase = 5;

    /// <summary>K18: the quality term <c>q × 10 + m</c>.</summary>
    public const int QualityWeight = 10;

    /// <summary>K16: <c>exA = (ta × defPow / atkPow) / 12 + 1</c>.</summary>
    public const int MeleeAttackerExchangeDivisor = 12;

    /// <summary>K16: <c>exD = (td × atkPow / defPow) / 10 + 1</c>.</summary>
    public const int MeleeDefenderExchangeDivisor = 10;

    /// <summary>K19: <c>+2</c> to the better side of a melee exchange.</summary>
    public const int MeleeMoraleGain = 2;

    /// <summary>K19: <c>−3</c> to the other side.</summary>
    public const int MeleeMoraleLoss = 3;

    /// <summary>K21: the shooting base's <c>ts × 5 + 150000</c> denominator, the multiplier.</summary>
    public const int ShotTroopMultiplier = 5;

    /// <summary>K21: the shooting base's <c>ts × 5 + 150000</c> denominator, the constant.</summary>
    public const int ShotDenominatorConstant = 150000;

    /// <summary>K21: <c>min(shooter / 3, …)</c>.</summary>
    public const int ShotShooterDivisor = 3;

    /// <summary>K21: <c>min(…, target / 2)</c>.</summary>
    public const int ShotTargetDivisor = 2;

    /// <summary>K22: <c>m −= min(3, loss × 35 / (troopsAfter + 1))</c>, the multiplier.</summary>
    public const int ShotMoraleMultiplier = 35;

    /// <summary>K22: the cap.</summary>
    public const int ShotMoraleCap = 3;

    /// <summary>K30: 14 columns.</summary>
    public const int GridColumns = 14;

    /// <summary>K30: 12 rows.</summary>
    public const int GridRows = 12;

    /// <summary>K30: the attacker's home row (D01 assigns row 2 to the attacker).</summary>
    public const int AttackerHomeRow = 2;

    /// <summary>K30: the defender's home row.</summary>
    public const int DefenderHomeRow = 9;

    /// <summary>D01: units 15–20 go to the next row inward.</summary>
    public const int UnitsPerHomeRow = 14;

    /// <summary>D09: the C2/C5 round cap.</summary>
    public const int TacticalRoundCap = 100;

    /// <summary>D21: <c>Xf = vuln × shooterShare1000 × 2 / 9</c> (the 4.5 scale).</summary>
    public const int FireExposureNumerator = 2;

    /// <summary>D21.</summary>
    public const int FireExposureDenominator = 9;

    /// <summary>D20: the one-matrix-point floor on exposure, ×1000.</summary>
    public const int MeleeExposureFloor1000 = 1000;

    /// <summary>§3 fixed point: shares are ×1000.</summary>
    public const int SharePerMille = 1000;

    /// <summary>C3/C4: <c>Mel_i = mbar1000 × troops × (q×10 + M) / 2,000,000 + 12</c> — K16's /2000 times the ×1000 share.</summary>
    public const long MatrixPowerDivisor = 2_000_000;

    /// <summary>D30: rounds 1–3 are C4's fire rounds.</summary>
    public const int C4FireRounds = 3;

    /// <summary>D31: C4's shock die, <c>Random(10)</c>.</summary>
    public const int C4ShockDie = 10;

    /// <summary>D31: <c>(5 + d)</c>.</summary>
    public const int C4ShockDieBase = 5;

    /// <summary>D31: the <c>/ 60</c> pace.</summary>
    public const int C4ShockPace = 60;

    /// <summary>D32: <c>P −= 200 × losses / troopsAtStart</c>.</summary>
    public const int C4MoraleDamage = 200;

    /// <summary>D33: C4's round cap.</summary>
    public const int C4RoundCap = 30;

    /// <summary>D33: pursuit deals shock damage × 2.</summary>
    public const int C4PursuitMultiplier = 2;

    /// <summary>D40: C5's disorder cost, <c>troops × 5 / 100</c>.</summary>
    public const int C5DisorderPercent = 5;

    /// <summary>D41: at most 2 cavalry pursuers per fleeing unit.</summary>
    public const int C5MaxPursuers = 2;

    /// <summary>D41: at most 2 pursuing shots per fleeing unit.</summary>
    public const int C5MaxPursuingShots = 2;

    /// <summary>D42: an ordered withdrawal's pursuit losses are halved.</summary>
    public const int C5OrderedDiscountDivisor = 2;

    /// <summary>D43: withdraw if <c>liveP_S × 100 &lt; 60 × liveP_O</c>.</summary>
    public const int C5WithdrawalPercent = 60;

    /// <summary>D44: the earliest withdrawal is at the end of round 3.</summary>
    public const int C5EarliestWithdrawalRound = 3;

    /// <summary>K20: the shooting vulnerability <c>+0x20</c>, which is in no ruleset (§3). Keyed by unit-type id.</summary>
    /// <param name="unitTypeId">One of the five shipped unit types.</param>
    public static int Vulnerability(string unitTypeId) => unitTypeId switch
    {
        "light_infantry" => 18,
        "heavy_infantry" => 2,
        "archers" => 18,
        "light_cavalry" => 15,
        "heavy_cavalry" => 4,
        _ => throw new ArgumentOutOfRangeException(
            nameof(unitTypeId), unitTypeId, "K20 is known only for the five shipped unit types."),
    };

    /// <summary>§6.4 and §6.5.2: "cavalry" means <c>light_cavalry</c> and <c>heavy_cavalry</c>.</summary>
    /// <param name="unitTypeId">A unit-type id.</param>
    public static bool IsCavalry(string unitTypeId) =>
        unitTypeId is "light_cavalry" or "heavy_cavalry";
}

/// <summary>
/// The per-type tables every candidate reads, resolved once from a <see cref="Ruleset"/>: indices follow
/// <c>combat.detailedResolver.typeEffectivenessOrder</c>, so <see cref="Matrix"/> is K15 exactly as shipped.
/// </summary>
public sealed class CandidateTables
{
    private CandidateTables(Ruleset ruleset)
    {
        Ruleset = ruleset;
        var detailed = ruleset.Combat.DetailedResolver;
        var order = detailed.TypeEffectivenessOrder;
        TypeIds = order.ToArray();
        var count = TypeIds.Length;
        Weight = new int[count];
        BattalionSize = new int[count];
        RoutFloor = new int[count];
        Shots = new int[count];
        Range = new int[count];
        Moves = new int[count];
        Vulnerability = new int[count];
        IsCavalry = new bool[count];
        Matrix = new int[count, count];

        for (var i = 0; i < count; i++)
        {
            var type = ruleset.UnitTypeById(TypeIds[i])
                       ?? throw new InvalidOperationException($"typeEffectivenessOrder names '{TypeIds[i]}', which is no unit type.");
            Weight[i] = type.CombatPowerWeight;           // K01
            BattalionSize[i] = type.StandardBattalionSize; // K07
            RoutFloor[i] = type.StandardBattalionSize / CandidateConstants.RoutFloorDivisor; // K08
            Shots[i] = type.Shots;                         // K23
            Range[i] = type.Range;                         // K24
            Moves[i] = type.Moves;                         // K25
            Vulnerability[i] = CandidateConstants.Vulnerability(TypeIds[i]); // K20
            IsCavalry[i] = CandidateConstants.IsCavalry(TypeIds[i]);
            for (var j = 0; j < count; j++)
            {
                Matrix[i, j] = detailed.TypeEffectiveness[i][j]; // K15, value[attacker][defender]
            }
        }
    }

    /// <summary>The ruleset these tables came from.</summary>
    public Ruleset Ruleset { get; }

    /// <summary>The type ids, in <c>typeEffectivenessOrder</c>.</summary>
    public string[] TypeIds { get; }

    /// <summary>The number of unit types.</summary>
    public int TypeCount => TypeIds.Length;

    /// <summary>K01.</summary>
    public int[] Weight { get; }

    /// <summary>K07.</summary>
    public int[] BattalionSize { get; }

    /// <summary>K08.</summary>
    public int[] RoutFloor { get; }

    /// <summary>K23.</summary>
    public int[] Shots { get; }

    /// <summary>K24.</summary>
    public int[] Range { get; }

    /// <summary>K25.</summary>
    public int[] Moves { get; }

    /// <summary>K20.</summary>
    public int[] Vulnerability { get; }

    /// <summary>Light or heavy cavalry.</summary>
    public bool[] IsCavalry { get; }

    /// <summary>K15, <c>[attackerType, defenderType]</c>.</summary>
    public int[,] Matrix { get; }

    /// <summary>Builds the tables for <paramref name="ruleset"/>.</summary>
    public static CandidateTables For(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return new CandidateTables(ruleset);
    }

    /// <summary>The index of <paramref name="unitTypeId"/>.</summary>
    public int IndexOf(string unitTypeId)
    {
        for (var i = 0; i < TypeIds.Length; i++)
        {
            if (string.Equals(TypeIds[i], unitTypeId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(unitTypeId), unitTypeId, "Not in typeEffectivenessOrder.");
    }

    /// <summary>
    /// <c>share1000(t) = 1000 × troops of type t / total troops</c> (D20), over the given troop counts.
    /// </summary>
    /// <param name="types">Each unit's type index.</param>
    /// <param name="troops">Each unit's troops; units at 0 count for nothing.</param>
    public long[] Shares1000(int[] types, long[] troops)
    {
        var byType = new long[TypeCount];
        long total = 0;
        for (var i = 0; i < types.Length; i++)
        {
            if (troops[i] <= 0)
            {
                continue;
            }

            byType[types[i]] += troops[i];
            total += troops[i];
        }

        var shares = new long[TypeCount];
        if (total == 0)
        {
            return shares;
        }

        for (var t = 0; t < TypeCount; t++)
        {
            shares[t] = CandidateConstants.SharePerMille * byType[t] / total;
        }

        return shares;
    }

    /// <summary><c>mbar1000_i = Σ_t share1000_O(t) × matrix[type_i][t]</c> (§6.3).</summary>
    public long MeleeBar1000(int type, long[] enemyShares)
    {
        long sum = 0;
        for (var t = 0; t < TypeCount; t++)
        {
            sum += enemyShares[t] * Matrix[type, t];
        }

        return sum;
    }

    /// <summary><c>vbar1000_O = Σ_t share1000_O(t) × vuln[t]</c> (§6.3).</summary>
    public long VulnerabilityBar1000(long[] enemyShares)
    {
        long sum = 0;
        for (var t = 0; t < TypeCount; t++)
        {
            sum += enemyShares[t] * Vulnerability[t];
        }

        return sum;
    }

    /// <summary>D20: <c>Xm_E(u) = 1000 + Σ_t share1000_E(t) × matrix[t][u]</c>.</summary>
    public long MeleeExposure1000(int unitType, long[] enemyShares)
    {
        long sum = CandidateConstants.MeleeExposureFloor1000;
        for (var t = 0; t < TypeCount; t++)
        {
            sum += enemyShares[t] * Matrix[t, unitType];
        }

        return sum;
    }

    /// <summary>D21: <c>Xf_E(u) = vuln[u] × shooterShare1000_E × 2 / 9</c>.</summary>
    public long FireExposure1000(int unitType, long[] enemyShares)
    {
        long shooterShare = 0;
        for (var t = 0; t < TypeCount; t++)
        {
            if (Shots[t] > 0)
            {
                shooterShare += enemyShares[t];
            }
        }

        return Vulnerability[unitType] * shooterShare * CandidateConstants.FireExposureNumerator
               / CandidateConstants.FireExposureDenominator;
    }
}
