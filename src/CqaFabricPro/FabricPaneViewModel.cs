using System.Collections.ObjectModel;
using System.Net;
using ArcGIS.Core.CIM;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using CqaFabricPro.Infrastructure;
using CqaFabricPro.Services;

namespace CqaFabricPro;

internal sealed class FabricPaneViewModel : DockPane
{
    internal const string DockPaneId = "CqaFabricPro_FabricPane";
    private readonly List<TileProxyService> _tileProxies = [];
    private string _apiKey = string.Empty;
    private string _status = "Enter your CostQuest API key to connect.";
    private bool _isConnected;
    private string? _selectedVintage;
    private string? _selectedLayer;

    public FabricPaneViewModel()
    {
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !string.IsNullOrWhiteSpace(ApiKey));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsConnected);
        AddLayerCommand = new AsyncRelayCommand(AddLayerAsync, CanAddLayer);
        IdentifyCommand = new AsyncRelayCommand(ActivateIdentifyAsync, CanAddLayer);
    }

    public string ApiKey { get => _apiKey; set { if (SetProperty(ref _apiKey, value)) ConnectCommand.RaiseCanExecuteChanged(); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (!SetProperty(ref _isConnected, value)) return;
            RefreshCommand.RaiseCanExecuteChanged();
            AddLayerCommand.RaiseCanExecuteChanged();
            IdentifyCommand.RaiseCanExecuteChanged();
        }
    }
    public ObservableCollection<string> Vintages { get; } = [];
    public ObservableCollection<string> Layers { get; } = [];
    public string? SelectedVintage { get => _selectedVintage; set { if (SetProperty(ref _selectedVintage, value)) { AddLayerCommand.RaiseCanExecuteChanged(); IdentifyCommand.RaiseCanExecuteChanged(); } } }
    public string? SelectedLayer { get => _selectedLayer; set { if (SetProperty(ref _selectedLayer, value)) { AddLayerCommand.RaiseCanExecuteChanged(); IdentifyCommand.RaiseCanExecuteChanged(); } } }
    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddLayerCommand { get; }
    public AsyncRelayCommand IdentifyCommand { get; }

    private async Task ActivateIdentifyAsync()
    {
        IdentifySession.Current = new(ApiKey, SelectedVintage!, SelectedLayer!);
        await ArcGIS.Desktop.Framework.FrameworkApplication.SetCurrentToolAsync("CqaFabricPro_IdentifyTool");
        Status = $"Identify active: click a {SelectedLayer} feature on the map.";
    }

    private bool CanAddLayer() => IsConnected && !string.IsNullOrWhiteSpace(SelectedVintage)
        && !string.IsNullOrWhiteSpace(SelectedLayer) && MapView.Active?.Map is not null;

    private async Task AddLayerAsync()
    {
        var map = MapView.Active?.Map;
        if (map is null)
        {
            Status = "Open or activate a map before adding Fabric tiles.";
            return;
        }

        var vintage = SelectedVintage!;
        var layerName = SelectedLayer!;
        var displayName = $"Fabric {vintage} - {layerName}";
        Status = $"Adding {displayName}...";
        TileProxyService? proxy = null;
        try
        {
            proxy = TileProxyService.Start(ApiKey, vintage, layerName);
            var styleUri = new Uri(proxy.StyleUrl);
            await QueuedTask.Run(() =>
            {
                var connection = new CIMVectorTileDataConnection { URI = styleUri.AbsoluteUri };
                var parameters = new LayerCreationParams(connection)
                {
                    Name = displayName,
                    IsVisible = true,
                    MapMemberPosition = MapMemberPosition.AddToTop,
                    MinimumScale = layerName is "locations" or "retirements" ? 0 : 80000
                };
                LayerFactory.Instance.CreateLayer<VectorTileLayer>(parameters, map);
            });
            _tileProxies.Add(proxy);
            proxy = null;
            Status = $"Added {displayName} to the active map.";
        }
        catch (Exception ex) { Status = $"Could not add the layer: {ex.Message}"; }
        finally { proxy?.Dispose(); }
    }

    private async Task ConnectAsync()
    {
        Status = "Validating API key...";
        IsConnected = false;
        try
        {
            using var client = new CostQuestApiClient(ApiKey);
            if (!await client.VerifyApiKeyAsync())
            {
                Status = "The API key was not accepted.";
                return;
            }
            IsConnected = true;
            await RefreshAsync();
        }
        catch (CostQuestApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Status = "The API key was not accepted.";
        }
        catch (Exception ex) { Status = $"Connection failed: {ex.Message}"; }
    }

    private async Task RefreshAsync()
    {
        Status = "Loading Fabric vintages and layers...";
        try
        {
            using var client = new CostQuestApiClient(ApiKey);
            var vintageTask = client.GetVintagesAsync();
            var layerTask = client.GetLayersAsync();
            await Task.WhenAll(vintageTask, layerTask);
            Replace(Vintages, vintageTask.Result);
            Replace(Layers, layerTask.Result);
            SelectedVintage ??= Vintages.FirstOrDefault();
            SelectedLayer ??= Layers.FirstOrDefault();
            Status = $"Connected. Loaded {Vintages.Count} vintages and {Layers.Count} layers.";
        }
        catch (Exception ex)
        {
            IsConnected = false;
            Status = $"Loading metadata failed: {ex.Message}";
        }
    }

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }
}
