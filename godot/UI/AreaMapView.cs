using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Assets;

namespace IC2.Slice.UI;

/// <summary>
/// The overview mini-map — <c>docs/game-design.md</c>'s Screen/flow "Overview mini-map" bullet: the
/// original's Area map, "a small picture of the whole world. A click on it re-centres the map
/// [confirmed: <c>2026-09-29-nation-view-origin-and-unit-map-clicks.md</c>], and a rectangle shows the
/// map's current view." It takes no orders: the only thing a click does is re-centre the order map
/// (<see cref="GameMapView.CentreOnTile"/>), exactly as the audit's
/// <c>TAreaMap_AreaMapClick</c> → <c>TUnitMap_AreaMapClicked</c> path does
/// (<c>docs/investigations/original-ui-command-audit.md</c> §1.5).
/// </summary>
/// <remarks>
/// <para>
/// The original's Area map is a separate window; making it an embedded panel is the user's decision of
/// 2026-10-01 (one order map plus a small overview), so this is a <see cref="Control"/>, never a second
/// <c>Window</c>. It sits at the top of the right-hand column, above the context panel — the placement
/// is <strong>[designed]</strong> (the entry's Scope), chosen because the right column already holds the
/// context panel and its 340&#160;px floor, so a 320&#160;px overview widens nothing.
/// </para>
/// <para>
/// The terrain is baked once into one image at one pixel per world tile, then drawn scaled to the
/// mini-map's own pixel size; only the view rectangle is redrawn on a <see cref="GameMapView.ViewChanged"/>
/// event. A per-frame redraw of 44,800 tiles is exactly what the task entry's hazards forbid. The
/// terrain palette deliberately mirrors <see cref="GameMapView"/>'s own private one (that class's Owns
/// in this task is "a public centre-on-tile method, the visible tile rectangle, and a view-changed event
/// only", so the table is not lifted out of it); both are the placeholder palette the map already drew
/// before an asset pack tinted anything.
/// </para>
/// </remarks>
public partial class AreaMapView : Control
{
    private static readonly Color ViewRectColor = new(1f, 0.9f, 0.3f);
    private static readonly Color BorderColor = new(0f, 0f, 0f, 0.65f);

    /// <summary>The tile colour for a terrain name the map does not know (the tile-type list is open).</summary>
    private static readonly Color UnknownTerrainColor = new(0.24f, 0.24f, 0.24f);

