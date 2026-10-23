using System.Buffers.Binary;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T184 Done-when 1–3 and 5 for the canonical from-scratch encoding: the expected offsets are written out
/// here as literals from the SAV atlas ledger (research <c>f83a886</c>), never taken from the codec.
/// </summary>
public sealed class OriginalSaveCodecTests
{
    // Literal framing for the fixture: 2 armies, 2 fleets, 3 news lines.
    private const int Cities = 89600, ArmyCount = 100956, Army0 = 100958, FleetCount = Army0 + 2 * 656, Fleet0 = FleetCount + 2;
    private const int Nation0 = Fleet0 + 2 * 26, Merc0 = Nation0 + 16 * 1172, NewsIndex = Merc0 + 600, Trailer = NewsIndex + 2 + 3 * 61;

    private static short I16(byte[] b, int o) => BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(o, 2));

    [Fact]
    public void The_encoding_has_the_exact_length_and_every_field_at_its_atlas_offset()
    {
        var d = OriginalSaveDocumentFixture.Build();
        var b = OriginalSaveCodec.Encode(d);

        Assert.Equal(Trailer + 55, b.Length);
        Assert.Equal(20 + 5 % 16, I16(b, 2 * (OriginalSaveDocumentFixture.CityTile(5).X * 140 + OriginalSaveDocumentFixture.CityTile(5).Y)));
        // City 5: name "C328" then zeros to 14 bytes, x, y, owner, allegiance ... tribute.
        Assert.Equal("C328"u8.ToArray().Concat(new byte[10]), b[(Cities + 5 * 34)..(Cities + 5 * 34 + 14)]);
        Assert.Equal(new short[] { 5, 10, 5, 5, 70, 100, 50, 30, 60, 5 },
            Enumerable.Range(0, 10).Select(k => I16(b, Cities + 5 * 34 + 14 + 2 * k)));
        Assert.Equal(2, I16(b, ArmyCount));
        // Army 0: header, two packed slots, then 18 zero slots.
        Assert.Equal(new short[] { 5, 100, 0, 4, 0, 50, 300, 70 }, Enumerable.Range(0, 8).Select(k => I16(b, Army0 + 2 * k)));
        Assert.Equal(new short[] { 2, 0, 3000, 6 }, Enumerable.Range(0, 4).Select(k => I16(b, Army0 + 16 + 32 + 2 * k)));
        Assert.Equal("Velites"u8.ToArray().Concat(new byte[17]), b[(Army0 + 16 + 32 + 8)..(Army0 + 16 + 64)]);
        Assert.All(b[(Army0 + 16 + 64)..(Army0 + 656)], x => Assert.Equal(0, x));
        Assert.Equal(2, I16(b, FleetCount));
        Assert.Equal(new short[] { 7, 120, -1, 0, 0, -1, 9, 40, 100, 20, 90, 1, 2 },
            Enumerable.Range(0, 13).Select(k => I16(b, Fleet0 + 2 * k)));

        var n3 = Nation0 + 3 * 1172;
        Assert.Equal("Ptolemaic"u8.ToArray().Concat(new byte[2]), b[n3..(n3 + 11)]);
        Assert.Equal("Leader of 3"u8.ToArray().Concat(new byte[16]), b[(n3 + 0x0B)..(n3 + 0x26)]);
        Assert.Equal(0, I16(b, n3 + 0x26 + 2 * 4));
        Assert.Equal(3, I16(b, Nation0 + 1172 + 0x26));
        Assert.Equal(0x1234 + 3, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(n3 + 0x46, 2)));
        var listLength = d.Nations[3].CityList.Count;
        Assert.Equal(d.Nations[3].CityList, Enumerable.Range(0, listLength).Select(e => (int)I16(b, n3 + 0x48 + 2 * e)));
        Assert.All(Enumerable.Range(listLength, 334 - listLength), e => Assert.Equal(-1, I16(b, n3 + 0x48 + 2 * e)));
        Assert.Equal(new short[] { 1, 2, 800, (short)d.Nations[3].CityList[0] }, Enumerable.Range(0, 4).Select(k => I16(b, n3 + 0x2E4 + 2 * k)));
        Assert.Equal(0x0000FF00u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(n3 + 0x428, 4)));
        Assert.Equal(50_003, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(n3 + 0x430, 4)));
        Assert.Equal(9_003, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(n3 + 0x438, 4)));
        Assert.Equal(new short[] { 450, 50, (short)d.Nations[3].Capital, (short)listLength, (short)listLength, 20, 1_000, -1 },
            Enumerable.Range(0, 8).Select(k => I16(b, n3 + 0x440 + 2 * k)));
        Assert.All(b[(n3 + 0x450)..(n3 + 0x46B)], x => Assert.Equal(0, x));
        Assert.Equal(new byte[] { 1, 0, 0 }, b[(n3 + 0x46B)..(n3 + 0x46E)]);
        Assert.Equal(new short[] { 10, 20, 300, 200, 30, 40, 250, 180, 50, 60, 220, 160, 3, 4, 0, 10, 5 },
            Enumerable.Range(0, 17).Select(k => I16(b, n3 + 0x46E + 2 * k)));
        Assert.Equal(0, b[n3 + 0x490]);
        Assert.Equal(1, b[Nation0 + 0x490]);
        Assert.Equal(new short[] { 0, 0, 0, 0, -1, 0 }, Enumerable.Range(0, 6).Select(k => I16(b, Merc0 + 12 * 49 + 2 * k)));
        Assert.Equal(2, I16(b, NewsIndex));
        Assert.Equal("A fleet is damaged in a storm"u8.ToArray(), b[(NewsIndex + 2 + 61)..(NewsIndex + 2 + 61 + 29)]);
        Assert.All(b[(NewsIndex + 2 + 61 + 29)..(NewsIndex + 2 + 122)], x => Assert.Equal(0, x));
        Assert.Equal(new short[] { -1, 0, 0, 0, 3, 270, 1, 10, 20, 600, 800 }, Enumerable.Range(0, 11).Select(k => I16(b, Trailer + 32 + 2 * k)));
        Assert.Equal(0, b[^1]);
    }

    [Fact]
    public void The_production_readers_accept_the_encoding_and_decoding_returns_the_same_bytes()
    {
        var b = OriginalSaveCodec.Encode(OriginalSaveDocumentFixture.Build());
        var layout = OriginalSaveLayout.Validate(b);
        Assert.Equal(2, layout.ArmyCount);
        Assert.Equal(2, layout.FleetCount);
        Assert.Equal(2, layout.NewsIndex);
        Assert.Equal(b, OriginalSaveCodec.Encode(OriginalSaveCodec.Decode(b)));
        Assert.Equal(OriginalSaveCodec.Encode(OriginalSaveDocumentFixture.Build()), b);
    }

    [Fact]
    public void Changing_one_field_changes_only_its_own_bytes()
    {
        var before = OriginalSaveCodec.Encode(OriginalSaveDocumentFixture.Build());
        var after = OriginalSaveCodec.Encode(OriginalSaveDocumentFixture.Build().WithNation(3, n => n with { Tax = 21 }));
        var changed = Enumerable.Range(0, before.Length).Where(i => before[i] != after[i]).ToArray();
        Assert.Equal(new[] { Nation0 + 3 * 1172 + 0x44A }, changed);
    }

    [Fact]
    public void A_carried_inert_block_is_written_and_null_writes_zeros()
    {
        var inert = new OriginalInertBytes(Enumerable.Range(1, 27).Select(i => (byte)i).ToArray(), 0xAB, [0xC1, 0xC2, 0xC3]);
        var b = OriginalSaveCodec.Encode(OriginalSaveDocumentFixture.Build().WithNation(2, n => n with { Inert = inert }));
        var n2 = Nation0 + 2 * 1172;
        Assert.Equal(Enumerable.Range(1, 27).Select(i => (byte)i), b[(n2 + 0x450)..(n2 + 0x46B)]);
        Assert.Equal(0xAB, b[n2 + 0x46D]);
        Assert.Equal(new byte[] { 0xC1, 0xC2, 0xC3 }, b[(n2 + 0x491)..(n2 + 0x494)]);
    }

    public static IEnumerable<object[]> Unrepresentable()
    {
        var d = OriginalSaveDocumentFixture.Build();
        yield return ["city[0].x", d.WithCity(0, c => c with { X = 32768 })];
        yield return ["city[0].tribute", d.WithCity(0, c => c with { Tribute = -32769 })];
        yield return ["city[0].name", d.WithCity(0, c => c with { Name = "ABCDEFGHIJKLMN" })];
        yield return ["city[0].name", d.WithCity(0, c => c with { Name = "" })];
        yield return ["city[0].name", d.WithCity(0, c => c with { Name = "Ro\0me" })];
        yield return ["city[0].name", d.WithCity(0, c => c with { Name = "Ancôna" })];
        yield return ["army[0].unit[0].name", d.WithArmy(0, a => a with { Units = [a.Units[0] with { Name = new string('x', 24) }] })];
        yield return ["army[0].unit[0].troops", d.WithArmy(0, a => a with { Units = [a.Units[0] with { Troops = 0 }] })];
        yield return ["army[0].units", d.WithArmy(0, a => a with { Units = Enumerable.Repeat(a.Units[0], 21).ToArray() })];
        yield return ["fleet[1].moves", d.WithFleet(1, f => f with { Moves = 40000 })];
        yield return ["nation[0].leader", d.WithNation(0, n => n with { Leader = new string('L', 27) })];
        yield return ["nation[0].neighbours", d.WithNation(0, n => n with { Neighbours = 65536 })];
        yield return ["nation[0].controller", d.WithNation(0, n => n with { Controller = 2 })];
        yield return ["army[0].unit[0].name", d.WithArmy(0, a => a with { Units = [a.Units[0] with { Name = "" }] })];
        yield return ["nation[0].relations", d.WithNation(0, n => n with { Relations = n.Relations.Take(15).ToArray() })];
        yield return ["nation[0].cityList[0]", d.WithNation(0, n => n with { CityList = [334] })];
        yield return ["nation[0].recruits", d.WithNation(0, n => n with { Recruits = n.Recruits.Take(39).ToArray() })];
        yield return ["news", d with { News = Enumerable.Repeat("x", 41).ToArray() }];
        yield return ["news[0]", d with { News = [new string('n', 61)] }];
        yield return ["trailer.yearBc", d with { Trailer = d.Trailer with { YearBc = 70000 } }];
        yield return ["map", d with { Map = d.Map.Take(100).ToArray() }];
    }

    [Theory]
    [MemberData(nameof(Unrepresentable))]
    public void An_unrepresentable_value_is_refused_naming_its_field(string field, OriginalSaveDocument document)
    {
        var ex = Assert.Throws<OriginalSaveEncodingException>(() => OriginalSaveCodec.Encode(document));
        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void The_boundary_values_themselves_are_accepted()
    {
        var d = OriginalSaveDocumentFixture.Build()
            .WithCity(0, c => c with { Name = "ABCDEFGHIJKLM", Tribute = short.MinValue, Supplies = short.MaxValue })
            .WithArmy(0, a => a with { Units = [a.Units[0] with { Name = new string('x', 23), Troops = short.MaxValue }] })
            .WithNation(0, n => n with { Leader = new string('L', 26), Neighbours = 65535, Controller = 1 })
            with { News = Enumerable.Repeat(new string('n', 60), 40).ToArray() };
        var b = OriginalSaveCodec.Encode(d);
        Assert.Equal(39, OriginalSaveLayout.Validate(b).NewsIndex);
    }

    [Fact]
    public void An_empty_news_log_writes_index_minus_one()
    {
        var b = OriginalSaveCodec.Encode(OriginalSaveDocumentFixture.Build() with { News = [] });
        Assert.Equal(-1, OriginalSaveLayout.Validate(b).NewsIndex);
    }
}
