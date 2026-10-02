using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

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

    private GameSession? _session;
    private GameMapView? _orderMap;
    private AreaMapGeometry? _geometry;
    private ImageTexture? _terrainTexture;
    private AreaMapPixelRect _viewRect;

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

        var view = _viewRect;
        DrawRect(new Rect2(view.X, view.Y, view.Width, view.Height), ViewRectColor, false, 1.5f);
    }
}
