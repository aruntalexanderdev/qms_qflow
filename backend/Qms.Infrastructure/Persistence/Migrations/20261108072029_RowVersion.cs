using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qms.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Model-only migration (STORY-005, ADR-008 section 5): maps the PostgreSQL system column <c>xmin</c> as the row version of
    /// every <c>Entity</c>. <c>xmin</c> exists on every table already, so there is no DDL. EF Core generates an
    /// <c>AddColumn xmin</c> for each table when the property is first mapped; PostgreSQL rejects that (system column name),
    /// so the generated operations were removed before the migration was applied. The snapshot and the designer carry the model.
    /// </summary>
    public partial class RowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
