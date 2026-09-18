using IC2.Engine.Core;

namespace IC2.Engine.Naval.Commands;

/// <summary>
/// Lands an embarked army — <c>docs/task-catalogue.md</c> "T14 Naval" DoD 15, added after the first
/// review found the fleet-move handler left a carried army behind with no way to ever land it. Restores
/// the army's covered map cell (the original's <c>army[+8]</c> sentinel, <c>FUN_0044B840</c>) and zeroes
/// its moves (<c>army[+6] = 0</c>).
/// </summary>
/// <param name="X">
/// The requested landing tile's X coordinate, or <see langword="null"/> to let the handler auto-pick the
/// nearest passable-for-army tile — <c>FUN_0044B840</c>'s confirmed "automatic landing-tile branch for an
/// AI seat". A human seat must name a tile explicitly, matching the presumed click-to-land UI the report
/// does not further detail: <see cref="Commands.DisembarkArmyRejections.LandingTileRequired"/> fires
/// when a human seat omits it.
/// </param>
/// <param name="Y">See <see cref="X"/>.</param>
/// <remarks>
/// <c>[open, flagged not fixed]</c> (round 2 review, N12): this does not check whether the landing tile
/// is already occupied by a hostile army or city — landing onto contested ground is a T16/T17 concern
/// (battle resolution, capture), and no Done-when line here asks for it. Left as a bare terrain-passability
/// check.
/// </remarks>
public sealed record DisembarkArmyCommand(string IssuingNationId, string ArmyId, int? X, int? Y) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "naval.disembark-army";
}

/// <summary>Rejection codes this command's handler declares, in its own directory — see <see cref="RejectionCode"/>.</summary>
public static class DisembarkArmyRejections
{
    /// <summary>The command names an army id that does not exist.</summary>
    public static readonly RejectionCode UnknownArmy = new("naval.unknown-army");

    /// <summary>The named army belongs to a nation other than the one issuing the command.</summary>
    public static readonly RejectionCode NotYourArmy = new("naval.not-your-army");

    /// <summary>The army is not currently embarked.</summary>
    public static readonly RejectionCode ArmyNotEmbarked = new("naval.army-not-embarked");

    /// <summary>The fleet carrying the army was under construction (never actually reachable in practice, since an embarked army implies a launched fleet -- checked anyway).</summary>
    public static readonly RejectionCode UnknownFleet = new("naval.unknown-fleet");

    /// <summary>A human seat omitted the landing tile; the automatic branch is AI-only.</summary>
    public static readonly RejectionCode LandingTileRequired = new("naval.landing-tile-required");

    /// <summary>The requested landing tile is not adjacent to the carrying fleet.</summary>
    public static readonly RejectionCode LandingTileTooFar = new("naval.landing-tile-too-far");

    /// <summary>The requested (or auto-picked) landing tile is not passable for an army.</summary>
    public static readonly RejectionCode LandingTileNotPassable = new("naval.landing-tile-not-passable");

    /// <summary>No passable landing tile exists adjacent to the fleet, for the automatic (AI) branch.</summary>
    public static readonly RejectionCode NoLandingTileAvailable = new("naval.no-landing-tile-available");
}
