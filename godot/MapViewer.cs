using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;
using IC2.Data;

public partial class MapViewer : Control
{
    private const float ZoomStep = 1.25f;
    private const float MaximumZoom = 10f;
    private const float InitialZoom = 4f;
    private const float DragThreshold = 4f;
    private static readonly Color Background = new(0.07f, 0.11f, 0.15f);
    private static readonly Color CityColor = new(1.0f, 0.97f, 0.86f);
    private static readonly Color SelectedColor = new(1.0f, 0.40f, 0.25f);
    private static readonly Color RiverColor = new(0.05f, 0.16f, 0.58f);
    private static readonly Color MarkerOutline = new(0.07f, 0.09f, 0.14f);
    private static readonly Color FleetColor = new(0.88f, 0.96f, 1.0f);

    private WorldPrefix? _initialWorld;
    private WorldPrefix? _world;
    private SaveArmyTable? _armyTable;
    private SaveRecruitmentTable? _recruitmentTable;
    private ImageTexture? _terrain;
    private CityRecord? _selected;
    private UnitMarker? _selectedUnit;
    private readonly List<UnitMarker> _units = new();
    private readonly List<(int X, int Y, ushort Code)> _rivers = new();
    private readonly List<string> _sourcePaths = new();
    private Label _title = null!;
    private Label _legend = null!;
    private Label _status = null!;
    private Button _fitButton = null!;
    private OptionButton _sourcePicker = null!;
    private PanelContainer _detailPanel = null!;
    private Label _detailText = null!;
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
        _legend = MakeLabel("Colored squares: cities  ·  flags: armies  ·  sails: fleets  ·  blue lines: rivers", 14);
        _status = MakeLabel("Loading original data…", 17);
        _title.Position = new Vector2(24, 16);
        _legend.Position = new Vector2(24, 53);
        _fitButton = new Button { Text = "Show whole map", CustomMinimumSize = new Vector2(170f, 38f) };
        _fitButton.Pressed += FitWholeMap;
        AddChild(_fitButton);
        _sourcePicker = new OptionButton { CustomMinimumSize = new Vector2(170f, 38f) };
        _sourcePicker.ItemSelected += index => LoadSource((int)index);
        AddChild(_sourcePicker);
        _detailPanel = new PanelContainer { Visible = false, MouseFilter = MouseFilterEnum.Stop };
        var detailScroll = new ScrollContainer();
        _detailText = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _detailText.AddThemeFontSizeOverride("font_size", 15);
        detailScroll.AddChild(_detailText);
        _detailPanel.AddChild(detailScroll);
        AddChild(_detailPanel);

