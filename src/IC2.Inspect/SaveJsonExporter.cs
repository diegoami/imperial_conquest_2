using System.Text.Json;
using System.Text.Json.Serialization;
using IC2.Data;

internal static class SaveJsonExporter
{
    public static void Export(string savePath, string outputPath)
    {
        var data = File.ReadAllBytes(savePath);
        var world = WorldPrefix.Parse(data);
        var turn = SaveTurnState.Parse(data);
        var nations = SaveNationTable.Parse(data);
        var recruitment = SaveRecruitmentTable.Parse(data);
        var armies = SaveArmyTable.Parse(data);
        var fleets = SaveFleetTable.Parse(data);
        var mercenaries = SaveMercenaryTable.Parse(data);

        var document = new
        {
            source = Path.GetFileName(savePath),
            turn = new
            {
                week = turn.Week,
                season = turn.SeasonName,
                year = turn.YearBc,
                currentNation = NationRef(turn.CurrentNationCode)
            },
            map = new { width = WorldPrefix.MapWidth, height = WorldPrefix.MapHeight },
            nations = nations.Nations.Select(n => new
            {
                code = n.Code,
                name = n.Name,
                leader = n.Leader,
                humanPlayer = n.HumanPlayer,
                capital = CityRef(world, n.CapitalCityIndex),
                cityCount = n.CityCount,
                mapCityCount = world.Cities.Count(c => c.OwnerCode == n.Code),
                taxRatePercent = n.TaxRatePercent,
                mobilizedPercent = n.MobilizedPercent,
                treasuryTalents = n.Treasury,
                unityValue = n.UnityValue
            }),
            cities = world.Cities.Select(c => new
            {
                index = c.Index,
                name = c.Name,
                x = c.X,
                y = c.Y,
                owner = NationRef(c.OwnerCode),
                allegiance = NationRef(c.AllegianceCode),
                populationThousands = c.PopulationThousands,
                referencePopulationThousands = c.ReferencePopulationThousands,
                fortificationPercent = c.FortificationPercent,
                tributeTalents = c.TributeTalents,
                supplyTons = c.Supplies,
                loyaltyValue = c.LoyaltyValue,
                garrisonTroops = recruitment.TroopsAtCity(c.Index),
                garrisonUnits = recruitment.Entries.Where(e => e.CityIndex == c.Index).Select(e => new
                {
                    typeCode = e.TypeCode,
                    typeName = UnitCatalog.TypeName(e.TypeCode),
                    troops = e.Troops,
                    stateCode = e.StateCode
                })
            }),
            armies = armies.Armies.Select(a => new
            {
                index = a.Index,
                x = a.X,
                y = a.Y,
                owner = NationRef(a.OwnerCode),
                moves = a.Moves,
                moraleValue = a.MoraleValue,
                supplyTons = a.Supplies,
                money = a.Money,
                totalTroops = a.TotalTroops,
                units = a.Units.Select(u => new
                {
                    slot = u.Slot,
                    name = u.Name,
                    typeCode = u.TypeCode,
                    typeName = UnitCatalog.TypeName(u.TypeCode),
                    troops = u.Troops,
                    qualityCode = u.QualityCode,
                    qualityName = UnitCatalog.QualityName(u.QualityCode)
                })
            }),
            fleets = fleets.Fleets.Select(f => new
            {
                index = f.Index,
                x = f.X,
                y = f.Y,
                shipCount = f.ShipCount,
                cityIndex = f.CityIndex,
                cityName = f.CityIndex < WorldPrefix.CityCount ? world.Cities[f.CityIndex].Name : null
            }),
            mercenaryOffers = mercenaries.Records.Where(m => !m.IsEmpty).Select(m => new
            {
                index = m.Index,
                x = m.X,
                y = m.Y,
                label = m.Label,
                typeCode = m.TypeCode,
                typeName = UnitCatalog.TypeName(m.TypeCode),
                troops = m.Troops,
                qualityCode = m.QualityCode,
                qualityName = UnitCatalog.QualityName(m.QualityCode)
            })
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, options));

        static object NationRef(ushort code) => new { code, name = NationCatalog.Name(code) };
        static object? CityRef(WorldPrefix world, ushort index) =>
            index == SaveNationTable.NoCapitalSentinel ? null : new { index, name = world.Cities[index].Name };
    }
}
