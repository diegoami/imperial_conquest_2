using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class FleetBalancePreviewCheck : Node
{
    private bool _ok = true;
    public override void _Ready()
    {
        try
        {
            foreach (var reverse in new[] { false, true })
                foreach (var empty in new[] { false, true }) Transfer(reverse, empty, cancel: false);
            Transfer(false, true, cancel: true);
            Split(cancel: false); Split(cancel: true); Stale();
        }
        catch (Exception ex) { GD.PrintErr($"FleetBalancePreviewCheck: {ex}"); _ok = false; }
        GD.Print($"FleetBalancePreviewCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static GameSession Build()
    {
        var data = GameDataContext.Repository.Resolve("classical-mediterranean");
        var initial = new GameSession(data.World, data.Ruleset, data.Scenario, seedOverride: 42, humanSeatNationId: "rome");
        var state = initial.State;
        var terrain = data.World.Terrain.Decode(data.World.Width, data.World.Height);
        bool Water(int x, int y) => data.World.TileTypeByCode(terrain[y * data.World.Width + x])?.PassableByFleets == true;
        var point = (from y in Enumerable.Range(1, data.World.Height - 2)
                     from x in Enumerable.Range(1, data.World.Width - 3)
                     where Water(x, y) && Water(x + 1, y) && Water(x, y + 1)
                     select (x, y)).First();
        var template = state.Fleets.First();
        var a = template with { Id = "preview-a", X = point.x, Y = point.y, Ships = 30, SupplyTons = 300,
            Money = 100, Nation = "rome", CarriedArmyId = null, ConstructionTicksRemaining = null };
        var b = a with { Id = "preview-b", X = point.x + 1, Ships = 20, SupplyTons = 200, Money = 50 };
        state = state with { Fleets = ValueList.Of(a, b) };
        return new GameSession(data.World, data.Ruleset, data.Scenario,
            new SaveGame(state.SchemaVersion, "fleet-preview", "fleet-preview", state.ScenarioId, state.WorldId, state.RulesetId, state));
    }

    private static string Balance(FleetState fleet) => $"{fleet.Id}: {fleet.Ships} ships · {fleet.SupplyTons} t · {fleet.Money} talents";
    private void Remove(Control dialog) { RemoveChild(dialog); dialog.QueueFree(); }

    private void Transfer(bool reverse, bool empty, bool cancel)
    {
        var session = Build(); var before = session.State;
        var a = reverse ? "preview-b" : "preview-a"; var b = reverse ? "preview-a" : "preview-b";
        var dialog = new FleetTransferDialog { Session = session, FleetId = a, PartnerId = b };
        AddChild(dialog);
        dialog.AdjustShipsForCheck(empty ? session.State.FleetById(a)!.Ships : 5);
        dialog.AdjustSupplyForCheck(10);
        dialog.AdjustMoneyForCheck(empty ? -20 : 20);
        var figures = dialog.FiguresForCheck;
        Check(ReferenceEquals(before, session.State), "fleet preview preserves authoritative state and RNG");
        var events = 0; session.EventsPublished += _ => events++;
        if (cancel)
        {
            dialog.CancelForCheck();
            Check(ReferenceEquals(before, session.State) && events == 0, "Cancel discards all fleet allocations");
        }
        else
        {
            dialog.OkForCheck();
            Check(events == 1 && session.State.Fleets.All(f => figures.Contains(Balance(f))),
                "both displayed balances match the committed command result in either direction");
            if (empty)
                Check(session.State.FleetById(a) is null && session.State.FleetById(b) is { Ships: 50, Money: 150, SupplyTons: 500 },
                    "empty donor pools its complete stores after the reverse resource leg");
        }
        Remove(dialog);
    }

    private void Split(bool cancel)
    {
        var session = Build(); var before = session.State;
        var dialog = new SplitFleetDialog { Session = session, FleetId = "preview-a", NewFleetId = "preview-new" };
        AddChild(dialog);
        dialog.AdjustShipsForCheck(10); dialog.AdjustSupplyForCheck(100); dialog.AdjustMoneyForCheck(40);
        var figures = dialog.FiguresForCheck;
        Check(figures.Contains("preview-a: 20 ships · 200 t · 60 talents")
            && figures.Contains("preview-new: 10 ships · 100 t · 40 talents") && ReferenceEquals(before, session.State),
            "Split displays both resulting balances before committing");
        if (cancel)
        {
            dialog.CancelForCheck(); Check(ReferenceEquals(before, session.State), "Split Cancel preserves the campaign");
        }
        else
        {
            dialog.OkForCheck();
            Check(session.State.FleetById("preview-new") is { } added && figures.Contains(Balance(added))
                && figures.Contains(Balance(session.State.FleetById("preview-a")!)), "Split preview equals the actual parent and child");
        }
        Remove(dialog);
    }

    private void Stale()
    {
        var session = Build();
        var dialog = new FleetTransferDialog { Session = session, FleetId = "preview-a", PartnerId = "preview-b" };
        var closed = false; dialog.Closed += () => closed = true; AddChild(dialog);
        dialog.AdjustShipsForCheck(5);
        session.Submit("fleet-transfer preview-a preview-b 1 0 0"); var external = session.State;
        dialog.OkForCheck();
        Check(!closed && ReferenceEquals(external, session.State) && dialog.ReplyForCheck.Contains("Reopen", StringComparison.OrdinalIgnoreCase),
            "a stale fleet plan cannot overwrite or partially change the campaign");
        Remove(dialog);
    }

    private void Check(bool condition, string message) { GD.Print((condition ? "PASS: " : "FAIL: ") + message); _ok &= condition; }
}
