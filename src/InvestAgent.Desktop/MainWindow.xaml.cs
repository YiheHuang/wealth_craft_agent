using InvestAgent.Desktop.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace InvestAgent.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void NewsLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            if (e.Uri is not null && !string.IsNullOrWhiteSpace(e.Uri.ToString()))
            {
                Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
                e.Handled = true;
            }
        }
        catch
        {
            // ignore
        }
    }
}
