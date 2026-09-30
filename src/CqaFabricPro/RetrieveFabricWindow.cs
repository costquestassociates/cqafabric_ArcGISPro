using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using CqaFabricPro.Services;

namespace CqaFabricPro;

internal sealed class RetrieveFabricWindow : Window
{
    private static RetrieveFabricWindow? _active;
    private readonly FabricPaneViewModel _session;
    private readonly ComboBox _vintage = new();
    private readonly ComboBox _layer = new();
    private readonly ComboBox _defineLayer = new();
    private readonly CheckBox _useSelection = new() { Content = "Use selected polygons only" };
    private readonly ListBox _fields = new() { SelectionMode = SelectionMode.Extended, Height = 150 };
    private readonly DataGrid _results = new() { IsReadOnly = true, AutoGenerateColumns = true };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _retrieve = new() { Content = "Retrieve", MinWidth = 95 };
    private readonly Button _save = new() { Content = "Save CSV", MinWidth = 95, IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _addToMap = new() { Content = "Add to Map", MinWidth = 105, IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private JsonArray _records = [];
    private IReadOnlyDictionary<string, MvtSpatialFeature> _polygonFeatures =
        new Dictionary<string, MvtSpatialFeature>(StringComparer.OrdinalIgnoreCase);

    private RetrieveFabricWindow(FabricPaneViewModel session)
    {
        _session = session;
        Title = "CQA Fabric";
        Width = 900;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;
        FabricUi.Apply(this);

        var root = new DockPanel { Margin = FabricUi.PageMargin };
        Content = root;
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        top.Children.Add(new TextBlock { Text = "Retrieve Fabric", FontSize = 14, FontWeight = FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        top.Children.Add(new TextBlock
        {
            Text = "Choose a polygon Define Layer. Retrieve finds intersecting Fabric locations, footprints, or parcels.",
            Foreground = FabricUi.MutedBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12)
        });

        var selectors = new Grid();
        selectors.ColumnDefinitions.Add(new ColumnDefinition());
        selectors.ColumnDefinitions.Add(new ColumnDefinition());
        var vintagePanel = Labeled("Vintage", _vintage, new Thickness(0, 0, 6, 0));
        var layerPanel = Labeled("Layer", _layer, new Thickness(6, 0, 0, 0));
        Grid.SetColumn(layerPanel, 1);
        selectors.Children.Add(vintagePanel);
        selectors.Children.Add(layerPanel);
        top.Children.Add(selectors);

        var definePanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        definePanel.Children.Add(new TextBlock { Text = "Define Layer (polygon)" });
        var defineRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        defineRow.ColumnDefinitions.Add(new ColumnDefinition());
        defineRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        defineRow.Children.Add(_defineLayer);
        var refreshLayers = new Button { Content = "Refresh layers", Margin = new Thickness(8, 0, 0, 0), MinWidth = 100 };
        refreshLayers.Click += async (_, _) => await LoadDefineLayersAsync();
        Grid.SetColumn(refreshLayers, 1);
        defineRow.Children.Add(refreshLayers);
        definePanel.Children.Add(defineRow);
        _useSelection.Margin = new Thickness(0, 6, 0, 0);
        definePanel.Children.Add(_useSelection);
        top.Children.Add(definePanel);

        top.Children.Add(new TextBlock { Text = "Fields (Ctrl+click for multiple)", Margin = new Thickness(0, 10, 0, 4) });
        top.Children.Add(_fields);
        var fieldButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        var selectAll = new Button { Content = "Select all", MinWidth = 85 };
        selectAll.Click += (_, _) => _fields.SelectAll();
        var clear = new Button { Content = "Clear", MinWidth = 85, Margin = new Thickness(8, 0, 0, 0) };
        clear.Click += (_, _) => _fields.UnselectAll();
        fieldButtons.Children.Add(selectAll);
        fieldButtons.Children.Add(clear);
        top.Children.Add(fieldButtons);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        _retrieve.Click += Retrieve_OnClick;
        _save.Click += Save_OnClick;
        _addToMap.Click += AddToMap_OnClick;
        actions.Children.Add(_retrieve);
        actions.Children.Add(_save);
        actions.Children.Add(_addToMap);
        top.Children.Add(actions);
        _status.Margin = new Thickness(0, 10, 0, 0);
        top.Children.Add(_status);

        root.Children.Add(_results);
        _vintage.ItemsSource = session.Vintages;
        var retrieveLayers = session.Layers.ToArray();
        _layer.ItemsSource = retrieveLayers;
        _vintage.SelectedItem = session.SelectedVintage ?? session.Vintages.FirstOrDefault();
        _layer.SelectedItem = retrieveLayers.FirstOrDefault(name => name.Equals(session.SelectedLayer, StringComparison.OrdinalIgnoreCase))
            ?? retrieveLayers.FirstOrDefault();
        _vintage.SelectionChanged += Selection_OnChanged;
        _layer.SelectionChanged += Selection_OnChanged;
        Loaded += async (_, _) => { await LoadDefineLayersAsync(); await LoadFieldsAsync(); };
        Closed += (_, _) => { if (ReferenceEquals(_active, this)) _active = null; };
    }

    internal static void ShowFor(FabricPaneViewModel session)
    {
        if (_active is not null)
        {
            _active.Activate();
            return;
        }
        _active = new RetrieveFabricWindow(session);
        _active.Show();
    }

    private static StackPanel Labeled(string label, Control control, Thickness margin)
    {
        var panel = new StackPanel { Margin = margin };
        panel.Children.Add(new TextBlock { Text = label });
        control.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(control);
        return panel;
    }

    private async void Selection_OnChanged(object sender, SelectionChangedEventArgs e) => await LoadFieldsAsync();

    private async Task LoadFieldsAsync()
    {
        var vintage = _vintage.SelectedItem as string;
        var layer = _layer.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(vintage) || string.IsNullOrWhiteSpace(layer)) return;
        _fields.IsEnabled = false;
        _status.Text = "Loading fields...";
        try
        {
            using var api = new CostQuestApiClient(_session.ApiKey);
            var metadata = await api.GetFieldsAsync(vintage, layer);
            _fields.ItemsSource = metadata.OfType<JsonObject>()
                .Where(item => item["fieldname"] is not null)
                .OrderByDescending(item => int.TryParse(item["listorder"]?.ToString(), out var order) ? order : 0)
                .Select(item => new FieldOption(item["fieldname"]!.ToString(), item["displayname"]?.ToString() ?? item["fieldname"]!.ToString()))
                .ToArray();
            _status.Text = $"Loaded {_fields.Items.Count} fields.";
        }
        catch (Exception ex) { _status.Text = $"Could not load fields: {ex.Message}"; }
        finally { _fields.IsEnabled = true; }
    }

    private async Task LoadDefineLayersAsync()
    {
        var currentName = (_defineLayer.SelectedItem as LayerOption)?.Name;
        var options = await QueuedTask.Run(() => MapView.Active?.Map.GetLayersAsFlattenedList()
            .OfType<FeatureLayer>()
            .Where(layer => layer.ShapeType == ArcGIS.Core.CIM.esriGeometryType.esriGeometryPolygon)
            .Select(layer => new LayerOption(layer.Name, layer))
            .ToArray() ?? []);
        _defineLayer.ItemsSource = options;
        _defineLayer.SelectedItem = options.FirstOrDefault(option => option.Name == currentName) ?? options.FirstOrDefault();
        if (options.Length == 0) _status.Text = "Add a polygon feature layer to the active map, then click Refresh layers.";
    }

    private async void Retrieve_OnClick(object sender, RoutedEventArgs e)
    {
        var vintage = _vintage.SelectedItem as string;
        var layer = _layer.SelectedItem as string;
        var defineLayer = _defineLayer.SelectedItem as LayerOption;
        var fields = _fields.SelectedItems.OfType<FieldOption>().Select(item => item.Name).ToArray();
        if (string.IsNullOrWhiteSpace(vintage) || string.IsNullOrWhiteSpace(layer) || fields.Length == 0 || defineLayer is null)
        {
            _status.Text = "Select a polygon Define Layer, vintage, and at least one field.";
            return;
        }

        _retrieve.IsEnabled = false;
        _save.IsEnabled = false;
        _addToMap.IsEnabled = false;
        _status.Text = "Reading and dissolving Define Layer polygons...";
        try
        {
            var polygon = await ReadAreaOfInterestAsync(defineLayer.Layer, _useSelection.IsChecked == true);
            using var api = new CostQuestApiClient(_session.ApiKey);
            IReadOnlyList<string> ids;
            _polygonFeatures = new Dictionary<string, MvtSpatialFeature>(StringComparer.OrdinalIgnoreCase);
            if (layer.Equals("locations", StringComparison.OrdinalIgnoreCase))
            {
                _status.Text = "Collecting Fabric locations in the Define Layer...";
                ids = await api.CollectAsync(vintage, ToGeoJson(polygon));
            }
            else
            {
                _status.Text = $"Selecting intersecting Fabric {layer} polygons...";
                var features = await MvtSpatialQueryService.FindWithinAsync(
                    new IdentifyContext(_session.ApiKey, vintage, layer), polygon);
                _polygonFeatures = features.ToDictionary(feature => feature.Uuid, StringComparer.OrdinalIgnoreCase);
                ids = features.Select(feature => feature.Uuid).ToArray();
            }
            if (ids.Count == 0)
            {
                _records = [];
                _results.ItemsSource = null;
                _status.Text = $"No Fabric {layer} features intersect the Define Layer.";
                return;
            }
            _status.Text = $"Retrieving selected fields for {ids.Count} Fabric {layer} feature(s)...";
            _records = await api.RetrieveAsync(vintage, layer, ids, fields);
            _results.ItemsSource = BuildTable(_records).DefaultView;
            _save.IsEnabled = _records.Count > 0;
            _addToMap.IsEnabled = _records.Count > 0;
            _status.Text = $"Retrieved {_records.Count} record(s).";
        }
        catch (Exception ex) { _status.Text = $"Retrieve failed: {ex.Message}"; }
        finally { _retrieve.IsEnabled = true; }
    }

    private async void AddToMap_OnClick(object sender, RoutedEventArgs e)
    {
        var view = MapView.Active;
        var vintage = _vintage.SelectedItem as string;
        var layer = _layer.SelectedItem as string;
        var defineName = (_defineLayer.SelectedItem as LayerOption)?.Name ?? "DefineLayer";
        if (view?.Map is null || string.IsNullOrWhiteSpace(vintage) || string.IsNullOrWhiteSpace(layer) || _records.Count == 0)
        {
            _status.Text = "Open a map and retrieve records before adding results.";
            return;
        }

        _addToMap.IsEnabled = false;
        _status.Text = "Creating the result layer...";
        try
        {
            var ids = _records.OfType<JsonObject>().Select(record => record["uuid"]?.ToString())
                .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
            IReadOnlyDictionary<string, JsonObject> coordinatesById = new Dictionary<string, JsonObject>();
            if (layer.Equals("locations", StringComparison.OrdinalIgnoreCase))
            {
                using var api = new CostQuestApiClient(_session.ApiKey);
                var coordinates = await api.RetrieveAsync(vintage, "locations", ids, ["latitude", "longitude"]);
                coordinatesById = coordinates.OfType<JsonObject>().Where(record => record["uuid"] is not null)
                    .ToDictionary(record => record["uuid"]!.ToString(), StringComparer.OrdinalIgnoreCase);
            }
            var layerName = $"{defineName}_{layer}_{vintage}_{DateTime.Now:yyyyMMddHHmmss}";
            var originalCamera = view.Camera;
            var added = await QueuedTask.Run(() => CreateResultLayer(view.Map, layerName, layer, _records, coordinatesById, _polygonFeatures));
            await view.ZoomToAsync(originalCamera, TimeSpan.Zero);
            _status.Text = added > 0
                ? $"Added {added} retrieved feature(s) to {layerName}."
                : "No retrieved records contained valid geometry.";
        }
        catch (Exception ex) { _status.Text = $"Could not add results to the map: {ex.Message}"; }
        finally { _addToMap.IsEnabled = _records.Count > 0; }
    }

    private static int CreateResultLayer(Map map, string layerName, string fabricLayer, JsonArray records,
        IReadOnlyDictionary<string, JsonObject> coordinatesById,
        IReadOnlyDictionary<string, MvtSpatialFeature> polygonFeatures)
    {
        var propertyNames = records.OfType<JsonObject>().SelectMany(record => record.Select(property => property.Key))
            .Where(name => !name.Equals("latitude", StringComparison.OrdinalIgnoreCase) &&
                           !name.Equals("longitude", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var fieldNames = CreateFieldNames(propertyNames);
        var fields = new List<ArcGIS.Core.Data.DDL.FieldDescription> { ArcGIS.Core.Data.DDL.FieldDescription.CreateObjectIDField() };
        foreach (var propertyName in propertyNames)
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription(fieldNames[propertyName], FieldType.String)
            {
                AliasName = propertyName,
                Length = propertyName.Equals("uuid", StringComparison.OrdinalIgnoreCase) ? 255 : 4000
            });

        var databaseName = $"CqaFabricRetrieve_{Guid.NewGuid():N}";
        using var geodatabase = new Geodatabase(new MemoryConnectionProperties(databaseName));
        var isPoint = fabricLayer.Equals("locations", StringComparison.OrdinalIgnoreCase);
        const string datasetName = "retrieved_fabric";
        var description = new FeatureClassDescription(datasetName, fields,
            new ShapeDescription(isPoint ? GeometryType.Point : GeometryType.Polygon, SpatialReferences.WGS84));
        var schemaBuilder = new SchemaBuilder(geodatabase);
        schemaBuilder.Create(description);
        if (!schemaBuilder.Build())
            throw new InvalidOperationException(string.Join(Environment.NewLine, schemaBuilder.ErrorMessages));

        using var featureClass = geodatabase.OpenDataset<FeatureClass>(datasetName);
        var shapeField = featureClass.GetDefinition().GetShapeField();
        var added = 0;
        geodatabase.ApplyEdits(() =>
        {
            foreach (var record in records.OfType<JsonObject>())
            {
                var uuid = record["uuid"]?.ToString();
                if (string.IsNullOrWhiteSpace(uuid)) continue;
                using var buffer = featureClass.CreateRowBuffer();
                if (isPoint)
                {
                    if (!coordinatesById.TryGetValue(uuid, out var coordinate) ||
                        !double.TryParse(coordinate["longitude"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
                        !double.TryParse(coordinate["latitude"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)) continue;
                    buffer[shapeField] = MapPointBuilderEx.CreateMapPoint(longitude, latitude, SpatialReferences.WGS84);
                }
                else
                {
                    if (!polygonFeatures.TryGetValue(uuid, out var spatialFeature)) continue;
                    var builder = new PolygonBuilderEx(SpatialReferences.WGS84);
                    foreach (var ring in spatialFeature.Rings) builder.AddPart(ring);
                    var polygon = GeometryEngine.Instance.SimplifyAsFeature(builder.ToGeometry()) as Polygon;
                    if (polygon is null || polygon.IsEmpty) continue;
                    buffer[shapeField] = polygon;
                }
                foreach (var propertyName in propertyNames) buffer[fieldNames[propertyName]] = Value(record[propertyName]);
                using var row = featureClass.CreateRow(buffer);
                added++;
            }
        });

        if (added > 0)
        {
            var parameters = new FeatureLayerCreationParams(featureClass)
            {
                Name = layerName,
                IsVisible = true,
                AutoZoomOnEmptyMap = false,
                MapMemberPosition = MapMemberPosition.AddToTop
            };
            LayerFactory.Instance.CreateLayer<FeatureLayer>(parameters, map);
        }
        return added;
    }

    private static Dictionary<string, string> CreateFieldNames(IEnumerable<string> propertyNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertyName in propertyNames)
        {
            var cleaned = new string(propertyName.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
            if (string.IsNullOrWhiteSpace(cleaned) || !char.IsLetter(cleaned[0])) cleaned = $"f_{cleaned}";
            if (cleaned.Length > 60) cleaned = cleaned[..60];
            var candidate = cleaned;
            for (var suffix = 2; !used.Add(candidate); suffix++) candidate = $"{cleaned[..Math.Min(cleaned.Length, 56)]}_{suffix}";
            result[propertyName] = candidate;
        }
        return result;
    }

    private static Task<Polygon> ReadAreaOfInterestAsync(FeatureLayer layer, bool selectedOnly) => QueuedTask.Run(() =>
    {
        IReadOnlyList<long>? objectIds = null;
        if (selectedOnly)
        {
            using var selection = layer.GetSelection();
            objectIds = selection.GetObjectIDs();
            if (objectIds.Count == 0) throw new InvalidOperationException("The Define Layer has no selected polygons.");
        }

        var geometries = new List<Geometry>();
        using var table = layer.GetTable();
        var filter = new QueryFilter { SubFields = "*", ObjectIDs = objectIds };
        using var cursor = table.Search(filter, false);
        while (cursor.MoveNext())
        {
            using var row = cursor.Current;
            if (row is Feature feature && feature.GetShape() is Polygon polygon && !polygon.IsEmpty)
                geometries.Add(polygon);
        }
        if (geometries.Count == 0) throw new InvalidOperationException("The Define Layer contains no polygons to retrieve against.");
        var dissolved = GeometryEngine.Instance.Union(geometries) as Polygon
            ?? throw new InvalidOperationException("The Define Layer polygons could not be dissolved.");
        return GeometryEngine.Instance.Project(dissolved, SpatialReferences.WGS84) as Polygon
            ?? throw new InvalidOperationException("The Define Layer could not be projected to WGS 84.");
    });

    private static JsonObject ToGeoJson(Polygon polygon)
    {
        var polygons = new JsonArray();
        JsonArray? currentPolygon = null;
        for (var partIndex = 0; partIndex < polygon.PartCount; partIndex++)
        {
            if (polygon.IsExteriorRing(partIndex) || currentPolygon is null)
            {
                currentPolygon = [];
                polygons.Add(currentPolygon);
            }
            var ring = new JsonArray();
            MapPoint? first = null;
            foreach (var segment in polygon.Parts[partIndex])
            {
                first ??= segment.StartPoint;
                ring.Add(new JsonArray(segment.StartPoint.X, segment.StartPoint.Y));
            }
            if (first is not null) ring.Add(new JsonArray(first.X, first.Y));
            currentPolygon.Add(ring);
        }

        return polygons.Count == 1
            ? new JsonObject { ["type"] = "Polygon", ["coordinates"] = polygons[0]!.DeepClone() }
            : new JsonObject { ["type"] = "MultiPolygon", ["coordinates"] = polygons };
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save Retrieve Fabric results", Filter = "CSV files (*.csv)|*.csv", FileName = "fabric_retrieve.csv" };
        if (dialog.ShowDialog(this) != true) return;
        var fields = _records.OfType<JsonObject>().SelectMany(record => record.Select(property => property.Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var csv = new StringBuilder().AppendLine(string.Join(',', fields.Select(Csv)));
        foreach (var record in _records.OfType<JsonObject>())
            csv.AppendLine(string.Join(',', fields.Select(field => Csv(Value(record[field])))));
        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
        _status.Text = $"Saved {dialog.FileName}";
    }

    private static DataTable BuildTable(JsonArray records)
    {
        var table = new DataTable();
        var fields = records.OfType<JsonObject>().SelectMany(record => record.Select(property => property.Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var field in fields) table.Columns.Add(field);
        foreach (var record in records.OfType<JsonObject>())
        {
            var row = table.NewRow();
            foreach (var field in fields) row[field] = Value(record[field]);
            table.Rows.Add(row);
        }
        return table;
    }

    private static string Value(JsonNode? node) => node is null ? string.Empty : node.ToJsonString().Trim('"');
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private sealed record FieldOption(string Name, string DisplayName) { public override string ToString() => DisplayName; }
    private sealed record LayerOption(string Name, FeatureLayer Layer) { public override string ToString() => Name; }
}
