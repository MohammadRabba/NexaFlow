using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using NexaFlow.Application.Abstractions;
using NexaFlow.Application.Features.Organizations.Dtos;
using NexaFlow.Domain.Entities;
using NexaFlow.Domain.Exceptions;

namespace NexaFlow.Application.Features.Organizations.Commands;

public sealed class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, OrganizationDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CreateOrganizationCommandHandler> _logger;

    public CreateOrganizationCommandHandler(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<CreateOrganizationCommandHandler> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<OrganizationDto> Handle(CreateOrganizationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Authenticated user — fail closed (the controller's [Authorize] should have caught this,
        // but defense in depth: the handler does not trust the controller).
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId)
        {
            throw new DomainException("Authenticated user required to create an organization.", "UNAUTHENTICATED");
        }

        var now = DateTimeOffset.UtcNow;

        // Slug: derive from name if not supplied; lowercase; ensure uniqueness.
        var slug = string.IsNullOrWhiteSpace(request.SlugSuggestion)
            ? Slugify(request.Name)
            : request.SlugSuggestion!.Trim().ToLowerInvariant();

        // Check slug uniqueness. (The DB unique constraint would also catch this, but
        // we want to return a friendly 409 instead of a 500 from a constraint violation.)
        var slugTaken = await _db.IsOrganizationSlugTakenAsync(slug, cancellationToken);
        if (slugTaken)
        {
            throw new DomainException(
                $"An organization with slug '{slug}' already exists. Choose a different name or slug.",
                "ORGANIZATION_SLUG_TAKEN");
        }

        var org = Organization.Create(request.Name, slug, userId, now);
        _db.Add(org);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Organization {OrgId} created by user {UserId} (slug: {Slug}).",
            org.Id, userId, org.Slug);

        return new OrganizationDto(
            Id: org.Id,
            Name: org.Name,
            Slug: org.Slug,
            OwnerUserId: org.OwnerUserId,
            CreatedAtUtc: org.CreatedAtUtc);
    }

    /// <summary>
    ///     Convert an organization name to a URL-friendly slug. Lowercase, hyphenated.
    ///     "Acme Inc." → "acme-inc". Short non-alpha sequences collapse to a single hyphen.
    /// </summary>
    private static string Slugify(string name)
    {
        var trimmed = name.Trim().ToLowerInvariant();
        var slug = new StringBuilder(trimmed.Length);
        var lastWasHyphen = false;
        foreach (var c in trimmed)
        {
            if (char.IsLetterOrDigit(c))
            {
                slug.Append(c);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                slug.Append('-');
                lastWasHyphen = true;
            }
        }
        // Trim leading/trailing hyphens
        var result = slug.ToString().Trim('-');
        if (result.Length < 2) result = result + "-org";
        return result.Length > 60 ? result[..60] : result;
    }
}
