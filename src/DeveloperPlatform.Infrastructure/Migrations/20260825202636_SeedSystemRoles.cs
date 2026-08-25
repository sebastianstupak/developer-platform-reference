using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DeveloperPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedSystemRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "CreatedAt", "IsSystem", "Name" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Owner" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Admin" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Developer" },
                    { new Guid("44444444-4444-4444-4444-444444444444"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Viewer" }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Permission", "RoleId" },
                values: new object[,]
                {
                    { "api-keys:manage", new Guid("11111111-1111-1111-1111-111111111111") },
                    { "audit:read", new Guid("11111111-1111-1111-1111-111111111111") },
                    { "members:manage", new Guid("11111111-1111-1111-1111-111111111111") },
                    { "projects:read", new Guid("11111111-1111-1111-1111-111111111111") },
                    { "projects:write", new Guid("11111111-1111-1111-1111-111111111111") },
                    { "roles:manage", new Guid("11111111-1111-1111-1111-111111111111") },
                    { "secrets:read", new Guid("11111111-1111-1111-1111-111111111111") },
                    { "secrets:write", new Guid("11111111-1111-1111-1111-111111111111") },
                    { "service-accounts:manage", new Guid("11111111-1111-1111-1111-111111111111") },
                    { "api-keys:manage", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "audit:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "members:manage", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "projects:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "projects:write", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "secrets:read", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "secrets:write", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "service-accounts:manage", new Guid("22222222-2222-2222-2222-222222222222") },
                    { "projects:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "projects:write", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "secrets:read", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "secrets:write", new Guid("33333333-3333-3333-3333-333333333333") },
                    { "audit:read", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "projects:read", new Guid("44444444-4444-4444-4444-444444444444") },
                    { "secrets:read", new Guid("44444444-4444-4444-4444-444444444444") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "api-keys:manage", new Guid("11111111-1111-1111-1111-111111111111") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "audit:read", new Guid("11111111-1111-1111-1111-111111111111") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "members:manage", new Guid("11111111-1111-1111-1111-111111111111") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "projects:read", new Guid("11111111-1111-1111-1111-111111111111") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "projects:write", new Guid("11111111-1111-1111-1111-111111111111") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "roles:manage", new Guid("11111111-1111-1111-1111-111111111111") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "secrets:read", new Guid("11111111-1111-1111-1111-111111111111") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "secrets:write", new Guid("11111111-1111-1111-1111-111111111111") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "service-accounts:manage", new Guid("11111111-1111-1111-1111-111111111111") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "api-keys:manage", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "audit:read", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "members:manage", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "projects:read", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "projects:write", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "secrets:read", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "secrets:write", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "service-accounts:manage", new Guid("22222222-2222-2222-2222-222222222222") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "projects:read", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "projects:write", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "secrets:read", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "secrets:write", new Guid("33333333-3333-3333-3333-333333333333") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "audit:read", new Guid("44444444-4444-4444-4444-444444444444") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "projects:read", new Guid("44444444-4444-4444-4444-444444444444") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "secrets:read", new Guid("44444444-4444-4444-4444-444444444444") });

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"));

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"));

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"));
        }
    }
}
