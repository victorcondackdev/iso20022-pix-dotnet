using Iso20022.Pix.Ids;

namespace Iso20022.Pix.Tests;

public class IdsTests
{
    private const string SampleE2E = "E9999999920261121103000000000001";

    [Fact]
    public void ParsesEndToEndId()
    {
        PixId id = PixIdentifiers.ParseEndToEndId(SampleE2E);
        Assert.Equal('E', id.Prefix);
        Assert.Equal("99999999", id.Ispb);
        Assert.Equal(new DateTime(2026, 11, 21, 10, 30, 0, DateTimeKind.Utc), id.Time);
        Assert.Equal(DateTimeKind.Utc, id.Time.Kind);
        Assert.Equal("00000000001", id.Sequence);
        Assert.Equal(SampleE2E, id.ToString());
    }

    [Theory]
    [InlineData("", "must be 32 characters")]
    [InlineData("D9999999920261121103000000000001", "must start with 'E'")]
    [InlineData("E99999A9920261121103000000000001", "ISPB")]
    [InlineData("E999999992026112110X000000000001", "timestamp")]
    [InlineData("E9999999920261321103000000000001", "not a valid date")]
    [InlineData("E9999999920260230103000000000001", "not a valid date")]
    [InlineData("E99999999202611211030000000000-1", "alphanumeric")]
    public void RejectsInvalidEndToEndIds(string input, string fragment)
    {
        InvalidIdentifierException e = Assert.Throws<InvalidIdentifierException>(() => PixIdentifiers.ParseEndToEndId(input));
        Assert.Equal(IdentifierKind.EndToEndId, e.Kind);
        Assert.StartsWith("ids: invalid EndToEndId: ", e.Message);
        Assert.Contains(fragment, e.Message);
        Assert.False(PixIdentifiers.TryParseEndToEndId(input, out _));
    }

    [Fact]
    public void ReturnIdUsesItsOwnPrefix()
    {
        InvalidIdentifierException e = Assert.Throws<InvalidIdentifierException>(() => PixIdentifiers.ParseReturnId(SampleE2E));
        Assert.Equal(IdentifierKind.ReturnId, e.Kind);
        Assert.Equal("ids: invalid RtrId: must start with 'D'", e.Message);
        Assert.Equal("must start with 'D'", e.Detail);
    }

    [Fact]
    public void GeneratesIdentifiers()
    {
        var at = new DateTimeOffset(2026, 11, 21, 7, 30, 59, TimeSpan.FromHours(-3));
        string e2e = PixIdentifiers.NewEndToEndId("99999999", at);
        Assert.StartsWith("E99999999202611211030", e2e);
        Assert.True(PixIdentifiers.TryParseEndToEndId(e2e, out _));

        using MemoryStream rnd = TestData.Stream(Enumerable.Repeat(new byte[] { 0, 61, 255 }, 20).SelectMany(b => b).ToArray());
        string rtr = PixIdentifiers.NewReturnId("12345678", at, rnd);
        Assert.Equal("D123456782026112110300z0z0z0z0z0", rtr);

        Assert.Throws<InvalidIdentifierException>(() => PixIdentifiers.NewEndToEndId("1234", at));
        using MemoryStream unusable = TestData.Stream(Enumerable.Repeat((byte)255, 4096).ToArray());
        Assert.Throws<InvalidOperationException>(() => PixIdentifiers.NewEndToEndId("12345678", at, unusable));
        using var failing = new FailingStream();
        InvalidOperationException io = Assert.Throws<InvalidOperationException>(() => PixIdentifiers.NewEndToEndId("12345678", at, failing));
        Assert.Contains("boom", io.Message);
    }

    [Fact]
    public void ZeroRandomnessIsDeterministic()
    {
        using MemoryStream zeros = TestData.Stream(new byte[256]);
        string id = PixIdentifiers.NewEndToEndId("99999999", new DateTimeOffset(2026, 11, 21, 10, 30, 0, TimeSpan.Zero), zeros);
        Assert.Equal("E9999999920261121103000000000000", id);
    }

    [Fact]
    public void MessageIdentifiers()
    {
        string m = PixIdentifiers.NewMsgId("99999999");
        Assert.Equal(32, m.Length);
        Assert.StartsWith("M99999999", m);
        Assert.True(PixIdentifiers.IsValidMsgId(m));
        Assert.Throws<InvalidIdentifierException>(() => PixIdentifiers.NewMsgId("x"));
        foreach (string bad in new[] { string.Empty, "   ", new string('x', 36), ((char)0xD800).ToString() })
        {
            Assert.Throws<InvalidIdentifierException>(() => PixIdentifiers.ValidateMsgId(bad));
        }

        PixIdentifiers.ValidateMsgId(new string((char)0xE3, 35));
    }

    [Theory]
    [InlineData("52998224725", true, false)]
    [InlineData("11144477735", true, false)]
    [InlineData("11222333000181", false, true)]
    [InlineData("12ABC34501DE35", false, true)]
    [InlineData("52998224724", false, false)]
    [InlineData("11111111111", false, false)]
    [InlineData("529.982.247-25", false, false)]
    [InlineData("11222333000182", false, false)]
    [InlineData("00000000000000", false, false)]
    [InlineData("12abc34501de35", false, false)]
    public void TaxIdentifiers(string input, bool cpf, bool cnpj)
    {
        Assert.Equal(cpf, TaxIds.IsValidCpf(input));
        Assert.Equal(cnpj, TaxIds.IsValidCnpj(input));
    }

    [Theory]
    [InlineData("52998224725", PixKeyType.Cpf)]
    [InlineData("11222333000181", PixKeyType.Cnpj)]
    [InlineData("12ABC34501DE35", PixKeyType.Cnpj)]
    [InlineData("fulano@example.com", PixKeyType.Email)]
    [InlineData("+5521987654321", PixKeyType.Phone)]
    [InlineData("123e4567-e89b-12d3-a456-426614174000", PixKeyType.Evp)]
    public void ClassifiesPixKeys(string key, PixKeyType type)
    {
        Assert.Equal(type, PixKeys.Classify(key));
        Assert.NotEqual("UNKNOWN", PixKeys.DictName(type));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Fulano@example.com")]
    [InlineData("a@b@c.com")]
    [InlineData("a@nodot")]
    [InlineData("+0123")]
    [InlineData("+55 21 98765-4321")]
    [InlineData("123E4567-E89B-12D3-A456-426614174000")]
    [InlineData("529.982.247-25")]
    public void RejectsInvalidPixKeys(string key)
    {
        InvalidIdentifierException e = Assert.Throws<InvalidIdentifierException>(() => PixKeys.Classify(key));
        Assert.Equal(IdentifierKind.PixKey, e.Kind);
        Assert.False(PixKeys.TryClassify(key, out PixKeyType t));
        Assert.Equal(PixKeyType.Unknown, t);
    }

    [Fact]
    public void Ispb()
    {
        Assert.True(PixIdentifiers.IsValidIspb("00000000"));
        Assert.False(PixIdentifiers.IsValidIspb("0000000"));
        Assert.False(PixIdentifiers.IsValidIspb("0000000a"));
        Assert.False(PixIdentifiers.IsValidIspb(null));
    }

    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("boom");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
