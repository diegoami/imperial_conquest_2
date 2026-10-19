using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T169: every dialog/screen fits the approved physical minimum under the real canvas stretch.</summary>
public partial class DialogFitCheck : Control
{
    private const int SettleFrames = 12;
    // Measured by this check at pre-theme checkpoint d2643be, fixed 340px context panel, same fixture/23 labels.
    // Baseline viewport was 1500x1500. Both runs retain the 340px panel floor;
    // any scrollbar narrows text width, making the height comparison conservative.
    private const float BeforeArmyHeight = 661;
    private readonly Queue<Func<Control>> _cases = new();
    private readonly HashSet<string> _covered = new(StringComparer.Ordinal);
    private Control? _current;
    private MainGameScreen? _game;
    private int _frame;
    private bool _ok = true;
    private int _count;
    public override void _Ready()
    {
        GetWindow().Mode = Window.ModeEnum.Windowed;
        GetWindow().Size = new Vector2I(1366, 768);
        _game = new MainGameScreen { Session = BuildSession(), RepositoryRoot = GameDataContext.RepositoryRoot };
        _game.SetAnchorsPreset(LayoutPreset.FullRect);
        Size = GetViewport().GetVisibleRect().Size;
        AddChild(_game);
        _game.SelectArmyForCheck("tour-a");
        PrepareCases();
    }
    public override void _Process(double delta)
    {
        if (++_frame < SettleFrames) return;
        _frame = 0;
        try
        {
            Size = GetViewport().GetVisibleRect().Size;
            if (_game is not null)
            {
                var content = Descendants(_game.ContextPanel).OfType<VBoxContainer>().Single();
                var labels = Descendants(content).OfType<Label>().ToArray();
                var expected = InformationPanelModel.OwnArmy(_game.Session.State, _game.Session.World, _game.Session.Ruleset, _game.Session.State.ArmyById("tour-a")!).Select(line => line.Text).ToArray();
                Assert(labels.Skip(1).Take(expected.Length).Select(label => label.Text).SequenceEqual(expected), "army retains every T140 field in order");
                Assert(labels.Length == 23 && labels.Length == expected.Length + 2, "army retains all 23 labels including heading and order note");
                Assert(Mathf.Abs(_game.ContextPanel.Size.X - 340) < 1, "army comparison retains baseline 340px panel width");
                var armyScroll = Descendants(_game.ContextPanel).OfType<ScrollContainer>().Single();
                GD.Print($"DialogFitCheck: army panel={_game.ContextPanel.GetGlobalRect()}; scroll={armyScroll.GetGlobalRect()}; content={content.GetGlobalRect()}");
                var height = content.GetCombinedMinimumSize().Y;
                Assert(height < BeforeArmyHeight, $"army content height {height}px < measured predecessor {BeforeArmyHeight}px");
                GD.Print($"DialogFitCheck: physical={GetWindow().Size}; stretched viewport={Size}; army height={height}; baseline={BeforeArmyHeight}; labels={labels.Length}");
                RemoveChild(_game); _game.QueueFree(); _game = null;
            }
            else if (_current is not null)
            {
                var screenshotDir = System.Environment.GetEnvironmentVariable("IC2_SCREENSHOT_DIR");
                if (!string.IsNullOrWhiteSpace(screenshotDir))
                {
                    Directory.CreateDirectory(screenshotDir);
                    var error = GetViewport().GetTexture().GetImage().SavePng(Path.Combine(screenshotDir, $"{_count:D2}-{_current.GetType().Name}.png"));
                    Assert(error == Error.Ok, "minimum screenshot saved");
                }
                CheckFits(_current);
                _covered.Add(_current.GetType().Name);
                _count++;
                RemoveChild(_current); _current.QueueFree(); _current = null;
            }
            if (_cases.Count > 0)
            {
                _current = _cases.Dequeue()();
                _current.SetAnchorsPreset(LayoutPreset.FullRect);
                AddChild(_current);
                return;
            }
            Assert(GetWindow().Size == new Vector2I(1366, 768), "physical window is the approved 1366x768 minimum");
            var sourceRoot = ProjectSettings.GlobalizePath("res://");
            var inventory = Directory.GetFiles(Path.Combine(sourceRoot,"UI","Dialogs"),"*.cs").Concat(Directory.GetFiles(Path.Combine(sourceRoot,"Screens"),"*.cs"))
                .Select(Path.GetFileNameWithoutExtension).Where(name => GetType().Assembly.GetType("IC2.Slice.UI."+name)?.IsSubclassOf(typeof(Control)) == true || GetType().Assembly.GetType("IC2.Slice.Screens."+name)?.IsSubclassOf(typeof(Control)) == true).Select(name => name!).ToHashSet(StringComparer.Ordinal);
            Assert(inventory.SetEquals(_covered), $"all dialog/screen controls covered; missing={string.Join(",",inventory.Except(_covered))}; extra={string.Join(",",_covered.Except(inventory))}");
            GD.Print($"DialogFitCheck: cases={_count}; distinct controls={_covered.Count}; source inventory={inventory.Count}");
            Finish();
        }
        catch (Exception ex) { GD.PrintErr($"FAIL DialogFitCheck: {ex}"); _ok=false; Finish(); }
    }
    private void PrepareCases()
    {
        var session = BuildSession();
        var dialogNames = new[] { "ArmyTransferDialog","BalanceSheetDialog","BuildFleetDialog","ChangeUnitsDialog","EndTurnBox","FindCityDialog","FleetTransferDialog","FortifyDialog","RecruitMercenariesDialog","RecruitUnitDialog","RepairDialog","SplitArmyDialog","SplitFleetDialog","SupplyDialog","TaxationDialog" };
        foreach (var name in dialogNames)
        {
            var captured=name;
            _cases.Enqueue(() => MakeDialog(captured,session,false));
        }
        _cases.Enqueue(() => MakeDialog("SupplyDialog",session,true));
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        _cases.Enqueue(() => new BattleResultScreen { Session=new GameSession(toy.World,toy.Ruleset,toy.Scenario), Result=BattleResultViewModel.FromResult(MakeBattleResult(LoserFate.Destroyed)) });
        _cases.Enqueue(() => new DiplomacyScreen { Session=BuildSession() });
        _cases.Enqueue(() => new HotseatHandoffScreen { Info=new HotseatHandoffInfo("rome","Rome",true), CalendarLine="270 BC — Winter" });
        _cases.Enqueue(() => new GameEndScreen { Model=new GameEndViewModel("End of Game","You, ruler of Rome", "Your nation holds every city.","Years in power: 40", "At start", "At end",new[]{"Wealth 10000","Cities 10","Treasury 1000"},new[]{"Wealth 20000","Cities 20","Treasury 2000"},new[]{GameEndButton.MainMenu,GameEndButton.ViewMap}) });
        _cases.Enqueue(() => new PeaceOfferScreen { Model=new PeaceOfferViewModel("Offer of peace",new[]{"The enemy offers peace.","Do you accept?"},new[]{new PeaceOfferButton("Yes","peace-yes"),new PeaceOfferButton("No","peace-no")}) });
        _cases.Enqueue(() => new BattleEndedScreen { Model=new BattleEndedViewModel("Battle ended","Rome's army defeats Carthage's army.","Rome","Carthage",session.Ruleset.UnitTypes.Select(type=>new BattleEndedRow(type.Name,10000,8000,9000)).Append(new BattleEndedRow("Total",100000,80000,90000)).ToArray(),"The army of Rome captured 250 talents.","The army of Rome captured 100 tons of supplies.") });
        _cases.Enqueue(() => { var s=ClassicalSession();s.Submit("attack-army army-0 army-3");return new TacticalBattleScreen { Session=s,InitialView=s.OpenTacticalBattle!,RepositoryRoot=GameDataContext.RepositoryRoot }; });
        foreach (var button in new[] { "friendly_units", "enemy_units" })
        {
            var captured = button;
            _cases.Enqueue(() =>
            {
                var s = ClassicalSession(); s.Submit("attack-army army-0 army-3");
                var screen = new TacticalBattleScreen { Session = s, InitialView = s.OpenTacticalBattle!,
                    InitialTimeline = Array.Empty<TacticalBattleFrame>(), RepositoryRoot = GameDataContext.RepositoryRoot };
                screen.Ready += () => screen.PressToolbarButton(captured);
                return screen;
            });
        }
        _cases.Enqueue(() =>
        {
            var s = ClassicalSession(); s.Submit("attack-army army-0 army-3");
            var output = s.BattleGeneral(true);
            var exchange = output.TacticalTimeline.First(f => f.Event is TacticalMeleeEvent);
            return new TacticalBattleScreen { Session = s, InitialView = exchange.Before.View,
                InitialTimeline = new[] { exchange }, RepositoryRoot = GameDataContext.RepositoryRoot };
        });
    }
    private static Control MakeDialog(string name,GameSession session,bool fleetSupply)
    {
        var type=typeof(DialogFitCheck).Assembly.GetType("IC2.Slice.UI."+name)!;
        var dialog=(Control)Activator.CreateInstance(type)!;
        var values=new Dictionary<string,object> {
            ["Session"]=session, ["Submit"]=(Func<string,IReadOnlyList<string>>)(line=>session.Submit(line).Lines),
            ["ArmyId"]="tour-a", ["FleetId"]="tour-fleet", ["CityId"]="rome",
            ["PartnerId"]=name=="FleetTransferDialog" ? "tour-fleet-b":"tour-b",
            ["NewArmyId"]="fit-split-army",["NewFleetId"]="fit-split-fleet",
            ["WarningLines"]=new[]{"Some armies can still move.","Some fleets can still move.","Armies have no orders.","Cities are not recruiting.","Do you want to end the turn?"}
        };
        foreach(var property in type.GetProperties())
        {
            if(property.SetMethod is null)continue;
            if(name=="SupplyDialog" && ((fleetSupply && property.Name=="ArmyId") || (!fleetSupply && property.Name=="FleetId")))continue;
            if(values.TryGetValue(property.Name,out var value))property.SetValue(dialog,value);
        }
        return dialog;
    }
    private void CheckFits(Control root)
    {
        var viewport=new Rect2(Vector2.Zero,Size);
        var checkedControls=0;
        foreach(var c in Descendants(root).OfType<Control>())
        {
            if(!c.IsVisibleInTree())continue;
            // Scroll contents may extend vertically; the real scroll viewport itself must fit.
            var scroll=AncestorScroll(c,root);
            var rect=c.GetGlobalRect();
            if(scroll is not null)
            {
                if(!rect.Intersects(scroll.GetGlobalRect()))continue;
                if(rect.Position.Y < scroll.GetGlobalRect().Position.Y || rect.End.Y > scroll.GetGlobalRect().End.Y)continue;
                if(scroll.HorizontalScrollMode == ScrollContainer.ScrollMode.Disabled)
                    Assert(rect.Position.X >= scroll.GetGlobalRect().Position.X-1 && rect.End.X <= scroll.GetGlobalRect().End.X+1, $"{root.GetType().Name}: scroll content fits horizontally");
            }
            checkedControls++;
            if (c is RichTextLabel rich && rich.Text.Length > 0)
                Assert(rich.GetContentHeight() <= rich.Size.Y + 1, "current exchange rich text has no clipped lines");
            Assert(viewport.Grow(1).Encloses(rect),$"{root.GetType().Name}/{c.GetPath()}: rect {rect} fits {viewport}");
            if(c is Label label && label.Text.Length>0)
            {
                Assert(label.GetVisibleLineCount()>=label.GetLineCount(),$"{root.GetType().Name}: label has no clipped lines: {label.Text}");
                if(label.AutowrapMode==TextServer.AutowrapMode.Off)
                    Assert(label.GetMinimumSize().X<=label.Size.X+1,$"{root.GetType().Name}: label width fits: {label.Text}");
            }
        }
        GD.Print($"DialogFitCheck: {root.GetType().Name}: checked {checkedControls} visible controls");
    }
    private static ScrollContainer? AncestorScroll(Node c,Node root)
    {
        for(var p=c.GetParent();p is not null && p!=root;p=p.GetParent())if(p is ScrollContainer s)return s;
        return null;
    }
    private void Assert(bool condition,string detail) { if(!condition){GD.PrintErr("FAIL DialogFitCheck: "+detail);_ok=false;} }
    private void Finish() { var code=_ok?0:1;GD.Print($"DialogFitCheck: exiting with code {code}.");GetTree().Quit(code);SetProcess(false); }
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach(var child in node.GetChildren()){yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    private static GameSession BuildSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        static StartingArmy Roman(string id, int x, int y, params (string Type, int Troops, int Quality)[] units) => new(
            id, "rome", x, y, Morale: 70, Money: 100, SupplyTons: 100, Moves: 8,
            Units: ValueList.From(units.Select((unit, index) =>
                new UnitSlot(0, unit.Type, unit.Troops, unit.Quality, $"{id} unit {index + 1}"))));
        var armies = resolved.World.StartingArmies.ToList();
        armies.Add(Roman("tour-a", 100, 100, ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8), ("light_cavalry", 2_000, 7)));
        armies.Add(Roman("tour-b", 100, 99, ("light_cavalry", 4_000, 7), ("light_infantry", 1_000, 5)));
        var fleets = resolved.World.StartingFleets.ToList();
        fleets.Add(new StartingFleet("tour-fleet", "rome", 100, 120, Ships: 10, ConditionPercent: 80, Money: 0, SupplyTons: 300, Moves: 8));
        fleets.Add(new StartingFleet("tour-fleet-b", "rome", 100, 121, Ships: 6, ConditionPercent: 100, Money: 50, SupplyTons: 150, Moves: 8));
        var world = resolved.World with
        {
            StartingArmies = ValueList.From(armies),
            StartingFleets = ValueList.From(fleets),
        };
        var initial = new GameSession(world, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: "rome");

