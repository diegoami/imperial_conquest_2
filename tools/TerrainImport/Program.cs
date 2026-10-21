using System.Text.Json.Nodes;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Tools.TerrainImport;

// T201: the terrain importer's command line. See docs/scenario-authoring.md, "Authoring terrain from a PNG".
//   import <map.png> --world <world.json>   check the map and write the world's terrain sidecar
//   export --world <world.json> <map.png>   write a world's terrain as a PNG to edit
//   blank <width> <height> <map.png>        a map of calm sea to start painting on
//   palette                                 print the terrain colours
try
{
    return args switch
    {
        ["import", var png, "--world", var world] => Import(png, world),
        ["export", "--world", var world, var png] => Export(world, png),
        ["blank", var w, var h, var png] => Blank(int.Parse(w, System.Globalization.CultureInfo.InvariantCulture), int.Parse(h, System.Globalization.CultureInfo.InvariantCulture), png),
        ["palette"] => PrintPalette(),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is TerrainImportException or GameDataException or IOException or FormatException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int Import(string pngPath, string worldPath)
{
    var world = JsonNode.Parse(File.ReadAllText(worldPath))!.AsObject();
    var width = (int)world["width"]!;
    var height = (int)world["height"]!;
    var terrain = world["terrain"]?.AsObject();
    var dataFile = (string?)terrain?["dataFile"];
    if ((string?)terrain?["encoding"] != "base64" || string.IsNullOrWhiteSpace(dataFile))
    {
        var suggested = Path.GetFileNameWithoutExtension(worldPath) + ".terrain.b64";
        throw new TerrainImportException(
            $"'{worldPath}' must name its sidecar: set \"terrain\": {{ \"encoding\": \"base64\", \"dataFile\": \"{suggested}\" }}.");
    }

    var codes = TerrainImporter.CodesFrom(PngCodec.Read(File.ReadAllBytes(pngPath)), width, height);
    var sidecar = TerrainImporter.Sidecar(codes);

    // The engine's own loader is the final check, run before anything is written: the world, with the new
    // terrain inline in place of its sidecar, must load (tile types defined, cities on the map, and so on).
    var inline = world.DeepClone().AsObject();
    inline["terrain"] = new JsonObject { ["encoding"] = "base64", ["runs"] = null, ["data"] = sidecar.TrimEnd('\n') };
    var loaded = GameDataLoader.Load<World>(worldPath, inline.ToJsonString());

    var sidecarPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(worldPath))!, dataFile);
    File.WriteAllText(sidecarPath, sidecar, new System.Text.UTF8Encoding(false));
    Console.WriteLine($"Wrote {sidecarPath}: {width}×{height} tiles; '{loaded.Id}' loads.");
    return 0;
}

static int Export(string worldPath, string pngPath)
{
    var world = GameDataLoader.LoadFile<World>(worldPath);
    File.WriteAllBytes(pngPath, TerrainImporter.Png(world.Width, world.Height, world.Terrain.Decode(world.Width, world.Height)));
    Console.WriteLine($"Wrote {pngPath}: {world.Width}×{world.Height}.");
    return 0;
}

static int Blank(int width, int height, string pngPath)
{
    if (width is <= 0 or > TerrainImporter.MaxSide || height is <= 0 or > TerrainImporter.MaxSide)
    {
        throw new TerrainImportException($"A map must be 1–{TerrainImporter.MaxSide} tiles on each side.");
    }

    File.WriteAllBytes(pngPath, TerrainImporter.Png(width, height, new int[width * height]));
    Console.WriteLine($"Wrote {pngPath}: {width}×{height} of calm sea.");
    return 0;
}

static int PrintPalette()
{
    foreach (var swatch in TerrainImporter.Palette)
    {
        Console.WriteLine($"{swatch.Code,2}  {swatch.Hex}  {swatch.TileId}");
    }

    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("usage: import <map.png> --world <world.json> | export --world <world.json> <map.png> | blank <width> <height> <map.png> | palette");
    return 2;
}
