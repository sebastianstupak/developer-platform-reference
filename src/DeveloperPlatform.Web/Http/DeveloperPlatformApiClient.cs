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

    // --- Access management (Slice 6) ---

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            return await _http.GetFromJsonAsync<IReadOnlyList<T>>(url, ct) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public Task<IReadOnlyList<PermissionDto>> GetPermissionsAsync(CancellationToken ct = default)
        => GetListAsync<PermissionDto>("/api/v1/permissions", ct);

    public Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default)
        => GetListAsync<RoleDto>("/api/v1/roles", ct);

    public Task<IReadOnlyList<MemberDto>> GetMembersAsync(CancellationToken ct = default)
        => GetListAsync<MemberDto>("/api/v1/members", ct);

    public Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync(CancellationToken ct = default)
        => GetListAsync<InvitationDto>("/api/v1/invitations", ct);

    public async Task<bool> InviteMemberAsync(string email, Guid roleId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "/api/v1/invitations",
                new { email, roleId, scopeType = "Tenant", scopeTargetId = (Guid?)null },
                ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RevokeInvitationAsync(Guid invitationId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync($"/api/v1/invitations/{invitationId}/revoke", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task<IReadOnlyList<ServiceAccountDto>> GetServiceAccountsAsync(CancellationToken ct = default)
        => GetListAsync<ServiceAccountDto>("/api/v1/service-accounts", ct);

    public async Task<Guid?> CreateServiceAccountAsync(
        string name, string? description, IEnumerable<string> permissionTokens, CancellationToken ct = default)
    {
        try
        {
            var grants = permissionTokens
                .Select(t => new { permission = ResourceActionFromToken(t), scopeType = "Tenant", scopeTargetId = (Guid?)null })
                .ToList();
            var response = await _http.PostAsJsonAsync(
                "/api/v1/service-accounts", new { name, description, grants }, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return body.GetProperty("serviceAccountId").GetGuid();
        }
        catch
        {
            return null;
        }
    }

    // The API's Permission enum name (e.g. "ProjectsRead") is what the create body expects;
    // the catalog exposes it as a "projects:read" token, so map token → PascalCase enum name.
    private static string ResourceActionFromToken(string token)
    {
        var parts = token.Split(':');
        static string Pascal(string s) => string.Concat(
            s.Split('-').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
        return parts.Length == 2 ? Pascal(parts[0]) + Pascal(parts[1]) : token;
    }

    public Task<IReadOnlyList<ApiKeyDto>> GetApiKeysAsync(Guid serviceAccountId, CancellationToken ct = default)
        => GetListAsync<ApiKeyDto>($"/api/v1/service-accounts/{serviceAccountId}/keys", ct);

    public async Task<IssuedKeyDto?> IssueApiKeyAsync(
        Guid serviceAccountId, string name, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"/api/v1/service-accounts/{serviceAccountId}/keys",
                new { name, expiresAt = (DateTime?)null }, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            return await response.Content.ReadFromJsonAsync<IssuedKeyDto>(ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> RevokeApiKeyAsync(
        Guid serviceAccountId, Guid credentialId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync(
                $"/api/v1/service-accounts/{serviceAccountId}/keys/{credentialId}/revoke", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // --- Environments & Secrets (Slice D) ---

    public Task<IReadOnlyList<EnvironmentDto>> GetEnvironmentsAsync(Guid projectId, CancellationToken ct = default)
        => GetListAsync<EnvironmentDto>($"/api/v1/projects/{projectId}/environments", ct);

    public async Task<Guid> CreateEnvironmentAsync(
        Guid projectId, string name, string type, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/environments", new { name, type }, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("environmentId").GetGuid();
    }

    public async Task RenameEnvironmentAsync(
        Guid projectId, Guid environmentId, string name, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"/api/v1/projects/{projectId}/environments/{environmentId}", new { name }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteEnvironmentAsync(
        Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync(
            $"/api/v1/projects/{projectId}/environments/{environmentId}", ct);
        response.EnsureSuccessStatusCode();
    }

    public Task<IReadOnlyList<SecretDto>> GetSecretsAsync(
        Guid projectId, Guid environmentId, CancellationToken ct = default)
        => GetListAsync<SecretDto>($"/api/v1/projects/{projectId}/environments/{environmentId}/secrets", ct);

    public async Task SetSecretAsync(
        Guid projectId, Guid environmentId, string name, string value, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(
            $"/api/v1/projects/{projectId}/environments/{environmentId}/secrets/{Uri.EscapeDataString(name)}",
            new { value },
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> RevealSecretAsync(
        Guid projectId, Guid environmentId, string name, CancellationToken ct = default)
    {
        var response = await _http.PostAsync(
            $"/api/v1/projects/{projectId}/environments/{environmentId}/secrets/{Uri.EscapeDataString(name)}/reveal",
            null,
            ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RevealDto>(ct);
        return body!.Value;
    }

    public async Task DeleteSecretAsync(
        Guid projectId, Guid environmentId, string name, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync(
            $"/api/v1/projects/{projectId}/environments/{environmentId}/secrets/{Uri.EscapeDataString(name)}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<int> RotateKeyAsync(CancellationToken ct = default)
    {
        var response = await _http.PostAsync("/api/v1/secrets/rotate-key", null, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RotateKeyDto>(ct);
        return body!.SecretsReEncrypted;
    }

    // --- Audit log (Slice B) ---

    public async Task<AuditPageDto> GetAuditEventsAsync(
        AuditFilterDto filter, int page, int pageSize, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<AuditPageDto>(
                $"/api/v1/audit{BuildAuditQuery(filter, page, pageSize)}", ct);
            return result ?? new AuditPageDto([], 0, page, pageSize);
        }
        catch
        {
            return new AuditPageDto([], 0, page, pageSize);
        }
    }

    private static string BuildAuditQuery(AuditFilterDto filter, int page, int pageSize)
    {
        var parts = new List<string>();

        if (filter.From is not null)
        {
            parts.Add($"from={Uri.EscapeDataString(filter.From.Value.ToString("o"))}");
        }

        if (filter.To is not null)
        {
            parts.Add($"to={Uri.EscapeDataString(filter.To.Value.ToString("o"))}");
        }

        foreach (var principalId in filter.PrincipalIds)
        {
            parts.Add($"principalId={Uri.EscapeDataString(principalId.ToString())}");
        }

        foreach (var commandType in filter.CommandTypes)
        {
            parts.Add($"commandType={Uri.EscapeDataString(commandType)}");
        }

        foreach (var status in filter.Statuses)
        {
            parts.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (filter.CrossTenantOnly is not null)
        {
            parts.Add($"crossTenantOnly={Uri.EscapeDataString(filter.CrossTenantOnly.Value.ToString())}");
        }

        if (filter.ProjectId is not null)
        {
            parts.Add($"projectId={Uri.EscapeDataString(filter.ProjectId.Value.ToString())}");
        }

        parts.Add($"page={page}");
        parts.Add($"pageSize={pageSize}");

        return "?" + string.Join("&", parts);
    }

    public async Task<AuditDetailDto?> GetAuditEventDetailAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<AuditDetailDto>($"/api/v1/audit/{id}", ct);
        }
        catch
        {
            return null;
        }
    }

    public Task<IReadOnlyList<string>> GetAuditCommandTypesAsync(CancellationToken ct = default)
        => GetListAsync<string>("/api/v1/audit/command-types", ct);
}
