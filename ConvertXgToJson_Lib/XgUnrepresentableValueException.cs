using System.Diagnostics;
using System.Text;

namespace ConvertXgToJson_Lib;

/// <summary>
/// The exception thrown when a value cannot be written because the XG
/// format cannot represent it: the value is valid in memory and in JSON,
/// and only the binary format lacks a faithful encoding for it.
/// </summary>
/// <remarks>
/// <para>
/// This reports a limit of the format, not a fault in the caller's
/// argument or in the object's state: the in-memory model and the JSON
/// document accept the value, so the constraint is checked only where the
/// format is written. It is the read side's mirror — malformed input
/// there is an <see cref="InvalidDataException"/>.
/// </para>
/// <para>
/// <see cref="Reason"/> names the constraint; when the value is a comment,
/// <see cref="CommentIndex"/> names it; when the failure is one character
/// the format's encoding cannot represent, <see cref="Character"/> is that
/// character. The message is composed from these for a reader — a
/// consumer reads the properties, never the message.
/// </para>
/// </remarks>
public sealed class XgUnrepresentableValueException : Exception
{
    /// <summary>Initializes a new instance with a system-supplied message.</summary>
    public XgUnrepresentableValueException()
    {
    }

    /// <summary>Initializes a new instance with the specified message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public XgUnrepresentableValueException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified message and the
    /// exception that caused this one.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of this one, or null.</param>
    public XgUnrepresentableValueException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    internal XgUnrepresentableValueException(
        string message, XgUnrepresentableValueReason reason, int? commentIndex, Rune? character,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Debug.Assert(reason != XgUnrepresentableValueReason.Unspecified, "The library always names the reason.");
        Debug.Assert(character.HasValue == (reason == XgUnrepresentableValueReason.UnencodableCharacter),
            "Character is set exactly when the reason is an unencodable character.");
        Reason = reason;
        CommentIndex = commentIndex;
        Character = character;
    }

    /// <summary>
    /// The constraint the value breaks; <see cref="XgUnrepresentableValueReason.Unspecified"/>
    /// only for an instance created through a standard constructor.
    /// </summary>
    public XgUnrepresentableValueReason Reason { get; }

    /// <summary>
    /// The index, in the comment table of the file being written, of the
    /// comment the format cannot carry; null when the value is not a
    /// comment.
    /// </summary>
    public int? CommentIndex { get; }

    /// <summary>
    /// The character the format's encoding cannot represent — set exactly
    /// when <see cref="Reason"/> is
    /// <see cref="XgUnrepresentableValueReason.UnencodableCharacter"/>, and
    /// null otherwise.
    /// </summary>
    public Rune? Character { get; }
}
