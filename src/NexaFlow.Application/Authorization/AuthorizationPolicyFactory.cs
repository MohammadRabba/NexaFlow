using Microsoft.AspNetCore.Authorization;

namespace NexaFlow.Application.Authorization;

/// <summary>
///     ASP.NET Core authorization policy builder convenience. Wraps
///     <see cref="AuthorizationPolicyBuilder.RequireAssertion" /> so controllers can
///     name permissions by their <c>Permissions.X</c> const rather than build a
///     policy inline. This is a small helper, NOT a framework.
/// </summary>
public static class AuthorizationPolicyFactory
{
    public static AuthorizationPolicy RequirePermission(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}
