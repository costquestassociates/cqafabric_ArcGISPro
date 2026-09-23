using System.Windows;
using System.Windows.Controls;

namespace CqaFabricPro;

public partial class FabricPaneView : UserControl
{
    public FabricPaneView() => InitializeComponent();

    private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is FabricPaneViewModel viewModel && sender is PasswordBox passwordBox)
            viewModel.ApiKey = passwordBox.Password;
    }
}
