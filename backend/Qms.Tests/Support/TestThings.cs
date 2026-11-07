using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Qms.Api.Common;
using Qms.Application.Common;

namespace Qms.Tests.Support;

// Test-only use case, endpoints and validator. They live in the test assembly and are only added to the test host.

public sealed record ThingPart(string? Name);

public sealed record CreateThingCommand(string? Name, int Quantity, IReadOnlyList<ThingPart>? Parts = null);

public sealed class CreateThingValidator : AbstractValidator<CreateThingCommand>
{
    public CreateThingValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(20);
        RuleFor(command => command.Quantity).GreaterThan(0);
        RuleForEach(command => command.Parts).ChildRules(part => part.RuleFor(p => p.Name).NotEmpty());
    }
}

public sealed class CreateThingHandler : ICommandHandler<CreateThingCommand, Guid>
{
    public Task<Result<Guid>> HandleAsync(CreateThingCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(command.Name == "duplicate"
            ? Result<Guid>.Failure(Error.Conflict("thing.duplicate", "A thing with this name already exists."))
            : Result<Guid>.Success(Guid.NewGuid()));
}

public sealed record GetThingQuery(string? Id);

public sealed class GetThingValidator : AbstractValidator<GetThingQuery>
{
    public GetThingValidator()
    {
        RuleFor(query => query.Id).NotEmpty();
    }
}

public sealed class GetThingHandler : IQueryHandler<GetThingQuery, string>
{
    public Task<Result<string>> HandleAsync(GetThingQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result<string>.Failure(Error.NotFound("thing.not_found", "No such thing.")));
}

[Route(ApiRoutes.Prefix + "/test")]
public sealed class TestThingsController : QmsControllerBase
{
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("secret internal detail");

    [HttpPost("things")]
    public async Task<IActionResult> Create(
        CreateThingCommand command,
        [FromServices] ICommandHandler<CreateThingCommand, Guid> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(command, cancellationToken)).ToActionResult(this);

    [HttpGet("things/{id}")]
    public async Task<IActionResult> Get(
        string id,
        [FromServices] IQueryHandler<GetThingQuery, string> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetThingQuery(id), cancellationToken)).ToActionResult(this);
}
