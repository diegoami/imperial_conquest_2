using IC2.Data;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: IC2.Inspect <Imperial Conquest 2.dat> [save.sav]");
    return 2;
}

try
{
    var dat = WorldPrefix.Parse(File.ReadAllBytes(args[0]));
    Console.WriteLine($"DAT: {dat.Cells.Count} map cells, {dat.Cities.Count} city records");
    Console.WriteLine($"Candidate map: {WorldPrefix.MapWidth} × {WorldPrefix.MapHeight}");
    Console.WriteLine($"First/last city: {dat.Cities[0].Name} / {dat.Cities[^1].Name}");

    if (args.Length == 2)
    {
        var save = WorldPrefix.Parse(File.ReadAllBytes(args[1]));
        var changedCells = 0;
        var zeroToOne = 0;
        for (var i = 0; i < dat.Cells.Count; i++)
        {
            if (dat.Cells[i] == save.Cells[i]) continue;
            changedCells++;
            if (dat.Cells[i] == 0 && save.Cells[i] == 1) zeroToOne++;
        }

        var changedCities = 0;
        for (var i = 0; i < dat.Cities.Count; i++)
        {
            var initial = dat.Cities[i];
            var saved = save.Cities[i];
            if (initial.Name != saved.Name || initial.X != saved.X || initial.Y != saved.Y)
                throw new InvalidDataException($"City identity/coordinates differ at record {i}: {initial.Name} vs {saved.Name}.");
            var changedOffsets = new List<int>();
            for (var j = 0; j < WorldPrefix.CityRecordLength; j++)
                if (initial.RawByteAt(j) != saved.RawByteAt(j)) changedOffsets.Add(j);
            if (changedOffsets.Count == 0) continue;
            changedCities++;
            Console.WriteLine($"City {i} {initial.Name}: changed record byte offsets {string.Join(", ", changedOffsets)}");
        }
        Console.WriteLine($"SAV: {changedCells} changed map cells ({zeroToOne} changed 0 → 1), {changedCities} changed city records");
    }
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

