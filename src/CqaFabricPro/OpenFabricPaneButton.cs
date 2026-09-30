using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;

namespace CqaFabricPro;

internal sealed class OpenFabricPaneButton : Button
{
    protected override void OnUpdate()
    {
        Enabled = FindPane()?.IsConnected == true;
    }

    protected override void OnClick()
    {
        try
        {
            ShowPane();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"CQA Fabric could not open:\n\n{ex}", "CQA Fabric");
        }
    }

    internal static FabricPaneViewModel? FindPane() =>
        FrameworkApplication.DockPaneManager.Find(FabricPaneViewModel.DockPaneId) as FabricPaneViewModel;

    internal static FabricPaneViewModel? ShowPane()
    {
            var pane = FindPane();
            if (pane is null)
            {
                MessageBox.Show(
                    $"ArcGIS Pro could not create dock pane '{FabricPaneViewModel.DockPaneId}'. Reinstall the latest add-in package and restart ArcGIS Pro.",
                    "CQA Fabric");
                return null;
            }

            pane.IsVisible = true;
            pane.Activate();
            return pane;
    }
}
