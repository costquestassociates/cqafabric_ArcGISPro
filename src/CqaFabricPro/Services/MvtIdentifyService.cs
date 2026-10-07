using System.IO;
using System.Net.Http;
using System.Text;

namespace CqaFabricPro.Services;

internal static class MvtIdentifyService
{
    private const double EarthCircumferenceMeters = 40075016.68557849;

    public static async Task<IReadOnlyList<string>> FindAsync(IdentifyContext context, double longitude, double latitude,
        double hitRadiusMeters, CancellationToken token = default)
    {
        const int zoom = 14;
        var n = 1 << zoom;
        var worldX = (longitude + 180.0) / 360.0 * n;
        var radians = latitude * Math.PI / 180.0;
        var worldY = (1.0 - Math.Asinh(Math.Tan(radians)) / Math.PI) / 2.0 * n;
        var metersPerTile = EarthCircumferenceMeters * Math.Max(Math.Cos(latitude * Math.PI / 180.0), 0.000001) / n;
        var radiusInTiles = Math.Max(hitRadiusMeters, 0.0) / metersPerTile;
        var minTileX = Math.Clamp((int)Math.Floor(worldX - radiusInTiles), 0, n - 1);
        var maxTileX = Math.Clamp((int)Math.Floor(worldX + radiusInTiles), 0, n - 1);
        var minTileY = Math.Clamp((int)Math.Floor(worldY - radiusInTiles), 0, n - 1);
        var maxTileY = Math.Clamp((int)Math.Floor(worldY + radiusInTiles), 0, n - 1);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("apikey", context.ApiKey);
        var requests = new List<Task<IReadOnlyList<string>>>();
        for (var tileX = minTileX; tileX <= maxTileX; tileX++)
        for (var tileY = minTileY; tileY <= maxTileY; tileY++)
        {
            var x = tileX;
            var y = tileY;
            requests.Add(ReadTileAsync(x, y));
        }
        var results = await Task.WhenAll(requests);
        return results.SelectMany(ids => ids).Distinct().ToArray();

        async Task<IReadOnlyList<string>> ReadTileAsync(int tileX, int tileY)
        {
            var url = $"https://api.costquest.com/fabric/{Uri.EscapeDataString(context.Vintage)}/tiles/{Uri.EscapeDataString(context.Layer)}/{zoom}/{tileX}/{tileY}";
            var bytes = await http.GetByteArrayAsync(url, token);
            return ParseTile(bytes, context.Layer, worldX - tileX, worldY - tileY, latitude, n, hitRadiusMeters);
        }
    }

    private static IReadOnlyList<string> ParseTile(byte[] bytes, string layerName, double fractionX, double fractionY,
        double latitude, int tileCount, double hitRadiusMeters)
    {
        var reader = new ProtoReader(bytes);
        while (reader.TryField(out var field, out var wire))
        {
            if (field == 3 && wire == 2)
            {
                var layer = reader.ReadBytes();
                var result = ParseLayer(layer, layerName, fractionX, fractionY, latitude, tileCount, hitRadiusMeters);
                if (result is not null) return result;
            }
            else reader.Skip(wire);
        }
        return [];
    }

    private static IReadOnlyList<string>? ParseLayer(byte[] bytes, string expectedName, double fx, double fy,
        double latitude, int tileCount, double hitRadiusMeters)
    {
        var r = new ProtoReader(bytes); string name = ""; uint extent = 4096;
        var features = new List<byte[]>(); var keys = new List<string>(); var values = new List<string>();
        while (r.TryField(out var f, out var w))
        {
            if (f == 1) name = r.ReadString(); else if (f == 2) features.Add(r.ReadBytes());
            else if (f == 3) keys.Add(r.ReadString()); else if (f == 4) values.Add(ReadValue(r.ReadBytes()));
            else if (f == 5) extent = (uint)r.ReadVarint(); else r.Skip(w);
        }
        if (!name.Equals(expectedName, StringComparison.OrdinalIgnoreCase)) return null;
        var x = fx * extent; var y = fy * extent; var hits = new List<string>();
        var metersPerTileUnit = EarthCircumferenceMeters * Math.Max(Math.Cos(latitude * Math.PI / 180.0), 0.000001)
            / (tileCount * extent);
        var pointHitRadius = Math.Max(hitRadiusMeters, 0.0) / metersPerTileUnit;
        foreach (var feature in features)
        {
            var fr = new ProtoReader(feature); byte[] tags = []; byte[] geometry = []; int type = 0;
            while (fr.TryField(out var f, out var w))
            { if (f == 2) tags = fr.ReadBytes(); else if (f == 3) type = (int)fr.ReadVarint(); else if (f == 4) geometry = fr.ReadBytes(); else fr.Skip(w); }
            if (!Hit(geometry, type, x, y, pointHitRadius)) continue;
            var tr = new ProtoReader(tags); var indices = new List<int>(); while (!tr.End) indices.Add((int)tr.ReadVarint());
            for (var i = 0; i + 1 < indices.Count; i += 2)
                if (indices[i] < keys.Count && indices[i + 1] < values.Count && keys[indices[i]].Equals("uuid", StringComparison.OrdinalIgnoreCase)) hits.Add(values[indices[i + 1]]);
        }
        return hits.Distinct().ToArray();
    }

