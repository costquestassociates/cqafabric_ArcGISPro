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
            var result = await QueuedTask.Run(() =>
            {
                var projected = GeometryEngine.Instance.Project(point, SpatialReferences.WGS84) as MapPoint;
                var layers = view.Map.GetLayersAsFlattenedList()
                    .OfType<VectorTileLayer>()
                    .Where(layer => layer.IsVisible && FabricName.IsMatch(layer.Name))
                    .Select(layer => layer.Name)
                    .ToArray();
                return (projected, layers);
            });
            if (result.projected is null) return false;

            IdentifyContext? hitContext = null;
            IReadOnlyList<string> ids = [];
            foreach (var layerName in result.layers)
            {
                var match = FabricName.Match(layerName);
                var candidate = new IdentifyContext(session.ApiKey, match.Groups["vintage"].Value, match.Groups["layer"].Value.ToLowerInvariant());
                var candidateIds = await MvtIdentifyService.FindAsync(candidate, result.projected.X, result.projected.Y);
                if (candidateIds.Count == 0) continue;
                hitContext = candidate;
                ids = candidateIds;
                break;
            }

            if (hitContext is null)
            {
                ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show("No feature was found in any visible CQA Fabric layer at that location.", "CQA Fabric");
                return true;
            }

            using var api = new CostQuestApiClient(hitContext.ApiKey);
            var fields = await api.GetFieldsAsync(hitContext.Vintage, hitContext.Layer);
            var aliases = fields.OfType<JsonObject>()
                .Where(item => item["fieldname"] is not null)
                .ToDictionary(item => item["fieldname"]!.ToString(), item => item["displayname"]?.ToString() ?? item["fieldname"]!.ToString(), StringComparer.OrdinalIgnoreCase);
            var records = new List<JsonObject>();
            foreach (var id in ids.Take(20))
            {
                var node = await api.GetIndividualDataAsync(hitContext.Vintage, hitContext.Layer, id);
                if (node is JsonArray array) records.AddRange(array.OfType<JsonObject>());
                else if (node is JsonObject record) records.Add(record);
            }
            var popupContext = hitContext;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CloseActivePopup();
                var popup = new FabricPopupWindow(popupContext, records, aliases);
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
}
