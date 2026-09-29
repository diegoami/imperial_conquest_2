using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Assets;

namespace IC2.Slice.UI;

/// <summary>
/// The main game screen's map — <c>docs/game-design.md</c> §"User interface" item 2: "directly extends
/// the existing MapViewer (Godot Control, zoom/pan/click-to-select already built) — rendering isn't
/// replaced, just given commands instead of being read-only." <strong>What is actually extended is
/// <c>Slice.cs</c>'s drawing (the same engine-state-driven terrain/city/army rendering, through the same
/// <see cref="World"/>/<see cref="GameSession.State"/> and <see cref="AssetPackTextureLoader"/> T48
/// already wired), not <c>MapViewer.cs</c> itself</strong> — see this class's own remarks for why.
/// </summary>
/// <remarks>
/// <para>
/// <c>MapViewer.cs</c> (T02's research inspector) reads the <em>original</em> <c>.DAT</c>/<c>.sav</c>
/// binary format directly through <c>IC2.Data</c> (<c>WorldPrefix</c>, <c>SaveArmyTable</c>, ...) and
/// never touches <see cref="IC2.Engine.Model.GameState"/> at all — <c>Slice.cs</c>'s own class remarks
/// say this explicitly ("what is beside this scene is the research inspector ... and never touches the
/// engine at all"). There is no engine-state seam in <c>MapViewer.cs</c> to extend: its <c>_Draw</c>,
/// <c>SelectAt</c> and pan/zoom math all key off <c>IC2.Data</c>'s own record types. This class instead
/// reuses <c>MapViewer.cs</c>'s own <em>mechanics</em> (the zoom/pan formulas, click-to-select) verbatim
/// in shape, and <c>Slice.cs</c>'s own <em>data source</em> (terrain via <see cref="World.Terrain"/>,
/// markers via <see cref="GameSession.State"/>, icons via <see cref="AssetPackTextureLoader"/>) — the
/// "layout intent, not its markup" <c>docs/tasks/T24.md</c> asks for, applied to the two most relevant
/// prior classes rather than either one wholesale.
/// </para>
/// <para>
/// <strong>Given commands, not merely read.</strong> <see cref="BeginMoveOrder"/> arms this view so the
/// next map click issues a real <c>move &lt;army&gt; &lt;x&gt; &lt;y&gt;</c> through
/// <see cref="GameSession.Submit"/> — never a direct mutation of <see cref="GameSession.State"/>.
/// </para>
/// <para>
/// <strong>T94: markers draw the pack's confirmed size tiers.</strong> Every city/army/fleet marker's
/// icon key comes from <see cref="MapMarkerKeys"/> — the <em>live</em> capital
/// (<see cref="NationState.CapitalCityId"/>) or population tier for cities, the original's own
/// 25,000/50,000 troop bands and 25/50 ship bands for armies and fleets — replacing T48's
/// <c>[designed]</c> per-unit-type army icon choice (folded
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/454">#454</see> item 3). When the
/// loader cannot resolve a key, the pre-T94 coloured shape remains the fallback, exactly as before.
/// </para>
/// </remarks>
public partial class GameMapView : Control
{
    private const float BaseTileSize = 6f;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 12f;
    private const float DragThreshold = 4f;

    private static readonly Color BackgroundColor = new(0.05f, 0.08f, 0.11f);
    private static readonly Color GridColor = new(0f, 0f, 0f, 0.15f);
    private static readonly Color CityRingColor = new(1.0f, 0.97f, 0.86f);
    private static readonly Color SelectedRingColor = new(0.95f, 0.78f, 0.35f);
    private static readonly Color UnknownTerrainColor = new(0.24f, 0.24f, 0.24f);
    private static readonly Color UnknownNationColor = new(0.6f, 0.6f, 0.6f);

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

    /// <summary>Fired when a city is clicked (and no move/attack order is pending).</summary>
    public event Action<string>? CitySelected;

    public event Action<string>? ArmySelected;

    public event Action<string>? FleetSelected;

    public event Action? SelectionCleared;

    /// <summary>Fired after a pending move/attack order is issued, with the session's own output lines.</summary>
    public event Action<IReadOnlyList<string>>? CommandIssued;

    public bool ShowCities = true;
    public bool ShowArmies = true;
    public bool ShowFleets = true;

    private GameSession? _session;
    private AssetPackTextureLoader? _assetLoader;
    private float _zoom = 1f;
    private Vector2 _pan = Vector2.Zero;
    private Vector2 _pressPosition;
    private bool _dragging;
    private bool _dragMoved;
    private ImageTexture? _terrainTexture;

