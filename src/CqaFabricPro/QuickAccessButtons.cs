using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;

namespace CqaFabricPro;

internal sealed class IdentifyFabricButton : Button
{
    protected override async void OnClick()
    {
        try
        {
            var pane = OpenFabricPaneButton.FindPane();
            if (pane is null || !pane.CanIdentify)
            {
                MessageBox.Show("Log in and open a map before identifying Fabric features.", "CQA Fabric");
                return;
            }

            await pane.ToggleIdentifyAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Identify Fabric could not be changed:\n\n{ex.Message}", "CQA Fabric");
        }
    }

    protected override void OnUpdate()
    {
        var isActive = FrameworkApplication.CurrentTool == "CqaFabricPro_IdentifyTool";
        Caption = isActive ? "Deactivate Identify" : "Identify Fabric";
        Enabled = OpenFabricPaneButton.FindPane()?.CanIdentify == true;
    }
}

internal sealed class RetrieveFabricButton : Button
{
    protected override void OnClick()
    {
        var pane = OpenFabricPaneButton.FindPane();
        if (pane?.IsConnected != true) return;
        RetrieveFabricWindow.ShowFor(pane);
    }

    protected override void OnUpdate()
    {
        Enabled = OpenFabricPaneButton.FindPane()?.IsConnected == true;
    }
}

internal sealed class LocateFabricButton : Button
{
    protected override void OnClick()
    {
        var pane = OpenFabricPaneButton.FindPane();
        if (pane?.IsConnected == true) LocateFabricWindow.ShowFor(pane);
    }

    protected override void OnUpdate() => Enabled = OpenFabricPaneButton.FindPane()?.IsConnected == true;
}

internal sealed class LoginLogoutButton : Button
{
    protected override async void OnClick()
    {
        try
        {
            var pane = OpenFabricPaneButton.FindPane();
            if (pane?.IsConnected == true)
            {
                await pane.LogoutAsync();
                return;
            }

            if (pane is null)
            {
                MessageBox.Show("CQA Fabric authentication could not be opened.", "CQA Fabric");
                return;
            }

            LoginWindow.ShowFor(pane);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"CQA Fabric login/logout failed:\n\n{ex.Message}", "CQA Fabric");
        }
    }

    protected override void OnUpdate()
    {
        Caption = OpenFabricPaneButton.FindPane()?.IsConnected == true ? "Log Out" : "Log In";
        Enabled = true;
    }
}
