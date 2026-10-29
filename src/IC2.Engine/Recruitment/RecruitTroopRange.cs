using IC2.Engine.Model;

namespace IC2.Engine.Recruitment;

/// <summary>
/// The troop amounts a new recruitment order may name for one unit type: one fifth of the type's
/// <see cref="UnitTypeRules.StandardBattalionSize"/> (rounded down) up to the full battalion.
/// </summary>
/// <remarks>
/// The original keeps this range in its recruit dialog only; <c>RecruitUnit</c> bounds no amount
/// <strong>[derived: decompiled; the heavy-infantry minimum of 1,200 seen live under Wine,
/// research <c>2026-10-29-v084-known-differences-and-command-gaps.md</c> §2]</strong>. T237 (#920 item 2,
/// the owner's decision of 2026-10-29) makes the engine refuse an amount outside it as well, so no path
/// (CLI, script, AI) orders what a player of the original could not. The recruit dialog's box reads the
/// same range, so the two never disagree.
/// </remarks>
public static class RecruitTroopRange
{
    /// <summary>The divisor turning a battalion size into the smallest order.</summary>
    public const int MinimumDivisor = 5;

    /// <summary>The smallest order for <paramref name="type"/>: one fifth of its battalion, rounded down.</summary>
    public static int Minimum(UnitTypeRules type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.StandardBattalionSize / MinimumDivisor;
    }

    /// <summary>The largest order for <paramref name="type"/>: its standard battalion.</summary>
    public static int Maximum(UnitTypeRules type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.StandardBattalionSize;
    }

    /// <summary>Whether <paramref name="troops"/> lies within <paramref name="type"/>'s range.</summary>
    public static bool Contains(UnitTypeRules type, int troops) =>
        troops >= Minimum(type) && troops <= Maximum(type);
}
