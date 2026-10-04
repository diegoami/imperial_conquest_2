using IC2.Engine.Core;

namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// The tactical battle's one draw seam, with the original's <c>Random(n)</c> semantics
/// (<c>docs/game-design.md</c> §Combat, "The tactical battle", "Every random draw, in order").
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: code, static, <c>2026-10-04-decompiled-tactical-battle-rules.md</c>, "Every
/// <c>Random</c> call, in draw order"]</strong>: every draw of the battle module is Delphi's
/// <c>System.Random(n)</c> (<c>FUN_0040284C</c>), which returns a value in <c>[0, n)</c>, and
/// <c>Random(0)</c> returns 0 <em>and still advances the generator</em>. Every routine under
/// <c>Battle/Tactical</c> draws only through this interface, so the order of the draws is the order of
/// the calls, and the battle's trace (<see cref="TacticalDrawEvent"/>) records each one.
/// </para>
/// <para>
/// Three implementations exist: <see cref="RngBattleDraws"/> (the game's, over an <see cref="IRng"/>
/// stream whose name and derivation are T125's), <see cref="DelphiBattleDraws"/> (the original's own
/// generator, so T129 can replay the original's seeds), and a scripted one in the tests.
/// </para>
/// </remarks>
public interface IBattleDraws
{
    /// <summary>
    /// Delphi's <c>Random(n)</c>: a value in <c>[0, n)</c> for a positive <paramref name="n"/>, 0 for
    /// <c>n = 0</c>. Every call consumes exactly one draw, whatever <paramref name="n"/> is.
    /// </summary>
    /// <param name="n">
    /// The bound. It may be zero (copy-in's <c>Random(quality × 4)</c> for quality 0) and, when a 32-bit
    /// product has wrapped (the melee's <c>troops × D</c>), negative; see each implementation for what a
    /// negative bound returns.
    /// </param>
    int Random(int n);
}

/// <summary>
/// The game's draw seam: <c>Random(n)</c> over one <see cref="IRng"/> stream (design principle 4).
/// </summary>
/// <remarks>
/// A positive bound draws <see cref="IRng.NextInt(int)"/>. A zero bound consumes one raw draw and returns
/// 0, the original's <c>Random(0)</c>. A negative bound (reachable only after a 32-bit wrap, the design's
/// "Integer semantics") consumes one raw draw and maps its high 32 bits exactly as Delphi's
/// <c>MUL</c> does with the bound read as unsigned, <c>(u × (uint)n) shr 32</c>, so the value has the
/// original's shape. <strong>[designed: the stream, T125's; confirmed: the order and the
/// <c>Random(0)</c> semantics]</strong>.
/// </remarks>
public sealed class RngBattleDraws : IBattleDraws
{
    private readonly IRng _rng;

    /// <summary>Wraps <paramref name="rng"/>; every draw advances it once.</summary>
    public RngBattleDraws(IRng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        _rng = rng;
    }

    /// <inheritdoc />
    public int Random(int n)
    {
        if (n > 0)
        {
            return _rng.NextInt(n);
        }

        var raw = _rng.NextUInt64();
        if (n == 0)
        {
            return 0;
        }

        var high = (uint)(raw >> 32);
        return unchecked((int)(uint)(((ulong)high * (uint)n) >> 32));
    }
}

/// <summary>
/// The original's own generator, Delphi <c>System.Random</c> (<c>FUN_0040284C</c>), so the golden master
/// (T129) can replay a battle from the lab build's baked <c>RandSeed</c>.
/// </summary>
/// <remarks>
/// <strong>[confirmed: code, <c>2026-10-03-new-game-turn-order-shuffle.md</c> item 3]</strong>:
/// <c>RandSeed := RandSeed × $08088405 + 1</c> (32-bit, wrapping), then
/// <c>Random(n) = (RandSeed × n) shr 32</c> on the <em>new</em> seed, unsigned. The multiplier is the
/// generator's definition, not a gameplay constant. A negative <paramref name="n"/> in
/// <see cref="Random"/> is read as unsigned, as the original's <c>MUL</c> reads it.
/// </remarks>
public sealed class DelphiBattleDraws : IBattleDraws
{
    /// <summary>Delphi's linear-congruential multiplier, <c>$08088405</c> (the report's item 3).</summary>
    private const uint Multiplier = 0x08088405u;

    /// <summary>Starts the generator at <paramref name="randSeed"/>, the original's <c>RandSeed</c>.</summary>
    public DelphiBattleDraws(uint randSeed) => RandSeed = randSeed;

    /// <summary>The generator's current <c>RandSeed</c>.</summary>
    public uint RandSeed { get; private set; }

    /// <inheritdoc />
    public int Random(int n)
    {
        RandSeed = unchecked((RandSeed * Multiplier) + 1u);
        return unchecked((int)(uint)(((ulong)RandSeed * (uint)n) >> 32));
    }
}
