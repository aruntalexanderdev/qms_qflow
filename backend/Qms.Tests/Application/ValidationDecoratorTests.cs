using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Qms.Application.Common;
using Qms.Tests.Support;

namespace Qms.Tests.Application;

public sealed class ValidationDecoratorTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddHandlersAndValidators(typeof(ValidationDecoratorTests).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Invalid_command_returns_validation_error_and_does_not_call_the_handler()
    {
        using var provider = BuildProvider();
        var handler = provider.GetRequiredService<ICommandHandler<CreateThingCommand, Guid>>();

        var result = await handler.HandleAsync(new CreateThingCommand("", 0), CancellationToken.None);

        handler.ShouldBeOfType<ValidationDecorator<CreateThingCommand, Guid>>();
        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("validation.failed");
        result.Error.Details!.Keys.ShouldBe(["Name", "Quantity"], ignoreOrder: true);
    }

    [Fact]
    public async Task Valid_command_reaches_the_handler()
    {
        using var provider = BuildProvider();
        var handler = provider.GetRequiredService<ICommandHandler<CreateThingCommand, Guid>>();

        var result = await handler.HandleAsync(new CreateThingCommand("ok", 1), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Invalid_query_returns_validation_error()
    {
        using var provider = BuildProvider();
        var handler = provider.GetRequiredService<IQueryHandler<GetThingQuery, string>>();

        var result = await handler.HandleAsync(new GetThingQuery(null), CancellationToken.None);

        handler.ShouldBeOfType<QueryValidationDecorator<GetThingQuery, string>>();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public async Task Valid_query_reaches_the_handler()
    {
        using var provider = BuildProvider();
        var handler = provider.GetRequiredService<IQueryHandler<GetThingQuery, string>>();

        var result = await handler.HandleAsync(new GetThingQuery("1"), CancellationToken.None);

        result.Error!.Code.ShouldBe("thing.not_found");
    }

    [Fact]
    public void AddApplication_registers_without_error()
    {
        var services = new ServiceCollection();

        Should.NotThrow(() => services.AddApplication());
    }
}
