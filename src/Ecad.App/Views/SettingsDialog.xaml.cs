using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ecad.App.Services;

namespace Ecad.App.Views;

/// <summary>App-level preferences (Settings > Preferences...), not project data — persisted via
/// AppSettingsStore, same as the auto-reopen-last-project state. Currently one setting (wire color);
/// laid out as a plain vertical list so more settings slot in as their own labeled row later.</summary>
public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();

        var currentHex = AppSettingsStore.Current.WireColorHex;
        var match = ColorList.Items.Cast<ListBoxItem>()
            .FirstOrDefault(item => string.Equals((string)item.Tag, currentHex, StringComparison.OrdinalIgnoreCase));
        ColorList.SelectedItem = match ?? ColorList.Items[0];
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (ColorList.SelectedItem is ListBoxItem selected)
        {
            var settings = AppSettingsStore.Current;
            settings.WireColorHex = (string)selected.Tag;
            AppSettingsStore.Save(settings);
        }

        DialogResult = true;
    }
}
