using System.Runtime.CompilerServices;

// Domain exposes its internal setters and helper methods to Infrastructure and
// Application only. This keeps the domain invariants strong (no public setters)
// while allowing the DbContext in Infrastructure to stamp audit metadata without
// reflection, and Application handlers to raise domain events on aggregates.
[assembly: InternalsVisibleTo("NexaFlow.Application")]
[assembly: InternalsVisibleTo("NexaFlow.Infrastructure")]
[assembly: InternalsVisibleTo("NexaFlow.Application.Tests")]
[assembly: InternalsVisibleTo("NexaFlow.Infrastructure.Tests")]
[assembly: InternalsVisibleTo("NexaFlow.IntegrationTests")]
