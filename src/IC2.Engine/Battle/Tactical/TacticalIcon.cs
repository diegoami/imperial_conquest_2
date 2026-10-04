namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// The icon code a grid cell holds (<c>FUN_00437C40</c>, <c>TBattleMap_PrintSquare</c>):
/// <strong>[confirmed: code + resource, static, <c>2026-10-04-decompiled-tactical-battle-rules.md</c>
/// §1 "Grid" and §10]</strong>; <c>docs/game-design.md</c>, "The board and the battle state".
/// </summary>
/// <remarks>
/// <para>
/// A cell holds <see cref="Empty"/> (50), or <c>type × 3 + size</c> for an attacker unit and the same
/// <c>+ 20</c> for a defender unit, where <c>size = min(2, troops div (standardBattalionSize div 3))</c>:
/// the 15 icons (5 types × 3 sizes) of the battle's image list, the attacker's copies 0–14 and the
/// defender's 20–34. The sweep found the same rule in 567 of 585 saves, the other 18 being crafted ones
/// (B2). The thresholds this gives are LI 5,000/10,000, HI 2,000/4,000, archers 1,166/2,332, LC
/// 2,333/4,666 and HC 833/1,666 (report §10).
/// </para>
/// <para>
/// These four numbers are the battle block's encoding of a cell (the save format's, and the sprite
/// index's), not a rule's value: the size divisor <c>3</c> is the image list's three sizes per type, as
/// the multiplier <c>type × 3</c> is. They are written once here, cited, and read by nothing but the grid.
/// </para>
/// </remarks>
public static class TacticalIcon
{
    /// <summary>The code of an empty cell, <c>50</c> (report §1).</summary>
    public const int Empty = 50;

    /// <summary>The defender's offset, <c>+20</c> (report §1).</summary>
    public const int DefenderOffset = 20;

    /// <summary>The sizes per type: the image list's small, medium and large icon (report §10).</summary>
    public const int SizesPerType = 3;

    /// <summary>
    /// The icon size of <paramref name="troops"/> of a type with standard battalion
    /// <paramref name="standardBattalionSize"/>: <c>min(2, troops div (std div 3))</c>.
    /// </summary>
    public static int Size(int troops, int standardBattalionSize) =>
        Math.Min(SizesPerType - 1, troops / (standardBattalionSize / SizesPerType));

    /// <summary>The icon code for a unit of <paramref name="type"/> on <paramref name="side"/>.</summary>
    public static int Code(int side, int type, int troops, int standardBattalionSize) =>
        (type * SizesPerType) + Size(troops, standardBattalionSize)
        + (side == TacticalBattleState.DefenderSide ? DefenderOffset : 0);
}
