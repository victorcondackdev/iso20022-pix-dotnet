using System.Text;
using System.Xml;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix;

/// <summary>
/// Tells which ISO 20022 message a payload carries (pacs.008, pacs.002,
/// pacs.004, camt.054, any version) before choosing a parser.
/// </summary>
/// <remarks>
/// Detection reads the input with a streaming <see cref="XmlReader"/> only as
/// far as the <c>Document</c> start element, so its cost does not depend on
/// the message size. Any wrapper element and an <c>AppHdr</c> before the
/// Document are skipped. DTD processing is prohibited and no external
/// resource is ever resolved.
/// </remarks>
public static class MessageDetector
{
    /// <summary>Returns the type of the message in <paramref name="stream"/>, reading only up to the Document start tag.</summary>
    /// <param name="stream">The payload. It is not closed.</param>
    /// <returns>The message type.</returns>
    /// <exception cref="MessageParseException">There is no ISO 20022 Document, or the XML before it is malformed.</exception>
    public static MessageType Detect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using XmlReader r = XmlInput.Create(stream);
        return Scan(r, inspect: false).Type;
    }

    /// <summary>Returns the type of the message in <paramref name="xml"/>.</summary>
    /// <param name="xml">The payload.</param>
    /// <returns>The message type.</returns>
    /// <exception cref="MessageParseException">There is no ISO 20022 Document, or the XML before it is malformed.</exception>
    public static MessageType Detect(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using XmlReader r = XmlInput.Create(xml);
        return Scan(r, inspect: false).Type;
    }

    /// <summary>Returns the type of the message in the UTF-8 bytes <paramref name="utf8"/>.</summary>
    /// <param name="utf8">The payload.</param>
    /// <returns>The message type.</returns>
    /// <exception cref="MessageParseException">There is no ISO 20022 Document, or the XML before it is malformed.</exception>
    public static MessageType Detect(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        using var stream = new MemoryStream(utf8, writable: false);
        return Detect(stream);
    }

    /// <summary>
    /// Detects the message type and also reads a head.001 <c>AppHdr</c> that
    /// precedes the Document. Like <see cref="Detect(Stream)"/>, stops at the
    /// Document start element.
    /// </summary>
    /// <param name="stream">The payload. It is not closed.</param>
    /// <returns>The header (if any) and the message type.</returns>
    /// <exception cref="MessageParseException">There is no ISO 20022 Document, more than one AppHdr, or malformed XML before the Document.</exception>
    public static Envelope Inspect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using XmlReader r = XmlInput.Create(stream);
        return Scan(r, inspect: true);
    }

    /// <summary>Inspects the message in <paramref name="xml"/>; see <see cref="Inspect(Stream)"/>.</summary>
    /// <param name="xml">The payload.</param>
    /// <returns>The header (if any) and the message type.</returns>
    /// <exception cref="MessageParseException">There is no ISO 20022 Document, more than one AppHdr, or malformed XML before the Document.</exception>
    public static Envelope Inspect(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        using XmlReader r = XmlInput.Create(xml);
        return Scan(r, inspect: true);
    }

    /// <summary>Inspects the message in the UTF-8 bytes <paramref name="utf8"/>; see <see cref="Inspect(Stream)"/>.</summary>
    /// <param name="utf8">The payload.</param>
    /// <returns>The header (if any) and the message type.</returns>
    /// <exception cref="MessageParseException">There is no ISO 20022 Document, more than one AppHdr, or malformed XML before the Document.</exception>
    public static Envelope Inspect(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        using var stream = new MemoryStream(utf8, writable: false);
        return Inspect(stream);
    }

    private static Envelope Scan(XmlReader r, bool inspect)
    {
        AppHdr? header = null;
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
                    XmlInput.CheckDeclaration(r);
                    continue;
                }

                if (r.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                sawElement = true;
                if (r.LocalName == "AppHdr")
                {
                    if (!inspect)
                    {
                        r.Skip();
                    }
                    else if (header is not null)
                    {
                        throw MessageParseException.MultipleHeaders();
                    }
                    else
                    {
                        header = AppHdr.Read(r);
                    }

                    advance = false;
                    continue;
                }

                if (r.LocalName == "Document")
                {
                    return new Envelope(header, XmlInput.DocumentId(r.NamespaceURI));
                }
            }
        }
        catch (XmlException e)
        {
            throw XmlInput.Translate(e, sawElement);
        }
        catch (DecoderFallbackException e)
        {
            throw MessageParseException.Malformed(e.Message, e);
        }

        throw MessageParseException.NoDocument();
    }
}
