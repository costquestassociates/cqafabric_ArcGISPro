using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CqaFabricPro;

internal sealed class FabricPopupWindow : Window
{
    private readonly List<JsonObject> _records; private readonly Dictionary<string,string> _aliases; private readonly DataGrid _grid; private readonly ComboBox _selector;
    public FabricPopupWindow(IdentifyContext context,List<JsonObject> records,Dictionary<string,string> aliases)
    {
        _records=records;_aliases=aliases;Title="CQA Fabric";Width=520;Height=650;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Owner=Application.Current?.MainWindow;FabricUi.Apply(this);
        var root=new DockPanel{Margin=FabricUi.PageMargin};Content=root;
        var top=new StackPanel{Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock{Text="Identify Fabric",FontSize=14,FontWeight=FontWeights.Normal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,10)});
        var bar=new StackPanel{Orientation=Orientation.Horizontal};top.Children.Add(bar);
        _selector=new ComboBox{Width=300,ItemsSource=records.Select((r,i)=>$"Feature {i+1}: {r["location_id"] ?? r["uuid"]}").ToArray(),SelectedIndex=records.Count>0?0:-1};_selector.SelectionChanged+=(_,_)=>LoadRecord();bar.Children.Add(_selector);
        var copy=new Button{Content="Copy Details",Margin=new Thickness(8,0,0,0)};copy.Click+=(_,_)=>Clipboard.SetText(string.Join(Environment.NewLine,Rows.Select(r=>$"{r.Field}: {r.Value}")));bar.Children.Add(copy);
        _grid=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,HeadersVisibility=DataGridHeadersVisibility.Column,GridLinesVisibility=DataGridGridLinesVisibility.Horizontal};
        _grid.Columns.Add(new DataGridTextColumn{Header="Field",Binding=new Binding(nameof(Row.Field)),Width=new DataGridLength(2,DataGridLengthUnitType.Star)});
        _grid.Columns.Add(new DataGridTextColumn{Header="Value",Binding=new Binding(nameof(Row.Value)),Width=new DataGridLength(3,DataGridLengthUnitType.Star)});root.Children.Add(_grid);LoadRecord();
    }
    private ObservableCollection<Row> Rows {get;}=[];
    private void LoadRecord(){Rows.Clear();if(_selector.SelectedIndex<0)return;foreach(var p in _records[_selector.SelectedIndex])Rows.Add(new(_aliases.GetValueOrDefault(p.Key,p.Key),p.Value?.ToJsonString().Trim('"')??""));_grid.ItemsSource=Rows;}
    internal sealed record Row(string Field,string Value);
}
