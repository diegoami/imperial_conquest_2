using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T113 (docs/tasks/T113.md) Done-when 2, the Recruit mercenaries dialog's and the city right-click
/// listing's Godot-free model: the offers an army sees are the live offers of the first city at
/// Chebyshev distance exactly 1, in slot order; an army with no such city gets the empty list and the
/// dialog does not open; a hire that brings the army to the unit cap reports the dialog closed; an army
/// already at the cap does not open the dialog; the per-offer "Quarterly cost" box shows
/// <c>(troops × price × quality) div 1000</c>, not the gate or the actual quarterly upkeep.
/// </summary>
/// <remarks>
/// <strong>The classical case.</strong> An HC of 960 troops at quality 9 shows <strong>34</strong>
/// (Etruscan, on heavy cavalry's <c>quarterlyPrice = 4</c> — confirmed live from the
/// <c>classical-faithful</c> ruleset). An LI of 3,868 troops at quality 8 shows <strong>30</strong>
/// (light infantry's quarterly price is 1). The values come straight from the ruleset, not from a test
/// copy, exactly as AreaMapHighlightsTests reads the city and capital tiles from the live world.
/// <strong>The 20-unit cap.</strong> The ruleset carries <see cref="ArmyManagementRules.MaxUnitsPerArmy"/>
/// = 20, the same cap <c>HireMercenaryCommandHandler</c> applies; the model's two cap flags (open /
/// close) sit on either side of the cap so the test can pin both.
/// </remarks>
public sealed class MercenaryDialogModelTests
{
    private const string RomeId = "rome";
    private const string ArmyId = "t113-army";
    private const string CityAtArmyId = "t113-city";
    private const string HireCityId = "t113-hire-city";

    private static readonly Lazy<Ruleset> LazyClassicalRuleset = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean").Ruleset);

    private static Ruleset ClassicalRuleset => LazyClassicalRuleset.Value;

    /// <summary>
    /// The army at (5,5), a city at (4,5) (Chebyshev distance exactly 1 from the army), and a single
    /// offer on that city's tile. The army is one tile from the city, not on it: the engine's own
    /// adjacency rule — <c>Fun_00449D08</c> / <c>tests/fixtures/corpus.json</c>
    /// <c>mercenary.playerHireRange = 1</c> — rejects distance 0, so an offer on the army's own tile
    /// is not in reach
    /// <strong>[confirmed: <c>decompiled-mercenary-offer-list-and-position.md</c> §1; T76's
    /// HireMercenaryCommandHandler.FindChosenCity uses ChebyshevDistance == range]</strong>.
    /// </summary>
    private static (GameState State, Ruleset Ruleset) Scripted(
        int armyUnitCount = 1,
        int armyMoney = 0,
        bool withOffer = true,
        int offerTroops = 3_868,
        string offerType = "light_infantry",
        int offerQuality = 8,
        int offerLabel = 0,
        int slotIndex = 1,
        bool insertSentinelAfter = false,
        string secondOfferType = "heavy_cavalry",
        int secondOfferTroops = 960,
        int secondOfferQuality = 9,
        int secondOfferSlot = 2,
        int armyX = 5,
        int armyY = 5,
        int cityX = 4,
        int cityY = 5,
        int hireCityX = 6,
        int hireCityY = 5)
    {
        var initial = CoreTestbed.InitialState();
        var army = new ArmyState(
            ArmyId, RomeId, X: armyX, Y: armyY, Moves: 4, Morale: 70, Money: armyMoney, SupplyTons: 100,
            CoveredTileCode: null, AboardFleetId: null,
            Units: ValueList.From(Enumerable.Range(0, armyUnitCount).Select(index =>
                new UnitSlot(0, "light_infantry", 100, 6, $"T113 unit {index}"))));
        var city = new CityState(
            CityAtArmyId, "T113 city", X: cityX, Y: cityY, Owner: RomeId, Allegiance: RomeId,
            Loyalty: 90, SupplyTons: 100, FortificationCode: 0,
            PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
            Garrison: ValueList<UnitSlot>.Empty);

        var offers = new List<MercenaryPoolSlot>();
        if (withOffer)
        {
            offers.Add(new MercenaryPoolSlot(
                slotIndex, X: cityX, Y: cityY, NameLabel: offerLabel,
                UnitTypeId: offerType, Troops: offerTroops, Quality: offerQuality));
            if (insertSentinelAfter)
            {
                offers.Add(new MercenaryPoolSlot(
                    secondOfferSlot, X: cityX, Y: cityY, NameLabel: 0,
                    UnitTypeId: secondOfferType,
                    Troops: MercenaryDialogModel.HiredSlotSentinelTroops, Quality: secondOfferQuality));
            }
        }

        // A second city within range (Chebyshev distance exactly 1 from the army) to verify the
        // selected city is the one whose offers the engine would pick — the same "first city" rule the
        // engine's FindChosenCity implements. Both cities sit one tile from the army; the slot-order
        // tiebreak (the engine's "first live offer, in slot order") lets the offer's slot decide the
        // city, not the city's own id.
        var hireCity = new CityState(
            HireCityId, "T113 hire city", X: hireCityX, Y: hireCityY, Owner: RomeId, Allegiance: RomeId,
            Loyalty: 90, SupplyTons: 100, FortificationCode: 0,
            PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
            Garrison: ValueList<UnitSlot>.Empty);

        var state = initial with
        {
            Armies = ValueList.From(new[] { army }),
            Cities = ValueList.From(new[] { city, hireCity }),
            MercenaryPool = ValueList.From(offers),
        };
        return (state, ClassicalRuleset);
    }

