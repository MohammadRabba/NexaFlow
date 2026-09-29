using FluentAssertions;
using NexaFlow.Application.Authorization;
using NexaFlow.Domain.Enums;
using Xunit;

namespace NexaFlow.Application.Tests.Authorization;

/// <summary>
///     Tests for the role→permission mapping. These are deliberately small: the mapping
///     is a configuration table, not behavior. The point is to make the business rules
///     explicit and to prevent accidental drift (e.g., adding a permission and forgetting
///     to grant it to the right roles).
/// </summary>
public sealed class RolePermissionsTests
{
    [Fact]
    public void Owner_has_all_permissions()
    {
        var perms = RolePermissions.For(OrganizationRole.Owner);
        perms.Should().Contain(Permissions.OrganizationDelete);
        perms.Should().Contain(Permissions.MemberTransferOwnership);
        perms.Should().Contain(Permissions.ProjectCreate);
        perms.Should().Contain(Permissions.TaskUpdate);
    }

    [Fact]
    public void Admin_can_invite_but_cannot_transfer_ownership_or_delete_org()
    {
        var perms = RolePermissions.For(OrganizationRole.Admin);
        perms.Should().Contain(Permissions.MemberInvite);
        perms.Should().Contain(Permissions.MemberUpdate);
        perms.Should().Contain(Permissions.MemberRemove);
        perms.Should().NotContain(Permissions.MemberTransferOwnership);
        perms.Should().NotContain(Permissions.OrganizationDelete);
    }

    [Fact]
    public void Member_can_create_projects_and_tasks_but_not_manage_members()
    {
        var perms = RolePermissions.For(OrganizationRole.Member);
        perms.Should().Contain(Permissions.ProjectCreate);
        perms.Should().Contain(Permissions.TaskCreate);
        perms.Should().Contain(Permissions.TaskUpdate);
        perms.Should().NotContain(Permissions.MemberInvite);
        perms.Should().NotContain(Permissions.MemberRemove);
        perms.Should().NotContain(Permissions.OrganizationUpdate);
    }

    [Fact]
    public void Viewer_can_only_read()
    {
        var perms = RolePermissions.For(OrganizationRole.Viewer);
        perms.Should().Contain(Permissions.OrganizationRead);
        perms.Should().Contain(Permissions.MemberRead);
        perms.Should().Contain(Permissions.ProjectRead);
        perms.Should().Contain(Permissions.TaskRead);
        perms.Should().NotContain(Permissions.OrganizationUpdate);
        perms.Should().NotContain(Permissions.MemberInvite);
        perms.Should().NotContain(Permissions.ProjectCreate);
        perms.Should().NotContain(Permissions.TaskUpdate);
    }

    [Fact]
    public void None_role_has_no_permissions()
    {
        var perms = RolePermissions.For(OrganizationRole.None);
        perms.Should().BeEmpty();
    }

    [Theory]
    [InlineData(OrganizationRole.Owner, Permissions.MemberTransferOwnership, true)]
    [InlineData(OrganizationRole.Admin, Permissions.MemberTransferOwnership, false)]
    [InlineData(OrganizationRole.Member, Permissions.MemberInvite, false)]
    [InlineData(OrganizationRole.Viewer, Permissions.ProjectRead, true)]
    [InlineData(OrganizationRole.Viewer, Permissions.ProjectCreate, false)]
    public void Has_check_matches_For_mapping(OrganizationRole role, string permission, bool expected)
    {
        RolePermissions.Has(role, permission).Should().Be(expected);
    }
}
