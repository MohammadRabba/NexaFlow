using System.Security.Cryptography;
using System.Text;
using NexaFlow.Application.Abstractions;

namespace NexaFlow.Infrastructure.Authentication;

/// <summary>
///     Generates cryptographically-secure random tokens and SHA-256 verifier hashes.
///     Section 40: uses framework cryptography primitives only — no custom crypto.
///     <para>
///         Token format: 32 bytes (256 bits) of entropy from <see cref="RandomNumberGenerator" />,
///         base64url-encoded (URL-safe, no padding). The hash is the SHA-256 of the
///         base64url-encoded plaintext, hex-encoded.
///     </para>
///     <para>
///         Why hash the BASE64URL string and not the raw bytes? So that the consumer
///         (handler) can hash the plaintext token string received from the client
///         via <see cref="HashPlaintext" /> and compare the hash directly to the stored
///         hash. Both operations use the same input — the string form.
///     </para>
/// </summary>
public sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    private const int TokenByteLength = 32; // 256 bits of entropy

    public (string PlaintextToken, string TokenHash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var plaintextToken = Base64UrlEncode(bytes);
        var tokenHash = HashPlaintext(plaintextToken);
        return (plaintextToken, tokenHash);
    }

    public string HashPlaintext(string plaintextToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextToken);
        var bytes = Encoding.UTF8.GetBytes(plaintextToken);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>URL-safe base64 encoding without padding (per RFC 4648 §5).</summary>
    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
