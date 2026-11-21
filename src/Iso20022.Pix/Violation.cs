namespace Iso20022.Pix;

/// <summary>
/// One validation failure at an XPath-like location, for example
/// <c>/Document/FIToFICstmrCdtTrf/CdtTrfTxInf[0]/PmtId/EndToEndId</c>.
/// Indexes are zero-based.
/// </summary>
/// <param name="Path">The location of the offending element or attribute.</param>
/// <param name="Message">What is wrong with it.</param>
public readonly record struct Violation(string Path, string Message)
{
    /// <summary>Returns <c>path: message</c>.</summary>
    /// <returns>The formatted violation.</returns>
    public override string ToString() => Path + ": " + Message;
}
