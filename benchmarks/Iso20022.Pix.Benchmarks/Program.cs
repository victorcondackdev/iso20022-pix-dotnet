using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Iso20022.Pix.Ids;

namespace Iso20022.Pix.Benchmarks;

/// <summary>Entry point: <c>dotnet run -c Release --project benchmarks/Iso20022.Pix.Benchmarks -- --filter '*'</c>.</summary>
public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

    internal static string Load(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", name));
}

[MemoryDiagnoser]
public class MessageBenchmarks
{
    private string _pacs008 = string.Empty;
    private string _camt054 = string.Empty;
    private byte[] _large = Array.Empty<byte>();
    private Pacs008Document _document = new();

    [GlobalSetup]
    public void Setup()
    {
        _pacs008 = Program.Load("pacs008.xml");
        _camt054 = Program.Load("camt054.xml");
        _document = Pacs008Document.Parse(_pacs008);

        // A 1 MB message: detection cost must not depend on its size.
        string head = _pacs008.Substring(0, _pacs008.IndexOf("<FIToFICstmrCdtTrf>", StringComparison.Ordinal));
        _large = System.Text.Encoding.UTF8.GetBytes(head + new string(' ', 1 << 20) + "</Document>");
    }

    [Benchmark]
    public MessageType DetectSmall() => MessageDetector.Detect(_pacs008);

    [Benchmark]
    public MessageType DetectLarge() => MessageDetector.Detect(_large);

    [Benchmark]
    public int ParsePacs008() => Pacs008Document.Parse(_pacs008).Transactions.Count;

    [Benchmark]
    public int ParseCamt054() => Camt054Document.Parse(_camt054).Entries.Count;

    [Benchmark]
    public int Validate() => _document.Validate().Count;

    [Benchmark]
    public int Serialize() => _document.ToXml().Length;
}

[MemoryDiagnoser]
public class PrimitiveBenchmarks
{
    [Benchmark]
    public long ParseAmount() => BrlAmount.Parse("9999999999999999.99").Cents;

    [Benchmark]
    public string FormatAmount() => new BrlAmount(15075).ToIsoString();

    [Benchmark]
    public bool ParseEndToEndId() => PixIdentifiers.TryParseEndToEndId("E99999999202611211030a1B2c3D4e5F", out _);

    [Benchmark]
    public PixKeyType ClassifyKey() => PixKeys.Classify("pagamentos@example.com");

    [Benchmark]
    public string NewEndToEndId() => PixIdentifiers.NewEndToEndId("99999999", DateTimeOffset.UnixEpoch);
}
