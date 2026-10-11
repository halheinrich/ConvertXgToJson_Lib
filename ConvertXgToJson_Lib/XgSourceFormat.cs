namespace ConvertXgToJson_Lib;

/// <summary>
/// How a source's bytes are decoded into an <see cref="Models.XgFile"/> by
/// <see cref="XgSourceReader"/>: the caller states the form it holds — every
/// entry point requires the argument and none supplies a default (Hal's
/// ruling of 2026-10-11) — and the read decodes that form alone, inferring
/// nothing from a path or a name.
/// </summary>
public enum XgSourceFormat
{
    /// <summary>
    /// XG's own binary file, <c>.xg</c> or <c>.xgp</c> — told apart by its
    /// content, as <see cref="XgFileReader.ReadStream"/> reads it.
    /// </summary>
    Xg = 0,

    /// <summary>
    /// This library's JSON document, as <see cref="XgFileReader.ToJson"/> and
    /// <see cref="XgFileReader.WriteJsonAsync"/> write it and
    /// <see cref="XgFileReader.ReadJson"/> reads it.
    /// </summary>
    Json = 1,
}
