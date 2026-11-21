# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
adheres to [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-11-21

### Added

- `Pacs008Document`: FIToFICustomerCreditTransfer model (pacs.008.001.08
  layout) with `Parse`, `ToXml`, `Validate`, and the Pix-oriented
  `Pacs008Builder`.
- `Pacs002Document`: FIToFIPaymentStatusReport model (pacs.002.001.10
  layout), `Pacs002.Accept` and `Pacs002.Reject`, and a bilingual table of
  common status reason codes.
- `Pacs004Document`: PaymentReturn model (pacs.004.001.09 layout) and
  `Pacs004.FromPacs008`, which builds full or partial returns and refuses to
  return more than the original amount, including across prior partial
  returns.
- `Camt054Document`: BankToCustomerDebitCreditNotification model
  (camt.054.001.08 layout) with entry and reference helpers.
- `MessageDetector`: streaming message detection and head.001 `AppHdr`
  inspection, with DTD processing prohibited (XXE-safe).
- `Iso20022.Pix.Ids`: EndToEndId and RtrId parsing and generation, MsgId
  helpers, ISPB, CPF, numeric and alphanumeric CNPJ validation and Pix key
  classification.
- `BrlAmount`: exact BRL amounts in centavos with strict ISO 20022 decimal
  parsing and checked arithmetic.
- Validation that reports every violation with an XPath-like location.
- `Iso20022.Pix.Cli` (`iso20022`): `inspect`, `validate` and `return`
  commands.
- Parity vectors generated from the Go implementation, golden messages,
  seeded robustness tests and BenchmarkDotNet benchmarks.

[0.1.0]: https://github.com/victorcondackdev/iso20022-pix-dotnet/releases/tag/v0.1.0
