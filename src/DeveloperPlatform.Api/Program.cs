using Asp.Versioning;
using DeveloperPlatform.Api.Endpoints.ApiKeys;
using DeveloperPlatform.Api.Endpoints.Projects;
using DeveloperPlatform.Api.Endpoints.Permissions;
using DeveloperPlatform.Api.Endpoints.Health;
using DeveloperPlatform.Api.OpenApi;
using DeveloperPlatform.Infrastructure;
using DeveloperPlatform.Infrastructure.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
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

    builder.Services.AddOpenApi("v1", options =>
    {
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        options.AddDocumentTransformer((doc, ctx, ct) =>
        {
            doc.Info.Title = "Developer Platform API";
            doc.Info.Version = "v1";
            doc.Info.Description =
                "Multi-tenant developer platform — API key management, projects, environments, secrets, and audit.";
            doc.Info.Contact = new Microsoft.OpenApi.OpenApiContact
            {
                Name = "Developer Platform",
                Url = new Uri("https://github.com/sebastianstupak/developer-platform-reference")
            };
            return Task.CompletedTask;
        });
    });
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
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = builder.Configuration["Keycloak:Authority"];
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = false,
                NameClaimType = "preferred_username",
                RoleClaimType = "realm_access.roles"
            };
        });
    builder.Services.AddAuthorization();
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<DeveloperPlatform.Infrastructure.Authorization.ForbiddenExceptionHandler>();
    builder.Services.AddInfrastructure(builder.Configuration);

    var app = builder.Build();

    var versionSet = app.NewApiVersionSet()
        .HasApiVersion(new ApiVersion(1))
        .ReportApiVersions()
        .Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.MapOpenApi("/openapi/{documentName}.json");
    app.MapScalarApiReference("/docs", options =>
    {
        options
            .WithTitle("Developer Platform API")
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
            .WithCustomCss(ScalarCustomCss.MudBlazorTheme);
    });

    app.UseSerilogRequestLogging();
    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseWhen(
        ctx => ctx.Request.Path.StartsWithSegments("/api"),
        branch => branch.UseMiddleware<ExecutionContextMiddleware>()
    );

    app.MapHealth(versionSet);
    app.MapCreateApiKey(versionSet);
    app.MapProjects(versionSet);
    app.MapPermissions(versionSet);

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
