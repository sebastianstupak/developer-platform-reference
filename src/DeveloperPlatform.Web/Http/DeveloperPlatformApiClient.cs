using System.Net.Http.Json;
using System.Text.Json;
using DeveloperPlatform.Web.Http.Models;

namespace DeveloperPlatform.Web.Http;

public sealed class DeveloperPlatformApiClient
{
    private readonly HttpClient _http;

    public DeveloperPlatformApiClient(HttpClient http) => _http = http;

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ProjectDto>> GetProjectsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<IReadOnlyList<ProjectDto>>("/api/v1/projects", ct);
            return result ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<Guid?> CreateProjectAsync(
        string name, string? description, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "/api/v1/projects",
                new { name, description },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return body.GetProperty("projectId").GetGuid();
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> DeleteProjectAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"/api/v1/projects/{id}", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
