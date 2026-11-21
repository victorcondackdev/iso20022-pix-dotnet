using System.Text;
using Iso20022.Pix.Ids;

namespace Iso20022.Pix.Tests;

/// <summary>
/// The .NET analogue of the Go fuzz targets: a deterministic stream of
/// mutated messages and random strings that must never make the library
/// throw anything other than its documented exceptions, and that must keep
/// the round-trip invariants the Go fuzzers check.
/// </summary>
public class RobustnessTests
{
    private const int Cases = 4000;

    private static readonly string[] Goldens =
    {
        "pacs008.xml", "envelope_pacs008.xml", "pacs002_accept.xml", "pacs002_reject.xml", "pacs004.xml", "camt054.xml",
    };

    private const string Alphabet = "<>/&;=\"' \n\t#xX0123456789.:-ABCDEFabcdef!?[]";

    // Non-ASCII, multi-byte, a lone surrogate, NUL, NBSP and U+FFFE, built from code points.
    private static readonly string[] Exotic =
        new[] { 0x00E3, 0x20AC, 0xD800, 0x0000, 0x00A0, 0xFFFE }
            .Select(c => ((char)c).ToString())
            .Append(char.ConvertFromUtf32(0x1F642))
            .ToArray();

    [Fact]
    public void MutatedMessagesOnlyThrowDocumentedExceptions()
    {
        string[] seeds = Goldens.Select(TestData.Load).ToArray();
        var random = new Random(20261121);
        var failures = new List<string>();
        for (int i = 0; i < Cases && failures.Count < 10; i++)
        {
            string input = Mutate(random, seeds[random.Next(seeds.Length)]);
            try
            {
                Exercise(input);
            }
            catch (Exception e) when (e is not Iso20022Exception)
            {
                failures.Add(e.GetType().Name + ": " + e.Message + "\n" + input);
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void RandomIdentifiersNeverThrow()
    {
        var random = new Random(42);
        string[] seeds = { "E9999999920261121103000000000001", "fulano@example.com", "+5521987654321", "150.75", "2026-11-21T10:30:00Z", string.Empty };
        for (int i = 0; i < 20_000; i++)
        {
            string s = Mutate(random, seeds[random.Next(seeds.Length)]);
            if (PixIdentifiers.TryParseEndToEndId(s, out PixId id))
            {
                Assert.Equal(s, id.ToString());
                Assert.Equal(0, id.Time.Second);
            }

            PixIdentifiers.TryParseReturnId(s, out _);
            Assert.Equal(PixKeys.TryClassify(s, out PixKeyType t), t != PixKeyType.Unknown);
            if (BrlAmount.TryParse(s, out BrlAmount a))
            {
                Assert.Equal(a, BrlAmount.Parse(a.ToIsoString()));
            }

            _ = Components.IsoDateTime.IsValid(s);
            _ = Components.IsoDateTime.IsValidDate(s);
            _ = PixIdentifiers.IsValidMsgId(s);
            _ = TaxIds.IsValidCpf(s) || TaxIds.IsValidCnpj(s);
        }
    }

    [Fact]
    public void GeneratedIdentifiersAlwaysParse()
    {
        var random = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var seed = new byte[16];
            random.NextBytes(seed);
            byte[] bytes = Enumerable.Repeat(seed.Append((byte)1), 64).SelectMany(b => b).ToArray();
            DateTimeOffset at = DateTimeOffset.FromUnixTimeSeconds(random.NextInt64(-62135596800, 253402300799));
            using MemoryStream rnd = TestData.Stream(bytes);
            string e2e = PixIdentifiers.NewEndToEndId("99999999", at, rnd);
            Assert.True(PixIdentifiers.TryParseEndToEndId(e2e, out _), e2e);
        }
    }

    private static void Exercise(string input)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(input);
        Try(() => MessageDetector.Detect(input));
        Try(() => MessageDetector.Detect(utf8));
        Try(() => MessageDetector.Inspect(input).Validate());
        Try(() => RoundTrip(Pacs008Document.Parse(input), d => d.Validate(), d => d.ToXml(), Pacs008Document.Parse));
        Try(() => RoundTrip(Pacs008Document.Parse(utf8), d => d.Validate(), d => d.ToXml(), Pacs008Document.Parse));
        Try(() => RoundTrip(Pacs002Document.Parse(input), d => d.Validate(), d => d.ToXml(), Pacs002Document.Parse));
        Try(() => RoundTrip(Pacs004Document.Parse(input), d => d.Validate(), d => d.ToXml(), Pacs004Document.Parse));
        Try(() => RoundTrip(Camt054Document.Parse(input), d => d.Validate(), d => d.ToXml(), Camt054Document.Parse));
    }

    // Parse succeeded: validation must not throw, and whatever serializes must
    // parse back and serialize to the same text.
    private static int RoundTrip<T>(T doc, Func<T, IReadOnlyList<Violation>> validate, Func<T, string> toXml, Func<string, T> parse)
    {
        _ = validate(doc);
        string xml;
        try
        {
            xml = toXml(doc);
        }
        catch (InvalidAmountException)
        {
            return 0; // amounts above the maximum cannot be serialized
        }

        T back;
        try
        {
            back = parse(xml);
        }
        catch (MessageParseException e)
        {
            throw new InvalidOperationException("serialized output does not parse back: " + e.Message + "\n" + xml, e);
        }

        string again = toXml(back);
        if (!string.Equals(xml, again, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("serialization is not stable:\n" + xml + "\n---\n" + again);
        }

        return 1;
    }

    private static void Try(Func<object> action)
    {
        try
        {
            _ = action();
        }
        catch (MessageParseException)
        {
            // Documented: the payload is not a valid message of that type.
        }
    }

    private static string Mutate(Random random, string s)
    {
        var b = new StringBuilder(s);
        int edits = 1 + random.Next(3);
        for (int e = 0; e < edits; e++)
        {
            string piece = random.Next(8) == 0
                ? Exotic[random.Next(Exotic.Length)]
                : Alphabet[random.Next(Alphabet.Length)].ToString();
            int at = b.Length == 0 ? 0 : random.Next(b.Length);
            switch (random.Next(4))
            {
                case 0 when b.Length > 0:
                    b.Remove(at, 1).Insert(at, piece);
                    break;
                case 1 when b.Length > 0:
                    b.Remove(at, 1);
                    break;
                case 2 when b.Length > 40:
                    int len = random.Next(1, 40);
                    b.Insert(at, b.ToString(random.Next(b.Length - len), len));
                    break;
                default:
                    b.Insert(at, piece);
                    break;
            }
        }

        return b.ToString();
    }
}
