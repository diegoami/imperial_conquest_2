using IC2.Engine.Battle;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Presentation;
using IC2.Slice.Screens;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Battle.Tactical.Improved;

public sealed class CapAndRetreatTests
{
    private static Ruleset Preset(string name) => GameDataLoader.LoadFile<Ruleset>(Path.Combine(ModelTestPaths.DataRoot, "rulesets", name + ".json"));
    private static Ruleset Classical => Preset("classical-faithful");
    private static Ruleset Changed(Func<TacticalBattleRules, TacticalBattleRules> change)
    {
        var r = Classical;
        return r with { Combat = r.Combat with { Tactical = change(r.Combat.Tactical) } };
    }
    private sealed record Fixture(Ruleset Rules, World World, Scenario Scenario, GameState Game);
    private static Fixture Make(Ruleset r, UnitSlot[] north, UnitSlot[] south)
    {
        var armies = new[]
        {
            TacticalSessionTestbed.Army("north-army-1", "north", 3, 2, 68, north) with { Money = 100, SupplyTons = 500 },
            TacticalSessionTestbed.Army("south-army-1", "south", 4, 2, 59, south) with { Money = 40, SupplyTons = 60 },
        };
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, r, armies);
        return new(r, session.World, session.Scenario, session.State);
    }
    private static UnitSlot ArmyUnit(string type, int troops) => new(0, type, troops, 5, "unit");
    private static TacticalBattleState Bind(TacticalBattleState s) => s with
    {
        AttackerArmyId = "north-army-1", DefenderArmyId = "south-army-1",
        ComputerControlled = ValueList.Of(false, false), ComputerGeneral = ValueList.Of(false, false),
    };
    private static TacticalBattleSession Reopen(TacticalBattleState state, Fixture f)
    {
        var saved = new SavedTacticalBattle("north-army-1", "south-army-1", state.SideToMove, state.Placed, state.Counter,
            ValueList.From(state.Slots.Select(s => new SavedTacticalSlot(s.X,s.Y,s.Origin,s.Type,s.Troops,s.Quality,s.Morale,s.Moves,s.Shots,s.Target,s.Name,s.RoutedTroops))),
            state.Grid, state.ComputerControlled, state.ComputerGeneral, state.GeneralClaims, state.Selected, 1UL, true);
        return TacticalBattleSession.Reopen(f.Game, f.Rules, saved, exactRestore: true);
    }
    private static SaveGame Save(Fixture f, TacticalBattleSession battle) =>
        new(GameDataSchema.CurrentVersion, "test", "test", f.Scenario.Id, f.World.Id, f.Rules.Id, f.Game, Battle: battle.ToSaved());

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(100)]
    public void Only_halfRoundCap_ends_after_that_completed_half_round_including_placement(int cap)
    {
        var r = Changed(t => t with { HalfRoundCap = cap });
        var state = Bind(Arena((0, Unit(LI, 0, 0, 3000)), (D0, Unit(HI, 13, 11, 6000))))
            with { Counter = cap, Placed = cap > 2, SideToMove = cap == 1 ? 1 : 0 };
        var old = TacticalHalfRound.End(state, TacticalContext.From(Classical), NoDraws.Instance);
        var next = TacticalHalfRound.End(state, TacticalContext.From(r), NoDraws.Instance);
        Assert.False(old.IsOver);
        Assert.Equal(cap + 1, old.Counter);
        Assert.True(next.IsOver);
        Assert.True(next.Capped);
        Assert.Equal(cap, next.Counter);
        Assert.Equal(1, TacticalSurrender.Winner(next));
        Assert.Equal(new TacticalCapReachedEvent(cap), Assert.Single(next.Log.OfType<TacticalCapReachedEvent>()));
        Assert.Equal(state.Slots, next.Slots);
        Assert.Equal(state.Grid, next.Grid);
        Assert.DoesNotContain(next.Log, e => e is TacticalSetUpEvent);
        if (cap > 1)
        {
            var before = TacticalHalfRound.End(state with { Counter = cap - 1 }, TacticalContext.From(r), NoDraws.Instance);
            Assert.False(before.IsOver);
            Assert.Equal(cap, before.Counter);
        }
    }

    [Fact]
    public void Capped_withdrawal_keeps_purses_and_has_no_promotion_unity_transfer_or_peace_draw()
    {
        var f = Make(Preset("improved"), new[] { ArmyUnit("light_infantry",3000) }, new[] { ArmyUnit("heavy_infantry",6000) });
        var state = Bind(Arena((0, Unit(LI,0,0,3000)), (D0,Unit(HI,13,11,6000)))) with { Counter = 100 };
        var session = Reopen(state,f);
        Assert.True(session.EndHalfRound().Accepted);
        var draws = new Zeros();
        var result = session.Conclude(f.Game,f.Rules,draws,f.World);
        Assert.Null(result.Rejection);
        Assert.True(result.Report!.Capped);
        Assert.Equal("south-army-1",result.Report.WinnerArmyId);
        Assert.False(result.PeaceDrawn);
        Assert.False(result.PeaceOffered);
        Assert.Empty(result.Report.Promotions);
        Assert.Equal(0,result.Report.CapturedMoney);
        Assert.Equal(0,result.Report.CapturedSupplyTons);
        Assert.Equal(new[] { 3 },draws.Bounds);
        var after = result.State!;
        var attacker = after.ArmyById("north-army-1")!;
        var defender = after.ArmyById("south-army-1")!;
        Assert.Equal(2250,attacker.TotalTroops); // 5% + 10% * 6000/3000 = 25%.
        Assert.Equal(100,attacker.Money);
        Assert.Equal(22,attacker.SupplyTons);
        Assert.Equal(0,attacker.Moves);
        Assert.Equal(40,defender.Money);
        Assert.Equal(60,defender.SupplyTons);
        Assert.Equal(5,defender.Units[0].Quality);
        Assert.Equal(f.Game.ArmyById(defender.Id)!.Moves,defender.Moves);
        Assert.Equal(f.Game.Nations,after.Nations);
        Assert.Equal(f.Game.ArmyById(attacker.Id)!.Morale,attacker.Morale);
        Assert.Equal(f.Game.ArmyById(defender.Id)!.Morale,defender.Morale);
        Assert.Single(result.Events.OfType<ArmyScattered>());
        Assert.Empty(result.Events.OfType<BattleArmyDestroyed>());
        Assert.Contains("half-round cap",BattleEndedViewModel.FromReport(result.Report,after,f.Rules).WinnerLine,StringComparison.Ordinal);
        Assert.Contains(TacticalBattleSession.ReportLines(result.Report,after),line => line.Contains("2250 at the end",StringComparison.Ordinal));
        GameDataValidation.Validate("capped-state.json",after);
    }

    [Fact]
    public void Cap_skips_a_peace_draw_that_an_ordinary_victory_would_take()
    {
        var r=Changed(t=>t with {HalfRoundCap=100});
        r=r with {Combat=r.Combat with {AutoPeaceLoserUnityThreshold=0,AutoPeaceLoserCityThreshold=-1}};
        var f=Make(r,new[] {ArmyUnit("light_infantry",3000)},new[] {ArmyUnit("heavy_infantry",6000)});
        var reserve=f.Game.ArmyById("north-army-1")! with {Id="north-reserve",X=7,Y=5,Morale=99,Units=ValueList.Of(ArmyUnit("heavy_infantry",100000))};
        f=f with {Game=f.Game with {Armies=ValueList.From(f.Game.Armies.Append(reserve))}};
        var state=Bind(Arena((0,Unit(LI,0,0,3000)),(D0,Unit(HI,13,11,6000)))) with {Counter=100};
        var capped=Reopen(state,f);Assert.True(capped.EndHalfRound().Accepted);
        var capDraws=new ExpectedDraws((3,0));var capResult=capped.Conclude(f.Game,r,capDraws,f.World);
        Assert.Equal(0,capDraws.Remaining);Assert.False(capResult.PeaceDrawn);Assert.False(capResult.PeaceOffered);
        Assert.True(capResult.Report!.Capped);
        var natural=Reopen(state,f);Assert.True(natural.Surrender().Accepted);
        var normalDraws=new ExpectedDraws((4,0),(5,0));var normalResult=natural.Conclude(f.Game,r,normalDraws,f.World);
        Assert.Equal(0,normalDraws.Remaining);Assert.True(normalResult.PeaceDrawn);Assert.True(normalResult.PeaceOffered);
        Assert.False(normalResult.Report!.Capped);
    }

    [Fact]
    public void Natural_victory_during_the_cap_half_round_takes_priority_and_keeps_normal_rewards()
    {
        var f = Make(Changed(t => t with { HalfRoundCap = 100 }),new[] { ArmyUnit("heavy_infantry",6000) },new[] { ArmyUnit("light_infantry",1) });
        var state = Bind(Arena((0,Unit(HI,3,3,6000,quality:9,morale:99,target:D0)),(D0,Unit(LI,3,4,1,morale:19)))) with { Counter = 100 };
        var session = Reopen(state,f);
        Assert.True(session.EndHalfRound().Accepted);
        Assert.True(session.IsOver);
        Assert.False(session.Battle.Capped);
        Assert.DoesNotContain(session.Battle.Log,e=>e is TacticalCapReachedEvent);
        var draws = new Zeros();
        var result = session.Conclude(f.Game,f.Rules,draws,f.World);
        Assert.False(result.Report!.Capped);
        Assert.Equal(0,result.Report.WinnerSide);
        Assert.Equal(f.Game.ArmyById("south-army-1")!.Money,result.Report.CapturedMoney);
        Assert.Single(result.Report.Promotions);
        Assert.Equal(new[] { 4 },draws.Bounds);
        Assert.Null(result.State!.ArmyById("south-army-1"));
    }

    [Fact]
    public void Only_retreatLosses_preserves_prior_routes_but_surrender_captures_live_units()
    {
        var results = new List<TacticalBattleConclusion>();
        foreach (var r in new[] { Classical,Changed(t=>t with { RetreatLosses = true }) })
        {
            var f=Make(r,new[] { ArmyUnit("light_infantry",1000) },new[] { ArmyUnit("heavy_infantry",1000),ArmyUnit("heavy_infantry",6000) });
            var state=Bind(Arena((0,Unit(LI,3,3,1000,morale:99)),(D0,Unit(HI,3,4,1000,morale:19)),(D0+1,Unit(HI,4,4,6000,morale:99)))) with { SideToMove = 1 };
            state=TacticalRout.Rout(state,D0,TacticalContext.From(r),NoDraws.Instance);
            var session=Reopen(state,f);
            Assert.True(session.Surrender().Accepted);
            var draws=new Zeros();
            var result=session.Conclude(f.Game,r,draws,f.World);
            results.Add(result);
            if (r.Combat.Tactical.RetreatLosses)
            {
                var survivor=result.State!.ArmyById("south-army-1")!;
                Assert.Equal(850,survivor.TotalTroops);
                Assert.Equal(0,survivor.Money);
                Assert.Equal(0,survivor.SupplyTons);
                Assert.Equal(0,survivor.Moves);
                Assert.Single(survivor.Units);
                Assert.Equal(5,survivor.Units[0].Quality);
                Assert.Equal(new[] { 4,3 },draws.Bounds);
                Assert.Single(result.Events.OfType<ArmyScattered>());
                Assert.Empty(result.Events.OfType<BattleArmyDestroyed>());
                Assert.Equal(850,result.Report!.LoserFinishTotal);
                GameDataValidation.Validate("escaped-state.json",result.State);
            }
            else
            {
                Assert.Null(result.State!.ArmyById("south-army-1"));
                Assert.Equal(new[] { 4 },draws.Bounds);
                Assert.Single(result.Events.OfType<BattleArmyDestroyed>());
            }
        }
        Assert.Equal(results[0].State!.Nations,results[1].State!.Nations);
        Assert.Equal(results[0].State!.ArmyById("north-army-1"),results[1].State!.ArmyById("north-army-1"));
        Assert.Equal(results[0].Report!.Promotions,results[1].Report!.Promotions);
    }

    [Fact]
    public void Only_winnerRoutedUnitsRejoin_restores_exit_strength_without_an_extra_promotion_draw()
    {
        foreach(var r in new[] { Classical,Changed(t=>t with { WinnerRoutedUnitsRejoin = true }) })
        {
            var f=Make(r,new[] { ArmyUnit("heavy_infantry",1000),ArmyUnit("archers",500) },new[] { ArmyUnit("light_infantry",1000) });
            var state=Bind(Arena((0,Unit(HI,3,3,1000,morale:99)),(1,Unit(AR,4,3,500,morale:19)),(D0,Unit(LI,3,4,1000)))) with { SideToMove = 1 };
            state=TacticalRout.Rout(state,1,TacticalContext.From(r),NoDraws.Instance);
            var session=Reopen(state,f);Assert.True(session.Surrender().Accepted);
            var draws=new Zeros();var result=session.Conclude(f.Game,r,draws,f.World);
            var winner=result.State!.ArmyById("north-army-1")!;
            Assert.Equal(new[] { 4 },draws.Bounds);
            Assert.Single(result.Report!.Promotions);
            Assert.Equal(r.Combat.Tactical.WinnerRoutedUnitsRejoin ? 1500 : 1000,winner.TotalTroops);
            if(r.Combat.Tactical.WinnerRoutedUnitsRejoin)
            {
                Assert.Equal("archers",winner.Units[1].UnitTypeId);
                Assert.Equal(5,winner.Units[1].Quality);
                Assert.Equal(15,winner.SupplyTons);
            }
        }
    }

    [Fact]
    public void Cascade_exit_counts_are_captured_once_and_survive_a_later_rout_call()
    {
        var context=TacticalContext.From(Changed(t=>t with { RetreatLosses=true }));
        var state=Arena((0,Unit(LI,1,1,1000,morale:19)),(1,Unit(HI,2,1,3000,morale:35)),
            (2,Unit(AR,3,1,1000,morale:90)),(D0,Unit(HI,4,4,6000,morale:99)));
        var first=TacticalRout.Rout(state,0,context,NoDraws.Instance);
        Assert.Equal(1000,first.Slots[0].RoutedTroops);
        Assert.Equal(3000,first.Slots[1].RoutedTroops);
        Assert.Single(first.Log.OfType<TacticalCascadeRemovedEvent>());
        var repeated=TacticalRout.Rout(first,1,context,NoDraws.Instance);
        Assert.Equal(3000,repeated.Slots[1].RoutedTroops);
        Assert.Equal(4000,repeated.Slots.Sum(s=>s.RoutedTroops));
        var old=TacticalRout.Rout(state,0,TacticalContext.From(Classical),NoDraws.Instance);
        Assert.All(old.Slots,s=>Assert.Equal(0,s.RoutedTroops));
    }

    [Theory]
    [InlineData(LI,150)] [InlineData(HI,150)] [InlineData(AR,250)] [InlineData(LC,450)] [InlineData(HC,350)]
    public void Pursuit_uses_the_approved_type_weights_and_per_unit_floor(int type,int expected)
    {
        var c=TacticalContext.From(Changed(t=>t with { RetreatLosses=true }));
        var battle=Arena((0,Unit(type,1,1,1000)),(D0,Unit(HI,3,3,1000)));
        Assert.Equal(expected,RetreatLosses.Fraction(battle,0,1000,c));
        var units=ValueList.Of(ArmyUnit("light_infantry",666),ArmyUnit("light_infantry",334));
        var after=RetreatLosses.Apply(units,battle,0,c);
        Assert.Equal(666-(666*expected/1000),after[0].Troops);
        Assert.Equal(334-(334*expected/1000),after[1].Troops);
        var large=battle with { Slots=ValueList.From(battle.Slots.Select((s,i)=>i==0?s with { Troops=1000000 }:s)) };
        Assert.Equal(500,RetreatLosses.Fraction(large,0,1000,c));
        Assert.Equal(0,RetreatLosses.Fraction(battle,0,0,c));
    }

    [Fact]
    public void Routed_winners_do_not_pursue_and_small_units_can_lose_zero_troops()
    {
        var context=TacticalContext.From(Changed(t=>t with {RetreatLosses=true}));
        var state=Arena((D0,Unit(HI,3,3,1000)));
        state=state with {Slots=ValueList.From(state.Slots.Select((s,i)=>i==0?s with {RoutedTroops=1000}:s))};
        Assert.Equal(50,RetreatLosses.Fraction(state,0,1000,context));
        var units=ValueList.Of(ArmyUnit("light_infantry",1),ArmyUnit("heavy_infantry",19));
        Assert.Equal(units,RetreatLosses.Apply(units,state,0,context));
    }

    [Fact]
    public void A_real_rout_saved_at_the_start_restores_exactly_and_reaches_the_same_cap_and_outcome()
    {
        var f=Make(Preset("improved"),new[] { ArmyUnit("light_infantry",3000) },new[] { ArmyUnit("heavy_infantry",1000),ArmyUnit("heavy_infantry",6000) });
        var state=Bind(Arena((0,Unit(LI,3,3,3000)),(D0,Unit(HI,3,4,1000,morale:19)),(D0+1,Unit(HI,6,6,6000,morale:99)))) with { Counter=99 };
        var unsaved=Reopen(state,f);
        Assert.True(unsaved.Shoot(0,D0).Accepted);
        Assert.InRange(unsaved.Battle.Slots[D0].RoutedTroops,1,1000);
        Assert.True(unsaved.EndHalfRound().Accepted);
        Assert.Equal(100,unsaved.Battle.Counter);
        Assert.True(unsaved.AtHalfRoundStart);
        var save=Save(f,unsaved);var text=SaveManager.Serialize(save);
        Assert.Contains("routedTroops",text,StringComparison.Ordinal);
        var loaded=SaveManager.Load("routed-save.json",text,f.World,f.Rules);
        var restored=TacticalBattleSession.Reopen(loaded.State,f.Rules,loaded.Battle!,TacticalBattleSession.RulesetAsksForExactRestore(f.Rules));
        Assert.Equal(unsaved.ToSaved(),restored.ToSaved());
        Assert.Equal(100,restored.Battle.Counter);
        var prefix=unsaved.Battle.Log.Count;
        Assert.True(unsaved.EndHalfRound().Accepted);Assert.True(restored.EndHalfRound().Accepted);
        Assert.True(unsaved.Battle.Capped);Assert.True(restored.Battle.Capped);
        Assert.Equal(100,restored.Battle.Counter);
        Assert.Equal(unsaved.Battle.Log.Skip(prefix).ToList(),restored.Battle.Log.ToList());
        Assert.Equal(unsaved.DrawState,restored.DrawState);
        var a=unsaved.Conclude(f.Game,f.Rules,new Zeros(),f.World);
        var b=restored.Conclude(loaded.State,f.Rules,new Zeros(),f.World);
        Assert.Equal(a.State,b.State);
        Assert.Equal(a.Report! with { ExchangeLog=ValueList<TacticalEvent>.Empty },b.Report! with { ExchangeLog=ValueList<TacticalEvent>.Empty });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Escape_or_boxed_destruction_clears_only_the_losing_armys_fleet_claim(bool boxed)
    {
        var r=Changed(t=>t with { RetreatLosses=true });
        var f=Make(r,new[] {ArmyUnit("light_infantry",1000)},new[] {ArmyUnit("heavy_infantry",1000),ArmyUnit("heavy_infantry",6000)});
        var carrier=f.Game.Fleets.Single(x=>x.Nation=="south") with {CarriedArmyId="south-army-1"};
        var unrelated=f.Game.Fleets.Single(x=>x.Nation=="north");
        f=f with {Game=f.Game with {
            Fleets=ValueList.From(f.Game.Fleets.Select(x=>x.Id==carrier.Id?carrier:x)),
            Armies=ValueList.From(f.Game.Armies.Select(x=>x.Id=="south-army-1"?x with {AboardFleetId=carrier.Id}:x))}};
        var state=Bind(Arena((0,Unit(LI,3,3,1000,morale:99)),(D0,Unit(HI,3,4,1000,morale:0)),(D0+1,Unit(HI,4,4,6000,morale:99)))) with {SideToMove=1};
        state=TacticalRout.Rout(state,D0,TacticalContext.From(r),NoDraws.Instance);
        var session=Reopen(state,f);Assert.True(session.Surrender().Accepted);
        var world=boxed?f.World with {TileTypes=ValueList.From(f.World.TileTypes.Select(t=>t with {PassableByArmies=false}))}:f.World;
        var result=session.Conclude(f.Game,r,new Zeros(),world);
        Assert.Null(result.State!.FleetById(carrier.Id)!.CarriedArmyId);
        Assert.Equal(unrelated,result.State.FleetById(unrelated.Id));
        if(boxed)
        {
            Assert.Null(result.State.ArmyById("south-army-1"));
            Assert.Single(result.Events.OfType<BattleArmyDestroyed>());
            Assert.Empty(result.Events.OfType<ArmyScattered>());
            Assert.Equal(0,result.Report!.LoserFinishTotal);
        }
        else
        {
            var escaped=result.State.ArmyById("south-army-1")!;
            Assert.Null(escaped.AboardFleetId);
            var terrain=world.Terrain.Decode(world.Width,world.Height);
            Assert.Equal(terrain[escaped.Y*world.Width+escaped.X],escaped.CoveredTileCode);
            Assert.Single(result.Events.OfType<ArmyScattered>());
            Assert.Empty(result.Events.OfType<BattleArmyDestroyed>());
        }
        GameDataValidation.Validate("fleet-cleanup.json",result.State);
    }

    [Fact]
    public void A_zero_remnant_is_destroyed_without_drawing_a_scatter_distance()
    {
        var r=Changed(t=>t with {RetreatLosses=true,RetreatLossBase=1000,RetreatLossCap=1000,RetreatLossPursuit=0});
        GameDataValidation.Validate("full-loss-policy.json",r);
        var f=Make(r,new[] {ArmyUnit("light_infantry",1000)},new[] {ArmyUnit("heavy_infantry",1000),ArmyUnit("heavy_infantry",6000)});
        var state=Bind(Arena((0,Unit(LI,3,3,1000,morale:99)),(D0,Unit(HI,3,4,1000,morale:0)),(D0+1,Unit(HI,4,4,6000,morale:99)))) with {SideToMove=1};
        state=TacticalRout.Rout(state,D0,TacticalContext.From(r),NoDraws.Instance);
        var session=Reopen(state,f);Assert.True(session.Surrender().Accepted);
        var draws=new Zeros();var result=session.Conclude(f.Game,r,draws,f.World);
        Assert.Equal(new[] {4},draws.Bounds);
        Assert.Null(result.State!.ArmyById("south-army-1"));
        Assert.Equal(0,result.Report!.LoserFinishTotal);
        Assert.Single(result.Events.OfType<BattleArmyDestroyed>());
        Assert.Empty(result.Events.OfType<ArmyScattered>());
        GameDataValidation.Validate("empty-remnant.json",result.State);
    }

    [Fact]
    public void Promotion_then_peace_then_scatter_draws_exclude_rejoined_units_from_promotion()
    {
        var r=Changed(t=>t with {RetreatLosses=true,WinnerRoutedUnitsRejoin=true});
        r=r with {Combat=r.Combat with {AutoPeaceLoserUnityThreshold=0,AutoPeaceLoserCityThreshold=-1}};
        var f=Make(r,new[] {ArmyUnit("heavy_infantry",1000),ArmyUnit("archers",500)},new[] {ArmyUnit("heavy_infantry",1000),ArmyUnit("heavy_infantry",1000)});
        var reserve=f.Game.ArmyById("south-army-1")! with {Id="south-reserve",X=7,Y=5,Morale=99,Units=ValueList.Of(ArmyUnit("heavy_infantry",100000))};
        f=f with {Game=f.Game with {Armies=ValueList.From(f.Game.Armies.Append(reserve))}};
        var state=Bind(Arena((0,Unit(HI,3,3,1000,morale:99)),(1,Unit(AR,4,3,500,morale:0)),
            (D0,Unit(HI,3,4,1000,morale:0)),(D0+1,Unit(HI,4,4,1000,morale:99)))) with {SideToMove=1};
        var context=TacticalContext.From(r);
        state=TacticalRout.Rout(state,1,context,NoDraws.Instance);
        state=TacticalRout.Rout(state,D0,context,NoDraws.Instance);
        var session=Reopen(state,f);Assert.True(session.Surrender().Accepted);
        var draws=new ExpectedDraws((4,0),(5,0),(3,1));
        var result=session.Conclude(f.Game,r,draws,f.World);
        Assert.Equal(0,draws.Remaining);
        Assert.True(result.PeaceDrawn);Assert.True(result.PeaceOffered);
        Assert.Single(result.Report!.Promotions);
        Assert.Equal(5,result.State!.ArmyById("north-army-1")!.Units[1].Quality);
        Assert.Single(result.Events.OfType<PeaceTreatyOffered>());
        Assert.Single(result.Events.OfType<ArmyScattered>());
    }

    [Fact]
    public void Peace_gates_see_the_loser_removed_before_its_large_remnant_is_restored()
    {
        var r=Changed(t=>t with {RetreatLosses=true});
        r=r with {Combat=r.Combat with {AutoPeaceLoserUnityThreshold=0,AutoPeaceLoserCityThreshold=-1}};
        var f=Make(r,new[] {ArmyUnit("heavy_infantry",1000)},new[] {ArmyUnit("heavy_infantry",10000),ArmyUnit("heavy_infantry",1000)});
        var state=Bind(Arena((0,Unit(HI,3,3,1000,morale:99)),(D0,Unit(HI,3,4,10000,morale:0)),(D0+1,Unit(HI,4,4,1000,morale:99)))) with {SideToMove=1};
        state=TacticalRout.Rout(state,D0,TacticalContext.From(r),NoDraws.Instance);
        var session=Reopen(state,f);Assert.True(session.Surrender().Accepted);
        var draws=new ExpectedDraws((4,0),(3,0));var result=session.Conclude(f.Game,r,draws,f.World);
        Assert.Equal(0,draws.Remaining);Assert.False(result.PeaceDrawn);
        Assert.True(result.State!.ArmyById("south-army-1")!.TotalTroops>result.State.ArmyById("north-army-1")!.TotalTroops);
    }

    [Fact]
    public void Saved_routed_counts_reject_negative_live_excess_and_nonexistent_original_units()
    {
        var f=Make(Changed(t=>t with {RetreatLosses=true}),new[] {ArmyUnit("light_infantry",1000)},new[] {ArmyUnit("heavy_infantry",1000)});
        var session=Reopen(Bind(Arena((0,Unit(LI,3,3,1000)),(D0,Unit(HI,3,4,1000)))),f);
        var save=Save(f,session);
        foreach(var invalid in new[] {(Index:0,Troops:0,Routed:-1),(Index:0,Troops:1000,Routed:1),(Index:0,Troops:0,Routed:1001),(Index:1,Troops:0,Routed:1)})
        {
            var slots=ValueList.From(save.Battle!.Slots.Select((s,i)=>i==invalid.Index?s with {Troops=invalid.Troops,RoutedTroops=invalid.Routed}:s));
            var bad=save with {Battle=save.Battle with {Slots=slots}};
            var error=Assert.Throws<MalformedGameDataException>(()=>SaveManager.Load("invalid-routes.json",SaveManager.Serialize(bad),f.World,f.Rules));
            Assert.Contains("routedTroops",error.Message,StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("cap")] [InlineData("denominator")] [InlineData("base")] [InlineData("ceiling")]
    [InlineData("pursuit")] [InlineData("missingWeights")] [InlineData("weightCount")] [InlineData("negativeWeight")] [InlineData("overflow")]
    public void Invalid_new_tactical_policy_fields_are_rejected(string defect)
    {
        var r=Changed(t=>t with {RetreatLosses=true});var t=r.Combat.Tactical;
        t=defect switch
        {
            "cap"=>t with {HalfRoundCap=-1}, "denominator"=>t with {RetreatLossDenominator=0},
            "base"=>t with {RetreatLossBase=-1}, "ceiling"=>t with {RetreatLossCap=1001},
            "pursuit"=>t with {RetreatLossPursuit=-1}, "missingWeights"=>t with {RetreatPursuitWeights=null},
            "weightCount"=>t with {RetreatPursuitWeights=ValueList.Of(1)},
            "negativeWeight"=>t with {RetreatPursuitWeights=ValueList.Of(1,1,2,-1,3)},
            _=>t with {RetreatLossPursuit=int.MaxValue,RetreatPursuitWeights=ValueList.Of(int.MaxValue,int.MaxValue,int.MaxValue,int.MaxValue,int.MaxValue)},
        };
        r=r with {Combat=r.Combat with {Tactical=t}};
        Assert.Throws<MalformedGameDataException>(()=>GameDataValidation.Validate("bad-tactical-policy.json",r));
    }

    [Fact]
    public void Classical_saves_omit_new_routed_fields_and_legacy_missing_fields_default_to_zero()
    {
        var f=Make(Classical,new[] {ArmyUnit("light_infantry",1000)},new[] {ArmyUnit("heavy_infantry",1000)});
        var session=Reopen(Bind(Arena((0,Unit(LI,3,3,1000)),(D0,Unit(HI,3,4,1000)))),f);
        var text=SaveManager.Serialize(Save(f,session));
        Assert.DoesNotContain("routedTroops",text,StringComparison.Ordinal);
        var loaded=SaveManager.Load("classical-legacy.json",text,f.World,f.Rules);
        Assert.All(loaded.Battle!.Slots,s=>Assert.Equal(0,s.RoutedTroops));
        var resumed=TacticalBattleSession.Reopen(loaded.State,f.Rules,loaded.Battle,TacticalBattleSession.RulesetAsksForExactRestore(f.Rules));
        Assert.Equal(loaded.Battle.Counter+1,resumed.Battle.Counter);
    }

    private sealed class ExpectedDraws:IBattleDraws
    {
        private readonly Queue<(int Bound,int Result)> _expected;
        public ExpectedDraws(params (int Bound,int Result)[] expected)=>_expected=new(expected);
        public int Remaining=>_expected.Count;
        public int Random(int n){Assert.NotEmpty(_expected);var next=_expected.Dequeue();Assert.Equal(next.Bound,n);return next.Result;}
    }

    private sealed class Zeros:IBattleDraws
    {
        public List<int> Bounds {get;}=new();
        public int Random(int n){Bounds.Add(n);return 0;}
    }
}

