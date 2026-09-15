namespace ConvertXgToJson_Lib;

/// <summary>
/// Why the XG format cannot represent a value — the constraint an
/// <see cref="XgUnrepresentableValueException"/> reports, typed so a
/// consumer never reads the message to tell one from another.
/// </summary>
public enum XgUnrepresentableValueReason
{
    /// <summary>
    /// No reason was given: the exception was created through one of its
    /// standard constructors, which carry no data.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The value holds a character the format's encoding cannot encode;
    /// <see cref="XgUnrepresentableValueException.Character"/> is that
    /// character.
    /// </summary>
    UnencodableCharacter = 1,

    /// <summary>
    /// The value holds an unpaired surrogate — a lone UTF-16 code unit,
    /// which is not a character, so no encoding can carry it.
    /// </summary>
    UnpairedSurrogate = 2,

    /// <summary>
    /// The value holds the byte pair 0x01 0x02 the comment table reserves
    /// as its escape for an embedded CRLF, so it would read back as a CRLF.
    /// </summary>
    ReservedCrlfEscape = 3,
}