    private string? _selectedCityId;
    private string? _selectedArmyId;
    private string? _selectedFleetId;

    private PendingMapAction _pendingAction = PendingMapAction.None;
    private string? _pendingActorId;
    private bool _fittedOnce;

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;

        // User visual review (Q-B): the baked terrain image is one pixel per world tile
        // (BakeTerrainTexture), then stretched by DrawTextureRect up to MaxZoom (12x) -- Godot's default
        // canvas texture filter is linear, which blurred that upscale badly at higher zoom. Nearest-
        // neighbour keeps each tile a crisp block instead. Set only on this CanvasItem (never
        // project.godot's project-wide default), so no other screen's text/UI is affected.
        TextureFilter = TextureFilterEnum.Nearest;

        // A container (HSplitContainer) only assigns this control its real Size on its own layout pass,
        // which has not necessarily run yet the moment Attach() is called from MainGameScreen._Ready --
        // this control's own Size can still read (0,0) at that point. Refitting once the first non-zero
        // Resized fires closes that gap without re-fitting (and so discarding the player's own zoom/pan)
        // on every later resize too.
        Resized += OnResized;
    }

    private void OnResized()
    {
        if (_fittedOnce || _session is null || Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        FitToView();
        _fittedOnce = true;
        QueueRedraw();
    }

    /// <summary>Loads (or reloads) this view against a fresh session — called once by
    /// <see cref="MainGameScreen"/> after "Start Game".</summary>
    public void Attach(GameSession session, string repositoryRoot)
    {
        _session = session;
        _assetLoader ??= AssetPackTextureLoader.TryLoadPlaceholderPack(
            repositoryRoot,
            onFailure: key => GD.PushWarning($"T24 map: asset pack could not resolve or load '{key}'; falling back to the coloured marker."));

        BakeTerrainTexture();
        if (Size.X > 0 && Size.Y > 0)
        {
            FitToView();
            _fittedOnce = true;
        }

        QueueRedraw();
    }

    /// <summary>Re-renders against the session's current state — called after every command that mutates it.</summary>
    public void Refresh() => QueueRedraw();

    public void ClearSelection()
    {
        _selectedCityId = null;
        _selectedArmyId = null;
        _selectedFleetId = null;
        SelectionCleared?.Invoke();
        QueueRedraw();
    }

    /// <summary>Arms the map so the next click issues <c>move &lt;armyId&gt; &lt;x&gt; &lt;y&gt;</c>.</summary>
    public void BeginMoveOrder(string armyId)
    {
        _pendingAction = PendingMapAction.MoveArmy;
        _pendingActorId = armyId;
    }

    /// <summary>Arms the map so the next click on an enemy army/fleet/city issues an attack order.</summary>
    public void BeginAttackOrder(string attackerArmyId)
    {
        _pendingAction = PendingMapAction.AttackWithArmy;
        _pendingActorId = attackerArmyId;
    }

    public void CancelPendingAction() => _pendingAction = PendingMapAction.None;

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

    private void FitToView()
    {
        if (_session is null)
        {
            return;
        }

        var world = _session.World;
        var viewport = Size;
        if (viewport.X <= 0 || viewport.Y <= 0)
        {
            _zoom = 1f;
            _pan = Vector2.Zero;
            return;
        }

        var mapSize = new Vector2(world.Width, world.Height) * BaseTileSize;
        _zoom = Mathf.Clamp(Mathf.Min(viewport.X / mapSize.X, viewport.Y / mapSize.Y), MinZoom, MaxZoom);
        var scaledSize = mapSize * _zoom;
        _pan = (viewport - scaledSize) / 2f;
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } wheelUp:
                Zoom(wheelUp.Position, 1.15f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } wheelDown:
                Zoom(wheelDown.Position, 1f / 1.15f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } pressed:
                _dragging = true;
                _dragMoved = false;
                _pressPosition = pressed.Position;
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } released:
                _dragging = false;
                if (!_dragMoved)
                {
                    HandleClick(released.Position);
                }

                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                _pan += motion.Relative;
                if (motion.Relative.Length() > DragThreshold)
                {
                    _dragMoved = true;
                }

                QueueRedraw();
                AcceptEvent();
                break;
        }
    }

    private void Zoom(Vector2 cursor, float factor)
    {
        var nextZoom = Mathf.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (Mathf.Abs(nextZoom - _zoom) < 0.0001f)
        {
            return;
        }

        var normalized = (cursor - _pan) / _zoom;
        _zoom = nextZoom;
        _pan = cursor - normalized * _zoom;
        QueueRedraw();
    }

    private void HandleClick(Vector2 screenPosition)
    {
        if (_session is null)
        {
            return;
        }

        var tile = (screenPosition - _pan) / (_zoom * BaseTileSize);
        var x = Mathf.FloorToInt(tile.X);
        var y = Mathf.FloorToInt(tile.Y);

        var city = FindCityAt(x, y);
        var army = FindArmyAt(x, y);
        var fleet = FindFleetAt(x, y);

        if (_pendingAction != PendingMapAction.None)
        {
            ResolvePendingAction(x, y, city, army, fleet);
            return;
        }

        if (army is not null)
        {
            SelectArmy(army.Id);
        }
        else if (fleet is not null)
        {
            SelectFleet(fleet.Id);
        }
        else if (city is not null)
        {
            SelectCity(city.Id);
        }
        else
        {
            ClearSelection();
        }
    }

    private void ResolvePendingAction(int x, int y, CityState? city, ArmyState? army, FleetState? fleet)
    {
        if (_session is null || _pendingActorId is null)
        {
            _pendingAction = PendingMapAction.None;
            return;
        }

        var actorId = _pendingActorId;
        var action = _pendingAction;
        _pendingAction = PendingMapAction.None;
        _pendingActorId = null;

        IReadOnlyList<string> lines;
        switch (action)
        {
            case PendingMapAction.MoveArmy:
                lines = _session.Submit($"move {actorId} {x} {y}").Lines;
                break;
            case PendingMapAction.AttackWithArmy when army is not null:
                lines = _session.Submit($"attack-army {actorId} {army.Id}").Lines;
                break;
            case PendingMapAction.AttackWithArmy when city is not null:
                lines = _session.Submit($"besiege-city {actorId} {city.Id}").Lines;
                break;
            default:
                lines = new[] { "No valid target at that tile." };
                break;
        }

        CommandIssued?.Invoke(lines);
        SelectArmy(actorId);
        QueueRedraw();
    }

    private void SelectCity(string cityId)
    {
        _selectedCityId = cityId;
        _selectedArmyId = null;
        _selectedFleetId = null;
        CitySelected?.Invoke(cityId);
        QueueRedraw();
    }

    private void SelectArmy(string armyId)
    {
        _selectedArmyId = armyId;
        _selectedCityId = null;
        _selectedFleetId = null;
        ArmySelected?.Invoke(armyId);
        QueueRedraw();
    }

    private void SelectFleet(string fleetId)
    {
        _selectedFleetId = fleetId;
        _selectedCityId = null;
        _selectedArmyId = null;
        FleetSelected?.Invoke(fleetId);
        QueueRedraw();
    }

    private CityState? FindCityAt(int x, int y) =>
        _session?.State.Cities.FirstOrDefault(c => c.X == x && c.Y == y);

    private ArmyState? FindArmyAt(int x, int y) =>
        _session?.State.Armies.FirstOrDefault(a => a.X == x && a.Y == y);

    private FleetState? FindFleetAt(int x, int y) =>
        _session?.State.Fleets.FirstOrDefault(f => f.X == x && f.Y == y && !f.IsUnderConstruction);

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), BackgroundColor);

        if (_session is null)
        {
            return;
        }

        var world = _session.World;
        var tileSize = BaseTileSize * _zoom;

        if (_terrainTexture is not null)
        {
            var mapRect = new Rect2(_pan, new Vector2(world.Width, world.Height) * tileSize);
            DrawTextureRect(_terrainTexture, mapRect, false);
        }

        if (ShowCities)
        {
            foreach (var city in _session.State.Cities)
            {
                DrawCity(city, tileSize);
            }
        }

        if (ShowArmies)
        {
            foreach (var army in _session.State.Armies)
            {
                DrawArmy(army, tileSize);
            }
        }

        if (ShowFleets)
        {
            foreach (var fleet in _session.State.Fleets.Where(f => !f.IsUnderConstruction))
            {
                DrawFleet(fleet, tileSize);
            }
        }
    }

    private Vector2 TileCenter(int x, int y, float tileSize) =>
        _pan + new Vector2((x + 0.5f) * tileSize, (y + 0.5f) * tileSize);

    private void DrawCity(CityState city, float tileSize)
    {
        var center = TileCenter(city.X, city.Y, tileSize);
        var fillColor = NationColor(city.Owner);
        var radius = Mathf.Max(tileSize * 0.55f, 3f);
        var selected = string.Equals(city.Id, _selectedCityId, StringComparison.Ordinal);

        DrawCircle(center, radius + 1.5f, new Color(0f, 0f, 0f, 0.55f));

        // T94: key selection (live capital vs. population tier) is MapMarkerKeys' single decision,
        // shared with Slice.cs -- of game-design.md's [open] "City markers" section, only capital
        // status (orthogonal to population, live on NationState.CapitalCityId) is confirmed; the
        // tier boundaries are the ruleset's, and every shipped ruleset ships them empty.
        var texture = _assetLoader?.TryGetTexture(MapMarkerKeys.CityIcon(_session!.State, city, _session.Ruleset.MapMarkers));
        if (texture is not null)
        {
            var rect = new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2f);
            DrawTextureRect(texture, rect, false);
            DrawArc(center, radius, 0f, Mathf.Tau, 24, selected ? SelectedRingColor : fillColor, selected ? 2.5f : 1.5f);
            return;
        }

        DrawCircle(center, radius, fillColor);
        DrawArc(center, radius, 0f, Mathf.Tau, 24, selected ? SelectedRingColor : CityRingColor, selected ? 2.5f : 1.2f);
    }

    private void DrawArmy(ArmyState army, float tileSize)
    {
        if (army.AboardFleetId is not null)
        {
            return;
        }

        var center = TileCenter(army.X, army.Y, tileSize);
        var fillColor = NationColor(army.Nation);
        var half = Mathf.Max(tileSize * 0.4f, 2.5f);
        var selected = string.Equals(army.Id, _selectedArmyId, StringComparison.Ordinal);

        // T94 (#454 item 3): the confirmed three-tier size marker -- MapMarkerKeys reads the ruleset's
        // own 25,000/50,000 boundaries, never T48's [designed] per-unit-type plurality.
        var texture = _assetLoader?.TryGetTexture(MapMarkerKeys.ArmyIcon(army, _session!.Ruleset.MapMarkers));
        if (texture is not null)
        {
            DrawCircle(center, half + 2f, new Color(0f, 0f, 0f, 0.55f));
            var rect = new Rect2(center - new Vector2(half, half), new Vector2(half, half) * 2f);
            DrawTextureRect(texture, rect, false);
            DrawArc(center, half + 2f, 0f, Mathf.Tau, 24, selected ? SelectedRingColor : fillColor, selected ? 2.5f : 1.5f);
            return;
        }

        var points = new[]
        {
            center + new Vector2(0, -half),
            center + new Vector2(half, 0),
            center + new Vector2(0, half),
            center + new Vector2(-half, 0),
        };

        DrawColoredPolygon(points, fillColor);
        var ringColor = selected ? SelectedRingColor : CityRingColor;
        for (var i = 0; i < points.Length; i++)
        {
            DrawLine(points[i], points[(i + 1) % points.Length], ringColor, selected ? 2f : 1f);
        }
    }

    private void DrawFleet(FleetState fleet, float tileSize)
    {
        var center = TileCenter(fleet.X, fleet.Y, tileSize);
        var fillColor = NationColor(fleet.Nation);
        var radius = Mathf.Max(tileSize * 0.35f, 2f);
        var selected = string.Equals(fleet.Id, _selectedFleetId, StringComparison.Ordinal);

        // T94: the confirmed three-tier fleet marker (ruleset's own 25/50 ship boundaries) -- the
        // fleet equivalent of DrawArmy's tier icon.
        var texture = _assetLoader?.TryGetTexture(MapMarkerKeys.FleetIcon(fleet, _session!.Ruleset.MapMarkers));
        if (texture is not null)
        {
            var half = radius + 1.5f;
            DrawCircle(center, half, new Color(0f, 0f, 0f, 0.55f));
            var rect = new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2f);
            DrawTextureRect(texture, rect, false);
            DrawRect(rect, selected ? SelectedRingColor : fillColor, false, selected ? 2.5f : 1.5f);
            return;
        }

        var fallbackRect = new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2f);
        DrawRect(fallbackRect, fillColor);
        DrawRect(fallbackRect, selected ? SelectedRingColor : CityRingColor, false, selected ? 2f : 1f);
    }

    private Color NationColor(string nationId)
    {
        var nation = _session?.World.NationById(nationId);
        return nation is not null && ColorFromHex(nation.ColorHex, out var parsed) ? parsed : UnknownNationColor;
    }

    private static bool ColorFromHex(string? hex, out Color color)
    {
        color = UnknownNationColor;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        try
        {
            color = new Color(hex);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private enum PendingMapAction
    {
        None,
        MoveArmy,
        AttackWithArmy,
    }
}
