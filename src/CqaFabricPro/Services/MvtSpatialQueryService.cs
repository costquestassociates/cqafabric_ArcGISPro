using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using ArcGIS.Core.Geometry;

namespace CqaFabricPro.Services;

internal sealed record MvtSpatialFeature(string Uuid, IReadOnlyList<IReadOnlyList<MapPoint>> Rings);

internal static class MvtSpatialQueryService
{
    private const int Zoom = 14;

    public static async Task<IReadOnlyList<MvtSpatialFeature>> FindWithinAsync(IdentifyContext context, Polygon area,
        CancellationToken token = default)
    {
        var areaRings = area.Parts.Select(part => CloseRing(part
            .Select(segment => ToWorld(segment.StartPoint.X, segment.StartPoint.Y)).ToArray())).ToArray();
        var allPoints = areaRings.SelectMany(ring => ring).ToArray();
        if (allPoints.Length == 0) return [];
        var tileCount = 1 << Zoom;
        var minX = Math.Clamp((int)Math.Floor(allPoints.Min(point => point.x)), 0, tileCount - 1);
        var maxX = Math.Clamp((int)Math.Floor(allPoints.Max(point => point.x)), 0, tileCount - 1);
        var minY = Math.Clamp((int)Math.Floor(allPoints.Min(point => point.y)), 0, tileCount - 1);
        var maxY = Math.Clamp((int)Math.Floor(allPoints.Max(point => point.y)), 0, tileCount - 1);
        var tileTotal = (long)(maxX - minX + 1) * (maxY - minY + 1);
        if (tileTotal > 20000)
            throw new InvalidOperationException($"The Define Layer covers {tileTotal:N0} vector tiles. Select a smaller area (maximum 20,000 tiles)." );

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("apikey", context.ApiKey);
        var hits = new ConcurrentDictionary<string, MvtSpatialFeature>(StringComparer.OrdinalIgnoreCase);
        using var throttle = new SemaphoreSlim(8);
        var tasks = new List<Task>();
        for (var tileX = minX; tileX <= maxX; tileX++)
        for (var tileY = minY; tileY <= maxY; tileY++)
        {
            var x = tileX;
            var y = tileY;
            tasks.Add(Task.Run(async () =>
            {
                await throttle.WaitAsync(token);
                try
                {
                    var url = $"https://api.costquest.com/fabric/{Uri.EscapeDataString(context.Vintage)}/tiles/{Uri.EscapeDataString(context.Layer)}/{Zoom}/{x}/{y}";
                    byte[] bytes;
                    try { bytes = await http.GetByteArrayAsync(url, token); }
                    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return; }
                    foreach (var feature in ParseTile(bytes, context.Layer, x, y, areaRings)) hits.TryAdd(feature.Uuid, feature);
                }
                finally { throttle.Release(); }
            }, token));
        }
        await Task.WhenAll(tasks);
        return hits.Values.OrderBy(feature => feature.Uuid, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<MvtSpatialFeature> ParseTile(byte[] bytes, string layerName, int tileX, int tileY,
        IReadOnlyList<IReadOnlyList<(double x, double y)>> areaRings)
    {
        var reader = new ProtoReader(bytes);
        while (reader.TryField(out var field, out var wire))
        {
            if (field == 3 && wire == 2)
            {
                var result = ParseLayer(reader.ReadBytes(), layerName, tileX, tileY, areaRings);
                if (result is not null) return result;
            }
            else reader.Skip(wire);
        }
        return [];
    }

    private static IReadOnlyList<MvtSpatialFeature>? ParseLayer(byte[] bytes, string expectedName, int tileX, int tileY,
        IReadOnlyList<IReadOnlyList<(double x, double y)>> areaWorldRings)
    {
        var reader = new ProtoReader(bytes); string name = ""; uint extent = 4096;
        var features = new List<byte[]>(); var keys = new List<string>(); var values = new List<string>();
        while (reader.TryField(out var field, out var wire))
        {
            if (field == 1) name = reader.ReadString(); else if (field == 2) features.Add(reader.ReadBytes());
            else if (field == 3) keys.Add(reader.ReadString()); else if (field == 4) values.Add(ReadValue(reader.ReadBytes()));
            else if (field == 5) extent = (uint)reader.ReadVarint(); else reader.Skip(wire);
        }
        if (!name.Equals(expectedName, StringComparison.OrdinalIgnoreCase)) return null;
        var areaRings = areaWorldRings.Select(ring => (IReadOnlyList<(double x, double y)>)ring
            .Select(point => ((point.x - tileX) * extent, (point.y - tileY) * extent)).ToArray()).ToArray();
        var hits = new List<MvtSpatialFeature>();
        foreach (var featureBytes in features)
        {
            var featureReader = new ProtoReader(featureBytes); byte[] tags = []; byte[] geometry = []; var type = 0;
            while (featureReader.TryField(out var field, out var wire))
            {
                if (field == 2) tags = featureReader.ReadBytes(); else if (field == 3) type = (int)featureReader.ReadVarint();
                else if (field == 4) geometry = featureReader.ReadBytes(); else featureReader.Skip(wire);
            }
            if (type != 3) continue;
            var rings = DecodeRings(geometry);
            if (!Intersects(rings, areaRings)) continue;
            var uuid = ReadUuid(tags, keys, values);
            if (string.IsNullOrWhiteSpace(uuid)) continue;
            var mapRings = rings.Select(ring => (IReadOnlyList<MapPoint>)ring.Select(point =>
                ToMapPoint(tileX + point.x / extent, tileY + point.y / extent)).ToArray()).ToArray();
            hits.Add(new MvtSpatialFeature(uuid, mapRings));
        }
        return hits;
    }

    private static IReadOnlyList<IReadOnlyList<(double x, double y)>> DecodeRings(byte[] geometry)
    {
        var reader = new ProtoReader(geometry); var x = 0; var y = 0;
        var rings = new List<IReadOnlyList<(double x, double y)>>(); List<(double x, double y)>? current = null;
        while (!reader.End)
        {
            var command = (uint)reader.ReadVarint(); var id = command & 7; var count = (int)(command >> 3);
            if (id is 1 or 2)
            {
                for (var index = 0; index < count; index++)
                {
                    x += Zig(reader.ReadVarint()); y += Zig(reader.ReadVarint());
                    if (id == 1) { current = []; rings.Add(current); }
                    current?.Add((x, y));
                }
            }
            else if (id == 7 && current is { Count: > 0 }) current.Add(current[0]);
            else break;
        }
        return rings;
    }

    private static bool Intersects(IReadOnlyList<IReadOnlyList<(double x, double y)>> first,
        IReadOnlyList<IReadOnlyList<(double x, double y)>> second)
    {
        if (first.SelectMany(ring => ring).Any(point => Inside(point, second))) return true;
        if (second.SelectMany(ring => ring).Any(point => Inside(point, first))) return true;
        foreach (var aRing in first)
        foreach (var bRing in second)
        for (var a = 1; a < aRing.Count; a++)
        for (var b = 1; b < bRing.Count; b++)
            if (SegmentsIntersect(aRing[a - 1], aRing[a], bRing[b - 1], bRing[b])) return true;
        return false;
    }

    private static bool Inside((double x, double y) point, IReadOnlyList<IReadOnlyList<(double x, double y)>> rings)
    {
        var inside = false;
        foreach (var ring in rings)
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i]; var b = ring[j];
                if ((a.y > point.y) != (b.y > point.y) && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
        return inside;
    }

    private static bool SegmentsIntersect((double x, double y) a, (double x, double y) b,
        (double x, double y) c, (double x, double y) d)
    {
        if (Math.Max(a.x, b.x) < Math.Min(c.x, d.x) || Math.Max(c.x, d.x) < Math.Min(a.x, b.x) ||
            Math.Max(a.y, b.y) < Math.Min(c.y, d.y) || Math.Max(c.y, d.y) < Math.Min(a.y, b.y)) return false;
        static double Cross((double x, double y) p, (double x, double y) q, (double x, double y) r) =>
            (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
        var abC = Cross(a, b, c); var abD = Cross(a, b, d); var cdA = Cross(c, d, a); var cdB = Cross(c, d, b);
        return abC * abD <= 0 && cdA * cdB <= 0;
    }

    private static IReadOnlyList<(double x, double y)> CloseRing(IReadOnlyList<(double x, double y)> ring)
    {
        if (ring.Count == 0 || ring[0] == ring[^1]) return ring;
        return ring.Concat([ring[0]]).ToArray();
    }

    private static string? ReadUuid(byte[] tags, IReadOnlyList<string> keys, IReadOnlyList<string> values)
    {
        var reader = new ProtoReader(tags); var indices = new List<int>(); while (!reader.End) indices.Add((int)reader.ReadVarint());
        for (var index = 0; index + 1 < indices.Count; index += 2)
            if (indices[index] < keys.Count && indices[index + 1] < values.Count && keys[indices[index]].Equals("uuid", StringComparison.OrdinalIgnoreCase))
                return values[indices[index + 1]];
        return null;
    }

    private static (double x, double y) ToWorld(double longitude, double latitude)
    {
        var n = 1 << Zoom; var radians = latitude * Math.PI / 180.0;
        return ((longitude + 180.0) / 360.0 * n, (1.0 - Math.Asinh(Math.Tan(radians)) / Math.PI) / 2.0 * n);
    }

    private static MapPoint ToMapPoint(double worldX, double worldY)
    {
        var n = 1 << Zoom;
        var longitude = worldX / n * 360.0 - 180.0;
        var latitude = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * worldY / n))) * 180.0 / Math.PI;
        return MapPointBuilderEx.CreateMapPoint(longitude, latitude, SpatialReferences.WGS84);
    }

    private static int Zig(ulong value) => (int)((value >> 1) ^ (ulong)-(long)(value & 1));
    private static string ReadValue(byte[] bytes) { var reader = new ProtoReader(bytes); while (reader.TryField(out var field, out var wire)) { if (field == 1) return reader.ReadString(); if (field is 4 or 5) return reader.ReadVarint().ToString(); if (field == 6) return Zig(reader.ReadVarint()).ToString(); reader.Skip(wire); } return ""; }

    private sealed class ProtoReader(byte[] data)
    {
        private int _position; public bool End => _position >= data.Length;
        public bool TryField(out int field, out int wire) { field = wire = 0; if (End) return false; var key = ReadVarint(); field = (int)(key >> 3); wire = (int)(key & 7); return true; }
        public ulong ReadVarint() { ulong value = 0; for (var shift = 0; shift < 64; shift += 7) { var next = data[_position++]; value |= (ulong)(next & 127) << shift; if ((next & 128) == 0) return value; } throw new InvalidDataException(); }
        public byte[] ReadBytes() { var length = (int)ReadVarint(); var result = data.AsSpan(_position, length).ToArray(); _position += length; return result; }
        public string ReadString() => Encoding.UTF8.GetString(ReadBytes());
        public void Skip(int wire) { if (wire == 0) ReadVarint(); else if (wire == 1) _position += 8; else if (wire == 2) _position += (int)ReadVarint(); else if (wire == 5) _position += 4; else throw new InvalidDataException(); }
    }
}