        // A Roman city one tile from tour-a (a supply provider, and the place mercenaries are offered) and
        // two mercenary offers there, so Supply army and Recruit mercenaries open on the sample game.
        var city = new CityState(
            "tour-city", "Tour city", X: 101, Y: 100, Owner: "rome", Allegiance: "rome",
            Loyalty: 90, SupplyTons: 500, FortificationCode: 0,
            PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, Garrison: ValueList<UnitSlot>.Empty);
        var offers = new List<MercenaryPoolSlot>
        {
            new(900, X: 101, Y: 100, NameLabel: 0, UnitTypeId: "heavy_cavalry", Troops: 960, Quality: 9),
            new(901, X: 101, Y: 100, NameLabel: 0, UnitTypeId: "light_infantry", Troops: 3_868, Quality: 8),
        };
        var state = initial.State with
        {
            Cities = ValueList.From(initial.State.Cities.Concat(new[] { city })),
            MercenaryPool = ValueList.From(initial.State.MercenaryPool.Concat(offers)),
        };
        var save = new SaveGame(state.SchemaVersion, "font-tour", "font-tour", state.ScenarioId, state.WorldId, state.RulesetId, state);
        return new GameSession(world, resolved.Ruleset, resolved.Scenario, save);
    }

    private static GameSession ClassicalSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var ruleset = resolved.Ruleset with { Flags = resolved.Ruleset.Flags with { NewGameTurnOrder = NewGameTurnOrderPolicy.Fixed } };
        var armies = resolved.World.StartingArmies.Select(a => a.Id == "army-3" ? a with { X = 101, Y = 37, Moves = 8 } : a).ToList();
        var world = resolved.World with { StartingArmies = ValueList.From(armies) };
        return new GameSession(world, ruleset, resolved.Scenario, null, "rome") { HumanBattles = HumanBattleMode.Tactical };
    }

    private static BattleResult MakeBattleResult(LoserFate fate) => new(
        Kind: BattleKind.Field,
        AttackerId: "north-army-1",
        DefenderId: "south-army-1",
        AttackerNationId: "north",
        DefenderNationId: "south",
        AttackerPower: 120,
        DefenderPower: 80,
        Winner: BattleSide.Attacker,
        AppliedDefeatOutcome: fate == LoserFate.Destroyed ? DefeatOutcome.Destroyed : DefeatOutcome.Scatter,
        LoserFate: fate,
        WinnerCasualties: 12,
        LoserCasualties: 80,
        UnitCasualties: ValueList<UnitCasualty>.Empty,
        Promotions: ValueList<UnitPromotion>.Empty,
        AbsorbedMoney: 250,
        AbsorbedSupplyTons: 10,
        WinnerUnityDelta: 5,
        LoserUnityDelta: -10,
        WinnerShipsLost: 0,
        WinnerConditionLost: 0,
        WinnerUnitsLost: 0,
        PeaceTreatyFired: false,
        PeaceTreatyOffered: false,
        Scatter: fate == LoserFate.Scattered
            ? new ScatterOutcome(FromX: 4, FromY: 2, ToX: 5, ToY: 3, RequestedDistance: 2, ActualDistance: 2)
            : null);

}
