using IC2.Engine.Model;

namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// The ruleset's numbers as the tactical battle reads them: <c>combat.tactical</c> (T122),
/// <c>unitTypes[]</c> (moves, shots, range, <c>standardBattalionSize</c>, <c>shotVulnerability</c>),
/// <c>combat.detailedResolver</c> (the melee matrix and divisors, T120) and the write-back's fields.
/// Every gameplay number the battle uses comes from here, never from a C# literal (design principle 1).
/// </summary>
/// <remarks>
/// The unit type of a slot is its index into <see cref="Ruleset.UnitTypes"/>, the original's type word
/// (0 LI, 1 HI, 2 archers, 3 LC, 4 HC). The matrix is read through
/// <see cref="DetailedResolverRules.TypeEffectivenessOrder"/> by type id, so a ruleset may list it in any
/// order. <c>classical-faithful</c> and <c>improved</c> run the same rules here; nothing reads a preset flag.
/// </remarks>
public sealed class TacticalContext
{
    private readonly int[] _matrixIndex;

    private TacticalContext(Ruleset ruleset)
    {
        Ruleset = ruleset;
        Rules = ruleset.Combat.Tactical;
        Melee = ruleset.Combat.DetailedResolver;
        Combat = ruleset.Combat;
        SlotsPerSide = ruleset.ArmyManagement.MaxUnitsPerArmy;

        var types = ruleset.UnitTypes;
        TypeCount = types.Count;
        _matrixIndex = new int[TypeCount];
        for (var t = 0; t < TypeCount; t++)
        {
            var index = Melee.TypeEffectivenessOrder.ToList().IndexOf(types[t].Id);
            if (index < 0)
            {
                throw new ArgumentException(
                    $"Ruleset '{ruleset.Id}': unit type '{types[t].Id}' has no row in combat.detailedResolver.typeEffectivenessOrder.",
                    nameof(ruleset));
            }

            _matrixIndex[t] = index;
        }

        ArcherType = TypeIndexOf(ruleset.ArcherUnitTypeId);
        SlowAdvanceExemptType = TypeIndexOf(Rules.SlowAdvanceExemptType);
    }

    /// <summary>The ruleset the context was built from.</summary>
    public Ruleset Ruleset { get; }

    /// <summary><c>combat.tactical</c>.</summary>
    public TacticalBattleRules Rules { get; }

    /// <summary><c>combat.detailedResolver</c>: the melee matrix, divisors and caps.</summary>
    public DetailedResolverRules Melee { get; }

    /// <summary><c>combat</c>: the write-back's quality floor and cap, promotion chance, unity swing and supply divisor.</summary>
    public CombatRules Combat { get; }

    /// <summary>Slots per side: <c>armyManagement.maxUnitsPerArmy</c> (20), the army's unit records.</summary>
    public int SlotsPerSide { get; }

    /// <summary>The number of unit types.</summary>
    public int TypeCount { get; }

    /// <summary>The archers' type index (<see cref="Ruleset.ArcherUnitTypeId"/>), or <c>−1</c>.</summary>
    public int ArcherType { get; }

    /// <summary>The slow advance's exempt type (heavy infantry), or <c>−1</c>.</summary>
    public int SlowAdvanceExemptType { get; }

    /// <summary>Builds the context for <paramref name="ruleset"/>.</summary>
    public static TacticalContext From(Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return new TacticalContext(ruleset);
    }

    /// <summary>The type index of <paramref name="unitTypeId"/>, or <c>−1</c>.</summary>
    public int TypeIndexOf(string unitTypeId) => Ruleset.UnitTypes.IndexOfId(u => u.Id, unitTypeId);

    /// <summary>The type's unit-type record.</summary>
    public UnitTypeRules TypeOf(int type) => Ruleset.UnitTypes[type];

    /// <summary>The type's moves per half-round (stat; 4/2/4/6/5).</summary>
    public int MovesOf(int type) => TypeOf(type).Moves;

    /// <summary>The type's shots for the battle (stat <c>+0x1C</c>; 7/0/25/9/0).</summary>
    public int ShotsOf(int type) => TypeOf(type).Shots;

    /// <summary>The type's shooting range (1/0/2/1/0).</summary>
    public int RangeOf(int type) => TypeOf(type).Range;

    /// <summary>The type's standard battalion size.</summary>
    public int StandardOf(int type) => TypeOf(type).StandardBattalionSize;

    /// <summary>The type's shooting vulnerability (stat <c>+0x20</c>; 18/2/18/15/4).</summary>
    public int VulnerabilityOf(int type) => TypeOf(type).ShotVulnerability;

    /// <summary>The melee matrix, <c>M[attackerType][defenderType]</c> (DAT <c>0x1F7A6</c>).</summary>
    public int Matrix(int attackerType, int defenderType) =>
        Melee.TypeEffectiveness[_matrixIndex[attackerType]][_matrixIndex[defenderType]];
}
