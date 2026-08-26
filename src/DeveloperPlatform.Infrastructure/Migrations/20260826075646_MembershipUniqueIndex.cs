using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeveloperPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MembershipUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId_UserId",
                table: "Memberships",
                columns: new[] { "TenantId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Memberships_TenantId_UserId",
                table: "Memberships");
        }
    }
}
