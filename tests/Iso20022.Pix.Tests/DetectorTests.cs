using System.Text;

namespace Iso20022.Pix.Tests;

public class DetectorTests
{
    private const string Ns = "urn:iso:std:iso:20022:tech:xsd:";

    [Theory]
    [InlineData("pacs008.xml", "pacs.008.001.08")]
    [InlineData("envelope_pacs008.xml", "pacs.008.001.08")]
    [InlineData("pacs002_accept.xml", "pacs.002.001.10")]
    [InlineData("pacs002_reject.xml", "pacs.002.001.10")]
    [InlineData("pacs004.xml", "pacs.004.001.09")]
    [InlineData("camt054.xml", "camt.054.001.08")]
    public void DetectsGoldenFiles(string file, string expected)
    {
        using FileStream s = File.OpenRead(TestData.Path(file));
        MessageType mt = MessageDetector.Detect(s);
        Assert.Equal(expected, mt.ToString());
        Assert.True(mt.IsSupported);
        Assert.NotEqual(string.Empty, mt.Name);
        Assert.Equal(Ns + expected, mt.Namespace);
    }

    [Fact]
    public void DetectsAnyVersionAndPrefix()
    {
        Assert.Equal(MessageIds.Pacs008, MessageDetector.Detect("<Document xmlns=\"" + Ns + "pacs.008.001.13\"/>").Id);
        Assert.Equal(
            MessageIds.Pacs008,
            MessageDetector.Detect("<ns:Document xmlns:ns=\"" + Ns + "pacs.008.001.02\"><ns:FIToFICstmrCdtTrf/></ns:Document>").Id);
        MessageType camt053 = MessageDetector.Detect("<Document xmlns=\"" + Ns + "camt.053.001.08\"/>");
        Assert.False(camt053.IsSupported);
        Assert.Equal(string.Empty, camt053.Name);
        Assert.Equal("08", camt053.Version);
    }

    [Theory]
    [InlineData("", MessageParseErrorKind.NoDocument)]
    [InlineData("<a><b/></a>", MessageParseErrorKind.NoDocument)]
    [InlineData("<Document/>", MessageParseErrorKind.UnknownNamespace)]
    [InlineData("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008\"/>", MessageParseErrorKind.UnknownNamespace)]
    [InlineData("<Document xmlns=\"urn:x:pacs.008.001.08\"/>", MessageParseErrorKind.UnknownNamespace)]
    [InlineData("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:PACS.008.001.08\"/>", MessageParseErrorKind.UnknownNamespace)]
    [InlineData("<!DOCTYPE Document><Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\"/>", MessageParseErrorKind.DirectiveForbidden)]
    [InlineData("<a><b></a>", MessageParseErrorKind.Malformed)]
    [InlineData("<E><AppHdr><x></AppHdr></E>", MessageParseErrorKind.Malformed)]
    public void DetectErrors(string input, MessageParseErrorKind kind)
    {
        MessageParseException e = Assert.Throws<MessageParseException>(() => MessageDetector.Detect(input));
        Assert.Equal(kind, e.Kind);
    }

