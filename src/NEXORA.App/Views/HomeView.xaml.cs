using System.Windows.Controls;

namespace NEXORA.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        LogBox.TextChanged += (_, _) => LogBox.ScrollToEnd();
    }
}
