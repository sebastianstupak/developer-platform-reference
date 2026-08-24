using Asp.Versioning;
using DeveloperPlatform.Api.Endpoints.ApiKeys;
using DeveloperPlatform.Api.Endpoints.Health;
using DeveloperPlatform.Infrastructure;
using DeveloperPlatform.Infrastructure.Context;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, config) =>
    {
        config
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .WriteTo.Console()
            .WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day);
    });

    builder.Services.AddOpenApi();
    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });
    builder.Services.AddProblemDetails();
    builder.Services.AddInfrastructure(builder.Configuration);

    var app = builder.Build();

    var versionSet = app.NewApiVersionSet()
        .HasApiVersion(new ApiVersion(1))
        .ReportApiVersions()
        .Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();
    app.UseMiddleware<ExecutionContextMiddleware>();

    app.MapHealth(versionSet);
    app.MapCreateApiKey(versionSet);

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
