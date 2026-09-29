namespace NexaFlow.Domain.ValueObjects;

/// <summary>
///     Strongly-typed wrapper for an Organization identifier. Carries the invariant
///     "an empty Guid is not a valid tenant". Using this type in command / query signatures
///     prevents accidental misuse of an unrelated Guid where a tenant is expected (section 6).
/// </summary>
public readonly struct TenantId : IEquatable<TenantId>
{
    public Guid Value { get; }

    public TenantId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("TenantId must not be Guid.Empty.", nameof(value));
        Value = value;
    }

    public static TenantId From(Guid value) => new(value);

    /// <summary>Try to construct a <see cref="TenantId" />. Returns false for Guid.Empty.</summary>
    public static bool TryFrom(Guid value, out TenantId tenantId)
    {
        if (value == Guid.Empty)
        {
            tenantId = default;
            return false;
        }
        tenantId = new TenantId(value);
        return true;
    }

    public bool Equals(TenantId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is TenantId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value.ToString();

    /// <summary>Explicit conversion to <see cref="Guid" /> (preferred over implicit per CA2225).</summary>
    public Guid ToGuid() => Value;

    public static bool operator ==(TenantId left, TenantId right) => left.Equals(right);
    public static bool operator !=(TenantId left, TenantId right) => !left.Equals(right);

    public static implicit operator Guid(TenantId tenantId) => tenantId.Value;
}
