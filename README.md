# iso20022-pix-dotnet

[![CI](https://github.com/victorcondackdev/iso20022-pix-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/victorcondackdev/iso20022-pix-dotnet/actions/workflows/ci.yml)

Typed, validated ISO 20022 messages for Brazil's instant payment system,
Pix, as exchanged through the SPI (Sistema de Pagamentos Instantâneos),
for .NET 8. No dependencies beyond the base class library; trimming- and
AOT-friendly.

It covers the message flow a Pix participant handles every day: the payment
(`pacs.008`), its acceptance or rejection (`pacs.002`), the return
(`pacs.004`, including MED returns), the account notification (`camt.054`)
and the business application header (`head.001`). It is a port of
[iso20022-pix-go](https://github.com/victorcondackdev/iso20022-pix-go) and
is tested for parity against it: same accepted inputs, same violation paths
and messages, byte-identical XML.

```
dotnet add package Iso20022.Pix
```

## Why

The expensive bugs in instant-payment integrations are rarely about XML.
They are amounts that drift by a centavo, identifiers that are one
character short, returns that exceed what was paid, and validation errors
that surface one at a time, one rejected message after another. This
library makes those mistakes hard to write and cheap to diagnose:

- **Centavos, never floating point.** `BrlAmount` is a `readonly record
  struct` over `long` centavos with a strict ISO 20022 lexical grammar.
- **Every violation at once, with a location.** `Validate()` walks the whole
  message and returns `IReadOnlyList<Violation>`, each with an XPath-like
  path such as `/Document/FIToFICstmrCdtTrf/CdtTrfTxInf[0]/PmtId/EndToEndId`.
- **Builders that refuse impossible messages.** `Pacs002.Accept/Reject` and
  `Pacs004.FromPacs008` derive answers from the original payment; partial
  returns can never add up to more than was paid.

## Quick start

Detect what arrived, parse it, validate it, answer it:

```csharp
using Iso20022.Pix;

MessageType type = MessageDetector.Detect(stream); // reads only up to <Document>

if (type.Id == MessageIds.Pacs008)
{
    Pacs008Document payment = Pacs008Document.Parse(payload);
    IReadOnlyList<Violation> violations = payment.Validate();
    Pacs002Document report = violations.Count == 0
        ? Pacs002.Accept(payment, new Pacs002Options { SettledAt = DateTimeOffset.UtcNow })
        : Pacs002.Reject(payment, "BE17", new Pacs002Options
          {
              AdditionalInfo = violations.Take(3).Select(v => v.ToString()).ToArray(),
          });
    Send(report.ToXml());
}
```

Build a payment (MsgId and EndToEndId are generated, then the document is
validated; a `MessageValidationException` lists every violation):

```csharp
Pacs008Document doc = new Pacs008Builder(DateTimeOffset.UtcNow)
    .Add(new Transfer
    {
        Amount = BrlAmount.Parse("150.75"),
        Initiation = Pacs008.InitiationDict,
        Debtor = new TransferParty { Name = "Fulano de Tal", TaxId = "52998224725" },
        DebtorAccount = new TransferAccount { Ispb = "99999999", Branch = "0001", Number = "123456", Type = AccountTypes.Current },
        Creditor = new TransferParty { Name = "Loja Exemplo Ltda", TaxId = "11222333000181" },
        CreditorAccount = new TransferAccount { Ispb = "88888888", Branch = "0001", Number = "6543210", Type = AccountTypes.Current },
        PixKey = "pagamentos@example.com",
        RemittanceInfo = "Pedido 4521",
    })
    .Build();
string xml = doc.ToXml();
```

Return part of it:

```csharp
Pacs004Document ret = Pacs004.FromPacs008(payment, Pacs004.ReasonCustomerRefund, BrlAmount.Parse("50.00"),
    new Pacs004Options { PriorReturns = alreadyReturned }); // MessageBuildException (AmountExceeded) past the original
```

Validation reports everything at once:

```
/Document/FIToFICstmrCdtTrf/CdtTrfTxInf[0]/PmtId/EndToEndId: must be 32 characters, got 4
/Document/FIToFICstmrCdtTrf/CdtTrfTxInf[0]/IntrBkSttlmAmt/@Ccy: must be "BRL", got "USD"
/Document/FIToFICstmrCdtTrf/CdtTrfTxInf[0]/Dbtr/Id/PrvtId/Othr[0]/Id: must be a valid CPF, got "12345678900"
```

## Message coverage

| Message  | ISO name                              | Layout follows | Parse / ToXml | Validate | Helpers                                  |
|----------|---------------------------------------|----------------|---------------|----------|------------------------------------------|
| pacs.008 | FIToFICustomerCreditTransfer          | .001.08        | yes           | yes      | `Pacs008Builder`                         |
| pacs.002 | FIToFIPaymentStatusReport             | .001.10        | yes           | yes      | `Pacs002.Accept`, `Reject`, `Reasons`    |
| pacs.004 | PaymentReturn                         | .001.09        | yes           | yes      | `Pacs004.FromPacs008`, `Reasons`         |
| camt.054 | BankToCustomerDebitCreditNotification | .001.08        | yes           | yes      | `Entries`, `EndToEndIds`, `Signed`       |
| head.001 | BusinessApplicationHeader (AppHdr)    | .001.01        | via `Inspect` | yes      | `AppHdr.Create`, MsgDefIdr consistency   |

`Parse` accepts any version of a message (`pacs.008.001.xx`), from a
`string`, a `Stream` or UTF-8 bytes, and either a bare `Document` or a
business message that wraps an `AppHdr` and a `Document`. Only the elements
Pix uses are modelled; others are ignored on parse and not re-emitted.

Supporting namespaces:

| Namespace                 | Purpose |
|---------------------------|---------|
| `Iso20022.Pix`            | Messages, builders, `BrlAmount`, `MessageDetector`, `Violation`, exceptions |
| `Iso20022.Pix.Ids`        | EndToEndId and RtrId (parse/generate), MsgId, ISPB, CPF, CNPJ (numeric and alphanumeric), Pix keys |
| `Iso20022.Pix.Components` | Shared ISO 20022 components (parties, accounts, agents, amounts, dates, code tables) |

## Command line

```
dotnet tool install --global Iso20022.Pix.Cli

iso20022 inspect  pacs008.xml                          # detect and print key fields
iso20022 validate pacs008.xml                          # list every violation, exit 1 if any
iso20022 return -reason MD06 -amount 10.00 pacs008.xml > pacs004.xml
```

`-` reads from standard input. Sample messages live in [`testdata/`](testdata).

## Safe parsing (XXE)

All parsing goes through `System.Xml.XmlReader` with `DtdProcessing.Prohibit`
and `XmlResolver = null`. A payload that carries a DOCTYPE is rejected with
`MessageParseErrorKind.DirectiveForbidden` before any entity can be
declared, so external entities (XXE), entity-expansion bombs and
network or file access during parsing are ruled out by construction. The
same payloads are refused by the Go implementation, which rejects every XML
directive.

`MessageDetector.Detect` and `Inspect` read a stream only as far as the
`Document` start tag: a gateway can route mixed traffic without buffering or
parsing the body, and detection cost does not grow with message size.

## Design notes

**Hand-written XML, not `XmlSerializer`.** Each message has explicit read and
write code. That keeps the library free of reflection (trimming- and
AOT-safe), fixes element order and namespace handling, and lets the
decoder follow the Go rules exactly: children matched by local name in any
namespace, unknown elements skipped, a repeated element overwriting text and
merging into components. Output is written by a small emitter rather than
`XmlWriter`, because byte identity with Go's `encoding/xml` requires its
escaping (`&#34;`, `&#39;`, `&#xA;`, ...) and its `<X></X>` form for empty
elements.

**Strict at the edges, lenient where the catalog evolves.** Parsing rejects
malformed XML, DTDs, the namespace of another message, a second `Document`
and malformed amounts. Validation enforces the ISO 20022 types (Max35Text,
Max140Text, ISODateTime, code choices) and the stable Pix rules: BRL,
`CLRG`, `SLEV`, 8-digit ISPBs, the EndToEndId and RtrId layouts, CPF and
CNPJ check digits, Pix key shapes. Reason codes outside the bundled tables
are kept and accepted, because the SPI catalog changes faster than
libraries do.

**Models mirror the XML.** Records are named after the ISO 20022 tags
(`MsgId`, `CdtTrfTxInf`, `Dbtr`, `Othr`), so code reads like the message
definition report. As in Go, an empty string means "absent" and `null`
means an absent optional component; collections are never null. Record
equality compares collections by reference, so compare `ToXml()` output for
structural equality.

## Parity with the Go implementation

[`tools/vecgen`](tools/vecgen) runs
[iso20022-pix-go](https://github.com/victorcondackdev/iso20022-pix-go) over a
deterministic corpus and writes the observed outputs as C# literals
(`tests/Iso20022.Pix.Tests/Parity/GoVectors.g.cs`). The parity tests require
identical results for:

- the golden files: `ToXml(Parse(file))` is byte-identical to the file;
- about 150 mutated documents (replaced text, deleted, duplicated, renamed
  and unknown elements, namespace changes, comments, CDATA, truncation):
  parse outcome, every violation path and message, and the SHA-256 of the
  serialized XML;
- over 400 model mutations (a field set to an edge-case value: control
  characters, non-printable Unicode, overlong text, out-of-range amounts,
  lenient ISO dates) with violations and serialized XML;
- amount parsing and formatting, ISO dates, EndToEndId/RtrId parsing,
  generation from fixed random streams, MsgId, CPF/CNPJ, Pix keys;
- message detection and AppHdr inspection;
- 37 builder scenarios with a fixed clock and fixed random bytes, including
  every refusal message.

To regenerate, clone the Go repository next to this one and run
`go -C tools/vecgen run . > tests/Iso20022.Pix.Tests/Parity/GoVectors.g.cs`.

Known differences, all outside the vectors: `XmlReader` refuses several
top-level elements and top-level text where Go's decoder tolerates them,
normalizes literal white space in attribute values, and reports its own
wording for malformed XML; an EndToEndId or RtrId dated year 0000 is
rejected because `System.DateTime` cannot represent it.

## Scope and disclaimer

This project is not affiliated with, endorsed by or derived from documents
of the Banco Central do Brasil. It models ISO 20022 elements as defined by
the ISO 20022 message definitions and the Pix conventions in public
regulation. The BCB publishes SPI catalogs that further restrict these
messages (allowed elements, cardinalities, code lists, lengths and
versions) and revises them over time. Validate production traffic against
the current catalog; `Validate()` here is a strong first line, not a
certification. Sample ISPBs, CPFs and CNPJs in this repository are
fictitious or publicly known test values.

## License

[MIT](LICENSE)
