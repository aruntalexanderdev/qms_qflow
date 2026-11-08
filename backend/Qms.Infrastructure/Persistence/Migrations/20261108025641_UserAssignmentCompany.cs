using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserAssignmentCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AC-23: refuse (and change nothing: the whole migration runs in one transaction) when an existing assignment
            // already crosses companies or belongs to a user without a company (Super Admin).
            migrationBuilder.Sql(@"
DO $$
DECLARE offending integer;
BEGIN
    SELECT (SELECT count(*) FROM user_branch ub
              JOIN branch b ON b.id = ub.branch_id
              JOIN asp_net_users u ON u.id = ub.user_id
             WHERE u.company_id IS DISTINCT FROM b.company_id)
         + (SELECT count(*) FROM user_counter uc
              JOIN counter c ON c.id = uc.counter_id
              JOIN asp_net_users u ON u.id = uc.user_id
             WHERE u.company_id IS DISTINCT FROM c.company_id)
      INTO offending;
    IF offending > 0 THEN
        RAISE EXCEPTION 'UserAssignmentCompany: % user_branch/user_counter row(s) assign a user to another company or a user without a company; fix or delete them first.', offending;
    END IF;
END $$;");

            migrationBuilder.DropForeignKey(
                name: "fk_user_branch_branch_branch_id",
                table: "user_branch");

            migrationBuilder.DropForeignKey(
                name: "fk_user_counter_counter_counter_id",
                table: "user_counter");

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                table: "user_counter",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "company_id",
                table: "user_branch",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("UPDATE user_branch ub SET company_id = b.company_id FROM branch b WHERE b.id = ub.branch_id;");
            migrationBuilder.Sql("UPDATE user_counter uc SET company_id = c.company_id FROM counter c WHERE c.id = uc.counter_id;");

            migrationBuilder.AlterColumn<Guid>(
                name: "company_id",
                table: "user_counter",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "company_id",
                table: "user_branch",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_counter_company_id_id",
                table: "counter",
                columns: new[] { "company_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_counter_company_id_counter_id",
                table: "user_counter",
                columns: new[] { "company_id", "counter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_branch_company_id_branch_id",
                table: "user_branch",
                columns: new[] { "company_id", "branch_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_user_branch_branch_company_id_branch_id",
                table: "user_branch",
                columns: new[] { "company_id", "branch_id" },
                principalTable: "branch",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_branch_company_company_id",
                table: "user_branch",
                column: "company_id",
                principalTable: "company",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_user_counter_company_company_id",
                table: "user_counter",
                column: "company_id",
                principalTable: "company",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_user_counter_counter_company_id_counter_id",
                table: "user_counter",
                columns: new[] { "company_id", "counter_id" },
                principalTable: "counter",
                principalColumns: new[] { "company_id", "id" },
                onDelete: ReferentialAction.Cascade);

            // Not modelled by EF (it cannot key the nullable AppUser.CompanyId): the user must belong to the same company
            // as the assignment. A Super Admin (company_id NULL) never satisfies a non-null FK, so cannot be assigned.
            // Deleting the user still cascades; changing the user's company while assigned is rejected.
            migrationBuilder.Sql("ALTER TABLE asp_net_users ADD CONSTRAINT ux_asp_net_users_id_company_id UNIQUE (id, company_id);");
            migrationBuilder.Sql("ALTER TABLE user_branch ADD CONSTRAINT fk_user_branch_user_company FOREIGN KEY (user_id, company_id) REFERENCES asp_net_users (id, company_id) ON DELETE CASCADE;");
            migrationBuilder.Sql("ALTER TABLE user_counter ADD CONSTRAINT fk_user_counter_user_company FOREIGN KEY (user_id, company_id) REFERENCES asp_net_users (id, company_id) ON DELETE CASCADE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE user_counter DROP CONSTRAINT fk_user_counter_user_company;");
            migrationBuilder.Sql("ALTER TABLE user_branch DROP CONSTRAINT fk_user_branch_user_company;");
            migrationBuilder.Sql("ALTER TABLE asp_net_users DROP CONSTRAINT ux_asp_net_users_id_company_id;");

            migrationBuilder.DropForeignKey(
                name: "fk_user_branch_branch_company_id_branch_id",
                table: "user_branch");

            migrationBuilder.DropForeignKey(
                name: "fk_user_branch_company_company_id",
                table: "user_branch");

            migrationBuilder.DropForeignKey(
                name: "fk_user_counter_company_company_id",
                table: "user_counter");

            migrationBuilder.DropForeignKey(
                name: "fk_user_counter_counter_company_id_counter_id",
                table: "user_counter");

            migrationBuilder.DropIndex(
                name: "ix_user_counter_company_id_counter_id",
                table: "user_counter");

            migrationBuilder.DropIndex(
                name: "ix_user_branch_company_id_branch_id",
                table: "user_branch");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_counter_company_id_id",
                table: "counter");

            migrationBuilder.DropColumn(
                name: "company_id",
                table: "user_counter");

            migrationBuilder.DropColumn(
                name: "company_id",
                table: "user_branch");

            migrationBuilder.AddForeignKey(
                name: "fk_user_branch_branch_branch_id",
                table: "user_branch",
                column: "branch_id",
                principalTable: "branch",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_user_counter_counter_counter_id",
                table: "user_counter",
                column: "counter_id",
                principalTable: "counter",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
