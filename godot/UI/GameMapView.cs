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
/// <para>
/// <strong>T94 rework round 1 (N3, the user's decision of 2026-09-29): the icon is tinted with its
/// owner's colour.</strong> One bitmap otherwise draws every nation identically, where the pre-T94
/// shapes were filled in the owner's colour. The <c>modulate</c> argument of
/// <see cref="CanvasItem.DrawTextureRect"/> now carries <see cref="NationColor"/> — built from
/// <see cref="MarkerTint.ForOwner"/>, the world's own <c>colorHex</c> — so the owner's colour is the
/// icon itself. The placeholder pack's flat squares are tinted as they are (not special-cased), and
/// T51's neutral silhouettes tint the same way with no further code change.
/// </para>
/// <para>
/// <strong>Marker style A (bug
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see> rework round 1,
/// the user's visual review of 2026-09-29): the icon sits on a dark square.</strong> The tinted icon
/// alone, inside a round dark halo with a thin owner-coloured ring, read as a coloured blob at normal
/// zoom. The original game's look, chosen on the PR, is drawn instead: a small near-black, mostly
/// opaque <see cref="MarkerBackdropColor"/> square at the icon's own size, the owner-tinted silhouette
/// on top, and no owner-coloured ring at all. A selected marker gets a bright
/// <see cref="SelectedRingColor"/> outline around that square instead — see
/// <see cref="DrawCity"/>, <see cref="DrawArmy"/> and <see cref="DrawFleet"/>. The no-texture fallback
/// shapes (filled in the owner's colour and ringed as before) are unchanged.
/// </para>
/// </remarks>
public partial class GameMapView : Control
{
    private const float BaseTileSize = 6f;
    private const float MinZoom = 0.5f;
    private const float DragThreshold = 4f;

    /// <summary>The largest zoom factor <see cref="Zoom"/> clamps to — exposed so
    /// <c>godot/Checks/MapClipCheck.cs</c> can assert the maximum-zoom case without restating the
    /// constant.</summary>
    public const float MaxZoom = 12f;

    private static readonly Color BackgroundColor = new(0.05f, 0.08f, 0.11f);
    private static readonly Color GridColor = new(0f, 0f, 0f, 0.15f);
    private static readonly Color CityRingColor = new(1.0f, 0.97f, 0.86f);
    private static readonly Color SelectedRingColor = new(0.95f, 0.78f, 0.35f);

    /// <summary>Marker style A's dark square behind every pack-textured city, army and fleet marker
    /// (bug <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see> rework
    /// round 1): near-black and mostly opaque, the icon's own size, so the owner-tinted silhouette on
    /// top keeps contrast against any terrain. The user chose this, the original game's look, on the
    /// PR after the round halo and thin owner-coloured ring read as a coloured blob at normal zoom.</summary>
    private static readonly Color MarkerBackdropColor = new(0.02f, 0.02f, 0.03f, 0.85f);
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

