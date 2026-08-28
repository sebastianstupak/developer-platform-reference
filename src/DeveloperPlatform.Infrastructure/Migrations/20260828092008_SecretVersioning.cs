using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SecretVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentVersion",
                table: "Secrets",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "SecretVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SecretId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    EncryptedValue = table.Column<byte[]>(type: "longblob", nullable: false),
                    KeyId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RolledBackFrom = table.Column<int>(type: "int", nullable: true),
                    CreatedByPrincipalId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedByPrincipalType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    TenantId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecretVersions_Secrets_SecretId",
                        column: x => x.SecretId,
                        principalTable: "Secrets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SecretVersions_SecretId_VersionNumber",
                table: "SecretVersions",
                columns: new[] { "SecretId", "VersionNumber" },
                unique: true);

            migrationBuilder.Sql(@"
INSERT INTO SecretVersions
  (Id, TenantId, SecretId, VersionNumber, EncryptedValue, KeyId, CreatedAt,
   CreatedByPrincipalId, CreatedByPrincipalType, CreatedByUserId, RolledBackFrom)
SELECT UUID(), s.TenantId, s.Id, 1, s.EncryptedValue, s.KeyId, s.UpdatedAt,
       NULL, NULL, NULL, NULL
FROM Secrets s;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecretVersions");

            migrationBuilder.DropColumn(
                name: "CurrentVersion",
                table: "Secrets");
        }
    }
}
