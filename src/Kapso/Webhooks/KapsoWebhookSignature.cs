using System.Buffers;
using System.Security.Cryptography;
using System.Text;

namespace Kapso.Webhooks;

/// <summary>
/// Verifies the <c>X-Webhook-Signature</c> header Kapso sends with every webhook.
/// </summary>
/// <remarks>
/// Kapso signs the raw request body with HMAC-SHA256 under the webhook's secret
/// and sends the digest as lowercase hex.
///
/// Verify against the bytes as they arrived. Re-serializing a parsed body does not
/// reproduce them — key order, whitespace and unicode escaping can all differ, and
/// every difference fails the check.
/// </remarks>
public static class KapsoWebhookSignature
{
    /// <summary>Header carrying the signature.</summary>
    public const string HeaderName = "X-Webhook-Signature";

    private const int DigestLength = 32;
    private const int HexLength = DigestLength * 2;

    /// <summary>
    /// Whether <paramref name="signature"/> is Kapso's signature over
    /// <paramref name="payload"/>.
    /// </summary>
    /// <param name="payload">The raw request body, exactly as received.</param>
    /// <param name="signature">Value of the <c>X-Webhook-Signature</c> header.</param>
    /// <param name="secret">The webhook's secret key.</param>
    /// <returns>
    /// <see langword="false"/> for a missing, malformed or wrong signature. Never
    /// throws on bad input: a forged header should produce a 401 from the caller,
    /// not an unhandled exception and a 500.
    /// </returns>
    public static bool Verify(ReadOnlySpan<byte> payload, ReadOnlySpan<char> signature, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        // Length is not secret, so returning early on it leaks nothing.
        if (signature.Length != HexLength)
        {
            return false;
        }

        // Decoded without exceptions: this input is attacker-controlled, and a
        // malformed header should cost a branch, not a throw.
        Span<byte> provided = stackalloc byte[DigestLength];
        if (Convert.FromHexString(signature, provided, out _, out var decoded) != OperationStatus.Done
            || decoded != DigestLength)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[DigestLength];
        var key = Encoding.UTF8.GetBytes(secret);
        HMACSHA256.HashData(key, payload, expected);

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    /// <summary>
    /// Computes the signature Kapso would send for a payload. Intended for tests
    /// and for fixtures that stand in for a real delivery.
    /// </summary>
    public static string Compute(ReadOnlySpan<byte> payload, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        Span<byte> digest = stackalloc byte[DigestLength];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload, digest);

        return Convert.ToHexStringLower(digest);
    }
}
