using System.Xml;
using Iso20022.Pix.Components;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix;

/// <summary>
/// The ISO 20022 business application header (head.001), which travels next
/// to the Document and identifies sender, receiver and the message
/// definition. Pix participants are identified by ISPB in
/// <c>FIId/FinInstnId/ClrSysMmbId/MmbId</c>.
/// </summary>
public sealed record AppHdr
{
    /// <summary>The namespace used when serializing a header whose <see cref="Namespace"/> is empty.</summary>
    public const string DefaultNamespace = MessageIds.NamespacePrefix + "head.001.001.01";

    /// <summary>The namespace of the AppHdr element, as parsed; empty means <see cref="DefaultNamespace"/>.</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>The sender.</summary>
    public HeaderParty Fr { get; set; } = new();

    /// <summary>The receiver.</summary>
    public HeaderParty To { get; set; } = new();

    /// <summary>The business message identifier (Max35Text).</summary>
    public string BizMsgIdr { get; set; } = string.Empty;

    /// <summary>The message definition identifier, e.g. <c>pacs.008.001.08</c>.</summary>
    public string MsgDefIdr { get; set; } = string.Empty;

    /// <summary>The business service.</summary>
    public string BizSvc { get; set; } = string.Empty;

    /// <summary>The creation date and time (ISODateTime).</summary>
    public string CreDt { get; set; } = string.Empty;

    /// <summary>
    /// Returns a header from one ISPB to another for a business message
    /// identified by <paramref name="bizMsgIdr"/> carrying the message type
    /// <paramref name="msgDefIdr"/> (e.g. <c>pacs.008.001.08</c>).
    /// </summary>
    /// <param name="fromIspb">The sender's ISPB.</param>
    /// <param name="toIspb">The receiver's ISPB.</param>
    /// <param name="bizMsgIdr">The business message identifier.</param>
    /// <param name="msgDefIdr">The message definition identifier.</param>
    /// <param name="created">The creation time.</param>
    /// <returns>The header.</returns>
    public static AppHdr Create(string fromIspb, string toIspb, string bizMsgIdr, string msgDefIdr, DateTimeOffset created) => new()
    {
        Namespace = DefaultNamespace,
        Fr = new HeaderParty { FIId = Agent.FromIspb(fromIspb) },
        To = new HeaderParty { FIId = Agent.FromIspb(toIspb) },
        BizMsgIdr = bizMsgIdr,
        MsgDefIdr = msgDefIdr,
        CreDt = IsoDateTime.Format(created),
    };

    /// <summary>
    /// Checks the header and returns every violation: <c>Fr</c> and <c>To</c>
    /// identified by 8-digit ISPB, <c>BizMsgIdr</c> (Max35Text), <c>MsgDefIdr</c>
    /// a message identifier such as <c>pacs.008.001.08</c>, and <c>CreDt</c> an
    /// ISODateTime.
    /// </summary>
    /// <returns>The violations; empty when the header is valid.</returns>
    public IReadOnlyList<Violation> Validate()
    {
        var c = new Checker();
        const string p = "/AppHdr";
        foreach ((string name, HeaderParty? party) in new[] { ("Fr", Fr), ("To", To) })
        {
            Agent? fiid = party?.FIId;
            if (fiid is null)
            {
                c.Add(p + "/" + name + "/FIId", "is required");
                continue;
            }

            c.Agent(p + "/" + name + "/FIId", fiid);
        }

        c.Max35(p + "/BizMsgIdr", BizMsgIdr, true);
        if (string.IsNullOrEmpty(MsgDefIdr))
        {
            c.Add(p + "/MsgDefIdr", "is required");
        }
        else if (!XmlInput.TryParseMessageId(MsgDefIdr, out _))
        {
            c.Add(p + "/MsgDefIdr", "must be a message identifier such as pacs.008.001.08, got " + GoFormat.Quote(MsgDefIdr));
        }

        c.Max35(p + "/BizSvc", BizSvc, false);
        c.DateTime(p + "/CreDt", CreDt, true);
        return c.Result;
    }

    /// <summary>Serializes the header as indented XML with an XML declaration, identical to the Go output.</summary>
    /// <returns>The XML.</returns>
    public string ToXml()
    {
        var w = new XmlOutput();
        Write(w);
        return w.Finish();
    }

    internal void Write(XmlOutput w)
    {
        w.Start("AppHdr", string.IsNullOrEmpty(Namespace) ? DefaultNamespace : Namespace);
        HeaderParty.Write(w, "Fr", Fr);
        HeaderParty.Write(w, "To", To);
        w.Element("BizMsgIdr", BizMsgIdr);
        w.Element("MsgDefIdr", MsgDefIdr);
        w.Optional("BizSvc", BizSvc);
        w.Element("CreDt", CreDt);
        w.End("AppHdr");
    }

    internal static AppHdr Read(XmlReader r)
    {
        var h = new AppHdr { Namespace = r.NamespaceURI };
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Fr": HeaderParty.ReadInto(r, h.Fr); break;
                case "To": HeaderParty.ReadInto(r, h.To); break;
                case "BizMsgIdr": h.BizMsgIdr = XmlInput.ReadText(r); break;
                case "MsgDefIdr": h.MsgDefIdr = XmlInput.ReadText(r); break;
                case "BizSvc": h.BizSvc = XmlInput.ReadText(r); break;
                case "CreDt": h.CreDt = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }

        return h;
    }
}

/// <summary>The <c>Fr</c>/<c>To</c> party of the AppHdr, identified as a financial institution (<c>FIId</c>).</summary>
public sealed record HeaderParty
{
    /// <summary>The financial institution.</summary>
    public Agent? FIId { get; set; }

    /// <summary><c>FIId/FinInstnId/ClrSysMmbId/MmbId</c>, or an empty string.</summary>
    public string Ispb => FIId?.Ispb ?? string.Empty;

    internal static void Write(XmlOutput w, string name, HeaderParty? p)
    {
        w.Start(name);
        Agent? fiid = p?.FIId;
        if (fiid is not null)
        {
            Xsd.WriteAgent(w, "FIId", fiid);
        }

        w.End(name);
    }

    internal static void ReadInto(XmlReader r, HeaderParty p)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName == "FIId")
            {
                p.FIId ??= new Agent();
                Xsd.ReadAgent(r, p.FIId);
            }
            else
            {
                r.Skip();
            }
        }
    }
}

/// <summary>The result of <see cref="MessageDetector.Inspect(Stream)"/>: the optional AppHdr and the type of the Document.</summary>
/// <param name="Header">The header; null when the payload is a bare Document.</param>
/// <param name="Type">The type of the Document.</param>
public sealed record Envelope(AppHdr? Header, MessageType Type)
{
    /// <summary>
    /// Checks the header, when present, with <see cref="AppHdr.Validate"/> and
    /// verifies that its <c>MsgDefIdr</c> matches the Document namespace.
    /// </summary>
    /// <returns>The violations; empty when valid or when there is no header.</returns>
    public IReadOnlyList<Violation> Validate()
    {
        if (Header is null)
        {
            return Array.Empty<Violation>();
        }

        var c = new Checker();
        c.AddAll(Header.Validate());
        if (!string.IsNullOrEmpty(Header.MsgDefIdr) && Header.MsgDefIdr != Type.ToString())
        {
            c.Add("/AppHdr/MsgDefIdr", "is " + GoFormat.Quote(Header.MsgDefIdr) + " but the Document is " + Type.ToString());
        }

        return c.Result;
    }
}
