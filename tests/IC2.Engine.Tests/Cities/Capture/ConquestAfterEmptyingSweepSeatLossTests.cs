using System.Linq;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// T92 rework round 1, B2 (DoD 2): T83's own lost-seat fallback (<c>GameSession.AnnounceAndAdoptWatchModeIfSeatIsLost</c>)
/// for the exact "capture whose own sweep empties the loser" case <see cref="ConquestAfterEmptyingSweepTests"/>
/// drives directly through <c>CityCaptureResolver.Capture</c>. This file drives the SAME shape of
/// elimination through the real <c>GameSession</c>/AI pipeline instead: "south" is the designated
/// <c>--seat</c> nation, an AI "north" empties it in one turn (a direct siege plus the same call's own
/// regular defection cascade), and <c>HandleEnd</c>'s own end-of-round check announces the fall and
/// adopts watch mode -- once, from reading final state, not from how many times the underlying
/// elimination-adjacent code paths ran this turn (<see cref="GameSession"/>'s own remarks on
/// <c>_seatLost</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why the attacker is AI, and why war starts already declared.</strong> Whenever
/// <c>humanSeatNationId</c> is set, every other seat becomes AI (<c>GameSession</c>'s own constructor) --
/// there is no way to drive "north"'s own attack through a manual command while "south" is the designated
/// seat, since a manual command only ever acts for the state's own <c>ActiveNationId</c>
/// (<c>HandleBesiegeCity</c>: <c>new BesiegeCityCommand(State.ActiveNationId, ...)</c>), and once a human
/// seat is designated, GameSession auto-plays every earlier AI seat's own turn as a "prelude" before the
/// human's first command (<c>AdvanceToHumanSeat</c>) -- so "north" can never be manually driven once
/// "south" is designated. "south" is placed first in <see cref="World.TurnOrder"/> so no prelude runs at
/// all, and the world's own <see cref="World.StartingRelations"/> already has north and south at war, so
/// <c>AiMilitaryPhase.ProposeOwnWarDeclaration</c>'s own <c>Random(10)</c> roll never has to hit -- the
/// siege decision itself (<c>AiMilitaryPhase.ProposeSieges</c>) has no random draw of its own: given one
/// army, one adjacent already-at-war target, and a ratio the overwhelming army clears by a wide margin,
/// it is the only military candidate on offer, so no seed search is needed (contrast
/// <c>GameSessionCommandsTests.HandleEnd_prints_the_dash_wrapped_elimination_from_an_ai_seats_own_turn</c>,
/// which needs one because it exercises the war-declaration roll this fixture deliberately bypasses).
/// </para>
/// <para>
/// <strong>The scenario.</strong> "south" starts owning exactly two cities, neither one anybody's capital
/// (<see cref="NationDefinition.CapitalCityId"/> is <see langword="null"/>, matching
/// <c>ConquestAfterEmptyingSweepTests.BuildScenario</c>'s own non-capital branch): "south-target" (the
/// besieged city) and "south-last" (weak, low-loyalty, within cascade range of the same army, so the SAME
/// <c>besiege-city</c> command's own regular cascade defects it too, exactly like the direct-call
/// scenario). The toy world's own "meridia" is reassigned to "north" rather than deleted, so nothing
/// dangles.
/// </para>
/// </remarks>
public sealed class ConquestAfterEmptyingSweepSeatLossTests
{
    private const string Loser = "south";
    private const string Winner = "north";