    private static bool Hit(byte[] geometry, int type, double px, double py, double pointHitRadius)
    {
        var r = new ProtoReader(geometry); var x = 0; var y = 0; var rings = new List<List<(int x,int y)>>(); List<(int,int)>? current = null;
        while (!r.End)
        {
            var cmd = (uint)r.ReadVarint(); var id = cmd & 7; var count = (int)(cmd >> 3);
            if (id is 1 or 2) for (var i=0;i<count;i++) { x += Zig(r.ReadVarint()); y += Zig(r.ReadVarint()); if(id==1){current=[];rings.Add(current);} current?.Add((x,y)); }
            else if (id == 7) { } else break;
        }
        if (type == 1)
        {
            var radiusSquared = pointHitRadius * pointHitRadius;
            return rings.SelectMany(ring => ring).Any(point =>
            {
                var dx = point.x - px;
                var dy = point.y - py;
                return dx * dx + dy * dy <= radiusSquared;
            });
        }
        var inside=false; foreach(var ring in rings){ for(int i=0,j=ring.Count-1;i<ring.Count;j=i++) { var a=ring[i];var b=ring[j]; if (DistanceSquaredToSegment(px, py, a.x, a.y, b.x, b.y) <= pointHitRadius * pointHitRadius) return true; if(((a.y>py)!=(b.y>py)) && px < (double)(b.x-a.x)*(py-a.y)/(b.y-a.y)+a.x) inside=!inside; } } return inside;
    }
    private static double DistanceSquaredToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax; var dy = by - ay;
        if (dx == 0 && dy == 0) return (px - ax) * (px - ax) + (py - ay) * (py - ay);
        var t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy), 0.0, 1.0);
        var nearestX = ax + t * dx; var nearestY = ay + t * dy;
        return (px - nearestX) * (px - nearestX) + (py - nearestY) * (py - nearestY);
    }
    private static int Zig(ulong value) => (int)((value >> 1) ^ (ulong)-(long)(value & 1));
    private static string ReadValue(byte[] bytes) { var r=new ProtoReader(bytes); while(r.TryField(out var f,out var w)){ if(f==1)return r.ReadString(); if(f is 4 or 5)return r.ReadVarint().ToString(); if(f==6)return Zig(r.ReadVarint()).ToString(); r.Skip(w);} return ""; }

    private sealed class ProtoReader(byte[] data)
    {
        private int _p; public bool End=>_p>=data.Length;
        public bool TryField(out int field,out int wire){field=wire=0;if(End)return false;var k=ReadVarint();field=(int)(k>>3);wire=(int)(k&7);return true;}
        public ulong ReadVarint(){ulong v=0;for(var s=0;s<64;s+=7){var b=data[_p++];v|=(ulong)(b&127)<<s;if((b&128)==0)return v;}throw new InvalidDataException();}
        public byte[] ReadBytes(){var n=(int)ReadVarint();var b=data.AsSpan(_p,n).ToArray();_p+=n;return b;}
        public string ReadString()=>Encoding.UTF8.GetString(ReadBytes());
        public void Skip(int wire){if(wire==0)ReadVarint();else if(wire==1)_p+=8;else if(wire==2)_p+=(int)ReadVarint();else if(wire==5)_p+=4;else throw new InvalidDataException();}
    }
}
