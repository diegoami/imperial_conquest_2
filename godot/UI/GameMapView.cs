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
/// <strong>Given commands, not merely read (T99).</strong> Orders are given on this map exactly as the
/// original's Unit map takes them (<c>docs/investigations/original-ui-command-audit.md</c> §2.1): a
/// click is resolved by <see cref="MapClickRules"/> — the Godot-free decision table whose every row is
/// that audit's — and the row's composed order is submitted through <see cref="GameSession.Submit"/>,
/// never a direct mutation of <see cref="GameSession.State"/>. The left button selects and orders; the
/// right button only opens the clicked marker's unit list. The pre-T99 button-armed "pending action"
/// (a context-panel button arming the next click, an arming the original never had) is gone: the only
/// confirmation is the attack prompt a click against a nation the seat is not at war with raises
/// (<see cref="AttackConfirmationRequested"/>).
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
/// <strong>T97: markers draw the original's own background square and foreground glyph.</strong> Every
/// city, army and fleet marker with a pack texture is a square filled with the owner's
/// <em>background</em> colour (<see cref="NationDefinition.ColorHex"/>), with the authored silhouette
/// tinted in the owner's <em>foreground</em> colour
/// (<see cref="NationDefinition.GlyphColorHex"/>) — the original's own look, from
/// <c>2026-09-29-nation-marker-colours.md</c>, superseding both T49's designed palette and bug
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/517">#517</see>'s near-black
/// backdrop. A nation with no foreground (every pre-T97 world) gets white or black chosen by its
/// background's luminance. The gold selection outline and the size tiers are unchanged, and the
/// no-texture fallback shapes keep drawing in the owner's background colour.
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

    /// <summary>The tile colour for a terrain name the map does not know (the tile-type list is open).</summary>
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

    /// <summary>
    /// Fired when a city becomes the information panel's subject — selected by a left click, or shown
    /// after a foreign target a selection could not attack dropped it (T99: the original's "the
    /// selection is cleared and only the information panel changes" row).
    /// </summary>
    public event Action<string>? CitySelected;

    public event Action<string>? ArmySelected;

    public event Action<string>? FleetSelected;

    public event Action? SelectionCleared;

    /// <summary>Fired after a map order is issued, with the session's own output lines.</summary>
    public event Action<IReadOnlyList<string>>? CommandIssued;

    /// <summary>
    /// T99, the user's two-button decision of 2026-10-01: fired when the right button clicks a city,
    /// army or fleet, so the panel shows its unit list (a city's garrison, an army's units, a fleet's
    /// ships and any army aboard). Fired <em>instead of</em> any selection change or order — a right
    /// click never selects and never orders.
    /// </summary>
    public event Action<MapEntityKind, string>? UnitListRequested;

    /// <summary>
    /// T99, the original's own attack prompt (confirmed:
    /// <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c>): fired when a left click
    /// resolves to an attack, besiege or naval attack against a nation the active seat is <em>not</em>
    /// at war with, with the whole outcome (its <see cref="MapClickOutcome.OrderLine"/> and
    /// <see cref="MapClickOutcome.ConfirmationText"/>). The screen that wires this opens its
    /// <c>ConfirmPrompt</c>; its answer comes back through
    /// <see cref="AnswerAttackConfirmation"/> — Yes submits the order (the engine composes the
    /// declaration of war itself), No drops the selection. Never raised when already at war.
    /// </summary>
    public event Action<MapClickOutcome>? AttackConfirmationRequested;

    /// <summary>
    /// T102: fired whenever this view's visible tile rectangle changes — a wheel zoom, a drag, a re-fit,
    /// <see cref="CentreOnTile"/>, or a resize. The overview mini-map (<see cref="AreaMapView"/>) listens
    /// so its view rectangle follows every pan and zoom (<c>docs/game-design.md</c>'s Screen/flow
    /// "Overview mini-map" bullet), without this class knowing the mini-map exists.
    /// </summary>
    public event Action? ViewChanged;

    private GameSession? _session;
    private AssetPackTextureLoader? _assetLoader;
    private float _zoom = 1f;
    private Vector2 _pan = Vector2.Zero;
    private Vector2 _pressPosition;
    private bool _dragging;
    private bool _dragMoved;
    private ImageTexture? _terrainTexture;
    private int[]? _terrainCells;

    // T148 "The map's surface is painted, not tiled": the baked splat lattice and the six seamless
    // surface textures the shader blends, held so TerrainMemoryBytesForCheck can report the terrain
    // draw's own footprint. _surfaceCanvas is the child CanvasItem (show-behind-parent, so the markers
    // still draw over it) that carries the surface shader; it is null when the shader or a texture is
    // missing, and the draw falls back to the flat-colour texture.
    private TerrainSplatMap? _splatMap;
    private ImageTexture? _splatTextureA;
    private ImageTexture? _splatTextureB;
    private readonly Texture2D?[] _surfaceTextures = new Texture2D?[6];
    private ColorRect? _surfaceCanvas;
    private ShaderMaterial? _surfaceMaterial;
    private TerrainSurfaceState _drawnSurface;

    private string? _selectedCityId;
    private string? _selectedArmyId;
    private string? _selectedFleetId;

    private MapClickOutcome? _pendingConfirmedAttack;
    private bool _fittedOnce;

    // T147 (bug #781 point 1): once the player has zoomed or panned, the view is theirs, so a later resize
    // keeps its centre instead of re-fitting. Until then every resize re-fits, so a window maximised after
    // the first frame no longer keeps the first (letterboxing) fit.
    private bool _userAdjustedView;

    // The size at the last layout, so a resize can read the tile that was at the control's centre before
    // the new size arrived and put it back at the new centre.
    private Vector2 _lastSize;

    // T147 (bug #781 point 1, Sol R2): a genuine window/root resize changes the container this control
    // sits in, so this control's rect changes with its parent's. A sibling's layout change (T132's
    // side-panel toggle, the news log) changes only this control's rect and leaves the parent's alone.
    // Comparing the parent's size is the explicit protection the toggle's exact zoom and pan need
    // (godot/Checks/SidePanelToggleCheck.cs) without stopping the re-fit on a real window resize.
    private Vector2 _lastParentSize;

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
        if (_session is null || Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        var parentSize = ParentControlSize();
        var parentResized = !parentSize.IsEqualApprox(_lastParentSize);

        if (!_fittedOnce)
        {
            // The first real layout: the control was (0,0) when Attach ran.
            FitToView();
            _fittedOnce = true;
        }
        else if (!parentResized)
        {
            // A sibling's layout changed this control's rect (the side panel/column toggling, the news
            // log) but the window/root did not resize. T132 protects the side-panel toggle's exact zoom
            // and pan (godot/Checks/SidePanelToggleCheck.cs), so nothing about the view moves here;
            // NotifyViewChanged below still tells the mini-map its rect changed.
        }
        else if (_userAdjustedView)
        {
            // The player has zoomed or panned: a resize keeps the tile under the view's centre, so it
            // never scrolls the view the player set up.
            KeepViewCentreAcrossResize();
        }
        else
        {
            // T147 (bug #781 point 1, Sol R2): the view is still the automatic fit, so it is redone on
            // every window/root resize -- shrinking as well as growing -- and always covers the new rect.
            FitToView();
        }

        _lastParentSize = parentSize;
        _lastSize = Size;

        // B1 (T102 review): VisibleTileRect depends on Size, and project.godot sets
        // window/stretch/aspect "expand", so a later resize (a window resize or maximise) changes
        // the visible tile rectangle even though the player's own zoom and pan did not. Announce
        // it so the mini-map's view rectangle follows; without this the rectangle goes stale.
        NotifyViewChanged();
    }

    /// <summary>The immediate parent's size when it is a <see cref="Control"/>, else zero.</summary>
    private Vector2 ParentControlSize() =>
        GetParent() is Control parent ? parent.Size : Vector2.Zero;

    /// <summary>
    /// T147 (bug #781 point 1): after the player has zoomed or panned, a resize keeps the view's tile
    /// centre. The zoom is never allowed below the new size's cover zoom, so a window that grew cannot
    /// expose background; growing the zoom around the centre keeps the same tile under the centre.
    /// </summary>
    private void KeepViewCentreAcrossResize()
    {
        if (_lastSize.X <= 0 || _lastSize.Y <= 0)
        {
            FitToView();
            return;
        }

        var oldTileSize = BaseTileSize * _zoom;
        var centreTile = ((_lastSize / 2f) - _pan) / oldTileSize;

        var minimum = MinimumZoom();
        if (_zoom < minimum)
        {
            _zoom = minimum;
        }

        _pan = (Size / 2f) - (centreTile * (BaseTileSize * _zoom));
        ClampPanToView();
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
        ResolveSurfaceResources();
        _userAdjustedView = false;
        if (Size.X > 0 && Size.Y > 0)
        {
            FitToView();
            _fittedOnce = true;
        }

        _lastSize = Size;
        _lastParentSize = ParentControlSize();
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

    /// <summary>
    /// T148: sets the exact drawn cell size (in screen pixels) through the same clamp-and-announce path
    /// every zoom uses. <c>godot/Checks/TerrainSurfaceCheck.cs</c> and the visual-review screenshot tour
    /// need the 32 px a cell the review uses, which the wheel's 1.15 factor cannot reach; the check must
    /// not know <see cref="BaseTileSize"/>. Check-only; the app never calls it.
    /// </summary>
    public void SetDrawnCellPixelsForCheck(float cellPixels)
    {
        if (_session is null || Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        _zoom = Mathf.Clamp(cellPixels / BaseTileSize, MinimumZoom(), MaxZoom);
        _userAdjustedView = true;
        ClampPanToView();
        NotifyViewChanged();
    }

    /// <summary>
    /// T148 Done-when 3: the surface the last <see cref="_Draw"/> actually drew — the shader path (with
    /// the six surface keys bound) or the flat-colour fallback — recorded in <see cref="_Draw"/> itself and
    /// never recomputed from configuration, so the check asserts the drawn code, not the configured
    /// material. Read-only.
    /// </summary>
    public TerrainSurfaceState TerrainSurfaceForCheck => _drawnSurface;

    /// <summary>
    /// T148 Done-when 3: the bytes the terrain draw holds alive right now — width × height × 4 for the
    /// flat-colour fallback, each splat texture and each mipmapped surface texture (a mipmapped one
    /// counted with its whole chain, × 4/3). The draw retains no <see cref="Image"/> after upload, so each
    /// texture is counted once. Used by <c>godot/Checks/TerrainSurfaceCheck.cs</c> to prove the whole
    /// terrain draw stays under 24 MB.
    /// </summary>
    public long TerrainMemoryBytesForCheck
    {
        get
        {
            var total = Bytes(_terrainTexture, mipmapped: false);
            total += Bytes(_splatTextureA, mipmapped: false);
            total += Bytes(_splatTextureB, mipmapped: false);
            foreach (var texture in _surfaceTextures)
            {
                total += Bytes(texture, mipmapped: true);
            }

            return total;
        }
    }

    private static long Bytes(Texture2D? texture, bool mipmapped)
    {
        if (texture is null)
        {
            return 0;
        }

        var baseBytes = (long)texture.GetWidth() * texture.GetHeight() * 4;
        return mipmapped ? (baseBytes * 4) / 3 : baseBytes;
    }

    /// <summary>The rect <see cref="_Draw"/> would draw the terrain texture in at the current zoom and
    /// pan — exposed so <c>godot/Checks/MapClipCheck.cs</c> can prove the maximum-zoom case really does
    /// draw past this control's own rect (the bug's precondition), not merely that clipping is enabled.</summary>
    public Rect2 TerrainDrawRectForCheck => _session is null
        ? new Rect2(_pan, Vector2.Zero)
        : new Rect2(_pan, new Vector2(_session.World.Width, _session.World.Height) * BaseTileSize * _zoom);

    /// <summary>
    /// T102: the tile rectangle the order map currently shows, in tile coordinates — the inverse of
    /// <see cref="HandleClick"/>'s screen-to-tile transform. The overview mini-map
    /// (<see cref="AreaMapView"/>) maps this through <see cref="AreaMapGeometry"/> to draw the view
    /// rectangle, so the two views cannot disagree about what is on screen. Returns an empty rect
    /// before <see cref="Attach"/>.
    /// </summary>
    public Rect2 VisibleTileRect
    {
        get
        {
            if (_session is null)
            {
                return new Rect2(Vector2.Zero, Vector2.Zero);
            }

            var tileSize = BaseTileSize * _zoom;
            return new Rect2(-_pan / tileSize, Size / tileSize);
        }
    }

    /// <summary>
    /// T102: re-centres this view on a tile — the original's Area-map click
    /// (<c>TAreaMap_AreaMapClick</c> → <c>TUnitMap_AreaMapClicked</c>, confirmed by
    /// <c>2026-09-29-nation-view-origin-and-unit-map-clicks.md</c> and
    /// <c>original-ui-command-audit.md</c> §1.5). The tile's centre is placed at this view's own centre
    /// at the current zoom. It changes no selection and submits no order; it raises
    /// <see cref="ViewChanged"/>.
    /// </summary>
    public void CentreOnTile(int x, int y)
    {
        if (_session is null || Size.X <= 0 || Size.Y <= 0)
        {
            return;
        }

        var tileSize = BaseTileSize * _zoom;
        // T147 (bug #781 point 1, Sol R3): a re-centre clamps like every other pan path, so it can never
        // expose background; the tile is brought as close to the control's centre as the clamp allows.
        _pan = (Size / 2f) - (new Vector2(x + 0.5f, y + 0.5f) * tileSize);
        ClampPanToView();
        NotifyViewChanged();
    }

    /// <summary>The one place this view's zoom/pan updates announce themselves and redraw.</summary>
    private void NotifyViewChanged()
    {
        QueueRedraw();
        ViewChanged?.Invoke();
    }

    /// <summary>
    /// The screen-space position of tile (<paramref name="x"/>, <paramref name="y"/>)'s centre at the
    /// current zoom and pan — the exact inverse of <see cref="HandleClick"/>'s own screen-to-tile
    /// transform, exposed so <c>godot/Checks/MapClickCheck.cs</c> can drive <see cref="_GuiInput"/>
    /// with real mouse events at real positions instead of a helper that skips the input path.
    /// </summary>
    public Vector2 TileCenterForCheck(int x, int y) =>
        _pan + new Vector2(x + 0.5f, y + 0.5f) * (BaseTileSize * _zoom);

    /// <summary>
    /// Clears the map's selection — T99's own cancel path (the entry's Esc/Shift+X wiring calls this),
    /// and the end of every order row the audit says ends the selection.
    /// </summary>
    public void ClearSelection()
    {
        _selectedCityId = null;
        _selectedArmyId = null;
        _selectedFleetId = null;
        SelectionCleared?.Invoke();
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

        // T99: the same decode the click path reads (a click's land/sea class comes from the tile
        // type of the cell under the cursor, never a second terrain table) — kept instead of decoded
        // again per click.
        _terrainCells = cells;
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

    /// <summary>
    /// T148: bakes the splat lattice, resolves the six surface textures through the pack loader and sets
    /// up the shader child. A missing shader or texture leaves the matching slot null, and the draw then
    /// falls back to the flat-colour texture. Called once per <see cref="Attach"/> (the world does not
    /// change under a view).
    /// </summary>
    private void ResolveSurfaceResources()
    {
        if (_session is null)
        {
            return;
        }

        _splatMap = TerrainSplatMap.Bake(_session.World);

        // The splat lattice as two RGBA8 textures. The source Images are not retained: the textures own
        // their pixels, and TerrainMemoryBytesForCheck counts only what stays alive.
        var splatImageA = Image.CreateFromData(
            _splatMap.LatticeWidth, _splatMap.LatticeHeight, false, Image.Format.Rgba8, _splatMap.SplatA);
        var splatImageB = Image.CreateFromData(
            _splatMap.LatticeWidth, _splatMap.LatticeHeight, false, Image.Format.Rgba8, _splatMap.SplatB);
        _splatTextureA = ImageTexture.CreateFromImage(splatImageA);
        _splatTextureB = ImageTexture.CreateFromImage(splatImageB);

        for (var i = 0; i < TerrainSplatMap.SurfaceKeysByClass.Count; i++)
        {
            _surfaceTextures[i] = _assetLoader?.TryGetSurfaceTexture(TerrainSplatMap.SurfaceKeysByClass[i]);
        }

        EnsureSurfaceCanvas();
        BindSurfaceUniforms();
    }

    /// <summary>
    /// T148: the child CanvasItem the surface shader draws on. It sits behind its parent
    /// (<see cref="CanvasItem.ShowBehindParent"/>) so the markers this view draws in <c>_Draw</c> stay on
    /// top, and it inherits the parent's own <see cref="Control.ClipContents"/> so zoomed in it cannot
    /// spill over the toolbar (bug #498).
    /// </summary>
    private void EnsureSurfaceCanvas()
    {
        if (_surfaceCanvas is not null)
        {
            return;
        }

        var shader = GD.Load<Shader>("res://UI/TerrainSurface.gdshader");
        if (shader is null)
        {
            return;
        }

        _surfaceMaterial = new ShaderMaterial { Shader = shader };
        _surfaceCanvas = new ColorRect
        {
            Name = "TerrainSurface",
            Color = new Color(1f, 1f, 1f, 1f),
            MouseFilter = MouseFilterEnum.Ignore,
            ShowBehindParent = true,
            TextureRepeat = TextureRepeatEnum.Enabled,
            TextureFilter = TextureFilterEnum.LinearWithMipmaps,
            Material = _surfaceMaterial,
        };
        AddChild(_surfaceCanvas);
    }

    private void BindSurfaceUniforms()
    {
        if (_surfaceMaterial is null || _session is null || _splatMap is null)
        {
            return;
        }

        _surfaceMaterial.SetShaderParameter("splat_a", _splatTextureA);
        _surfaceMaterial.SetShaderParameter("splat_b", _splatTextureB);
        _surfaceMaterial.SetShaderParameter("tex_plain", _surfaceTextures[0]);
        _surfaceMaterial.SetShaderParameter("tex_desert", _surfaceTextures[1]);
        _surfaceMaterial.SetShaderParameter("tex_forest", _surfaceTextures[2]);
        _surfaceMaterial.SetShaderParameter("tex_mountain", _surfaceTextures[3]);
        _surfaceMaterial.SetShaderParameter("tex_shallow", _surfaceTextures[4]);
        _surfaceMaterial.SetShaderParameter("tex_deep", _surfaceTextures[5]);
        _surfaceMaterial.SetShaderParameter("world_size", new Vector2(_session.World.Width, _session.World.Height));
        _surfaceMaterial.SetShaderParameter("surface_scale", SurfaceCellsPerRepeat);
    }

    /// <summary>The cells one surface-texture repeat spans in the shader — a few cells keeps the far zoom calm.</summary>
    private const float SurfaceCellsPerRepeat = 6f;

    /// <summary>
    /// T148: draws the river strokes over the surface, in the water colour, about a fifth of a cell wide
    /// with a one-pixel minimum, once a cell is drawn at 4 px or more. Each stroke is a quadratic Bézier
    /// from exit midpoint to exit midpoint through its cell centre; neighbouring cells' strokes share an
    /// end on the shared edge, so a river reads as one continuous line.
    /// </summary>
    private void DrawRivers(World world, float tileSize)
    {
        if (_terrainCells is null || tileSize < 4f)
        {
            return;
        }

        var strokes = TerrainSplatMap.RiverStrokes(_terrainCells, world.Width, world.Height);
        if (strokes.Count == 0)
        {
            return;
        }

        var colour = TerrainColorsByName["River"];
        var width = Mathf.Max(tileSize * 0.2f, 1f);
        var points = new Vector2[8];
        foreach (var stroke in strokes)
        {
            for (var k = 0; k < points.Length; k++)
            {
                var t = k / (points.Length - 1f);
                var inverse = 1f - t;
                var x = (inverse * inverse * stroke.Start.X)
                    + (2f * inverse * t * stroke.Control.X)
                    + (t * t * stroke.End.X);
                var y = (inverse * inverse * stroke.Start.Y)
                    + (2f * inverse * t * stroke.Control.Y)
                    + (t * t * stroke.End.Y);
                points[k] = _pan + (new Vector2(x, y) * tileSize);
            }

            DrawPolyline(points, colour, width, true);
        }
    }

    /// <summary>Whether every surface resource the shader path needs resolved.</summary>
    private bool SurfaceReady =>
        _surfaceCanvas is not null
        && _surfaceMaterial is not null
        && _splatTextureA is not null
        && _splatTextureB is not null
        && _surfaceTextures.All(texture => texture is not null);

    private IReadOnlyList<string> ResolvedSurfaceKeys()
    {
        var keys = new List<string>(6);
        for (var i = 0; i < TerrainSplatMap.SurfaceKeysByClass.Count; i++)
        {
            if (_surfaceTextures[i] is not null)
            {
                keys.Add(TerrainSplatMap.SurfaceKeysByClass[i]);
            }
        }

        return keys;
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
            NotifyViewChanged();
            return;
        }

        // T147 (bug #781 point 1): the default fit covers the control rather than letterboxing it --
        // the larger of the two ratios, so no background shows on either axis. The overflow is reachable
        // by the existing pan, clamped by ClampPanToView so the map's edge cannot leave the control's.
        var mapSize = new Vector2(world.Width, world.Height) * BaseTileSize;
        _zoom = Mathf.Clamp(CoverZoom(viewport), MinZoom, MaxZoom);
        var scaledSize = mapSize * _zoom;
        _pan = (viewport - scaledSize) / 2f;
        ClampPanToView();
        NotifyViewChanged();
    }

    /// <summary>The zoom at which the map exactly covers <paramref name="viewport"/> on its tighter axis.</summary>
    private float CoverZoom(Vector2 viewport) =>
        _session is null
            ? 1f
            : Mathf.Max(
                viewport.X / (_session.World.Width * BaseTileSize),
                viewport.Y / (_session.World.Height * BaseTileSize));

    /// <summary>
    /// T147 (bug #781 point 1): the smallest zoom the player may reach — the cover zoom for the current
    /// size, so the background can never show. Clamped into the same [<see cref="MinZoom"/>,
    /// <see cref="MaxZoom"/>] range the fit uses.
    /// </summary>
    private float MinimumZoom() => Mathf.Min(Mathf.Clamp(CoverZoom(Size), MinZoom, MaxZoom), MaxZoom);

    /// <summary>
    /// T147 (bug #781 point 1): keeps the map's edge at the control's edge, so panning can never expose
    /// background. On an axis the map cannot cover (a map smaller than the control at <see cref="MaxZoom"/>)
    /// the map is centred on that axis instead.
    /// </summary>
    private void ClampPanToView()
    {
        if (_session is null)
        {
            return;
        }

        var scaledSize = new Vector2(_session.World.Width, _session.World.Height) * BaseTileSize * _zoom;
        _pan = new Vector2(
            scaledSize.X >= Size.X ? Mathf.Clamp(_pan.X, Size.X - scaledSize.X, 0f) : (Size.X - scaledSize.X) / 2f,
            scaledSize.Y >= Size.Y ? Mathf.Clamp(_pan.Y, Size.Y - scaledSize.Y, 0f) : (Size.Y - scaledSize.Y) / 2f);
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
                    HandleClick(released.Position, MapClickButton.Left);
                }

                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } rightReleased:
                // T99, the user's two-button decision: the right button only opens the clicked
                // marker's unit list. It never starts (or can be mistaken for) a drag, so it needs
                // no press state — the release alone is the click.
                HandleClick(rightReleased.Position, MapClickButton.Right);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                _pan += motion.Relative;
                if (motion.Relative.Length() > DragThreshold)
                {
                    _dragMoved = true;
                }

                // T147 (bug #781 point 1): a drag is the player's own pan — clamp it so no background shows
                // and stop the automatic re-fit on later resizes.
                _userAdjustedView = true;
                ClampPanToView();
                NotifyViewChanged();
                AcceptEvent();
                break;
        }
    }

    private void Zoom(Vector2 cursor, float factor)
    {
        // T147 (bug #781 point 1): the zoom-out limit is the cover zoom, so a wheel-out can never expose
        // background, and the player's own zoom ends the automatic re-fit on later resizes.
        var nextZoom = Mathf.Clamp(_zoom * factor, MinimumZoom(), MaxZoom);
        if (Mathf.Abs(nextZoom - _zoom) < 0.0001f)
        {
            return;
        }

        var normalized = (cursor - _pan) / _zoom;
        _zoom = nextZoom;
        _pan = cursor - normalized * _zoom;
        _userAdjustedView = true;
        ClampPanToView();
        NotifyViewChanged();
    }

    /// <summary>
    /// Resolves one mouse click to its <see cref="MapClickRules"/> row and applies it — the whole T99
    /// order path. The context is gathered from the live state (the marker on the tile, the selection's
    /// moves, the Chebyshev distance, the relation) and the resolved row is applied exactly as the audit
    /// describes: order rows submit their composed command through <see cref="GameSession.Submit"/>,
    /// select rows raise the panel's events, and the confirmable attack rows raise
    /// <see cref="AttackConfirmationRequested"/> instead of submitting anything.
    /// </summary>
    private void HandleClick(Vector2 screenPosition, MapClickButton button)
    {
        if (_session is null)
        {
            return;
        }

        var tile = (screenPosition - _pan) / (_zoom * BaseTileSize);
        var x = Mathf.FloorToInt(tile.X);
        var y = Mathf.FloorToInt(tile.Y);

        var outcome = MapClickRules.Resolve(BuildClickContext(button, x, y));
        ApplyOutcome(outcome);
        QueueRedraw();
    }

    private MapClickContext BuildClickContext(MapClickButton button, int x, int y)
    {
        var state = _session!.State;
        var world = _session.World;

        MapClickTarget? target = null;
        if (x >= 0 && x < world.Width && y >= 0 && y < world.Height)
        {
            if (FindArmyAt(x, y) is { } army)
            {
                target = new MapClickTarget(MapEntityKind.Army, army.Id, army.Nation);
            }
            else if (FindFleetAt(x, y) is { } fleet)
            {
                target = new MapClickTarget(MapEntityKind.Fleet, fleet.Id, fleet.Nation, fleet.CarriedArmyId);
            }
            else if (FindCityAt(x, y) is { } city)
            {
                target = new MapClickTarget(MapEntityKind.City, city.Id, city.Owner);
            }
        }

        MapClickSelection? selection = null;
        if (_selectedArmyId is { } selectedArmyId && state.ArmyById(selectedArmyId) is { } selectedArmy)
        {
            selection = new MapClickSelection(MapEntityKind.Army, selectedArmy.Id, selectedArmy.Moves);
        }
        else if (_selectedFleetId is { } selectedFleetId && state.FleetById(selectedFleetId) is { } selectedFleet)
        {
            selection = new MapClickSelection(
                MapEntityKind.Fleet, selectedFleet.Id, selectedFleet.Moves, selectedFleet.CarriedArmyId);
        }

        // The tile's land/sea class is the engine's own terrain table — never a second one here.
        // Outside the world, neither class holds (a click off the map matches no row at all).
        var land = false;
        var sea = false;
        if (x >= 0 && x < world.Width && y >= 0 && y < world.Height && _terrainCells is { } cells)
        {
            var tileType = world.TileTypeByCode(cells[(y * world.Width) + x]);
            land = tileType?.PassableByArmies == true;
            sea = tileType?.PassableByFleets == true;
        }

        // The Chebyshev distance the audit settles — the engine's shared metric, not an inline copy.
        var distance = selection is null
            ? 0
            : Math.Max(
                Math.Abs(SelectionX(selection, state) - x),
                Math.Abs(SelectionY(selection, state) - y));

        var codes = _session.Ruleset.Diplomacy.StateCodes;
        var relation = RelationTo(target?.Nation, codes);

        return new MapClickContext(
            Button: button,
            X: x,
            Y: y,
            TileIsLand: land,
            TileIsSea: sea,
            Target: target,
            Selection: selection,
            Distance: distance,
            Relation: relation,
            WarCode: codes.War,
            ActiveNationId: state.ActiveNationId);
    }

    private static int SelectionX(MapClickSelection selection, GameState state) =>
        selection.Kind == MapEntityKind.Army
            ? state.ArmyById(selection.Id)?.X ?? 0
            : state.FleetById(selection.Id)?.X ?? 0;

    private static int SelectionY(MapClickSelection selection, GameState state) =>
        selection.Kind == MapEntityKind.Army
            ? state.ArmyById(selection.Id)?.Y ?? 0
            : state.FleetById(selection.Id)?.Y ?? 0;

    /// <summary>
    /// The relation between the active seat and the target's nation, failing closed exactly as
    /// <see cref="GameSession"/>'s own <c>IsAtWar</c> does for an unknown nation: an unknown nation is
    /// not at war, so the click still asks the prompt and the engine's own gates report whatever they
    /// report. <see langword="null"/> when there is no target (no relation to read).
    /// </summary>
    private int RelationTo(string? targetNation, RelationStateCodes codes)
    {
        if (targetNation is null)
        {
            return codes.Peace;
        }

        var relations = _session!.State.Relations;
        return relations.IndexOf(_session.State.ActiveNationId) >= 0 && relations.IndexOf(targetNation) >= 0
            ? relations.Get(_session.State.ActiveNationId, targetNation)
            : codes.Peace;
    }

    private void ApplyOutcome(MapClickOutcome outcome)
    {
        switch (outcome.Kind)
        {
            case MapClickOutcomeKind.Nothing:
                return;

            case MapClickOutcomeKind.Select:
                FocusAsSelection(outcome.FocusKind ?? MapEntityKind.City, outcome.FocusId!);
                return;

            case MapClickOutcomeKind.ShowUnitList:
                UnitListRequested?.Invoke(outcome.FocusKind ?? MapEntityKind.City, outcome.FocusId!);
                return;

            case MapClickOutcomeKind.DropSelectionAndShowTarget:
                // The audit's own row: the selection is cleared and only the information panel
                // changes — the target's details show without becoming the map's selection.
                ClearSelection();
                ShowDetails(outcome.FocusKind ?? MapEntityKind.City, outcome.FocusId!);
                return;

            case MapClickOutcomeKind.EmbarkArmy:
                SubmitMapOrder(outcome.OrderLine!);
                // "Then the fleet becomes the selection" — the fleet is the clicked target.
                FocusAsSelection(MapEntityKind.Fleet, outcome.FocusId!);
                return;

            case MapClickOutcomeKind.DisembarkArmy:
                SubmitMapOrder(outcome.OrderLine!);
                // "The selection is cleared and the army's details are shown."
                ClearSelection();
                ShowDetails(MapEntityKind.Army, outcome.FocusId!);
                return;

            case MapClickOutcomeKind.MoveArmy:
                SubmitMoveOrder(outcome, MapEntityKind.Army);
                return;

            case MapClickOutcomeKind.MoveFleet:
                SubmitMoveOrder(outcome, MapEntityKind.Fleet);
                return;

            case MapClickOutcomeKind.AttackArmy:
            case MapClickOutcomeKind.BesiegeCity:
            case MapClickOutcomeKind.AttackFleet:
                if (outcome.RequiresWarConfirmation)
                {
                    // The original's own flow: the confirmation is part of the click — nothing is
                    // submitted until the prompt answers Yes. The screen wires the prompt; the
                    // answer comes back through AnswerAttackConfirmation.
                    _pendingConfirmedAttack = outcome;
                    AttackConfirmationRequested?.Invoke(outcome);
                    return;
                }

                SubmitMapOrder(outcome.OrderLine!);
                ClearSelection();
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Submits a move or move-fleet row and applies the audit's stay-selected rule
    /// (<c>TUnitMap_MoveHumanArmy</c>): the unit stays selected while it still has moves left, and the
    /// selection ends when they reach 0 — read from the post-order state, since only the engine knows
    /// how many moves the walk spent.
    /// </summary>
    private void SubmitMoveOrder(MapClickOutcome outcome, MapEntityKind moverKind)
    {
        var moverId = moverKind == MapEntityKind.Army ? _selectedArmyId : _selectedFleetId;
        SubmitMapOrder(outcome.OrderLine!);

        if (moverId is null)
        {
            ClearSelection();
            return;
        }

        var state = _session!.State;
        var moves = moverKind == MapEntityKind.Army
            ? state.ArmyById(moverId)?.Moves
            : state.FleetById(moverId)?.Moves;
        if (moves is >= 1)
        {
            FocusAsSelection(moverKind, moverId);
        }
        else
        {
            ClearSelection();
        }
    }

    /// <summary>
    /// The one submission path every map order goes through: the composed line through
    /// <see cref="GameSession.Submit"/>, its lines to <see cref="CommandIssued"/> (which the screen
    /// funnels into its shared outcome label and battle overlays), and a redraw.
    /// </summary>
    private void SubmitMapOrder(string orderLine)
    {
        if (_session is null)
        {
            return;
        }

        var lines = _session.Submit(orderLine).Lines;
        CommandIssued?.Invoke(lines);
        QueueRedraw();
    }

    /// <summary>
    /// The answer to a raised <see cref="AttackConfirmationRequested"/>: Yes submits the order (the
    /// engine's own <c>ComposeDeclareWarIfNeeded</c> puts the declaration of war in front of it, so
    /// the UI never submits a second one) and ends the selection; No submits nothing and drops the
    /// selection. The prompt's own buttons call this — public for the screen that wires them.
    /// </summary>
    public void AnswerAttackConfirmation(bool yes)
    {
        if (_pendingConfirmedAttack is not { } outcome)
        {
            return;
        }

        _pendingConfirmedAttack = null;
        if (yes)
        {
            SubmitMapOrder(outcome.OrderLine!);
        }

        // Either answer ends the selection: the audit's "On no … the selection is simply dropped", and
        // "the selection also ends by itself after an attack".
        ClearSelection();
    }

    /// <summary>Raises the panel's subject event for the entity the outcome focuses, without holding it as the map's selection.</summary>
    private void ShowDetails(MapEntityKind kind, string id)
    {
        switch (kind)
        {
            case MapEntityKind.City:
                CitySelected?.Invoke(id);
                break;
            case MapEntityKind.Army:
                ArmySelected?.Invoke(id);
                break;
            case MapEntityKind.Fleet:
                FleetSelected?.Invoke(id);
                break;
        }
    }

    /// <summary>Makes the entity the map's selection (and the panel's subject) through the same path a plain select uses.</summary>
    private void FocusAsSelection(MapEntityKind kind, string id)
    {
        switch (kind)
        {
            case MapEntityKind.City:
                SelectCity(id);
                break;
            case MapEntityKind.Army:
                SelectArmy(id);
                break;
            case MapEntityKind.Fleet:
                SelectFleet(id);
                break;
        }
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

    /// <summary>
    /// The army marker on a tile, for the click's target lookup. An embarked army has its fleet's own
    /// X/Y (<c>EmbarkArmyCommandHandler</c> copies the fleet's position and <c>MoveFleetCommandHandler</c>
    /// carries it along) but is not drawn there — <see cref="DrawArmy"/> skips it — so it must not win
    /// the click over the visible fleet carrying it (review round 1, B1).
    /// </summary>
    private ArmyState? FindArmyAt(int x, int y) =>
        _session?.State.Armies.FirstOrDefault(a => a.X == x && a.Y == y && a.AboardFleetId is null);

    private FleetState? FindFleetAt(int x, int y) =>
        _session?.State.Fleets.FirstOrDefault(f => f.X == x && f.Y == y && !f.IsUnderConstruction);

    public override void _Draw()
    {
        if (_session is null)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), BackgroundColor);
            _drawnSurface = TerrainSurfaceState.None;
            return;
        }

        var world = _session.World;
        var tileSize = BaseTileSize * _zoom;
        var mapRect = new Rect2(_pan, new Vector2(world.Width, world.Height) * tileSize);

        // T148: one shader pass over the whole terrain rectangle when every surface resource resolved;
        // otherwise the flat-colour texture is drawn exactly as before. The recorded state is what the
        // draw actually did, never a recomputation from the configured material.
        if (SurfaceReady)
        {
            BindSurfaceUniforms();
            _surfaceCanvas!.Position = mapRect.Position;
            _surfaceCanvas.Size = mapRect.Size;
            _surfaceCanvas.Visible = true;
            _drawnSurface = new TerrainSurfaceState(
                TerrainSurfaceKind.Shader, TerrainSplatMap.SurfaceKeysByClass);
        }
        else
        {
            if (_surfaceCanvas is not null)
            {
                _surfaceCanvas.Visible = false;
            }

            DrawRect(new Rect2(Vector2.Zero, Size), BackgroundColor);
            if (_terrainTexture is not null)
            {
                DrawTextureRect(_terrainTexture, mapRect, false);
            }

            _drawnSurface = new TerrainSurfaceState(TerrainSurfaceKind.FlatFallback, ResolvedSurfaceKeys());
        }

        DrawRivers(world, tileSize);

        // T110: the map always draws every layer. The bottom toolbar's Cities/Armies/Fleets toggles hid
        // them, which the original never does; the Area map's Show entries paint highlights instead, so
        // there is no hide flag left to consult here.
        foreach (var city in _session.State.Cities)
        {
            DrawCity(city, tileSize);
        }

        foreach (var army in _session.State.Armies)
        {
            DrawArmy(army, tileSize);
        }

        foreach (var fleet in _session.State.Fleets.Where(f => !f.IsUnderConstruction))
        {
            DrawFleet(fleet, tileSize);
        }
    }

    private Vector2 TileCenter(int x, int y, float tileSize) =>
        _pan + new Vector2((x + 0.5f) * tileSize, (y + 0.5f) * tileSize);

    private void DrawCity(CityState city, float tileSize)
    {
        var center = TileCenter(city.X, city.Y, tileSize);
        var (background, foreground) = NationColors(city.Owner);
        var radius = Mathf.Max(tileSize * 0.55f, 3f);
        var selected = string.Equals(city.Id, _selectedCityId, StringComparison.Ordinal);

        // T94: key selection (live capital vs. population tier) is MapMarkerKeys' single decision,
        // shared with Slice.cs -- of game-design.md's [open] "City markers" section, only capital
        // status (orthogonal to population, live on NationState.CapitalCityId) is confirmed; the
        // tier boundaries are the ruleset's, and every shipped ruleset ships them empty.
        var texture = _assetLoader?.TryGetTexture(MapMarkerKeys.CityIcon(_session!.State, city, _session.Ruleset.MapMarkers));
        if (texture is not null)
        {
            // T97: the original's marker -- a square filled with the owner's background colour, the
            // authored silhouette tinted with the owner's foreground colour; only a selected marker
            // is outlined (in gold).
            var rect = new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2f);
            DrawRect(rect, background);
            DrawTextureRect(texture, rect, false, foreground);
            if (selected)
            {
                DrawRect(rect, SelectedRingColor, false, 2.5f);
            }

            return;
        }

        // No texture (or no loader, bug #517): the pre-T94 shape is unchanged -- the round dark halo
        // behind the owner-background-coloured disc stays exactly as it was.
        DrawCircle(center, radius + 1.5f, new Color(0f, 0f, 0f, 0.55f));
        DrawCircle(center, radius, background);
        DrawArc(center, radius, 0f, Mathf.Tau, 24, selected ? SelectedRingColor : CityRingColor, selected ? 2.5f : 1.2f);
    }

    private void DrawArmy(ArmyState army, float tileSize)
    {
        if (army.AboardFleetId is not null)
        {
            return;
        }

        var center = TileCenter(army.X, army.Y, tileSize);
        var (background, foreground) = NationColors(army.Nation);
        var half = Mathf.Max(tileSize * 0.4f, 2.5f);
        var selected = string.Equals(army.Id, _selectedArmyId, StringComparison.Ordinal);

        // T94 (#454 item 3): the confirmed three-tier size marker -- MapMarkerKeys reads the ruleset's
        // own 25,000/50,000 boundaries, never T48's [designed] per-unit-type plurality.
        var texture = _assetLoader?.TryGetTexture(MapMarkerKeys.ArmyIcon(army, _session!.Ruleset.MapMarkers));
        if (texture is not null)
        {
            // T97: the original's marker -- the owner's background square with the silhouette tinted
            // in the owner's foreground colour; only a selected marker is outlined (in gold).
            var rect = new Rect2(center - new Vector2(half, half), new Vector2(half, half) * 2f);
            DrawRect(rect, background);
            DrawTextureRect(texture, rect, false, foreground);
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

        DrawColoredPolygon(points, background);
        var ringColor = selected ? SelectedRingColor : CityRingColor;
        for (var i = 0; i < points.Length; i++)
        {
            DrawLine(points[i], points[(i + 1) % points.Length], ringColor, selected ? 2f : 1f);
        }
    }

    private void DrawFleet(FleetState fleet, float tileSize)
    {
        var center = TileCenter(fleet.X, fleet.Y, tileSize);
        var (background, foreground) = NationColors(fleet.Nation);
        var radius = Mathf.Max(tileSize * 0.35f, 2f);
        var selected = string.Equals(fleet.Id, _selectedFleetId, StringComparison.Ordinal);

        // T94: the confirmed three-tier fleet marker (ruleset's own 25/50 ship boundaries) -- the
        // fleet equivalent of DrawArmy's tier icon.
        var texture = _assetLoader?.TryGetTexture(MapMarkerKeys.FleetIcon(fleet, _session!.Ruleset.MapMarkers));
        if (texture is not null)
        {
            // T97: the original's marker -- the owner's background square with the silhouette tinted
            // in the owner's foreground colour; only a selected marker is outlined (in gold).
            var rect = new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2f);
            DrawRect(rect, background);
            DrawTextureRect(texture, rect, false, foreground);
            if (selected)
            {
                DrawRect(rect, SelectedRingColor, false, 2.5f);
            }

            return;
        }

        var fallbackRect = new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2f);
        DrawRect(fallbackRect, background);
        DrawRect(fallbackRect, selected ? SelectedRingColor : CityRingColor, false, selected ? 2f : 1f);
    }

    /// <summary>
    /// The owner's (background, foreground) pair for every marker fill and pack-icon tint: the loaded
    /// world's own <see cref="NationDefinition.ColorHex"/> and
    /// <see cref="NationDefinition.GlyphColorHex"/>, parsed by the Godot-free
    /// <see cref="MarkerTint.ForOwner"/> (T97) so the exact colours a marker is drawn with are
    /// testable. An unknown nation or an unparseable background falls back to
    /// <see cref="UnknownNationColor"/> with a luminance-chosen glyph, exactly as the previous
    /// Godot-side parse degraded.
    /// </summary>
    private (Color Background, Color Foreground) NationColors(string nationId)
    {
        if (MarkerTint.ForOwner(_session?.World, nationId) is { } owned)
        {
            return (ToColor(owned.Background), ToColor(owned.Foreground));
        }

        var unknown = new MarkerTint(UnknownNationColor.R, UnknownNationColor.G, UnknownNationColor.B, UnknownNationColor.A);
        return (UnknownNationColor, ToColor(MarkerTint.FallbackForeground(unknown)));
    }

    private static Color ToColor(MarkerTint tint) => new(tint.Red, tint.Green, tint.Blue, tint.Alpha);
}

/// <summary>The two terrain paths <see cref="GameMapView._Draw"/> can record having drawn.</summary>
public enum TerrainSurfaceKind
{
    /// <summary>Before <c>Attach</c>, or with no session: nothing was drawn.</summary>
    None,

    /// <summary>The surface shader drew the whole terrain rectangle from the six seamless textures.</summary>
    Shader,

    /// <summary>The flat-colour texture drew instead (a shader or surface texture was missing).</summary>
    FlatFallback,
}

/// <summary>
/// T148 Done-when 3: what the last <see cref="GameMapView._Draw"/> actually drew — the kind, the surface
/// keys that were bound, and whether the shader material was configured. Recorded in <c>_Draw</c>, never
/// recomputed, so a check asserts the drawn code rather than the configured resource.
/// </summary>
public readonly record struct TerrainSurfaceState(TerrainSurfaceKind Kind, IReadOnlyList<string> BoundSurfaceKeys)
{
    /// <summary>The state before any draw: nothing drawn, no keys bound.</summary>
    public static TerrainSurfaceState None { get; } =
        new(TerrainSurfaceKind.None, Array.Empty<string>());
}
