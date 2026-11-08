using Microsoft.EntityFrameworkCore;
using Npgsql;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Persistence;

// The two database features the queue engine (M3) relies on: the atomic token sequence and the partial queue index.
[Collection(PostgresCollection.Name)]
public sealed class QueueSupportTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = new(2026, 11, 8);

    [Fact]
    public async Task Parallel_token_issues_get_distinct_consecutive_sequences()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        const string sql = """
            INSERT INTO token_sequence (id, created_at, updated_at, company_id, branch_id, business_date, priority_type_id, service_type_id, last_sequence)
            VALUES (@id, now(), now(), @company, @branch, @date, @priority, @service, 1)
            ON CONFLICT (branch_id, business_date, priority_type_id, service_type_id)
            DO UPDATE SET last_sequence = token_sequence.last_sequence + 1, updated_at = now()
            RETURNING last_sequence
            """;

        var issues = Enumerable.Range(0, 25).Select(async _ =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("company", SeedIds.Company1);
            command.Parameters.AddWithValue("branch", SeedIds.Branch1);
            command.Parameters.AddWithValue("date", Today);
            command.Parameters.AddWithValue("priority", SeedIds.Vip);
            command.Parameters.AddWithValue("service", SeedIds.Service(2));
            var sequence = (int)(await command.ExecuteScalarAsync())!;
            await transaction.CommitAsync();
            return sequence;
        });

        var sequences = await Task.WhenAll(issues);

        sequences.Order().ShouldBe(Enumerable.Range(1, 25));
    }

    [Fact]
    public async Task The_call_next_query_uses_the_partial_queue_index()
    {
        var connectionString = await postgres.NewDatabaseAsync(seeded: true);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Few rows make the planner prefer a scan; ask it to avoid a sequential scan so the test checks index usability.
        await using (var setup = new NpgsqlCommand("SET enable_seqscan = off", connection))
        {
            await setup.ExecuteNonQueryAsync();
        }

        var plan = new List<string>();
        var sql = $"""
            EXPLAIN SELECT id FROM token_visit
            WHERE zone_id = '{SeedIds.Zone(1)}' AND service_type_id = '{SeedIds.Service(2)}' AND priority_type_id = '{SeedIds.Vip}'
              AND ended_at IS NULL AND called_at IS NULL
            ORDER BY is_front DESC, queued_at
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """;
        await using (var explain = new NpgsqlCommand(sql, connection))
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        var text = string.Join(Environment.NewLine, plan);
        text.ShouldContain("ix_token_visit_queue");
        text.ShouldNotContain("Sort");
    }
}
