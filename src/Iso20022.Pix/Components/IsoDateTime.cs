using System.Globalization;
using Iso20022.Pix.Internal;

namespace Iso20022.Pix.Components;

/// <summary>
/// ISO 20022 <c>ISODateTime</c> and <c>ISODate</c> values. Messages keep them
/// in their lexical form, so that parsing and re-serializing never changes
/// them; these helpers produce and check that form.
/// </summary>
public static class IsoDateTime
{
    /// <summary>
    /// Formats <paramref name="value"/> in UTC with millisecond precision
    /// (truncated), for example <c>2026-11-21T10:30:00.000Z</c>, as commonly
    /// used by the SPI.
    /// </summary>
    /// <param name="value">The instant.</param>
    /// <returns>The ISODateTime.</returns>
    public static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>Formats the calendar date of <paramref name="value"/> in its own offset as <c>YYYY-MM-DD</c>.</summary>
    /// <param name="value">The instant.</param>
    /// <returns>The ISODate.</returns>
    public static string FormatDate(DateTimeOffset value) =>
        value.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reports whether <paramref name="value"/> is an ISODateTime: a date and
    /// time with an optional fraction of a second and an optional <c>Z</c> or
    /// <c>+hh:mm</c>/<c>-hh:mm</c> offset.
    /// </summary>
    /// <param name="value">The lexical value.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValid(string? value) => value is not null && IsoTime.IsDateTime(value);

    /// <summary>Reports whether <paramref name="value"/> is an ISODate (<c>YYYY-MM-DD</c>, a real calendar date).</summary>
    /// <param name="value">The lexical value.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValidDate(string? value) => value is not null && IsoTime.IsDate(value);
}
