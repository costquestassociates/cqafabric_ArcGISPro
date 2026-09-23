using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;

namespace CqaFabricPro;

internal sealed class OpenFabricPaneButton : Button
{
    protected override void OnClick()
    {
        try
        {
            var pane = FrameworkApplication.DockPaneManager.Find(FabricPaneViewModel.DockPaneId);
            if (pane is null)
            {
                MessageBox.Show(
                    $"ArcGIS Pro could not create dock pane '{FabricPaneViewModel.DockPaneId}'. Reinstall the latest add-in package and restart ArcGIS Pro.",
                    "CQA Fabric");
                return;
            }

            pane.IsVisible = true;
            pane.Activate();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"CQA Fabric could not open:\n\n{ex}", "CQA Fabric");
        }
    }
}