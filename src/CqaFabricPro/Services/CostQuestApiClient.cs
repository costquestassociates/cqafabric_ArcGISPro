using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CqaFabricPro.Services;

internal sealed class CostQuestApiClient : IDisposable
{
    private static readonly Uri BaseUri = new("https://api.costquest.com/");
    private readonly HttpClient _http;

    public CostQuestApiClient(string apiKey, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("An API key is required.", nameof(apiKey));
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _http.BaseAddress = BaseUri;
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Add("apikey", apiKey.Trim());
        _http.DefaultRequestHeaders.CacheControl = new() { NoCache = true };
    }

    public async Task<bool> VerifyApiKeyAsync(CancellationToken token = default)
    {
        using var response = await _http.GetAsync("accountcontrol/listkeys", token);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<string>> GetVintagesAsync(CancellationToken token = default)
    {
        var json = await GetJsonArrayAsync("fabric/vintages", token);
        return json.Select(x => x?["vintage"]?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>().OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<string>> GetLayersAsync(CancellationToken token = default)
    {
        var json = await GetJsonArrayAsync("fabric/layers", token);
        return json.Select(x => x?["layername"]?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>().ToArray();
    }

    public Task<JsonArray> GetFieldsAsync(string vintage, string layer, CancellationToken token = default) =>
        GetJsonArrayAsync($"fabric/{Segment(vintage)}/fields/{Segment(layer)}", token);

    public Task<JsonNode?> GetIndividualDataAsync(string vintage, string layer, string uuid, CancellationToken token = default) =>
        GetJsonAsync($"fabric/{Segment(vintage)}/data/{Segment(layer)}?uuid={Uri.EscapeDataString(uuid)}", token);

    public Task<JsonNode?> GeocodeAsync(string address, CancellationToken token = default) =>
        GetJsonAsync($"geocode/autocomplete?text={Uri.EscapeDataString(address)}", token);

    public Task<JsonNode?> MatchAsync(string vintage, string address, CancellationToken token = default) =>
        GetJsonAsync($"fabricext/{Segment(vintage)}/match?text={Uri.EscapeDataString(address)}", token);

    public Task<JsonNode?> LocateAsync(string vintage, JsonNode records, double? nearDistanceMeters = null, CancellationToken token = default)
    {
        var suffix = nearDistanceMeters.HasValue ? $"?neardistancem={nearDistanceMeters.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : string.Empty;
        return PostJsonAsync($"fabricext/{Segment(vintage)}/locate{suffix}", records, token);
    }

    public Task<JsonNode?> TranslateAsync(string vintage, JsonNode records, CancellationToken token = default) =>
        PostJsonAsync($"fabricext/{Segment(vintage)}/translate", records, token);

    private async Task<JsonArray> GetJsonArrayAsync(string path, CancellationToken token)
    {
        var node = await GetJsonAsync(path, token);
        return node as JsonArray ?? throw new CostQuestApiException(HttpStatusCode.OK, "The service returned an unexpected response.");
    }

    private async Task<JsonNode?> GetJsonAsync(string path, CancellationToken token)
    {
        using var response = await _http.GetAsync(path, token);
        return await ReadResponseAsync(response, token);
    }

    private async Task<JsonNode?> PostJsonAsync(string path, JsonNode payload, CancellationToken token)
    {
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path, content, token);
        return await ReadResponseAsync(response, token);
    }

    private static async Task<JsonNode?> ReadResponseAsync(HttpResponseMessage response, CancellationToken token)
    {
        var text = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new CostQuestApiException(response.StatusCode, string.IsNullOrWhiteSpace(text) ? response.ReasonPhrase ?? "Request failed." : text);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static string Segment(string value) => Uri.EscapeDataString(value ?? throw new ArgumentNullException(nameof(value)));

    public void Dispose() => _http.Dispose();
}

internal sealed class CostQuestApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
