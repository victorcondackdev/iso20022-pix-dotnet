using System.Text;
using System.Xml;

namespace Iso20022.Pix.Internal;

/// <summary>
/// Hand-written XML reading shared by the message models. The decoding rules
/// follow Go's <c>encoding/xml</c> unmarshalling, so that both implementations
/// read the same values from the same bytes:
/// <list type="bullet">
/// <item>child elements are matched by local name in any namespace; unknown elements are skipped;</item>
/// <item>the text of a simple element is its direct character data, white space included; nested elements are skipped;</item>
/// <item>a repeated non-list element overwrites strings and merges into components;</item>
/// <item>a repeated list element appends.</item>
/// </list>
/// </summary>
internal static class XmlInput
{
    /// <summary>The ISO 20022 namespace prefix.</summary>
    public const string NamespacePrefix = "urn:iso:std:iso:20022:tech:xsd:";

    /// <summary>
    /// Reader settings: DTDs are prohibited and no external resource is ever
    /// resolved, which rules out XXE and entity-expansion attacks.
    /// </summary>
    public static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = false,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CheckCharacters = true,
        CloseInput = false,
        ConformanceLevel = ConformanceLevel.Document,
    };

    public static XmlReader Create(Stream stream) => XmlReader.Create(stream, Settings());

    public static XmlReader Create(string xml) => XmlReader.Create(new StringReader(xml), Settings());

    /// <summary>Translates a reader failure into the library's exception.</summary>
    /// <param name="e">The reader failure.</param>
    /// <param name="sawElement">Whether an element was read before the failure.</param>
    public static MessageParseException Translate(XmlException e, bool sawElement)
    {
        // XmlReader reports a prohibited DOCTYPE with a dedicated message that
        // names the DTD; the Go implementation rejects all directives.
        if (e.Message.Contains("DTD is prohibited", StringComparison.Ordinal)
            || e.Message.Contains("DtdIsProhibited", StringComparison.Ordinal))
        {
            return MessageParseException.DirectiveForbidden(e);
        }

        // An input without any element (empty, or only white space and
        // comments) has no Document, as in Go; XmlReader calls it a missing root.
        if (!sawElement && (e.Message.Contains("Root element is missing", StringComparison.Ordinal)
            || e.Message.Contains("MissingRoot", StringComparison.Ordinal)))
        {
            return MessageParseException.NoDocument();
        }

        return MessageParseException.Malformed(e.Message, e);
    }

    /// <summary>Rejects an XML declaration that names an encoding other than UTF-8, as Go does.</summary>
    public static void CheckDeclaration(XmlReader r)
    {
        string? encoding = r.GetAttribute("encoding");
        if (!string.IsNullOrEmpty(encoding) && !string.Equals(encoding, "utf-8", StringComparison.OrdinalIgnoreCase))
        {
            throw MessageParseException.Malformed("encoding " + GoFormat.Quote(encoding) + " declared; only UTF-8 is supported");
        }
    }

    /// <summary>
    /// Moves past the start tag of the current element and returns the depth to
    /// pass to <see cref="NextChild"/>, or -1 when the element is empty.
    /// </summary>
    public static int Enter(XmlReader r)
    {
        if (r.IsEmptyElement)
        {
            r.Read();
            return -1;
        }

        int depth = r.Depth;
        r.Read();
        return depth;
    }

    /// <summary>
    /// Advances to the next child element of the element entered at
    /// <paramref name="depth"/> and returns true, or consumes its end tag and
    /// returns false. The caller must consume each child it is given.
    /// </summary>
    public static bool NextChild(XmlReader r, int depth)
    {
        if (depth < 0)
        {
            return false;
        }

        while (true)
        {
            switch (r.NodeType)
            {
                case XmlNodeType.Element:
                    return true;
                case XmlNodeType.EndElement when r.Depth == depth:
                    r.Read();
                    return false;
                default:
                    if (!r.Read())
                    {
                        throw new XmlException("unexpected end of input");
                    }

                    break;
            }
        }
    }

    /// <summary>Reads the direct character data of the current element and consumes it.</summary>
    public static string ReadText(XmlReader r)
    {
        if (r.IsEmptyElement)
        {
            r.Read();
            return string.Empty;
        }

        int depth = r.Depth;
        r.Read();
        string? single = null;
        StringBuilder? many = null;
        while (true)
        {
            switch (r.NodeType)
            {
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    if (single is null)
                    {
                        single = r.Value;
                    }
                    else
                    {
                        many ??= new StringBuilder(single);
                        many.Append(r.Value);
                    }

                    r.Read();
                    break;
                case XmlNodeType.Element:
                    r.Skip();
                    break;
                case XmlNodeType.EndElement when r.Depth == depth:
                    r.Read();
                    return many?.ToString() ?? single ?? string.Empty;
                default:
                    if (!r.Read())
                    {
                        throw new XmlException("unexpected end of input");
                    }

                    break;
            }
        }
    }

    /// <summary>Skips the current element and its content.</summary>
    public static void Skip(XmlReader r) => r.Skip();

    /// <summary>Reads an ISO 20022 amount element: the <c>Ccy</c> attribute and the decimal content.</summary>
    public static void ReadAmount(XmlReader r, Components.CurrencyAmount target)
    {
        if (r.MoveToFirstAttribute())
        {
            do
            {
                // Go matches attributes by local name in any namespace; the last one wins.
                if (r.LocalName == "Ccy")
                {
                    target.Ccy = r.Value;
                }
            }
            while (r.MoveToNextAttribute());

            r.MoveToElement();
        }

        string text = ReadText(r);
        string? error = BrlAmount.ParseCore(text, out long cents);
        if (error is not null)
        {
            throw new MessageParseException(MessageParseErrorKind.InvalidAmount, "money: invalid amount: " + error);
        }

        target.Value = new BrlAmount(cents);
    }

    /// <summary>Reads a boolean element with Go's <c>strconv.ParseBool</c> rules (empty is false).</summary>
    public static bool ReadBoolean(XmlReader r)
    {
        string text = ReadText(r);
        if (text.Length == 0)
        {
            return false;
        }

        switch (Text.GoTrimSpace(text))
        {
            case "1" or "t" or "T" or "TRUE" or "true" or "True":
                return true;
            case "0" or "f" or "F" or "FALSE" or "false" or "False":
                return false;
            default:
                throw new MessageParseException(
                    MessageParseErrorKind.InvalidBoolean,
                    "strconv.ParseBool: parsing " + GoFormat.Quote(Text.GoTrimSpace(text)) + ": invalid syntax");
        }
    }

    /// <summary>Parses a message identifier such as <c>pacs.008.001.08</c>, or the full namespace.</summary>
    public static bool TryParseMessageId(string? s, out MessageType id)
    {
        id = default;
        if (s is null)
        {
            return false;
        }

        if (s.StartsWith(NamespacePrefix, StringComparison.Ordinal))
        {
            s = s.Substring(NamespacePrefix.Length);
        }

        string[] parts = s.Split('.');
        if (parts.Length != 4 || parts[0].Length != 4 || parts[1].Length != 3 || parts[2].Length != 3 || parts[3].Length != 2)
        {
            return false;
        }

        foreach (char c in parts[0])
        {
            if (c < 'a' || c > 'z')
            {
                return false;
            }
        }

        for (int i = 1; i < 4; i++)
        {
            foreach (char c in parts[i])
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }
        }

        id = new MessageType(parts[0], parts[1], parts[2], parts[3]);
        return true;
    }

    /// <summary>The message identifier of a <c>Document</c> element's namespace.</summary>
    public static MessageType DocumentId(string ns)
    {
        if (!TryParseMessageId(ns, out MessageType id) || !ns.StartsWith(NamespacePrefix, StringComparison.Ordinal))
        {
            throw MessageParseException.UnknownNamespace(ns);
        }

        return id;
    }

    /// <summary>
    /// Finds the <c>Document</c> element in a bare Document or a business
    /// message wrapping an AppHdr and a Document, checks that its namespace is
    /// <paramref name="expectedBase"/> (e.g. <c>pacs.008</c>) in any version,
    /// reads it with <paramref name="read"/> and verifies that the rest of the
    /// input is well-formed and contains no second Document.
    /// </summary>
    public static T ReadDocument<T>(XmlReader r, string expectedBase, Func<XmlReader, string, T> read)
        where T : class
    {
        T? result = null;
        bool sawElement = false;
        try
        {
            bool advance = true;
            while (true)
            {
                if (advance && !r.Read())
                {
                    break;
                }

                advance = true;
                if (r.ReadState == ReadState.EndOfFile)
                {
                    break;
                }

                if (r.NodeType == XmlNodeType.XmlDeclaration)
                {
                    CheckDeclaration(r);
                    continue;
                }

                if (r.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                sawElement = true;
                if (r.LocalName != "Document")
                {
                    continue;
                }

                if (result is not null)
                {
                    throw MessageParseException.MultipleDocuments();
                }

                string ns = r.NamespaceURI;
                MessageType id = DocumentId(ns);
                if (id.Id != expectedBase)
                {
                    throw MessageParseException.Unexpected(id.ToString(), expectedBase);
                }

                result = read(r, ns);
                advance = false;
            }
        }
        catch (XmlException e)
        {
            throw Translate(e, sawElement);
        }
        catch (DecoderFallbackException e)
        {
            throw MessageParseException.Malformed(e.Message, e);
        }

        return result ?? throw MessageParseException.NoDocument();
    }
}
