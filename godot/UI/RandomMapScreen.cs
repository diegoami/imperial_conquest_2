using Godot;
using IC2.Engine.MapGen;
using IC2.Engine.Model;
using IC2.Engine.Serialization;

namespace IC2.Slice.UI;

// This screen means the generator's style registry, not UserSettings.cs's map-rendering styles of
// the same name in this very namespace â€” the alias keeps that collision from resolving silently.
using GeneratorStyles = IC2.Engine.MapGen.MapStyles;

/// <summary>
/// T294f: New Game's random-map screen â€” the owner-approved flow (docs/tasks/T294.md, decisions
/// 2026-11-09): "Random map, style and seed, three previews, choose one, seats". Choose a style and a
/// seed, generate the three candidates, look at their previews, take one into the seat step.
/// </summary>
/// <remarks>
/// <strong>Manual reference and approved exception.</strong> The original's New Game (manual Â§1,
/// "Starting a new game") has one fixed Mediterranean map and no random-map step, so this screen is
/// the contract's specifically approved exception; the seat step it hands over to keeps the manual's
/// "Human and computer leaders" behaviour (the drawn leader names, the human ticks, the 25-character
/// names â€” already implemented by <see cref="ScenarioSeatScreen"/>). The three candidates are
/// <see cref="GeneratedGame.Previews"/>' pure function of (style, seed); generation runs off the
/// UI thread and the world that comes back is embedded in the session's saves (T294e), so no preview
/// cache exists and a reload never regenerates.
/// </summary>
public partial class RandomMapScreen : Control
{
    /// <summary>Raised once "Use this map" fires, with the generated game's resolved triple and its provenance.</summary>
    public event Action<ResolvedScenario, GeneratedWorld>? Chosen;

    /// <summary>Raised by the "Back" button â€” returns to the ruleset chooser.</summary>
    public event Action? BackRequested;

    /// <summary>The ruleset the generated game runs under (the flow's first step chose it).</summary>
    public required string RulesetId { get; init; }

    public OptionButton StyleChoice { get; private set; } = null!;
    public LineEdit SeedEdit { get; private set; } = null!;
    public Button GenerateButton { get; private set; } = null!;
    public List<Button> CandidateButtons { get; } = new();
    public List<TextureRect> PreviewRects { get; } = new();
    public Label StatusLine { get; private set; } = null!;

    /// <summary>The "Continue to seats" button, for the flow checks that drive this screen.</summary>
    public Button ContinueButton => _useButton;

    private List<GeneratedWorld>? _candidates;
    private int _chosen = -1;
    private Button _useButton = null!;
    private bool _generating;
    private string _pendingStatus = string.Empty;
    private bool _populateQueued;

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", AppTheme.ScreenMarginSide);
        margin.AddThemeConstantOverride("margin_right", AppTheme.ScreenMarginSide);
        margin.AddThemeConstantOverride("margin_top", AppTheme.ScreenMarginVertical);
        margin.AddThemeConstantOverride("margin_bottom", AppTheme.ScreenMarginVertical);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(header);
        var backButton = UiKit.MakeButton("< Back", () => BackRequested?.Invoke(), AppTheme.Button);
        backButton.CustomMinimumSize = new Vector2(90, 34);
        header.AddChild(backButton);
        header.AddChild(UiKit.MakeLabel("New Game â€” Random Map", AppTheme.Header, UiKit.AccentColor));

        var picks = new HBoxContainer();
        picks.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(picks);
        picks.AddChild(UiKit.MakeLabel("Style"));
        StyleChoice = new OptionButton { CustomMinimumSize = new Vector2(220, 0) };
        foreach (var style in GeneratorStyles.All) StyleChoice.AddItem(style.Id);
        StyleChoice.Select(0);
        picks.AddChild(StyleChoice);
        picks.AddChild(UiKit.MakeLabel("Seed"));
        SeedEdit = new LineEdit { CustomMinimumSize = new Vector2(140, 0), Text = "1" };
        SeedEdit.TextChanged += _ => UpdateGenerateEnabled();
        picks.AddChild(SeedEdit);
        GenerateButton = UiKit.MakeButton("Generate previews", Generate, AppTheme.Button);
        picks.AddChild(GenerateButton);

