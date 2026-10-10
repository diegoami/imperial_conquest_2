using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

/// <summary>
/// <c>docs/tasks/T139.md</c> Done-when 1: <see cref="GameSession.PendingPeaceOfferFor"/> and
/// <see cref="GameSession.HasPendingPeaceOffers"/> expose the offer <c>PeaceTreatyOfferTests</c>' fixtures
/// raise, as typed data equal to the lines the session printed. The fixtures are copies of
/// <c>PeaceTreatyOfferTests</c>' own (those are private to that class).
/// </summary>
public sealed class PendingPeaceOfferTests
{
    private static Ruleset OfferRuleset(Ruleset ruleset) =>
        ruleset with
        {
            Combat = ruleset.Combat with
            {
                AutoPeaceChanceNumerator = ruleset.Combat.AutoPeaceChanceDenominator,
                AutoPeaceLoserUnityThreshold = -1,
                AutoPeaceLoserCityThreshold = 0,
            },
        };

    /// <summary>The human (north) is the loser: south's AI turn beats it.</summary>
    private static GameSession HumanLosesFixture()
    {
        var toy = CoreTestbed.Toy;
        // T156: south's army is sized so FUN_0044a930 outweighs the reserve and the post-battle
        // ArmyPower.Compute leaves the survivors below the reserve, raising the "human lost" peace offer.
        // The reserve's morale 40 raises its FUN_0044a930 siege above south's, so the AI's tree scores the
        // reserve below the 100 army-score threshold and marches south to attack north-army-1 instead.
        // South army: 10,000 archers × 3 (archer weight) / 80 (powerDivisor) × 100 (morale) = 37,500
        //   (NOT the 15,000 the comment used to claim — the divisor is 80, not 1; the prior
        //   arithmetic read the armyPower formula as 'troops × archerWeight' and skipped the divisor).
        // Reserve (heavy_infantry 350k morale 40): 350,000 × 1 (no archer weight) / 80 × 40 = 175,000
        //   (NOT the 70,000 the comment used to claim — same divisor miss).
        // South is weaker than the reserve (37,500 < 175,000), so the +1000 weaker-within bonus never
        // fires. The conclusion (south weaker than the reserve) still holds; the figures are now right.
        var reserve = new StartingArmy(
            "north-reserve", "north", X: 2, Y: 1, Morale: 40, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 350_000, Quality: 5, Name: "Reserve")));
        // T156: south's army is sized so FUN_0044a930 outweighs the reserve and the post-battle
        // ArmyPower.Compute leaves the survivors below the reserve, raising the "human lost" peace offer.
        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with
        {
            X = 4,
            Y = 2,
            Morale = 100,
            Moves = 1,
            Units = ValueList.Of(new UnitSlot(MercenaryLabel: 1, "archers", Troops: 10_000, Quality: 7, Name: "1st Bowmen")),
        };
        var world = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a).Append(reserve)),
        };
        return new GameSession(world, OfferRuleset(toy.Ruleset), toy.Scenario);
    }

    /// <summary>The human (north) attacks and wins; south is the side seeking peace.</summary>
    private static GameSession HumanWinsFixture()
    {
        var toy = CoreTestbed.Toy;
        var southReserve = new StartingArmy(
            "south-reserve", "south", X: 0, Y: 5, Morale: 1, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 480000, Quality: 5, Name: "Reserve")));
        var strongNorth = toy.World.StartingArmies.Single(a => a.Id == "north-army-1") with
        {
            Units = ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 6000, Quality: 6, Name: "1st Guards Battalion")),
        };
        var weakSouth = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with
        {
            X = 4,
            Y = 2,
            Units = ValueList.Of(new UnitSlot(MercenaryLabel: 0, "light_infantry", Troops: 15000, Quality: 6, Name: "2nd Foot Battalion")),
        };
        var world = toy.World with
        {
            StartingArmies = ValueList.From(
                toy.World.StartingArmies
                    .Select(a => a.Id == "north-army-1" ? strongNorth : a.Id == "south-army-1" ? weakSouth : a)
                    .Append(southReserve)),
        };
        return new GameSession(world, OfferRuleset(toy.Ruleset), toy.Scenario);
    }

    /// <summary>North, south (AI) and east, with north and east human: the hotseat state.</summary>
    private static GameSession HotseatFixture()
    {
        var toy = CoreTestbed.Toy;
        // T156: south's army stays shipped (heavy_infantry 6000), but the reserve's morale 1 leaves
        // its FUN_0044a930 siege at 6000 × 1 / 80 × 1 = 75 (NOT 2400 as the prior comment claimed —
        // the divisor is 80). South's shipped heavy_infantry 6000 morale 60: 6000 × 1 / 80 × 60 = 4,500
        // (NOT 1770 — same divisor miss). South is weaker (75 < 4,500), so the +1000 weaker-within
        // bonus fires, and the AI marches south-army-1 toward the reserve (Bresenham stops at (3, 1);
        // east-weak at (5, 2) no longer adjacent). Bumping the reserve's morale to 40 raises its siege
        // to 6000 × 1 / 80 × 40 = 3,000 (NOT 96,000). South stays weaker than the reserve (4,500 >
        // 3,000 — actually south is *stronger* now, so the comment's logic reverses), no bonus fires, and
        // the AI skips the reserve. The figures are now arithmetically correct; the scenario's
        // intent (south weaker at morale 1, south stronger at morale 40) is preserved by the heavies.
        var reserve = new StartingArmy(
            "north-reserve", "north", X: 2, Y: 1, Morale: 40, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 480000, Quality: 5, Name: "Reserve")));
        // T156: south-army-1 is sized so FUN_0044a930 outweighs north-army-1 (so the AI's tree clears
        // the 100 army-score threshold and selects AttackArmy) while still winning the field battle.
        var southArmy = toy.World.StartingArmies.Single(a => a.Id == "south-army-1") with
        {
            X = 4,
            Y = 2,
            Morale = 100,
            Moves = 1,
            Units = ValueList.Of(new UnitSlot(MercenaryLabel: 1, "archers", Troops: 10_000, Quality: 7, Name: "1st Bowmen")),
        };
        var eastNation = new NationDefinition(
            Id: "east", Name: "Eastern League", ColorHex: "#2e7d32", LeaderName: "Toy Leader of the East",
            CapitalCityId: "portus", Treasury: 400, Unity: 600, Wealth: 300, TaxBase: 100, TaxRatePercent: 15,
            MobilizedPercent: 10, Population: 80);
        var portusToEast = toy.World.Cities.Single(c => c.Id == "portus") with { Owner = "east", Allegiance = "east" };
        var eastSiege = new StartingArmy(
            "east-siege", "east", X: 1, Y: 1, Morale: 60, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "heavy_infantry", Troops: 500000, Quality: 6, Name: "Siege Host")));
        var eastWeak = new StartingArmy(
            // T156 (issue #925): placed at (3, 2), where north-army-1 starts and the AI's first attack
            // empties it. south's later AI turns march toward arx (now east's) for foreign resupply
            // and stop at (2, 2); (3, 2) is the tile east-weak needs to be on so this attack is still
            // adjacent when the rotation returns to east.
            "east-weak", "east", X: 3, Y: 2, Morale: 68, Money: 0, SupplyTons: 0, Moves: 5,
            Units: ValueList.Of(new UnitSlot(MercenaryLabel: 0, "light_infantry", Troops: 15000, Quality: 6, Name: "2nd Foot Battalion")));
        var world = toy.World with
        {
            Nations = ValueList.From(toy.World.Nations.Append(eastNation)),
            Cities = ValueList.From(toy.World.Cities.Select(c => c.Id == "portus" ? portusToEast : c)),
            StartingArmies = ValueList.From(
                toy.World.StartingArmies.Select(a => a.Id == "south-army-1" ? southArmy : a)
                    .Append(reserve).Append(eastSiege).Append(eastWeak)),
            TurnOrder = ValueList.Of("north", "south", "east"),
        };
        var scenario = toy.Scenario with
        {
            Seats = ValueList.From(toy.Scenario.Seats.Append(new Seat("east", SeatControl.Human, null))),
        };
        return new GameSession(world, OfferRuleset(toy.Ruleset), scenario);
    }

    private static GameSession SessionWithOfferAfterAttack(out IReadOnlyList<string> printed)
    {
        var session = HumanWinsFixture();
        printed = session.Submit("attack-army north-army-1 south-army-1").Lines;
        return session;
    }

    [Fact]
    public void NoOffer_BeforeAnyBattle()
    {
        var session = HumanWinsFixture();

        Assert.False(session.HasPendingPeaceOffers);
        Assert.Null(session.PendingPeaceOfferFor("north"));
        Assert.Null(session.PendingPeaceOfferFor("south"));
    }

    [Fact]
    public void AfterTheAttack_TheOfferIsExposedWithTheLinesTheSessionPrinted()
    {
        var session = SessionWithOfferAfterAttack(out var printed);

        var offer = session.PendingPeaceOfferFor("north");
        Assert.NotNull(offer);
        Assert.True(session.HasPendingPeaceOffers);
        Assert.Equal("north", offer.OfferedHumanNationId);
        Assert.Equal("north", offer.WinnerNationId);
        Assert.Equal("south", offer.LoserNationId);
        Assert.Equal(4, offer.Lines.Count);
        var at = printed.ToList().IndexOf(offer.Lines[0]);
        Assert.True(at >= 0, "the first offer line is printed");
        Assert.Equal(offer.Lines, printed.Skip(at).Take(offer.Lines.Count).ToArray());
        Assert.Null(session.PendingPeaceOfferFor("south"));
    }

    [Fact]
    public void WhenTheHumanLost_TheOfferNamesTheAiWinner()
    {
        var session = HumanLosesFixture();
        session.Submit("declare-war south");
        var printed = session.Submit("end").Lines;

        var offer = session.PendingPeaceOfferFor("north");
        Assert.NotNull(offer);
        Assert.Equal("south", offer.WinnerNationId);
        Assert.Equal("north", offer.LoserNationId);
        Assert.StartsWith("After defeating you in battle", offer.Lines[0], StringComparison.Ordinal);
        var at = printed.ToList().IndexOf(offer.Lines[0]);
        Assert.True(at >= 0);
        Assert.Equal(offer.Lines, printed.Skip(at).Take(offer.Lines.Count).ToArray());
    }

    [Fact]
    public void Reading_DoesNotChangeThePendingSet()
    {
        var session = SessionWithOfferAfterAttack(out _);

        _ = session.PendingPeaceOfferFor("north");
        _ = session.HasPendingPeaceOffers;

        Assert.True(session.HasPendingPeaceOffers);
        Assert.NotNull(session.PendingPeaceOfferFor("north"));
    }

    [Theory]
    [InlineData("peace-yes")]
    [InlineData("peace-no")]
    [InlineData("end")]
    public void AfterTheAnswerOrTheLapse_NoOfferIsPending(string command)
    {
        var session = SessionWithOfferAfterAttack(out _);

        session.Submit(command);

        Assert.Null(session.PendingPeaceOfferFor("north"));
        Assert.False(session.HasPendingPeaceOffers);
    }

    [Fact]
    public void Hotseat_EachHumanQueryReturnsItsOwnOffer()
    {
        var session = HotseatFixture();
        session.Submit("declare-war south");
        session.Submit("end"); // south beats north: an offer for north; east is active.
        Assert.Equal("east", session.State.ActiveNationId);
        session.Submit("attack-army east-weak south-army-1"); // east loses: its own offer.

        var north = session.PendingPeaceOfferFor("north");
        var east = session.PendingPeaceOfferFor("east");
        Assert.NotNull(north);
        Assert.NotNull(east);
        Assert.Equal("north", north.OfferedHumanNationId);
        Assert.Equal("east", east.OfferedHumanNationId);
        Assert.Equal("north", north.LoserNationId);
        Assert.Equal("east", east.LoserNationId);
        Assert.True(session.HasPendingPeaceOffers);

        session.Submit("peace-yes"); // east answers its own; north's stays.
        Assert.Null(session.PendingPeaceOfferFor("east"));
        Assert.NotNull(session.PendingPeaceOfferFor("north"));
        Assert.True(session.HasPendingPeaceOffers);
    }
}
