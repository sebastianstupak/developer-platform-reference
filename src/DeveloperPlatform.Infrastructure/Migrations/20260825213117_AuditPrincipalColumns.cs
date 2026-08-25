using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditPrincipalColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ApiKeyId",
                table: "AuditOutboxEntries",
                newName: "PrincipalId");

            migrationBuilder.RenameColumn(
                name: "ApiKeyId",
                table: "AuditEvents",
                newName: "PrincipalId");

            migrationBuilder.AddColumn<string>(
                name: "PrincipalType",
                table: "AuditOutboxEntries",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PrincipalType",
                table: "AuditEvents",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrincipalType",
                table: "AuditOutboxEntries");

            migrationBuilder.DropColumn(
                name: "PrincipalType",
                table: "AuditEvents");

            migrationBuilder.RenameColumn(
                name: "PrincipalId",
                table: "AuditOutboxEntries",
                newName: "ApiKeyId");

            migrationBuilder.RenameColumn(
                name: "PrincipalId",
                table: "AuditEvents",
                newName: "ApiKeyId");
        }
    }
}
