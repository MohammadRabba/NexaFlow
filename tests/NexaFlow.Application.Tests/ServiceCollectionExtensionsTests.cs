using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NexaFlow.Application;
using Xunit;

namespace NexaFlow.Application.Tests;

/// <summary>
///     Smoke tests for the Application layer DI wiring (Phase 1).
///     Verifies that <c>AddApplication</c> registers MediatR, FluentValidation,
///     and the pipeline behaviors without error. Once Phase 2 adds actual
///     command handlers, these tests will be expanded with handler-level tests.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddApplication_should_register_mediator_services()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddApplication();

        // Assert
        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetService<IMediator>();
        mediator.Should().NotBeNull();
    }

    [Fact]
    public void AddApplication_should_complete_without_throwing()
    {
        // Act
        var act = () =>
        {
            var services = new ServiceCollection();
            services.AddApplication();
            using var sp = services.BuildServiceProvider();
            return sp;
        };

        // Assert — Phase 1 has no validators/handlers, but MediatR + behaviors still register
        act.Should().NotThrow();
    }
}
