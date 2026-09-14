using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ConvertXgToJson_Lib.Json;

namespace ConvertXgToJson_Lib.Tests.Helpers;

/// <summary>
/// The three metadata mechanisms over one options configuration — the
/// frame of the halheinrich/backgammon#129 leg-2 gate. Every path is built
/// by copying <see cref="XgJsonOptions.Default"/> and swapping only its
/// resolver, so no suite restates the document's policy — indentation,
/// camelCase naming, the null-ignore condition and the
/// halheinrich/backgammon#164 enum converters stay single-sourced in
/// <see cref="XgJsonOptions"/>. What varies between the paths is exactly one
/// thing: where the <see cref="JsonTypeInfo"/> comes from. Shared by
/// <c>XgJsonContextTests</c> (the mechanism gate) and
/// <c>SaveRecordConverterTests</c> (the discriminator contract, which must
/// hold on every path alike).
/// </summary>
internal static class ResolverPaths
{
    /// <summary>The pre-change mechanism: runtime reflection.</summary>
    public static readonly JsonSerializerOptions ReflectionOptions =
        new(XgJsonOptions.Default) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    /// <summary>What this library ships: this repo's context chained ahead of
    /// BgDataTypes_Lib's, the arc's composition pattern.</summary>
    public static readonly JsonSerializerOptions ChainedOptions = XgJsonOptions.Default;

    /// <summary>
    /// This repo's context alone, unchained — the pin that the
    /// <c>XgFile</c> closure is self-sufficient here and does not silently
    /// lean on the link below it. (The chain is still load-bearing for
    /// <see cref="XgJsonOptions"/> as a whole: the four BgDataTypes_Lib wire
    /// enums resolve one link down, which is what
    /// <c>EnumTokenStrictnessTests</c> exercises.)
    /// </summary>
    public static readonly JsonSerializerOptions ContextOnlyOptions =
        new(XgJsonOptions.Default) { TypeInfoResolver = XgJsonContext.Default };

    /// <summary>Every path by name — the names <see cref="Named"/> resolves.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [nameof(ReflectionOptions), nameof(ChainedOptions), nameof(ContextOnlyOptions)];

    /// <summary>Every path by name, for a theory that must hold on each.</summary>
    public static TheoryData<string> All
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string name in Names)
                data.Add(name);
            return data;
        }
    }

    /// <summary>The path a theory row names.</summary>
    public static JsonSerializerOptions Named(string path) => path switch
    {
        nameof(ReflectionOptions) => ReflectionOptions,
        nameof(ChainedOptions) => ChainedOptions,
        nameof(ContextOnlyOptions) => ContextOnlyOptions,
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, "Not a resolver path."),
    };
}
