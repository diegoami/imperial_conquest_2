using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// T159 (#959): the AI's fleet merge under <c>classical-faithful</c> — <c>FUN_00450b30</c>, decompiled in
/// <c>2026-10-11-decompiled-ai-merge-armies-and-fleets.md</c> (research <c>3c3707b</c>). The direction is the
/// reverse of what the old summary row read: the idle outer fleet (<paramref name="SurvivingFleetId"/>, under
/// 40 ships) survives and takes the partner's ships, money and supplies, and the partner
/// (<paramref name="AbsorbedFleetId"/>, any size) is deleted at once (the user's decision 4 of 2026-10-11).
/// Unlike <see cref="JoinFleetsCommand"/> it works at a distance (Chebyshev below 34); only an AI seat under
/// <c>flags.aiEconomy = original</c> may issue it.
/// </summary>
/// <remarks>
/// The AI's choice of the pair is made by <c>AiFaithfulEconomy.MergeFleets</c>; the handler re-checks the gates.
/// The survivor's position, moves, condition and owner are untouched; the absorbed fleet's moves and
/// condition are discarded [confirmed: decompile, F4]. No news line, event or treasury write [F6].
/// </remarks>
/// <param name="IssuingNationId">The AI-controlled nation that owns both fleets.</param>
/// <param name="SurvivingFleetId">The idle outer fleet, which survives.</param>
/// <param name="AbsorbedFleetId">The partner fleet, which is deleted once its ships, money and supplies move.</param>
public sealed record AiAbsorbFleetCommand(string IssuingNationId, string SurvivingFleetId, string AbsorbedFleetId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.ai-absorb-fleet";
}

/// <summary>Rejection codes <see cref="AiAbsorbFleetCommandHandler"/> declares — see <see cref="RejectionCode"/>.</summary>
public static class AiAbsorbFleetRejections
{
    /// <summary>The issuing seat is not AI-controlled (or is unknown or eliminated): a human never reaches this command.</summary>
    public static readonly RejectionCode NotAnAiSeat = new("naval.ai-absorb-not-ai-seat");

    /// <summary>The ruleset's <c>flags.aiEconomy</c> is not <c>original</c> (<c>improved</c> never merges at a distance).</summary>
    public static readonly RejectionCode NotOriginalAi = new("naval.ai-absorb-not-original-ai");

    /// <summary>The two named fleet ids are the same fleet.</summary>
    public static readonly RejectionCode SameFleet = new("naval.ai-absorb-same-fleet");

    /// <summary>Either named fleet id does not exist.</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.ai-absorb-unknown-fleet");

    /// <summary>Either fleet belongs to a nation other than the issuing one.</summary>
    public static readonly RejectionCode NotYourFleet = new("naval.ai-absorb-not-your-fleet");

    /// <summary>The pair fails one of the original's gates (ships, distance, combined ships, carried army, under construction).</summary>
    public static readonly RejectionCode GateFailed = new("naval.ai-absorb-gate-failed");
}
