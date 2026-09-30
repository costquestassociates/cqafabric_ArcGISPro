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
    private bool _rememberApiKey;
    private string? _selectedVintage;
    private string? _selectedLayer;
    private string _identifyButtonText = "Identify Fabric";

    public FabricPaneViewModel()
    {
        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !string.IsNullOrWhiteSpace(ApiKey));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => IsConnected);
        AddLayerCommand = new AsyncRelayCommand(AddLayerAsync, CanAddLayer);
        IdentifyCommand = new AsyncRelayCommand(ToggleIdentifyAsync, CanAddLayer);
        var savedKey = WindowsCredentialStore.Load();
        if (!string.IsNullOrWhiteSpace(savedKey))
        {
            ApiKey = savedKey;
            RememberApiKey = true;
            _ = ConnectAsync();
        }
    }

    public string ApiKey { get => _apiKey; set { if (SetProperty(ref _apiKey, value)) ConnectCommand.RaiseCanExecuteChanged(); } }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool RememberApiKey { get => _rememberApiKey; set => SetProperty(ref _rememberApiKey, value); }
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
    public string AppVersion => $"Version {typeof(FabricPaneViewModel).Assembly.GetName().Version?.ToString(3)}";
    public string IdentifyButtonText { get => _identifyButtonText; private set => SetProperty(ref _identifyButtonText, value); }

    internal async Task ToggleIdentifyAsync()
    {
        if (ArcGIS.Desktop.Framework.FrameworkApplication.CurrentTool == "CqaFabricPro_IdentifyTool")
        {
            IdentifySession.Current = null;
            IdentifyFabricTool.CloseActivePopup();
            await ArcGIS.Desktop.Framework.FrameworkApplication.SetCurrentToolAsync("esri_mapping_exploreTool");
            IdentifyButtonText = "Identify Fabric";
            Status = "Identify inactive.";
            return;
        }

        IdentifySession.Current = new(ApiKey, SelectedVintage!, SelectedLayer!);
        await ArcGIS.Desktop.Framework.FrameworkApplication.SetCurrentToolAsync("CqaFabricPro_IdentifyTool");
        IdentifyButtonText = "Deactivate Identify";
        Status = "Identify active: click the map to identify the top visible Fabric layer.";
    }

    private bool CanAddLayer() => IsConnected && !string.IsNullOrWhiteSpace(SelectedVintage)
        && !string.IsNullOrWhiteSpace(SelectedLayer) && MapView.Active?.Map is not null;

    internal bool CanIdentify => CanAddLayer();

    private async Task AddLayerAsync()
    {
        var activeView = MapView.Active;
        var map = activeView?.Map;
        if (map is null)
        {
            Status = "Open or activate a map before adding Fabric tiles.";
            return;
        }

        var vintage = SelectedVintage!;
        var layerName = SelectedLayer!;
        var displayName = $"Fabric {vintage} - {layerName}";
        var originalCamera = activeView!.Camera;
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
                    AutoZoomOnEmptyMap = false,
                    MapMemberPosition = MapMemberPosition.AddToTop,
                    MinimumScale = layerName is "locations" or "retirements" ? 0 : 80000
                };
                LayerFactory.Instance.CreateLayer<VectorTileLayer>(parameters, map);
            });
            await activeView.ZoomToAsync(originalCamera, TimeSpan.Zero);
            _tileProxies.Add(proxy);
            proxy = null;
            Status = $"Added {displayName} to the active map.";
        }
        catch (Exception ex) { Status = $"Could not add the layer: {ex.Message}"; }
        finally { proxy?.Dispose(); }
    }

    internal async Task ConnectAsync()
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
            if (RememberApiKey) WindowsCredentialStore.Save(ApiKey);
            else WindowsCredentialStore.Delete();
            await RefreshAsync();
        }
        catch (CostQuestApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Status = "The API key was not accepted.";
        }
        catch (Exception ex) { Status = $"Connection failed: {ex.Message}"; }
    }

    internal async Task LogoutAsync()
    {
        try
        {
            WindowsCredentialStore.Delete();
            foreach (var proxy in _tileProxies) proxy.Dispose();
            _tileProxies.Clear();
            IdentifySession.Current = null;
            IdentifyFabricTool.CloseActivePopup();
            await ArcGIS.Desktop.Framework.FrameworkApplication.SetCurrentToolAsync("esri_mapping_exploreTool");
            IdentifyButtonText = "Identify Fabric";
            ApiKey = string.Empty;
            RememberApiKey = false;
            IsConnected = false;
            Vintages.Clear();
            Layers.Clear();
            SelectedVintage = null;
            SelectedLayer = null;
            Status = "Logged out. The saved API key was removed.";
        }
        catch (Exception ex) { Status = $"Logout failed: {ex.Message}"; }
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
