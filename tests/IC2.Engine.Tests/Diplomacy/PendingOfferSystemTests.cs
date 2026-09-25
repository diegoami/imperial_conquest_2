using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 10: the pending offer is state, cleared and re-rolled at every
/// human turn start, accepting waives the 3-partner cap, and the announcement is a presentation event
/// with no news line.
/// </summary>
public sealed class PendingOfferSystemTests
{
    private const string Human = "human-nation";
    private const string CandidateAi = "candidate-ai";
    private const string OtherAi = "other-ai";

    private static World ToyWorld => DiplomacyTestbed.Toy.World;

    private static GameState HumanTurnState(bool candidateEliminated = false) =>
        DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human),
            DiplomacyTestbed.Nation(CandidateAi, "CandidateAi", eliminated: candidateEliminated),
            DiplomacyTestbed.Nation(OtherAi, "OtherAi"));

    /// <summary>
    /// T82 (#359, bug #357): report §1b's trade condition needs a tax-base gap the plain
    /// <see cref="HumanTurnState"/> (every nation at its taxBase default, 0) can never clear --
    /// <c>candidate.TaxBase &gt; floor</c> is <c>0 &gt; 0</c>, always false. This variant gives
    /// <see cref="CandidateAi"/> a real taxBase and a fourth nation it already trades with, poorer than
    /// <see cref="Human"/>, satisfying both trade-condition clauses (report §1b: <c>taxBase[r] &gt; floor</c>
    /// and <c>r has a partner k with taxBase[k] &lt; taxBase[h]</c>) without touching
    /// <see cref="NeighbourGeography"/> at all -- none of these ids are in <see cref="ToyWorld"/>'s own
    /// nation list, so the alliance override never fires here regardless of the roll.
    /// </summary>
    private static GameState TradeEligibleHumanTurnState()
    {
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human, taxBase: 500),
            DiplomacyTestbed.Nation(CandidateAi, "CandidateAi", taxBase: 900),
            DiplomacyTestbed.Nation(OtherAi, "OtherAi"),
            DiplomacyTestbed.Nation("poorer-partner", "PoorerPartner", taxBase: 100));

        return state with
        {
            Relations = state.Relations.WithRelation(
                CandidateAi, "poorer-partner", DiplomacyTestbed.Ruleset.Diplomacy.StateCodes.Trade),
        };
    }

    /// <summary>An existing pending offer is cleared at the human seat's turn start, whatever it was.</summary>
    [Fact]
    public void DoD10_AnExistingOffer_IsClearedAtHumanTurnStart()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = HumanTurnState() with
        {
            PendingOffer = new PendingDiplomaticOffer(OtherAi, ruleset.Diplomacy.StateCodes.Trade),
        };

        var (after, _) = PendingOfferSystem.Apply(state, ruleset, ToyWorld, DiplomacyTestbed.Rng());

        // Either cleared outright, or replaced by a freshly rolled one -- never the SAME stale offer this
        // run started with (the whole point of "re-rolled", not "kept").
        if (after.PendingOffer is not null)
        {
            Assert.NotEqual(state.PendingOffer, after.PendingOffer);
        }
    }

    /// <summary>An AI seat's turn touches the pending offer not at all.</summary>
    [Fact]
    public void DoD10_AnAiSeatsTurn_LeavesThePendingOfferUntouched()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var existing = new PendingDiplomaticOffer(Human, ruleset.Diplomacy.StateCodes.Trade);
        var state = HumanTurnState() with
        {
            ActiveSeatIndex = 1, // CandidateAi's turn, not the human's
            PendingOffer = existing,
        };

        var (after, announcement) = PendingOfferSystem.Apply(state, ruleset, ToyWorld, DiplomacyTestbed.Rng());

        Assert.Equal(existing, after.PendingOffer);
        Assert.Null(announcement);
    }

    /// <summary>
    /// A candidate on a cooldown, or already eliminated, never becomes an offer, over many seeds (the
    /// relation/alive gates are checked before the chance roll, per the source's own short-circuit order).
    /// </summary>
    [Fact]
    public void DoD10_AnEliminatedCandidate_NeverBecomesAnOffer()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = HumanTurnState(candidateEliminated: true);

        for (ulong seed = 1; seed < 500; seed++)
        {
            var (after, _) = PendingOfferSystem.Apply(state, ruleset, ToyWorld, new SplitMix64Rng(seed));
            if (after.PendingOffer is { } offer)
            {
                Assert.NotEqual(CandidateAi, offer.ProposingNationId);
            }
        }
    }

    /// <summary>
    /// A candidate on a cooldown never becomes an offer (the confirmed gate: <c>relation[human][r] == 0</c>).
    /// </summary>
    [Fact]
    public void DoD10_ACandidateOnACooldown_NeverBecomesAnOffer()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = HumanTurnState();
        state = state with { Relations = state.Relations.WithRelation(Human, CandidateAi, -4) };

        for (ulong seed = 1; seed < 500; seed++)
        {
            var (after, _) = PendingOfferSystem.Apply(state, ruleset, ToyWorld, new SplitMix64Rng(seed));
            if (after.PendingOffer is { } offer)
            {
                Assert.NotEqual(CandidateAi, offer.ProposingNationId);
            }
        }
    }

    /// <summary>
    /// When every gate passes, the offer is the confirmed relation code for trade, and its dialog text is
    /// the exact period-terminated literal from Q5, not the corpus's own paraphrase.
    /// </summary>
    [Fact]
    public void DoD10_WhenRolled_TheOfferIsTrade_WithTheExactDialogText()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = TradeEligibleHumanTurnState();

        PendingDiplomaticOfferAnnounced? found = null;
        for (ulong seed = 1; seed < 2000 && found is null; seed++)
        {
            var (_, announcement) = PendingOfferSystem.Apply(state, ruleset, ToyWorld, new SplitMix64Rng(seed));
            found = announcement;
        }

        Assert.NotNull(found);
        Assert.Equal(ruleset.Diplomacy.StateCodes.Trade, found!.ProposedRelationCode);
        Assert.Equal($"{found.ProposingNationName} wants to trade with {found.TargetNationName}.", found.DialogText);
    }

    /// <summary>
    /// T82 (#359, bug #357) Done-when 1: "the relation is never written" -- a rolled offer, trade or
    /// alliance, never touches <see cref="DiplomaticRelations"/>. Only <see cref="AcceptPendingOfferCommand"/>,
    /// a separate, human-issued step, can do that.
    /// </summary>
    [Fact]
    public void DoD1_ARolledOffer_NeverWritesTheRelation()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = TradeEligibleHumanTurnState();

        var foundAny = false;
        for (ulong seed = 1; seed < 2000; seed++)
        {
            var (after, announcement) = PendingOfferSystem.Apply(state, ruleset, ToyWorld, new SplitMix64Rng(seed));
            if (announcement is null)
            {
                continue;
            }

            foundAny = true;
            Assert.Equal(
                ruleset.Diplomacy.StateCodes.Peace, after.Relations.Get(Human, announcement.ProposingNationId));
        }

        Assert.True(foundAny, "expected at least one offer across 2000 seeds");
    }

    /// <summary>
    /// T82 (#359, bug #357) Done-when 1: "it clears at the next human turn start" -- a rolled offer is
    /// gone (or replaced by a freshly rolled one, never the same value) the next time this human's turn
    /// begins, whether or not the human did anything with it.
    /// </summary>
    [Fact]
    public void DoD1_ARolledOffer_ClearsAtTheNextHumanTurnStart()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = TradeEligibleHumanTurnState();

        (GameState State, PendingDiplomaticOfferAnnounced Announcement)? rolled = null;
        for (ulong seed = 1; seed < 2000 && rolled is null; seed++)
        {
            var (after, announcement) = PendingOfferSystem.Apply(state, ruleset, ToyWorld, new SplitMix64Rng(seed));
            if (announcement is not null)
            {
                rolled = (after, announcement);
            }
        }

        Assert.NotNull(rolled);
        var offered = rolled!.Value.State;
        Assert.NotNull(offered.PendingOffer);

        // The next human turn start: re-roll with an unrelated seed. Whatever it produces, it is never
        // the same offer the first roll left behind.
        var (clearedOrReplaced, _) = PendingOfferSystem.Apply(offered, ruleset, ToyWorld, new SplitMix64Rng(999_999));

        Assert.NotEqual(offered.PendingOffer, clearedOrReplaced.PendingOffer);
    }

    /// <summary>
    /// T82 (#359, bug #357) Done-when 1: the alliance condition, report §1b -- "r in neighbours(h) and not
    /// atWar(h)" -- overrides a trade the same roll would otherwise have set. Needs a real
    /// <see cref="NeighbourGeography"/> pair, so this uses its own small world rather than
    /// <see cref="ToyWorld"/> (whose "north"/"south" ids do not match these nations at all).
    /// </summary>
    [Fact]
    public void DoD1_AllianceOverridesTrade_WhenNeighbouringAndNotAtWar()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var world = NeighbourWorld("human-nation", "candidate-ai");

        // Trade-eligible too (taxBase gap + a poorer partner), so a hit that did NOT apply the alliance
        // override would still show up as a (wrong) trade offer, not silence -- proving the override
        // actually fired, not merely that the trade path never did.
        var state = TradeEligibleHumanTurnState();

        PendingDiplomaticOfferAnnounced? found = null;
        for (ulong seed = 1; seed < 2000 && found is null; seed++)
        {
            var (_, announcement) = PendingOfferSystem.Apply(state, ruleset, world, new SplitMix64Rng(seed));
            found = announcement;
        }

        Assert.NotNull(found);
        Assert.Equal(ruleset.Diplomacy.StateCodes.Alliance, found!.ProposedRelationCode);
        Assert.Equal(
            $"{found.ProposingNationName} wants to form an alliance with {found.TargetNationName}.",
            found.DialogText);
    }

    /// <summary>
    /// The mirrored case: neighbouring, but the human is at war with a third nation -- report §1b's own
    /// "not atWar(h)" clause -- so the alliance override does not apply, and a trade-eligible candidate
    /// falls back to a trade offer instead of silence.
    /// </summary>
    [Fact]
    public void DoD1_NoAllianceOverride_WhenTheHumanIsAtWarWithAnyone()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var world = NeighbourWorld("human-nation", "candidate-ai");
        var state = TradeEligibleHumanTurnState();
        state = state with
        {
            Relations = state.Relations.WithRelation(Human, OtherAi, ruleset.Diplomacy.StateCodes.War),
        };

        PendingDiplomaticOfferAnnounced? found = null;
        for (ulong seed = 1; seed < 2000 && found is null; seed++)
        {
            var (_, announcement) = PendingOfferSystem.Apply(state, ruleset, world, new SplitMix64Rng(seed));
            found = announcement;
        }

        Assert.NotNull(found);
        Assert.Equal(ruleset.Diplomacy.StateCodes.Trade, found!.ProposedRelationCode);
    }

    /// <summary>
    /// Report §1b's trade condition, first clause: <c>taxBase[r] &gt; floor</c>. A candidate no richer
    /// than the human's own weakest current partner never becomes a trade offer -- checked with the human
    /// already at its 3-partner cap, so <c>floor</c> is that weakest partner's taxBase rather than 0.
    /// </summary>
    [Fact]
    public void DoD1_NoTradeOffer_WhenTheCandidateIsNotRicherThanTheFloor()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human, taxBase: 500),
            DiplomacyTestbed.Nation(CandidateAi, "CandidateAi", taxBase: 200), // at the floor, not above it
            DiplomacyTestbed.Nation("p1", "P1", taxBase: 200),
            DiplomacyTestbed.Nation("p2", "P2", taxBase: 300),
            DiplomacyTestbed.Nation("p3", "P3", taxBase: 400),
            DiplomacyTestbed.Nation("poorer-partner", "PoorerPartner", taxBase: 50));

        state = state with
        {
            Relations = state.Relations
                .WithRelation(Human, "p1", codes.Trade)
                .WithRelation(Human, "p2", codes.Trade)
                .WithRelation(Human, "p3", codes.Trade) // human at its 3-partner cap; floor = min(200,300,400) = 200
                .WithRelation(CandidateAi, "poorer-partner", codes.Trade), // has a partner poorer than the human
        };

        for (ulong seed = 1; seed < 2000; seed++)
        {
            var (after, _) = PendingOfferSystem.Apply(state, ruleset, ToyWorld, new SplitMix64Rng(seed));
            if (after.PendingOffer is { } offer)
            {
                Assert.NotEqual(CandidateAi, offer.ProposingNationId);
            }
        }
    }

    /// <summary>
    /// Report §1b's trade condition, second clause: <c>r has a partner k with taxBase[k] &lt; taxBase[h]</c>.
    /// A candidate richer than the floor but with no partner poorer than the human never becomes a trade
    /// offer either.
    /// </summary>
    [Fact]
    public void DoD1_NoTradeOffer_WhenTheCandidateHasNoPartnerPoorerThanTheHuman()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human, taxBase: 500),
            DiplomacyTestbed.Nation(CandidateAi, "CandidateAi", taxBase: 900),
            DiplomacyTestbed.Nation("richer-partner", "RicherPartner", taxBase: 950));

        state = state with
        {
            // CandidateAi's only partner is richer than the human, not poorer.
            Relations = state.Relations.WithRelation(
                CandidateAi, "richer-partner", ruleset.Diplomacy.StateCodes.Trade),
        };

        for (ulong seed = 1; seed < 2000; seed++)
        {
            var (after, _) = PendingOfferSystem.Apply(state, ruleset, ToyWorld, new SplitMix64Rng(seed));
            if (after.PendingOffer is { } offer)
            {
                Assert.NotEqual(CandidateAi, offer.ProposingNationId);
            }
        }
    }

    /// <summary>A small world giving exactly two nations a real, wide (14-tile) shared border.</summary>
    private static World NeighbourWorld(string a, string b)
    {
        const int Width = 20;
        const int Height = 14;
        var ids = new[] { a, b };
        var nations = ids
            .Select(id => new NationDefinition(id, id, "#000000", id, id, 0, 500, 10000, 1000, 10, 0, 100))
            .ToArray();
        var cities = ids
            .Select((id, band) => new CityDefinition(
                id, id, (band * (Width / 2)) + (Width / 4), Height / 2, id, id,
                80, 0, 50, 10, 10, 0, ValueList<UnitSlot>.Empty))
            .ToArray();

        return new World(
            GameDataSchema.CurrentVersion,
            "pending-offer-neighbour-world",
            "PendingOfferSystem neighbour test world",
            Width,
            Height,
            new TerrainGrid(TerrainEncoding.RunLength, Runs: ValueList<TerrainRun>.Of(new TerrainRun(2, Width * Height))),
            ValueList<TileType>.Of(new TileType("plain", 2, "Plain", true, false)),
            ValueList<NationDefinition>.Of(nations),
            ValueList<CityDefinition>.Of(cities),
            ValueList<StartingArmy>.Empty,
            ValueList<StartingFleet>.Empty,
            ValueList<string>.Of(ids));
    }

    /// <summary>DoD 10: the offer is a presentation event, never a news line.</summary>
    [Fact]
    public void DoD10_TheAnnouncement_WritesNoNewsLine()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = TradeEligibleHumanTurnState();

        GameState? after = null;
        for (ulong seed = 1; seed < 2000; seed++)
        {
            var (result, announcement) = PendingOfferSystem.Apply(state, ruleset, ToyWorld, new SplitMix64Rng(seed));
            if (announcement is not null)
            {
                after = result;
                break;
            }
        }

        Assert.NotNull(after);
        Assert.Empty(after!.NewsLog.Slots);
        Assert.Equal(-1, after.NewsLog.MostRecentSlot);
    }

    /// <summary>DoD 10: accepting a trade offer waives the 3-partner cap.</summary>
    [Fact]
    public void DoD10_AcceptingATradeOffer_WaivesTheThreePartnerCap()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var codes = ruleset.Diplomacy.StateCodes;

        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Human, "Human", control: SeatControl.Human),
            DiplomacyTestbed.Nation("p1", "P1", taxBase: 100),
            DiplomacyTestbed.Nation("p2", "P2", taxBase: 200),
            DiplomacyTestbed.Nation("p3", "P3", taxBase: 300),
            DiplomacyTestbed.Nation(CandidateAi, "CandidateAi", taxBase: 50));

        var relations = state.Relations
            .WithRelation(Human, "p1", codes.Trade)
            .WithRelation(Human, "p2", codes.Trade)
            .WithRelation(Human, "p3", codes.Trade);
        state = state with
        {
            Relations = relations,
            PendingOffer = new PendingDiplomaticOffer(CandidateAi, codes.Trade),
        };

        var dispatcher = DiplomacyTestbed.Dispatcher();
        var result = dispatcher.Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsAccepted);
        Assert.Equal(codes.Trade, result.State.Relations.Get(Human, CandidateAi));

        // The waiver means none of the three existing partners was dropped, even though this is a fourth.
        Assert.Equal(codes.Trade, result.State.Relations.Get(Human, "p1"));
        Assert.Equal(codes.Trade, result.State.Relations.Get(Human, "p2"));
        Assert.Equal(codes.Trade, result.State.Relations.Get(Human, "p3"));
    }

    /// <summary>DoD 10: accepting does NOT clear the pending offer -- only the next human turn start does.</summary>
    [Fact]
    public void DoD10_Accepting_DoesNotClearThePendingOffer()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = HumanTurnState() with
        {
            PendingOffer = new PendingDiplomaticOffer(CandidateAi, ruleset.Diplomacy.StateCodes.Trade),
        };

        var dispatcher = DiplomacyTestbed.Dispatcher();
        var result = dispatcher.Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.State.PendingOffer);
        Assert.Equal(CandidateAi, result.State.PendingOffer!.ProposingNationId);
    }

    [Fact]
    public void DoD10_AcceptingWithNoPendingOffer_IsRejected()
    {
        var state = HumanTurnState();
        var dispatcher = DiplomacyTestbed.Dispatcher();

        var result = dispatcher.Dispatch(state, new AcceptPendingOfferCommand(Human));

        Assert.True(result.IsRejected);
        Assert.Equal(AcceptPendingOfferRejections.NoPendingOffer, result.Code);
    }
}
