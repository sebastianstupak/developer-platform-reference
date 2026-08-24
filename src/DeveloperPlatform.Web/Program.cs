using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using DeveloperPlatform.Web.Http;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "dev-platform-auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
})
.AddOpenIdConnect(options =>
{
    options.Authority = config["Keycloak:Authority"];
    options.ClientId = "web-client";
    options.ClientSecret = config["Keycloak:ClientSecret"];
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.TokenValidationParameters = new TokenValidationParameters
    {
        NameClaimType = "preferred_username"
    };
});

builder.Services.AddAuthorization();

var redisConnectionString = config.GetConnectionString("Redis")!;
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);
builder.Services.AddSession(o => o.Cookie.IsEssential = true);

builder.Services.AddDataProtection()
    .PersistKeysToStackExchangeRedis(
        ConnectionMultiplexer.Connect(redisConnectionString),
        "DevPlatform-DataProtection-Keys");

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ApiTokenHandler>();
builder.Services.AddHttpClient<DeveloperPlatformApiClient>(c =>
    c.BaseAddress = new Uri(config["Api:BaseUrl"]!))
    .AddHttpMessageHandler<ApiTokenHandler>();

builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();
app.UseAntiforgery();

app.MapGet("/login", () =>
    Results.Challenge(
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            RedirectUri = "/"
        },
        [OpenIdConnectDefaults.AuthenticationScheme]))
    .AllowAnonymous();

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
}).RequireAuthorization();

app.MapRazorComponents<DeveloperPlatform.Web.Components.App>();

app.Run();