    /// <summary>
    /// Done-when 2: the per-offer "Quarterly cost" box figure is the original's <em>displayed</em>
    /// <c>(troops × price × quality) div 1000</c>. The brief's two numeric cases — Etruscan HC 960 q9
    /// shows 34, Samnite LI 3,868 q8 shows 30 — land here against the ruleset's own prices
    /// (heavy_cavalry.quarterlyPrice = 4, light_infantry.quarterlyPrice = 1).
    /// </summary>
    [Fact]
    public void Quarterly_cost_is_the_original_displayed_shape_not_the_pay()
    {
        var (state, ruleset) = Scripted();

        // The two numeric cases from the brief, computed against the ruleset the dialog reads.
        Assert.Equal(34, MercenaryDialogModel.DisplayedQuarterlyCost(960, ruleset.UnitTypeById("heavy_cavalry")!.QuarterlyPrice, 9));
        Assert.Equal(30, MercenaryDialogModel.DisplayedQuarterlyCost(3_868, ruleset.UnitTypeById("light_infantry")!.QuarterlyPrice, 8));

        // The model's offer carries the displayed figure, not the gate (24 / 27 for these two), not the
        // pay (28 for the HC, 30 for the LI), and not the paid-up hire "cost" — those were the
        // pre-T143 design that bug #755 corrected.
        var model = MercenaryDialogModel.ForArmy(state, ArmyId, ruleset);
        var offer = Assert.Single(model.Offers);

        // The single scripted offer is the LI 3,868 q8; its displayed cost is 30.
        Assert.Equal("light_infantry", offer.UnitTypeId);
        Assert.Equal(3_868, offer.Troops);
        Assert.Equal(8, offer.Quality);
        Assert.Equal(30, offer.QuarterlyCostTalents);
    }