    [Fact]
    public void AiTurnThatEmptiesTheDesignatedSeatThroughItsOwnSweep_AnnouncesTheFallAndAdoptsWatchMode()
    {
        var toy = CoreTestbed.Toy;
        var codes = toy.Ruleset.Diplomacy.StateCodes;

        var world = toy.World with
        {
            Cities = ValueList.Of(
                toy.World.CityById("arx")!,
                toy.World.CityById("portus")!,
                // Repurposed to "north" -- "south" owns nothing but the two cities below, so it can
                // actually reach zero (Meridia would otherwise keep it alive regardless of what happens
                // to the two cities this scenario sweeps).
                toy.World.CityById("meridia")! with { Owner = Winner, Allegiance = Winner },
                // The toy world's own map is only 8x6 -- every coordinate below stays inside it.
                new CityDefinition(
                    "south-target", "South Target", X: 6, Y: 4, Owner: Loser, Allegiance: Loser,
                    Loyalty: 40, SupplyTons: 0, FortificationCode: 0, PopulationThousands: 10,
                    MaxPopulationThousands: 20, Tribute: 0, Garrison: ValueList<UnitSlot>.Empty),
                // Loyalty 30 (< CascadeLoyaltyThreshold, 65), weak, well within CascadeDistanceMax (10) of
                // the besieging army below -- the same "second city the same call's cascade sweeps too"
                // shape as ConquestAfterEmptyingSweepTests.BuildScenario's own "loser-last".
                new CityDefinition(
                    "south-last", "South Last", X: 7, Y: 4, Owner: Loser, Allegiance: Loser,
                    Loyalty: 30, SupplyTons: 0, FortificationCode: 0, PopulationThousands: 1,
                    MaxPopulationThousands: 10, Tribute: 0, Garrison: ValueList<UnitSlot>.Empty)),
            Nations = ValueList.Of(
                toy.World.NationById(Winner)!,
                // Unity 660: after the siege's own -15 (645), strictly under CascadeUnityThreshold (650),
                // so the cascade fires against "south-last" in the same call -- see
                // ConquestAfterEmptyingSweepTests.BuildScenario's own remark for the identical arithmetic.
                // CapitalCityId null: "south" no longer holds Meridia, and neither new city is a capital.
                toy.World.NationById(Loser)! with { CapitalCityId = null, Unity = 660 }),
            StartingArmies = ValueList.Of(
                new StartingArmy(
                    "north-overwhelming-army", Winner, X: 6, Y: 3, Morale: 60, Money: 0, SupplyTons: 0,
                    Moves: 1, Units: ValueList.Of(CaptureFixtures.Unit("archers", 400_000)))),
            StartingFleets = ValueList<StartingFleet>.Empty,
            // "south" first -- no AI prelude runs before its own (empty) first turn, so "north"'s own AI
            // turn (the one that empties "south") is reached through HandleEnd's own loop, exactly where
            // Done-when 2 asks the lost-seat fallback to be checked.
            TurnOrder = ValueList.Of(Loser, Winner),
            // Already at war: skips AiMilitaryPhase.ProposeOwnWarDeclaration's own Random(10) roll
            // entirely -- see this type's own remarks on why no seed search is needed.
            StartingRelations = DiplomaticRelations.Uniform(ValueList.Of(Loser, Winner), codes.Peace)
                .WithRelation(Loser, Winner, codes.War),
        };

        var scenario = toy.Scenario with
        {
            Seats = ValueList.Of(
                new Seat(Loser, SeatControl.Human),
                new Seat(
                    Winner, SeatControl.Ai,
                    new AiPersonality(Aggression: 0.8, ExpansionDrive: 0.5, LoyaltyToAlliances: 0.5))),
        };

        var session = new GameSession(world, toy.Ruleset, scenario, seedOverride: 1, humanSeatNationId: Loser);

        // "south" (human, designated) does nothing and ends its own turn -- every AI decision below
        // happens on "north"'s own turn, inside HandleEnd's own while loop.
        var output = session.Submit("end");

        Assert.True(session.State.NationById(Loser)!.Eliminated);
        Assert.Equal(Winner, session.State.NationById(Loser)!.ConqueredBy);
        Assert.Equal(0, session.State.CountCitiesOwnedBy(Loser));

        // T83's own lost-seat fallback: announced once, from the final state, regardless of the two
        // elimination-adjacent blocks (the cascade's own defection, then the conquest) this same AI turn
        // ran to get there -- Done-when 2's own closing clause.
        Assert.Contains(output.Lines, line => line.Contains("has fallen", System.StringComparison.Ordinal)
            && line.Contains("Watch mode from here on", System.StringComparison.Ordinal));

        // Watch mode is adopted permanently: a further "end" plays without any seat left to pause on --
        // it does not throw and does not re-announce the same fall a second time.
        var secondEnd = session.Submit("end");
        Assert.DoesNotContain(secondEnd.Lines, line => line.Contains("has fallen", System.StringComparison.Ordinal));
    }
}
