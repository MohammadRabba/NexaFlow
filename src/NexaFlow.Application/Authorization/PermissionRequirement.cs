using Microsoft.AspNetCore.Authorization;

namespace NexaFlow.Application.Authorization;

/// <summary>
///     ASP.NET Core authorization requirement that the current user hold a specific
///     permission in the resolved tenant. Combined with the tenant resolution, this
///     enforces "user U is a member of org O with role R, and role R grants permission P".
/// </summary>
/// <remarks>
///     This is plain ASP.NET Core — no custom authorization framework. The requirement
///     is enforced by <see cref="PermissionAuthorizationHandler" />, which queries the
///     database for the user's current role (database is authoritative; JWT carries
///     no role claims per Phase 2 directive).
/// </remarks>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Permission = permission;
    }
}