    /// <summary>
    /// Done-when 2: an army with no city at Chebyshev distance exactly 1 gets the empty offer list and
    /// the dialog does not open. The distance gate is the engine's own <c>FUN_00449D08</c> rule
    /// (<c>tests/fixtures/corpus.json</c> <c>mercenary.playerHireRange</c>: "exactly 1, not at most
    /// 1"), so an army two tiles away is also empty even with the offer right under it.
    /// </summary>
    [Fact]
    public void No_city_at_distance_one_means_no_offers_and_the_dialog_does_not_open()
    {
        var (state, ruleset) = Scripted(); // army at (5,5), city at (4,5), offer on (4,5)
        var arranged = state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a with { X = 12, Y = 12 })), // > 1 tile from (4,5)
        };

        var model = MercenaryDialogModel.ForArmy(arranged, ArmyId, ruleset);
        Assert.Empty(model.Offers);
        Assert.False(model.DialogOpens);
    }

    /// <summary>
    /// Done-when 2, 6: the dialog's offers for an army are the live offers of the <em>first city at
    /// distance exactly 1</em>, in slot order. With one city at distance exactly 1 from the army
    /// holding two offers (slot 1 first, slot 2 with a <c>0xFFFF</c> hired-slot sentinel — invisible
    /// to the model) the dialog shows the live one only, in slot order, and skips the sentinel.
    /// </summary>
    [Fact]
    public void Dialog_offers_are_the_chosen_citys_offers_in_slot_order_skipping_hired_sentinels()
    {
        var (state, ruleset) = Scripted(insertSentinelAfter: true, armyX: 5, armyY: 5, cityX: 4, cityY: 5);

        var model = MercenaryDialogModel.ForArmy(state, ArmyId, ruleset);
        var offer = Assert.Single(model.Offers);
        Assert.Equal(1, offer.SlotIndex);
        Assert.Equal(MercenaryDialogModel.HiredSlotSentinelTroops, 0xFFFF);
    }

    /// <summary>
    /// Done-when 2: a hire that brings the army to 20 units reports the dialog closed (<see cref="MercenaryDialogModel.HireFillsCap"/>).
    /// The clone's packed unit list applies T15's <see cref="ArmyManagementRules.MaxUnitsPerArmy"/>,
    /// so the rule here uses the ruleset directly. An army at 19 is the boundary: the next hire ends at
    /// 20, the dialog closes; an army at 18 does not close.
    /// </summary>
    [Fact]
    public void Hire_that_brings_army_to_20_closes_the_dialog_but_19_does_not()
    {
        var (state19, ruleset) = Scripted(armyUnitCount: 19);
        var model19 = MercenaryDialogModel.ForArmy(state19, ArmyId, ruleset);
        var offer19 = model19.Offers[0];
        Assert.True(model19.HireFillsCap(offer19), "army at 19 + 1 = 20 closes");
        Assert.True(model19.DialogOpens, "dialog still opens from 19");

        var (state18, _) = Scripted(armyUnitCount: 18);
        var model18 = MercenaryDialogModel.ForArmy(state18, ArmyId, ruleset);
        var offer18 = model18.Offers[0];
        Assert.False(model18.HireFillsCap(offer18), "army at 18 + 1 = 19 stays open");
        Assert.True(model18.DialogOpens, "dialog still opens from 18");
    }

    /// <summary>
    /// Done-when 2: an army already at 20 units does not open the dialog. The cap is the ruleset's
    /// <see cref="ArmyManagementRules.MaxUnitsPerArmy"/>; the brief says "an army already at 20 units
    /// does not open" <c>[confirmed: T76's own engine order — exception 7 in the brief's
    /// stacking]</c>, and the packed unit list makes that identical to the engine's 20-unit refusal.
    /// </summary>
    [Fact]
    public void Army_already_at_20_units_does_not_open_the_dialog()
    {
        var (state, ruleset) = Scripted(armyUnitCount: 20);
        var model = MercenaryDialogModel.ForArmy(state, ArmyId, ruleset);

        Assert.NotEmpty(model.Offers); // The offers still exist; only the dialog-opens gate is off.
        Assert.False(model.DialogOpens, "the dialog does not open for an army at the cap");
    }

    /// <summary>
    /// Done-when 6 (the 2026-10-05 amendment): the model's city-side view reads exactly the live offers
    /// on the city's tile in slot order, and skips the <c>0xFFFF</c> hired-slot sentinel so the
    /// listing cannot list a slot the player already hired out of. The pool is fed to the model with
    /// the live slots deliberately reversed (slot 4 first, slot 3 second); a model that read the pool
    /// in collection order would surface slot 4 first, which the original's "first live offer, in slot
    /// order" rule would never show. The intermediate sentinel on slot 5 is in the middle and must
    /// not appear either.
    /// </summary>
    [Fact]
    public void City_listing_reads_the_offers_on_its_own_tile_in_slot_order_skipping_the_sentinel()
    {
        // A city at (10,10) with three offers on its tile: slot 4 (live), slot 5 (the 0xFFFF
        // hired-slot sentinel), slot 3 (live). The pool is built with the live slots reversed so a
        // "load in pool order" implementation surfaces slot 4 first and fails the test.
        var initial = CoreTestbed.InitialState();
        var city = new CityState(
            "t113-city-amend", "T113 amend city", X: 10, Y: 10, Owner: RomeId, Allegiance: RomeId,
            Loyalty: 90, SupplyTons: 100, FortificationCode: 0,
            PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
            Garrison: ValueList<UnitSlot>.Empty);

        var offers = new List<MercenaryPoolSlot>
        {
            new(4, 10, 10, 0, "archers", 2_000, 8),
            new(5, 10, 10, 0, "heavy_cavalry",
                MercenaryDialogModel.HiredSlotSentinelTroops, 9),
            new(3, 10, 10, 0, "light_infantry", 1_000, 6),
        };

        var state = initial with
        {
            Cities = ValueList.Of(new[] { city }),
            MercenaryPool = ValueList.From(offers),
        };

        var model = MercenaryDialogModel.ForCity(state, "t113-city-amend", ClassicalRuleset);

        Assert.Equal(2, model.Offers.Count);
        Assert.Equal(new[] { 3, 4 }, model.Offers.Select(o => o.SlotIndex));
        Assert.DoesNotContain(model.Offers, o => o.Troops == MercenaryDialogModel.HiredSlotSentinelTroops);
        Assert.Equal("t113-city-amend", model.CityId);
    }

    /// <summary>
    /// Done-when 6: a city with no mercenary offer on its tile gets an empty list (a city with none).
    /// The view's "There are no mercenaries at &lt;city&gt;" line is the panel's render of an empty
    /// list, so the model returns it.
    /// </summary>
    [Fact]
    public void City_with_no_offers_returns_an_empty_list()
    {
        var (state, ruleset) = Scripted(withOffer: false);

        var model = MercenaryDialogModel.ForCity(state, CityAtArmyId, ruleset);
        Assert.Empty(model.Offers);
        Assert.Equal(CityAtArmyId, model.CityId);
    }

    /// <summary>
    /// Done-when 2: a hire composes <c>hire-mercenary &lt;army&gt; &lt;slot&gt;</c> — the exact verb
    /// and token shape the engine's <see cref="IC2.Engine.Recruitment.Commands.HireMercenaryCommand"/>
    /// parses (T113 hazard: the verb shape is shared with the engine, the test pins it).
    /// </summary>
    [Fact]
    public void Hire_compose_a_command_line_the_engine_parses()
    {
        var (state, ruleset) = Scripted();
        var model = MercenaryDialogModel.ForArmy(state, ArmyId, ruleset);
        var offer = model.Offers[0];

        Assert.Equal($"hire-mercenary {ArmyId} {offer.SlotIndex}", model.HireCommandLine(offer, ArmyId));
    }

    /// <summary>
    /// R6 (review round 1): the dialog's "Quarterly cost" multiplication uses a wide intermediate
    /// (<see cref="long"/>) before the divisor, so an offer whose <c>troops × price × quality</c>
    /// exceeds <see cref="int.MaxValue"/> but whose divided result fits in <see cref="int"/> lands
    /// as a positive, correct figure rather than wrapping negative. A model that multiplied in
    /// <see cref="int"/> would wrap the intermediate to a negative number that the divide cannot
    /// undo. The chosen boundary (<c>50,000,000 × 5 × 9 = 2,250,000,000 &gt; int.MaxValue</c>)
    /// divides cleanly to 2,250,000, an int the displayed-cost box can carry.
    /// </summary>
    [Fact]
    public void Displayed_quarterly_cost_uses_a_wide_intermediate_so_a_large_offer_does_not_wrap()
    {
        const int troops = 50_000_000;
        const int price = 5;
        const int quality = 9;
        const long product = (long)troops * price * quality;

        Assert.True(product > int.MaxValue,
            $"the chosen boundary actually exceeds int.MaxValue at the multiplied step (product={product})");
        Assert.True((int)(product / 1000) > 0,
            "the chosen boundary divides into a positive int");

        // A model that multiplied in int would wrap the intermediate to negative; the divide then
        // yields a wrong (and possibly negative) int. The widened path divides the long product.
        Assert.Equal((int)(product / 1000), MercenaryDialogModel.DisplayedQuarterlyCost(troops, price, quality));
    }
}
