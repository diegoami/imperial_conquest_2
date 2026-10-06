using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// T146: the draws the original makes when a leader is replaced — a human seat's fall
/// (<c>FUN_00449078</c> :47790–47791, then <c>FUN_0044c8f0</c> :50789–50794), a computer nation's
/// deposition (<c>FUN_0044c8f0</c>, second draw only), and rebirth's single name draw
/// (<c>FUN_0044C360</c>, <c>decompiled-quarterly-rebellion.md</c> §4). Every draw is a
/// <see cref="IRng.NextInt(int)"/> against the nation's own pool size (the DAT's 12 names), from a
/// pool this task exports into the world (<see cref="NationDefinition.LeaderNames"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>A human fall is two draws, the second retrying.</strong>
/// <c>TPremierForm_HumanLeaderFalls</c> first writes one <c>Random(12)</c> entry, then repeats
/// <c>Random(12)</c> until the drawn name differs from the field's <em>current</em> content — which is
/// the first draw, not the old leader — and writes it. So the final name can be the old leader again
/// (the second draw differs from the first only), and the draw count is <c>1 + k</c> where <c>k >= 1</c>
/// is the number of second-draw tries. <see cref="ApplyFall"/> reproduces exactly that order.
/// </para>
/// <para>
/// <strong>A computer deposition is the second draw alone.</strong> The first draw lives inside
/// <c>TPremierForm_HumanLeaderFalls</c>, which a computer nation never reaches; <c>FUN_0044c8f0</c>
/// writes the news line with the old leader and then repeats <c>Random(12)</c> until the name differs
/// from the old leader. <see cref="ApplyDeposition"/> is that loop; the news event itself is the
/// caller's, published with the old leader.
/// </para>
/// <para>
/// <strong>Rebirth is one unconditional draw.</strong> <c>FUN_0044C360</c> makes one <c>Random(12)</c>
/// from the nation's 12 names with no "differs from the current name" retry;
/// <see cref="ApplyRebirth"/> writes that entry. It draws from the caller's own stream (the shared
/// quarterly one <see cref="Rebirth.Run"/> already uses), not from a per-event stream — see that
/// method's own remarks.
/// </para>
/// <para>
/// <strong>Refuse, never spin.</strong> A pool with fewer than two distinct names can never satisfy a
/// retry-until-differs draw. Validation forbids such a pool, but a state built directly (bypassing
/// <c>GameDataValidation</c>) could still carry one, so both retry loops check first and throw
/// <see cref="InvalidOperationException"/> rather than looping forever.
/// </para>
/// </remarks>
public static class LeaderSuccession
{
    /// <summary>
    /// The fall's own stream name for one nation at one calendar point, from which
    /// <see cref="FallStream"/> derives the generator. The key names the occasion, the nation and the
    /// calendar point, so two falls of one nation at different points draw afresh while a resume
    /// derives the same key from the already-persisted seed and calendar.
    /// </summary>
    public static string FallStreamKey(string nationId, CalendarState calendar) =>
        $"leaders.fall/{nationId}/{calendar.YearBc}/{calendar.SeasonIndex}/{calendar.Week}/{calendar.TurnIndex}";

    /// <summary>The computer deposition's own stream name, the same shape as <see cref="FallStreamKey"/>.</summary>
    public static string DepositionStreamKey(string nationId, CalendarState calendar) =>
        $"leaders.deposition/{nationId}/{calendar.YearBc}/{calendar.SeasonIndex}/{calendar.Week}/{calendar.TurnIndex}";

    /// <summary>
    /// The fall's generator: <c>SplitMix64Rng.ForStream(rootSeed, key)</c> — the brief's own derivation,
    /// never the shared quarterly <c>context.Rng</c>, so a fall does not move any later quarterly draw.
    /// </summary>
    public static IRng FallStream(ulong rootSeed, string nationId, CalendarState calendar) =>
        SplitMix64Rng.ForStream(rootSeed, FallStreamKey(nationId, calendar));

    /// <summary>The computer deposition's generator, derived exactly like <see cref="FallStream"/>.</summary>
    public static IRng DepositionStream(ulong rootSeed, string nationId, CalendarState calendar) =>
        SplitMix64Rng.ForStream(rootSeed, DepositionStreamKey(nationId, calendar));

    /// <summary>The nation's exported leader-name pool, or <see langword="null"/> when the world carries none.</summary>
    public static ValueList<string>? PoolFor(World world, string nationId)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.NationById(nationId)?.LeaderNames;
    }

    /// <summary>
    /// A human seat's fall: one draw writes <c>pool[i]</c>, then draws repeat until the entry differs
    /// from that first draw. See this type's own remarks for the draw count and why the old leader can
    /// come back.
    /// </summary>
    /// <param name="nation">The nation as it stands at the fall (its pre-fall leader is overwritten).</param>
    /// <param name="pool">The nation's leader-name pool; must hold at least two distinct names.</param>
    /// <param name="stream">The fall's own stream (<see cref="FallStream"/>).</param>
    public static NationState ApplyFall(NationState nation, ValueList<string> pool, IRng stream)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(stream);
        RequireDrawable(pool, nation.Id);

        var first = pool[stream.NextInt(pool.Count)];
        var second = pool[stream.NextInt(pool.Count)];
        while (string.Equals(second, first, StringComparison.Ordinal))
        {
            second = pool[stream.NextInt(pool.Count)];
        }

        return nation with { LeaderName = second };
    }

    /// <summary>
    /// A computer nation's deposition: draws repeat until the entry differs from the nation's
    /// <em>old</em> leader. See this type's own remarks.
    /// </summary>
    /// <param name="nation">The deposed nation, still carrying its old leader.</param>
    /// <param name="pool">The nation's leader-name pool; must hold at least two distinct names.</param>
    /// <param name="stream">The deposition's own stream (<see cref="DepositionStream"/>).</param>
    public static NationState ApplyDeposition(NationState nation, ValueList<string> pool, IRng stream)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(stream);
        RequireDrawable(pool, nation.Id);

        var draw = pool[stream.NextInt(pool.Count)];
        while (string.Equals(draw, nation.LeaderName, StringComparison.Ordinal))
        {
            draw = pool[stream.NextInt(pool.Count)];
        }

        return nation with { LeaderName = draw };
    }

    /// <summary>
    /// Rebirth's one unconditional draw: the entry the stream selects, written with no retry. See
    /// <see cref="Rebirth.Run"/> for why this draw stays on the shared quarterly stream.
    /// </summary>
    public static NationState ApplyRebirth(NationState nation, ValueList<string> pool, IRng stream)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(stream);

        return nation with { LeaderName = pool[stream.NextInt(pool.Count)] };
    }

    /// <summary>
    /// A retry-until-differs draw needs at least two distinct names; a pool that slipped past
    /// validation is refused rather than spun on.
    /// </summary>
    private static void RequireDrawable(ValueList<string> pool, string nationId)
    {
        if (pool.Count == 0)
        {
            throw new InvalidOperationException($"Nation '{nationId}' has an empty leader-name pool.");
        }

        for (var i = 1; i < pool.Count; i++)
        {
            if (!string.Equals(pool[i], pool[0], StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Nation '{nationId}' has a leader-name pool of {pool.Count} identical names; a differing draw is impossible.");
    }
}