        try
        {
            var repositoryRoot = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
            var settings = AssetSettings.Load(Path.Combine(repositoryRoot, "assets.local.ini"));
            _initialWorld = WorldPrefix.Parse(File.ReadAllBytes(settings.DatPath));
            for (var y = 0; y < WorldPrefix.MapHeight; y++)
                for (var x = 0; x < WorldPrefix.MapWidth; x++)
                {
                    var code = _initialWorld.CellAt(x, y);
                    if (code is >= 6 and <= 11) _rivers.Add((x, y, code));
                }
            _sourcePaths.Add(settings.DatPath);
            _sourcePicker.AddItem("Initial world");
            var savesDirectory = Path.Combine(settings.DirectoryPath, "saves");
            if (Directory.Exists(savesDirectory))
            {
                var saves = Directory.GetFiles(savesDirectory, "*.sav");
                Array.Sort(saves, StringComparer.OrdinalIgnoreCase);
                foreach (var save in saves)
                {
                    _sourcePaths.Add(save);
                    _sourcePicker.AddItem(Path.GetFileName(save));
                }
            }
            LoadSource(0);
            FocusOnRome();
            GD.Print($"Loaded {WorldPrefix.MapCellCount} map cells, {_initialWorld.Cities.Count} cities, and {_rivers.Count} river tiles from the configured DAT.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _status.Text = $"Could not load original data: {ex.Message}";
            GD.PushError(_status.Text);
        }

        PlaceStatus();
        PlaceHeaderControls();
        PlaceDetailPanel();
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what != NotificationResized || _status is null || _fitButton is null || _sourcePicker is null) return;
        PlaceStatus();
        PlaceHeaderControls();
        PlaceDetailPanel();
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
        foreach (var (x, y, code) in _rivers)
        {
            var point = mapRect.Position + new Vector2((x + 0.5f) * step, (y + 0.5f) * step);
            if (viewport.Grow(step / 2f).HasPoint(point)) DrawRiver(point, step, code);
        }
        foreach (var city in _world.Cities)
        {
            var point = mapRect.Position + new Vector2((city.X + 0.5f) * step, (city.Y + 0.5f) * step);
            if (viewport.HasPoint(point)) DrawCity(point, step, city.OwnerCode);
        }
        foreach (var unit in _units)
        {
            var point = mapRect.Position + new Vector2((unit.X + 0.5f) * step, (unit.Y + 0.5f) * step);
            if (!viewport.HasPoint(point)) continue;
            if (unit.Fleet) DrawFleet(point, step, unit.OwnerCode);
            else DrawArmy(point, step, unit.OwnerCode);
        }
        if (_selected is not null) DrawSelection(mapRect, step, _selected.X, _selected.Y);
        if (_selectedUnit is { } selectedUnit) DrawSelection(mapRect, step, selectedUnit.X, selectedUnit.Y);
        DrawFrameMasks(viewport);
    }

    private void DrawRiver(Vector2 center, float step, ushort code)
    {
        var half = step / 2f;
        var width = MathF.Max(1.4f, step * 0.34f);
        if (code is 6 or 8 or 9) DrawLine(center, center + Vector2.Right * half, RiverColor, width);
        if (code is 6 or 10 or 11) DrawLine(center, center + Vector2.Left * half, RiverColor, width);
        if (code is 7 or 8 or 11) DrawLine(center, center + Vector2.Up * half, RiverColor, width);
        if (code is 7 or 9 or 10) DrawLine(center, center + Vector2.Down * half, RiverColor, width);
    }

    private void DrawCity(Vector2 center, float step, ushort ownerCode)
    {
        var side = Math.Clamp(step * 0.8f, 3f, 17f);
        var rect = new Rect2(center - Vector2.One * side / 2f, Vector2.One * side);
        DrawRect(rect, OwnerColor(ownerCode));
        DrawRect(rect, MarkerOutline, false, 1f);
        if (side < 10f) return;
        var glyphColor = ownerCode is 4 or 5 or 7 or 8 or 13 or 14 ? MarkerOutline : CityColor;
        var roofY = center.Y - side * 0.22f;
        DrawLine(new Vector2(center.X - side * 0.28f, roofY), new Vector2(center.X + side * 0.28f, roofY), glyphColor, 1.5f);
        DrawLine(new Vector2(center.X - side * 0.22f, roofY), new Vector2(center.X - side * 0.22f, center.Y + side * 0.26f), glyphColor, 1.5f);
        DrawLine(new Vector2(center.X + side * 0.22f, roofY), new Vector2(center.X + side * 0.22f, center.Y + side * 0.26f), glyphColor, 1.5f);
    }

    private void DrawArmy(Vector2 center, float step, ushort ownerCode)
    {
        var radius = Math.Clamp(step * 0.46f, 3f, 12f);
        DrawCircle(center, radius, MarkerOutline);
        var poleX = center.X - radius * 0.22f;
        DrawLine(new Vector2(poleX, center.Y - radius * 0.65f), new Vector2(poleX, center.Y + radius * 0.65f), FleetColor, MathF.Max(1f, radius * 0.16f));
        DrawColoredPolygon(new[] {
            new Vector2(poleX, center.Y - radius * 0.7f),
            new Vector2(center.X + radius * 0.7f, center.Y - radius * 0.27f),
            new Vector2(poleX, center.Y + radius * 0.1f)
        }, OwnerColor(ownerCode));
    }

    private void DrawFleet(Vector2 center, float step, ushort ownerCode)
    {
        var radius = Math.Clamp(step * 0.46f, 3f, 12f);
        DrawCircle(center, radius, OwnerColor(ownerCode));
        DrawLine(center + new Vector2(-radius * 0.7f, radius * 0.35f),
            center + new Vector2(radius * 0.7f, radius * 0.35f), FleetColor, MathF.Max(1.4f, radius * 0.22f));
        DrawColoredPolygon(new[] {
            center + new Vector2(0f, -radius * 0.7f),
            center + new Vector2(0f, radius * 0.18f),
            center + new Vector2(radius * 0.6f, radius * 0.18f)
        }, FleetColor);
    }

    private void DrawSelection(Rect2 mapRect, float step, int x, int y)
    {
        var point = mapRect.Position + new Vector2((x + 0.5f) * step, (y + 0.5f) * step);
        var radius = Math.Clamp(step * 0.85f, 7f, 20f);
        if (MapViewportRect().Grow(-radius).HasPoint(point))
            DrawArc(point, radius, 0, MathF.Tau, 32, SelectedColor, 2f);
    }

    private void DrawFrameMasks(Rect2 viewport)
    {
        DrawRect(new Rect2(0, 0, Size.X, viewport.Position.Y), Background);
        DrawRect(new Rect2(0, viewport.End.Y, Size.X, Size.Y - viewport.End.Y), Background);
        DrawRect(new Rect2(0, viewport.Position.Y, viewport.Position.X, viewport.Size.Y), Background);
        DrawRect(new Rect2(viewport.End.X, viewport.Position.Y, Size.X - viewport.End.X, viewport.Size.Y), Background);
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
        UnitMarker? nearestUnit = null;
        var unitDistanceSquared = float.MaxValue;
        foreach (var unit in _units)
        {
            var point = mapRect.Position + new Vector2((unit.X + 0.5f) * step, (unit.Y + 0.5f) * step);
            var distanceSquared = point.DistanceSquaredTo(position);
            if (distanceSquared >= unitDistanceSquared) continue;
            nearestUnit = unit;
            unitDistanceSquared = distanceSquared;
        }
        if (nearestUnit is { } hitUnit && unitDistanceSquared <= MathF.Pow(MathF.Max(8f, step * 0.5f), 2f))
        {
            _selected = null;
            _selectedUnit = hitUnit;
            var army = !hitUnit.Fleet ? FindArmy(hitUnit.X, hitUnit.Y) : null;
            if (army is not null)
            {
                _status.Text = $"{NationCatalog.Name(army.OwnerCode)} army at ({army.X}, {army.Y}) · {army.Units.Count} units · {army.TotalTroops:N0} troops · {army.Supplies} tons supply · {army.Money} money";
                _detailText.Text = FormatArmy(army);
                _detailPanel.Visible = true;
            }
            else
            {
                _detailPanel.Visible = false;
                _status.Text = $"{NationCatalog.Name(hitUnit.OwnerCode)} {(hitUnit.Fleet ? "fleet" : "army")} marker at ({hitUnit.X}, {hitUnit.Y}) · details still under study";
            }
            QueueRedraw();
            return;
        }
        _detailPanel.Visible = false;
        _selectedUnit = null;
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
        {
            _status.Text = $"{_selected.Name} · ({_selected.X}, {_selected.Y}) · controlled by {NationCatalog.Name(_selected.OwnerCode)} · {_selected.PopulationThousands * 1000:N0} people · {_selected.Supplies} tons supply";
            _detailText.Text = FormatCity(_selected);
            _detailPanel.Visible = true;
        }
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

    private void LoadSource(int index)
    {
        if (_initialWorld is null || (uint)index >= _sourcePaths.Count) return;
        try
        {
            var saveData = index == 0 ? null : File.ReadAllBytes(_sourcePaths[index]);
            var world = saveData is null ? _initialWorld : WorldPrefix.Parse(saveData);
            var armyTable = saveData is null ? null : SaveArmyTable.Parse(saveData);
            SaveRecruitmentTable? recruitmentTable = null;
            if (saveData is not null)
            {
                try { recruitmentTable = SaveRecruitmentTable.Parse(saveData); }
                catch (InvalidDataException ex) { GD.Print($"Recruitment details unavailable for {Path.GetFileName(_sourcePaths[index])}: {ex.Message}"); }
            }
            var image = Image.CreateEmpty(WorldPrefix.MapWidth, WorldPrefix.MapHeight, false, Image.Format.Rgba8);
            var units = new List<UnitMarker>();
            for (var y = 0; y < WorldPrefix.MapHeight; y++)
            {
                for (var x = 0; x < WorldPrefix.MapWidth; x++)
                {
                    var code = world.CellAt(x, y);
                    var backgroundCode = code;
                    if (code >= 20)
                    {
                        var originalCode = _initialWorld.CellAt(x, y);
                        backgroundCode = originalCode < 20 ? originalCode : (ushort)(code >= 300 ? 0 : 2);
                    }
                    image.SetPixel(x, y, TerrainColor(backgroundCode));
                    if (code is >= 200 and < 300)
                        units.Add(new UnitMarker(x, y, code, (ushort)((code - 200) % 16), false));
                    else if (code is >= 332 and < 348)
                        units.Add(new UnitMarker(x, y, code, (ushort)(code - 332), true));
                }
            }
            _world = world;
            _armyTable = armyTable;
            _recruitmentTable = recruitmentTable;
            _terrain = ImageTexture.CreateFromImage(image);
            _units.Clear();
            _units.AddRange(units);
            _selected = null;
            _selectedUnit = null;
            _detailPanel.Visible = false;
            var armies = 0;
            foreach (var unit in _units) if (!unit.Fleet) armies++;
            _status.Text = $"{_sourcePicker.GetItemText(index)} · {armies} army and {_units.Count - armies} fleet markers · wheel: zoom · drag: move";
            QueueRedraw();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _status.Text = $"Could not load {Path.GetFileName(_sourcePaths[index])}: {ex.Message}";
            GD.PushError(_status.Text);
        }
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

    private void PlaceHeaderControls()
    {
        _fitButton.Position = new Vector2(MathF.Max(24f, Size.X - 194f), 16f);
        _sourcePicker.Position = new Vector2(MathF.Max(24f, Size.X - 380f), 16f);
    }

    private void PlaceDetailPanel()
    {
        _detailPanel.Position = new Vector2(MathF.Max(24f, Size.X - 414f), 80f);
        _detailPanel.Size = new Vector2(MathF.Min(390f, Size.X - 48f), MathF.Max(100f, Size.Y - 160f));
    }

    private ArmyRecord? FindArmy(int x, int y)
    {
        if (_armyTable is null) return null;
        foreach (var army in _armyTable.Armies)
            if (army.X == x && army.Y == y) return army;
        return null;
    }

    private static string FormatArmy(ArmyRecord army)
    {
        var text = new StringBuilder();
        text.AppendLine($"{NationCatalog.Name(army.OwnerCode)} army at ({army.X}, {army.Y})");
        text.AppendLine($"{army.Units.Count} units · {army.TotalTroops:N0} troops");
        text.AppendLine($"Supply {army.Supplies} tons · money {army.Money}");
        text.AppendLine();
        foreach (var unit in army.Units)
        {
            text.AppendLine(unit.Name);
            text.AppendLine($"  {unit.Troops:N0} · {UnitCatalog.TypeName(unit.TypeCode)} · {UnitCatalog.QualityName(unit.QualityCode)}");
        }
        return text.ToString();
    }

    private string FormatCity(CityRecord city)
    {
        var text = new StringBuilder();
        text.AppendLine($"{city.Name} at ({city.X}, {city.Y})");
        text.AppendLine($"Controlled by {NationCatalog.Name(city.OwnerCode)}");
        text.AppendLine($"Allegiance to {NationCatalog.Name(city.AllegianceCode)}");
        text.AppendLine($"Population {city.PopulationThousands * 1000:N0}");
        text.AppendLine($"Fortification {city.FortificationPercent}%");
        text.AppendLine($"Tribute {city.TributeTalents} talents");
        text.AppendLine($"Supply {city.Supplies} tons");
        text.AppendLine($"Loyalty value {city.LoyaltyValue}");
        if (_recruitmentTable is null)
        {
            if (_armyTable is not null) text.AppendLine("Recruitment details unavailable for this save");
            return text.ToString();
        }
        var count = 0;
        foreach (var entry in _recruitmentTable.Entries)
            if (entry.CityIndex == city.Index) count++;
        if (count == 0) return text.ToString();
        text.AppendLine();
        text.AppendLine($"Recruiting {count} units:");
        foreach (var entry in _recruitmentTable.Entries)
        {
            if (entry.CityIndex != city.Index) continue;
            text.AppendLine($"{UnitCatalog.TypeName(entry.TypeCode)} · {entry.Troops:N0}");
        }
        return text.ToString();
    }

    private readonly record struct UnitMarker(int X, int Y, ushort Code, ushort OwnerCode, bool Fleet);

    private static Color OwnerColor(ushort code) => code switch
    {
        0 => new Color(0.50f, 0f, 0.50f),
        1 => new Color(1f, 0f, 0f),
        2 => new Color(0.50f, 0.50f, 0f),
        3 => new Color(0f, 0f, 0.50f),
        4 => new Color(1f, 1f, 1f),
        5 => new Color(0f, 1f, 0f),
        6 => new Color(0.50f, 0f, 0f),
        7 => new Color(0f, 1f, 1f),
        8 => new Color(1f, 1f, 0f),
        9 => new Color(0f, 0f, 0.50f),
        10 => new Color(0f, 0.50f, 0f),
        11 => new Color(0f, 0.50f, 0.50f),
        12 => new Color(0f, 0f, 1f),
        13 => new Color(1f, 0f, 1f),
        14 => new Color(1f, 0f, 0f),
        15 => new Color(0.50f, 0.50f, 0.50f),
        _ => new Color(0.72f, 0.71f, 0.59f)
    };

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
        1 => new Color(0.09f, 0.20f, 0.42f),
        2 => new Color(0.46f, 0.66f, 0.37f),
        3 => new Color(0.89f, 0.80f, 0.49f),
        4 => new Color(0.26f, 0.42f, 0.26f),
        5 => new Color(0.65f, 0.66f, 0.67f),
        >= 6 and <= 11 => new Color(0.26f, 0.42f, 0.26f),
        >= 20 and < 300 => new Color(0.46f, 0.66f, 0.37f),
        >= 300 => new Color(0.13f, 0.30f, 0.49f),
        _ => new Color(0.72f, 0.53f, 0.33f)
    };
}
