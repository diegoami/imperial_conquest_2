using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using F = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class AiConfirmedDestructionSupersessionTests
{
    private const string Me = "north";
    private const string Foe = "south";
    private static World World => BattleCommandTestbed.ToyWorld;
    private static ArmyState Army(string id, string nation, int x, int y) =>
        F.Army(id, nation, x, y, 60, F.Unit("light_infantry", 1000));
    private static AiTurnDecided Boundary(string nation = Me) => new(nation, ValueList<string>.Empty, 0, 0, 0, false);
    private static AiArmyTargetTree.DecisionRecord Decision(string target = "t") =>
        new(Me, "s", 2, 2, AiArmyTargetTree.Kind.AttackArmy, null, -1, -1, target, 6, target == "t" ? 2 : 4, 0, 100, 0, 4, 0);
    private static BattleResolved Win(string winner = "w", string loser = "t", string winNation = Me, string loseNation = Foe) =>
        new(new BattleResult(BattleKind.Field, winner, loser, winNation, loseNation, 100, 50,
            BattleSide.Attacker, null, LoserFate.Destroyed, 0, 1000,
            ValueList<UnitCasualty>.Empty, ValueList<UnitPromotion>.Empty,
            0, 0, 0, 0, 0, 0, 0, false, false, null));
    private static RecruitMobilized Created(string id) => new(Me, "home", id, true, "light_infantry", 1000, 6, "Recruit");
    private static GameState State(params ArmyState[] armies) =>
        BattleCommandTestbed.StateWith(new[] { F.Nation(Me), F.Nation(Foe) }, Array.Empty<CityState>(), armies);
    private sealed record Evidence(GameState Start, GameState End, List<DomainEvent> Events,
        List<AiArmyTargetTree.DecisionRecord> Decisions, World World);
    private static Evidence Valid()
    {
        var start = State(Army("s", Me, 2, 2), Army("w", Me, 3, 2),
            Army("t", Foe, 6, 2), Army("u", Foe, 6, 4));
        var end = State(Army("s", Me, 3, 3), Army("w", Me, 3, 2), Army("u", Foe, 6, 4));
        return new(start, end, new() { Win(), new ArmyMoved("s", Me, 2, 2, 3, 3, 1), Boundary() },
            new() { Decision(), Decision(), Decision("u") }, World);
    }
    private static bool Classify(Evidence e, int index = 0) =>
        AiTargetTreeDecisionsAreCarriedOutTests.IsSupersededByConfirmedDestruction(
            e.Decisions, index, e.Start, e.End, e.Events, e.World);

    [Fact]
    public void Both_earlier_duplicate_records_independently_find_the_later_carried_retarget()
    {
        var e = Valid();
        Assert.True(Classify(e, 0));
        Assert.True(Classify(e, 1));
        Assert.False(Classify(e, 2));
    }

    [Fact]
    public void Accepted_later_attack_also_proves_the_retarget_without_subject_movement()
    {
        var e = Valid();
        e.Events[1] = Win("s", "u");
        e = e with { End = State(Army("s", Me, 2, 2), Army("w", Me, 3, 2)) };
        Assert.True(Classify(e));
    }

    [Fact]
    public void A_city_retarget_with_accepted_siege_qualifies()
    {
        var e = Valid();
        e.Decisions[2] = e.Decisions[2] with { Kind = AiArmyTargetTree.Kind.AttackCity, TargetCityId = "city", TargetCityX = 6, TargetCityY = 4 };
        e.Events[1] = new BattleResolved(Win("s", "city").Result with { Kind = BattleKind.Siege });
        e = e with { End = State(Army("s", Me, 2, 2), Army("w", Me, 3, 2), Army("u", Foe, 6, 4)) };
        Assert.True(Classify(e));
    }

    [Fact]
    public void Target_movement_before_destruction_preserves_proved_identity()
    {
        var e = Valid();
        e = e with { Start = State(Army("s", Me, 2, 2), Army("w", Me, 3, 2), Army("t", Foe, 5, 2), Army("u", Foe, 6, 4)) };
        e.Events.Insert(0, new ArmyMoved("t", Foe, 5, 2, 6, 2, 1));
        Assert.True(Classify(e));
    }

    [Theory]
    [InlineData("absent-target-at-start")]
    [InlineData("own-target")]
    [InlineData("wrong-target-nation")]
    [InlineData("wrong-winner-nation")]
    [InlineData("unknown-target-nation")]
    [InlineData("ambiguous-nation-map")]
    [InlineData("missing-nation-map")]
    [InlineData("subject-won")]
    [InlineData("foreign-winner")]
    [InlineData("missing-winner")]
    [InlineData("wrong-target-id")]
    [InlineData("scatter-instead-of-death")]
    [InlineData("mere-absence")]
    [InlineData("target-present")]
    [InlineData("target-recreated")]
    [InlineData("target-scattered")]
    [InlineData("target-moved-after-death")]
    [InlineData("wrong-target-position")]
    [InlineData("inconsistent-target-move")]
    [InlineData("wrong-target-move-nation")]
    [InlineData("duplicate-target-death")]
    [InlineData("target-conflicting-battle")]
    [InlineData("missing-boundary")]
    [InlineData("duplicate-boundary")]
    [InlineData("death-before-turn")]
    [InlineData("death-after-turn")]
    [InlineData("no-later-entry")]
    [InlineData("only-duplicate-entry")]
    [InlineData("earlier-retarget-only")]
    [InlineData("other-subject")]
    [InlineData("other-nation")]
    [InlineData("other-position")]
    [InlineData("nonattack-retarget")]
    [InlineData("missing-action")]
    [InlineData("action-before-death")]
    [InlineData("action-after-boundary")]
    [InlineData("wrong-action-nation")]
    [InlineData("wrong-march-origin")]
    [InlineData("march-away")]
    [InlineData("march-no-progress")]
    [InlineData("march-no-cost")]
    [InlineData("extra-subject-move")]
    [InlineData("subject-scattered")]
    [InlineData("subject-recreated")]
    [InlineData("subject-absent")]
    [InlineData("subject-wrong-owner")]
    [InlineData("subject-end-conflict")]
    [InlineData("subject-conflicting-battle")]
    [InlineData("winner-recreated")]
    public void Missing_or_conflicting_evidence_fails_closed(string mutation)
    {
        var e = Valid();
        var death = (BattleResolved)e.Events[0];
        var march = (ArmyMoved)e.Events[1];
        switch (mutation)
        {
            case "absent-target-at-start": e = e with { Start = e.Start with { Armies = ValueList.From(e.Start.Armies.Where(a => a.Id != "t")) } }; break;
            case "own-target": e = e with { Start = e.Start with { Armies = ValueList.From(e.Start.Armies.Select(a => a.Id == "t" ? a with { Nation = Me } : a)) } }; break;
            case "wrong-target-nation": e.Events[0] = death with { Result = death.Result with { DefenderNationId = Me } }; break;
            case "wrong-winner-nation": e.Events[0] = death with { Result = death.Result with { AttackerNationId = Foe } }; break;
            case "unknown-target-nation": e = e with { Start = e.Start with { Armies = ValueList.From(e.Start.Armies.Select(a => a.Id == "t" ? a with { Nation = "unknown" } : a)) } }; break;
            case "ambiguous-nation-map": e = e with { World = e.World with { Nations = ValueList.From(e.World.Nations.Concat(new[] { e.World.Nations[0] with { Id = "collision", Name = Foe } })) } }; break;
            case "missing-nation-map": e = e with { World = e.World with { Nations = ValueList.From(e.World.Nations.Where(n => n.Id != Foe)) } }; break;
            case "subject-won": e.Events[0] = Win("s"); break;
            case "foreign-winner": e = e with { Start = e.Start with { Armies = ValueList.From(e.Start.Armies.Select(a => a.Id == "w" ? a with { Nation = Foe } : a)) } }; break;
            case "missing-winner": e = e with { Start = e.Start with { Armies = ValueList.From(e.Start.Armies.Where(a => a.Id != "w")) } }; break;
            case "wrong-target-id": e.Events[0] = Win("w", "wrong"); break;
            case "scatter-instead-of-death": e.Events[0] = death with { Result = death.Result with { LoserFate = LoserFate.Scattered } }; break;
            case "mere-absence": e.Events.RemoveAt(0); break;
            case "target-present": e = e with { End = e.End with { Armies = ValueList.From(e.End.Armies.Append(Army("t", Foe, 6, 2))) } }; break;
            case "target-recreated": e.Events.Insert(1, Created("t")); break;
            case "target-scattered": e.Events.Insert(0, new ArmyScattered("t", Foe, 6, 2)); break;
            case "target-moved-after-death": e.Events.Insert(1, new ArmyMoved("t", Foe, 6, 2, 6, 3, 1)); break;
            case "wrong-target-position": e.Decisions[0] = e.Decisions[0] with { TargetArmyX = 5 }; break;
            case "inconsistent-target-move": e.Events.Insert(0, new ArmyMoved("t", Foe, 5, 2, 6, 2, 1)); break;
            case "wrong-target-move-nation": e.Events.Insert(0, new ArmyMoved("t", Me, 6, 2, 6, 2, 1)); break;
            case "duplicate-target-death": e.Events.Insert(1, death); break;
            case "target-conflicting-battle": e.Events.Add(death with { Result = death.Result with { LoserFate = LoserFate.Scattered } }); break;
            case "missing-boundary": e.Events.RemoveAt(2); break;
            case "duplicate-boundary": e.Events.Add(Boundary()); break;
            case "death-before-turn": e.Events.Insert(1, Boundary(Foe)); break;
            case "death-after-turn": e.Events.Insert(0, Boundary()); e.Events.RemoveAt(e.Events.Count - 1); break;
            case "no-later-entry": e.Decisions.RemoveRange(1, 2); break;
            case "only-duplicate-entry": e.Decisions.RemoveAt(2); break;
            case "earlier-retarget-only": e.Decisions.Reverse(); Assert.False(Classify(e, 1)); return;
            case "other-subject": e.Decisions[2] = e.Decisions[2] with { ArmyId = "w" }; break;
            case "other-nation": e.Decisions[2] = e.Decisions[2] with { NationId = Foe }; break;
            case "other-position": e.Decisions[2] = e.Decisions[2] with { ArmyX = 3 }; break;
            case "nonattack-retarget": e.Decisions[2] = e.Decisions[2] with { Kind = AiArmyTargetTree.Kind.MoveToResupplyCity }; break;
            case "missing-action": e.Events.RemoveAt(1); e = e with { End = e.End with { Armies = ValueList.From(e.End.Armies.Select(a => a.Id == "s" ? a with { X = 2, Y = 2 } : a)) } }; break;
            case "action-before-death": e.Events.Reverse(0, 2); break;
            case "action-after-boundary": (e.Events[1], e.Events[2]) = (e.Events[2], e.Events[1]); break;
            case "wrong-action-nation": e.Events[1] = march with { NationId = Foe }; break;
            case "wrong-march-origin": e.Events[1] = march with { FromX = 1 }; break;
            case "march-away": e.Events[1] = march with { ToX = 1, ToY = 1 }; break;
            case "march-no-progress": e.Events[1] = march with { ToX = 2, ToY = 2 }; break;
            case "march-no-cost": e.Events[1] = march with { MovesSpent = 0 }; break;
            case "extra-subject-move": e.Events.Insert(1, march with { ToX = 2, ToY = 2 }); break;
            case "subject-scattered": e.Events.Insert(1, new ArmyScattered("s", Me, 2, 2)); break;
            case "subject-recreated": e.Events.Insert(1, Created("s")); break;
            case "subject-absent": e = e with { End = e.End with { Armies = ValueList.From(e.End.Armies.Where(a => a.Id != "s")) } }; break;
            case "subject-wrong-owner": e = e with { End = e.End with { Armies = ValueList.From(e.End.Armies.Select(a => a.Id == "s" ? a with { Nation = Foe } : a)) } }; break;
            case "subject-end-conflict": e = e with { End = e.End with { Armies = ValueList.From(e.End.Armies.Select(a => a.Id == "s" ? a with { X = 7 } : a)) } }; break;
            case "subject-conflicting-battle": e.Events.Insert(1, Win("s", "u")); break;
            case "winner-recreated": e.Events.Insert(0, Created("w")); break;
            default: throw new InvalidOperationException(mutation);
        }
        Assert.False(Classify(e));
    }

    [Theory]
    [InlineData("wrong-target")]
    [InlineData("wrong-attacker")]
    [InlineData("wrong-attacker-nation")]
    [InlineData("wrong-defender-nation")]
    [InlineData("wrong-battle-kind")]
    public void Accepted_attack_must_match_the_later_retarget_and_nations(string mutation)
    {
        var e = Valid();
        var attack = Win("s", "u").Result;
        attack = mutation switch
        {
            "wrong-target" => attack with { DefenderId = "t" },
            "wrong-attacker" => attack with { AttackerId = "w" },
            "wrong-attacker-nation" => attack with { AttackerNationId = Foe },
            "wrong-defender-nation" => attack with { DefenderNationId = Me },
            _ => attack with { Kind = BattleKind.Siege },
        };
        e.Events[1] = new BattleResolved(attack);
        e = e with { End = State(Army("s", Me, 2, 2), Army("w", Me, 3, 2)) };
        Assert.False(Classify(e));
    }
}