    [Fact]
    public void ExternalEntitiesAreNeverResolved()
    {
        // A classic XXE payload: with DTD processing prohibited it is refused
        // before any entity could be declared, let alone fetched.
        const string xxe = "<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>"
            + "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\"><FIToFICstmrCdtTrf><GrpHdr><MsgId>&x;</MsgId></GrpHdr></FIToFICstmrCdtTrf></Document>";
        Assert.Equal(MessageParseErrorKind.DirectiveForbidden, Assert.Throws<MessageParseException>(() => MessageDetector.Detect(xxe)).Kind);
        Assert.Equal(MessageParseErrorKind.DirectiveForbidden, Assert.Throws<MessageParseException>(() => Pacs008Document.Parse(xxe)).Kind);

        const string bomb = "<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">]>"
            + "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\">&b;</Document>";
        Assert.Equal(MessageParseErrorKind.DirectiveForbidden, Assert.Throws<MessageParseException>(() => Pacs008Document.Parse(bomb)).Kind);
    }

    [Fact]
    public void DetectionIsStreaming()
    {
        string head = "<?xml version=\"1.0\"?><Document xmlns=\"" + Ns + "pacs.008.001.08\">";
        var body = new StringBuilder(head);
        for (int i = 0; i < 1 << 16; i++)
        {
            body.Append("<x>filler</x>");
        }

        body.Append("</Document>");
        using var counting = new CountingStream(Encoding.UTF8.GetBytes(body.ToString()));
        Assert.Equal(MessageIds.Pacs008, MessageDetector.Detect(counting).Id);
        Assert.True(counting.BytesRead < 16 * 1024, "read " + counting.BytesRead + " bytes of " + counting.Length);
    }

    [Fact]
    public void ParsesMessageTypes()
    {
        MessageType mt = MessageType.Parse("pacs.004.001.09");
        Assert.Equal(MessageIds.Pacs004, mt.Id);
        Assert.Equal("PaymentReturn", mt.Name);
        Assert.Equal(mt, MessageType.Parse(Ns + "pacs.004.001.09"));
        Assert.Throws<MessageParseException>(() => MessageType.Parse("pacs.4.1.9"));
        Assert.False(MessageType.TryParse("Pacs.008.001.08", out _));
        Assert.Equal(string.Empty, default(MessageType).ToString());
        Assert.Equal(string.Empty, default(MessageType).Id);
        Assert.Contains("Header", new MessageType("head", "001", "001", "01").Name);
    }

    [Fact]
    public void InspectsTheApplicationHeader()
    {
        Envelope env = MessageDetector.Inspect(TestData.Load("envelope_pacs008.xml"));
        AppHdr h = Assert.IsType<AppHdr>(env.Header);
        Assert.Equal("99999999", h.Fr.Ispb);
        Assert.Equal("00038166", h.To.Ispb);
        Assert.Equal("pacs.008.001.08", h.MsgDefIdr);
        Assert.Empty(env.Validate());
        Assert.Equal(MessageIds.Pacs008, env.Type.Id);

        Envelope bare = MessageDetector.Inspect(TestData.LoadBytes("pacs008.xml"));
        Assert.Null(bare.Header);
        Assert.Empty(bare.Validate());

        string mismatch = TestData.Load("envelope_pacs008.xml").Replace("<MsgDefIdr>pacs.008.001.08", "<MsgDefIdr>pacs.002.001.10", StringComparison.Ordinal);
        Violation v = Assert.Single(MessageDetector.Inspect(mismatch).Validate());
        Assert.Equal("/AppHdr/MsgDefIdr: is \"pacs.002.001.10\" but the Document is pacs.008.001.08", v.ToString());
    }

    [Theory]
    [InlineData("<E><AppHdr/><AppHdr/><Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\"/></E>")]
    [InlineData("<E><AppHdr><Fr></AppHdr></E>")]
    [InlineData("<E><AppHdr/></E>")]
    [InlineData("<E><AppHdr/><Document/></E>")]
    [InlineData("<E")]
    public void InspectRejects(string input) => Assert.Throws<MessageParseException>(() => MessageDetector.Inspect(input));

    [Fact]
    public void ApplicationHeaderRoundTrip()
    {
        var at = new DateTimeOffset(2026, 11, 21, 10, 30, 1, 123, TimeSpan.Zero);
        AppHdr h = AppHdr.Create("99999999", "00038166", "M9999999920261121103000000000001", "pacs.008.001.08", at);
        Assert.Empty(h.Validate());
        string xml = h.ToXml();
        Assert.Contains("<AppHdr xmlns=\"" + AppHdr.DefaultNamespace + "\">", xml);
        string envelope = "<E>" + xml.Substring(xml.IndexOf('\n', StringComparison.Ordinal) + 1)
            + "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08\"/></E>";
        Envelope env = MessageDetector.Inspect(envelope);
        AppHdr parsed = env.Header!;
        Assert.Equal(h.BizMsgIdr, parsed.BizMsgIdr);
        Assert.Equal("2026-11-21T10:30:01.123Z", parsed.CreDt);
        Assert.Empty(env.Validate());
        Assert.Contains("<AppHdr xmlns=\"" + AppHdr.DefaultNamespace + "\">", new AppHdr().ToXml());
    }

    [Fact]
    public void ApplicationHeaderValidation()
    {
        var bad = new AppHdr
        {
            To = new HeaderParty { FIId = new Components.Agent() },
            MsgDefIdr = "pacs8",
            BizSvc = new string('s', 36),
            CreDt = "yesterday",
        };
        IReadOnlyList<Violation> vs = bad.Validate();
        string[] prefixes =
        {
            "/AppHdr/Fr/FIId: is required",
            "/AppHdr/To/FIId/FinInstnId/ClrSysMmbId/MmbId",
            "/AppHdr/BizMsgIdr: is required",
            "/AppHdr/MsgDefIdr: must be a message identifier",
            "/AppHdr/BizSvc",
            "/AppHdr/CreDt",
        };
        Assert.Equal(prefixes.Length, vs.Count);
        for (int i = 0; i < prefixes.Length; i++)
        {
            Assert.StartsWith(prefixes[i], vs[i].ToString());
        }

        Assert.Contains(new AppHdr().Validate(), v => v.ToString() == "/AppHdr/MsgDefIdr: is required");
        Assert.Equal(string.Empty, new HeaderParty().Ispb);
    }

    private sealed class CountingStream : MemoryStream
    {
        public CountingStream(byte[] data)
            : base(data, writable: false)
        {
        }

        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = base.Read(buffer, offset, Math.Min(count, 64));
            BytesRead += n;
            return n;
        }

        public override int Read(Span<byte> buffer)
        {
            int n = base.Read(buffer.Slice(0, Math.Min(buffer.Length, 64)));
            BytesRead += n;
            return n;
        }
    }
}
