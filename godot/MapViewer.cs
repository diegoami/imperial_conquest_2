using System;
using System.IO;
using Godot;
using IC2.Data;

public partial class MapViewer : Control
{
    private static readonly Color Background = new(0.07f, 0.11f, 0.15f);
    private static readonly Color CityColor = new(1.0f, 0.97f, 0.86f);
    private static readonly Color SelectedColor = new(1.0f, 0.40f, 0.25f);

    private WorldPrefix? _world;
    private ImageTexture? _terrain;
    private CityRecord? _selected;
    private Label _title = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        _title = MakeLabel("Imperial Conquest 2 · world map", 25);
        _status = MakeLabel("Loading original data…", 17);
        _title.Position = new Vector2(24, 16);

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
            _status.Text = "334 cities · click a city to inspect it · colors are provisional";
            GD.Print($"Loaded {WorldPrefix.MapCellCount} map cells and {_world.Cities.Count} cities from the configured DAT.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _status.Text = $"Could not load original data: {ex.Message}";
            GD.PushError(_status.Text);
        }

        PlaceStatus();
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationResized || _status is null) return;
        PlaceStatus();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Background);
        if (_world is null || _terrain is null) return;

        var mapRect = MapRect();
        DrawTextureRect(_terrain, mapRect, false);
        var step = mapRect.Size.X / WorldPrefix.MapWidth;
        foreach (var city in _world.Cities)
        {
            var point = mapRect.Position + new Vector2((city.X + 0.5f) * step, (city.Y + 0.5f) * step);
            DrawCircle(point, MathF.Max(2f, step * 0.48f), CityColor);
        }
        if (_selected is not null)
        {
            var point = mapRect.Position + new Vector2((_selected.X + 0.5f) * step, (_selected.Y + 0.5f) * step);
            DrawArc(point, MathF.Max(6f, step * 1.1f), 0, MathF.Tau, 32, SelectedColor, 2f);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click || _world is null)
            return;
        var mapRect = MapRect();
        if (!mapRect.HasPoint(click.Position)) return;

        var step = mapRect.Size.X / WorldPrefix.MapWidth;
        var cell = (click.Position - mapRect.Position) / step;
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

        _selected = nearestSquared <= 9f ? nearest : null;
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

    private Rect2 MapRect()
    {
        var usableWidth = MathF.Max(1f, Size.X - 48f);
        var usableHeight = MathF.Max(1f, Size.Y - 150f);
        var scale = MathF.Min(usableWidth / WorldPrefix.MapWidth, usableHeight / WorldPrefix.MapHeight);
        var width = WorldPrefix.MapWidth * scale;
        var height = WorldPrefix.MapHeight * scale;
        return new Rect2(new Vector2((Size.X - width) / 2f, 80f + (usableHeight - height) / 2f), new Vector2(width, height));
    }

    private void PlaceStatus() => _status.Position = new Vector2(24, MathF.Max(64f, Size.Y - 48f));

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
