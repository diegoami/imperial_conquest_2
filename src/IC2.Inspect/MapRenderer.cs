using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;
using IC2.Data;

internal static class MapRenderer
{
    private const int Scale = 5;
    private const int Top = 88;
    private const int MapWidth = WorldPrefix.MapWidth * Scale;
    private const int MapHeight = WorldPrefix.MapHeight * Scale;

    private static readonly HashSet<string> LabelCities = new(StringComparer.Ordinal)
    {
        "Rome", "Carthago", "Alexandria", "Sidon", "Rhagae"
    };

    public static void Render(string datPath, string outputPath)
    {
        if (!string.Equals(Path.GetExtension(outputPath), ".svg", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Map output must have an .svg extension.", nameof(outputPath));
        var world = WorldPrefix.Parse(File.ReadAllBytes(datPath));
        var fullOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);
        using var writer = new StreamWriter(fullOutput, false, new UTF8Encoding(false));
        writer.WriteLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1600\" height=\"850\" viewBox=\"0 0 1600 850\">");
        writer.WriteLine("<rect width=\"1600\" height=\"850\" fill=\"#172430\"/>");
        writer.WriteLine("<text x=\"28\" y=\"38\" fill=\"#f8f2e8\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"27\" font-weight=\"700\">Imperial Conquest 2 · candidate world map</text>");
        writer.WriteLine("<text x=\"29\" y=\"64\" fill=\"#c3d0da\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"14\">320 × 140 cells · column-major DAT layout · palette is provisional</text>");

        for (var y = 0; y < WorldPrefix.MapHeight; y++)
        {
            for (var x = 0; x < WorldPrefix.MapWidth; x++)
            {
                var value = world.CellAt(x, y);
                writer.WriteLine($"<rect x=\"{x * Scale}\" y=\"{Top + y * Scale}\" width=\"{Scale}\" height=\"{Scale}\" fill=\"{Color(value)}\"/>");
            }
        }

        foreach (var city in world.Cities)
        {
            var cx = city.X * Scale + Scale / 2;
            var cy = Top + city.Y * Scale + Scale / 2;
            writer.WriteLine($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"2.2\" fill=\"#fff7e8\" stroke=\"#342a25\" stroke-width=\"0.9\"/>");
        }
        foreach (var city in world.Cities)
        {
            if (!LabelCities.Contains(city.Name)) continue;
            var cx = city.X * Scale + Scale / 2;
            var cy = Top + city.Y * Scale + Scale / 2;
            var textX = city.X > 285 ? cx - 8 : cx + 8;
            var anchor = city.X > 285 ? "end" : "start";
            var label = SecurityElement.Escape(city.Name);
            writer.WriteLine($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"5\" fill=\"#ee7258\" stroke=\"#fff7e8\" stroke-width=\"1.2\"/>");
            writer.WriteLine($"<text x=\"{textX}\" y=\"{cy - 7}\" text-anchor=\"{anchor}\" fill=\"#fffdf7\" stroke=\"#172430\" stroke-width=\"3\" paint-order=\"stroke\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"15\" font-weight=\"700\">{label}</text>");
        }

        writer.WriteLine("<text x=\"28\" y=\"818\" fill=\"#c3d0da\" font-family=\"Segoe UI, Arial, sans-serif\" font-size=\"14\">Each white dot is a candidate city coordinate. Cell values 20+ are highlighted; terrain meanings remain unverified.</text>");
        writer.WriteLine("</svg>");
        Console.WriteLine($"Rendered {world.Cells.Count} cells and {world.Cities.Count} cities to {fullOutput}");
    }

    private static string Color(ushort value) => value switch
    {
        0 => "#214c7c",
        2 => "#76a85e",
        3 => "#d2b97b",
        4 => "#967e5a",
        5 => "#b9c6c0",
        6 => "#d0a560",
        7 => "#4b844b",
        8 => "#786446",
        9 => "#975d37",
        10 => "#507d69",
        11 => "#9b7850",
        >= 20 and < 200 => "#c84748",
        >= 200 => "#ee27b2",
        _ => "#b78654"
    };
}
