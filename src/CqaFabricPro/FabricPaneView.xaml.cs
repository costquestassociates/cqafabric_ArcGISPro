using System.Windows.Controls;

namespace CqaFabricPro;

public partial class FabricPaneView : UserControl
{
    public FabricPaneView()
    {
        InitializeComponent();
        FabricUi.Apply(this);
    }
}
