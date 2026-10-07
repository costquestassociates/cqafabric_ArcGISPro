using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using CqaFabricPro.Services;

namespace CqaFabricPro;

internal sealed class IdentifyFabricTool : MapTool
{
    private static readonly Regex FabricName = new(@"^Fabric\s+(?<vintage>[^\s]+)\s+-\s+(?<layer>locations|retirements|footprints|parcels)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static FabricPopupWindow? _activePopup;

    internal static void CloseActivePopup()
    {
        if (_activePopup is null) return;
        var popup = _activePopup;
        _activePopup = null;
        popup.Close();
    }

    public IdentifyFabricTool()
    {
        IsSketchTool = true;
        SketchType = SketchGeometryType.Point;
        SketchOutputMode = SketchOutputMode.Map;
    }

    protected override async Task<bool> OnSketchCompleteAsync(Geometry geometry)
    {
        var session = IdentifySession.Current;
        var view = ActiveMapView;
        if (session is null || view is null || geometry is not MapPoint point) return false;

        try
        {
            await Application.Current.Dispatcher.InvokeAsync(CloseActivePopup);
            var scope = IdentifySearchSettings.LayerScope;
            var result = await QueuedTask.Run(() =>
            {
                var projected = GeometryEngine.Instance.Project(point, SpatialReferences.WGS84) as MapPoint;
                var selectedLayers = scope == IdentifyLayerScope.SelectedInContents
                    ? view.GetSelectedLayers().ToHashSet()
                    : null;
                var layers = view.Map.GetLayersAsFlattenedList()
                    .OfType<VectorTileLayer>()
                    .Where(layer => FabricName.IsMatch(layer.Name))
                    .Where(layer => scope == IdentifyLayerScope.SelectedInContents
                        ? selectedLayers!.Contains(layer)
                        : layer.IsVisible)
                    .Select(layer => layer.Name)
                    .ToArray();
                if (projected is null) return (projected, layers, toleranceMeters: 0.0);

                var screenPoint = view.MapToScreen(point);
                var radiusPoint = view.ScreenToMap(new Point(screenPoint.X + SelectionEnvironment.SelectionTolerance, screenPoint.Y));
                var radiusWgs84 = GeometryEngine.Instance.Project(radiusPoint, SpatialReferences.WGS84) as MapPoint;
                var toleranceMeters = radiusWgs84 is null ? 0.0 : HaversineMeters(projected, radiusWgs84);
                return (projected, layers, toleranceMeters);
            });
            if (result.projected is null) return false;

            var hits = new List<(IdentifyContext Context, IReadOnlyList<string> Ids)>();
            foreach (var layerName in result.layers)
            {
                var match = FabricName.Match(layerName);
                var candidate = new IdentifyContext(session.ApiKey, match.Groups["vintage"].Value, match.Groups["layer"].Value.ToLowerInvariant());
                var candidateIds = await MvtIdentifyService.FindAsync(candidate, result.projected.X, result.projected.Y, result.toleranceMeters);
                if (candidateIds.Count == 0) continue;
                hits.Add((candidate, candidateIds));
                if (scope == IdentifyLayerScope.TopmostLayer) break;
            }

            if (hits.Count == 0)
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show("No feature was found in the configured CQA Fabric search layers at that location.", "CQA Fabric");
                return true;
            }

            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var records = new List<IdentifiedFabricRecord>();
            foreach (var hit in hits)
            {
                using var api = new CostQuestApiClient(hit.Context.ApiKey);
                var fields = await api.GetFieldsAsync(hit.Context.Vintage, hit.Context.Layer);
                foreach (var item in fields.OfType<JsonObject>().Where(item => item["fieldname"] is not null))
                {
                    var fieldName = item["fieldname"]!.ToString();
                    aliases.TryAdd(fieldName, item["displayname"]?.ToString() ?? fieldName);
                }
                foreach (var id in hit.Ids.Take(20 - records.Count))
                {
                    var node = await api.GetIndividualDataAsync(hit.Context.Vintage, hit.Context.Layer, id);
                    if (node is JsonArray array)
                        records.AddRange(array.OfType<JsonObject>().Take(20 - records.Count)
                            .Select(record => new IdentifiedFabricRecord(hit.Context, record)));
                    else if (node is JsonObject record) records.Add(new(hit.Context, record));
                }
                if (records.Count >= 20) break;
            }
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CloseActivePopup();
                var popup = new FabricPopupWindow(records, aliases);
                popup.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_activePopup, popup)) _activePopup = null;
                };
                _activePopup = popup;
                popup.Show();
            });
        }
        catch (Exception ex)
        {
            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(ex.Message, "CQA Fabric Identify");
        }
        return true;
    }

    private static double HaversineMeters(MapPoint first, MapPoint second)
    {
        const double earthRadiusMeters = 6371008.8;
        var latitude1 = first.Y * Math.PI / 180.0;
        var latitude2 = second.Y * Math.PI / 180.0;
        var deltaLatitude = latitude2 - latitude1;
        var deltaLongitude = (second.X - first.X) * Math.PI / 180.0;
        var a = Math.Pow(Math.Sin(deltaLatitude / 2.0), 2)
            + Math.Cos(latitude1) * Math.Cos(latitude2) * Math.Pow(Math.Sin(deltaLongitude / 2.0), 2);
        return earthRadiusMeters * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
    }
}