        StatusLine = UiKit.MakeLabel(string.Empty, AppTheme.Button);
        column.AddChild(StatusLine);

        var previews = new HBoxContainer();
        previews.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        previews.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(previews);
        for (var i = 0; i < 3; i++)
        {
            var slot = new VBoxContainer();
            slot.AddThemeConstantOverride("separation", AppTheme.DialogGap);
            previews.AddChild(slot);
            var rect = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(340, 240),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            slot.AddChild(rect);
            PreviewRects.Add(rect);
            var index = i; // one closure per slot: a shared loop variable would hand every button the final i
            var pick = UiKit.MakeButton($"Use candidate {i + 1}", () => UseCandidate(index), AppTheme.Button);
            pick.Disabled = true;
            pick.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            slot.AddChild(pick);
            CandidateButtons.Add(pick);
        }

        _useButton = UiKit.MakeButton("Continue to seats", () => Chosen?.Invoke(BuildResolved(), _candidates![_chosen]), AppTheme.Button);
        _useButton.Disabled = true;
        _useButton.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        column.AddChild(_useButton);
    }

    private void UpdateGenerateEnabled() =>
        GenerateButton.Disabled = _generating || !ulong.TryParse(SeedEdit.Text.Trim(), out _);

    private void Generate()
    {
        if (_generating || !ulong.TryParse(SeedEdit.Text.Trim(), out var seed)) return;
        _generating = true;
        _candidates = null;
        _chosen = -1;
        _useButton.Disabled = true;
        GenerateButton.Disabled = true;
        StatusLine.Text = "Generating three candidatesâ€¦";
        foreach (var pick in CandidateButtons) pick.Disabled = true;
        foreach (var rect in PreviewRects) rect.Texture = null;

        var styleId = GeneratorStyles.All[StyleChoice.Selected].Id;
        Task.Run(() => GeneratedGame.Previews(styleId, seed)).ContinueWith(t =>
        {
            // Worker thread: only fields here; every control touch is deferred to _Process.
            _generating = false;
            if (t.IsCompletedSuccessfully)
            {
                _candidates = t.Result.ToList();
                _pendingStatus = $"Three candidates for '{styleId}', seeds {seed}-{seed + 2}. Pick one.";
            }
            else
            {
                _pendingStatus = t.Exception?.InnerException?.Message ?? "Generation failed.";
            }

            _populateQueued = true;
        });
    }

    public override void _Process(double delta)
    {
        if (!_populateQueued) return;
        _populateQueued = false;
        Populate();
    }

    private void Populate()
    {
        StatusLine.Text = _pendingStatus;
        UpdateGenerateEnabled();
        if (_candidates is null) return;
        for (var i = 0; i < _candidates.Count && i < PreviewRects.Count; i++)
        {
            PreviewRects[i].Texture = WorldPreview.Render(_candidates[i].World);
            CandidateButtons[i].Disabled = false;
        }
    }

    private void UseCandidate(int index)
    {
        if (_candidates is null || index >= _candidates.Count) return;
        _chosen = index;
        _useButton.Disabled = false;
        for (var i = 0; i < CandidateButtons.Count; i++)
            CandidateButtons[i].Disabled = _candidates is null || i == index;
    }

    private ResolvedScenario BuildResolved()
    {
        var candidate = _candidates![_chosen];
        var scenario = GeneratedGame.ScenarioFor(candidate.World, RulesetId, candidate.Seed);
        return new ResolvedScenario(scenario, candidate.World, RulesetById());
    }

    private Ruleset RulesetById() =>
        GameDataContext.Repository.RulesetById(RulesetId)
        ?? throw new InvalidOperationException($"the shipped data set has no ruleset '{RulesetId}'.");
}
