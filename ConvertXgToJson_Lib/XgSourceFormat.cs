namespace ConvertXgToJson_Lib;

/// <summary>
/// How a source's bytes are decoded into an <see cref="Models.XgFile"/> by
/// <see cref="XgSourceReader"/>: the caller states the form it holds — every
/// entry point requires the argument and none supplies a default, and the
/// zero value, <see cref="Unspecified"/>, is refused (Hal's rulings of
/// 2026-10-11) — and the read decodes that form alone, inferring nothing
/// from a path or a name. Refusing <see cref="Unspecified"/> refuses a format
/// nobody stated; it cannot show that a stated format fits the bytes.
/// </summary>
public enum XgSourceFormat
{
    /// <summary>
    /// No format stated: the value an unset <see cref="XgSourceFormat"/> holds
    /// (<c>default</c>). Every <see cref="XgSourceReader"/> entry point refuses
    /// it, as a usage error, before anything is acquired — as it refuses a
    /// value that is no member at all. Zero is reserved for it, as
    /// <see cref="XgUnrepresentableValueReason.Unspecified"/> is.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// XG's own binary file, <c>.xg</c> or <c>.xgp</c> — told apart by its
    /// content, as <see cref="XgFileReader.ReadStream"/> reads it.
    /// </summary>
    Xg = 1,

    /// <summary>
    /// This library's JSON document, as <see cref="XgFileReader.ToJson"/> and
    /// <see cref="XgFileReader.WriteJsonAsync"/> write it and
    /// <see cref="XgFileReader.ReadJson"/> reads it.
    /// </summary>
    Json = 2,
}
