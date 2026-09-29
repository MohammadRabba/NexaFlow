using NexaFlow.Application.Abstractions;

namespace NexaFlow.Infrastructure.Services;

/// <summary>
///     BCrypt-based password hasher. Mature, well-audited, adaptive hashing.
///     Section 40: do NOT implement custom cryptography.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return BCrypt.Net.BCrypt.HashPassword(plaintext, WorkFactor);
    }

    public bool Verify(string plaintext, string storedHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedHash);
        try
        {
            return BCrypt.Net.BCrypt.Verify(plaintext, storedHash);
        }
        catch (FormatException)
        {
            // Stored hash is malformed — treat as verification failure, never throw.
            return false;
        }
    }
}
