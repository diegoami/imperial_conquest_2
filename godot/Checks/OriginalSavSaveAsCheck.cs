using System.Security.Cryptography;
using Godot;
using IC2.Data;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T186: real Save As routing to the checked live-session exporter, with a private synthetic template.</summary>
public partial class OriginalSavSaveAsCheck : Node
{
    private bool _ok = true;

    public override void _Ready()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ic2-t186-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { Run(folder); }
        catch (Exception ex) { GD.PrintErr($"FAIL OriginalSavSaveAsCheck: {ex}"); _ok = false; }
        finally { Directory.Delete(folder, recursive: true); }
        var code = _ok ? 0 : 1;
        GD.Print($"OriginalSavSaveAsCheck: exiting with code {code}.");
        CheckExit.Quit(this, code);
    }

    private void Run(string folder)
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var session = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario,
            seedOverride: 17, humanSeatNationId: "rome");
        var game = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        AddChild(game);

        var template = Path.Combine(folder, "template.sav");
        var profile = new OriginalSaveUiProfile(
            Enumerable.Range(0, 16).Select(_ => new OriginalNationUi(1, 0,
                [0, 0, 400, 300], [400, 0, 300, 200], [400, 200, 300, 260], 40, 80, 0, 10, 5)).ToArray(),
            0, 0, 600, 800);
        var source = new SaveGame(GameDataSchema.CurrentVersion, "check", "check",
            session.State.ScenarioId, session.State.WorldId, session.State.RulesetId, session.State);
        File.WriteAllBytes(template, OriginalSaveCodec.Encode(
            OriginalSaveExporter.ToDocument(source, session.World, session.Ruleset, profile).Document));
        var templateHash = SHA256.HashData(File.ReadAllBytes(template));
        var before = session.State;

        Check(game.CommandTable.TryInvoke("file.save_as"), "File > Save As opens the existing command");
        var saveDialog = game.SaveAsDialog;
        Check(saveDialog.Filters.Length == 2 && saveDialog.Filters[0].Contains("*.json")
            && saveDialog.Filters[1].Contains("*.sav"), "Save As offers both file formats");
        saveDialog.Visible = false;

        var cancelled = Path.Combine(folder, "cancelled.sav");
        saveDialog.EmitSignal(FileDialog.SignalName.FileSelected, cancelled);
        Check(game.SavTemplatePendingForCheck && game.SavTemplateDialog.Visible,
            "SAV output waits for a separate original-template dialog");
        game.SavTemplateDialog.Visible = false;
        game.SavTemplateDialog.EmitSignal(FileDialog.SignalName.Canceled);
        Check(!game.SavTemplatePendingForCheck && !File.Exists(cancelled), "template cancel writes nothing");

        var missing = Path.Combine(folder, "missing-output.sav");
        game.SaveAsForCheck(missing);
        game.SavTemplateDialog.Visible = false;
        game.SavTemplateForCheck(Path.Combine(folder, "missing-template.sav"));
        Check(!File.Exists(missing) && game.SaveMessageForCheck.StartsWith("Not exported:", StringComparison.Ordinal),
            "missing template refuses without output");

        var malformedTemplate = Path.Combine(folder, "malformed-template.sav");
        File.WriteAllBytes(malformedTemplate, [1, 2, 3]);
        var malformedOutput = Path.Combine(folder, "malformed-output.sav");
        game.SaveAsForCheck(malformedOutput);
        game.SavTemplateDialog.Visible = false;
        game.SavTemplateForCheck(malformedTemplate);
        Check(!File.Exists(malformedOutput) && game.SaveMessageForCheck.StartsWith("Not exported:", StringComparison.Ordinal),
            "malformed template refuses without a partial output");

        var output = Path.Combine(folder, "export.sav");
        game.SaveAsForCheck(output);
        game.SavTemplateDialog.Visible = false;
        game.SavTemplateForCheck(template);
        Check(File.Exists(output), "the production Save As callback wrote a real SAV");
        if (File.Exists(output))
        {
            OriginalSaveLayout.Validate(File.ReadAllBytes(output));
            Check(OriginalSaveCodec.Decode(File.ReadAllBytes(output)).Armies.Count == session.State.Armies.Count,
                "the SAV decodes with the expected live army count");
        }
        Check(game.SaveMessageForCheck.StartsWith("Exported original-compatible SAV", StringComparison.Ordinal),
            "the screen reports export success");
        Check(game.LastSavedPath is null && ReferenceEquals(before, session.State),
            "SAV export keeps the current native path and live state unchanged");
        Check(templateHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(template))),
            "the private template is unchanged");

        var outputHash = SHA256.HashData(File.ReadAllBytes(output));
        game.SaveAsForCheck(output);
        Check(!game.SavTemplatePendingForCheck && game.SaveMessageForCheck.Contains("already exists", StringComparison.Ordinal)
            && outputHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(output))),
            "an existing SAV is never replaced");

        var native = Path.Combine(folder, "native.json");
        game.SaveAsForCheck(native);
        Check(File.Exists(native) && game.LastSavedPath == native,
            "native JSON Save As still chooses the ordinary Save path");
        var beforeNative = File.ReadAllBytes(native);
        session.Submit("end");
        game.PressSaveForCheck();
        Check(game.LastSavedPath == native && !beforeNative.SequenceEqual(File.ReadAllBytes(native)),
            "ordinary Save still updates JSON after a SAV export");
        Check(outputHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(output))),
            "ordinary Save leaves the exported SAV unchanged");

        var armies = resolved.World.StartingArmies.Select(a => a.Id == "army-3"
            ? a with { X = 101, Y = 37, Moves = 8 } : a);
        var battleWorld = resolved.World with { StartingArmies = ValueList.From(armies) };
        var battleSession = new GameSession(battleWorld, resolved.Ruleset, resolved.Scenario, null, "rome")
            { HumanBattles = HumanBattleMode.Tactical };
        battleSession.Submit("attack-army army-0 army-3");
        Check(battleSession.OpenTacticalBattle is not null, "the refusal control opened a real tactical battle");
        var battleGame = new MainGameScreen { Session = battleSession, RepositoryRoot = GameDataContext.RepositoryRoot };
        AddChild(battleGame);
        var battleOutput = Path.Combine(folder, "battle-refused.sav");
        battleGame.SaveAsForCheck(battleOutput);
        battleGame.SavTemplateDialog.Visible = false;
        battleGame.SavTemplateForCheck(template);
        Check(!File.Exists(battleOutput) && battleGame.SaveMessageForCheck.Contains("OPEN_BATTLE", StringComparison.Ordinal),
            "the real screen reports the bridge's open-battle refusal");
    }

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL OriginalSavSaveAsCheck: {description}");
        _ok &= condition;
    }
}
