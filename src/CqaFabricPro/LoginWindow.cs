using System.Windows;
using System.Windows.Controls;

namespace CqaFabricPro;

internal sealed class LoginWindow : Window
{
    private readonly FabricPaneViewModel _viewModel;
    private readonly PasswordBox _apiKey = new();
    private readonly CheckBox _remember = new() { Content = "Remember API key securely" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _connect = new() { Content = "Log In", MinWidth = 88, IsDefault = true };

    private LoginWindow(FabricPaneViewModel viewModel)
    {
        _viewModel = viewModel;
        Title = "CQA Fabric";
        Width = 430;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;
        FabricUi.Apply(this);

        var root = new StackPanel { Margin = FabricUi.PageMargin };
        Content = root;
        root.Children.Add(new TextBlock { Text = "Log In", FontSize = 14, FontWeight = FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) });
        root.Children.Add(new TextBlock { Text = "CostQuest API key" });
        _apiKey.Margin = new Thickness(0, 5, 0, 10);
        _apiKey.Password = viewModel.ApiKey;
        root.Children.Add(_apiKey);
        _remember.IsChecked = viewModel.RememberApiKey;
        _remember.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(_remember);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _connect.Click += Connect_OnClick;
        buttons.Children.Add(_connect);
        var cancel = new Button { Content = "Cancel", MinWidth = 88, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        _status.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(_status);
        root.Children.Add(new TextBlock
        {
            Text = "Saved keys are encrypted by Windows Credential Manager and are not written to the ArcGIS project.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = FabricUi.MutedBrush,
            Margin = new Thickness(0, 12, 0, 0)
        });
    }

    internal static bool ShowFor(FabricPaneViewModel viewModel) => new LoginWindow(viewModel).ShowDialog() == true;

    private async void Connect_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_apiKey.Password))
        {
            _status.Text = "Enter an API key.";
            return;
        }

        _connect.IsEnabled = false;
        _status.Text = "Validating API key...";
        _viewModel.ApiKey = _apiKey.Password;
        _viewModel.RememberApiKey = _remember.IsChecked == true;
        await _viewModel.ConnectAsync();
        _status.Text = _viewModel.Status;
        _connect.IsEnabled = true;
        if (_viewModel.IsConnected) DialogResult = true;
    }
}
