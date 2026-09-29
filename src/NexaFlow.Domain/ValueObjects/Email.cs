namespace NexaFlow.Domain.ValueObjects;

/// <summary>
///     A normalized, validated email address stored as a value object.
///     <para>
///         Value objects are immutable and compared by value (section 10). The Domain
///         layer does not contain any email-sending logic — only the invariants of an
///         email address (well-formed, normalized lowercase, max 320 chars per RFC 5321).
///     </para>
/// </summary>
public sealed record Email
{
    private const int MaxLength = 320;

    /// <summary>The display form (preserves original case, trimmed).</summary>
    public string Value { get; }

    /// <summary>The normalized form (lowercase, trimmed) — used for uniqueness lookups.</summary>
    public string Normalized { get; }

    private Email(string value, string normalized)
    {
        Value = value;
        Normalized = normalized;
    }

    /// <summary>
    ///     Construct an <see cref="Email" /> from a raw input string.
    ///     Throws <see cref="FormatException" /> if the address is not a syntactically
    ///     valid email. This is a domain rule, not application input validation —
    ///     FluentValidation handles the user-facing error first (section 13).
    /// </summary>
    public static Email Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FormatException("Email is required.");

        var trimmed = raw.Trim();
        if (trimmed.Length > MaxLength)
            throw new FormatException($"Email exceeds maximum length of {MaxLength} characters.");

        // RFC-5321-ish lightweight check; the application validator gives a friendlier error.
        if (!trimmed.Contains('@') || trimmed.IndexOf('@') == trimmed.Length - 1)
            throw new FormatException($"Email '{trimmed}' is not a valid email address.");

        return new Email(trimmed, trimmed.ToLowerInvariant());
    }

    public static bool TryCreate(string? raw, out Email? email)
    {
        try
        {
            email = Create(raw);
            return true;
        }
        catch (FormatException)
        {
            email = null;
            return false;
        }
    }

    public override string ToString() => Value;
}
