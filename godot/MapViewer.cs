using System;
using System.IO;
using Godot;
using IC2.Data;

public partial class MapViewer : Control
{
    private const float ZoomStep = 1.25f;
    private const float MaximumZoom = 10f;
    private const float InitialZoom = 2.5f;
    private const float DragThreshold = 4f;
    private static readonly Color Background = new(0.07f, 0.11f, 0.15f);
    private static readonly Color CityColor = new(1.0f, 0.97f, 0.86f);
    private static readonly Color SelectedColor = new(1.0f, 0.40f, 0.25f);

    private WorldPrefix? _world;
    private ImageTexture? _terrain;
    private CityRecord? _selected;
    private Label _title = null!;
    private Label _status = null!;
    private Button _fitButton = null!;
    private float _zoom = 1f;
    private Vector2 _pan = Vector2.Zero;
    private Vector2 _pressPosition;
    private bool _dragging;
    private bool _dragMoved;

    public override void _Ready()
    {
        GetWindow().Mode = Window.ModeEnum.Maximized;
        TextureFilter = TextureFilterEnum.Nearest;
        _title = MakeLabel("Imperial Conquest 2 · world map", 25);
        _status = MakeLabel("Loading original data…", 17);
        _title.Position = new Vector2(24, 16);
        _fitButton = new Button { Text = "Show whole map", CustomMinimumSize = new Vector2(170f, 38f) };
        _fitButton.Pressed += FitWholeMap;
        AddChild(_fitButton);

        try
        {
            var repositoryRoot = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
            var settings = AssetSettings.Load(Path.Combine(repositoryRoot, "assets.local.ini"));
            _world = WorldPrefix.Parse(File.ReadAllBytes(settings.DatPath));
            var image = Image.CreateEmpty(WorldPrefix.MapWidth, WorldPrefix.MapHeight, false, Image.Format.Rgba8);
            for (var y = 0; y < WorldPrefix.MapHeight; y++)
                for (var x = 0; x < WorldPrefix.MapWidth; x++)
                    image.SetPixel(x, y, TerrainColor(_world.CellAt(x, y)));
            _terrain = ImageTexture.CreateFromImage(image);
            _status.Text = "Near Rome · mouse wheel: zoom · drag: move map · click: inspect city · colors provisional";
            FocusOnRome();
            GD.Print($"Loaded {WorldPrefix.MapCellCount} map cells and {_world.Cities.Count} cities from the configured DAT.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _status.Text = $"Could not load original data: {ex.Message}";
            GD.PushError(_status.Text);
        }

        PlaceStatus();
        PlaceFitButton();
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationResized || _status is null || _fitButton is null) return;
        PlaceStatus();
        PlaceFitButton();
        ClampPan();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        if (_world is null || _terrain is null) return;

        var mapRect = MapRect();
        var viewport = MapViewportRect();
        var visible = mapRect.Intersection(viewport);
        if (visible.Size.X <= 0 || visible.Size.Y <= 0) return;
        var source = new Rect2(
            (visible.Position - mapRect.Position) / mapRect.Size * new Vector2(WorldPrefix.MapWidth, WorldPrefix.MapHeight),
            visible.Size / mapRect.Size * new Vector2(WorldPrefix.MapWidth, WorldPrefix.MapHeight));
        DrawTextureRectRegion(_terrain, visible, source);
        var step = mapRect.Size.X / WorldPrefix.MapWidth;
        var cityRadius = Math.Clamp(step * 0.48f, 2f, 6f);
        foreach (var city in _world.Cities)
        {
            var point = mapRect.Position + new Vector2((city.X + 0.5f) * step, (city.Y + 0.5f) * step);
            if (!viewport.Grow(-cityRadius).HasPoint(point)) continue;
            DrawCircle(point, cityRadius, CityColor);
        }
        if (_selected is not null)
        {
            var point = mapRect.Position + new Vector2((_selected.X + 0.5f) * step, (_selected.Y + 0.5f) * step);
            var radius = Math.Clamp(step * 1.1f, 6f, 14f);
            if (viewport.Grow(-radius).HasPoint(point))
                DrawArc(point, radius, 0, MathF.Tau, 32, SelectedColor, 2f);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_world is null) return;
        if (@event is InputEventMouseMotion motion && _dragging)
        {
            if (!_dragMoved && motion.Position.DistanceTo(_pressPosition) > DragThreshold)
                _dragMoved = true;
            if (_dragMoved)
            {
                _pan += motion.Relative;
                ClampPan();
                QueueRedraw();
            }
            AcceptEvent();
            return;
        }
        if (@event is not InputEventMouseButton click) return;

        if (click.Pressed && click.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            if (MapViewportRect().HasPoint(click.Position) && MapRect().HasPoint(click.Position))
                ZoomAt(click.Position, click.ButtonIndex == MouseButton.WheelUp ? ZoomStep : 1f / ZoomStep);
            AcceptEvent();
            return;
        }
        if (click.ButtonIndex != MouseButton.Left) return;
        if (click.Pressed)
        {
            _dragging = MapViewportRect().HasPoint(click.Position) && MapRect().HasPoint(click.Position);
            _dragMoved = false;
            _pressPosition = click.Position;
            if (_dragging) AcceptEvent();
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        if (!_dragMoved && click.Position.DistanceTo(_pressPosition) <= DragThreshold)
            SelectAt(click.Position);
        AcceptEvent();
    }

    private void SelectAt(Vector2 position)
    {
        if (_world is null || !MapViewportRect().HasPoint(position)) return;
        var mapRect = MapRect();
        if (!mapRect.HasPoint(position)) return;

        var step = mapRect.Size.X / WorldPrefix.MapWidth;
        var cell = (position - mapRect.Position) / step;
        CityRecord? nearest = null;
        var nearestSquared = float.MaxValue;
        foreach (var city in _world.Cities)
        {
            var dx = cell.X - (city.X + 0.5f);
            var dy = cell.Y - (city.Y + 0.5f);
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= nearestSquared) continue;
            nearest = city;
            nearestSquared = distanceSquared;
        }

        var hitRadius = MathF.Max(8f, Math.Clamp(step * 0.48f, 2f, 6f) + 3f) / step;
        _selected = nearestSquared <= hitRadius * hitRadius ? nearest : null;
        if (_selected is not null)
            _status.Text = $"{_selected.Name} · ({_selected.X}, {_selected.Y}) · initial supplies: {_selected.Supplies} tons";
        else
        {
            var x = Math.Clamp((int)cell.X, 0, WorldPrefix.MapWidth - 1);
            var y = Math.Clamp((int)cell.Y, 0, WorldPrefix.MapHeight - 1);
            _status.Text = $"Map cell ({x}, {y}) · raw terrain value {_world.CellAt(x, y)}";
        }
        QueueRedraw();
    }

