using System.Text.Json;
using System.Text.Json.Serialization;
using IC2.Data;

public static class SaveJsonExporter
{
    public static void Export(string savePath, string outputPath)
    {
        var data = File.ReadAllBytes(savePath);
        var world = WorldPrefix.Parse(data);
        var nations = SaveNationTable.Parse(data);
        var recruitment = SaveRecruitmentTable.Parse(data);
        var armies = SaveArmyTable.Parse(data);
        var fleets = SaveFleetTable.Parse(data);

        // Neither the calendar/current-turn trailer nor the mercenary-offer pool exists in the DAT
        // at all (see SaveTurnState/SaveMercenaryTable's own DatDataNotPresentException messages),
        // so export null / an empty list for those rather than letting the whole export crash on a
        // file every other section of this document parses cleanly.
        object? turnJson = null;
        try
        {
            var turn = SaveTurnState.Parse(data);
            turnJson = new
            {
                week = turn.Week,
                season = turn.SeasonName,
                year = turn.YearBc,
                currentNation = NationRef(turn.CurrentNationCode)
            };
        }
        catch (DatDataNotPresentException)
        {
        }

        // T34 #40 item 7: on a DAT input this must export null, exactly like turnJson above — not an
        // empty array, which would be a fabricated "empty pool" indistinguishable from a SAV that
        // genuinely has zero available offers.
        IEnumerable<object>? mercenaryOffersJson = null;
        try
        {
            mercenaryOffersJson = SaveMercenaryTable.Parse(data).Records
                .Where(m => !m.IsEmpty)
                .Select(m => new
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
                });
        }
        catch (DatDataNotPresentException)
        {
        }

        var document = new
        {
            source = Path.GetFileName(savePath),
            turn = turnJson,
            map = new { width = WorldPrefix.MapWidth, height = WorldPrefix.MapHeight },
            nations = nations.Nations.Select(n => new
            {
                code = n.Code,
                name = n.Name,
                // Both fields are genuinely absent from a DAT-origin record (see NationRecord.Leader /
                // .HumanPlayer's own doc comments) — export null rather than touching the throwing
                // HumanPlayer accessor or fabricating a value.
                leader = n.Leader,
                humanPlayer = n.Source == SaveFileFormat.Dat ? (bool?)null : n.HumanPlayer,
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
                morale = a.Morale,
                coveredCell = a.CoveredCell,
                aboardFleet = a.IsAboardFleet,
                supplyTons = a.Supplies,
                supplyCapacityTons = a.SupplyCapacityTons,
                supplyPercent = a.SupplyPercent,
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
                    qualityName = UnitCatalog.QualityName(u.QualityCode),
                    isMercenary = u.IsMercenary,
                    mercenaryLabel = u.MercenaryLabel
                })
            }),
            fleets = fleets.Fleets.Select(f => new
            {
                index = f.Index,
                owner = new { code = f.OwnerCode, name = NationCatalog.Name(f.OwnerCode) },
                x = f.X,
                y = f.Y,
                shipCount = f.ShipCount,
                launched = f.IsLaunched,
                constructionCountdown = f.IsLaunched ? (int?)null : f.ConstructionCountdown,
                buildCityIndex = f.BuildCityIndex,
                buildCityName = f.BuildCityIndex is { } b && b < WorldPrefix.CityCount ? world.Cities[b].Name : null,
                conditionPercent = f.ConditionPercent,
                supplyTons = f.Supplies,
                supplyCapacityTons = f.SupplyCapacityTons,
                money = f.Money,
                carriedArmyIndex = f.CarriedArmyIndex,
                transportCapacityTroops = f.TransportCapacityTroops,
                quarterlyUpkeep = f.QuarterlyUpkeep
            }),
            mercenaryOffers = mercenaryOffersJson
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, options));

        static object NationRef(ushort code) => new { code, name = NationCatalog.Name(code) };
        static object? CityRef(WorldPrefix world, ushort index) =>
            index == SaveNationTable.NoCapitalSentinel ? null : new { index, name = world.Cities[index].Name };
    }
}
