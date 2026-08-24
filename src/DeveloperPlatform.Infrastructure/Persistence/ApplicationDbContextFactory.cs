using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DeveloperPlatform.Infrastructure.Persistence;

internal sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost;Port=3306;Database=developer_platform;User=app;Password=app;";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
            .Options;

        return new ApplicationDbContext(options, new HttpExecutionContext(), TenancyMode.SharedTables);
    }
}
