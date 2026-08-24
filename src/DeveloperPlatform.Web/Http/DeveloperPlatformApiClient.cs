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
}
