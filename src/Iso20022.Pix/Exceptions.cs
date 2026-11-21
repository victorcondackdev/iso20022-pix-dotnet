using System.Text;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix;

/// <summary>
/// Base class of every exception the library throws for invalid input:
/// malformed messages, amounts and identifiers, documents that fail
/// validation and builder requests that cannot be honoured.
/// </summary>
/// <remarks>
/// Messages are identical to the error strings of the Go implementation,
/// except for <see cref="MessageParseErrorKind.Malformed"/>, whose detail comes
/// from <see cref="System.Xml.XmlReader"/>.
/// </remarks>
public abstract class Iso20022Exception : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this one, if any.</param>
    protected Iso20022Exception(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Why a payload could not be parsed or detected.</summary>
public enum MessageParseErrorKind
{
    /// <summary>The XML is not well-formed, or uses an encoding other than UTF-8.</summary>
    Malformed,

    /// <summary>No ISO 20022 <c>Document</c> element was found.</summary>
    NoDocument,

    /// <summary>The <c>Document</c> namespace is not an ISO 20022 message namespace.</summary>
    UnknownNamespace,

    /// <summary>The <c>Document</c> is a different message than the one requested.</summary>
    UnexpectedMessage,

    /// <summary>The payload contains a DOCTYPE (DTD) declaration, which is refused.</summary>
    DirectiveForbidden,

    /// <summary>The payload contains more than one <c>Document</c> element.</summary>
    MultipleDocuments,

    /// <summary>The payload contains more than one <c>AppHdr</c> element.</summary>
    MultipleHeaders,

    /// <summary>An amount element is not a valid ISO 20022 BRL amount.</summary>
    InvalidAmount,

    /// <summary>A boolean element is not a valid boolean.</summary>
    InvalidBoolean,
}

/// <summary>A payload could not be parsed, or its message type could not be detected.</summary>
public sealed class MessageParseException : Iso20022Exception
{
    internal MessageParseException(MessageParseErrorKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    /// <summary>The class of the error.</summary>
    public MessageParseErrorKind Kind { get; }

    internal static MessageParseException NoDocument() =>
        new(MessageParseErrorKind.NoDocument, "iso20022: no ISO 20022 Document element found");

    internal static MessageParseException UnknownNamespace(string? ns) =>
        new(MessageParseErrorKind.UnknownNamespace, ns is null
            ? "iso20022: Document namespace is not an ISO 20022 message namespace"
            : "iso20022: Document namespace is not an ISO 20022 message namespace: " + GoFormat.Quote(ns));

    internal static MessageParseException Unexpected(string got, string want) =>
        new(MessageParseErrorKind.UnexpectedMessage, "iso20022: unexpected message type: got " + got + ", want " + want);

    internal static MessageParseException DirectiveForbidden(Exception? inner) =>
        new(MessageParseErrorKind.DirectiveForbidden, "iso20022: XML directives (DOCTYPE, ENTITY) are not allowed", inner);

    internal static MessageParseException MultipleDocuments() =>
        new(MessageParseErrorKind.MultipleDocuments, "iso20022: more than one Document element");

    internal static MessageParseException MultipleHeaders() =>
        new(MessageParseErrorKind.MultipleHeaders, "iso20022: more than one AppHdr");

    internal static MessageParseException Malformed(string detail, Exception? inner = null) =>
        new(MessageParseErrorKind.Malformed, "iso20022: malformed XML: " + detail, inner);
}

/// <summary>An amount is not a valid ISO 20022 BRL amount, or cannot be serialized.</summary>
public sealed class InvalidAmountException : Iso20022Exception
{
    internal InvalidAmountException(string detail)
        : base("money: invalid amount: " + detail)
    {
    }
}

/// <summary>The kind of identifier an <see cref="InvalidIdentifierException"/> refers to.</summary>
public enum IdentifierKind
{
    /// <summary>A Pix EndToEndId.</summary>
    EndToEndId,

    /// <summary>A Pix return identification (RtrId).</summary>
    ReturnId,

    /// <summary>A message identification (MsgId, Max35Text).</summary>
    MsgId,

    /// <summary>A Pix key (chave Pix).</summary>
    PixKey,
}

/// <summary>An identifier is malformed, or one cannot be generated from the given inputs.</summary>
public sealed class InvalidIdentifierException : Iso20022Exception
{
    internal InvalidIdentifierException(IdentifierKind kind, string detail)
        : base(Prefix(kind) + ": " + detail)
    {
        Kind = kind;
        Detail = detail;
    }

    /// <summary>The kind of identifier.</summary>
    public IdentifierKind Kind { get; }

    /// <summary>The reason, without the <c>ids: invalid ...:</c> prefix.</summary>
    public string Detail { get; }

    internal static string Prefix(IdentifierKind kind) => kind switch
    {
        IdentifierKind.EndToEndId => "ids: invalid EndToEndId",
        IdentifierKind.ReturnId => "ids: invalid RtrId",
        IdentifierKind.MsgId => "ids: invalid MsgId",
        _ => "ids: invalid Pix key",
    };
}

/// <summary>A document failed validation; every violation is listed.</summary>
public sealed class MessageValidationException : Iso20022Exception
{
    internal MessageValidationException(IReadOnlyList<Violation> violations, object document)
        : base(Format(violations))
    {
        Violations = violations;
        Document = document;
    }

    /// <summary>Every violation found, in document order.</summary>
    public IReadOnlyList<Violation> Violations { get; }

    /// <summary>The document that was built, so that callers can inspect what was produced.</summary>
    public object Document { get; }

    /// <summary>
    /// Formats violations as the Go implementation does: <c>no violations</c>,
    /// a single <c>path: message</c>, or <c>N violations:</c> followed by one
    /// tab-indented line per violation.
    /// </summary>
    /// <param name="violations">The violations.</param>
    /// <returns>The formatted text.</returns>
    public static string Format(IReadOnlyList<Violation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);
        switch (violations.Count)
        {
            case 0:
                return "no violations";
            case 1:
                return violations[0].ToString();
        }

        var b = new StringBuilder();
        b.Append(GoFormat.Int(violations.Count)).Append(" violations:");
        foreach (Violation v in violations)
        {
            b.Append("\n\t").Append(v.ToString());
        }

        return b.ToString();
    }
}

/// <summary>Why a builder refused a request.</summary>
public enum MessageBuildErrorKind
{
    /// <summary>There is nothing to build from: no transfers, or an original without transactions.</summary>
    NoTransactions,

    /// <summary>A rejection was requested without a reason code.</summary>
    MissingReason,

    /// <summary>The reason code is not in the bundled table and unknown codes were not allowed.</summary>
    UnknownReason,

    /// <summary>The original transaction to return was not found.</summary>
    TransactionNotFound,

    /// <summary>An amount is zero or negative where a positive amount is required.</summary>
    InvalidAmount,

    /// <summary>The return would exceed the amount available for return.</summary>
    AmountExceeded,
}

/// <summary>A builder (<see cref="Pacs008Builder"/>, <see cref="Pacs002"/>, <see cref="Pacs004"/>) refused a request.</summary>
public sealed class MessageBuildException : Iso20022Exception
{
    internal MessageBuildException(MessageBuildErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    /// <summary>The class of the error.</summary>
    public MessageBuildErrorKind Kind { get; }
}
