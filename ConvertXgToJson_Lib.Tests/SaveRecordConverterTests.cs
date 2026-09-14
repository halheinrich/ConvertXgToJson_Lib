using System.Text.Json;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.Parsing;
using ConvertXgToJson_Lib.Tests.Helpers;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The <c>$type</c> contract of <c>SaveRecordConverter</c>
/// (halheinrich/backgammon#177, halheinrich/backgammon#178): one table maps
/// every <see cref="RecordType"/> member to the class that carries it, both
/// directions consult it, and so every member round-trips —
/// <see cref="UnknownRecord"/>'s two tags included — while the two copies
/// of the tag a document carries (<c>$type</c> and <c>entryType</c>)
/// cannot drift apart. Each pin runs on all three resolver paths
/// (<see cref="ResolverPaths"/>): the contract is the converter's, not the
/// metadata mechanism's. The goldens (<c>JsonContractTests</c>) remain the
/// byte gate and are untouched by this suite.
/// </summary>
public class SaveRecordConverterTests
{
    public static TheoryData<string> Paths => ResolverPaths.All;

    /// <summary>Every path crossed with the two tags <see cref="UnknownRecord"/>
    /// carries, the tag by name because the enum is internal.</summary>
    public static TheoryData<string, string> PathsByUnknownTag
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (string path in ResolverPaths.Names)
            {
                data.Add(path, nameof(RecordType.Comment));
                data.Add(path, nameof(RecordType.Missing));
            }
            return data;
        }
    }

    private static string Serialize(SaveRecord record, JsonSerializerOptions options) =>
        JsonSerializer.Serialize<SaveRecord>(record, options);

    private static SaveRecord Deserialize(string json, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<SaveRecord>(json, options)!;

    private static string DiscriminatorOf(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("$type").GetString()!;
    }

    // -----------------------------------------------------------------------
    //  The round-trip the issue reported lost
    // -----------------------------------------------------------------------

    /// <summary>
    /// An <see cref="UnknownRecord"/> writes the tag it was constructed with
    /// and reads back as an <see cref="UnknownRecord"/> carrying that same
    /// tag — the asymmetry halheinrich/backgammon#177 reported was a
    /// document that wrote <c>"Comment"</c> and could not be read.
    /// </summary>
    [Theory]
    [MemberData(nameof(PathsByUnknownTag))]
    public void UnknownRecord_RoundTrips_TaggedAsWritten(string path, string tagName)
    {
        var tag = Enum.Parse<RecordType>(tagName);
        var options = ResolverPaths.Named(path);
        string json = Serialize(new UnknownRecord(tag), options);

        DiscriminatorOf(json).Should().Be(tag.ToString());
        var read = Deserialize(json, options);
        read.Should().BeOfType<UnknownRecord>().Which.EntryType.Should().Be(tag);
        Serialize(read, options).Should().Be(json, "the read record re-emits the document it was read from");
    }

    /// <summary>
    /// Every <see cref="RecordType"/> member reads to a concrete record
    /// tagged with it and writes the member's name back — and, together,
    /// the members reach every concrete <see cref="SaveRecord"/> class the
    /// assembly declares, so a variant cannot exist outside the table.
    /// </summary>
    [Theory]
    [MemberData(nameof(Paths))]
    public void EveryRecordTypeMember_RoundTrips_AndTheMembersCoverEveryVariant(string path)
    {
        var options = ResolverPaths.Named(path);
        var reached = new HashSet<Type>();

        foreach (var tag in Enum.GetValues<RecordType>())
        {
            string name = tag.ToString();
            string json = $$"""{"$type":"{{name}}","entryType":"{{JsonNamingPolicy.CamelCase.ConvertName(name)}}"}""";

            var record = Deserialize(json, options);
            record.EntryType.Should().Be(tag, $"{name} must read back tagged as itself");
            record.GetType().IsAbstract.Should().BeFalse();
            DiscriminatorOf(Serialize(record, options)).Should().Be(name);
            reached.Add(record.GetType());
        }

        var declared = typeof(SaveRecord).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(SaveRecord).IsAssignableFrom(t));
        reached.Should().BeEquivalentTo(declared,
            "every concrete SaveRecord class must be the class of some RecordType member");
    }

    // -----------------------------------------------------------------------
    //  Read refusals
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Paths))]
    public void Read_RefusesADiscriminatorTheTableDoesNotName(string path)
    {
        var options = ResolverPaths.Named(path);

        foreach (string bogus in new[] { "Bogus", "cube", "2" })
        {
            var act = () => Deserialize($$"""{"$type":"{{bogus}}","entryType":"cube"}""", options);
            act.Should().Throw<JsonException>($"'{bogus}' names no RecordType member");
        }
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public void Read_RefusesAMissingDiscriminator(string path)
    {
        var act = () => Deserialize("""{"entryType":"cube"}""", ResolverPaths.Named(path));
        act.Should().Throw<JsonException>();
    }

    /// <summary>
    /// The two copies of the tag must agree — including when the second is
    /// absent and the class's default tag stands in for it: a bare
    /// <c>"Comment"</c> would otherwise read back as a Missing record.
    /// </summary>
    [Theory]
    [MemberData(nameof(Paths))]
    public void Read_RefusesADiscriminatorThatDisagreesWithEntryType(string path)
    {
        var options = ResolverPaths.Named(path);

        var mismatched = () => Deserialize("""{"$type":"Cube","entryType":"move"}""", options);
        mismatched.Should().Throw<JsonException>();

        var defaulted = () => Deserialize("""{"$type":"Comment"}""", options);
        defaulted.Should().Throw<JsonException>(
            "UnknownRecord's default tag is Missing, which is not what the discriminator says");
    }

    // -----------------------------------------------------------------------
    //  Write refusals
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Paths))]
    public void Write_RefusesARecordWhoseClassIsNotItsTags(string path)
    {
        var act = () => Serialize(new CubeRecord { EntryType = RecordType.Move }, ResolverPaths.Named(path));
        act.Should().Throw<JsonException>();
    }

    /// <summary>
    /// The parser tags an unrecognised record with whatever code it read,
    /// and the byte admits codes the enum does not name. Such a record has
    /// no discriminator and is refused at write — before the old shape's
    /// silent <c>"42"</c> that no reader could take back.
    /// </summary>
    [Theory]
    [MemberData(nameof(Paths))]
    public void Write_RefusesAnUnnamedTag(string path)
    {
        var act = () => Serialize(new UnknownRecord((RecordType)42), ResolverPaths.Named(path));
        act.Should().Throw<JsonException>();
    }
}
