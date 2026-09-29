using System.Security.Cryptography;
using System.Text;
using NexaFlow.Application.Abstractions;

namespace NexaFlow.Application.Tests.TestDoubles;

/// <summary>
///     Test double for <see cref="ISecureTokenGenerator" /> — uses the same algorithm
///     as the production SecureTokenGenerator (RandomNumberGenerator + SHA256) so tests
///     can validate the token round-trip behavior without referencing Infrastructure.
/// </summary>
public sealed class FakeSecureTokenGenerator : ISecureTokenGenerator
{
    private const int TokenByteLength = 32;

    public (string PlaintextToken, string TokenHash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var plaintext = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return (plaintext, HashPlaintext(plaintext));
    }

    public string HashPlaintext(string plaintextToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextToken);
        var bytes = Encoding.UTF8.GetBytes(plaintextToken);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
