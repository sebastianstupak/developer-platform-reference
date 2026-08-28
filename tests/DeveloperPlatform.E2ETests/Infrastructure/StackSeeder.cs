using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace DeveloperPlatform.E2ETests.Infrastructure;

public sealed class StackSeeder(string keycloakBaseUrl, string apiBaseUrl)
{
    private static readonly (string Type, string Name)[] Envs =
        [("Production", "production"), ("Staging", "staging"), ("Development", "development")];

    public async Task<string> GetAccessTokenAsync()
    {
        using var http = new HttpClient();
        var res = await http.PostAsync(
            $"{keycloakBaseUrl}/realms/developer-platform/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "cli-client",
                ["username"] = "dev@example.com",
                ["password"] = "password",
                ["scope"] = "openid",
            }));
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.access_token;
    }

    public async Task SeedAsync()
    {
        var token = await GetAccessTokenAsync();
        using var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var existing = await http.GetFromJsonAsync<List<ProjectDto>>("/api/v1/projects") ?? [];
        var byName = existing.ToDictionary(p => p.Name, p => p.Id);

        foreach (var name in new[] { "payments-api", "billing-service" })
        {
            if (!byName.TryGetValue(name, out var projectId))
            {
                var created = await http.PostAsJsonAsync("/api/v1/projects",
                    new { name, description = $"{name} (seeded)" });
                created.EnsureSuccessStatusCode();
                projectId = (await created.Content.ReadFromJsonAsync<ProjectCreatedDto>())!.ProjectId;
            }

            foreach (var (type, envName) in Envs)
            {
                var env = await http.PostAsJsonAsync(
                    $"/api/v1/projects/{projectId}/environments",
                    new { name = envName, type });
                if (!env.IsSuccessStatusCode)
                {
                    continue; // already exists
                }

                var envId = (await env.Content.ReadFromJsonAsync<EnvironmentCreatedDto>())!.EnvironmentId;
                await http.PutAsJsonAsync(
                    $"/api/v1/projects/{projectId}/environments/{envId}/secrets/DATABASE_URL",
                    new { value = $"mysql://{envName}.db/{name}" });
            }
        }
    }

    private sealed record TokenResponse(string access_token);
    private sealed record ProjectDto(Guid Id, string Name);
    private sealed record ProjectCreatedDto(Guid ProjectId);
    private sealed record EnvironmentCreatedDto(Guid EnvironmentId);
}
