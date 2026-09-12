using IC2.Data;

if ((args.Length == 3 && args[0] == "--inspect-city") ||
    (args.Length == 5 && args[0] == "--config" && args[2] == "--inspect-city"))
{
    try
    {
        var configured = args[0] == "--config";
        var settings = AssetSettings.Load(configured ? args[1] : "assets.local.ini");
        var argStart = configured ? 3 : 1;
        var inspectedSavePath = settings.ResolveSavePath(args[argStart]);
        var data = File.ReadAllBytes(inspectedSavePath);
        var world = WorldPrefix.Parse(data);
        var queue = SaveRecruitmentTable.Parse(data);
        CityRecord? city = null;
        foreach (var candidate in world.Cities)
            if (string.Equals(candidate.Name, args[argStart + 1], StringComparison.OrdinalIgnoreCase))
            {
                city = candidate;
                break;
            }
        if (city is null) throw new ArgumentException($"City {args[argStart + 1]} was not found in {Path.GetFileName(inspectedSavePath)}.");
        Console.WriteLine($"{city.Name} at ({city.X}, {city.Y}) · controlled by {NationCatalog.Name(city.OwnerCode)} · allegiance to {NationCatalog.Name(city.AllegianceCode)}");
        Console.WriteLine($"Population {city.PopulationThousands * 1000:N0} · fortification {city.FortificationPercent}% · tribute {city.TributeTalents} talents · supplies {city.Supplies} tons · loyalty value {city.LoyaltyValue}");
        var found = 0;
        foreach (var entry in queue.Entries)
        {
            if (entry.CityIndex != city.Index) continue;
            if (found++ == 0) Console.WriteLine("Recruitment:");
            Console.WriteLine($"  {UnitCatalog.TypeName(entry.TypeCode)} · {entry.Troops:N0} troops · state code {entry.StateCode}");
        }
        if (found == 0) Console.WriteLine("No active recruitment entries were found for this city.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

if ((args.Length == 4 && args[0] == "--inspect-army") ||
    (args.Length == 6 && args[0] == "--config" && args[2] == "--inspect-army"))
{
    try
    {
        var configured = args[0] == "--config";
        var settings = AssetSettings.Load(configured ? args[1] : "assets.local.ini");
        var argStart = configured ? 3 : 1;
        if (!ushort.TryParse(args[argStart + 1], out var x) || !ushort.TryParse(args[argStart + 2], out var y))
            throw new ArgumentException("Army coordinates must be nonnegative whole numbers.");
        var inspectedSavePath = settings.ResolveSavePath(args[argStart]);
        var table = SaveArmyTable.Parse(File.ReadAllBytes(inspectedSavePath));
        var found = false;
        foreach (var army in table.Armies)
        {
            if (army.X != x || army.Y != y) continue;
            found = true;
            Console.WriteLine($"Army {army.Index} at ({x}, {y}) · owner code {army.OwnerCode}");
            Console.WriteLine($"{army.Units.Count} units · {army.TotalTroops:N0} troops · {army.Supplies} tons supply · {army.Money} money");
            foreach (var unit in army.Units)
            {
                Console.WriteLine($"  {unit.Name} · {UnitCatalog.TypeName(unit.TypeCode)} · {unit.Troops:N0} · {UnitCatalog.QualityName(unit.QualityCode)}");
            }
        }
        if (!found) throw new ArgumentException($"No army record at ({x}, {y}) in {Path.GetFileName(inspectedSavePath)}.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

if ((args.Length == 2 && args[0] == "--render-map") ||
    (args.Length == 4 && args[0] == "--config" && args[2] == "--render-map"))
{
    try
    {
        var configured = args[0] == "--config";
        var settings = AssetSettings.Load(configured ? args[1] : "assets.local.ini");
        var outputPath = args[configured ? 3 : 1];
        MapRenderer.Render(settings.DatPath, outputPath);
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

if ((args.Length == 3 && args[0] == "--compare-saves") ||
    (args.Length == 5 && args[0] == "--config" && args[2] == "--compare-saves"))
{
    try
    {
        var configured = args[0] == "--config";
        var settings = AssetSettings.Load(configured ? args[1] : "assets.local.ini");
        var first = settings.ResolveSavePath(args[configured ? 3 : 1]);
        var second = settings.ResolveSavePath(args[configured ? 4 : 2]);
        SaveComparer.Compare(first, second);
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

string datPath;
string? savePath = null;
if (args.Length == 0 || (args.Length == 2 && args[0] == "--save") ||
    (args.Length == 2 && args[0] == "--config") ||
    (args.Length == 4 && args[0] == "--config" && args[2] == "--save"))
{
    try
    {
        var configPath = args.Length >= 2 && args[0] == "--config" ? args[1] : "assets.local.ini";
        var settings = AssetSettings.Load(configPath);
        datPath = settings.DatPath;
        if (args.Length >= 2 && args[^2] == "--save")
            savePath = settings.ResolveSavePath(args[^1]);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}
else if (args.Length is 1 or 2 && args[0] != "--save" && args[0] != "--config")
{
    datPath = args[0];
    if (args.Length == 2) savePath = args[1];
}
else
{
    Console.Error.WriteLine("Usage: IC2.Inspect [--save <save.sav>] | [--config <assets.ini> [--save <save.sav>]] | [--config <assets.ini>] --compare-saves <first.sav> <second.sav> | [--config <assets.ini>] --inspect-city <save.sav> <city-name> | [--config <assets.ini>] --inspect-army <save.sav> <x> <y> | [--config <assets.ini>] --render-map <output.svg> | <data.dat> [save.sav]");
    return 2;
}

try
{
    var dat = WorldPrefix.Parse(File.ReadAllBytes(datPath));
    Console.WriteLine($"DAT: {dat.Cells.Count} map cells, {dat.Cities.Count} city records");
    Console.WriteLine($"Candidate map: {WorldPrefix.MapWidth} × {WorldPrefix.MapHeight}");
    Console.WriteLine($"First/last city: {dat.Cities[0].Name} / {dat.Cities[^1].Name}");

    if (savePath is not null)
    {
        var save = WorldPrefix.Parse(File.ReadAllBytes(savePath));
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
