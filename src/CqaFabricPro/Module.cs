using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;

namespace CqaFabricPro;

internal sealed class Module : ArcGIS.Desktop.Framework.Contracts.Module
{
    private static Module? _instance;

    public static Module Current => _instance ??= (Module)FrameworkApplication.FindModule("CqaFabricPro_Module");

    protected override bool CanUnload() => true;
}
