namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Abstraction over password hashing. Implementations use BCrypt (or an equivalent
///     adaptive hashing algorithm — never custom crypto — section 40). The hash
///     includes the salt and cost factor in the encoded string.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hash a plaintext password. The encoded string includes salt + cost factor.</summary>
    string Hash(string plaintext);

    /// <summary>
    ///     Verify a plaintext password against a stored hash.
    ///     Constant-time comparison (BCrypt internal) — section 40 (avoid timing attacks).
    /// </summary>
    bool Verify(string plaintext, string storedHash);
}