    private void ZoomAt(Vector2 cursor, float factor)
    {
        var oldRect = MapRect();
        var normalized = (cursor - oldRect.Position) / oldRect.Size;
        var nextZoom = Math.Clamp(_zoom * factor, 1f, MaximumZoom);
        if (MathF.Abs(nextZoom - _zoom) < 0.0001f) return;
        _zoom = nextZoom;
        var baseRect = BaseMapRect();
        _pan = cursor - normalized * baseRect.Size * _zoom - baseRect.Position;
        ClampPan();
        QueueRedraw();
    }

    private void FocusOnRome()
    {
        if (_world is null) return;
        foreach (var city in _world.Cities)
        {
            if (city.Name != "Rome") continue;
            _zoom = InitialZoom;
            var viewport = MapViewportRect();
            var baseRect = BaseMapRect();
            var cityPosition = new Vector2((city.X + 0.5f) / WorldPrefix.MapWidth,
                (city.Y + 0.5f) / WorldPrefix.MapHeight);
            _pan = viewport.Position + viewport.Size / 2f - baseRect.Position - cityPosition * baseRect.Size * _zoom;
            ClampPan();
            return;
        }
    }

    private void FitWholeMap()
    {
        _zoom = 1f;
        _pan = Vector2.Zero;
        ClampPan();
        QueueRedraw();
    }

    private Rect2 MapViewportRect() => new(new Vector2(24f, 80f),
        new Vector2(MathF.Max(1f, Size.X - 48f), MathF.Max(1f, Size.Y - 150f)));

    private Rect2 BaseMapRect()
    {
        var viewport = MapViewportRect();
        var scale = MathF.Min(viewport.Size.X / WorldPrefix.MapWidth, viewport.Size.Y / WorldPrefix.MapHeight);
        var size = new Vector2(WorldPrefix.MapWidth * scale, WorldPrefix.MapHeight * scale);
        return new Rect2(viewport.Position + (viewport.Size - size) / 2f, size);
    }

    private Rect2 MapRect()
    {
        var baseRect = BaseMapRect();
        return new Rect2(baseRect.Position + _pan, baseRect.Size * _zoom);
    }

    private void ClampPan()
    {
        var viewport = MapViewportRect();
        var baseRect = BaseMapRect();
        var mapSize = baseRect.Size * _zoom;
        var x = mapSize.X <= viewport.Size.X
            ? viewport.Position.X + (viewport.Size.X - mapSize.X) / 2f
            : Math.Clamp(baseRect.Position.X + _pan.X, viewport.End.X - mapSize.X, viewport.Position.X);
        var y = mapSize.Y <= viewport.Size.Y
            ? viewport.Position.Y + (viewport.Size.Y - mapSize.Y) / 2f
            : Math.Clamp(baseRect.Position.Y + _pan.Y, viewport.End.Y - mapSize.Y, viewport.Position.Y);
        _pan = new Vector2(x, y) - baseRect.Position;
    }

    private void PlaceStatus() => _status.Position = new Vector2(24, MathF.Max(64f, Size.Y - 48f));

    private void PlaceFitButton() => _fitButton.Position = new Vector2(MathF.Max(24f, Size.X - 194f), 16f);

    private Label MakeLabel(string text, int fontSize)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        AddChild(label);
        return label;
    }

    private static Color TerrainColor(ushort value) => value switch
    {
        0 => new Color(0.13f, 0.30f, 0.49f),
        2 => new Color(0.46f, 0.66f, 0.37f),
        3 => new Color(0.89f, 0.80f, 0.49f),
        4 => new Color(0.26f, 0.42f, 0.26f),
        5 => new Color(0.65f, 0.66f, 0.67f),
        >= 6 and <= 11 => new Color(0.30f, 0.47f, 0.31f),
        >= 20 and < 200 => new Color(0.78f, 0.28f, 0.28f),
        >= 200 => new Color(0.93f, 0.15f, 0.70f),
        _ => new Color(0.72f, 0.53f, 0.33f)
    };
}
