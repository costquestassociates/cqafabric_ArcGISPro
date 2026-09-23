using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CqaFabricPro.Services;

internal sealed class TileProxyService : IDisposable
{
    private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CqaFabricPro", "tile-proxy.log");
    private readonly TcpListener _listener;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _vintage;
    private readonly string _layer;

    private TileProxyService(string apiKey, string vintage, string layer)
    {
        _vintage = vintage;
        _layer = layer;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.Add("apikey", apiKey);
        _http.DefaultRequestHeaders.CacheControl = new() { NoCache = true };
        _ = AcceptLoopAsync(_stop.Token);
    }

    public int Port { get; }
    public string StyleUrl => $"http://127.0.0.1:{Port}/style.json";
    public static TileProxyService Start(string apiKey, string vintage, string layer) => new(apiKey, vintage, layer);

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                _ = HandleClientAsync(client, token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var requestLine = await reader.ReadLineAsync(token);
                string? line;
                do { line = await reader.ReadLineAsync(token); } while (!string.IsNullOrEmpty(line));

                var parts = requestLine?.Split(' ');
                if (parts is null || parts.Length < 2 || parts[0] != "GET")
                {
                    await WriteAsync(stream, 400, "text/plain", Encoding.UTF8.GetBytes("Bad request"), token);
                    return;
                }

                var path = new Uri("http://localhost" + parts[1]).AbsolutePath;
                if (path.Equals("/style.json", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteAsync(stream, 200, "application/json", Encoding.UTF8.GetBytes(CreateStyleJson()), token);
                    return;
                }

                var tile = path.Trim('/').Split('/');
                if (tile.Length == 4 && tile[0] == "tiles" && tile.Skip(1).All(value => int.TryParse(value, out _)))
                {
                    var upstream = $"https://api.costquest.com/fabric/{Uri.EscapeDataString(_vintage)}/tiles/{Uri.EscapeDataString(_layer)}/{tile[1]}/{tile[2]}/{tile[3]}";
                    using var response = await _http.GetAsync(upstream, token);
                    var body = await response.Content.ReadAsByteArrayAsync(token);
                    var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/vnd.mapbox-vector-tile";
                    Log($"{DateTimeOffset.Now:o} {_vintage}/{_layer} z={tile[1]} x={tile[2]} y={tile[3]} status={(int)response.StatusCode} bytes={body.Length} type={contentType}");
                    await WriteAsync(stream, (int)response.StatusCode, contentType, body, token);
                    return;
                }

                await WriteAsync(stream, 404, "text/plain", Encoding.UTF8.GetBytes("Not found"), token);
            }
            catch when (token.IsCancellationRequested) { }
            catch (Exception ex) { Log($"{DateTimeOffset.Now:o} {_vintage}/{_layer} proxy error: {ex}"); }
        }
    }

    private string CreateStyleJson()
    {
        var isPoint = _layer is "locations" or "retirements";
        var styleLayers = new List<Dictionary<string, object>>();
        if (isPoint)
        {
            styleLayers.Add(new()
            {
                ["id"] = _layer,
                ["type"] = "circle",
                ["source"] = "composite",
                ["source-layer"] = _layer,
                ["minzoom"] = 0,
                ["maxzoom"] = 24,
                ["layout"] = new Dictionary<string, object>(),
                ["paint"] = new Dictionary<string, object>
                {
                    ["circle-radius"] = 5,
                    ["circle-stroke-color"] = "#000000",
                    ["circle-color"] = _layer == "retirements" ? "#ffa500" : "#00ff00",
                    ["circle-opacity"] = 0.8,
                    ["circle-stroke-width"] = 1
                }
            });
        }
        else
        {
            var fillColor = _layer == "parcels" ? "#ff0000" : "#f5820b";
            var lineColor = _layer == "parcels" ? "#ff0000" : "#7f3f00";
            styleLayers.Add(new()
            {
                ["id"] = _layer + "-fill",
                ["type"] = "fill",
                ["source"] = "composite",
                ["source-layer"] = _layer,
                ["minzoom"] = 12,
                ["maxzoom"] = 24,
                ["layout"] = new Dictionary<string, object>(),
                ["paint"] = new Dictionary<string, object>
                {
                    ["fill-color"] = fillColor,
                    ["fill-opacity"] = _layer == "parcels" ? 0.15 : 0.5
                }
            });
            styleLayers.Add(new()
            {
                ["id"] = _layer + "-outline",
                ["type"] = "line",
                ["source"] = "composite",
                ["source-layer"] = _layer,
                ["minzoom"] = 12,
                ["maxzoom"] = 24,
                ["layout"] = new Dictionary<string, object>(),
                ["paint"] = new Dictionary<string, object>
                {
                    ["line-color"] = lineColor,
                    ["line-opacity"] = 1.0,
                    ["line-width"] = 1.5
                }
            });
        }

        var style = new
        {
            version = 8,
            name = $"CQA Fabric {_vintage} - {_layer}",
            sources = new Dictionary<string, object>
            {
                ["composite"] = new
                {
                    type = "vector",
                    tiles = new[] { $"http://127.0.0.1:{Port}/tiles/{{z}}/{{x}}/{{y}}" },
                    minzoom = 0,
                    maxzoom = 14
                }
            },
            layers = styleLayers
        };
        return JsonSerializer.Serialize(style);
    }
    private static async Task WriteAsync(NetworkStream stream, int status, string contentType, byte[] body, CancellationToken token)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", _ => "Error" };
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, token);
        await stream.WriteAsync(body, token);
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            lock (typeof(TileProxyService)) File.AppendAllText(LogPath, message + Environment.NewLine);
        }
        catch { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _http.Dispose();
        _stop.Dispose();
    }
}
