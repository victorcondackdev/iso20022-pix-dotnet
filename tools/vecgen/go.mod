module github.com/victorcondackdev/iso20022-pix-dotnet/tools/vecgen

go 1.22

// The generator runs against a local checkout of the Go implementation,
// cloned next to this repository (../iso20022-pix-go relative to the
// repository root).
replace github.com/victorcondackdev/iso20022-pix-go => ../../../iso20022-pix-go

require github.com/victorcondackdev/iso20022-pix-go v0.0.0-00010101000000-000000000000