    private static readonly Dictionary<string, Color> TerrainColorsByName =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sea"] = new Color(0.06f, 0.16f, 0.40f),
            ["Plain"] = new Color(0.30f, 0.42f, 0.18f),
            ["Desert"] = new Color(0.62f, 0.52f, 0.28f),
            ["Forest"] = new Color(0.10f, 0.28f, 0.14f),
            ["Mountains"] = new Color(0.42f, 0.38f, 0.36f),
            ["River"] = new Color(0.14f, 0.46f, 0.70f),
        };

    /// <summary>
    /// The fill and outline of one highlighted tile — the Area map's Show layers (T110).
    /// <strong>[designed]</strong>: the audit's §1.5 table names the four Show functions and their
    /// addresses but does not transcribe the original's highlight colour, and no report or investigation
    /// carries a colour for the Area map's markers. Searched <c>docs/investigations/</c> for
    /// "Area map" highlight/marker colour and "TAreaMap_Show" pixels; empty. Amber over the terrain is
    /// this task's own choice.
    /// </summary>
    private static readonly Color HighlightFillColor = new(1f, 0.85f, 0.2f, 0.45f);
    private static readonly Color HighlightLineColor = new(1f, 0.85f, 0.2f, 0.95f);

    /// <summary>
    /// The Area-map strip's rows, in display order: the five Show entries, then the six Show
    /// mercenaries entries (disabled until T113), then Find a city — the audit's §1.5 table order. Six
    /// columns keep the strip inside the mini-map's own 320&#160;px (and the context panel's 340&#160;px
    /// floor), so it can never widen the 1500&#160;px design viewport (the task entry's rule).
    /// </summary>
    private static readonly string[] StripOrder =
    {
        "area_map.show_cities",
        "area_map.show_capital",
        "area_map.show_armies",
        "area_map.show_fleets",
        "area_map.show_all",
        "area_map.show_mercs_light_infantry",
        "area_map.show_mercs_heavy_infantry",
        "area_map.show_mercs_archers",
        "area_map.show_mercs_light_cavalry",
        "area_map.show_mercs_heavy_cavalry",
        "area_map.show_mercs_all",
        "area_map.find_city",
    };

    /// <summary>
    /// The strip wraps at six columns. <strong>[designed]</strong>: the audit's §1.5 lists the entries
    /// and §3.2 lists the original's 13 buttons as one sequence, with no column count (and marks only
    /// the gold-coin button <c>[open]</c>); searched <c>docs/investigations/</c> for the Area-map toolbar
    /// layout / column count and found none. Six columns keep the strip inside the mini-map's own
    /// 320&#160;px (and the context panel's 340&#160;px floor).
    /// </summary>
    private const int StripColumns = 6;

    /// <summary>The one command table the strip's buttons run, the same one the menus and toolbar run.</summary>
    public required GameCommandTable Table { get; init; }

    /// <summary>The pack loader for the strip's <c>ui.command.*.icon</c> textures, the same one the main
    /// toolbar uses.</summary>
    public required AssetPackTextureLoader? AssetLoader { get; init; }

    private GameSession? _session;
    private GameMapView? _orderMap;
    private AreaMapGeometry? _geometry;
    private ImageTexture? _terrainTexture;
    private AreaMapPixelRect _viewRect;

    /// <summary>The viewed nation's highlights that are on — empty until a Show entry is pressed.</summary>
    private readonly HashSet<AreaMapHighlightKind> _activeHighlights = new();

    /// <summary>The viewed nation, or <see langword="null"/> for the original's All nations selection.</summary>
    private string? _viewedNationId;

    /// <summary>The city Find a city last chose, if any — highlighted independently of the Show layers.</summary>
    private string? _findCityId;

    private readonly Dictionary<string, Button> _stripButtons = new(StringComparer.Ordinal);
    private bool _hintsEnabled = true;

    /// <summary>How many times <see cref="_Draw"/> has painted. Exposed so <c>NationsAreaMapCheck</c>
    /// can prove a command re-queued a redraw (the highlight layer must not go stale).</summary>
    private int _drawCount;

    /// <summary>The highlight tiles the last <see cref="_Draw"/> actually painted — the drawn set, as
    /// opposed to <see cref="HighlightTilesForCheck"/>'s live set.</summary>
    private IReadOnlySet<(int X, int Y)> _lastDrawnHighlights = new HashSet<(int X, int Y)>();

    /// <summary>The geometry this mini-map is currently laid out with — exposed so the headless
    /// <c>godot/Checks/AreaMapCheck.cs</c> can send its click at a real tile's mini-map pixel.</summary>
    public AreaMapGeometry GeometryForCheck =>
        _geometry ?? throw new InvalidOperationException("AreaMapView.Attach has not run.");

    /// <summary>
    /// The mini-map's current view rectangle, in its own pixels, updated on every
    /// <see cref="GameMapView.ViewChanged"/> — exposed so <c>godot/Checks/AreaMapCheck.cs</c> can assert
    /// it equals <see cref="GameMapView.VisibleTileRect"/> mapped through <see cref="AreaMapGeometry"/>.
    /// </summary>
    public AreaMapPixelRect ViewRectForCheck => _viewRect;

    /// <summary>Whether the terrain image has been baked — the check's precondition for the view
    /// rectangle being meaningful.</summary>
    public bool HasTerrainForCheck => _terrainTexture is not null;

    /// <summary>How many times this mini-map has painted. <c>NationsAreaMapCheck</c> reads it before and
    /// after a command to prove the highlights were re-queued rather than left stale.</summary>
    public int DrawCountForCheck => _drawCount;

    /// <summary>The highlight tiles the last paint actually drew. Comparing this with
    /// <see cref="HighlightTilesForCheck"/> is what catches a stale layer: the live set can move while
    /// the painted pixels still show the old tiles.</summary>
    public IReadOnlySet<(int X, int Y)> LastDrawnHighlightTilesForCheck => _lastDrawnHighlights;

    /// <summary>The strip button for <paramref name="commandId"/>, or <see langword="null"/> when the
    /// strip does not carry it — exposed so <c>NationsAreaMapCheck</c> can click the real button.</summary>
    public Button? StripButtonForCheck(string commandId) =>
        _stripButtons.TryGetValue(commandId, out var button) ? button : null;

    /// <summary>
    /// Re-queues a redraw so the highlight layer reflects the current state. <see cref="MainGameScreen"/>
    /// calls this after <em>every</em> issued command: a move, a capture, an "end" all change which
    /// tiles the active Show layers mark, and without this the painted pixels keep the old positions
    /// until something else happens to redraw.
    /// </summary>
    public void Refresh() => QueueRedraw();

    /// <summary>
    /// Builds the Area-map toolbar strip — the row of <c>ui.command.*</c> buttons above the mini-map
    /// (the task entry's Scope): the five Show entries, the six Show mercenaries entries (disabled until
    /// T113) and Find a city. Every button runs its own <see cref="GameCommandTable"/> row, so the strip
    /// and the Area map menu are one path, and it is icon-only with the caption as its tooltip, exactly
    /// like <c>CommandToolbar</c>. Called by <see cref="MainGameScreen"/>, which adds the returned
    /// control to the side column <em>above</em> this mini-map (so this control's own drawing still
    /// starts at its top-left and T102's click arithmetic is unchanged).
    /// </summary>
    public Control BuildStrip()
    {
        var grid = new GridContainer { Columns = StripColumns, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);

        foreach (var id in StripOrder)
        {
            if (GameCommandTable.RowById(id) is not { } row)
            {
                continue;
            }

            var button = BuildStripButton(row);
            _stripButtons[id] = button;
            grid.AddChild(button);
        }

        return grid;
    }

    /// <summary>
    /// Sets the viewed nation, or <see langword="null"/> for the original's All nations selection. The
    /// active highlights keep their on/off state and simply re-scope (Scope: "Changing the viewed nation
    /// re-scopes the highlights that are on").
    /// </summary>
    public void SetViewedNation(string? nationId)
    {
        _viewedNationId = nationId;
        QueueRedraw();
    }

    /// <summary>The city Find a city last chose, or <see langword="null"/> when none has been chosen.</summary>
    public string? FindCityIdForCheck => _findCityId;

    /// <summary>The viewed nation the highlights are scoped to; <see langword="null"/> is All nations.</summary>
    public string? ViewedNationIdForCheck => _viewedNationId;

    /// <summary>The highlight layers currently on — what a check reads to prove a Show entry took.</summary>
    public IReadOnlyCollection<AreaMapHighlightKind> ActiveHighlightsForCheck => _activeHighlights.ToArray();

    /// <summary>
    /// Every tile currently highlighted: the union of the active Show layers for the viewed nation, plus
    /// the tile of the city Find a city last chose. This is what <c>NationsAreaMapCheck</c> compares to
    /// <see cref="AreaMapHighlights"/>.
    /// </summary>
    public IReadOnlySet<(int X, int Y)> HighlightTilesForCheck
    {
        get
        {
            var tiles = new HashSet<(int X, int Y)>();
            if (_session is null)
            {
                return tiles;
            }

            foreach (var kind in _activeHighlights)
            {
                tiles.UnionWith(AreaMapHighlights.TilesFor(state: _session.State, kind, viewedNationId: _viewedNationId));
            }

            if (_findCityId is not null && _session.State.CityById(_findCityId) is { } city)
            {
                tiles.Add((city.X, city.Y));
            }

            return tiles;
        }
    }

    /// <summary>Turns one Show layer on or off (the Show menu items and strip toggles call this).</summary>
    public void SetHighlight(AreaMapHighlightKind kind, bool on)
    {
        if (on)
        {
            _activeHighlights.Add(kind);
        }
        else
        {
            _activeHighlights.Remove(kind);
        }

        SyncStripStates();
        QueueRedraw();
    }

    /// <summary>Toggles one Show layer — what a strip button's own toggle press and its menu item do.</summary>
    public void ToggleHighlight(AreaMapHighlightKind kind) =>
        SetHighlight(kind, !_activeHighlights.Contains(kind));

    /// <summary>
    /// Show all: turns every one of the four layers on, or (when they are already all on) off — the
    /// original's <c>TAreaMap_ShowAll</c> sets cities, capital, fleets and armies together, and the entry
    /// is a check item that turns its highlight on or off (Scope).
    /// </summary>
    public void ToggleShowAll()
    {
        var allOn = true;
        foreach (var kind in Enum.GetValues<AreaMapHighlightKind>())
        {
            allOn &= _activeHighlights.Contains(kind);
        }

        foreach (var kind in Enum.GetValues<AreaMapHighlightKind>())
        {
            if (allOn)
            {
                _activeHighlights.Remove(kind);
            }
            else
            {
                _activeHighlights.Add(kind);
            }
        }

        SyncStripStates();
        QueueRedraw();
    }

    /// <summary>Highlights the city Find a city chose, or clears it with <see langword="null"/>.</summary>
    public void SetFindCityHighlight(string? cityId)
    {
        _findCityId = cityId;
        QueueRedraw();
    }

    /// <summary>Turns the strip's tooltips on or off with the screen's one Show hints command (T100).</summary>
    public void SetHintsEnabled(bool enabled)
    {
        _hintsEnabled = enabled;
        foreach (var (id, button) in _stripButtons)
        {
            button.TooltipText = enabled ? GameCommandTable.RowById(id)?.Caption ?? string.Empty : string.Empty;
        }
    }

    private Button BuildStripButton(GameCommandRow row)
    {
        var button = new Button
        {
            Name = "AreaMap_" + row.Id.Replace('.', '_'),
            Disabled = !row.Wired,
            ToggleMode = IsShowEntry(row.Id),
            CustomMinimumSize = new Vector2(0, 30),
            TooltipText = _hintsEnabled ? row.Caption : string.Empty,
        };

        if (row.IconKey is { } iconKey && AssetLoader?.TryGetTexture(iconKey) is { } texture)
        {
            button.Icon = texture;
        }
        else
        {
            // A missing texture falls back to the caption, never an empty button (T100's convention).
            button.Text = row.Caption;
        }

        var commandId = row.Id;
        button.Pressed += () => Table.TryInvoke(commandId);
        return button;
    }

    private static bool IsShowEntry(string commandId) =>
        commandId is "area_map.show_cities"
            or "area_map.show_capital"
            or "area_map.show_armies"
            or "area_map.show_fleets"
            or "area_map.show_all";

    private static string IdFor(AreaMapHighlightKind kind) => kind switch
    {
        AreaMapHighlightKind.Cities => "area_map.show_cities",
        AreaMapHighlightKind.Capital => "area_map.show_capital",
        AreaMapHighlightKind.Armies => "area_map.show_armies",
        AreaMapHighlightKind.Fleets => "area_map.show_fleets",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private void SyncStripStates()
    {
        var allOn = true;
        foreach (var kind in Enum.GetValues<AreaMapHighlightKind>())
        {
            var on = _activeHighlights.Contains(kind);
            allOn &= on;
            if (_stripButtons.TryGetValue(IdFor(kind), out var button))
            {
                button.ButtonPressed = on;
            }
        }

        if (_stripButtons.TryGetValue("area_map.show_all", out var showAll))
        {
            showAll.ButtonPressed = allOn;
        }
    }

    public override void _Ready()
    {
        // Godot's default linear filter would blur the upscale; Nearest keeps every tile a crisp block,
        // exactly as GameMapView already does for the order map.
        TextureFilter = TextureFilterEnum.Nearest;
        ClipContents = true;
    }

    /// <summary>
    /// Loads (or reloads) this mini-map against a fresh session and its order map — called once by
    /// <see cref="MainGameScreen"/> after the order map itself has attached. It sizes itself to the
    /// whole world at one pixel or more per tile, bakes the terrain once and listens for view changes.
    /// </summary>
    public void Attach(GameSession session, GameMapView orderMap)
    {
        if (_orderMap is not null)
        {
            _orderMap.ViewChanged -= RefreshFromOrderMap;
        }

        _session = session;
        _orderMap = orderMap;
        _geometry = AreaMapGeometry.ForWorld(session.World.Width, session.World.Height);
        CustomMinimumSize = new Vector2(_geometry.PixelWidth, _geometry.PixelHeight);

        BakeTerrainTexture();
        orderMap.ViewChanged += RefreshFromOrderMap;
        RefreshFromOrderMap();
    }

    public override void _ExitTree()
    {
        if (_orderMap is not null)
        {
            _orderMap.ViewChanged -= RefreshFromOrderMap;
        }
    }

    private void RefreshFromOrderMap()
    {
        if (_geometry is null || _orderMap is null)
        {
            return;
        }

        var visible = _orderMap.VisibleTileRect;
        _viewRect = _geometry.MapTileRect(
            visible.Position.X,
            visible.Position.Y,
            visible.Position.X + visible.Size.X,
            visible.Position.Y + visible.Size.Y);
        QueueRedraw();
    }

    private void BakeTerrainTexture()
    {
        if (_session is null)
        {
            return;
        }

        var world = _session.World;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        var image = Image.CreateEmpty(world.Width, world.Height, false, Image.Format.Rgba8);
        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
            {
                var code = cells[(y * world.Width) + x];
                var tileType = world.TileTypeByCode(code);
                var color = tileType is not null && TerrainColorsByName.TryGetValue(tileType.Name, out var known)
                    ? known
                    : UnknownTerrainColor;
                image.SetPixel(x, y, color);
            }
        }

        _terrainTexture = ImageTexture.CreateFromImage(image);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_geometry is null || _orderMap is null)
        {
            return;
        }

        // The click only re-centres the order map (the original's Area-map path); it never selects and
        // never submits a command. The left button's press is ignored so a press-drag-release cannot be
        // mistaken for anything else, and the release alone is the click.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } released)
        {
            var tileX = _geometry.PixelToTileX(released.Position.X);
            var tileY = _geometry.PixelToTileY(released.Position.Y);
            if (tileX >= 0 && tileX < _geometry.WorldWidth && tileY >= 0 && tileY < _geometry.WorldHeight)
            {
                _orderMap.CentreOnTile(tileX, tileY);
            }

            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        if (_geometry is null || _terrainTexture is null)
        {
            return;
        }

        var mapRect = new Rect2(0f, 0f, _geometry.PixelWidth, _geometry.PixelHeight);
        DrawTextureRect(_terrainTexture, mapRect, false);
        DrawRect(mapRect, BorderColor, false, 1f);

        // T110: the Show layers' highlight tiles, painted over the terrain and *under* the view
        // rectangle's outline (drawn last, just below), so the rectangle stays visible on top of the
        // tiles it frames. The exact set painted is snapshotted here, and DrawCountForCheck counts the
        // paint, so a headless check can compare what was actually drawn with HighlightTilesForCheck's
        // live set and catch a stale layer.
        var highlights = HighlightTilesForCheck;
        _lastDrawnHighlights = highlights;
        _drawCount++;
        foreach (var (x, y) in highlights)
        {
            var tile = new Rect2(_geometry.TileToPixelX(x), _geometry.TileToPixelY(y), _geometry.Scale, _geometry.Scale);
            DrawRect(tile, HighlightFillColor, true);
            DrawRect(tile, HighlightLineColor, false, 1f);
        }

        var view = _viewRect;
        DrawRect(new Rect2(view.X, view.Y, view.Width, view.Height), ViewRectColor, false, 1.5f);
    }
}
