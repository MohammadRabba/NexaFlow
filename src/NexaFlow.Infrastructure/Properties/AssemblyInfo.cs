using System.Runtime.CompilerServices;

// Infrastructure exposes its internal helpers (CurrentUserService.SetPrincipal,
// CurrentTenantService.SetTenant) to the Api host and to test projects only.
[assembly: InternalsVisibleTo("NexaFlow.Api")]
[assembly: InternalsVisibleTo("NexaFlow.Infrastructure.Tests")]
[assembly: InternalsVisibleTo("NexaFlow.IntegrationTests")]
