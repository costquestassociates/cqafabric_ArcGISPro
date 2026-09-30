using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CqaFabricPro;

internal static class FabricUi
{
    internal static readonly Thickness PageMargin = new(16);
    internal static readonly Brush MutedBrush = SystemColors.GrayTextBrush;

    internal static void Apply(Window window)
    {
        window.FontFamily = new FontFamily("Segoe UI");
        window.FontSize = 12;
        window.Background = SystemColors.WindowBrush;
        AddStyles(window.Resources);
    }

    internal static void Apply(FrameworkElement view)
    {
        view.SetValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI"));
        view.SetValue(TextElement.FontSizeProperty, 12d);
        AddStyles(view.Resources);
    }

    private static void AddStyles(ResourceDictionary resources)
    {
        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 5, 12, 5)));
        button.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 28d));
        button.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        resources[typeof(Button)] = button;

        var input = new Style(typeof(TextBox));
        input.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 26d));
        input.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 3, 5, 3)));
        input.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        resources[typeof(TextBox)] = input;

        var password = new Style(typeof(PasswordBox));
        password.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 26d));
        password.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 3, 5, 3)));
        resources[typeof(PasswordBox)] = password;

        var combo = new Style(typeof(ComboBox));
        combo.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 26d));
        combo.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 2, 5, 2)));
        resources[typeof(ComboBox)] = combo;

        var check = new Style(typeof(CheckBox));
        check.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        resources[typeof(CheckBox)] = check;

        var grid = new Style(typeof(DataGrid));
        grid.Setters.Add(new Setter(DataGrid.HeadersVisibilityProperty, DataGridHeadersVisibility.Column));
        grid.Setters.Add(new Setter(DataGrid.GridLinesVisibilityProperty, DataGridGridLinesVisibility.Horizontal));
        grid.Setters.Add(new Setter(DataGrid.RowBackgroundProperty, SystemColors.WindowBrush));
        grid.Setters.Add(new Setter(DataGrid.AlternatingRowBackgroundProperty, SystemColors.ControlLightBrush));
        grid.Setters.Add(new Setter(DataGrid.AlternationCountProperty, 2));
        grid.Setters.Add(new Setter(Control.BorderBrushProperty, SystemColors.ActiveBorderBrush));
        grid.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        resources[typeof(DataGrid)] = grid;

        var list = new Style(typeof(ListBox));
        list.Setters.Add(new Setter(Control.BorderBrushProperty, SystemColors.ActiveBorderBrush));
        list.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        resources[typeof(ListBox)] = list;
    }
}
