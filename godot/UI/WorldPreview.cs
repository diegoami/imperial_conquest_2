using Godot;
using IC2.Engine.MapGen;
using IC2.Engine.Model;

namespace IC2.Slice.UI;

/// <summary>
/// T294f: the random-map screen's terrain thumbnail — one texture pixel per map cell at a fixed zoom,
/// with a dot for every settlement in its owner's colour. [designed] presentation: the original draws
/// one fixed Mediterranean map, so there is no manual reference for a generated preview; the palette
/// is this project's own, chosen for the shipped terrain ids.
/// </summary>
public static class WorldPreview
{
    /// <summary>Each map cell renders as scale × scale texture pixels.</summary>
    public const int Scale = 2;

    public static ImageTexture Render(World world)
    {
        var codes = world.Terrain.Decode(world.Width, world.Height);
        var width = world.Width * Scale;
        var height = world.Height * Scale;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < world.Height; y++)
            for (var x = 0; x < world.Width; x++)
                Fill(pixels, width, x * Scale, y * Scale, TerrainColor(codes[(y * world.Width) + x]));

        foreach (var city in world.Cities)
        {
            var owner = world.Nations.FirstOrDefault(n => n.Id == city.Owner);
            Fill(pixels, width, city.X * Scale, city.Y * Scale, Color.FromHtml(owner?.ColorHex ?? "#FFFFFF"));
        }

        var image = Image.CreateFromData(width, height, useMipmaps: false, Image.Format.Rgba8, pixels);
        return ImageTexture.CreateFromImage(image);
    }

    private static void Fill(byte[] pixels, int width, int x, int y, Color color)
    {
        for (var dy = 0; dy < Scale; dy++)
            for (var dx = 0; dx < Scale; dx++)
            {
                var at = ((y + dy) * width + (x + dx)) * 4;
                pixels[at] = (byte)color.R8;
                pixels[at + 1] = (byte)color.G8;
                pixels[at + 2] = (byte)color.B8;
                pixels[at + 3] = 255;
            }
    }

    private static Color TerrainColor(int code) => code switch
    {
        CanonicalTiles.SeaCalm => new Color(0.12f, 0.24f, 0.43f),
        CanonicalTiles.SeaRough => new Color(0.10f, 0.20f, 0.37f),
        CanonicalTiles.Desert => new Color(0.76f, 0.68f, 0.45f),
        CanonicalTiles.Forest => new Color(0.22f, 0.41f, 0.24f),
        CanonicalTiles.Mountains => new Color(0.49f, 0.47f, 0.51f),
        >= CanonicalTiles.River1 => new Color(0.33f, 0.51f, 0.71f),
        _ => new Color(0.44f, 0.60f, 0.36f), // plain
    };
}
