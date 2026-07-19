using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;

namespace Ecad.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var message = "Your project data up to this point has already been saved — every change is written to disk immediately, so nothing is lost.\n\n"
            + e.Exception;
        MessageBox.Show(message, "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}

