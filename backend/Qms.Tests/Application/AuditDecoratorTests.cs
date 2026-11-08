using System.Data.Common;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Application.AuditFixtures;
using Qms.Tests.Persistence;

namespace Qms.Tests.Application
{
    // STORY-005 AC-31 (decorator mechanics) and AC-27 (a failing audit insert rolls the change back too), against a real database.
    [Collection(PostgresCollection.Name)]
    public sealed class AuditDecoratorTests(PostgresFixture postgres)
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 30, 0, TimeSpan.Zero);

        private sealed class Harness : IAsyncDisposable
        {
            public required ServiceProvider Provider { get; init; }

            public required string ConnectionString { get; init; }

            public required EventLog Log { get; init; }

            public required FakeUser User { get; init; }

            public ICommandHandler<TCommand, TResult> Handler<TCommand, TResult>() =>
                Provider.GetRequiredService<ICommandHandler<TCommand, TResult>>();

            public async Task<List<AuditLog>> AuditAsync()
            {
                await using var db = PostgresFixture.CreateContext(ConnectionString);
                return await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            }

            public async Task<bool> ServiceTypeExistsAsync(string prefix)
            {
                await using var db = PostgresFixture.CreateContext(ConnectionString);
                return await db.ServiceTypes.IgnoreQueryFilters().AnyAsync(s => s.Prefix == prefix);
            }

            public async ValueTask DisposeAsync() => await Provider.DisposeAsync();
        }

        private async Task<Harness> StartAsync(string role = Roles.CompanyAdmin)
        {
            var connectionString = await postgres.NewDatabaseAsync(seeded: true);
            var log = new EventLog();
            var user = new FakeUser(SeedIds.Company1, role);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(log);
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            services.AddSingleton<ICurrentUser>(user);
            services.AddSingleton(new LoggingTenant(SeedIds.Company1, log));
            services.AddSingleton<ICurrentTenant>(p => p.GetRequiredService<LoggingTenant>());
            services.AddSingleton<ITenantScope>(p => p.GetRequiredService<LoggingTenant>());
            services.AddScoped<QmsDbContext>(provider =>
            {
                var options = new DbContextOptionsBuilder<QmsDbContext>();
                options.UseQmsPostgres(connectionString).AddInterceptors(
                    new AuditableEntityInterceptor(provider.GetRequiredService<TimeProvider>()), new TransactionLog(provider.GetRequiredService<EventLog>()));
                return new QmsDbContext(options.Options, provider.GetRequiredService<ICurrentTenant>());
            });
            services.AddScoped<IQmsDbContext>(p => p.GetRequiredService<QmsDbContext>());
            services.AddHandlersAndValidators(typeof(AuditDecoratorTests).Assembly, type => type.Namespace == typeof(EventLog).Namespace);
            return new Harness { Provider = services.BuildServiceProvider(), ConnectionString = connectionString, Log = log, User = user };
        }

        [Fact]
        public async Task Ac31_the_order_is_validation_cross_tenant_audit_then_handler()
        {
            await using var harness = await StartAsync(Roles.SuperAdmin);
            using var scope = harness.Provider.CreateScope();

            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CrossTenantAuditedCommand, TestDto>>()
                .HandleAsync(new CrossTenantAuditedCommand("ok"), CancellationToken.None);

            result.IsSuccess.ShouldBeTrue();
            harness.Log.Events.ShouldBe(["validator", "cross-tenant", "transaction", "handler"]);
        }

        [Fact]
        public async Task Ac31_a_non_super_admin_is_refused_before_any_transaction_opens()
        {
            await using var harness = await StartAsync(Roles.CompanyAdmin);
            using var scope = harness.Provider.CreateScope();

            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CrossTenantAuditedCommand, TestDto>>()
                .HandleAsync(new CrossTenantAuditedCommand("ok"), CancellationToken.None);

            result.Error!.Code.ShouldBe("auth.forbidden");
            harness.Log.Events.ShouldBe(["validator"]);
            (await harness.AuditAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task Ac31_the_row_has_the_result_id_the_clock_time_the_user_and_the_data_without_not_audited_properties()
        {
            await using var harness = await StartAsync();
            using var scope = harness.Provider.CreateScope();

            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<AuditedCommand, TestDto>>()
                .HandleAsync(new AuditedCommand("ok", "p4ssw0rd!", TestKind.Second), CancellationToken.None);

            var row = (await harness.AuditAsync()).Single();
            row.EntityName.ShouldBe("test_entity");
            row.Action.ShouldBe("create");
            row.EntityId.ShouldBe(result.Value.Id);
            row.CompanyId.ShouldBe(SeedIds.Company1);
            row.UserId.ShouldBe(harness.User.UserId);
            row.DeviceId.ShouldBeNull();
            row.At.ShouldBe(Now);
            using var data = JsonDocument.Parse(row.Data!);
            data.RootElement.GetProperty("name").GetString().ShouldBe("ok");
            data.RootElement.GetProperty("kind").GetString().ShouldBe("Second");
            data.RootElement.TryGetProperty("secret", out _).ShouldBeFalse();
            row.Data!.ShouldNotContain("p4ssw0rd!");
            (await harness.ServiceTypeExistsAsync("Z")).ShouldBeTrue();
        }

        [Fact]
        public async Task Ac31_without_an_id_on_the_result_the_command_id_is_used()
        {
            await using var harness = await StartAsync();
            using var scope = harness.Provider.CreateScope();
            var id = Guid.NewGuid();

            (await scope.ServiceProvider.GetRequiredService<ICommandHandler<IdOnlyCommand, int>>()
                .HandleAsync(new IdOnlyCommand(id), CancellationToken.None)).IsSuccess.ShouldBeTrue();

            (await harness.AuditAsync()).Single().EntityId.ShouldBe(id);
        }

        [Fact]
        public async Task Ac31_commands_without_audited_or_with_not_audited_on_the_type_write_no_row()
        {
            await using var harness = await StartAsync();
            using var scope = harness.Provider.CreateScope();

            var plain = scope.ServiceProvider.GetRequiredService<ICommandHandler<PlainCommand, int>>();
            var optedOut = scope.ServiceProvider.GetRequiredService<ICommandHandler<OptedOutCommand, int>>();
            (await plain.HandleAsync(new PlainCommand(), CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await optedOut.HandleAsync(new OptedOutCommand(), CancellationToken.None)).IsSuccess.ShouldBeTrue();

            (await harness.AuditAsync()).ShouldBeEmpty();
            harness.Log.Events.ShouldNotContain("transaction");
            plain.ShouldBeOfType<ValidationDecorator<PlainCommand, int>>();
        }

        [Fact]
        public async Task Ac31_a_failing_validator_stops_before_any_transaction_opens()
        {
            await using var harness = await StartAsync();
            using var scope = harness.Provider.CreateScope();

            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<AuditedCommand, TestDto>>()
                .HandleAsync(new AuditedCommand("", "x", TestKind.First), CancellationToken.None);

            result.Error!.Type.ShouldBe(ErrorType.Validation);
            harness.Log.Events.ShouldBe(["validator"]);
            (await harness.AuditAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task Ac31_a_failed_result_rolls_back_the_handler_changes_and_writes_no_row()
        {
            await using var harness = await StartAsync();
            using var scope = harness.Provider.CreateScope();

            var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<AuditedCommand, TestDto>>()
                .HandleAsync(new AuditedCommand("fail", "x", TestKind.First), CancellationToken.None);

            result.IsFailure.ShouldBeTrue();
            (await harness.ServiceTypeExistsAsync("Z")).ShouldBeFalse();
            (await harness.AuditAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task Ac31_a_handler_exception_rolls_back_and_writes_no_row()
        {
            await using var harness = await StartAsync();
            using var scope = harness.Provider.CreateScope();

            await Should.ThrowAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ICommandHandler<AuditedCommand, TestDto>>()
                .HandleAsync(new AuditedCommand("throw", "x", TestKind.First), CancellationToken.None));

            (await harness.ServiceTypeExistsAsync("Z")).ShouldBeFalse();
            (await harness.AuditAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task Ac27_when_the_audit_insert_fails_the_data_change_is_rolled_back_too()
        {
            await using var harness = await StartAsync();
            using var scope = harness.Provider.CreateScope();

            // The entity name is longer than audit_log.entity (100 characters), so only the audit insert fails.
            await Should.ThrowAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<ICommandHandler<BrokenAuditCommand, int>>()
                .HandleAsync(new BrokenAuditCommand(), CancellationToken.None));

            (await harness.ServiceTypeExistsAsync("Z")).ShouldBeFalse();
            (await harness.AuditAsync()).ShouldBeEmpty();
        }

        [Fact]
        public async Task Post_commit_actions_run_after_the_commit_and_are_dropped_when_the_command_fails()
        {
            await using var harness = await StartAsync();

            using (var scope = harness.Provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<ICommandHandler<AuditedCommand, TestDto>>()
                    .HandleAsync(new AuditedCommand("fail", "x", TestKind.First), CancellationToken.None);
            }

            using (var scope = harness.Provider.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<ICommandHandler<AuditedCommand, TestDto>>()
                    .HandleAsync(new AuditedCommand("ok", "x", TestKind.First), CancellationToken.None);
            }

            using (var scope = harness.Provider.CreateScope())
            {
                // A failing action never fails the committed command.
                var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<AuditedCommand, TestDto>>()
                    .HandleAsync(new AuditedCommand("explode-after", "x", TestKind.First), CancellationToken.None);
                result.IsSuccess.ShouldBeTrue();
            }

            harness.Log.Events.Where(e => e.StartsWith("post-commit", StringComparison.Ordinal)).ShouldBe(["post-commit", "post-commit:explode"]);
        }
    }
}

namespace Qms.Tests.Application.AuditFixtures
{
    // Handlers, validators and commands for the decorator tests. Only this namespace is registered by the harness.
    public sealed class EventLog
    {
        private readonly List<string> _events = [];

        public IReadOnlyList<string> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public void Add(string value)
        {
            lock (_events)
            {
                _events.Add(value);
            }
        }
    }

    public sealed class LoggingTenant(Guid companyId, EventLog log) : ICurrentTenant, ITenantScope
    {
        private readonly CrossTenantSection _section = new();

        public Guid? CompanyId => companyId;

        public bool IsCrossTenant => _section.IsActive;

        public IDisposable BeginCrossTenant()
        {
            log.Add("cross-tenant");
            return _section.Begin();
        }
    }

    public sealed class TransactionLog(EventLog log) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            log.Add("transaction");
            return base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }

    public enum TestKind
    {
        First,
        Second,
    }

    public sealed record TestDto(Guid Id);

    [Audited("test_entity", "create")]
    public sealed record AuditedCommand(string? Name, [property: NotAudited] string? Secret, TestKind Kind);

    public sealed class AuditedCommandRules : AbstractValidator<AuditedCommand>
    {
        public AuditedCommandRules(EventLog log)
        {
            RuleFor(c => c.Name).Must(name =>
            {
                log.Add("validator");
                return !string.IsNullOrEmpty(name);
            });
        }
    }

    public sealed class AuditedCommandHandler(IQmsDbContext db, ICurrentTenant tenant, EventLog log, IPostCommitActions postCommit)
        : ICommandHandler<AuditedCommand, TestDto>
    {
        public async Task<Result<TestDto>> HandleAsync(AuditedCommand command, CancellationToken cancellationToken)
        {
            log.Add("handler");
            var row = new ServiceType { CompanyId = tenant.CompanyId!.Value, NameEn = "t", NameAr = "ت", Prefix = command.Name == "explode-after" ? "Y" : "Z" };
            db.ServiceTypes.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            postCommit.Enqueue(_ =>
            {
                log.Add(command.Name == "explode-after" ? "post-commit:explode" : "post-commit");
                return command.Name == "explode-after" ? throw new InvalidOperationException("boom") : Task.CompletedTask;
            });
            if (command.Name == "throw")
            {
                throw new InvalidOperationException("handler failed");
            }

            return command.Name == "fail" ? Error.Conflict("test.failed", "failed") : new TestDto(row.Id);
        }
    }

    [Audited("test_entity", "create")]
    public sealed record CrossTenantAuditedCommand(string? Name);

    public sealed class CrossTenantAuditedValidator : AbstractValidator<CrossTenantAuditedCommand>
    {
        public CrossTenantAuditedValidator(EventLog log) =>
            RuleFor(c => c.Name).Must(_ =>
            {
                log.Add("validator");
                return true;
            });
    }

    [CrossTenant]
    public sealed class CrossTenantAuditedHandler(EventLog log) : ICommandHandler<CrossTenantAuditedCommand, TestDto>
    {
        public Task<Result<TestDto>> HandleAsync(CrossTenantAuditedCommand command, CancellationToken cancellationToken)
        {
            log.Add("handler");
            return Task.FromResult<Result<TestDto>>(new TestDto(Guid.NewGuid()));
        }
    }

    [Audited("test_entity", "touch")]
    public sealed record IdOnlyCommand(Guid Id);

    public sealed class IdOnlyHandler : ICommandHandler<IdOnlyCommand, int>
    {
        public Task<Result<int>> HandleAsync(IdOnlyCommand command, CancellationToken cancellationToken) => Task.FromResult<Result<int>>(1);
    }

    public sealed record PlainCommand;

    public sealed class PlainHandler : ICommandHandler<PlainCommand, int>
    {
        public Task<Result<int>> HandleAsync(PlainCommand command, CancellationToken cancellationToken) => Task.FromResult<Result<int>>(1);
    }

    [Audited("test_entity", "create")]
    [NotAudited]
    public sealed record OptedOutCommand;

    public sealed class OptedOutHandler : ICommandHandler<OptedOutCommand, int>
    {
        public Task<Result<int>> HandleAsync(OptedOutCommand command, CancellationToken cancellationToken) => Task.FromResult<Result<int>>(1);
    }

    [Audited("e123456789e123456789e123456789e123456789e123456789e123456789e123456789e123456789e123456789e123456789e", "create")]
    public sealed record BrokenAuditCommand;

    public sealed class BrokenAuditHandler(IQmsDbContext db, ICurrentTenant tenant) : ICommandHandler<BrokenAuditCommand, int>
    {
        public async Task<Result<int>> HandleAsync(BrokenAuditCommand command, CancellationToken cancellationToken)
        {
            db.ServiceTypes.Add(new ServiceType { CompanyId = tenant.CompanyId!.Value, NameEn = "t", NameAr = "ت", Prefix = "Z" });
            await db.SaveChangesAsync(cancellationToken);
            return 1;
        }
    }
}
