namespace NexaFlow.Domain.Common;

/// <summary>
///     Marker interface for entities that belong to a specific tenant (Organization).
///     <para>
///         Implementing this interface opts the entity into:
///         <list type="bullet">
///             <item>EF Core global query filter on OrganizationId (section 27).</item>
///             <item>Required <c>OrganizationId</c> column with appropriate indexes.</item>
///             <item>Application-level tenant validation in command handlers.</item>
///         </list>
///     </para>
///     <para>
///         IMPORTANT (section 27): global query filters are a defense-in-depth layer,
///         NOT the only layer. Authorization handlers must still verify resource-level
///         access (e.g., "can U act on project P?"). Cross-tenant tests are required
///         (Phase 3).
///     </para>
/// </summary>
public interface ITenantEntity
{
    /// <summary>
    ///     The organization that owns this row. Set by the Application layer from
    ///     <c>ICurrentTenantService.OrganizationId</c>; never trusted from the client.
    /// </summary>
    Guid OrganizationId { get; }
}
