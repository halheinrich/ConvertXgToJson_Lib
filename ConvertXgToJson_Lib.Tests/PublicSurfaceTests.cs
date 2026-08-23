using ConvertXgToJson_Lib.Models;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// Pins the encapsulation decision of halheinrich/backgammon#131: the XG
/// record model is internal, <see cref="XgFile"/> is the only public type
/// of its namespace and exposes no structure, and <see cref="XgFileBuilder"/>
/// is the one public way to synthesize one. A record type drifting back to
/// <c>public</c> — the widest interface in the tree, which no production
/// consumer ever used — fails here, not in a consumer-graph audit.
/// </summary>
public class PublicSurfaceTests
{
    private static readonly System.Reflection.Assembly Library = typeof(XgFile).Assembly;

    [Fact]
    public void Models_XgFileIsTheOnlyPublicType()
    {
        var publicModelTypes = Library.GetExportedTypes()
            .Where(t => t.Namespace == typeof(XgFile).Namespace)
            .Select(t => t.Name);

        publicModelTypes.Should().Equal([nameof(XgFile)],
            "the XG record model is internal by design; consumers hold XgFile as an opaque handle");
    }

    [Fact]
    public void XgFile_ExposesNoPublicMembers()
    {
        var publicMembers = typeof(XgFile)
            .GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(m => m.Name);

        publicMembers.Should().BeEmpty(
            "XgFile is opaque: no public constructor, no public property — synthesis goes through XgFileBuilder");
    }

    [Fact]
    public void OpeningBook_LookupSurfaceIsInternal()
    {
        Library.GetExportedTypes().Select(t => t.Name)
            .Should().NotContain([nameof(OpeningBookEntry), nameof(OpeningBookKey)],
                "book lookup keys on the internal record position convention; the public intent is " +
                "XgIteratorOptions.OpeningBook enrichment");

        typeof(OpeningBook).GetMethod(nameof(OpeningBook.TryGetEntry), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Should().BeNull();
        typeof(OpeningBook).GetMethod(nameof(OpeningBook.GetEntries), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void NoPublicMember_ReferencesAnInternalType()
    {
        // The compiler already rejects a public signature over an internal
        // type; this guards the reflection-visible remainder (public
        // properties on public types) so the cascade cannot re-open by
        // accident through a new public type.
        var leaks = Library.GetExportedTypes()
            .SelectMany(t => t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static))
            .Where(p => p.PropertyType.Assembly == Library && !p.PropertyType.IsVisible)
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");

        leaks.Should().BeEmpty();
    }
}
