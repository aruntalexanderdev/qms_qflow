using Microsoft.Extensions.DependencyInjection;
using Qms.Application.Auth;
using Qms.Application.Common;

namespace Qms.Tests.Auth;

// Handler rules with hand-written fakes: no database, no HTTP.
public sealed class HandlerUnitTests
{
    private static readonly CurrentUserDto Ada = new(Guid.NewGuid(), "ada", "Ada", Roles.CompanyAdmin, Guid.NewGuid(), "en", [], []);

    private sealed class FakeAccounts : IUserAccounts
    {
        public PasswordCheckResult Check { get; set; } = new(PasswordCheckOutcome.InvalidCredentials);

        public CurrentUserDto? Active { get; set; }

        public Task<PasswordCheckResult> CheckPasswordAsync(string userName, string password, CancellationToken cancellationToken) =>
            Task.FromResult(Check);

        public Task<CurrentUserDto?> FindActiveAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Active);
    }

    private sealed class FakeTokens : ITokenService
    {
        public AccessToken CreateAccessToken(CurrentUserDto user) => new("access-for-" + user.UserName, DateTimeOffset.UtcNow.AddMinutes(15));
    }

    private sealed class FakeRefreshStore : IRefreshTokenStore
    {
        public RefreshRotation Rotation { get; set; } = new(RefreshRotationOutcome.Invalid);

        public List<string> Revoked { get; } = [];

        public int Issued { get; private set; }

        public string? LastIp { get; private set; }

        public Task<IssuedRefreshToken> IssueAsync(Guid userId, string? clientIp, CancellationToken cancellationToken)
        {
            Issued++;
            LastIp = clientIp;
            return Task.FromResult(new IssuedRefreshToken("refresh-1", DateTimeOffset.UtcNow.AddDays(7)));
        }

        public Task<RefreshRotation> RotateAsync(string token, string? clientIp, CancellationToken cancellationToken) => Task.FromResult(Rotation);

        public Task RevokeFamilyAsync(string token, CancellationToken cancellationToken)
        {
            Revoked.Add(token);
            return Task.CompletedTask;
        }
    }

    // ---- login

    [Fact]
    public async Task Login_succeeds_with_an_access_token_and_a_new_refresh_family()
    {
        var accounts = new FakeAccounts { Check = new PasswordCheckResult(PasswordCheckOutcome.Succeeded, Ada) };
        var store = new FakeRefreshStore();

        var result = await new LoginHandler(accounts, new FakeTokens(), store).HandleAsync(new LoginCommand("ada", "pw", "10.0.0.1"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("access-for-ada");
        result.Value.RefreshToken.ShouldBe("refresh-1");
        result.Value.User.ShouldBe(Ada);
        store.Issued.ShouldBe(1);
        store.LastIp.ShouldBe("10.0.0.1");
    }

    [Theory]
    [InlineData(PasswordCheckOutcome.InvalidCredentials, "auth.invalid_credentials")]
    [InlineData(PasswordCheckOutcome.LockedOut, "auth.locked_out")]
    public async Task Login_failures_are_401_with_their_code_and_issue_nothing(PasswordCheckOutcome outcome, string code)
    {
        var store = new FakeRefreshStore();

        var result = await new LoginHandler(new FakeAccounts { Check = new PasswordCheckResult(outcome) }, new FakeTokens(), store)
            .HandleAsync(new LoginCommand("ada", "pw"), CancellationToken.None);

        result.Error!.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
        store.Issued.ShouldBe(0);
    }

    [Fact]
    public void The_login_validator_requires_both_fields_and_caps_their_length()
    {
        var validator = new LoginValidator();

        validator.Validate(new LoginCommand("ada", "pw")).IsValid.ShouldBeTrue();
        validator.Validate(new LoginCommand(null, "pw")).IsValid.ShouldBeFalse();
        validator.Validate(new LoginCommand("ada", "")).IsValid.ShouldBeFalse();
        validator.Validate(new LoginCommand("ada", new string('p', 257))).IsValid.ShouldBeFalse();
    }

    // ---- refresh

    [Fact]
    public async Task Refresh_without_a_token_is_refresh_invalid()
    {
        var result = await NewRefresh(new FakeRefreshStore(), new FakeAccounts()).HandleAsync(new RefreshSessionCommand(" "), CancellationToken.None);

        result.Error!.Code.ShouldBe("auth.refresh_invalid");
    }

    [Fact]
    public async Task Refresh_maps_reuse_and_invalid_to_their_codes()
    {
        var store = new FakeRefreshStore { Rotation = new RefreshRotation(RefreshRotationOutcome.Reused) };
        (await NewRefresh(store, new FakeAccounts()).HandleAsync(new RefreshSessionCommand("t"), CancellationToken.None))
            .Error!.Code.ShouldBe("auth.refresh_reused");

        store.Rotation = new RefreshRotation(RefreshRotationOutcome.Invalid);
        (await NewRefresh(store, new FakeAccounts()).HandleAsync(new RefreshSessionCommand("t"), CancellationToken.None))
            .Error!.Code.ShouldBe("auth.refresh_invalid");
    }

    [Fact]
    public async Task Refresh_rotates_and_returns_new_tokens()
    {
        var store = new FakeRefreshStore
        {
            Rotation = new RefreshRotation(RefreshRotationOutcome.Rotated, Ada.Id, new IssuedRefreshToken("refresh-2", DateTimeOffset.UtcNow.AddDays(7))),
        };

        var result = await NewRefresh(store, new FakeAccounts { Active = Ada }).HandleAsync(new RefreshSessionCommand("refresh-1"), CancellationToken.None);

        result.Value.RefreshToken.ShouldBe("refresh-2");
        result.Value.AccessToken.ShouldBe("access-for-ada");
        store.Revoked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Refresh_ends_the_session_when_the_user_is_gone_or_inactive()
    {
        var store = new FakeRefreshStore
        {
            Rotation = new RefreshRotation(RefreshRotationOutcome.Rotated, Ada.Id, new IssuedRefreshToken("refresh-2", DateTimeOffset.UtcNow.AddDays(7))),
        };

        var result = await NewRefresh(store, new FakeAccounts { Active = null }).HandleAsync(new RefreshSessionCommand("refresh-1"), CancellationToken.None);

        result.Error!.Code.ShouldBe("auth.refresh_invalid");
        store.Revoked.ShouldBe(["refresh-2"]);
    }

    private static RefreshSessionHandler NewRefresh(FakeRefreshStore store, FakeAccounts accounts) => new(store, accounts, new FakeTokens());

    // ---- logout and me

    [Fact]
    public async Task Logout_revokes_the_family_and_is_idempotent_without_a_token()
    {
        var store = new FakeRefreshStore();
        var handler = new LogoutHandler(store);

        (await handler.HandleAsync(new LogoutCommand("refresh-1"), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await handler.HandleAsync(new LogoutCommand(null), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        store.Revoked.ShouldBe(["refresh-1"]);
    }

    private sealed class FakeCurrentUser(Guid? userId, string? role = null) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null;

        public Guid? UserId => userId;

        public Guid? CompanyId => null;

        public string? Role => role;

        public string? Language => "en";

        public bool IsInRole(string value) => role == value;
    }

    [Fact]
    public async Task Me_reads_the_signed_in_user_and_is_401_when_nobody_or_an_inactive_user()
    {
        var found = await new GetCurrentUserHandler(new FakeCurrentUser(Ada.Id), new FakeAccounts { Active = Ada })
            .HandleAsync(new GetCurrentUserQuery(), CancellationToken.None);
        var inactive = await new GetCurrentUserHandler(new FakeCurrentUser(Ada.Id), new FakeAccounts { Active = null })
            .HandleAsync(new GetCurrentUserQuery(), CancellationToken.None);
        var nobody = await new GetCurrentUserHandler(new FakeCurrentUser(null), new FakeAccounts { Active = Ada })
            .HandleAsync(new GetCurrentUserQuery(), CancellationToken.None);

        found.Value.ShouldBe(Ada);
        inactive.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        nobody.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    // ---- CrossTenantDecorator (AC-11, AC-12 at unit level)

    private sealed class RecordingScope : ITenantScope
    {
        public int Begun { get; private set; }

        public int Disposed { get; private set; }

        public bool Open => Begun > Disposed;

        public IDisposable BeginCrossTenant()
        {
            Begun++;
            return new Handle(this);
        }

        private sealed class Handle(RecordingScope owner) : IDisposable
        {
            public void Dispose() => owner.Disposed++;
        }
    }

    private sealed class ProbeHandler(RecordingScope scope) : ICommandHandler<string, bool>
    {
        public int Runs { get; private set; }

        public bool SectionOpenInside { get; private set; }

        public Task<Result<bool>> HandleAsync(string command, CancellationToken cancellationToken)
        {
            Runs++;
            SectionOpenInside = scope.Open;
            return Task.FromResult(command == "fail" ? Result<bool>.Failure(Error.Conflict("x.y", "no")) : Result<bool>.Success(true));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Roles.CompanyAdmin)]
    [InlineData(Roles.BranchManager)]
    [InlineData(Roles.CounterStaff)]
    public async Task The_decorator_refuses_everyone_but_a_Super_Admin_without_running_the_handler(string? role)
    {
        var scope = new RecordingScope();
        var probe = new ProbeHandler(scope);
        var decorator = new CrossTenantDecorator<string, bool>(probe, new FakeCurrentUser(role is null ? null : Guid.NewGuid(), role), scope);

        var result = await decorator.HandleAsync("go", CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.Forbidden);
        result.Error.Code.ShouldBe("auth.forbidden");
        probe.Runs.ShouldBe(0);
        scope.Begun.ShouldBe(0);
    }

    [Theory]
    [InlineData("go")]
    [InlineData("fail")]
    public async Task The_decorator_opens_the_section_for_a_Super_Admin_for_exactly_the_handler_call(string command)
    {
        var scope = new RecordingScope();
        var probe = new ProbeHandler(scope);
        var decorator = new CrossTenantDecorator<string, bool>(probe, new FakeCurrentUser(Guid.NewGuid(), Roles.SuperAdmin), scope);

        await decorator.HandleAsync(command, CancellationToken.None);

        probe.Runs.ShouldBe(1);
        probe.SectionOpenInside.ShouldBeTrue();
        scope.Open.ShouldBeFalse();
        scope.Disposed.ShouldBe(1);
    }

    private sealed class ThrowingHandler : ICommandHandler<string, bool>
    {
        public Task<Result<bool>> HandleAsync(string command, CancellationToken cancellationToken) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task The_section_is_closed_even_when_the_handler_throws()
    {
        var scope = new RecordingScope();
        var decorator = new CrossTenantDecorator<string, bool>(new ThrowingHandler(), new FakeCurrentUser(Guid.NewGuid(), Roles.SuperAdmin), scope);

        await Should.ThrowAsync<InvalidOperationException>(() => decorator.HandleAsync("go", CancellationToken.None));

        scope.Open.ShouldBeFalse();
    }

    // ---- registration: only [CrossTenant] handlers get the decorator, validation stays outermost

    [CrossTenant]
    private sealed class MarkedHandler : ICommandHandler<MarkedCommand, bool>
    {
        public Task<Result<bool>> HandleAsync(MarkedCommand command, CancellationToken cancellationToken) => Task.FromResult(Result<bool>.Success(true));
    }

    private sealed class PlainHandler : ICommandHandler<PlainCommand, bool>
    {
        public Task<Result<bool>> HandleAsync(PlainCommand command, CancellationToken cancellationToken) => Task.FromResult(Result<bool>.Success(true));
    }

    private sealed record MarkedCommand;

    private sealed record PlainCommand;

    [Fact]
    public async Task Only_handlers_marked_CrossTenant_are_wrapped_by_the_decorator()
    {
        var services = new ServiceCollection();
        services.AddHandlersAndValidators(typeof(HandlerUnitTests).Assembly, type => type.DeclaringType == typeof(HandlerUnitTests));
        var scope = new RecordingScope();
        services.AddSingleton<ITenantScope>(scope);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser(Guid.NewGuid(), Roles.CompanyAdmin));
        await using var provider = services.BuildServiceProvider();
        using var requestScope = provider.CreateScope();

        var plain = await requestScope.ServiceProvider.GetRequiredService<ICommandHandler<PlainCommand, bool>>().HandleAsync(new PlainCommand(), CancellationToken.None);
        var marked = await requestScope.ServiceProvider.GetRequiredService<ICommandHandler<MarkedCommand, bool>>().HandleAsync(new MarkedCommand(), CancellationToken.None);

        plain.IsSuccess.ShouldBeTrue();
        marked.Error!.Code.ShouldBe("auth.forbidden");
        scope.Begun.ShouldBe(0);
    }
}
