using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using CqaFabricPro.Services;

namespace CqaFabricPro;

internal sealed class LocateFabricWindow : Window
{
    private static LocateFabricWindow? _active;
    private readonly FabricPaneViewModel _session;
    private readonly ComboBox _vintage = new();
    private readonly RadioButton _coordinate = new() { Content = "Coordinate", IsChecked = true };
    private readonly RadioButton _address = new() { Content = "Address", Margin = new Thickness(14, 0, 0, 0) };
    private readonly TextBox _input = new();
    private readonly CheckBox _batch = new() { Content = "Batch CSV", Margin = new Thickness(14, 0, 0, 0) };
    private readonly TextBox _csvPath = new() { IsReadOnly = true };
    private readonly Button _browse = new() { Content = "Browse…", MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
    private readonly CheckBox _attributes = new() { Content = "Include all location attributes", IsChecked = false };
    private readonly DataGrid _grid = new() { IsReadOnly = true, AutoGenerateColumns = true };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Foreground = FabricUi.MutedBrush };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _run = new() { Content = "Locate", MinWidth = 90 };
    private readonly Button _save = new() { Content = "Save CSV", MinWidth = 90, IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
    private JsonArray _results = [];

    private LocateFabricWindow(FabricPaneViewModel session)
    {
        _session = session; Title = "CQA Fabric"; Width = 900; Height = 620;
        Owner = Application.Current?.MainWindow; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FabricUi.Apply(this);
        var root = new DockPanel { Margin = FabricUi.PageMargin }; Content = root;
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Locate Fabric", FontSize = 14, FontWeight = FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        top.Children.Add(new TextBlock { Text = "Vintage", Margin = new Thickness(0, 10, 0, 4) }); top.Children.Add(_vintage);
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 4) };
        modes.Children.Add(_coordinate); modes.Children.Add(_address); modes.Children.Add(_batch); top.Children.Add(modes);
        top.Children.Add(_hint); _input.Margin = new Thickness(0, 4, 0, 0); top.Children.Add(_input);
        var fileRow = new Grid { Margin = new Thickness(0, 4, 0, 0) }; fileRow.ColumnDefinitions.Add(new ColumnDefinition()); fileRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fileRow.Children.Add(_csvPath); Grid.SetColumn(_browse, 1); fileRow.Children.Add(_browse); top.Children.Add(fileRow);
        _attributes.Margin = new Thickness(0, 8, 0, 0); top.Children.Add(_attributes);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(_run); actions.Children.Add(_save); top.Children.Add(actions);
        _status.Margin = new Thickness(0, 8, 0, 0); top.Children.Add(_status); root.Children.Add(_grid);
        _vintage.ItemsSource = session.Vintages; _vintage.SelectedItem = session.SelectedVintage ?? session.Vintages.FirstOrDefault();
        _coordinate.Checked += (_, _) => UpdateHint(); _address.Checked += (_, _) => UpdateHint();
        _batch.Checked += (_, _) => UpdateHint(); _batch.Unchecked += (_, _) => UpdateHint(); _browse.Click += Browse_OnClick;
        _run.Click += Run_OnClick; _save.Click += Save_OnClick; UpdateHint();
        Closed += (_, _) => { if (ReferenceEquals(_active, this)) _active = null; };
    }

    internal static void ShowFor(FabricPaneViewModel session)
    {
        if (_active is not null) { _active.Activate(); return; }
        _active = new LocateFabricWindow(session); _active.Show();
    }

    private void UpdateHint()
    {
        var batch = _batch.IsChecked == true; _input.Visibility = batch ? Visibility.Collapsed : Visibility.Visible;
        _csvPath.Visibility = _browse.Visibility = batch ? Visibility.Visible : Visibility.Collapsed;
        _hint.Text = batch
            ? (_coordinate.IsChecked == true ? "CSV columns required: sourcekey, latitude, longitude." : "CSV columns required: sourcekey, address.")
            : (_coordinate.IsChecked == true ? "Enter longitude, latitude (example: -80.924, 36.660)." : "Enter a street address.");
    }

    private void Browse_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv", Title = "Select Locate Fabric input" };
        if (dialog.ShowDialog(this) == true) _csvPath.Text = dialog.FileName;
    }

    private async void Run_OnClick(object sender, RoutedEventArgs e)
    {
        var vintage = _vintage.SelectedItem as string; var batch = _batch.IsChecked == true;
        var text = batch ? _csvPath.Text.Trim() : _input.Text.Trim();
        if (string.IsNullOrWhiteSpace(vintage) || string.IsNullOrWhiteSpace(text)) { _status.Text = "Select a vintage and enter search text."; return; }
        _run.IsEnabled = false; _save.IsEnabled = false; _status.Text = "Locating Fabric locations...";
        try
        {
            using var api = new CostQuestApiClient(_session.ApiKey);
            var sourceKeys = new List<string>();
            if (batch)
            {
                var inputs = ReadCsv(text, _coordinate.IsChecked == true);
                sourceKeys.AddRange(inputs.Select(item => item["sourcekey"]!.ToString()));
                _results = [];
                var operation = _coordinate.IsChecked == true ? "locate" : "match";
                var batchSize = await api.GetMaxRequestAsync("fabricext", operation,
                    _coordinate.IsChecked == true ? 500 : 10);
                var progress = new Progress<int>(completed =>
                    _status.Text = $"Processed {completed:N0} of {inputs.Count:N0} input row(s)...");
                var responses = _coordinate.IsChecked == true
                    ? await api.LocateBulkAsync(vintage, inputs, batchSize, progress)
                    : await api.MatchBulkAsync(vintage, inputs, batchSize, progress);
                foreach (var response in responses)
                    foreach (var result in ExtractLocations(response)) _results.Add(result?.DeepClone());
            }
            else
            {
                JsonNode? response;
                if (_coordinate.IsChecked == true)
                {
                    var values = text.Split(',').Select(value => double.Parse(value.Trim(), CultureInfo.InvariantCulture)).ToArray();
                    if (values.Length != 2 || values[0] is < -180 or > 180 || values[1] is < -90 or > 90) throw new FormatException("Use longitude, latitude with valid decimal coordinates.");
                    response = await api.LocateAsync(vintage, new JsonArray(new JsonObject { ["longitude"] = values[0], ["latitude"] = values[1], ["sourcekey"] = "1" }));
                }
                else response = await api.MatchAsync(vintage, text);
                _results = ExtractLocations(response);
            }
            if (_attributes.IsChecked == true && _results.Count > 0)
            {
                var ids = _results.OfType<JsonObject>().Select(item => item["uuid"]?.ToString()).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
                var metadata = await api.GetFieldsAsync(vintage, "locations");
                var fields = metadata.OfType<JsonObject>().Select(item => item["fieldname"]?.ToString()).Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToArray();
                var details = await api.RetrieveAsync(vintage, "locations", ids, fields);
                var detailById = details.OfType<JsonObject>().Where(item => item["uuid"] is not null).ToDictionary(item => item["uuid"]!.ToString(), StringComparer.OrdinalIgnoreCase);
                foreach (var item in _results.OfType<JsonObject>())
                    if (item["uuid"] is JsonNode id && detailById.TryGetValue(id.ToString(), out var detail))
                        foreach (var property in detail) item[property.Key] = property.Value?.DeepClone();
            }
            if (batch)
            {
                var matched = _results.OfType<JsonObject>().Select(item => item["sourcekey"]?.ToString()).Where(key => key is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var key in sourceKeys.Where(key => !matched.Contains(key))) _results.Add(new JsonObject { ["sourcekey"] = key });
            }
            _grid.ItemsSource = BuildTable(new JsonArray(_results.Take(500).Select(item => item?.DeepClone()).ToArray())).DefaultView; _save.IsEnabled = _results.Count > 0;
            await ZoomToResultsAsync(_results); _status.Text = _results.Count == 0 ? "No Fabric locations were found." : $"Located {_results.Count} Fabric location(s).";
        }
        catch (Exception ex) { _results = []; _grid.ItemsSource = null; _status.Text = $"Locate failed: {ex.Message}"; }
        finally { _run.IsEnabled = true; }
    }

    private static JsonArray ExtractLocations(JsonNode? node)
    {
        var output = new JsonArray(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(JsonNode? value, string? inheritedSourceKey = null)
        {
            if (value is JsonArray array) { foreach (var child in array) Visit(child, inheritedSourceKey); return; }
            if (value is not JsonObject obj) return;
            var sourceKey = obj["sourcekey"]?.ToString() ?? inheritedSourceKey;
            if (obj["locations"] is JsonNode locations) Visit(locations, sourceKey);
            var uuid = obj["uuid"]?.ToString();
            var identity = $"{sourceKey}|{uuid}";
            if (!string.IsNullOrWhiteSpace(uuid) && seen.Add(identity)) { var copy = obj.DeepClone().AsObject(); if (copy["sourcekey"] is null && sourceKey is not null) copy["sourcekey"] = sourceKey; output.Add(copy); }
        }
        Visit(node); return output;
    }

    private static List<JsonObject> ReadCsv(string path, bool coordinates)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Select a valid CSV file.", path);
        using var parser = new TextFieldParser(path, Encoding.UTF8) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(","); var headers = parser.ReadFields() ?? throw new InvalidDataException("The CSV is empty.");
        var lookup = headers.Select((name, index) => (name: name.Trim(), index)).ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        string[] required = coordinates ? ["sourcekey", "latitude", "longitude"] : ["sourcekey", "address"];
        if (required.Any(name => !lookup.ContainsKey(name))) throw new InvalidDataException($"CSV columns required: {string.Join(", ", required)}.");
        var records = new List<JsonObject>();
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields(); if (row is null || row.Length == 0) continue;
            string Cell(string name) => lookup[name] < row.Length ? row[lookup[name]].Trim() : "";
            var key = Cell("sourcekey"); if (string.IsNullOrWhiteSpace(key)) continue;
            if (coordinates)
            {
                if (!double.TryParse(Cell("latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) || !double.TryParse(Cell("longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180) continue;
                records.Add(new JsonObject { ["sourcekey"] = key, ["latitude"] = latitude, ["longitude"] = longitude });
            }
            else { var address = Cell("address"); if (!string.IsNullOrWhiteSpace(address)) records.Add(new JsonObject { ["sourcekey"] = key, ["text"] = address }); }
        }
        if (records.Count == 0) throw new InvalidDataException("The CSV contains no valid input rows."); return records;
    }

    private static async Task ZoomToResultsAsync(JsonArray results)
    {
        var points = results.OfType<JsonObject>().Select(item =>
        {
            var okX = double.TryParse(item["longitude"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x);
            var okY = double.TryParse(item["latitude"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var y);
            return okX && okY ? MapPointBuilderEx.CreateMapPoint(x, y, SpatialReferences.WGS84) : null;
        }).Where(point => point is not null).Cast<MapPoint>().ToArray();
        if (MapView.Active is null || points.Length == 0) return;
        var envelope = EnvelopeBuilderEx.CreateEnvelope(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y), SpatialReferences.WGS84);
        await MapView.Active.ZoomToAsync(envelope.Expand(1.25, 1.25, true), TimeSpan.FromMilliseconds(300));
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "fabric_locate.csv" }; if (dialog.ShowDialog(this) != true) return;
        var fields = _results.OfType<JsonObject>().SelectMany(item => item.Select(p => p.Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var csv = new StringBuilder().AppendLine(string.Join(',', fields.Select(Csv)));
        foreach (var item in _results.OfType<JsonObject>()) csv.AppendLine(string.Join(',', fields.Select(field => Csv(Value(item[field])))));
        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true)); _status.Text = $"Saved {dialog.FileName}";
    }

    private static DataTable BuildTable(JsonArray records) { var table = new DataTable(); var fields = records.OfType<JsonObject>().SelectMany(r => r.Select(p => p.Key)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); foreach (var f in fields) table.Columns.Add(f); foreach (var record in records.OfType<JsonObject>()) { var row = table.NewRow(); foreach (var f in fields) row[f] = Value(record[f]); table.Rows.Add(row); } return table; }
    private static string Value(JsonNode? node) => node is null ? "" : node.ToJsonString().Trim('"');
    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
