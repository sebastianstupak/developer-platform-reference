using DeveloperPlatform.Application.ApiKeys.CreateApiKey;
using DeveloperPlatform.Application.Audit;
using DeveloperPlatform.Application.Commands;
using DeveloperPlatform.Application.Crypto;
using DeveloperPlatform.Application.Queries;
using DeveloperPlatform.Application.Tenancy;
using DeveloperPlatform.Infrastructure.ApiKeys;
using DeveloperPlatform.Infrastructure.Audit;
using DeveloperPlatform.Infrastructure.Context;
using DeveloperPlatform.Infrastructure.Crypto;
using DeveloperPlatform.Infrastructure.Dispatching;
using DeveloperPlatform.Infrastructure.Messaging;
using DeveloperPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperPlatform.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var tenancyMode = configuration.GetValue<TenancyMode>("Tenancy:Mode");
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");
        var masterKeyHex = configuration["Crypto:MasterKey"]
            ?? throw new InvalidOperationException("Crypto:MasterKey is missing.");
        var masterKey = Convert.FromHexString(masterKeyHex);
        var rabbitHost = configuration["RabbitMQ:Host"] ?? "localhost";

        services.AddSingleton(typeof(TenancyMode), tenancyMode);

        services.AddDbContext<ApplicationDbContext>(opts =>
            opts.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

        services.AddScoped<HttpExecutionContext>();
        services.AddScoped<DeveloperPlatform.Application.Context.IExecutionContext>(
            sp => sp.GetRequiredService<HttpExecutionContext>());

        services.AddScoped<IAuditOutboxRepository, AuditOutboxRepository>();
        services.AddScoped<ITenantCryptoService>(sp =>
            new TenantCryptoService(sp.GetRequiredService<ApplicationDbContext>(), masterKey));

        services.AddScoped<SensitiveDataScrubber>();
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();

        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<ICommandHandler<CreateApiKeyCommand, CreateApiKeyResult>, CreateApiKeyCommandHandler>();

        // RabbitMQ publisher as singleton — InitializeAsync called synchronously at startup
        var publisher = new RabbitMqPublisher();
        publisher.InitializeAsync(rabbitHost).GetAwaiter().GetResult();
        services.AddSingleton(publisher);

        services.AddHostedService<OutboxRelayWorker>();
        services.AddHostedService(sp =>
            new AuditConsumer(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditConsumer>>(),
                rabbitHost));

        return services;
    }
}
