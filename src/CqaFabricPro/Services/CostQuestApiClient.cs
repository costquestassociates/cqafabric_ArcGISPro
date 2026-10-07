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
    // Match cqazapi-pytools defaults: address matching benefits from substantially
    // more parallelism than the other bulk endpoints.
    private const int DefaultWorkers = 4;
    private const int MatchWorkers = 16;
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

    public async Task<int> GetMaxRequestAsync(string api, string operation, int fallback, CancellationToken token = default)
    {
        var definitions = await GetJsonArrayAsync("accountcontrol/listapis", token);
        var definition = definitions.OfType<JsonObject>().FirstOrDefault(item =>
            item["api"]?.ToString().Equals(api, StringComparison.OrdinalIgnoreCase) == true &&
            item["operation"]?.ToString().Equals(operation, StringComparison.OrdinalIgnoreCase) == true &&
            item["method"]?.ToString().Equals("POST", StringComparison.OrdinalIgnoreCase) == true);
        return int.TryParse(definition?["maxrequest"]?.ToString(), out var maximum) && maximum > 0 ? maximum : fallback;
    }

    public Task<JsonArray> GetFieldsAsync(string vintage, string layer, CancellationToken token = default) =>
        GetJsonArrayAsync($"fabric/{Segment(vintage)}/fields/{Segment(layer)}", token);

    public Task<JsonNode?> GetIndividualDataAsync(string vintage, string layer, string uuid, CancellationToken token = default) =>
        GetJsonAsync($"fabric/{Segment(vintage)}/data/{Segment(layer)}?uuid={Uri.EscapeDataString(uuid)}", token);

    public Task<JsonNode?> GeocodeAsync(string address, CancellationToken token = default) =>
        GetJsonAsync($"geocode/autocomplete?text={Uri.EscapeDataString(address)}", token);

    public Task<JsonNode?> MatchAsync(string vintage, string address, CancellationToken token = default) =>
        GetJsonAsync($"fabricext/{Segment(vintage)}/match?text={Uri.EscapeDataString(address)}", token);

    public Task<JsonNode?> MatchAsync(string vintage, JsonNode records, CancellationToken token = default) =>
        PostJsonAsync($"fabricext/{Segment(vintage)}/match", records, token);

    public Task<IReadOnlyList<JsonNode?>> MatchBulkAsync(string vintage, IReadOnlyList<JsonObject> records,
        int batchSize, IProgress<int>? progress = null, CancellationToken token = default) =>
        PostBulkAsync($"fabricext/{Segment(vintage)}/match", records, batchSize, MatchWorkers, progress, token);

    public Task<JsonNode?> LocateAsync(string vintage, JsonNode records, double? nearDistanceMeters = null, CancellationToken token = default)
    {
        var suffix = nearDistanceMeters.HasValue ? $"?neardistancem={nearDistanceMeters.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : string.Empty;
        return PostJsonAsync($"fabricext/{Segment(vintage)}/locate{suffix}", records, token);
    }

    public Task<IReadOnlyList<JsonNode?>> LocateBulkAsync(string vintage, IReadOnlyList<JsonObject> records,
        int batchSize, IProgress<int>? progress = null, CancellationToken token = default) =>
        PostBulkAsync($"fabricext/{Segment(vintage)}/locate", records, batchSize, DefaultWorkers, progress, token);

    public Task<JsonNode?> TranslateAsync(string vintage, JsonNode records, CancellationToken token = default) =>
        PostJsonAsync($"fabricext/{Segment(vintage)}/translate", records, token);

    public async Task<JsonArray> RetrieveAsync(string vintage, string layer, IEnumerable<string> uuids,
        IEnumerable<string> fields, CancellationToken token = default)
    {
        var ids = uuids.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var requestedFields = fields.Where(value => !string.IsNullOrWhiteSpace(value) &&
                !value.Equals("uuid", StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0) return [];
        if (requestedFields.Length == 0) requestedFields = ["uuid"];

        var requests = (from idBatch in ids.Chunk(100)
                        from fieldGroup in requestedFields.Chunk(5)
                        select (Ids: idBatch, Fields: fieldGroup)).ToArray();
        var responses = await RunConcurrentlyAsync(requests, DefaultWorkers, async request =>
        {
            var payload = new JsonArray(request.Ids.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
            var fieldQuery = Uri.EscapeDataString(string.Join(',', request.Fields));
            return await PostJsonAsync($"fabric/{Segment(vintage)}/bulk/{Segment(layer)}?field={fieldQuery}", payload, token);
        }, token);

        var records = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var array in responses.OfType<JsonArray>())
        {
            foreach (var item in array.OfType<JsonObject>())
            {
                var uuid = item["uuid"]?.ToString();
                if (string.IsNullOrWhiteSpace(uuid)) continue;
                if (!records.TryGetValue(uuid, out var target)) records[uuid] = target = new JsonObject { ["uuid"] = uuid };
                foreach (var property in item) target[property.Key] = property.Value?.DeepClone();
            }
        }
        return new JsonArray(records.Values.OrderBy(record => record["uuid"]?.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(record => (JsonNode?)record).ToArray());
    }

    public async Task<IReadOnlyList<string>> CollectAsync(string vintage, JsonNode geoJson, CancellationToken token = default)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task CollectPageAsync(JsonNode body)
        {
            var response = await PostJsonAsync($"fabric/{Segment(vintage)}/collect2", body, token) as JsonObject
                ?? throw new CostQuestApiException(HttpStatusCode.OK, "The collect service returned an unexpected response.");
            if (response["data"] is JsonArray data)
                foreach (var item in data)
                    if (!string.IsNullOrWhiteSpace(item?.ToString())) ids.Add(item!.ToString());
            if (response["continuations"] is JsonArray continuations)
                foreach (var continuation in continuations.OfType<JsonObject>())
                    if (continuation["body"] is JsonNode continuationBody)
                        await CollectPageAsync(continuationBody);
        }

        await CollectPageAsync(geoJson);
        return ids.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

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

    private async Task<IReadOnlyList<JsonNode?>> PostBulkAsync(string path, IReadOnlyList<JsonObject> records,
        int batchSize, int workers, IProgress<int>? progress, CancellationToken token)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var batches = records.Chunk(batchSize).ToArray();
        var completed = 0;
        return await RunConcurrentlyAsync(batches, workers, async batch =>
        {
            var payload = new JsonArray(batch.Select(item => (JsonNode?)item.DeepClone()).ToArray());
            var response = await PostJsonAsync(path, payload, token);
            progress?.Report(Interlocked.Add(ref completed, batch.Length));
            return response;
        }, token);
    }

    private static async Task<TResult[]> RunConcurrentlyAsync<TInput, TResult>(IReadOnlyList<TInput> inputs,
        int workers, Func<TInput, Task<TResult>> action, CancellationToken token)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, workers));
        var tasks = inputs.Select(async (input, index) =>
        {
            await gate.WaitAsync(token);
            try { return (Index: index, Result: await action(input)); }
            finally { gate.Release(); }
        }).ToArray();
        var completed = await Task.WhenAll(tasks);
        return completed.OrderBy(item => item.Index).Select(item => item.Result).ToArray();
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
