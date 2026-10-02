using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Area map's <strong>Find a city</strong> dialog — the original's <c>TFindCity</c>
/// (<c>docs/investigations/original-ui-command-audit.md</c> §1.5): "a nation dropdown (any nation) and
/// that nation's city list, with capitals marked. Choosing a city centres the Unit map on it and
/// highlights it on the Area map [derived: code, <c>TFindCity_FillListBox</c> <c>0x0043D7DC</c>,
/// <c>ChangeCity</c> <c>0x0043D93C</c>]."
/// </summary>
/// <remarks>
/// <para>
/// A plain modal overlay added by <see cref="MainGameScreen.OpenFindCityDialog"/>, not a second
/// <c>Window</c> (the design's one-screen decision). It issues no command: <see cref="CityChosen"/>
/// carries the city id back to the screen, which re-centres the order map
/// (<see cref="GameMapView.CentreOnTile"/>) and highlights the tile on the mini-map. The engine state
/// is never mutated from the dialog.
/// </para>
/// <para>
/// <strong>How a capital is marked is [open]</strong> (audit §1.5: "a string transform; probably upper
/// case"). This marks it plainly with a <c>" (capital)"</c> suffix, which needs no reading of the
/// original's string table; if the original turns out to upper-case the name, the user adjusts it.
/// </para>
/// </remarks>
public partial class FindCityDialog : Control
{
    public required GameSession Session { get; init; }

    /// <summary>The nation the dropdown opens on, or <see langword="null"/> for the first in the world
    /// order — the viewed nation at the time the dialog opens.</summary>
    public string? InitialNationId { get; init; }

    /// <summary>Raised with the chosen city's id; <see cref="MainGameScreen"/> centres and highlights it.</summary>
    public event Action<string>? CityChosen;

    /// <summary>Raised by Cancel (or Esc); <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private OptionButton _nationDropdown = null!;
    private ItemList _cityList = null!;
    private readonly List<string> _cityIds = new();

    public override void _Ready()
    {
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(420, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Find a city", 18, UiKit.AccentColor));

        _nationDropdown = new OptionButton();
        column.AddChild(_nationDropdown);

        // Any nation, the original's own dropdown (audit §1.5) — the world's own order, never filtered.
        foreach (var nation in Session.State.Nations)
        {
            _nationDropdown.AddItem(nation.Name);
            _nationDropdown.SetItemMetadata(_nationDropdown.ItemCount - 1, nation.Id);
        }

        _nationDropdown.ItemSelected += _ => FillCities();

        _cityList = new ItemList { CustomMinimumSize = new Vector2(0, 220) };
        column.AddChild(_cityList);
        _cityList.ItemActivated += _ => Choose();

        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);
        buttons.AddChild(UiKit.MakeButton("Cancel", () => Closed?.Invoke()));
        buttons.AddChild(UiKit.MakeButton("Find", Choose));

        var initialIndex = IndexOfNation(InitialNationId);
        _nationDropdown.Select(initialIndex);
        FillCities();
    }

    /// <summary>Selects a nation by id and refills the city list — a check's way to drive the real
    /// dropdown without a hardcoded pixel click.</summary>
    public void SelectNationForCheck(string nationId)
    {
        var index = IndexOfNation(nationId);
        if (index < 0)
        {
            return;
        }

        _nationDropdown.Select(index);
        FillCities();
    }

    /// <summary>Selects a city by id — a check's way to drive the real list.</summary>
    public void SelectCityForCheck(string cityId)
    {
        var index = _cityIds.IndexOf(cityId);
        if (index >= 0)
        {
            _cityList.Select(index);
        }
    }

    /// <summary>Chooses the selected city exactly as the Find button does.</summary>
    public void ChooseForCheck() => Choose();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Closed?.Invoke();
            GetViewport().SetInputAsHandled();
        }
    }

    private void FillCities()
    {
        var nationId = SelectedNationId();
        var capitalId = nationId is null ? null : Session.State.NationById(nationId)?.CapitalCityId;

        _cityIds.Clear();
        _cityList.Clear();
        foreach (var city in Session.State.Cities)
        {
            if (!string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            {
                continue;
            }

            _cityList.AddItem(string.Equals(city.Id, capitalId, StringComparison.Ordinal)
                ? city.Name + " (capital)"
                : city.Name);
            _cityIds.Add(city.Id);
        }

        if (_cityIds.Count > 0)
        {
            _cityList.Select(0);
        }
    }

    private string? SelectedNationId()
    {
        var index = _nationDropdown.Selected;
        return index < 0 ? null : _nationDropdown.GetItemMetadata(index).AsString();
    }

    private int IndexOfNation(string? nationId)
    {
        for (var i = 0; i < _nationDropdown.ItemCount; i++)
        {
            if (string.Equals(_nationDropdown.GetItemMetadata(i).AsString(), nationId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return _nationDropdown.ItemCount > 0 ? 0 : -1;
    }

    private void Choose()
    {
        var selected = _cityList.GetSelectedItems();
        if (selected.Length == 0)
        {
            return;
        }

        CityChosen?.Invoke(_cityIds[selected[0]]);
    }
}
