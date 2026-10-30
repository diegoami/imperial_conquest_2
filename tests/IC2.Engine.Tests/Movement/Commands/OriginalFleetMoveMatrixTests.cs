using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Movement.Commands;

public sealed partial class OriginalFleetMoveTests
{
    // Every defined active E1FC row from part3-v3 (3452f6e). The three no-dispatch
    // cases belong to F608's matrix; coincident CB40 has its explicit safety test.
    public static IEnumerable<object[]> MovementControls => new object[][]
    {
        new object[] { "cargo-positive", 49, 62, 25, 48, 66, 48, 66, 21, true, false, false },
        new object[] { "cargo-zero", 49, 62, 0, 48, 66, 49, 62, 0, true, false, false },
        new object[] { "phase-alliance", 49, 62, 25, 45, 68, 74, 69, 0, false, false, false },
        new object[] { "phase-peace", 49, 62, 25, 45, 68, 74, 69, 0, false, false, false },
        new object[] { "phase-same-owner", 49, 62, 25, 45, 68, 74, 69, 0, false, false, false },
        new object[] { "phase-score100", 49, 62, 25, 45, 68, 74, 69, 0, false, false, false },
        new object[] { "phase-score101", 49, 62, 25, 41, 80, 74, 69, 0, false, false, false },
        new object[] { "phase-stored-a", 49, 62, 25, 48, 66, 48, 66, 21, false, false, true },
        new object[] { "phase-stored-b", 49, 62, 25, 47, 67, 74, 69, 0, false, false, false },
        new object[] { "phase-target-construction", 49, 62, 25, 45, 68, 74, 69, 0, false, false, false },
        new object[] { "phase-trade", 49, 62, 25, 45, 68, 74, 69, 0, false, false, false },
        new object[] { "phase-war", 49, 62, 25, 48, 65, 48, 64, 23, false, true, false },
        new object[] { "phase-war-stronger", 49, 62, 25, 45, 68, 74, 69, 0, false, false, false },
        new object[] { "phase-war-weaker", 49, 62, 25, 48, 65, 48, 64, 23, false, true, false },
        new object[] { "port-protected", 186, 90, 25, 124, 105, 161, 88, 0, false, false, false },
        new object[] { "port-unprotected", 185, 89, 25, 188, 92, 187, 91, 23, false, true, false },
        new object[] { "rough-ai-one", 49, 62, 1, 49, 63, 49, 62, 0, false, false, false },
        new object[] { "rough-ai-three", 49, 62, 3, 49, 63, 49, 63, 0, false, false, false },
        new object[] { "stored-one", 49, 62, 1, 48, 66, 49, 63, 0, false, false, false },
        new object[] { "stored-zero", 49, 62, 0, 48, 66, 49, 62, 0, false, false, false },
        new object[] { "route-landing-cargo", 49, 62, 25, 47, 64, 74, 69, 0, true, false, false },
        new object[] { "route-landing-no-cargo", 49, 62, 25, 47, 64, 74, 69, 0, false, false, false },
        new object[] { "route-own-port", 49, 62, 25, 47, 63, 74, 69, 0, false, false, false },
        new object[] { "route-inland", 49, 62, 25, 41, 57, 74, 69, 0, false, false, false },
        new object[] { "route-x-minus2", 49, 62, 25, 45, 68, 74, 69, 0, false, false, false },
        new object[] { "route-x-minus1-y-kept", 49, 62, 25, 45, 68, 74, 69, 0, false, false, false },
        new object[] { "route-x-0-residue", 49, 62, 25, 0, 0, 74, 69, 0, false, false, false },
        new object[] { "route-zero-moves", 49, 62, 0, 48, 66, 49, 62, 0, false, false, false },
        new object[] { "adjacent-landing-cargo", 48, 64, 25, 47, 64, 48, 65, 0, true, false, false },
        new object[] { "adjacent-landing-no-cargo", 48, 64, 25, 47, 64, 48, 65, 18, false, false, false },
        new object[] { "adjacent-own-port", 48, 64, 25, 47, 63, 48, 64, 18, false, false, false },
        new object[] { "hunt-tie-a", 49, 62, 25, 52, 62, 51, 62, 23, false, true, false },
        new object[] { "hunt-tie-b", 49, 62, 25, 49, 65, 49, 64, 23, false, true, false },
        new object[] { "hunt-protected", 49, 62, 25, 45, 68, 74, 69, 0, false, false, false },
        new object[] { "hunt-unprotected-2", 49, 62, 25, 49, 65, 49, 64, 23, false, true, false },
        new object[] { "p3-no-progress-sea-target", 49, 62, 25, 49, 63, 49, 63, 24, false, false, false },
        new object[] { "p3-carried-across-coast", 49, 62, 25, 47, 64, 74, 69, 0, true, false, false },
    };

    [Theory]
    [MemberData(nameof(MovementControls))]
    public void Promoted_movement_matrix_matches_endpoints_or_precombat_boundary(string name,
        int ox, int oy, int moves, int tx, int ty, int ex, int ey, int left, bool cargo, bool naval, bool blocker)
    {
        var f = Setup(moves, cargo, new(ox, oy), port: name is "adjacent-own-port" or "route-own-port",
            rough: name.StartsWith("rough-", StringComparison.Ordinal));
        var state = f.State;
        if (naval || blocker)
        {
            var enemy = state.Fleets[0] with { Id = "enemy", Nation = "rome",
                X = naval ? tx : 48, Y = naval ? ty : 65, Ships = 30, CarriedArmyId = null };
            state = state with { Fleets = ValueList.From(state.Fleets.Append(enemy)),
                Relations = state.Relations.WithRelation("carthage", "rome", f.Rules.Diplomacy.StateCodes.War) };
        }
        var result = f.Dispatch.Dispatch(state, Move(tx, ty));
        Assert.True(result.IsAccepted, result.ToString());
        if (naval)
        {
            // Original execution stopped before combat. Check that exact boundary, then rely
            // on the existing kernel's own outcome/legality tests, not an invented parity claim.
            var contact = Assert.Single(f.Events.Events.OfType<OriginalFleetArrival>(),
                a => a.ArrivalKind == "naval-contact");
            Assert.Equal((ex, ey, left), (contact.FleetX, contact.FleetY, contact.Moves));
        }
        else
        {
            var fleet = result.State.FleetById(FleetId)!;
            Assert.Equal((ex, ey, left), (fleet.X, fleet.Y, fleet.Moves));
        }
    }
}
