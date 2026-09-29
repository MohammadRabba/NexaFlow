namespace NexaFlow.Application.Abstractions;

/// <summary>
///     Generates cryptographically-secure random tokens and the SHA-256 verifier hash
///     that gets stored. The plaintext is returned to the caller (who is responsible
///     for delivering it out-of-band — email, return-once-from-API). The hash is the
///     ONLY thing persisted.
///     <para>
///         Section 40: do NOT implement custom cryptography. We use
///         <c>System.Security.Cryptography.RandomNumberGenerator</c> for entropy and
///         <c>SHA256</c> for the verifier — both framework primitives.
///     </para>
/// </summary>
public interface ISecureTokenGenerator
{
    /// <summary>
    ///     Generate a new random token. Returns the plaintext (one-time, sent to the
    ///     caller) and the SHA-256 hash (persisted).
    /// </summary>
    (string PlaintextToken, string TokenHash) Generate();

    /// <summary>
    ///     Compute the SHA-256 hash of an existing plaintext token. Used at consumption
    ///     time: the client sends the plaintext token, the handler hashes it and looks
    ///     up the stored hash. Constant-time comparison happens downstream (DB index).
    /// </summary>
    string HashPlaintext(string plaintextToken);
}
