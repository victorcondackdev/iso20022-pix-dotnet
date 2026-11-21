using System.Security.Cryptography;

namespace Iso20022.Pix.Internal;

/// <summary>
/// Draws identifier characters the way the Go implementation does, so that a
/// deterministic random stream yields the same identifiers in both.
/// </summary>
internal static class RandomSource
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// Fills <paramref name="destination"/> with characters drawn uniformly
    /// from [0-9A-Za-z]. Bytes are read 32 at a time from
    /// <paramref name="random"/> (or <see cref="RandomNumberGenerator"/> when
    /// null) and rejection-sampled: 248 = 4 * 62.
    /// </summary>
    public static void FillAlphanumeric(Span<char> destination, Stream? random)
    {
        Span<byte> buffer = stackalloc byte[32];
        int n = 0;
        for (int attempts = 0; n < destination.Length; attempts++)
        {
            // The cap only matters for a broken source that keeps returning bytes >= 248.
            if (attempts == 64)
            {
                throw new InvalidOperationException("ids: random source yields no usable bytes");
            }

            Read(random, buffer);
            foreach (byte c in buffer)
            {
                if (c < 248 && n < destination.Length)
                {
                    destination[n++] = Alphabet[c % 62];
                }
            }
        }
    }

    // io.ReadFull semantics: EOF before any byte, unexpected EOF after some.
    private static void Read(Stream? random, Span<byte> buffer)
    {
        if (random is null)
        {
            RandomNumberGenerator.Fill(buffer);
            return;
        }

        int total = 0;
        while (total < buffer.Length)
        {
            int read;
            try
            {
                read = random.Read(buffer.Slice(total));
            }
            catch (IOException e)
            {
                throw new InvalidOperationException("ids: reading random bytes: " + e.Message, e);
            }

            if (read == 0)
            {
                throw new InvalidOperationException(total == 0
                    ? "ids: reading random bytes: EOF"
                    : "ids: reading random bytes: unexpected EOF");
            }

            total += read;
        }
    }
}
