using IC2.Engine.Core;

namespace IC2.Engine.Armies.Commands;

/// <summary>
/// T159 (#959): the AI's army merge under <c>classical-faithful</c> — <c>FUN_004509f0</c>, decompiled in
/// <c>2026-10-11-decompiled-ai-merge-armies-and-fleets.md</c> (research <c>3c3707b</c>). The donor
/// (<paramref name="AbsorbedArmyId"/>) is absorbed by its partner (<paramref name="SurvivingArmyId"/>): the
/// partner takes the donor's money, supplies and unit slots, and the donor is deleted at once (the user's
/// decision 4 of 2026-10-11). Unlike <see cref="JoinArmiesCommand"/> it works at a distance (Chebyshev below
/// 18) and by other gates; only an AI seat under <c>flags.aiEconomy = original</c> may issue it.
/// </summary>
/// <remarks>
/// The AI's choice of the pair (the first donor in table order with a valid partner, then the first valid
/// partner) is made by <c>AiFaithfulEconomy.MergeArmies</c>; the handler re-checks the gates so no other
/// caller can force a merge the original would refuse. No news line, event or treasury write
/// [confirmed: decompile, A11].
/// </remarks>
/// <param name="IssuingNationId">The AI-controlled nation that owns both armies.</param>
/// <param name="SurvivingArmyId">The partner army, which survives and keeps its position, morale and every other field.</param>
/// <param name="AbsorbedArmyId">The donor army, which is deleted once its contents move.</param>
public sealed record AiAbsorbArmyCommand(string IssuingNationId, string SurvivingArmyId, string AbsorbedArmyId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "armies.ai-absorb-army";
}

/// <summary>Rejection codes <see cref="AiAbsorbArmyCommandHandler"/> declares — see <see cref="RejectionCode"/>.</summary>
public static class AiAbsorbArmyRejections
{
    /// <summary>The issuing seat is not AI-controlled (or is unknown or eliminated): a human never reaches this command.</summary>
    public static readonly RejectionCode NotAnAiSeat = new("armies.ai-absorb-not-ai-seat");

    /// <summary>The ruleset's <c>flags.aiEconomy</c> is not <c>original</c> (<c>improved</c> never merges at a distance).</summary>
    public static readonly RejectionCode NotOriginalAi = new("armies.ai-absorb-not-original-ai");

    /// <summary>The two named army ids are the same army.</summary>
    public static readonly RejectionCode SameArmy = new("armies.ai-absorb-same-army");

    /// <summary>Either named army id does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("armies.ai-absorb-unknown-army");

    /// <summary>Either army belongs to a nation other than the issuing one.</summary>
    public static readonly RejectionCode NotYourArmy = new("armies.ai-absorb-not-your-army");

    /// <summary>The pair fails one of the original's donor or partner gates (troops, distance, slot extent, aboard).</summary>
    public static readonly RejectionCode GateFailed = new("armies.ai-absorb-gate-failed");
}