        // Bug #498 part 1: _Draw draws the terrain texture and every marker at _pan + tile * tileSize
        // with no bounds of its own, so zoomed in the drawing spilled over the sibling controls
        // MainGameScreen builds -- the top bar (Save, End Turn) and the bottom toolbar -- leaving the
        // player unable to end a turn or save while zoomed in. ClipContents keeps every draw inside this
        // control's own rect, the rect MainGameScreen's own HBoxContainer lays out between those bars.
        ClipContents = true;

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
    /// <remarks>
    /// Bug <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see>: the pack
    /// this scene's one loader resolves is <see cref="SettingsScreen.SelectedPackId"/>, read here when
    /// the scene attaches — so a selection made in Settings applies to the next game started or
    /// loaded, while the game already on screen keeps the pack it began with (T94 folded #454 item 6).
    /// A <see langword="null"/> selection keeps <see cref="AssetPackTextureLoader.TryLoadPack"/>'s own
    /// default (<c>authored</c> when present and valid, otherwise <c>placeholder</c>), and a selected
    /// pack that is missing or malformed falls back to the placeholder, reported once and never
    /// thrown. <see cref="MainGameScreen"/> owns the attach call and is the screen that knows the
    /// repository root; reading the selection here keeps this one seam instead of adding a property to
    /// a file this fix does not own.
    /// </remarks>
    public void Attach(GameSession session, string repositoryRoot)
    {
        _session = session;
        _assetLoader ??= AssetPackTextureLoader.TryLoadPack(
            repositoryRoot,
            SettingsScreen.SelectedPackId,
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

    /// <summary>The id of the pack this view's one loader resolved — exposed so
    /// <c>godot/Checks/AssetPackSelectionCheck.cs</c> can assert the Settings selection really reaches
    /// the map. <see langword="null"/> before <see cref="Attach"/>, or when no pack (not even the
    /// placeholder) could be loaded.</summary>
    public string? LoadedPackIdForCheck => _assetLoader?.PackId;

    /// <summary>The current zoom factor — exposed for <c>godot/Checks/MapClipCheck.cs</c>, which drives
    /// this view to <see cref="MaxZoom"/> and asserts the drawing stays inside its own rect.</summary>
    public float ZoomFactor => _zoom;

    /// <summary>Zooms to <see cref="MaxZoom"/> through the same <see cref="Zoom"/> path the mouse wheel
    /// uses, so <c>godot/Checks/MapClipCheck.cs</c> reproduces the maximum-zoom case without simulating
    /// wheel events.</summary>
    public void ZoomToMaxForCheck()
    {
        if (Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        Zoom(Size / 2f, MaxZoom / _zoom);
    }

    /// <summary>The rect <see cref="_Draw"/> would draw the terrain texture in at the current zoom and
    /// pan — exposed so <c>godot/Checks/MapClipCheck.cs</c> can prove the maximum-zoom case really does
    /// draw past this control's own rect (the bug's precondition), not merely that clipping is enabled.</summary>
    public Rect2 TerrainDrawRectForCheck => _session is null
        ? new Rect2(_pan, Vector2.Zero)
        : new Rect2(_pan, new Vector2(_session.World.Width, _session.World.Height) * BaseTileSize * _zoom);

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

        // T94: key selection (live capital vs. population tier) is MapMarkerKeys' single decision,
        // shared with Slice.cs -- of game-design.md's [open] "City markers" section, only capital
        // status (orthogonal to population, live on NationState.CapitalCityId) is confirmed; the
        // tier boundaries are the ruleset's, and every shipped ruleset ships them empty.
        var texture = _assetLoader?.TryGetTexture(MapMarkerKeys.CityIcon(_session!.State, city, _session.Ruleset.MapMarkers));
        if (texture is not null)
        {
            // Marker style A (#517 rework round 1): a dark square at the icon's size, the owner-tinted
            // silhouette on top, and no owner-coloured ring -- only a selected marker is outlined.
            var rect = new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2f);
            DrawRect(rect, MarkerBackdropColor);
            DrawTextureRect(texture, rect, false, fillColor);
            if (selected)
            {
                DrawRect(rect, SelectedRingColor, false, 2.5f);
            }

            return;
        }

        // No texture (or no loader, bug #517): the pre-T94 shape is unchanged -- the round dark halo
        // behind the owner-coloured disc stays exactly as it was.
        DrawCircle(center, radius + 1.5f, new Color(0f, 0f, 0f, 0.55f));
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
            // N3: the owner's colour is the icon's modulate tint (see this class's remarks).
            // Marker style A (#517 rework round 1): a dark square at the icon's size, the tinted
            // silhouette on top, and no owner-coloured ring -- only a selected marker is outlined.
            var rect = new Rect2(center - new Vector2(half, half), new Vector2(half, half) * 2f);
            DrawRect(rect, MarkerBackdropColor);
            DrawTextureRect(texture, rect, false, fillColor);
            if (selected)
            {
                DrawRect(rect, SelectedRingColor, false, 2.5f);
            }

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
            // N3: the owner's colour is the icon's modulate tint (see this class's remarks).
            // Marker style A (#517 rework round 1): a dark square at the icon's size, the tinted
            // silhouette on top, and no owner-coloured ring -- only a selected marker is outlined.
            var rect = new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2f);
            DrawRect(rect, MarkerBackdropColor);
            DrawTextureRect(texture, rect, false, fillColor);
            if (selected)
            {
                DrawRect(rect, SelectedRingColor, false, 2.5f);
            }

            return;
        }

        var fallbackRect = new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2f);
        DrawRect(fallbackRect, fillColor);
        DrawRect(fallbackRect, selected ? SelectedRingColor : CityRingColor, false, selected ? 2f : 1f);
    }

    /// <summary>
    /// The owner's colour for every marker fill and pack-icon tint: the loaded world's own
    /// <see cref="NationDefinition.ColorHex"/>, parsed by the Godot-free <see cref="MarkerTint.ForOwner"/>
    /// (T94 rework round 1, N3) so the exact components a texture is modulated with are testable. An
    /// unknown nation or an unparseable colour falls back to <see cref="UnknownNationColor"/>, exactly
    /// as the previous Godot-side parse did. Marker style A (#517 rework round 1) rings no textured
    /// marker with it: the fallback shapes keep <see cref="CityRingColor"/>.
    /// </summary>
    private Color NationColor(string nationId)
    {
        var tint = MarkerTint.ForOwner(_session?.World, nationId);
        return tint is { } owned
            ? new Color(owned.Red, owned.Green, owned.Blue, owned.Alpha)
            : UnknownNationColor;
    }

    private enum PendingMapAction
    {
        None,
        MoveArmy,
        AttackWithArmy,
    }
}
