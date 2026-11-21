using Iso20022.Pix.Internal;

namespace Iso20022.Pix;

/// <summary>Message identifiers without variant and version, as returned by <see cref="MessageType.Id"/>.</summary>
public static class MessageIds
{
    /// <summary>The common prefix of ISO 20022 XML namespaces.</summary>
    public const string NamespacePrefix = XmlInput.NamespacePrefix;

    /// <summary>FIToFICustomerCreditTransfer.</summary>
    public const string Pacs008 = "pacs.008";

    /// <summary>FIToFIPaymentStatusReport.</summary>
    public const string Pacs002 = "pacs.002";

    /// <summary>PaymentReturn.</summary>
    public const string Pacs004 = "pacs.004";

    /// <summary>BankToCustomerDebitCreditNotification.</summary>
    public const string Camt054 = "camt.054";

    /// <summary>BusinessApplicationHeader.</summary>
    public const string Head001 = "head.001";
}

/// <summary>
/// Identifies an ISO 20022 message definition, such as <c>pacs.008.001.08</c>:
/// business area <c>pacs</c>, message functionality <c>008</c>, variant
/// <c>001</c> and version <c>08</c>.
/// </summary>
/// <param name="BusinessArea">The business area, e.g. <c>pacs</c>.</param>
/// <param name="Functionality">The message functionality, e.g. <c>008</c>.</param>
/// <param name="Variant">The variant, e.g. <c>001</c>.</param>
/// <param name="Version">The version, e.g. <c>08</c>.</param>
public readonly record struct MessageType(string BusinessArea, string Functionality, string Variant, string Version)
{
    /// <summary>The identifier without variant and version, e.g. <c>pacs.008</c>, for comparison with <see cref="MessageIds"/>.</summary>
    public string Id => string.IsNullOrEmpty(BusinessArea) ? string.Empty : BusinessArea + "." + Functionality;

    /// <summary>The XML namespace of the message.</summary>
    public string Namespace => MessageIds.NamespacePrefix + ToString();

    /// <summary>The ISO 20022 message name, e.g. <c>FIToFICustomerCreditTransfer</c>, or an empty string for messages this library does not model.</summary>
    public string Name => Id switch
    {
        MessageIds.Pacs008 => "FIToFICustomerCreditTransfer",
        MessageIds.Pacs002 => "FIToFIPaymentStatusReport",
        MessageIds.Pacs004 => "PaymentReturn",
        MessageIds.Camt054 => "BankToCustomerDebitCreditNotification",
        MessageIds.Head001 => "BusinessApplicationHeader",
        _ => string.Empty,
    };

    /// <summary>Whether this library parses the message.</summary>
    public bool IsSupported => Id is MessageIds.Pacs008 or MessageIds.Pacs002 or MessageIds.Pacs004 or MessageIds.Camt054;

    /// <summary>Parses <c>pacs.008.001.08</c> or the namespace <c>urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08</c>.</summary>
    /// <param name="s">The identifier or namespace.</param>
    /// <returns>The message type.</returns>
    /// <exception cref="MessageParseException">The text is not a message identifier (<see cref="MessageParseErrorKind.UnknownNamespace"/>).</exception>
    public static MessageType Parse(string s) =>
        XmlInput.TryParseMessageId(s, out MessageType id) ? id : throw MessageParseException.UnknownNamespace(null);

    /// <summary>Parses a message identifier without throwing.</summary>
    /// <param name="s">The identifier or namespace.</param>
    /// <param name="type">The message type, when valid.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool TryParse(string? s, out MessageType type) => XmlInput.TryParseMessageId(s, out type);

    /// <summary>Returns the full identifier, e.g. <c>pacs.008.001.08</c>, or an empty string for the default value.</summary>
    /// <returns>The identifier.</returns>
    public override string ToString() =>
        string.IsNullOrEmpty(BusinessArea) ? string.Empty : BusinessArea + "." + Functionality + "." + Variant + "." + Version;
}
