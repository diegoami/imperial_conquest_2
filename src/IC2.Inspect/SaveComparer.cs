using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using IC2.Data;

internal static class SaveComparer
{
    public static void Compare(string firstPath, string secondPath)
    {
        var firstBytes = File.ReadAllBytes(firstPath);
        var secondBytes = File.ReadAllBytes(secondPath);
        var first = WorldPrefix.Parse(firstBytes);
        var second = WorldPrefix.Parse(secondBytes);

        Console.WriteLine($"First: {Path.GetFileName(firstPath)}, {firstBytes.Length} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(firstBytes)).ToLowerInvariant()}");
        Console.WriteLine($"Second: {Path.GetFileName(secondPath)}, {secondBytes.Length} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(secondBytes)).ToLowerInvariant()}");
        Console.WriteLine($"Size difference: {secondBytes.Length - firstBytes.Length:+#;-#;0} bytes");

        var transitions = new SortedDictionary<(ushort Old, ushort New), int>();
        var changedCells = 0;
        for (var i = 0; i < WorldPrefix.MapCellCount; i++)
        {
            var oldValue = first.Cells[i];
            var newValue = second.Cells[i];
            if (oldValue == newValue) continue;
            changedCells++;
            var key = (oldValue, newValue);
            transitions.TryGetValue(key, out var count);
            transitions[key] = count + 1;
        }
        Console.WriteLine($"Candidate map: {changedCells} changed cells");
        foreach (var entry in transitions)
            Console.WriteLine($"  {entry.Key.Old} → {entry.Key.New}: {entry.Value}");

        var changedCities = 0;
        var changedOffsets = new int[WorldPrefix.CityRecordLength];
        var changedWord24 = 0;
        var increasedWord24 = 0;
        var decreasedWord24 = 0;
        for (var i = 0; i < WorldPrefix.CityCount; i++)
        {
            var before = first.Cities[i];
            var after = second.Cities[i];
            if (before.Name != after.Name || before.X != after.X || before.Y != after.Y)
                throw new InvalidDataException($"City identity or candidate coordinates differ at record {i}.");

            var cityChanged = false;
            var unusualOffsets = new List<int>();
            for (var j = 0; j < WorldPrefix.CityRecordLength; j++)
            {
                if (before.RawByteAt(j) == after.RawByteAt(j)) continue;
                cityChanged = true;
                changedOffsets[j]++;
                if (j is not (24 or 25)) unusualOffsets.Add(j);
            }
            if (cityChanged) changedCities++;
            if (unusualOffsets.Count > 0)
                Console.WriteLine($"City {i} {before.Name}: additional changed record byte offsets {string.Join(", ", unusualOffsets)}");

            var oldWord24 = (ushort)(before.RawByteAt(24) | before.RawByteAt(25) << 8);
            var newWord24 = (ushort)(after.RawByteAt(24) | after.RawByteAt(25) << 8);
            if (oldWord24 == newWord24) continue;
            changedWord24++;
            if (newWord24 > oldWord24) increasedWord24++;
            else decreasedWord24++;
        }
        Console.WriteLine($"City records: {changedCities} changed of {WorldPrefix.CityCount}");
        Console.WriteLine($"Unlabelled word +24: {changedWord24} changed ({increasedWord24} rose, {decreasedWord24} fell)");
        Console.WriteLine("Changed city-record byte offsets (number of cities):");
        for (var j = 0; j < changedOffsets.Length; j++)
            if (changedOffsets[j] > 0) Console.WriteLine($"  +{j}: {changedOffsets[j]}");
    }
}
