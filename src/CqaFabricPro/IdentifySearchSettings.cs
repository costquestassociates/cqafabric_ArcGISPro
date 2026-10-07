namespace CqaFabricPro;

internal enum IdentifyLayerScope
{
    TopmostLayer,
    VisibleLayers,
    SelectedInContents
}

internal static class IdentifySearchSettings
{
    public static IdentifyLayerScope LayerScope { get; set; } = IdentifyLayerScope.TopmostLayer;
}
